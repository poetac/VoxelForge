// AirbreathingOptimize.cs — multi-chain SA orchestrator for the air-breathing
// pillar. Mirrors the rocket-side MultiChainOptimizer wiring pattern but on
// the engine-family-agnostic IObjective surface (per architecture-greenfield-
// memo.md rec #4, shipped 2026-04-28 via PR #155).
//
// Design note: this file is intentionally thin. All optimizer logic
// (Sobol seeding, barrier migration, infeasible-exit) lives in
// MultiChainOptimizer; this class is solely the air-breathing pillar's
// entry point so callers don't need to construct the optimizer directly.
//
// Consumed by:
//   - Voxelforge.Airbreathing.Tests/Optimization/AirbreathingOptimizeTests.cs
//   - Voxelforge.Benchmarks/BenchSaAirbreathing.cs (uses
//     MultiChainOptimizer directly for per-iteration timing access)

using System;
using System.Threading;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Optimization;

/// <summary>
/// Top-level SA orchestration entry point for the air-breathing pillar.
/// Drives <see cref="MultiChainOptimizer"/> on any
/// <see cref="IObjective"/> — typically <see cref="RamjetObjective"/> or
/// <see cref="TurbojetObjective"/>.
/// </summary>
public static class AirbreathingOptimize
{
    /// <summary>
    /// Run multi-chain simulated annealing on <paramref name="objective"/>.
    /// </summary>
    /// <param name="objective">
    /// Engine-family-agnostic objective. Must be thread-safe;
    /// <see cref="MultiChainOptimizer"/> calls
    /// <see cref="IObjective.Evaluate"/> concurrently from N chain-tasks.
    /// </param>
    /// <param name="maxIterations">
    /// Per-chain iteration budget (default 500; use ≥ 200 for
    /// turbojet feasibility, ≥ 1000 for production quality).
    /// </param>
    /// <param name="baseSeed">
    /// Base RNG seed. Same seed + chain-count + max-iterations always
    /// produces bit-identical <c>BestParams</c> / <c>BestScore</c>.
    /// </param>
    /// <param name="chainCount">
    /// Number of parallel SA chains. 0 = auto-scale to
    /// <see cref="MultiChainOptimizer.DefaultChainCount()"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Honoured at chain-boundary barriers; returns best-so-far on cancel.
    /// </param>
    /// <param name="onProgress">
    /// Optional per-candidate progress callback (fired from chain tasks;
    /// must be thread-safe).
    /// </param>
    /// <returns>
    /// <see cref="MultiChainOptimizer.Result"/> with
    /// <c>BestParams</c> (design vector), <c>BestScore</c> (−Isp on
    /// feasible; <see cref="double.PositiveInfinity"/> if no feasible
    /// candidate found), and per-chain history.
    /// </returns>
    public static MultiChainOptimizer.Result Optimize(
        IObjective objective,
        int maxIterations = 500,
        int baseSeed = 42,
        int chainCount = 0,
        CancellationToken cancellationToken = default,
        Action<MultiChainOptimizer.ChainProgress>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(objective);
        var optimizer = new MultiChainOptimizer(
            objective:     objective,
            maxIterations: maxIterations,
            baseSeed:      baseSeed,
            chainCount:    chainCount);
        return optimizer.Run(
            objective:         objective,
            cancellationToken: cancellationToken,
            onProgress:        onProgress);
    }
}
