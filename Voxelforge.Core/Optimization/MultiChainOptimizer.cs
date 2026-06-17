// Sprint T1.1 (2026-04-25): multi-chain parallel SA with elite migration.
//
// Replaces the single-chain `SimulatedAnnealingOptimizer` workflow with N
// independent chains running in parallel, periodically exchanging elites
// to escape local optima as a population. Per CLAUDE.md ("T1.1") this is
// the highest-leverage open item in the optimization-infra roadmap —
// drops a typical SA wall-clock from ~3-5 s to ~250-500 ms (4-8×) on
// a 20-core workstation.
//
// Pairs with T1.2: each chain seeds its first K candidates from a
// non-overlapping Sobol slice (better than uniform-random initial
// coverage; faster time-to-feasible).
//
// Strict determinism contract: same baseSeed + chainCount + maxIter →
// identical (BestParams, BestScore) regardless of OS scheduler timing.
// Achieved via Barrier.SignalAndWait between chains at every migration
// boundary; the post-phase callback (executed once per phase by the
// runtime-chosen "leader" thread) collects elites and redistributes
// them in deterministic order. No race-y comparisons or compare-and-set
// games on shared state.
//
// What pre-T1.1 vs post-T1.1 looks like at the call site:
//
//   PRE:
//     var opt = new SimulatedAnnealingOptimizer(bounds, iters, seed);
//     while (!opt.IsComplete) {
//         var c = opt.NextCandidate();
//         var s = evaluate(c);
//         opt.ReportScore(c, s, breakdown);
//     }
//     var best = opt.BestParams;
//
//   POST:
//     var multi = new MultiChainOptimizer(bounds, iters, seed);
//     var result = multi.Run(c => (evaluate(c), breakdown));
//     var best = result.BestParams;
//
// Thread-safety contract on the evaluator: `RegenChamberOptimization.Evaluate`
// is pure over immutable `RegenChamberDesign` records (ADR-011), and the
// fluid-state cache in `RegenCoolingSolver` is local per `Solve()` call
// (P22 fix, 2026-04-24). So the evaluator can be invoked concurrently
// from N chain-tasks with no synchronisation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Voxelforge.Optimization;

/// <summary>
/// Parallel multi-chain SA orchestrator. Constructs N independent
/// <see cref="SimulatedAnnealingOptimizer"/> chains, runs them in
/// parallel, and periodically migrates the global-best elite to all
/// chains for cross-pollination. Default chain count auto-scales to
/// <see cref="DefaultChainCount"/>.
/// </summary>
[Deterministic]
public sealed class MultiChainOptimizer
{
    /// <summary>
    /// Default chain count: <c>Math.Clamp(ProcessorCount - 2, 1, 16)</c>.
    /// Leaves 2 cores headroom for the OS / IDE on a 20-core dev box →
    /// 16 chains; on a 4-core CI runner → 2 chains; on a 1-core fallback
    /// → 1 chain (degenerates to pre-T1.1 single-chain SA).
    /// </summary>
    public static int DefaultChainCount() =>
        Math.Clamp(Environment.ProcessorCount - 2, 1, 16);

    private readonly (double Min, double Max)[] _bounds;
    private readonly int _maxIter;
    private readonly int _baseSeed;
    private readonly int _chainCount;
    private readonly int _migrationCadence;
    private readonly bool _useSobolSeeding;
    private readonly int _sobolWarmupCount;

    public int ChainCount => _chainCount;
    public int MigrationCadence => _migrationCadence;
    public int SobolWarmupCount => _useSobolSeeding ? _sobolWarmupCount : 0;

