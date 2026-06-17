// FamilyNamespacePurityAnalyzer.cs — VFA002 (Sprint 0 / Wave 1, 2026-05-05).
//
// Pillar-discipline guard. Every named type declared in a family-specific
// assembly (`Voxelforge.{Family}.Core.dll`, `Voxelforge.{Family}.Voxels.dll`,
// `Voxelforge.{Family}.StlExporter.dll`, `Voxelforge.{Family}.Tests.dll`,
// etc.) must live in a namespace that begins with `Voxelforge.{Family}`.
//
// This catches the failure mode where a developer in
// `Voxelforge.Airbreathing.Core` accidentally drops a type into bare
// `namespace Voxelforge.Engines` — that'd silently extend shared Core's
// public surface with family-specific code and tangle dependencies.
//
// Scope. Same exclusion rule as VFA001: applies only to assemblies
// whose name matches `Voxelforge.{KnownFamily}.*`. Shared Core
// (`Voxelforge.Core`), the dispatcher app (`Voxelforge`), Voxels,
// generators, analyzers, benchmarks, kiosk, eval, renderer,
// stlexporter, microbenchmarks, and tests are family-agnostic by
// construction and exempt.
//
// Severity: Error.

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Voxelforge.Analyzers
{
    [DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
    public sealed class FamilyNamespacePurityAnalyzer : DiagnosticAnalyzer
    {
        private const string Category = "Voxelforge.Pillars";
        private const string HelpLink =
            "https://github.com/poetac/voxelforge/blob/main/Voxelforge/docs/ADR/ADR-026-multi-pillar-coordination.md";

        public static readonly DiagnosticDescriptor Vfa002 = new(
            id: "VFA002",
            title: "Type in family assembly outside family namespace",
            messageFormat:
                "Type '{0}' lives in namespace '{1}' but its assembly is '{2}'. " +
                "Family-specific assemblies must declare types under the matching " +
                "'Voxelforge.{3}' namespace.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Family-specific assemblies (`Voxelforge.{Family}.*`) must keep all " +
                "their named types under the matching `Voxelforge.{Family}` namespace " +
                "tree. Dropping a type into bare `Voxelforge.X` or another family's " +
                "namespace tangles the dependency graph and silently extends the wrong " +
                "assembly's public surface.",
            helpLinkUri: HelpLink);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Vfa002);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext compilationContext)
        {
            var assemblyName = compilationContext.Compilation.AssemblyName ?? string.Empty;
            var sourceFamily = CrossFamilyImportAnalyzer.ExtractFamilyFromAssemblyName(assemblyName);
            if (sourceFamily is null)
            {
                return; // Family-agnostic assembly — no namespace purity rule.
            }

            // Tests in family-specific assemblies often nest under a
            // `Voxelforge.Airbreathing.Tests.{X}` namespace, which IS a
            // sub-namespace of `Voxelforge.Airbreathing`, so the same
            // prefix check covers them.
            var requiredPrefix = "Voxelforge." + sourceFamily;

            compilationContext.RegisterSymbolAction(
                ctx => AnalyzeNamedType(ctx, sourceFamily, requiredPrefix),
                SymbolKind.NamedType);
        }

        private static void AnalyzeNamedType(
            SymbolAnalysisContext context,
            string sourceFamily,
            string requiredPrefix)
        {
            var symbol = (INamedTypeSymbol)context.Symbol;

            // Skip nested types — the outer type's namespace is what
            // matters, and outer types fire on their own analyzer pass.
            if (symbol.ContainingType is not null) return;

            // Skip implicitly-declared / compiler-generated symbols
            // (record positional ctors, anonymous types, etc.).
            if (symbol.IsImplicitlyDeclared) return;

            // Skip types that have no source location (forwarded types,
            // referenced metadata that the host happened to surface).
            if (symbol.Locations.Length == 0 ||
                !symbol.Locations[0].IsInSource)
            {
                return;
            }

            var ns = symbol.ContainingNamespace;
            var nsName = ns is { IsGlobalNamespace: true } ? "<global>" : ns.ToDisplayString();

            // Permitted: namespace == requiredPrefix or starts with `requiredPrefix.`
            if (string.Equals(nsName, requiredPrefix, StringComparison.Ordinal)) return;
            if (nsName.StartsWith(requiredPrefix + ".", StringComparison.Ordinal)) return;

            // Not permitted — fire on the type's first source location.
            var diagnostic = Diagnostic.Create(
                Vfa002,
                symbol.Locations[0],
                symbol.Name,
                nsName,
                context.Compilation.AssemblyName ?? "<unknown>",
                sourceFamily);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
