// AirbreathingOptimizeTests.cs — acceptance tests for the air-breathing
// pillar's multi-chain SA orchestrator (AirbreathingOptimize.Optimize).
//
// Coverage (8 tests):
//   1-2  Determinism: same seed + chain-count → bit-identical BestScore/BestParams
//   3    Turbojet feasibility: seed=42, 200 iters → ≥1 feasible candidate
//        (acceptance criterion from sub-step 1b)
//   4    Cancellation: honoured without deadlock
//   5-6  Chain count: auto (0) and explicit (2) propagate correctly
//   7    Per-chain iteration summary: all chains report > 0 iterations
//   8    WinningChain: index within valid range

using System.Threading;
using System.Threading.Tasks;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Optimization;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Tests.Optimization;

public sealed class AirbreathingOptimizeTests
{
    private static readonly FlightConditions RamjetCond =
        new(Altitude_m: 12_000.0, MachNumber: 2.0, Fuel: AirbreathingFuel.H2);

    private static readonly FlightConditions TurbojetCond =
        new(Altitude_m: 0.0, MachNumber: 0.001, Fuel: AirbreathingFuel.Jp8);

    // ── Determinism ────────────────────────────────────────────────────────────

    [Fact]
    public void Determinism_SameSeed_ProducesBitIdenticalBestScore()
    {
        var obj = RamjetObjective.WithDefaultBounds(RamjetCond);
        var r1  = AirbreathingOptimize.Optimize(obj, maxIterations: 100, baseSeed: 42, chainCount: 2);
        var r2  = AirbreathingOptimize.Optimize(obj, maxIterations: 100, baseSeed: 42, chainCount: 2);
        Assert.Equal(r1.BestScore, r2.BestScore);
    }

    [Fact]
    public void Determinism_SameSeed_ProducesBitIdenticalBestParams()
    {
        var obj = RamjetObjective.WithDefaultBounds(RamjetCond);
        var r1  = AirbreathingOptimize.Optimize(obj, maxIterations: 100, baseSeed: 42, chainCount: 2);
        var r2  = AirbreathingOptimize.Optimize(obj, maxIterations: 100, baseSeed: 42, chainCount: 2);
        Assert.Equal(r1.BestParams.Length, r2.BestParams.Length);
        for (int i = 0; i < r1.BestParams.Length; i++)
            Assert.Equal(r1.BestParams[i], r2.BestParams[i]);
    }

    // ── Feasibility acceptance (sub-step 1b) ───────────

    [Fact]
    public void Turbojet_Seed42_200Iters_AtLeastOneFeasibleCandidate()
    {
        // Acceptance criterion: turbojet feasibility envelope is wide enough
        // that 2 chains × 200 iterations reliably reaches a feasible design.
        // Score = -Isp_s on feasible; +∞ if no feasible found.
        var obj    = TurbojetObjective.WithDefaultBounds(TurbojetCond);
        var result = AirbreathingOptimize.Optimize(obj, maxIterations: 200, baseSeed: 42, chainCount: 2);
        Assert.True(
            double.IsFinite(result.BestScore) && result.BestScore < 0,
            $"Expected at least one feasible turbojet candidate (score < 0) in 200 iters. Got BestScore={result.BestScore}.");
    }

    // ── Cancellation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancellation_HonouredWithinReasonableTime()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        var obj = RamjetObjective.WithDefaultBounds(RamjetCond);

        // 50_000 iterations would run for many seconds without cancellation.
        // With cancellation, must return within 5 s.
        var task = Task.Run(() =>
            AirbreathingOptimize.Optimize(
                obj,
                maxIterations:     50_000,
                baseSeed:          42,
                chainCount:        2,
                cancellationToken: cts.Token));

        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(
            completed == task,
            "AirbreathingOptimize.Optimize did not return within 5 s after cancellation.");

        // Result should be non-null (best-so-far); no OperationCanceledException.
        var result = await task;
        Assert.NotNull(result);
        Assert.Equal(2, result.ChainCount);
    }

    // ── Chain count ────────────────────────────────────────────────────────────

    [Fact]
    public void AutoChainCount_Zero_DefaultsToAtLeastOne()
    {
        var obj    = RamjetObjective.WithDefaultBounds(RamjetCond);
        var result = AirbreathingOptimize.Optimize(obj, maxIterations: 50, baseSeed: 42, chainCount: 0);
        Assert.True(result.ChainCount >= 1,
            $"ChainCount should be ≥ 1 when chainCount=0 (auto). Got {result.ChainCount}.");
        Assert.Equal(MultiChainOptimizer.DefaultChainCount(), result.ChainCount);
    }

    [Fact]
    public void ExplicitChainCount_TwoChains_PropagatesCorrectly()
    {
        var obj    = RamjetObjective.WithDefaultBounds(RamjetCond);
        var result = AirbreathingOptimize.Optimize(obj, maxIterations: 50, baseSeed: 42, chainCount: 2);
        Assert.Equal(2, result.ChainCount);
    }

    // ── Chain summaries ────────────────────────────────────────────────────────

    [Fact]
    public void Result_Chains_AllHaveNonZeroIterations()
    {
        var obj    = RamjetObjective.WithDefaultBounds(RamjetCond);
        var result = AirbreathingOptimize.Optimize(obj, maxIterations: 100, baseSeed: 42, chainCount: 2);
        Assert.Equal(2, result.Chains.Count);
        foreach (var chain in result.Chains)
            Assert.True(chain.IterationsCompleted > 0,
                $"Chain {chain.ChainIndex} reported 0 iterations after a 100-iter run.");
    }

    [Fact]
    public void WinningChain_Index_InBoundsOf_ChainCount()
    {
        var obj    = RamjetObjective.WithDefaultBounds(RamjetCond);
        var result = AirbreathingOptimize.Optimize(obj, maxIterations: 50, baseSeed: 42, chainCount: 2);
        Assert.True(
            result.WinningChain >= 0 && result.WinningChain < result.ChainCount,
            $"WinningChain={result.WinningChain} is out of range [0, {result.ChainCount}).");
    }
}
