// HybridSACmaEsOrchestratorTests — Issue #210 (T1.3-followon).
//
// Two-phase round-trip pins. Categorical-then-continuous coverage uses
// the same ConvexObjective/RosenbrockObjective family the CMA-ES + NSGA-II
// suites rely on (sidesteps the xUnit + PicoGK pitfall by avoiding voxel
// constructions).

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class HybridSACmaEsOrchestratorTests
{
    /// <summary>
    /// Convex sum-of-squares around <paramref name="targetPerDim"/>, bounds
    /// [-10, 10]. Used for both SA and CMA-ES phase convergence pins.
    /// </summary>
    private sealed class ConvexObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;
        private readonly double _target;

        public ConvexObjective(int dim, double targetPerDim = 0.5)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++)
                _vars[i] = new DesignVariableInfo($"x{i}", -10.0, 10.0);
            _target = targetPerDim;
        }

        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            double sum = 0;
            for (int i = 0; i < vector.Length; i++)
            {
                double d = vector[i] - _target;
                sum += d * d;
            }
            return new EvaluationResult(sum, Array.Empty<FeasibilityViolation>(), null);
        }
    }

    [Fact]
    public void Convex_5D_BothPhasesRun_CmaImprovesOverSa()
    {
        // SA explores globally (4 chains × 200 iters) over [-10, 10]^5;
        // its convergence is bounded below by the SA optimizer's 5%
        // min-perturb floor (~1.0 / dim on this problem) so SA-best
        // typically lands ~O(1). CMA-ES then refines locally via the
        // covariance matrix and reaches machine-precision in 80 gens.
        var obj = new ConvexObjective(dim: 5, targetPerDim: 0.5);
        var orch = new HybridSACmaEsOrchestrator(
            objective:         obj,
            saMaxIterations:   200,
            saBaseSeed:        42,
            cmaMaxGenerations: 80,
            cmaSeed:           7,
            cmaInitialSigma:   0.3,
            saChainCount:      4);

        var result = orch.Run();

        Assert.True(result.SaResult.TotalIterations > 0, "SA didn't run");
        Assert.True(result.CmaResult.GenerationsCompleted > 0, "CMA didn't run");
        Assert.True(result.CmaBestScore < result.SaBestScore,
            $"CMA-ES didn't improve over SA: SA={result.SaBestScore:G6}, CMA={result.CmaBestScore:G6}");
        Assert.True(result.BestScore < 1e-6,
            $"End-to-end didn't converge: {result.BestScore:G6}");
        for (int i = 0; i < 5; i++)
            Assert.InRange(result.BestParams[i], 0.5 - 1e-2, 0.5 + 1e-2);
    }

    [Fact]
    public void Determinism_SameSeedsProducesIdenticalResult()
    {
        // The hybrid orchestrator inherits its determinism from the two
        // phase optimizers. Same (saBaseSeed, saChainCount, cmaSeed,
        // cmaInitialSigma) must produce bit-identical (BestParams, BestScore).
        var obj1 = new ConvexObjective(dim: 4, targetPerDim: 0.0);
        var obj2 = new ConvexObjective(dim: 4, targetPerDim: 0.0);

        var r1 = new HybridSACmaEsOrchestrator(
            obj1, saMaxIterations: 100, saBaseSeed: 13,
            cmaMaxGenerations: 30, cmaSeed: 99, cmaInitialSigma: 0.3,
            saChainCount: 3).Run();
        var r2 = new HybridSACmaEsOrchestrator(
            obj2, saMaxIterations: 100, saBaseSeed: 13,
            cmaMaxGenerations: 30, cmaSeed: 99, cmaInitialSigma: 0.3,
            saChainCount: 3).Run();

        Assert.Equal(r1.BestScore,    r2.BestScore);
        Assert.Equal(r1.BestParams,   r2.BestParams);
        Assert.Equal(r1.SaBestScore,  r2.SaBestScore);
        Assert.Equal(r1.CmaBestScore, r2.CmaBestScore);
        Assert.Equal(r1.WinningPhase, r2.WinningPhase);
    }

    [Fact]
    public void Determinism_DifferentCmaSeedDivergesOnlyInCmaPhase()
    {
        // Changing only the CMA-ES seed must produce identical SA-phase
        // results (SA depends on saBaseSeed only) but distinct CMA-ES
        // trajectories.
        var obj1 = new ConvexObjective(dim: 4);
        var obj2 = new ConvexObjective(dim: 4);

        var r1 = new HybridSACmaEsOrchestrator(
            obj1, saMaxIterations: 80, saBaseSeed: 5,
            cmaMaxGenerations: 25, cmaSeed: 1, cmaInitialSigma: 0.5,
            saChainCount: 2).Run();
        var r2 = new HybridSACmaEsOrchestrator(
            obj2, saMaxIterations: 80, saBaseSeed: 5,
            cmaMaxGenerations: 25, cmaSeed: 2, cmaInitialSigma: 0.5,
            saChainCount: 2).Run();

        Assert.Equal(r1.SaBestScore, r2.SaBestScore);
        Assert.Equal(r1.SaResult.BestParams, r2.SaResult.BestParams);
        // CMA-ES phase trajectories must differ — different seeds explore
        // different sample sequences (early generations especially diverge
        // strongly). After 25 gens the BestParams should be distinct.
        Assert.NotEqual(r1.CmaResult.BestParams, r2.CmaResult.BestParams);
    }

    [Fact]
    public void CmaPhase_StartsFromSaWinner()
    {
        // The CMA-ES initial mean must equal the SA winner's BestParams
        // — that's the contract that makes this a "hybrid" orchestrator
        // rather than two independent runs. We verify by comparing first-
        // gen scores: CMA-ES seeded from SA winner produces dramatically
        // lower first-gen scores than CMA-ES seeded from a uniform-random
        // point in [-10, 10]^4 (which would have score O(50) at best).
        var obj = new ConvexObjective(dim: 4, targetPerDim: 0.0);
        var orch = new HybridSACmaEsOrchestrator(
            obj, saMaxIterations: 100, saBaseSeed: 42,
            cmaMaxGenerations: 30, cmaSeed: 7,
            cmaInitialSigma: 0.1,    // tight σ so first-gen samples cluster around mean
            saChainCount: 3);
        var result = orch.Run();

        Assert.NotEmpty(result.CmaResult.History);
        // First-gen score floor with σ=0.1 around SA winner is at most
        // ~σ²·dim = 0.04. With a random initial mean far from target,
        // first-gen score would be O(20-100). We assert << 5.0 — easily
        // catches "CMA didn't start from SA winner" without flaking on
        // sampling noise.
        double firstGenBest = result.CmaResult.History[0].BestScore;
        Assert.True(firstGenBest < 5.0,
            $"CMA phase did not start near SA winner: SA-best={result.SaBestScore:G4}, CMA gen-0 best={firstGenBest:G4}");
        // The orchestrator's reported best can never be worse than SA
        // alone — it picks the better of the two phases.
        Assert.True(result.BestScore <= result.SaBestScore + 1e-9,
            $"Orchestrator regressed below SA winner: SA={result.SaBestScore:G6}, end-to-end={result.BestScore:G6}");
    }

    [Fact]
    public void Cancellation_PreCancelledToken_RunsCleanly()
    {
        // Pre-cancelled token: SA's chains exit cleanly before any
        // iteration, then CMA-ES's loop also exits at the first check.
        // Result is whatever each optimizer produced from initial state.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var obj = new ConvexObjective(dim: 3);
        var orch = new HybridSACmaEsOrchestrator(
            obj, saMaxIterations: 100, saBaseSeed: 1,
            cmaMaxGenerations: 50, cmaSeed: 1, cmaInitialSigma: 0.3,
            saChainCount: 2);
        var result = orch.Run(cts.Token);

        Assert.Equal(0, result.CmaResult.GenerationsCompleted);
        // SA may have run the seeded initial candidate (one eval per
        // chain) before the cancellation check; that's fine — we just
        // assert it terminated.
        Assert.True(result.ElapsedMilliseconds >= 0);
    }

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        var obj = new ConvexObjective(3);
        Assert.Throws<ArgumentNullException>(() =>
            new HybridSACmaEsOrchestrator(null!, 10, 1, 5, 1, 0.3));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HybridSACmaEsOrchestrator(obj, 0, 1, 5, 1, 0.3));   // saMaxIterations < 1
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HybridSACmaEsOrchestrator(obj, 10, 1, 0, 1, 0.3));  // cmaMaxGenerations < 1
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HybridSACmaEsOrchestrator(obj, 10, 1, 5, 1, 0.0));  // cmaInitialSigma <= 0
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HybridSACmaEsOrchestrator(obj, 10, 1, 5, 1, 0.3, saChainCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HybridSACmaEsOrchestrator(obj, 10, 1, 5, 1, 0.3, saMigrationCadence: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HybridSACmaEsOrchestrator(obj, 10, 1, 5, 1, 0.3, sobolWarmupCount: -1));
    }

    [Fact]
    public void WinningPhase_ReportsCorrectly()
    {
        // On a simple convex problem with reasonable budgets, CMA-ES
        // should always equal-or-beat SA, so WinningPhase should be Cma.
        var obj = new ConvexObjective(dim: 4);
        var result = new HybridSACmaEsOrchestrator(
            obj, saMaxIterations: 150, saBaseSeed: 33,
            cmaMaxGenerations: 60, cmaSeed: 33, cmaInitialSigma: 0.3,
            saChainCount: 3).Run();

        Assert.Equal(HybridSACmaEsOrchestrator.Phase.Cma, result.WinningPhase);
        Assert.Equal(result.CmaBestScore, result.BestScore);
    }
}
