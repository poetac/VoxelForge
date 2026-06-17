// HybridSACmaEsOrchestrator.cs — Issue #210 (T1.3-followon).
//
// Two-phase global+local optimizer: multi-chain Simulated Annealing for
// global exploration of the full design vector, then CMA-ES seeded from
// the SA winner for continuous local refinement. Pairs the strengths of
// both algorithms — SA's basin-hopping + categorical-friendly perturbation
// outer with CMA-ES's gradient-free continuous-polish inner.
//
// Per the issue: same (saBaseSeed, saChainCount, cmaSeed) → bit-identical
// (BestParams, BestScore). The two phases are sequenced strictly (SA
// completes before CMA-ES starts), so determinism is the conjunction of
// each phase's individual determinism contract.
//
// Plug-in clean: consumes IObjective only. No rocket-shaped record visible.

using System;
using System.Diagnostics;
using System.Threading;

namespace Voxelforge.Optimization;

/// <summary>
/// Hybrid Simulated Annealing (outer) + CMA-ES (inner) orchestrator.
/// Phase 1 runs <see cref="MultiChainOptimizer"/> over the full design
/// vector for global search; Phase 2 seeds <see cref="CmaEsOptimizer"/>
/// from the SA winner for continuous local refinement. Returns the
/// better of the two phases.
/// </summary>
public sealed class HybridSACmaEsOrchestrator
{
    /// <summary>
    /// Which phase produced the final reported best.
    /// <see cref="Cma"/> indicates CMA-ES improved over SA (the typical
    /// outcome when both phases run); <see cref="Sa"/> indicates SA's
    /// best was already at or below CMA-ES's reach (rare but possible
    /// e.g. when the CMA budget is small relative to the basin size).
    /// </summary>
    public enum Phase { Sa, Cma }

    /// <summary>
    /// Final result. <see cref="BestParams"/> + <see cref="BestScore"/>
    /// are the global winner across both phases. Per-phase artefacts are
    /// kept in <see cref="SaResult"/> + <see cref="CmaResult"/> for
    /// downstream diagnostics + tests.
    /// </summary>
    public sealed record Result(
        double[]                    BestParams,
        double                      BestScore,
        EvaluationResult?           BestEvaluation,
        Phase                       WinningPhase,
        double                      SaBestScore,
        double                      CmaBestScore,
        long                        ElapsedMilliseconds,
        MultiChainOptimizer.Result  SaResult,
        CmaEsOptimizer.Result       CmaResult);

    private readonly IObjective _objective;
    private readonly int        _saMaxIterations;
    private readonly int        _saBaseSeed;
    private readonly int        _saChainCount;
    private readonly int        _saMigrationCadence;
    private readonly bool       _useSobolSeeding;
    private readonly int        _sobolWarmupCount;
    private readonly int        _cmaMaxGenerations;
    private readonly int        _cmaSeed;
    private readonly double     _cmaInitialSigma;
    private readonly int?       _cmaLambdaOverride;

    /// <summary>
    /// Construct a hybrid SA + CMA-ES orchestrator.
    /// </summary>
    /// <param name="objective">Engine-family-agnostic oracle. Drives both phases.</param>
    /// <param name="saMaxIterations">Per-chain iteration budget for the SA phase.</param>
    /// <param name="saBaseSeed">Seed for SA chain 0. Chain i seeds from <c>saBaseSeed + i</c>.</param>
    /// <param name="cmaMaxGenerations">Generation budget for CMA-ES phase.</param>
    /// <param name="cmaSeed">Independent seed for CMA-ES. Different from <paramref name="saBaseSeed"/> by convention so the two phases explore independently.</param>
    /// <param name="cmaInitialSigma">CMA-ES initial step-size σ. Reasonable default: ~0.1× the smaller side of each variable's [Min, Max] band — narrow because the SA winner is already in a good basin.</param>
    /// <param name="saChainCount">SA chain count. <c>0</c> auto-scales to <see cref="MultiChainOptimizer.DefaultChainCount"/>.</param>
    /// <param name="saMigrationCadence">Iterations between SA elite-migration barriers.</param>
    /// <param name="useSobolSeeding">Whether SA seeds initial candidates from per-chain Sobol slices.</param>
    /// <param name="sobolWarmupCount">Number of Sobol-seeded SA candidates per chain.</param>
    /// <param name="cmaLambdaOverride">Optional CMA-ES population override.</param>
    public HybridSACmaEsOrchestrator(
        IObjective objective,
        int        saMaxIterations,
        int        saBaseSeed,
        int        cmaMaxGenerations,
        int        cmaSeed,
        double     cmaInitialSigma,
        int        saChainCount        = 0,
        int        saMigrationCadence  = 100,
        bool       useSobolSeeding     = true,
        int        sobolWarmupCount    = 64,
        int?       cmaLambdaOverride   = null)
    {
        if (objective is null) throw new ArgumentNullException(nameof(objective));
        if (saMaxIterations < 1) throw new ArgumentOutOfRangeException(nameof(saMaxIterations));
        if (cmaMaxGenerations < 1) throw new ArgumentOutOfRangeException(nameof(cmaMaxGenerations));
        if (cmaInitialSigma <= 0) throw new ArgumentOutOfRangeException(nameof(cmaInitialSigma));
        if (saChainCount < 0) throw new ArgumentOutOfRangeException(nameof(saChainCount));
        if (saMigrationCadence < 1) throw new ArgumentOutOfRangeException(nameof(saMigrationCadence));
        if (sobolWarmupCount < 0) throw new ArgumentOutOfRangeException(nameof(sobolWarmupCount));

        _objective          = objective;
        _saMaxIterations    = saMaxIterations;
        _saBaseSeed         = saBaseSeed;
        _saChainCount       = saChainCount;
        _saMigrationCadence = saMigrationCadence;
        _useSobolSeeding    = useSobolSeeding;
        _sobolWarmupCount   = sobolWarmupCount;
        _cmaMaxGenerations  = cmaMaxGenerations;
        _cmaSeed            = cmaSeed;
        _cmaInitialSigma    = cmaInitialSigma;
        _cmaLambdaOverride  = cmaLambdaOverride;
    }

