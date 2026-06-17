// CrossFamilyImportAnalyzer.cs — VFA001 (Sprint 0 / Wave 1, 2026-05-05).
//
// Pillar-discipline guard. Voxelforge's multi-pillar architecture keeps
// each engine family (rocket, air-breathing, marine, electric-propulsion,
// nuclear, CFD, solar) in its own `Voxelforge.{Family}.*` namespace +
// assembly. Family
// code may legitimately depend on:
//   • its own family (`Voxelforge.{OwnFamily}.*`)
//   • shared Core (`Voxelforge.X` where X is not a family token —
//     e.g. `Voxelforge.Optimization`, `Voxelforge.Combustion`)
//   • non-Voxelforge namespaces (System.*, xunit, etc.)
//
// Family code may NOT depend on a sibling family. A `using
// Voxelforge.Marine.X;` inside a file whose namespace starts with
// `Voxelforge.Airbreathing.*` is a tangle waiting to happen and fires
// VFA001 at compile time.
//
// Scope. Applies only to assemblies whose name matches
// `Voxelforge.{KnownFamily}.*`. The dispatcher app (`Voxelforge`),
// shared Core (`Voxelforge.Core`), generators, analyzers, benchmarks,
// renderers, kiosk, eval, and tests are all permitted to import any
// family — they are family-agnostic plumbing by design.
//
// Severity: Error (TreatWarningsAsErrors is on globally; this analyzer
// must never false-positive). Existing rocket + air-breathing code is
// audited clean as of 2026-05-05; new imports of a sibling family will
// fire on the first build.

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Voxelforge.Analyzers
{
    [DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
    public sealed class CrossFamilyImportAnalyzer : DiagnosticAnalyzer
    {
        private const string Category = "Voxelforge.Pillars";
        private const string HelpLink =
            "https://github.com/poetac/voxelforge/blob/main/Voxelforge/docs/ADR/ADR-026-multi-pillar-coordination.md";

        public static readonly DiagnosticDescriptor Vfa001 = new(
            id: "VFA001",
            title: "Cross-family import in family-specific assembly",
            messageFormat:
                "File in family '{0}' imports namespace '{1}' from sibling family '{2}'. " +
                "Family-specific code may import its own family or shared Core only.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Voxelforge engine families (rocket, air-breathing, marine, electric-propulsion, " +
                "nuclear, CFD, solar) live in separate `Voxelforge.{Family}.*` namespaces and " +
                "assemblies. Family code may import its own family + shared Core (bare " +
                "`Voxelforge.*`) + non-Voxelforge namespaces, but never a sibling family. " +
                "Cross-family coordination belongs in shared Core or in dispatcher / app assemblies.",
            helpLinkUri: HelpLink);

        // Known family tokens are the SSOT in FamilyTokens.All — see ADR-040.
        internal static ImmutableHashSet<string> KnownFamilyTokens => FamilyTokens.All;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Vfa001);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext compilationContext)
        {
            // Determine the source assembly's family token, if any. The
            // analyzer only runs on family-specific assemblies; everything
            // else (shared Core, dispatcher, tests, benchmarks) is exempt.
            var assemblyName = compilationContext.Compilation.AssemblyName ?? string.Empty;
            var sourceFamily = ExtractFamilyFromAssemblyName(assemblyName);
            if (sourceFamily is null)
            {
                return; // Family-agnostic assembly. Skip analysis.
            }

            compilationContext.RegisterSyntaxNodeAction(
                ctx => AnalyzeUsingDirective(ctx, sourceFamily),
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.UsingDirective);
        }

        private const string VoxelforgePrefix = "Voxelforge.";

        // Match the first-segment of `text` after `start` (up to the next
        // '.' or end-of-string) against the known family-token set. If a
        // match is found, return the canonical interned token string from
        // the set; otherwise null. Zero allocations on miss; on match we
        // return the table's own string instance (no substring allocation).
        private static string? MatchFamilyTokenAt(string text, int start)
        {
            int dot = text.IndexOf('.', start);
            int end = dot < 0 ? text.Length : dot;
            int len = end - start;
            if (len == 0) return null;
            // Linear-scan the 6-element token set with ordinal-region compare —
            // avoids the substring allocation that an `ImmutableHashSet.Contains`
            // path would force (HashSet APIs take a string, not a span).
            foreach (var token in KnownFamilyTokens)
            {
                if (token.Length == len &&
                    string.CompareOrdinal(text, start, token, 0, len) == 0)
                {
                    return token;
                }
            }
            return null;
        }

        // Extract the family token from an assembly name like
        // "Voxelforge.Airbreathing.Core" → "Airbreathing".
        // Returns null for assemblies that are not family-specific.
        internal static string? ExtractFamilyFromAssemblyName(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) return null;
            if (!assemblyName.StartsWith(VoxelforgePrefix, StringComparison.Ordinal)) return null;
            int start = VoxelforgePrefix.Length;
            // Must have at least one more dot (e.g. "Airbreathing.Core") —
            // a bare "Voxelforge.Foo" assembly without a sub-segment is
            // not family-specific (e.g. "Voxelforge.Core" itself).
            int dot = assemblyName.IndexOf('.', start);
            if (dot < 0) return null;
            return MatchFamilyTokenAt(assemblyName, start);
        }

        // Extract the target family token from a `using` directive's
        // namespace. Returns the family token, the empty string for
        // bare `Voxelforge.X` shared-Core paths (NOT a family), or
        // null for non-Voxelforge namespaces.
        internal static string? ExtractFamilyFromUsing(string namespacePath)
        {
            if (string.IsNullOrEmpty(namespacePath)) return null;
            // Outside Voxelforge.* entirely (System.*, Xunit, ...).
            if (!namespacePath.StartsWith(VoxelforgePrefix, StringComparison.Ordinal))
            {
                // Allow bare `using Voxelforge;` too — its target is the
                // root namespace, no family.
                return namespacePath == "Voxelforge" ? string.Empty : null;
            }
            // First segment matches a known family token → that family.
            // Otherwise, it's a shared-Core sub-namespace (Voxelforge.Optimization,
            // Voxelforge.Combustion, etc.) → empty string sentinel.
            return MatchFamilyTokenAt(namespacePath, VoxelforgePrefix.Length) ?? string.Empty;
        }

        private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context, string sourceFamily)
        {
            var usingDir = (UsingDirectiveSyntax)context.Node;
            // Skip using-aliases (`using X = Voxelforge.Foo;`) — the
            // alias rule is a different concern; the import itself still
            // happens through the right-hand side, which we cover via the
            // .Name property below.
            // Skip extern aliases (handled elsewhere).
            var name = usingDir.Name;
            if (name is null) return;

            var namespacePath = name.ToString();
            var targetFamily = ExtractFamilyFromUsing(namespacePath);

            // Non-Voxelforge import → allow.
            if (targetFamily is null) return;
            // Shared-Core import (bare Voxelforge.X where X is not a family token) → allow.
            if (targetFamily.Length == 0) return;
            // Same-family import → allow.
            if (string.Equals(targetFamily, sourceFamily, StringComparison.Ordinal)) return;

            // Cross-family import → fire.
            var diagnostic = Diagnostic.Create(
                Vfa001,
                usingDir.GetLocation(),
                sourceFamily,
                namespacePath,
                targetFamily);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
