// DeterministicAnalyzer.cs — ADR-020 / issue #209.
//
// Roslyn analyzer that fires inside the lexical / call-graph closure of
// any method or class marked with [Voxelforge.Optimization.Deterministic].
// A method M is "tainted" iff it has [Deterministic] OR some transitive
// caller has [Deterministic]. Tainted scope must be free of:
//
//   VFD001  DateTime.{Now,UtcNow,Today}, DateTimeOffset.{Now,UtcNow}
//   VFD002  new Random()              (zero-arg ctor only)
//   VFD003  Guid.NewGuid()
//   VFD004  Environment.TickCount     (Stopwatch.GetTimestamp() is allowed)
//
// Severity: Error (TreatWarningsAsErrors is on globally; analyzer must
// never false-positive).
//
// Limitations documented in ADR-020:
//   • Virtual / interface dispatch flags the declared method only.
//   • Reflection / dynamic dispatch is skipped (cannot resolve target).
//   • Field / property initializer expressions are skipped — only method,
//     constructor, accessor, and local-function bodies are analyzed.
//   VFD005  foreach over Dictionary / HashSet (non-deterministic order)
//   VFD006  Path.GetTempPath/GetTempFileName, Environment.GetEnvironmentVariable
//   VFD007  Thread.Sleep / Task.Delay (wall-clock delays)
//   VFD008  Process.Start / File.ReadAllText (out-of-process / filesystem
//           side effects that break determinism)
//   VFD009  string.GetHashCode() (default-overload — non-deterministic
//           across .NET process restarts due to hash randomization)
//   VFD011  Console.{Write, WriteLine, Error.Write, ...} (output-stream
//           side effects)
//   • Cross-assembly call closure is not analyzed; each project's analyzer
//     wiring is self-contained.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Voxelforge.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DeterministicAnalyzer : DiagnosticAnalyzer
    {
        public const string DeterministicAttributeFullName =
            "Voxelforge.Optimization.DeterministicAttribute";

        private const string Category = "Voxelforge.Determinism";
        private const string HelpLink =
            "https://github.com/poetac/voxelforge/blob/main/Voxelforge/docs/ADR/ADR-020-deterministic-analyzer.md";

        public static readonly DiagnosticDescriptor Vfd001 = new(
            id: "VFD001",
            title: "Wall-clock DateTime usage in [Deterministic] scope",
            messageFormat: "'{0}' reads wall-clock time and breaks the [Deterministic] contract",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Methods inside or transitively called from a [Deterministic] method must " +
                "not read wall-clock time. Use a deterministic time source or pass the value in.",
            helpLinkUri: HelpLink);

        public static readonly DiagnosticDescriptor Vfd002 = new(
            id: "VFD002",
            title: "Unseeded Random in [Deterministic] scope",
            messageFormat: "'new Random()' is non-deterministic; pass an explicit int seed",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Methods inside or transitively called from a [Deterministic] method must " +
                "not construct an unseeded Random. Pass an int seed.",
            helpLinkUri: HelpLink);

        public static readonly DiagnosticDescriptor Vfd003 = new(
            id: "VFD003",
            title: "Guid.NewGuid() in [Deterministic] scope",
            messageFormat: "'Guid.NewGuid()' is non-deterministic; pass a fixed Guid in or derive deterministically",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Methods inside or transitively called from a [Deterministic] method must " +
                "not call Guid.NewGuid().",
            helpLinkUri: HelpLink);

        public static readonly DiagnosticDescriptor Vfd004 = new(
            id: "VFD004",
            title: "Environment.TickCount in [Deterministic] scope",
            messageFormat: "'Environment.TickCount' is non-deterministic; use Stopwatch for elapsed-time instrumentation",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Environment.TickCount reads wall clock and wraps every ~25 days. " +
                "Stopwatch.GetTimestamp() is permitted by ADR-020 for elapsed-time " +
                "instrumentation in optimizer Run methods.",
            helpLinkUri: HelpLink);

        public static readonly DiagnosticDescriptor Vfd005 = new(
            id: "VFD005",
            title: "Dictionary or HashSet enumeration in [Deterministic] scope",
            messageFormat: "Enumerating '{0}' inside a [Deterministic] method is non-deterministic; use a sorted collection or sort before iterating",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "The enumeration order of Dictionary<,>, HashSet<>, ConcurrentDictionary<,>, " +
                "and ConcurrentBag<> is undefined. Inside a [Deterministic] method, use " +
                "SortedDictionary, SortedSet, or sort the keys before iterating.",
            helpLinkUri: HelpLink);

        public static readonly DiagnosticDescriptor Vfd006 = new(
            id: "VFD006",
            title: "Environment-dependent path in [Deterministic] scope",
            messageFormat: "'{0}' returns an environment-dependent value inside a [Deterministic] method",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Path.GetTempPath, Path.GetRandomFileName, Path.GetTempFileName, and " +
                "Environment.GetEnvironmentVariable return environment-dependent values " +
                "that break the [Deterministic] contract.",
            helpLinkUri: HelpLink);

        public static readonly DiagnosticDescriptor Vfd007 = new(
            id: "VFD007",
            title: "Wall-clock delay in [Deterministic] scope",
            messageFormat: "'{0}' introduces a wall-clock delay inside a [Deterministic] method",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Thread.Sleep and Task.Delay introduce wall-clock dependencies that " +
                "break determinism: a method whose execution time depends on Thread.Sleep " +
                "cannot be re-run deterministically across hardware / load conditions, " +
                "and tests that depend on the delay become flaky. Use a CancellationToken " +
                "wait or an explicit retry-counter loop instead.",
            helpLinkUri: HelpLink);

        public static readonly DiagnosticDescriptor Vfd008 = new(
            id: "VFD008",
            title: "Out-of-process / filesystem side effect in [Deterministic] scope",
            messageFormat: "'{0}' introduces an out-of-process or filesystem dependency inside a [Deterministic] method",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Process.Start, File.ReadAllText, File.WriteAllText, File.Exists, " +
                "Directory.GetFiles, and similar APIs introduce out-of-process / " +
                "filesystem-state dependencies that break determinism. The result of " +
                "a method that reads the filesystem depends on what files exist when " +
                "the method runs. Use voxelforge-eval subprocess oracle path " +
                "(explicitly OUTSIDE [Deterministic] surfaces) or a mockable IFileSource " +
                "abstraction inside.",
            helpLinkUri: HelpLink);

        public static readonly DiagnosticDescriptor Vfd009 = new(
            id: "VFD009",
            title: "string.GetHashCode() in [Deterministic] scope",
            messageFormat: "'string.GetHashCode()' is non-deterministic across process restarts due to .NET hash randomization",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "string.GetHashCode() returns different values across .NET process " +
                "restarts because the runtime randomizes the hash seed for security. " +
                "A [Deterministic] surface that uses string.GetHashCode() in any " +
                "non-trivial way produces different outputs across re-runs. Use " +
                "string.GetHashCode(StringComparison) (the explicit-comparison overload " +
                "is stable across processes) or compute a deterministic hash via " +
                "System.HashCode.Combine over individual characters.",
            helpLinkUri: HelpLink);

        public static readonly DiagnosticDescriptor Vfd011 = new(
            id: "VFD011",
            title: "Console output in [Deterministic] scope",
            messageFormat: "'{0}' writes to the console — output-stream side effect breaks determinism",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Console.Write, Console.WriteLine, Console.Error.Write, and related " +
                "console-output methods produce side effects that aren't captured by " +
                "the [Deterministic] surface's return value. Two runs may produce " +
                "identical return values but different console output (e.g. timing- " +
                "dependent debug prints), creating a hidden dependency that breaks " +
                "the strict-determinism contract. Use an ILogger abstraction (injected) " +
                "or accumulate output into a returned string for [Deterministic] code.",
            helpLinkUri: HelpLink);

        public static readonly DiagnosticDescriptor Vfd012 = new(
            id: "VFD012",
            title: "Wall-clock read inside IObjective implementation",
            messageFormat: "'{0}' reads a wall-clock time source inside an IObjective implementation — IObjective.Score must be deterministic across runs",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Any class implementing Voxelforge.Optimization.IObjective is part of the " +
                "optimizer evaluation surface. The same design vector must produce the same " +
                "Score / Violations on every run, regardless of when or where it's evaluated. " +
                "Reading DateTime.UtcNow, DateTimeOffset.Now, Environment.TickCount, or " +
                "Stopwatch.GetTimestamp() inside an IObjective method creates a hidden " +
                "wall-clock dependency that compiles cleanly through the IObjective interface " +
                "dispatch (so VFD001 / VFD004 don't fire). VFD012 fires structurally on the " +
                "IObjective scope without needing a [Deterministic] taint chain. The " +
                "intentional escape hatch is TeeObjective (which captures trace timestamps " +
                "by contract); it suppresses VFD012 on its Score method.",
            helpLinkUri: HelpLink);

        public static readonly DiagnosticDescriptor Vfd013 = new(
            id: "VFD013",
            title: "Static mutable-field read inside [Deterministic] / IObjective scope",
            messageFormat: "Reading static mutable field '{0}' inside a [Deterministic] or IObjective method introduces hidden state; pass the value as a parameter or mark the field `static readonly` / `const`",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "A static field that is neither `static readonly` nor `const` can be mutated " +
                "between two invocations of a [Deterministic] (or IObjective) method by " +
                "another thread / call path. Reading the field on the hot path therefore " +
                "introduces a hidden input that breaks the strict-determinism contract: " +
                "the same method, called with the same arguments, can return different " +
                "results in different runs depending on intervening writes to the static. " +
                "Either make the field immutable (`static readonly` / `const`), or pass " +
                "the value in as an explicit parameter. Fields annotated with [Pure] or " +
                "[ThreadSafe] are allow-listed.",
            helpLinkUri: HelpLink);

        public static readonly DiagnosticDescriptor Vfd014 = new(
            id: "VFD014",
            title: "Floating-point-accumulated time loop inside [Deterministic] scope",
            messageFormat: "for-loop with a double-typed index incremented by a non-zero double is non-deterministic across FP-rounding regimes; refactor to integer-tick form (derive t from the loop index, not by accumulation)",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "The pattern `for (double t = t0; t < tEnd; t += dt)` accumulates FP " +
                "roundoff across iterations, so the terminating tick count is a " +
                "function of host FMA / rounding behaviour (e.g., 10·0.1 = 0.9999... " +
                "but 20·0.05 = 1.0000007 — same target interval, different tick " +
                "counts). Inside a [Deterministic] method this breaks bit-equality " +
                "across runs. Refactor to integer-tick form: compute the tick count " +
                "as `(int)Math.Round((tEnd - t0) / dt) + 1` (closed [t0, tEnd]) or " +
                "`(int)Math.Round((tEnd - t0) / dt)` (half-open) and derive `t` from " +
                "the loop index. See ADR-042 / issue #547.",
            helpLinkUri: HelpLink);

        public static readonly DiagnosticDescriptor Vfd015 = new(
            id: "VFD015",
            title: "Sort comparer without tie-break inside [Deterministic] scope",
            messageFormat: "'{0}' inside a [Deterministic] / IObjective method uses a comparer that does not tie-break — ties produce non-deterministic ordering across runs; add a fallback CompareTo by a stable position-anchor",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Array.Sort, List<T>.Sort, and Enumerable.OrderBy use unstable " +
                "introsort under the hood. When two elements compare equal on the " +
                "primary key, the relative order they end up in is undefined and " +
                "can differ across runs at the same seed. Inside a [Deterministic] " +
                "scope this propagates non-determinism (post-sort recombination " +
                "weights, crowding-distance boundaries, etc.). Fix: tie-break by a " +
                "deterministic position-anchor. Canonical pattern: `(a, b) => { int " +
                "c = primary[a].CompareTo(primary[b]); return c != 0 ? c : " +
                "a.CompareTo(b); }`. Suppress with [SuppressMessage(\"Voxelforge." +
                "Determinism\", \"VFD015\")] plus an inline comment if you've " +
                "verified ties are impossible by construction.",
            helpLinkUri: HelpLink);

        // VFD016 — MathF.Clamp does not exist; use Math.Clamp.
        // This is a global (non-[Deterministic]-scoped) correctness rule:
        // System.MathF has no Clamp overload. The correct float-overloaded
        // alternative is Math.Clamp(float, float, float) (available since
        // .NET Core 2.0 / .NET Standard 2.1).
        public static readonly DiagnosticDescriptor Vfd016 = new(
            id: "VFD016",
            title: "MathF.Clamp does not exist; use Math.Clamp",
            messageFormat: "'MathF.Clamp' does not exist in .NET; use 'Math.Clamp(value, min, max)' instead",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "System.MathF does not expose a Clamp method. " +
                "Math.Clamp has float, double, decimal, and integer overloads. " +
                "Replace 'MathF.Clamp(value, min, max)' with 'Math.Clamp(value, min, max)'.",
            helpLinkUri: HelpLink);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Vfd001, Vfd002, Vfd003, Vfd004, Vfd005, Vfd006,
                Vfd007, Vfd008, Vfd009, Vfd011, Vfd012, Vfd013, Vfd014, Vfd015,
                Vfd016);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(OnCompilationStart);
            // VFD016 fires globally (not scoped to [Deterministic]) because
            // MathF.Clamp doesn't exist at all — it's a compile-time phantom.
            // Syntax-level check so it fires before (or alongside) CS0117.
            context.RegisterSyntaxNodeAction(
                CheckMathFClamp, SyntaxKind.InvocationExpression);
        }

        private const string IObjectiveFullName = "Voxelforge.Optimization.IObjective";

        // Per-compilation cache of the BCL sentinel types this analyzer
        // matches against (DateTime, Environment, Random, Stopwatch, ...).
        // Resolved once in OnCompilationStart and compared via
        // SymbolEqualityComparer.Default — avoids ~15 per-method
        // ToDisplayString() string allocations on every IDE keystroke
        // re-analysis (see #618).
        private sealed class Sentinels
        {
            public INamedTypeSymbol? IObjective;
            public INamedTypeSymbol? DateTime;
            public INamedTypeSymbol? DateTimeOffset;
            public INamedTypeSymbol? Environment;
            public INamedTypeSymbol? Console;
            public INamedTypeSymbol? Guid;
            public INamedTypeSymbol? Path;
            public INamedTypeSymbol? Thread;
            public INamedTypeSymbol? Task;
            public INamedTypeSymbol? Process;
            public INamedTypeSymbol? File;
            public INamedTypeSymbol? Directory;
            public INamedTypeSymbol? Stopwatch;
            public INamedTypeSymbol? Random;
            public INamedTypeSymbol? Array;
            public INamedTypeSymbol? Enumerable;
            // Open-generic collection sentinels — match via
            // OriginalDefinition equality so any closed instantiation
            // (Dictionary<int,string>, etc.) resolves to the same symbol.
            public INamedTypeSymbol? DictionaryT2;
            public INamedTypeSymbol? HashSetT1;
            public INamedTypeSymbol? ConcurrentDictionaryT2;
            public INamedTypeSymbol? ConcurrentBagT1;
            public INamedTypeSymbol? ListT1;
        }

        private static Sentinels BuildSentinels(Compilation compilation)
        {
            return new Sentinels
            {
                IObjective             = compilation.GetTypeByMetadataName(IObjectiveFullName),
                DateTime               = compilation.GetTypeByMetadataName("System.DateTime"),
                DateTimeOffset         = compilation.GetTypeByMetadataName("System.DateTimeOffset"),
                Environment            = compilation.GetTypeByMetadataName("System.Environment"),
                Console                = compilation.GetTypeByMetadataName("System.Console"),
                Guid                   = compilation.GetTypeByMetadataName("System.Guid"),
                Path                   = compilation.GetTypeByMetadataName("System.IO.Path"),
                Thread                 = compilation.GetTypeByMetadataName("System.Threading.Thread"),
                Task                   = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task"),
                Process                = compilation.GetTypeByMetadataName("System.Diagnostics.Process"),
                File                   = compilation.GetTypeByMetadataName("System.IO.File"),
                Directory              = compilation.GetTypeByMetadataName("System.IO.Directory"),
                Stopwatch              = compilation.GetTypeByMetadataName("System.Diagnostics.Stopwatch"),
                Random                 = compilation.GetTypeByMetadataName("System.Random"),
                Array                  = compilation.GetTypeByMetadataName("System.Array"),
                Enumerable             = compilation.GetTypeByMetadataName("System.Linq.Enumerable"),
                DictionaryT2           = compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2"),
                HashSetT1              = compilation.GetTypeByMetadataName("System.Collections.Generic.HashSet`1"),
                ConcurrentDictionaryT2 = compilation.GetTypeByMetadataName("System.Collections.Concurrent.ConcurrentDictionary`2"),
                ConcurrentBagT1        = compilation.GetTypeByMetadataName("System.Collections.Concurrent.ConcurrentBag`1"),
                ListT1                 = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1"),
            };
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext compilationContext)
        {
            var compilation = compilationContext.Compilation;
            var deterministicAttr = compilation.GetTypeByMetadataName(DeterministicAttributeFullName);
            var sentinels = BuildSentinels(compilation);
            if (deterministicAttr is null && sentinels.IObjective is null)
            {
                // Compilation references neither marker — nothing to do.
                return;
            }

            // Build the call graph + tainted set lazily on first operation analysis.
            // For compilations that have no [Deterministic] usage yet, the cost is
            // a single attribute-symbol lookup.
            var lazyTainted = new Lazy<HashSet<IMethodSymbol>>(
                () => deterministicAttr is null
                    ? new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)
                    : ComputeTaintedSet(compilation, deterministicAttr, compilationContext.CancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication);

            compilationContext.RegisterOperationAction(
                opCtx => AnalyzeOperation(opCtx, lazyTainted, sentinels),
                OperationKind.Invocation,
                OperationKind.ObjectCreation,
                OperationKind.PropertyReference,
                OperationKind.FieldReference,
                OperationKind.Loop);
        }

        // VFD012 scope check. Returns true when the enclosing user method is
        // defined inside a class that directly or transitively implements
        // Voxelforge.Optimization.IObjective. Static helper methods that share
        // the file with the IObjective implementation but live in a *different*
        // type don't count — VFD012 is scoped to the contract surface, not the
        // surrounding helpers.
        private static bool IsInIObjectiveImplementation(
            IMethodSymbol enclosing,
            Sentinels sentinels)
        {
            if (sentinels.IObjective is null) return false;
            var containing = enclosing.ContainingType;
            if (containing is null) return false;
            foreach (var iface in containing.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(iface, sentinels.IObjective))
                    return true;
            }
            return false;
        }

        // Builds the forward call graph (method M → set of methods M directly invokes
        // or constructs) and returns the BFS closure starting from every method
        // annotated with [Deterministic] (or whose containing class is annotated).
        //
        // Lambdas + local functions are intentionally NOT separate seeds: their
        // operations are walked as part of the enclosing user method's body
        // operation tree, so any prohibited call inside them shows up as an edge
        // from the enclosing method. The taint thus flows correctly through them.
        private static HashSet<IMethodSymbol> ComputeTaintedSet(
            Compilation compilation,
            INamedTypeSymbol deterministicAttr,
            CancellationToken ct)
        {
            var seeds = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var callGraph = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);

            foreach (var tree in compilation.SyntaxTrees)
            {
                ct.ThrowIfCancellationRequested();
                // Compilation.GetSemanticModel inside an analyzer is RS1030-flagged
                // because the returned model isn't thread-safe across concurrent
                // analyzer callbacks. Here we own the model on a single thread —
                // the call-graph build is wrapped in a Lazy<> with single-threaded
                // initialisation; no other code path observes this model object.
#pragma warning disable RS1030
                var model = compilation.GetSemanticModel(tree);
#pragma warning restore RS1030
                var root = tree.GetRoot(ct);

                foreach (var node in root.DescendantNodes())
                {
                    ct.ThrowIfCancellationRequested();

                    IMethodSymbol? symbol = node switch
                    {
                        MethodDeclarationSyntax m       => model.GetDeclaredSymbol(m, ct) as IMethodSymbol,
                        ConstructorDeclarationSyntax c  => model.GetDeclaredSymbol(c, ct) as IMethodSymbol,
                        AccessorDeclarationSyntax a     => model.GetDeclaredSymbol(a, ct) as IMethodSymbol,
                        LocalFunctionStatementSyntax lf => model.GetDeclaredSymbol(lf, ct) as IMethodSymbol,
                        _ => null,
                    };
                    if (symbol is null) continue;

                    if (HasAttribute(symbol, deterministicAttr) ||
                        HasAttribute(symbol.ContainingType, deterministicAttr))
                    {
                        seeds.Add(symbol);
                    }

                    var bodyOp = model.GetOperation(node, ct);
                    if (bodyOp is null) continue;

                    if (!callGraph.TryGetValue(symbol, out var calls))
                    {
                        calls = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                        callGraph[symbol] = calls;
                    }

                    foreach (var op in bodyOp.DescendantsAndSelf())
                    {
                        IMethodSymbol? target = op switch
                        {
                            IInvocationOperation inv     => inv.TargetMethod,
                            IObjectCreationOperation oc  => oc.Constructor,
                            _ => null,
                        };
                        if (target is null) continue;

                        // Skip lambda / anonymous-function "calls" — their bodies are
                        // already walked inside this same DescendantsAndSelf pass.
                        // Local functions DO get edges (they're real distinct symbols
                        // and tainting them is meaningful for separate analysis).
                        if (target.MethodKind == MethodKind.AnonymousFunction ||
                            target.MethodKind == MethodKind.LambdaMethod)
                        {
                            continue;
                        }

                        calls.Add(target);
                    }
                }
            }

            // Forward BFS over the call graph.
            var tainted = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var queue = new Queue<IMethodSymbol>(seeds);
            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var m = queue.Dequeue();
                if (!tainted.Add(m)) continue;
                if (callGraph.TryGetValue(m, out var callees))
                {
                    foreach (var c in callees) queue.Enqueue(c);
                }
            }

            return tainted;
        }

        private static bool HasAttribute(ISymbol? symbol, INamedTypeSymbol attr)
        {
            if (symbol is null) return false;
            foreach (var a in symbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, attr)) return true;
            }
            return false;
        }

        // Walks up through lambdas and local-function symbols to the topmost
        // user-declared method (the one that lives in the call graph). Returns
        // null if the operation is in a context that isn't inside a method
        // (e.g. a field initializer at the type-symbol level).
        private static IMethodSymbol? FindEnclosingUserMethod(ISymbol? symbol)
        {
            while (symbol is IMethodSymbol m)
            {
                if (m.MethodKind == MethodKind.AnonymousFunction ||
                    m.MethodKind == MethodKind.LambdaMethod ||
                    m.MethodKind == MethodKind.LocalFunction)
                {
                    symbol = m.ContainingSymbol;
                    continue;
                }
                return m;
            }
            return null;
        }

        private static void AnalyzeOperation(
            OperationAnalysisContext context,
            Lazy<HashSet<IMethodSymbol>> lazyTainted,
            Sentinels sentinels)
        {
            var enclosing = FindEnclosingUserMethod(context.ContainingSymbol);
            if (enclosing is null) return;

            var tainted = lazyTainted.Value;
            bool isTainted = tainted.Count > 0 && tainted.Contains(enclosing);
            bool isInIObjective = IsInIObjectiveImplementation(enclosing, sentinels);

            if (!isTainted && !isInIObjective) return;

            // VFD013 — static mutable-field reads. The same diagnostic fires
            // for either [Deterministic] taint OR IObjective scope (it's a
            // structural rule, not a wall-clock-source rule), so route it
            // before the scope split.
            if (context.Operation is IFieldReferenceOperation fieldRef)
            {
                CheckStaticMutableFieldReference(fieldRef, context, isTainted, isInIObjective);
                return;
            }

            // VFD014 — FP-accumulator time-loop pattern
            // `for (double t = ...; t < ...; t += <double>)`. Mirrors the
            // Vfd013 routing: same diagnostic for [Deterministic] taint OR
            // IObjective scope, so dispatch before the scope-split switch.
            if (context.Operation is IForLoopOperation forLoop)
            {
                CheckFpTimeLoop(forLoop, context, isTainted, isInIObjective);
                return;
            }

            // VFD015 — unstable sort with a comparer lambda that lacks a
            // tie-break. Fires for either [Deterministic] taint OR IObjective
            // scope; the operation is IInvocationOperation, shared with the
            // wall-clock-source rules, so dispatch it inline here in addition
            // to (not in place of) the per-scope switch below.
            if (context.Operation is IInvocationOperation sortInv)
            {
                CheckUnstableSort(sortInv, context, isTainted, isInIObjective, sentinels);
            }

            if (isTainted)
            {
                switch (context.Operation)
                {
                    case IPropertyReferenceOperation prop:
                        CheckPropertyReference(prop, context, sentinels);
                        break;
                    case IInvocationOperation inv:
                        CheckInvocation(inv, context, sentinels);
                        break;
                    case IObjectCreationOperation oc:
                        CheckObjectCreation(oc, context, sentinels);
                        break;
                    case IForEachLoopOperation forEach:
                        CheckForEachLoop(forEach, context, sentinels);
                        break;
                }
            }
            // VFD012 only fires when the operation is NOT already under
            // [Deterministic] taint — that path already produces VFD001 /
            // VFD004 on the same site, so double-reporting just adds noise.
            else if (isInIObjective)
            {
                switch (context.Operation)
                {
                    case IPropertyReferenceOperation prop:
                        CheckIObjectivePropertyReference(prop, context, sentinels);
                        break;
                    case IInvocationOperation inv:
                        CheckIObjectiveInvocation(inv, context, sentinels);
                        break;
                }
            }
        }

        // VFD013 — static mutable-field reads inside [Deterministic] or
        // IObjective scope. A static field that isn't `static readonly` /
        // `const` can be mutated between two invocations of a deterministic
        // method by another thread / call path, so reading it on the hot
        // path introduces hidden state. Fields annotated with [Pure] or
        // [ThreadSafe] are allow-listed.
        private static void CheckStaticMutableFieldReference(
            IFieldReferenceOperation op,
            OperationAnalysisContext context,
            bool isTainted,
            bool isInIObjective)
        {
            if (!isTainted && !isInIObjective) return;

            var field = op.Field;
            if (field is null) return;

            // Only flag static fields that are mutable.
            if (!field.IsStatic) return;
            if (field.IsReadOnly) return;
            if (field.IsConst) return;

            // Allow-list common safe attributes.
            foreach (var attr in field.GetAttributes())
            {
                var name = attr.AttributeClass?.Name;
                if (name is null) continue;
                if (name == "PureAttribute" || name == "ThreadSafeAttribute") return;
            }

            var diag = Diagnostic.Create(Vfd013, op.Syntax.GetLocation(), field.ToDisplayString());
            context.ReportDiagnostic(diag);
        }

        // VFD014 — floating-point-accumulator time-loop pattern.
        //
        // Fires inside [Deterministic] OR IObjective scope on the shape
        //
        //     for (double t = t0; t < tEnd; t += dt) { ... }
        //
        // which accumulates FP roundoff across iterations: the terminating
        // tick count becomes a function of host FMA / rounding behaviour
        // (10·0.1 = 0.9999... vs. 20·0.05 = 1.0000007 — same target interval,
        // different tick counts). The refactor target is integer-tick form,
        // where the loop index is `int` and `t` is computed as `t0 + i*dt`.
        //
        // Detection pattern (kept conservative to avoid false-positives):
        //   1. Before contains a variable declarator of type `double`.
        //   2. AtLoopBottom contains a CompoundAssignment += on that local
        //      whose right-hand side is typed `double`.
        // We do NOT inspect the Condition shape — any condition that
        // terminates the loop is suspect regardless of comparison operator.
        private static void CheckFpTimeLoop(
            IForLoopOperation forLoop,
            OperationAnalysisContext context,
            bool isTainted,
            bool isInIObjective)
        {
            if (!isTainted && !isInIObjective) return;

            // Find a loop-control local of type double declared in Before.
            ILocalSymbol? loopVar = null;
            foreach (var beforeOp in forLoop.Before)
            {
                if (beforeOp is IVariableDeclarationGroupOperation grp)
                {
                    foreach (var decl in grp.Declarations)
                    {
                        foreach (var declarator in decl.Declarators)
                        {
                            if (declarator.Symbol.Type.SpecialType == SpecialType.System_Double)
                            {
                                loopVar = declarator.Symbol;
                                break;
                            }
                        }
                        if (loopVar is not null) break;
                    }
                }
                if (loopVar is not null) break;
            }
            if (loopVar is null) return;

            // Confirm the step (AtLoopBottom): expression-statement wrapping
            // a `+=` compound assignment on the loop var with a double RHS.
            bool stepMatches = false;
            foreach (var bottomOp in forLoop.AtLoopBottom)
            {
                if (bottomOp is IExpressionStatementOperation exprStmt &&
                    exprStmt.Operation is ICompoundAssignmentOperation cao &&
                    cao.OperatorKind == BinaryOperatorKind.Add &&
                    cao.Target is ILocalReferenceOperation lro &&
                    SymbolEqualityComparer.Default.Equals(lro.Local, loopVar) &&
                    cao.Value.Type?.SpecialType == SpecialType.System_Double)
                {
                    stepMatches = true;
                    break;
                }
            }
            if (!stepMatches) return;

            context.ReportDiagnostic(Diagnostic.Create(Vfd014, forLoop.Syntax.GetLocation()));
        }

        // VFD015 — unstable sort with a comparer / keySelector lambda that
        // lacks a tie-break, called inside [Deterministic] or IObjective
        // scope.
        //
        // Detection is deliberately permissive: a lambda body that contains
        // exactly one CompareTo invocation and no conditional / ternary /
        // if-statement is flagged. The canonical fix is
        //
        //     (a, b) => { int c = primary[a].CompareTo(primary[b]);
        //                 return c != 0 ? c : a.CompareTo(b); }
        //
        // which introduces an IConditionalOperation, suppressing the rule.
        // False positives can also be silenced with
        // [SuppressMessage("Voxelforge.Determinism", "VFD015")] plus an
        // inline comment explaining why ties are impossible by construction.
        private static void CheckUnstableSort(
            IInvocationOperation invocation,
            OperationAnalysisContext context,
            bool isTainted,
            bool isInIObjective,
            Sentinels sentinels)
        {
            if (!isTainted && !isInIObjective) return;

            var method = invocation.TargetMethod;
            var owner = method.ContainingType;
            var methodName = method.Name;

            // Match Array.Sort, List<T>.Sort, Enumerable.OrderBy /
            // OrderByDescending. List<T>.Sort lives on a closed generic
            // owner type, so compare via the open-generic OriginalDefinition.
            bool isSort =
                (SymbolEqualityComparer.Default.Equals(owner, sentinels.Array) && methodName == "Sort") ||
                (sentinels.ListT1 is not null &&
                 SymbolEqualityComparer.Default.Equals(owner.OriginalDefinition, sentinels.ListT1) &&
                 methodName == "Sort") ||
                (SymbolEqualityComparer.Default.Equals(owner, sentinels.Enumerable) &&
                 (methodName == "OrderBy" || methodName == "OrderByDescending"));
            if (!isSort) return;

            // Look for a comparer / keySelector arg that is a lambda.
            // The lambda may be wrapped in an IDelegateCreationOperation
            // (the usual shape — explicit delegate conversion) or appear
            // bare on some code paths.
            foreach (var arg in invocation.Arguments)
            {
                IAnonymousFunctionOperation? lambda = null;
                if (arg.Value is IDelegateCreationOperation dco &&
                    dco.Target is IAnonymousFunctionOperation l1)
                    lambda = l1;
                else if (arg.Value is IAnonymousFunctionOperation l2)
                    lambda = l2;
                if (lambda is null) continue;

                // Walk the lambda body. If we find exactly one CompareTo
                // invocation and no IConditionalOperation (no ternary,
                // no `if` statement), the comparer is missing a tie-break.
                int compareToCount = 0;
                bool hasBranchOrFallback = false;
                foreach (var desc in lambda.Body.Descendants())
                {
                    if (desc is IInvocationOperation inv &&
                        inv.TargetMethod.Name == "CompareTo")
                    {
                        compareToCount++;
                    }
                    if (desc is IConditionalOperation)
                    {
                        hasBranchOrFallback = true;
                    }
                }
                if (compareToCount == 1 && !hasBranchOrFallback)
                {
                    // Materialise the display string only on the rare
                    // diagnostic-fire path; the hot path stays alloc-free.
                    context.ReportDiagnostic(Diagnostic.Create(Vfd015,
                        invocation.Syntax.GetLocation(),
                        $"{owner.ToDisplayString()}.{methodName}"));
                    break;
                }
            }
        }

        // VFD012 — time-source property reads inside IObjective scope.
        // Covers: DateTime.Now / UtcNow / Today, DateTimeOffset.Now / UtcNow,
        // Environment.TickCount / TickCount64. Mirrors the matchers in
        // CheckPropertyReference (VFD001 + VFD004) but the diagnostic is
        // VFD012-tagged so the suppression key matches the IObjective-scope
        // contract.
        private static void CheckIObjectivePropertyReference(
            IPropertyReferenceOperation op,
            OperationAnalysisContext context,
            Sentinels sentinels)
        {
            var prop = op.Property;
            if (!prop.IsStatic) return;

            var owner = prop.ContainingType;
            if (owner is null) return;

            var name = prop.Name;

            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.DateTime) &&
                (name == "Now" || name == "UtcNow" || name == "Today"))
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd012, op.Syntax.GetLocation(),
                    $"DateTime.{name}"));
                return;
            }
            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.DateTimeOffset) &&
                (name == "Now" || name == "UtcNow"))
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd012, op.Syntax.GetLocation(),
                    $"DateTimeOffset.{name}"));
                return;
            }
            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.Environment) &&
                (name == "TickCount" || name == "TickCount64"))
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd012, op.Syntax.GetLocation(),
                    $"Environment.{name}"));
                return;
            }
        }

        // VFD012 — time-source invocations inside IObjective scope. Stopwatch
        // is the main entry here. ADR-020 exempts Stopwatch.GetTimestamp() at
        // the project level (Deterministic taint), but IObjective scope is
        // stricter: a wall-clock read inside Score is always a determinism
        // hazard. Covers Stopwatch.GetTimestamp() (static) and Stopwatch.Start
        // / .StartNew / .Restart / .GetElapsedTime which all internally read
        // the timestamp.
        private static void CheckIObjectiveInvocation(
            IInvocationOperation op,
            OperationAnalysisContext context,
            Sentinels sentinels)
        {
            var m = op.TargetMethod;
            var owner = m.ContainingType;
            if (owner is null) return;

            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.Stopwatch) &&
                (m.Name == "GetTimestamp" || m.Name == "StartNew" ||
                 m.Name == "GetElapsedTime"))
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd012, op.Syntax.GetLocation(),
                    $"Stopwatch.{m.Name}"));
                return;
            }
        }

        private static void CheckPropertyReference(
            IPropertyReferenceOperation op,
            OperationAnalysisContext context,
            Sentinels sentinels)
        {
            var prop = op.Property;
            if (!prop.IsStatic) return;

            var owner = prop.ContainingType;
            if (owner is null) return;

            var name = prop.Name;

            // VFD001 — DateTime / DateTimeOffset wall-clock readers.
            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.DateTime) &&
                (name == "Now" || name == "UtcNow" || name == "Today"))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Vfd001, op.Syntax.GetLocation(), $"DateTime.{name}"));
                return;
            }
            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.DateTimeOffset) &&
                (name == "Now" || name == "UtcNow"))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Vfd001, op.Syntax.GetLocation(), $"DateTimeOffset.{name}"));
                return;
            }

            // VFD004 — Environment.TickCount.
            // Stopwatch.GetTimestamp() is *intentionally* not flagged — see ADR-020.
            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.Environment) && name == "TickCount")
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd004, op.Syntax.GetLocation()));
                return;
            }
        }

        private static void CheckInvocation(
            IInvocationOperation op,
            OperationAnalysisContext context,
            Sentinels sentinels)
        {
            var m = op.TargetMethod;
            var owner = m.ContainingType;
            if (owner is null) return;

            // VFD009 — string.GetHashCode() (instance method on string, no
            // args). The runtime randomizes the hash seed per process so the
            // result varies across re-runs. Allow GetHashCode(StringComparison)
            // and GetHashCode(ReadOnlySpan<char>, StringComparison) — those
            // are explicitly stable. Instance-method case; the early
            // `!m.IsStatic return` below intentionally comes after.
            if (m.Name == "GetHashCode" &&
                m.Parameters.Length == 0 &&
                owner.SpecialType == SpecialType.System_String)
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd009, op.Syntax.GetLocation()));
                return;
            }

            // VFD011 — Console output / input methods. Two shapes:
            //
            //   (a) Static methods on System.Console
            //       — Console.Write, Console.WriteLine, Console.ReadLine, etc.
            //
            //   (b) Instance methods on the TextWriter / TextReader returned
            //       by the static properties Console.Out, Console.Error,
            //       Console.In — e.g. Console.Error.WriteLine("..."). Here
            //       TargetMethod.ContainingType is System.IO.TextWriter, not
            //       System.Console, so the owner-equality check misses it.
            //       Walk the invocation receiver instead: if op.Instance is
            //       a static property reference on System.Console named Out,
            //       Error, or In, the call is a Console-stream side effect.
            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.Console) &&
                (m.Name == "Write" || m.Name == "WriteLine" ||
                 m.Name == "ReadLine" || m.Name == "ReadKey" || m.Name == "Read"))
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd011, op.Syntax.GetLocation(),
                    $"Console.{m.Name}"));
                return;
            }
            if (op.Instance is IPropertyReferenceOperation consoleStreamRef &&
                consoleStreamRef.Property.IsStatic &&
                consoleStreamRef.Property.ContainingType is { } streamOwner &&
                SymbolEqualityComparer.Default.Equals(streamOwner, sentinels.Console) &&
                (consoleStreamRef.Property.Name == "Out" ||
                 consoleStreamRef.Property.Name == "Error" ||
                 consoleStreamRef.Property.Name == "In") &&
                IsConsoleStreamMethod(m.Name))
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd011, op.Syntax.GetLocation(),
                    $"Console.{consoleStreamRef.Property.Name}.{m.Name}"));
                return;
            }

            // From here, only static methods are tracked.
            if (!m.IsStatic) return;

            // VFD003 — Guid.NewGuid() (zero-arg static method on System.Guid).
            if (m.Name == "NewGuid" &&
                m.Parameters.Length == 0 &&
                SymbolEqualityComparer.Default.Equals(owner, sentinels.Guid))
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd003, op.Syntax.GetLocation()));
                return;
            }

            // VFD006 — environment-dependent Path / Environment methods.
            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.Path) &&
                (m.Name == "GetTempPath" || m.Name == "GetRandomFileName" || m.Name == "GetTempFileName"))
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd006, op.Syntax.GetLocation(),
                    $"Path.{m.Name}"));
                return;
            }
            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.Environment) &&
                m.Name == "GetEnvironmentVariable")
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd006, op.Syntax.GetLocation(),
                    "Environment.GetEnvironmentVariable"));
                return;
            }

            // VFD007 — Thread.Sleep + Task.Delay (wall-clock dependencies).
            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.Thread) && m.Name == "Sleep")
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd007, op.Syntax.GetLocation(),
                    "Thread.Sleep"));
                return;
            }
            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.Task) && m.Name == "Delay")
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd007, op.Syntax.GetLocation(),
                    "Task.Delay"));
                return;
            }

            // VFD008 — Process.Start + System.IO.File / Directory side effects.
            // Static methods on System.Diagnostics.Process, System.IO.File,
            // and System.IO.Directory routinely break determinism in
            // [Deterministic] scopes.
            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.Process) && m.Name == "Start")
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd008, op.Syntax.GetLocation(),
                    "Process.Start"));
                return;
            }
            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.File) &&
                (m.Name == "ReadAllText" || m.Name == "ReadAllBytes" ||
                 m.Name == "ReadAllLines" || m.Name == "WriteAllText" ||
                 m.Name == "WriteAllBytes" || m.Name == "WriteAllLines" ||
                 m.Name == "AppendAllText" || m.Name == "AppendAllLines" ||
                 m.Name == "AppendAllBytes" ||
                 m.Name == "ReadAllTextAsync" || m.Name == "ReadAllBytesAsync" ||
                 m.Name == "ReadAllLinesAsync" || m.Name == "WriteAllTextAsync" ||
                 m.Name == "WriteAllBytesAsync" || m.Name == "WriteAllLinesAsync" ||
                 m.Name == "AppendAllTextAsync" || m.Name == "AppendAllLinesAsync" ||
                 m.Name == "Exists" || m.Name == "Delete" || m.Name == "Copy" ||
                 m.Name == "Move"))
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd008, op.Syntax.GetLocation(),
                    $"File.{m.Name}"));
                return;
            }
            if (SymbolEqualityComparer.Default.Equals(owner, sentinels.Directory) &&
                (m.Name == "GetFiles" || m.Name == "GetDirectories" ||
                 m.Name == "EnumerateFiles" || m.Name == "EnumerateDirectories" ||
                 m.Name == "Exists" || m.Name == "CreateDirectory" ||
                 m.Name == "Delete"))
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd008, op.Syntax.GetLocation(),
                    $"Directory.{m.Name}"));
                return;
            }
        }

        // Names of TextWriter / TextReader methods that read or write the
        // backing Console stream. Used by VFD011 instance-call detection
        // (Console.Out / Console.Error / Console.In receivers). Async
        // variants count: their continuation completes a Console-stream
        // side effect whose ordering is wall-clock dependent.
        private static bool IsConsoleStreamMethod(string name) =>
            name == "Write" ||
            name == "WriteLine" ||
            name == "WriteAsync" ||
            name == "WriteLineAsync" ||
            name == "Read" ||
            name == "ReadLine" ||
            name == "ReadToEnd" ||
            name == "ReadAsync" ||
            name == "ReadLineAsync" ||
            name == "ReadToEndAsync" ||
            name == "ReadBlock" ||
            name == "ReadBlockAsync" ||
            name == "Peek";

        private static void CheckForEachLoop(
            IForEachLoopOperation op,
            OperationAnalysisContext context,
            Sentinels sentinels)
        {
            var collectionType = op.Collection.Type;
            if (collectionType is null) return;

            var od = collectionType.OriginalDefinition;
            bool isDangerous =
                SymbolEqualityComparer.Default.Equals(od, sentinels.DictionaryT2) ||
                SymbolEqualityComparer.Default.Equals(od, sentinels.HashSetT1) ||
                SymbolEqualityComparer.Default.Equals(od, sentinels.ConcurrentDictionaryT2) ||
                SymbolEqualityComparer.Default.Equals(od, sentinels.ConcurrentBagT1);

            if (!isDangerous) return;
            context.ReportDiagnostic(Diagnostic.Create(Vfd005, op.Syntax.GetLocation(),
                collectionType.Name));
        }

        private static void CheckObjectCreation(
            IObjectCreationOperation op,
            OperationAnalysisContext context,
            Sentinels sentinels)
        {
            var ctor = op.Constructor;
            if (ctor is null) return;

            var owner = ctor.ContainingType;
            if (owner is null) return;

            // VFD002 — new Random() with no arguments.
            if (ctor.Parameters.Length == 0 &&
                SymbolEqualityComparer.Default.Equals(owner, sentinels.Random))
            {
                context.ReportDiagnostic(Diagnostic.Create(Vfd002, op.Syntax.GetLocation()));
                return;
            }
        }

        // VFD016 — MathF.Clamp (phantom method; does not exist in .NET).
        // Syntax-only check: matches any invocation of the form
        // `MathF.Clamp(...)` regardless of [Deterministic] scope or whether
        // the code compiles.  The fix is always Math.Clamp(value, min, max).
        private static void CheckMathFClamp(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return;
            if (memberAccess.Name.Identifier.Text != "Clamp")
                return;
            if (memberAccess.Expression is not IdentifierNameSyntax typeName)
                return;
            if (typeName.Identifier.Text != "MathF")
                return;
            context.ReportDiagnostic(Diagnostic.Create(Vfd016, invocation.GetLocation()));
        }
    }
}
