// FamilyNamespacePurityAnalyzerTests — VFA002.
//
// Each test feeds a hand-rolled user-source file (with a configured
// assembly name) into the analyzer harness and asserts which
// diagnostics fire (or don't).

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Voxelforge.Analyzers;

namespace Voxelforge.Tests.Analyzers;

public sealed class FamilyNamespacePurityAnalyzerTests
{
    private static async Task RunAsync(
        string assemblyName,
        string testSource,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<FamilyNamespacePurityAnalyzer, DefaultVerifier>
        {
            TestCode = testSource,
        };
        test.SolutionTransforms.Add((sol, projectId) =>
            sol.WithProjectAssemblyName(projectId, assemblyName));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    // Diagnostic-line precision is sensitive to text-trimming behavior of
    // raw string literals (the leading newline is stripped in C# 11). When
    // a test fails on column-mismatch, run with `dotnet test ... -v n` and
    // adjust the WithLocation column to match the actual reported value.

    private static DiagnosticResult Vfa002(int line, int column,
        string typeName, string namespaceName, string assemblyName, string family) =>
        new DiagnosticResult(FamilyNamespacePurityAnalyzer.Vfa002)
            .WithLocation(line, column)
            .WithArguments(typeName, namespaceName, assemblyName, family);

    // ── Family assembly: types in correct namespace ──

    [Fact]
    public async Task TypeInExactFamilyNamespace_DoesNotFire()
    {
        var src = """
            namespace Voxelforge.Airbreathing
            {
                public class C { }
            }
            """;
        await RunAsync("Voxelforge.Airbreathing.Core", src);
    }

    [Fact]
    public async Task TypeInFamilySubNamespace_DoesNotFire()
    {
        var src = """
            namespace Voxelforge.Airbreathing.Optimization
            {
                public class C { }
            }
            """;
        await RunAsync("Voxelforge.Airbreathing.Core", src);
    }

    [Fact]
    public async Task TypeInDeeplyNestedFamilySubNamespace_DoesNotFire()
    {
        var src = """
            namespace Voxelforge.Airbreathing.Atmosphere.US1976
            {
                public class C { }
            }
            """;
        await RunAsync("Voxelforge.Airbreathing.Core", src);
    }

    // ── Family assembly: violations fire ──

    [Fact]
    public async Task TypeInBareVoxelforgeNamespace_FiresVfa002()
    {
        var src = """
            namespace Voxelforge.Engines
            {
                public class C { }
            }
            """;
        await RunAsync(
            "Voxelforge.Airbreathing.Core",
            src,
            // Line 3: `public class C` declaration starts at column 18 (after `public class `).
            Vfa002(line: 3, column: 18,
                typeName: "C",
                namespaceName: "Voxelforge.Engines",
                assemblyName: "Voxelforge.Airbreathing.Core",
                family: "Airbreathing"));
    }

    [Fact]
    public async Task TypeInGlobalNamespace_FiresVfa002()
    {
        var src = """
            public class TopLevel { }
            """;
        await RunAsync(
            "Voxelforge.Airbreathing.Core",
            src,
            Vfa002(line: 1, column: 14,
                typeName: "TopLevel",
                namespaceName: "<global>",
                assemblyName: "Voxelforge.Airbreathing.Core",
                family: "Airbreathing"));
    }

    [Fact]
    public async Task TypeInOtherFamilyNamespace_FiresVfa002()
    {
        // A type accidentally declared in `namespace Voxelforge.Marine.Foo`
        // but compiled into `Voxelforge.Airbreathing.Core` should fire too.
        var src = """
            namespace Voxelforge.Marine.Foo
            {
                public class C { }
            }
            """;
        await RunAsync(
            "Voxelforge.Airbreathing.Core",
            src,
            Vfa002(line: 3, column: 18,
                typeName: "C",
                namespaceName: "Voxelforge.Marine.Foo",
                assemblyName: "Voxelforge.Airbreathing.Core",
                family: "Airbreathing"));
    }

    // ── Family-agnostic assemblies: skip entirely ──

    [Fact]
    public async Task SharedCoreAssembly_BareNamespace_DoesNotFire()
    {
        var src = """
            namespace Voxelforge.Engines
            {
                public class C { }
            }
            """;
        await RunAsync("Voxelforge.Core", src);
    }

    [Fact]
    public async Task DispatcherAssembly_BareNamespace_DoesNotFire()
    {
        // The main app legitimately uses bare Voxelforge.* namespaces.
        var src = """
            namespace Voxelforge.Optimization
            {
                public class RegenObjective { }
            }
            """;
        await RunAsync("Voxelforge", src);
    }

    [Fact]
    public async Task TestAssembly_AnyNamespace_DoesNotFire()
    {
        var src = """
            namespace Voxelforge.Tests
            {
                public class T { }
            }
            """;
        await RunAsync("Voxelforge.Tests", src);
    }

    // ── Nested types: outer drives the diagnostic, inner is skipped ──

    [Fact]
    public async Task NestedType_OuterDrivesDiagnostic()
    {
        var src = """
            namespace Voxelforge.Airbreathing.Stations
            {
                public class Outer
                {
                    public class Nested { }
                }
            }
            """;
        // Outer is in the right namespace; Nested is skipped (nested types
        // inherit their containing type's namespace by definition).
        await RunAsync("Voxelforge.Airbreathing.Core", src);
    }
}