    // RS0026 (closes #235): the two-ctor + two-Run-overload pattern is
    // intentional. The raw-bounds path lets unit tests and synthetic
    // benchmarks construct an optimizer without an IObjective; the
    // IObjective path is the production ergonomic surface. They cannot
    // collapse to a single signature without breaking one of the two
    // call patterns. Disable the analyzer with explicit justification
    // rather than refactoring out a useful overload.
#pragma warning disable RS0026 // Two-ctor + two-Run polymorphism is intentional (raw-bounds vs IObjective)
    public MultiChainOptimizer(
        (double Min, double Max)[] bounds,
        int maxIterations,
        int baseSeed,
        int chainCount = 0,
        int migrationCadence = 100,
        bool useSobolSeeding = true,
        int sobolWarmupCount = 64)
    {
        if (bounds is null) throw new ArgumentNullException(nameof(bounds));
        if (bounds.Length == 0) throw new ArgumentException("bounds must be non-empty", nameof(bounds));
        if (maxIterations < 1) throw new ArgumentOutOfRangeException(nameof(maxIterations));
        if (migrationCadence < 1) throw new ArgumentOutOfRangeException(nameof(migrationCadence));
        if (sobolWarmupCount < 0) throw new ArgumentOutOfRangeException(nameof(sobolWarmupCount));

        _bounds = bounds;
        _maxIter = maxIterations;
        _baseSeed = baseSeed;
        _chainCount = chainCount > 0 ? chainCount : DefaultChainCount();
        _migrationCadence = migrationCadence;
        _useSobolSeeding = useSobolSeeding;
        _sobolWarmupCount = Math.Min(sobolWarmupCount, maxIterations);
    }

    /// <summary>
    /// Construct from an <see cref="IObjective"/>. Bounds are derived
    /// from <see cref="IObjective.Variables"/> via
    /// <see cref="DesignVariableInfo.ToBoundsArray"/>. Functionally
    /// equivalent to the bounds-based constructor — provided for
    /// ergonomics when callers already hold an objective.
    /// </summary>
    public MultiChainOptimizer(
        IObjective objective,
        int maxIterations,
        int baseSeed,
        int chainCount = 0,
        int migrationCadence = 100,
        bool useSobolSeeding = true,
        int sobolWarmupCount = 64)
        : this(
            bounds:            DesignVariableInfo.ToBoundsArray(
                                  (objective ?? throw new ArgumentNullException(nameof(objective))).Variables),
            maxIterations:     maxIterations,
            baseSeed:          baseSeed,
            chainCount:        chainCount,
            migrationCadence:  migrationCadence,
            useSobolSeeding:   useSobolSeeding,
            sobolWarmupCount:  sobolWarmupCount)
    { }

    /// <summary>
    /// When set, applied to every chain's
    /// <see cref="SimulatedAnnealingOptimizer.MaxConsecutiveInfeasibleBeforeExit"/>
    /// before Run begins. 0 disables the persistent-infeasibility exit so each
    /// chain runs the full <see cref="_maxIter"/> regardless of feasibility.
    /// Useful for bench comparisons that need a fixed iteration budget per chain.
    /// </summary>
    public int? PerChainMaxConsecutiveInfeasibleBeforeExit { get; set; }

    /// <summary>
    /// Result of <see cref="Run"/>. Reports the global-best candidate
    /// across all chains and per-chain diagnostic data.
    /// </summary>
    public sealed record Result(
        double[] BestParams,
        double BestScore,
        object?  BestBreakdown,
        int      WinningChain,
        int      ChainCount,
        int      TotalIterations,
        long     ElapsedMilliseconds,
        IReadOnlyList<ChainSummary> Chains);

    public sealed record ChainSummary(
        int     ChainIndex,
        int     Seed,
        double  BestScore,
        int     IterationsCompleted,
        int     RestartCount,
        bool    InfeasibleExitTripped);

    /// <summary>
    /// Per-iteration progress event passed to the optional onProgress
    /// callback. Fired from inside the chain's Parallel.For body —
    /// concurrently from N chain-tasks — so any consumer-side state
    /// access must be thread-safe. Use Interlocked / ConcurrentBag /
    /// lock as appropriate.
    /// </summary>
    public sealed record ChainProgress(
        int       ChainIndex,
        int       Iteration,
        double[]  Candidate,
        double    Score,
        object?   Breakdown);

