// CrossFamilyImportAnalyzerTests — VFA001.
//
// Each test feeds a hand-rolled user-source file (an assumed family
// assembly name set via SolutionTransforms) into the analyzer harness
// and asserts which diagnostics fire (or don't).
//
// No PicoGK, no voxel construction — sidesteps the xUnit + PicoGK
// pitfall in CLAUDE.md.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Voxelforge.Analyzers;

namespace Voxelforge.Tests.Analyzers;

public sealed class CrossFamilyImportAnalyzerTests
{
    // Stub namespace declarations so the synthetic test sources compile.
    // The analyzer reads `using` directive text without resolving names,
    // but the test harness still runs the C# compiler over the sources;
    // referencing a non-existent namespace would produce CS0234. This
    // file declares every family namespace any test references plus a
    // pair of representative shared-Core and own-family namespaces with
    // dummy classes to satisfy `using static` references.
    private const string StubNamespaces = """
        namespace Voxelforge { }
        namespace Voxelforge.Engines { }
        namespace Voxelforge.Optimization { }
        namespace Voxelforge.Combustion { }
        namespace Voxelforge.Geometry.LpbfAnalysis { }
        namespace Voxelforge.Airbreathing { }
        namespace Voxelforge.Airbreathing.Cycles { }
        namespace Voxelforge.Airbreathing.Optimization { }
        namespace Voxelforge.Marine { }
        namespace Voxelforge.Marine.Cycles { }
        namespace Voxelforge.Marine.Helpers
        {
            public static class Constants { }
        }
        namespace Voxelforge.Electric { }
        namespace Voxelforge.Electric.Hall { }
        namespace Voxelforge.ElectricPropulsion { }
        namespace Voxelforge.ElectricPropulsion.Bar { }
        namespace Voxelforge.ElectricPropulsion.Plasma { }
        namespace Voxelforge.Cfd { }
        namespace Voxelforge.Cfd.Bar { }
        namespace Voxelforge.Marine.Foo { }
        namespace Voxelforge.Tests { }
        """;

