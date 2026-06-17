// PragmaSuppressionAnalyzer.cs — VFA005 (issue #626).
//
// Bans the ambient form of VFD012 suppression:
//
//   #pragma warning disable VFD012   ← VFA005 fires here
//   var t = DateTime.UtcNow;
//   #pragma warning restore VFD012   ← VFA005 also fires here
//
// Why ban it. VFD012 polices a determinism contract on IObjective —
// any wall-clock read inside an IObjective method silently violates
// the same-design-vector → same-Score requirement. The contract is
// load-bearing; suppressions must be exceptional and reviewable.
//
// `#pragma warning disable VFD012` is *invisible* to a reviewer
// browsing the symbol in the IDE or the PR diff hover — it lives in
// preprocessor trivia. The canonical escape hatch is the structural
// attribute form:
//
//   [SuppressMessage("Voxelforge.Determinism", "VFD012",
//        Justification = "TeeObjective captures wall-clock by contract")]
//
// which attaches to the symbol, surfaces in IDE hover + symbol search,
// and forces the author to write a Justification. The existing
// `Voxelforge.Core/Optimization/ObjectiveWrappers.cs` TeeObjective
// uses this form; no other VFD012 suppression exists in tree at the
// time this analyzer ships (verified via grep), so the empty-whitelist
// landing posture is correct.
//
// Future relaxation. If a legitimate need for `#pragma` suppression
// emerges (e.g., a generated file that can't carry an attribute), the
// whitelist mechanism can be added then — at present `SuppressMessage`
// covers every observed legitimate case so adding the whitelist now
// would be speculative complexity.
//
// Severity. Error, matching VFD012's own severity. A `#pragma disable
// VFD012` that the analyzer catches at build time is preferable to a
// silent determinism violation discovered weeks later via baseline
// drift.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Voxelforge.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class PragmaSuppressionAnalyzer : DiagnosticAnalyzer
    {
        private const string Category = "Voxelforge.Determinism";
        private const string HelpLink =
            "https://github.com/poetac/voxelforge/blob/main/Voxelforge/docs/ADR/ADR-020-deterministic-analyzer.md";

        public static readonly DiagnosticDescriptor Vfa005 = new(
            id: "VFA005",
            title: "Ambient #pragma suppression of VFD012",
            messageFormat:
                "Replace `#pragma warning {0} VFD012` with [SuppressMessage(\"Voxelforge.Determinism\", \"VFD012\", Justification = \"…\")] on the symbol so the suppression surfaces in IDE hover and review",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "VFD012 polices a determinism contract on IObjective: any wall-clock read " +
                "inside an IObjective method silently violates same-design-vector → same-Score. " +
                "Suppressions must be exceptional and visible. The canonical escape hatch is " +
                "the [SuppressMessage(\"Voxelforge.Determinism\", \"VFD012\", Justification = \"…\")] " +
                "attribute form (see Voxelforge.Core/Optimization/ObjectiveWrappers.cs TeeObjective " +
                "for a worked example). `#pragma warning disable VFD012` is ambient preprocessor " +
                "trivia, invisible to symbol-hover + review; VFA005 bans it outright.",
            helpLinkUri: HelpLink);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Vfa005);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxTreeAction(AnalyzeTree);
        }

        private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
        {
            var root = context.Tree.GetRoot(context.CancellationToken);

            foreach (var trivia in root.DescendantTrivia())
            {
                if (!trivia.IsKind(SyntaxKind.PragmaWarningDirectiveTrivia))
                    continue;

                if (trivia.GetStructure() is not PragmaWarningDirectiveTriviaSyntax pragma)
                    continue;

                string action = pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword)
                    ? "disable"
                    : pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.RestoreKeyword)
                        ? "restore"
                        : pragma.DisableOrRestoreKeyword.ValueText;

                foreach (var code in pragma.ErrorCodes)
                {
                    string codeText = code.ToString();
                    if (codeText == "VFD012" || codeText == "\"VFD012\"")
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Vfa005,
                            code.GetLocation(),
                            action));
                    }
                }
            }
        }
    }
}