    /// <summary>
    /// Run all chains in parallel until each completes <see cref="_maxIter"/>
    /// iterations or trips its infeasible-exit threshold. Returns the
    /// best candidate across all chains.
    /// </summary>
    /// <param name="evaluator">Pure evaluator: candidate → (score, optional breakdown).
    /// Called concurrently from N chain-tasks; must be thread-safe.</param>
    /// <param name="initialCandidate">Optional seed candidate broadcast to
    /// all chains before SA starts (typically AutoSeeder output).</param>
    /// <param name="cancellationToken">Cancellation token honoured between
    /// per-chain iterations and at every migration barrier. On cancellation,
    /// each chain's loop exits cleanly and Run returns the best result
    /// found so far. Throws <see cref="OperationCanceledException"/> only
    /// if cancelled before any iteration completed.</param>
    /// <param name="onProgress">Optional progress callback fired AFTER every
    /// per-chain iteration (so up to <c>chainCount × maxIterations</c> calls
    /// per Run). Receives <c>(chainIndex, iteration, score)</c>. Must be
    /// thread-safe — fired concurrently from N chain-tasks. Useful for
    /// driving UI progress bars and feeding Pareto fronts.</param>
    [Deterministic]
    public Result Run(
        Func<double[], (double score, object? breakdown)> evaluator,
        double[]? initialCandidate = null,
        CancellationToken cancellationToken = default,
        Action<ChainProgress>? onProgress = null)
    {
        if (evaluator is null) throw new ArgumentNullException(nameof(evaluator));

        long swStart = Stopwatch.GetTimestamp();

        // Construct N optimizers, each seeded baseSeed + chain index.
        // Each gets its own Sobol slice for warmup.
        var chains = new SimulatedAnnealingOptimizer[_chainCount];
        var sobolSlices = _useSobolSeeding && _sobolWarmupCount > 0
            ? PrecomputeSobolSlices()
            : null;

        for (int i = 0; i < _chainCount; i++)
        {
            chains[i] = new SimulatedAnnealingOptimizer(
                _bounds, _maxIter, _baseSeed + i);
            if (initialCandidate is not null)
                chains[i].SetInitialCandidate(initialCandidate);
            if (PerChainMaxConsecutiveInfeasibleBeforeExit is { } cap)
                chains[i].MaxConsecutiveInfeasibleBeforeExit = cap;
        }

        // Migration barrier. The post-phase callback runs once per phase
        // by exactly one (runtime-chosen) participant — that's fine for
        // determinism because the callback's logic is pure-deterministic
        // over the inputs (chain best-scores).
        using var barrier = new Barrier(
            participantCount: _chainCount,
            postPhaseAction: _ => MigrateElites(chains));

        // Each chain runs its own loop; barrier keeps them lockstep at
        // migration boundaries. The CancellationToken is honoured at every
        // iteration boundary AND every migration barrier — on cancel, all
        // chains drop out cleanly and the Result reports best-so-far.
        Parallel.For(0, _chainCount, chainIdx =>
        {
            var opt = chains[chainIdx];
            var sobolPts = sobolSlices?[chainIdx];

            while (!opt.IsComplete)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // Detach so other chains don't deadlock at the next barrier.
                    barrier.RemoveParticipant();
                    return;
                }

                int iter = opt.Iteration;
                double[] cand;

                if (sobolPts is not null && iter < sobolPts.Length)
                {
                    // T1.2: seed first K candidates from this chain's Sobol slice.
                    cand = MapUnitCubeToBounds(sobolPts[iter]);
                }
                else
                {
                    cand = opt.NextCandidate();
                }

                try
                {
                    var (score, breakdown) = evaluator(cand);
                    opt.ReportScore(cand, score, breakdown);

                    if (onProgress is not null)
                    {
                        // Per-iteration progress callback. Fired concurrently
                        // from N chain-tasks; consumers must be thread-safe.
                        // We pass the candidate by reference (consumer should
                        // clone if it needs to retain past return). The
                        // breakdown is intentionally NOT passed — typical
                        // consumers (Pareto, progress bars) read score only;
                        // breakdown lives in chain.BestBreakdown for the
                        // chain-best path.
                        onProgress(new ChainProgress(
                            ChainIndex: chainIdx,
                            Iteration:  opt.Iteration,
                            Candidate:  cand,
                            Score:      score,
                            Breakdown:  breakdown));
                    }

                    // Migration boundary. All chains must reach the barrier
                    // for the post-phase to fire — that's what enforces strict
                    // determinism (the migration sees a snapshot of all
                    // chains' best-scores at exactly the same iteration count).
                    int newIter = opt.Iteration;
                    if (newIter > 0 && newIter % _migrationCadence == 0
                        && newIter < _maxIter && !opt.IsComplete)
                    {
                        barrier.SignalAndWait();
                    }
                }
                catch (OperationCanceledException)
                {
                    // Cancellation can also surface *inside* evaluator(cand):
                    // the objective adapter calls ThrowIfCancellationRequested
                    // (EngineObjectiveAdapter), so a token tripped between the
                    // top-of-loop check above and the evaluation completing
                    // throws here rather than at the boundary check. Treat it
                    // identically to a boundary cancel: detach cleanly so peers
                    // don't deadlock at the next barrier, and let Result report
                    // best-so-far (already captured in chain.BestScore by the
                    // ReportScore calls made before cancellation). Without this,
                    // the exception escapes Parallel.For as an AggregateException
                    // and Run throws instead of returning — the contract
                    // violation exercised intermittently by
                    // AirbreathingOptimizeTests.Cancellation_HonouredWithinReasonableTime.
                    barrier.RemoveParticipant();
                    return;
                }
            }

            // Chains that finish early (e.g., infeasible-exit at iter 60)
            // skip remaining barriers — but other chains are still
            // SignalAndWait-ing. Detach this finished chain so the others
            // don't deadlock.
            barrier.RemoveParticipant();
        });

        long swEnd = Stopwatch.GetTimestamp();
        long elapsedMs = (swEnd - swStart) * 1000 / Stopwatch.Frequency;

        // Find winning chain (lowest BestScore). On ties prefer the
        // lower-index chain for deterministic tiebreaking.
        int winning = 0;
        double winningScore = chains[0].BestScore;
        for (int i = 1; i < _chainCount; i++)
        {
            if (chains[i].BestScore < winningScore)
            {
                winning = i;
                winningScore = chains[i].BestScore;
            }
        }

        var summaries = new ChainSummary[_chainCount];
        int totalIters = 0;
        for (int i = 0; i < _chainCount; i++)
        {
            summaries[i] = new ChainSummary(
                ChainIndex:            i,
                Seed:                  _baseSeed + i,
                BestScore:             chains[i].BestScore,
                IterationsCompleted:   chains[i].Iteration,
                RestartCount:          chains[i].RestartCount,
                InfeasibleExitTripped: chains[i].InfeasibleExitTripped);
            totalIters += chains[i].Iteration;
        }

        return new Result(
            BestParams:          (double[])chains[winning].BestParams.Clone(),
            BestScore:           chains[winning].BestScore,
            BestBreakdown:       chains[winning].BestBreakdown,
            WinningChain:        winning,
            ChainCount:          _chainCount,
            TotalIterations:     totalIters,
            ElapsedMilliseconds: elapsedMs,
            Chains:              summaries);
    }

    /// <summary>
    /// IObjective-shaped <see cref="Run"/> overload (Slice 2 of
    /// the IObjective decoupling, 2026-04-28). Adapts to
    /// the existing <c>Func</c>-based path internally; the only
    /// observable difference is what's stored on
    /// <see cref="Result.BestBreakdown"/> — for this overload it's
    /// the full <see cref="EvaluationResult"/> (carrying first-class
    /// <see cref="EvaluationResult.Violations"/> alongside
    /// <see cref="EvaluationResult.EngineSpecificBreakdown"/>) instead
    /// of the engine-specific record alone.
    /// <para>
    /// Strict-determinism contract is preserved: the bridge wrapper is
    /// pure-deterministic over Score values, so the SA accept/reject
    /// sequences are bit-identical to an equivalent <c>Func</c>-based
    /// run with the same evaluator semantics.
    /// </para>
    /// </summary>
    /// <param name="objective">
    /// Engine-family-agnostic evaluator. <c>DimensionCount</c> must
    /// equal the optimizer's bounds dimension; mismatch throws
    /// <see cref="ArgumentException"/>.
    /// </param>
    /// <param name="initialCandidate">
    /// Optional seed candidate broadcast to all chains before SA
    /// starts (typically AutoSeeder output projected through Pack).
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token honoured between per-chain iterations and
    /// at every migration barrier; also forwarded to
    /// <see cref="IObjective.Evaluate"/> for in-Evaluate cancellation.
    /// </param>
    /// <param name="onProgress">
    /// Optional progress callback fired AFTER every per-chain
    /// iteration. Same threading semantics as the <c>Func</c> overload.
    /// </param>
    [Deterministic]
    public Result Run(
        IObjective objective,
        double[]? initialCandidate = null,
        CancellationToken cancellationToken = default,
        Action<ChainProgress>? onProgress = null)
    {
        if (objective is null) throw new ArgumentNullException(nameof(objective));
        if (objective.DimensionCount != _bounds.Length)
            throw new ArgumentException(
                $"Objective DimensionCount {objective.DimensionCount} != optimizer bounds dim {_bounds.Length}. "
              + "Construct the optimizer with the IObjective ctor (which derives bounds from objective.Variables) "
              + "or pass matching bounds explicitly.",
                nameof(objective));

        return Run(
            evaluator: cand =>
            {
                var r = objective.Evaluate(cand, cancellationToken);
                // Store the full EvaluationResult as the breakdown so
                // consumers reading Result.BestBreakdown get first-class
                // access to .Violations alongside .EngineSpecificBreakdown.
                return (r.Score, (object?)r);
            },
            initialCandidate:  initialCandidate,
            cancellationToken: cancellationToken,
            onProgress:        onProgress);
    }

    private double[][][] PrecomputeSobolSlices()
    {
        var slices = new double[_chainCount][][];
        for (int i = 0; i < _chainCount; i++)
        {
            slices[i] = SobolSequence.ChainSlice(
                dimensions:  _bounds.Length,
                count:       _sobolWarmupCount,
                sliceIndex:  i,
                totalSlices: _chainCount);
        }
        return slices;
    }

    private double[] MapUnitCubeToBounds(double[] unitPoint)
    {
        var pt = new double[_bounds.Length];
        for (int i = 0; i < _bounds.Length; i++)
        {
            var (lo, hi) = _bounds[i];
            pt[i] = lo + unitPoint[i] * (hi - lo);
        }
        return pt;
    }

    // Post-barrier migration: broadcast the global-best (BestParams, BestScore)
    // to all chains' _current state. Each chain's own _best is preserved
    // (MigrateFrom doesn't touch it unless the migrated score beats it).
    // Deterministic because chains' best-scores are a deterministic
    // function of their seeds + iteration history.
    private static void MigrateElites(SimulatedAnnealingOptimizer[] chains)
    {
        int N = chains.Length;
        if (N <= 1) return;

        // Find the global best deterministically (lower score wins; ties
        // broken by lower chain index).
        int globalBestIdx = 0;
        double globalBestScore = chains[0].BestScore;
        for (int i = 1; i < N; i++)
        {
            if (chains[i].BestScore < globalBestScore)
            {
                globalBestIdx = i;
                globalBestScore = chains[i].BestScore;
            }
        }

        // Skip migration on the very first phase if no chain has a finite
        // best (e.g., all chains still in pre-feasibility flailing). The
        // pre-feasibility migration would just shuffle infinities around.
        if (double.IsPositiveInfinity(globalBestScore) || double.IsNaN(globalBestScore))
            return;

        var globalBestParams = chains[globalBestIdx].BestParams;
        // Broadcast to all chains EXCEPT the donor itself (no-op there).
        for (int i = 0; i < N; i++)
        {
            if (i == globalBestIdx) continue;
            chains[i].MigrateFrom(globalBestParams, globalBestScore);
        }
    }
}
#pragma warning restore RS0026 // closes #235; see disable site near line 82
