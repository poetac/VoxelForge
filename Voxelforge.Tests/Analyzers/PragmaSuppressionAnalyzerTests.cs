// PragmaSuppressionAnalyzerTests — VFA005 (issue #626).
//
// Each test feeds a hand-rolled user-source file into the analyzer
// harness and asserts whether VFA005 fires. The analyzer scans
// PragmaWarningDirectiveTriviaSyntax nodes; tests cover:
//   • #pragma warning disable VFD012  → VFA005 fires
//   • #pragma warning restore VFD012  → VFA005 fires
//   • #pragma warning disable VFD012, VFD013 → VFA005 fires only on VFD012
//   • #pragma warning disable VFD001 (or other rule)  → no VFA005
//   • #pragma warning disable (no rule code)  → no VFA005
//   • [SuppressMessage("Voxelforge.Determinism", "VFD012", ...)] → no VFA005
//     (the structural-attribute escape hatch is the intended form).

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Voxelforge.Analyzers;

namespace Voxelforge.Tests.Analyzers;

public sealed class PragmaSuppressionAnalyzerTests
{
    private static async Task RunAsync(string testSource, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<PragmaSuppressionAnalyzer, DefaultVerifier>
        {
            TestCode = testSource,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static DiagnosticResult Vfa005(int line, int column, string action) =>
        new DiagnosticResult(PragmaSuppressionAnalyzer.Vfa005)
            .WithLocation(line, column)
            .WithArguments(action);

    [Fact]
    public async Task PragmaDisableVfd012_Fires()
    {
        var src = """
            using System;

            namespace Voxelforge.Tests.Sample
            {
                public class C
                {
                    public void M()
                    {
            #pragma warning disable VFD012
                        var t = DateTime.UtcNow;
            #pragma warning restore VFD012
                    }
                }
            }
            """;
        // Line 9 / 11 carry the pragmas (1-indexed inside the test source).
        // VFA005 reports on the rule-code token (`VFD012`) — column 25 for
        // `disable` (after "#pragma warning disable "), column 25 for
        // `restore` (after "#pragma warning restore ").
        await RunAsync(src,
            Vfa005(line: 9, column: 25, action: "disable"),
            Vfa005(line: 11, column: 25, action: "restore"));
    }

    [Fact]
    public async Task PragmaDisableVfd012_InsideMultiCode_FiresOnVfd012Only()
    {
        var src = """
            using System;

            namespace Voxelforge.Tests.Sample
            {
                public class C
                {
                    public void M()
                    {
            #pragma warning disable VFD012, VFD013
                        var t = DateTime.UtcNow;
            #pragma warning restore VFD012, VFD013
                    }
                }
            }
            """;
        // Each pragma lists two rules; VFA005 fires on the VFD012 token
        // only (VFD013 token is unaffected). Same column 25 as the
        // single-rule case — VFD012 is still the first listed code.
        await RunAsync(src,
            Vfa005(line: 9, column: 25, action: "disable"),
            Vfa005(line: 11, column: 25, action: "restore"));
    }

    [Fact]
    public async Task PragmaDisableOtherRule_DoesNotFire()
    {
        var src = """
            using System;

            namespace Voxelforge.Tests.Sample
            {
                public class C
                {
                    public void M()
                    {
            #pragma warning disable VFD001
                        var t = DateTime.UtcNow;
            #pragma warning restore VFD001
                    }
                }
            }
            """;
        await RunAsync(src);
    }

    [Fact]
    public async Task PragmaDisableNoCode_DoesNotFire()
    {
        // `#pragma warning disable` (no rule list) is a global suppression;
        // VFA005 is scoped to VFD012 by name, so an empty rule list is
        // out of scope. This is intentional — the issue specifically
        // targets ambient `#pragma warning disable VFD012`.
        var src = """
            namespace Voxelforge.Tests.Sample
            {
                public class C
                {
                    public void M()
                    {
            #pragma warning disable
            #pragma warning restore
                    }
                }
            }
            """;
        await RunAsync(src);
    }

    [Fact]
    public async Task SuppressMessageAttribute_DoesNotFire()
    {
        // The canonical escape hatch for VFD012 — see TeeObjective in
        // Voxelforge.Core/Optimization/ObjectiveWrappers.cs. VFA005 must
        // not punish the structural-attribute form.
        var src = """
            using System;
            using System.Diagnostics.CodeAnalysis;

            namespace Voxelforge.Tests.Sample
            {
                public class C
                {
                    [SuppressMessage("Voxelforge.Determinism", "VFD012",
                        Justification = "Test fixture by contract.")]
                    public DateTime M()
                    {
                        return DateTime.UtcNow;
                    }
                }
            }
            """;
        await RunAsync(src);
    }
}
