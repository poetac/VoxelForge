// Vfd013AnalyzerTests — ADR-020 / issue #565.
//
// VFD013: Reading a static mutable field (one that isn't `static readonly`
// or `const`) inside a [Deterministic] taint closure OR inside an
// IObjective implementation introduces hidden state that breaks the
// strict-determinism contract. Fields annotated with [Pure] or
// [ThreadSafe] are allow-listed.
//
// Pure analyzer tests via Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit.
// Mirrors DeterministicAnalyzerTests.cs conventions (stub attribute /
// IObjective interface in side sources, hand-rolled user source as
// TestCode, line/column anchored to user source).

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Voxelforge.Analyzers;

namespace Voxelforge.Tests.Analyzers;

public sealed class Vfd013AnalyzerTests
{
    // Stub of the [Deterministic] marker attribute (same shape as
    // DeterministicAnalyzerTests.cs).
    private const string AttributeSource = """
        namespace Voxelforge.Optimization
        {
            [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
            public sealed class DeterministicAttribute : System.Attribute { }
        }
        """;

    // Stub of the IObjective interface — VFD013 needs the type symbol so
    // that IObjective-scope detection can fire on classes implementing it.
    private const string IObjectiveSource = """
        namespace Voxelforge.Optimization
        {
            public interface IObjective { }
        }
        """;

    private static async Task RunAsync(string testSource, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<DeterministicAnalyzer, DefaultVerifier>
        {
            TestCode = testSource,
        };
        test.TestState.Sources.Add(("DeterministicAttribute.cs", AttributeSource));
        test.TestState.Sources.Add(("IObjective.cs", IObjectiveSource));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static DiagnosticResult Vfd013(int line, int column, string fieldDisplay) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd013)
            .WithLocation(line, column)
            .WithArguments(fieldDisplay);

    // ── Positive — fires on static mutable field read in [Deterministic] ─

    [Fact]
    public async Task Vfd013_FiresOnStaticMutableFieldRead_InDeterministicScope()
    {
        // The exact RegenChamberOptimization._profileIndex shape that
        // evaded VFD001-012: a private static int field, mutated elsewhere,
        // read on the hot path of a [Deterministic] method.
        var src = """
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                private static int _x = 0;
                public int Read() => _x;
            }
            """;
        // Line 7: "    public int Read() => _x;" — `_x` is the expression body.
        await RunAsync(src, Vfd013(line: 7, column: 26, fieldDisplay: "C._x"));
    }

    // ── Negative — static readonly is immutable ───────────────────────────

    [Fact]
    public async Task Vfd013_DoesNotFire_OnStaticReadonlyFieldRead_InDeterministicScope()
    {
        // `static readonly` fields can be assigned only in the static
        // constructor / inline initializer, so two invocations of Read()
        // always see the same value. No determinism hazard.
        var src = """
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                private static readonly int X = 42;
                public int Read() => X;
            }
            """;
        await RunAsync(src);   // empty diagnostics
    }

    // ── Negative — const is compile-time inlined ──────────────────────────

    [Fact]
    public async Task Vfd013_DoesNotFire_OnConstFieldRead_InDeterministicScope()
    {
        // `const` fields are inlined by the C# compiler at the read site,
        // so the IL contains a literal load — there's no FieldReference
        // operation for the analyzer to observe in the first place. The
        // assertion here doubles as a guard: even if a future Roslyn
        // change started emitting FieldReference for const reads, the
        // descriptor's IsConst short-circuit would still skip it.
        var src = """
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                private const int X = 42;
                public int Read() => X;
            }
            """;
        await RunAsync(src);   // empty diagnostics
    }

    // ── Negative — outside [Deterministic] / IObjective scope ─────────────

    [Fact]
    public async Task Vfd013_DoesNotFire_Outside_DeterministicScope()
    {
        // Static mutable fields in a plain class are a legitimate pattern
        // (caches, counters, logger sinks). VFD013 must only fire when the
        // enclosing method is in the [Deterministic] taint closure OR
        // implements IObjective.
        var src = """
            public class C
            {
                private static int _x = 0;
                public int Read() => _x;
            }
            """;
        await RunAsync(src);   // empty diagnostics
    }

    // ── Positive — fires inside IObjective implementation ─────────────────

    [Fact]
    public async Task Vfd013_FiresOnStaticMutableFieldRead_InsideIObjective()
    {
        // Mirrors the VFD012 IObjective-scope pattern: no [Deterministic]
        // marker on the call chain, but the enclosing class implements
        // IObjective so the structural rule fires.
        var src = """
            using Voxelforge.Optimization;

            public sealed class ScoreWrapper : IObjective
            {
                private static int _bias = 0;
                public int Score() => _bias;
            }
            """;
        await RunAsync(src, Vfd013(line: 6, column: 27, fieldDisplay: "ScoreWrapper._bias"));
    }

    // ── Negative — [Pure] allow-list ──────────────────────────────────────

    [Fact]
    public async Task Vfd013_DoesNotFire_OnPureAttributedField_InDeterministicScope()
    {
        // The allow-list contract: fields decorated with [Pure] (or
        // [ThreadSafe]) document that the author has reasoned about
        // mutation safety / purity. The analyzer trusts the annotation
        // and skips the diagnostic.
        var src = """
            using Voxelforge.Optimization;

            namespace Voxelforge.Util
            {
                [System.AttributeUsage(System.AttributeTargets.Field)]
                public sealed class PureAttribute : System.Attribute { }
            }

            [Deterministic]
            public class C
            {
                [Voxelforge.Util.Pure]
                private static int _x = 0;
                public int Read() => _x;
            }
            """;
        await RunAsync(src);   // empty diagnostics — Pure-attributed field is allow-listed
    }
}