    private static async Task RunAsync(
        string assemblyName,
        string testSource,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<CrossFamilyImportAnalyzer, DefaultVerifier>
        {
            TestCode = testSource,
        };
        test.TestState.Sources.Add(("Stubs.cs", StubNamespaces));
        test.SolutionTransforms.Add((sol, projectId) =>
            sol.WithProjectAssemblyName(projectId, assemblyName));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static DiagnosticResult Vfa001(int line, int column,
        string sourceFamily, string targetNamespace, string targetFamily) =>
        new DiagnosticResult(CrossFamilyImportAnalyzer.Vfa001)
            .WithLocation(line, column)
            .WithArguments(sourceFamily, targetNamespace, targetFamily);

    // ── Family-specific assembly: legitimate own-family + shared-Core imports ──

    [Fact]
    public async Task OwnFamilyImport_DoesNotFire()
    {
        var src = """
            using Voxelforge.Airbreathing.Cycles;

            namespace Voxelforge.Airbreathing.Optimization
            {
                public class C { }
            }
            """;
        await RunAsync("Voxelforge.Airbreathing.Core", src);
    }

    [Fact]
    public async Task SharedCoreImport_DoesNotFire()
    {
        // Voxelforge.Optimization, Voxelforge.Combustion, Voxelforge.Engines —
        // all bare Voxelforge.X (X is not a family token) — are shared Core.
        var src = """
            using Voxelforge.Optimization;
            using Voxelforge.Engines;
            using Voxelforge.Geometry.LpbfAnalysis;

            namespace Voxelforge.Airbreathing.Optimization
            {
                public class C { }
            }
            """;
        await RunAsync("Voxelforge.Airbreathing.Core", src);
    }

    [Fact]
    public async Task NonVoxelforgeImport_DoesNotFire()
    {
        var src = """
            using System;
            using System.Collections.Generic;

            namespace Voxelforge.Airbreathing.Optimization
            {
                public class C { }
            }
            """;
        await RunAsync("Voxelforge.Airbreathing.Core", src);
    }

    [Fact]
    public async Task BareVoxelforgeImport_DoesNotFire()
    {
        // `using Voxelforge;` is the root namespace — no family.
        var src = """
            using Voxelforge;

            namespace Voxelforge.Airbreathing.Optimization
            {
                public class C { }
            }
            """;
        await RunAsync("Voxelforge.Airbreathing.Core", src);
    }

    // ── Family-specific assembly: cross-family imports fire ──

    [Fact]
    public async Task CrossFamilyImport_FiresVfa001()
    {
        // Airbreathing assembly importing Marine — even though no Marine
        // pillar exists yet, the analyzer must fire on the namespace
        // string itself. (The Marine.Cycles namespace is hypothetical;
        // the analyzer never resolves names — it parses the using path.)
        var src = """
            using Voxelforge.Marine.Cycles;

            namespace Voxelforge.Airbreathing.Optimization
            {
                public class C { }
            }
            """;
        await RunAsync(
            "Voxelforge.Airbreathing.Core",
            src,
            // Source line 1: `using Voxelforge.Marine.Cycles;` — column 1.
            Vfa001(line: 1, column: 1,
                sourceFamily: "Airbreathing",
                targetNamespace: "Voxelforge.Marine.Cycles",
                targetFamily: "Marine"));
    }

    [Fact]
    public async Task CrossFamilyImport_NestedFamilyToken_FiresVfa001()
    {
        // The 'ElectricPropulsion' segment is the family token; sub-namespaces
        // don't matter. (Was originally written against the legacy 'Electric'
        // token; updated to 'ElectricPropulsion' as part of #554 — see ADR-040.)
        var src = """
            using Voxelforge.ElectricPropulsion.Plasma;

            namespace Voxelforge.Airbreathing.Optimization
            {
                public class C { }
            }
            """;
        await RunAsync(
            "Voxelforge.Airbreathing.Core",
            src,
            Vfa001(line: 1, column: 1,
                sourceFamily: "Airbreathing",
                targetNamespace: "Voxelforge.ElectricPropulsion.Plasma",
                targetFamily: "ElectricPropulsion"));
    }

    // ── Family-agnostic assembly: analyzer skips entirely ──

    [Fact]
    public async Task SharedCoreAssembly_CrossFamilyImport_DoesNotFire()
    {
        // Voxelforge.Core may import any namespace — it's family-agnostic.
        var src = """
            using Voxelforge.Airbreathing.Cycles;

            namespace Voxelforge.Optimization
            {
                public class C { }
            }
            """;
        await RunAsync("Voxelforge.Core", src);
    }

    [Fact]
    public async Task DispatcherAssembly_CrossFamilyImport_DoesNotFire()
    {
        // Voxelforge (the main app) is the dispatcher — it legitimately
        // imports both rocket and air-breathing types.
        var src = """
            using Voxelforge.Airbreathing.Optimization;

            namespace Voxelforge
            {
                public class C { }
            }
            """;
        await RunAsync("Voxelforge", src);
    }

    [Fact]
    public async Task TestAssembly_CrossFamilyImport_DoesNotFire()
    {
        // Voxelforge.Tests (rocket test suite) and Voxelforge.MicroBenchmarks
        // are family-agnostic by name (no `Voxelforge.{Family}.*` match).
        var src = """
            using Voxelforge.Airbreathing.Optimization;

            namespace Voxelforge.Tests
            {
                public class C { }
            }
            """;
        await RunAsync("Voxelforge.Tests", src);
    }

    // ── Single-segment assembly names ──

    [Fact]
    public async Task BareVoxelforgeAssembly_NotConsideredFamilySpecific()
    {
        // The dispatcher is named just "Voxelforge" — no segment after
        // the prefix, so ExtractFamilyFromAssemblyName returns null.
        var src = """
            using Voxelforge.Marine.Cycles;
            using Voxelforge.Electric.Hall;

            namespace Voxelforge.Optimization
            {
                public class C { }
            }
            """;
        await RunAsync("Voxelforge", src);
    }

    // ── Static using + alias edge cases ──

    [Fact]
    public async Task UsingStaticOfCrossFamilyType_FiresVfa001()
    {
        // `using static Voxelforge.Marine.Helpers.Constants;` — the analyzer
        // walks the same namespace path and fires.
        var src = """
            using static Voxelforge.Marine.Helpers.Constants;

            namespace Voxelforge.Airbreathing.Optimization
            {
                public class C { }
            }
            """;
        await RunAsync(
            "Voxelforge.Airbreathing.Core",
            src,
            Vfa001(line: 1, column: 1,
                sourceFamily: "Airbreathing",
                targetNamespace: "Voxelforge.Marine.Helpers.Constants",
                targetFamily: "Marine"));
    }

    // ── Regression for #554 / audit F-1 ──

    [Fact]
    public async Task Vfa001_FiresOnMarineImport_FromElectricPropulsionCore()
    {
        // Regression for #554 / audit F-1: prior to the token-fix, the analyzer
        // bailed on assemblies named "Voxelforge.ElectricPropulsion.*" because
        // KnownFamilyTokens contained "Electric" rather than "ElectricPropulsion".
        var src = """
            using Voxelforge.Marine.Foo;

            namespace Voxelforge.ElectricPropulsion.Bar
            {
                public class C { }
            }
            """;
        await RunAsync(
            "Voxelforge.ElectricPropulsion.Core",
            src,
            Vfa001(line: 1, column: 1,
                sourceFamily: "ElectricPropulsion",
                targetNamespace: "Voxelforge.Marine.Foo",
                targetFamily: "Marine"));
    }

    [Fact]
    public async Task Vfa001_FiresOnMarineImport_FromCfdCore()
    {
        // Regression for #554 / audit F-1: CFD pillar was missing from KnownFamilyTokens
        // entirely. Synthetic compilation with assembly name "Voxelforge.Cfd.Core".
        var src = """
            using Voxelforge.Marine.Foo;

            namespace Voxelforge.Cfd.Bar
            {
                public class C { }
            }
            """;
        await RunAsync(
            "Voxelforge.Cfd.Core",
            src,
            Vfa001(line: 1, column: 1,
                sourceFamily: "Cfd",
                targetNamespace: "Voxelforge.Marine.Foo",
                targetFamily: "Marine"));
    }

    [Fact]
    public async Task Vfa001_DoesNotFire_OnEpCoreOwnFamilyImport()
    {
        // Negative control: ElectricPropulsion code importing from its own family
        // is legitimate.
        var src = """
            using Voxelforge.ElectricPropulsion.Plasma;

            namespace Voxelforge.ElectricPropulsion.Bar
            {
                public class C { }
            }
            """;
        await RunAsync("Voxelforge.ElectricPropulsion.Core", src);
    }
}