    /// <summary>
    /// Run both phases sequentially. Cancellation honoured at each
    /// phase boundary and inside each phase's own loop. If cancelled
    /// before SA completes, CMA-ES still runs (typically zero generations
    /// because the cancelled token is forwarded), and the orchestrator
    /// returns SA's best.
    /// </summary>
    public Result Run(CancellationToken cancellationToken = default)
    {
        long swStart = Stopwatch.GetTimestamp();

        // Phase 1: multi-chain SA over the full design vector.
        var sa = new MultiChainOptimizer(
            objective:         _objective,
            maxIterations:     _saMaxIterations,
            baseSeed:          _saBaseSeed,
            chainCount:        _saChainCount,
            migrationCadence:  _saMigrationCadence,
            useSobolSeeding:   _useSobolSeeding,
            sobolWarmupCount:  _sobolWarmupCount);
        var saResult = sa.Run(_objective, cancellationToken: cancellationToken);

        // Phase 2: CMA-ES seeded from SA winner. Initial mean is the
        // SA-best vector; reflection-at-bound (PR #245) keeps the
        // sampling inside DesignVariableInfo.{Min, Max} even if σ is
        // wide relative to the band.
        var cma = new CmaEsOptimizer(
            objective:        _objective,
            initialMean:      (double[])saResult.BestParams.Clone(),
            initialSigma:     _cmaInitialSigma,
            maxGenerations:   _cmaMaxGenerations,
            seed:             _cmaSeed,
            lambdaOverride:   _cmaLambdaOverride);
        var cmaResult = cma.Run(cancellationToken);

        long swEnd = Stopwatch.GetTimestamp();
        long elapsedMs = (swEnd - swStart) * 1000 / Stopwatch.Frequency;

        // Pick the global best. CMA-ES wins ties because it's the
        // refined phase — there's no point reporting SA's parent vector
        // when CMA-ES has matched it.
        bool cmaWins = cmaResult.BestScore <= saResult.BestScore;

        double[] bestParams;
        double bestScore;
        EvaluationResult? bestEval;
        Phase winningPhase;

        if (cmaWins)
        {
            bestParams   = (double[])cmaResult.BestParams.Clone();
            bestScore    = cmaResult.BestScore;
            bestEval     = cmaResult.BestEvaluation;
            winningPhase = Phase.Cma;
        }
        else
        {
            bestParams   = (double[])saResult.BestParams.Clone();
            bestScore    = saResult.BestScore;
            // MultiChainOptimizer.Run(IObjective, ...) stores the full
            // EvaluationResult as BestBreakdown; cast back through.
            // EvaluationResult is a struct, so cast through Nullable<T>
            // (saResult.BestBreakdown may be a non-EvaluationResult object
            // or null when the chain produced no feasible candidate).
            bestEval     = saResult.BestBreakdown as EvaluationResult?;
            winningPhase = Phase.Sa;
        }

        return new Result(
            BestParams:           bestParams,
            BestScore:            bestScore,
            BestEvaluation:       bestEval,
            WinningPhase:         winningPhase,
            SaBestScore:          saResult.BestScore,
            CmaBestScore:         cmaResult.BestScore,
            ElapsedMilliseconds:  elapsedMs,
            SaResult:             saResult,
            CmaResult:            cmaResult);
    }
}
