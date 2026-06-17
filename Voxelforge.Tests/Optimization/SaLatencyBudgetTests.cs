// SaLatencyBudgetTests — issue #636.
//
// Pins per-ResourceMode wall-clock budgets for MultiChainOptimizer.Run
// on a representative synthetic objective. The numerical thresholds
// (and the table in DESIGN_VARIABLES.md § SA Solve latency budgets)
// are the falsifier for P21 (parallel per-station wall-T solve,
// #642): a breach by > 20 % across two consecutive runs promotes P21
// to an active sprint.
//
// The test uses a ConvexObjective (sum-of-squares around 0.5) rather
// than the real regen pipeline because:
//   1. We want the test to measure SA + multi-chain scheduling
//      latency, not chamber physics latency. A synthetic objective
//      isolates the optimizer's per-iteration overhead.
//   2. Real-regen tests need a runner-bound PicoGK Library; the
//      latency-budget surface should run anywhere, with no fixture.
//
// `[Trait("Category", "Performance")]` lets CI filter this out when
// running on slow / cold-start runners. The 3 × headroom on the
// budget values (per the issue rationale) avoids flake on warm
// dev-loop runs.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Optimization;

public sealed class SaLatencyBudgetTests
{
    /// <summary>Convex synthetic objective — sum of squares around 0.5.</summary>
    private sealed class ConvexObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;

        public ConvexObjective(int dim)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++)
                _vars[i] = new DesignVariableInfo($"x{i}", 0.0, 1.0);
        }

        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            double sum = 0;
            for (int i = 0; i < vector.Length; i++)
            {
                double d = vector[i] - 0.5;
                sum += d * d;
            }
            return new EvaluationResult(sum, Array.Empty<FeasibilityViolation>(), null);
        }
    }

    // Budget table — see Voxelforge/docs/DESIGN_VARIABLES.md
    // § SA Solve latency budgets. Values carry 3 × headroom vs.
    // canonical-workstation medians so slow CI hardware doesn't flake.
    //
    // Maximum-mode budget is 30 000 ms (10 × headroom) because the
    // self-hosted runners share a single physical machine: when two CI
    // jobs run concurrently, the 16-chain SA can spike to 4–6 s due to
    // OS thread-scheduling contention. PRs #674 and #830 both documented
    // this pattern. The wider budget retains the regression guard (a real
    // 30 s blow-up is still caught) without generating false CI failures.
    [Theory]
    [Trait("Category", "Performance")]
    [InlineData("Quiet",    1,  2000)]   // single-chain surrogate
    [InlineData("Balanced", 4,  2500)]
    [InlineData("Maximum",  0, 30000)]   // chains=0 → DefaultChainCount auto; wide budget for shared-runner contention
    public void SA_Solve_StaysWithinBudget(string modeLabel, int chainCount, long budgetMs)
    {
        // 20 dims, 300 iters — same shape as the documented benchmark.
        var obj = new ConvexObjective(dim: 20);
        var bounds = obj.Variables.Count;
        var bounds_min_max = new (double Min, double Max)[bounds];
        for (int i = 0; i < bounds; i++)
            bounds_min_max[i] = (obj.Variables[i].Min, obj.Variables[i].Max);

        var optimizer = new MultiChainOptimizer(
            bounds:        bounds_min_max,
            maxIterations: 300,
            baseSeed:      42,
            chainCount:    chainCount);

        // Warm-up evaluation to absorb JIT / cold-cache cost; the
        // budget measures steady-state, not first-touch.
        _ = obj.Evaluate(new ReadOnlySpan<double>(new double[bounds]), default);

        var sw = Stopwatch.StartNew();
        var result = optimizer.Run(obj);
        sw.Stop();

        Assert.True(result.BestScore >= 0.0, $"SA should produce a finite score (got {result.BestScore})");
        Assert.True(
            sw.ElapsedMilliseconds <= budgetMs,
            $"SA latency budget breached for ResourceMode='{modeLabel}' chains={chainCount}: " +
            $"elapsed={sw.ElapsedMilliseconds} ms > budget={budgetMs} ms. " +
            $"Per Voxelforge/docs/DESIGN_VARIABLES.md § SA Solve latency budgets, a > 20 % " +
            $"breach across two consecutive runs promotes Performance P21 (#642) to active.");
    }
}
