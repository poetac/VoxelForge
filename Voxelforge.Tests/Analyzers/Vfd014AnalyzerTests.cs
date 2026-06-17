// Vfd014AnalyzerTests — ADR-042 / issue #547 / issue #565.
//
// VFD014: `for (double t = t0; t < tEnd; t += dt)` inside [Deterministic]
// (or IObjective) scope is the floating-point-accumulator time-loop
// anti-pattern. The terminating tick count depends on host FMA / FP-rounding
// behaviour, so two runs at the same seed produce different iteration
// counts. Refactor target is integer-tick form (the same shape PR 3 / #553
// applied to TimeStepIntegrator).

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Voxelforge.Analyzers;

namespace Voxelforge.Tests.Analyzers;

public sealed class Vfd014AnalyzerTests
{
    // Stub of the [Deterministic] marker attribute — mirrors the other
    // analyzer test files (DeterministicAnalyzerTests, etc.). Separate
    // source file lets user code lead with `using` directives.
    private const string AttributeSource = """
        namespace Voxelforge.Optimization
        {
            [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
            public sealed class DeterministicAttribute : System.Attribute { }
        }
        """;

    // Stub of the IObjective interface (minimal shape — only the type
    // symbol is needed to wire IObjective-scope detection).
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

    private static DiagnosticResult Vfd014(int line, int column) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd014).WithLocation(line, column);

    // ── Positive: fires on the canonical FP-accumulator shape ────────────

    [Fact]
    public async Task Vfd014_FiresOnFpTimeLoop_InDeterministicScope()
    {
        var src = """
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void Run()
                {
                    for (double t = 0; t < 1; t += 0.1)
                    {
                        var _ = t;
                    }
                }
            }
            """;
        // Line 8: "        for (double t = 0; t < 1; t += 0.1)" — 'f' at column 9.
        await RunAsync(src, Vfd014(line: 8, column: 9));
    }

    // ── Negative: integer-tick refactor (the recommended shape) is clean ─

    [Fact]
    public async Task Vfd014_DoesNotFire_OnIntegerTickForm_InDeterministicScope()
    {
        var src = """
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void Run()
                {
                    int n = 11;
                    for (int i = 0; i < n; i++)
                    {
                        double t = i * 0.1;
                        var _ = t;
                    }
                }
            }
            """;
        await RunAsync(src);   // empty diagnostics — integer index, not double
    }

    // ── Negative: outside [Deterministic] / IObjective, pattern is fine ──

    [Fact]
    public async Task Vfd014_DoesNotFire_OutsideDeterministicScope()
    {
        // A plain loop in non-deterministic code (e.g. a CLI driver, a
        // legitimate simulation that doesn't claim bit-equality) MUST NOT
        // fire VFD014.
        var src = """
            public class C
            {
                public void Run()
                {
                    for (double t = 0; t < 1; t += 0.1)
                    {
                        var _ = t;
                    }
                }
            }
            """;
        await RunAsync(src);   // empty diagnostics — not tainted, not IObjective
    }
}
