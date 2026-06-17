// TestNamingAnalyzer.cs — VFA004 (issue #625, partial).
//
// Pins the ambient `Method_Behaviour_Expected` test-naming convention
// that ~1500 + tests in tree follow but that's never been compile-time
// enforced. CA1707 (avoid-underscores) is globally suppressed in
// .editorconfig precisely because the convention here is the opposite
// — tests SHOULD carry underscores, and a new test missing one only
// surfaces in PR review.
//
// VFA004 fires when:
//   • The enclosing file path looks like a test file (ends in
//     `Tests.cs` — matches `*Tests.cs`, `*PropertyTests.cs`, etc).
//   • The method carries `[Fact]` or `[Theory]` (xunit).
//   • The method name contains no underscore.
//
// Severity: Warning (intentional). The repo's TreatWarningsAsErrors
// flag makes that load-bearing, so VFA004 ships in the
// WarningsNotAsErrors tolerance list — see Directory.Build.props.
// Rationale: a fast local-build signal is more valuable than a hard
// stop for a convention rule. If a single new test slips through, the
// PR reviewer still sees the analyzer note; the change isn't blocked.
//
// Not flagged:
//   • Helper methods inside test classes (no [Fact]/[Theory]).
//   • Test method names like `Foo_Bar` (have underscore — convention met).
//   • Non-test files (e.g. test infrastructure types in tests projects
//     whose filename doesn't end in `Tests.cs`).

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Voxelforge.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TestNamingAnalyzer : DiagnosticAnalyzer
    {
        private const string Category = "Voxelforge.Tests";
        private const string HelpLink =
            "https://github.com/poetac/voxelforge/blob/main/CONTRIBUTING.md#code-style";

        public static readonly DiagnosticDescriptor Vfa004 = new(
            id: "VFA004",
            title: "Test method without convention-required underscore",
            messageFormat:
                "Test method '{0}' should follow the `Method_Behaviour_Expected` naming convention — rename to include at least one underscore (current name is convention-violating)",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description:
                "Voxelforge tests follow the `Method_Behaviour_Expected` naming convention — " +
                "~1500 tests in tree carry the shape, and CA1707 is globally suppressed precisely " +
                "because the underscore-form is the convention here. VFA004 catches new test " +
                "methods that drift from this shape at compile time rather than PR review. " +
                "Severity: Warning so it surfaces locally without blocking a one-off rename. " +
                "If a particular test legitimately can't carry an underscore (e.g. a property-test " +
                "harness method that exposes a generated id), wrap it in " +
                "[SuppressMessage(\"Voxelforge.Tests\", \"VFA004\", Justification = \"…\")].",
            helpLinkUri: HelpLink);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Vfa004);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        }

        private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
        {
            // Scope check: only run on files whose name ends with "Tests.cs".
            // This catches the canonical project layout (*Tests/Foo.cs files
            // named FooTests.cs, BarTests.cs, etc.) without running over the
            // production source tree.
            string filePath = context.Node.SyntaxTree.FilePath ?? string.Empty;
            if (string.IsNullOrEmpty(filePath))
                return;
            int lastSep = filePath.LastIndexOfAny(new[] { '/', '\\' });
            string fileName = lastSep >= 0 ? filePath.Substring(lastSep + 1) : filePath;
            if (!fileName.EndsWith("Tests.cs", System.StringComparison.Ordinal))
                return;

            var method = (MethodDeclarationSyntax)context.Node;

            // Test-attribute filter: only fire on methods carrying [Fact] /
            // [Theory] (or the fully-qualified Xunit forms). Helper methods,
            // [SetUp]-style methods, and non-test methods are out of scope.
            if (!HasFactOrTheoryAttribute(method))
                return;

            string methodName = method.Identifier.ValueText;
            if (methodName.Contains('_'))
                return; // Convention met.

            context.ReportDiagnostic(Diagnostic.Create(
                Vfa004,
                method.Identifier.GetLocation(),
                methodName));
        }

        private static bool HasFactOrTheoryAttribute(MethodDeclarationSyntax method)
        {
            foreach (var attrList in method.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    string attrName = attr.Name.ToString();
                    // Strip generic-arg suffixes ("[Theory<T>]" not used in
                    // tree today, but defensive) and check the leaf name.
                    int lastDot = attrName.LastIndexOf('.');
                    if (lastDot >= 0)
                        attrName = attrName.Substring(lastDot + 1);
                    if (attrName == "Fact" || attrName == "FactAttribute" ||
                        attrName == "Theory" || attrName == "TheoryAttribute")
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
