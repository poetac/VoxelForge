// BayesianOptimizer.cs — Issue #199 (OOB-4 Bayesian optimization).
//
// Sequential model-based optimization (SMBO) over an IObjective using a
// GP surrogate (RBF kernel + ARD) and Expected-Improvement acquisition.
//
//   1. Initial design: K Sobol points → evaluate via objective → fit GP
//   2. Per iteration:
//      a. Acquisition optimization (random multistart over Sobol seeds)
//      b. Evaluate selected candidate
//      c. Refit GP on the augmented training set
//      d. Track best
//   3. Return best
//
// Plug-in clean on the IObjective interface (PR #155). No rocket-shaped
// record visible. Designed as the next gradient-free optimizer in the
// family alongside CMA-ES (PR #173) and NSGA-II (PR #174); same outer
// shape (constructor + Run + Result record + IterationRecord history).
//
// Acquisition functions:
//   • ExpectedImprovement: standard EI, maximised. Best general default.
//     EI(x) = (f_min - μ - ξ) · Φ(z) + σ · φ(z)   where z = (f_min - μ - ξ) / σ
//   • LowerConfidenceBound: μ - β · σ, minimised. Better when calibration
//     of σ is trustworthy and exploration trade-off is set externally.
//
// Hyperparameters (length scales, signal variance, noise variance) are
// caller-provided; automatic MLE-fit / hyperparameter tuning is left as
// a follow-up. The MVP target is a working sequential BO that beats
// uniform-random sampling on the canonical convergence benchmarks.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Voxelforge.Optimization.Bayesian;

/// <summary>
/// Bayesian optimization of an <see cref="IObjective"/> using a Gaussian-
/// Process surrogate and Expected-Improvement (or LCB) acquisition.
/// Same plug-in contract as <see cref="CmaEsOptimizer"/> /
/// <see cref="NsgaIIOptimizer"/>.
/// </summary>
[Deterministic]
public sealed class BayesianOptimizer
{
    /// <summary>
    /// Acquisition criterion to drive the next-evaluation choice.
    /// </summary>
    public enum AcquisitionFunction
    {
        /// <summary>
        /// Standard Expected Improvement, maximised. Good general default;
        /// well-balanced exploration/exploitation for smooth surfaces.
        /// </summary>
        ExpectedImprovement,
        /// <summary>
        /// Lower Confidence Bound (μ - β·σ), minimised. Cleaner asymptotic
        /// behaviour but sensitive to σ calibration; tune <c>ucbBeta</c>.
        /// </summary>
        LowerConfidenceBound
    }

    /// <summary>
    /// Per-iteration diagnostic record.
    /// </summary>
    public sealed record IterationRecord(
        int    Iteration,
        double BestScore,
        double SelectedAcquisition,
        double SelectedMean,
        double SelectedStd,
        double SelectedScore,
        long   TotalEvaluations);

    /// <summary>
    /// Final result.
    /// </summary>
    public sealed record Result(
        double[]                       BestParams,
        double                         BestScore,
        EvaluationResult?              BestEvaluation,
        int                            IterationsCompleted,
        long                           TotalEvaluations,
        long                           ElapsedMilliseconds,
        IReadOnlyList<IterationRecord> History);

    private readonly IObjective _objective;
    private readonly int        _dim;
    private readonly (double Min, double Max)[] _bounds;
    private readonly int        _initialDesignSize;
    private readonly int        _maxIterations;
    private readonly Random     _rng;
    private readonly double[]   _lengthScales;
    private readonly double     _signalVariance;
    private readonly double     _noiseVariance;
    private readonly AcquisitionFunction _acquisition;
    private readonly double     _eiXi;
    private readonly double     _ucbBeta;
    private readonly int        _acquisitionCandidates;

    // Phase 2 of #627 (tracked under #743). When true, infeasible
    // candidates' +∞ scores are replaced by SoftPenalty.Compute(eval) —
    // a sigmoid-saturated soft penalty (penalty_scale · Σ tanh(|SignedBreachMagnitude| / scale)).
    private readonly bool       _useSoftPenalty;

    /// <summary>
    /// Construct a Bayesian optimizer.
    /// </summary>
    /// <param name="objective">Engine-family-agnostic oracle.</param>
    /// <param name="initialDesignSize">Number of Sobol-seeded points
    /// evaluated before the BO loop starts. Common rule of thumb:
    /// <c>2·dim + 5</c> or larger. Must be ≥ 1.</param>
    /// <param name="maxIterations">Number of BO iterations after the
    /// initial design (i.e., total evaluations = <c>initialDesignSize +
    /// maxIterations</c>).</param>
    /// <param name="seed">RNG + Sobol seed for reproducibility.</param>
    /// <param name="lengthScale">Isotropic length scale, expressed as
    /// fraction of the [Min, Max] band per dim. Common default:
    /// <c>0.2</c> (kernel decays to ~37 % at 20 % of band).</param>
    /// <param name="signalVariance">σ_f² for the GP kernel.</param>
    /// <param name="noiseVariance">σ_n² noise jitter for numerical
    /// stability.</param>
    /// <param name="acquisition">EI or LCB acquisition criterion.</param>
    /// <param name="explorationXi">EI exploration trade-off (added to
    /// f_min before computing the improvement). Larger ⇒ more
    /// exploration.</param>
    /// <param name="ucbBeta">LCB exploration coefficient. Larger ⇒ more
    /// exploration (UCB minimises μ - β·σ).</param>
    /// <param name="acquisitionCandidates">Number of candidate points
    /// drawn (Sobol-seeded) per iteration over which the acquisition is
    /// maximised. Larger ⇒ better local optimum of acquisition at the
    /// cost of CPU.</param>
    public BayesianOptimizer(
        IObjective objective,
        int        initialDesignSize,
        int        maxIterations,
        int        seed                    = 42,
        double     lengthScale             = 0.2,
        double     signalVariance          = 1.0,
        double     noiseVariance           = 1e-6,
        AcquisitionFunction acquisition    = AcquisitionFunction.ExpectedImprovement,
        double     explorationXi           = 0.01,
        double     ucbBeta                 = 2.0,
        int        acquisitionCandidates   = 1024,
        bool       useSoftPenalty          = false)
    {
        if (objective is null) throw new ArgumentNullException(nameof(objective));
        if (initialDesignSize < 1) throw new ArgumentOutOfRangeException(nameof(initialDesignSize));
        if (maxIterations < 1) throw new ArgumentOutOfRangeException(nameof(maxIterations));
        if (lengthScale <= 0) throw new ArgumentOutOfRangeException(nameof(lengthScale));
        if (signalVariance <= 0) throw new ArgumentOutOfRangeException(nameof(signalVariance));
        if (noiseVariance < 0) throw new ArgumentOutOfRangeException(nameof(noiseVariance));
        if (ucbBeta < 0) throw new ArgumentOutOfRangeException(nameof(ucbBeta));
        if (acquisitionCandidates < 1) throw new ArgumentOutOfRangeException(nameof(acquisitionCandidates));

        _objective = objective;
        _dim = objective.DimensionCount;
        _bounds = DesignVariableInfo.ToBoundsArray(objective.Variables);
        _initialDesignSize = initialDesignSize;
        _maxIterations = maxIterations;
        _rng = new Random(seed);
        _lengthScales = new double[_dim];
        for (int i = 0; i < _dim; i++) _lengthScales[i] = lengthScale;
        _signalVariance = signalVariance;
        _noiseVariance = noiseVariance;
        _acquisition = acquisition;
        _eiXi = explorationXi;
        _ucbBeta = ucbBeta;
        _acquisitionCandidates = acquisitionCandidates;
        _useSoftPenalty = useSoftPenalty;
    }

    /// <summary>
    /// Run the BO loop until <paramref name="cancellationToken"/> fires
    /// or the iteration budget is exhausted.
    /// </summary>
    [Deterministic]
    public Result Run(CancellationToken cancellationToken = default)
    {
        long swStart = Stopwatch.GetTimestamp();
        long evaluations = 0;
        var history = new List<IterationRecord>(_maxIterations);

        // Pre-cancelled token: bail with empty result.
        if (cancellationToken.IsCancellationRequested)
        {
            return new Result(
                BestParams:           new double[_dim],
                BestScore:            double.PositiveInfinity,
                BestEvaluation:       null,
                IterationsCompleted:  0,
                TotalEvaluations:     0,
                ElapsedMilliseconds:  0,
                History:              history);
        }

        // Phase 1: initial design via Sobol. Each Sobol point is in
        // [0, 1)^D; scale to the variable bounds.
        var sobol = new SobolSequence(_dim);
        var X = new List<double[]>(_initialDesignSize + _maxIterations);
        var y = new List<double>(_initialDesignSize + _maxIterations);

        double[] bestParams = new double[_dim];
        double bestScore = double.PositiveInfinity;
        EvaluationResult? bestEval = null;

        for (int i = 0; i < _initialDesignSize; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var raw = sobol.Next();
            var pt = new double[_dim];
            for (int d = 0; d < _dim; d++)
                pt[d] = _bounds[d].Min + raw[d] * (_bounds[d].Max - _bounds[d].Min);
            var eval = _objective.Evaluate(pt, cancellationToken);
            double score = _useSoftPenalty ? SoftPenalty.Compute(eval) : eval.Score;
            evaluations++;
            X.Add(pt);
            y.Add(score);
            if (!double.IsNaN(score) && score < bestScore)
            {
                bestScore = score;
                bestParams = (double[])pt.Clone();
                bestEval = eval;
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            long swEndEarly = Stopwatch.GetTimestamp();
            return new Result(
                BestParams:           bestParams,
                BestScore:            bestScore,
                BestEvaluation:       bestEval,
                IterationsCompleted:  0,
                TotalEvaluations:     evaluations,
                ElapsedMilliseconds:  (swEndEarly - swStart) * 1000 / Stopwatch.Frequency,
                History:              history);
        }

        // Phase 2: BO loop. Refit the GP each iteration with the
        // augmented training set (cheaper-fit alternative: rank-1 update,
        // not implemented in MVP — N is small enough for full refits).
        var gp = new GaussianProcessSurrogate(
            bounds:         _bounds,
            lengthScales:   _lengthScales,
            signalVariance: _signalVariance,
            noiseVariance:  _noiseVariance);

        for (int iter = 0; iter < _maxIterations; iter++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Refit GP on current training set.
            gp.Fit(X.ToArray(), y.ToArray());

            // Optimize acquisition: draw _acquisitionCandidates Sobol points
            // (deterministic, low-discrepancy) and pick the one that
            // best satisfies the criterion. EI is maximised, LCB minimised.
            double bestAcq = (_acquisition == AcquisitionFunction.ExpectedImprovement)
                ? double.NegativeInfinity
                : double.PositiveInfinity;
            double[] bestCand = new double[_dim];
            double bestCandMean = 0.0;
            double bestCandStd  = 0.0;

            for (int c = 0; c < _acquisitionCandidates; c++)
            {
                var raw = sobol.Next();
                var cand = new double[_dim];
                for (int d = 0; d < _dim; d++)
                    cand[d] = _bounds[d].Min + raw[d] * (_bounds[d].Max - _bounds[d].Min);
                var (mean, variance) = gp.Predict(cand);
                double std = Math.Sqrt(variance);

                double a = _acquisition switch
                {
                    AcquisitionFunction.ExpectedImprovement
                        => ExpectedImprovement(mean, std, bestScore, _eiXi),
                    AcquisitionFunction.LowerConfidenceBound
                        => LowerConfidenceBound(mean, std, _ucbBeta),
                    _ => throw new InvalidOperationException("Unknown acquisition function")
                };

                bool improves = _acquisition == AcquisitionFunction.ExpectedImprovement
                    ? a > bestAcq
                    : a < bestAcq;
                if (improves)
                {
                    bestAcq = a;
                    bestCand = cand;
                    bestCandMean = mean;
                    bestCandStd  = std;
                }
            }

            // Evaluate the chosen candidate.
            var ev = _objective.Evaluate(bestCand, cancellationToken);
            double evScore = _useSoftPenalty ? SoftPenalty.Compute(ev) : ev.Score;
            evaluations++;
            X.Add(bestCand);
            y.Add(evScore);

            if (!double.IsNaN(evScore) && evScore < bestScore)
            {
                bestScore = evScore;
                bestParams = (double[])bestCand.Clone();
                bestEval = ev;
            }

            history.Add(new IterationRecord(
                Iteration:           iter,
                BestScore:           bestScore,
                SelectedAcquisition: bestAcq,
                SelectedMean:        bestCandMean,
                SelectedStd:         bestCandStd,
                SelectedScore:       evScore,
                TotalEvaluations:    evaluations));
        }

        long swEnd = Stopwatch.GetTimestamp();
        long elapsedMs = (swEnd - swStart) * 1000 / Stopwatch.Frequency;

        return new Result(
            BestParams:           bestParams,
            BestScore:            bestScore,
            BestEvaluation:       bestEval,
            IterationsCompleted:  history.Count,
            TotalEvaluations:     evaluations,
            ElapsedMilliseconds:  elapsedMs,
            History:              history);
    }

    /// <summary>
    /// Expected Improvement for minimization:
    /// <c>EI(x) = (f_min - μ - ξ) · Φ(z) + σ · φ(z)</c> where
    /// <c>z = (f_min - μ - ξ) / σ</c>, clamped to 0 when σ = 0 or when
    /// f_min is +Infinity (no feasible point seen yet — return σ as a
    /// crude exploration proxy so non-trivial draws can still happen).
    /// </summary>
    internal static double ExpectedImprovement(double mean, double std, double fMin, double xi)
    {
        if (std <= 0.0) return 0.0;
        if (double.IsPositiveInfinity(fMin))
        {
            // No feasible point: drive toward high-uncertainty regions.
            return std;
        }
        double improvement = fMin - mean - xi;
        double z = improvement / std;
        double phi = StandardNormalPdf(z);
        double Phi = StandardNormalCdf(z);
        return improvement * Phi + std * phi;
    }

    /// <summary>
    /// Lower Confidence Bound for minimization: <c>μ - β · σ</c>.
    /// </summary>
    internal static double LowerConfidenceBound(double mean, double std, double beta)
        => mean - beta * std;

    private static double StandardNormalPdf(double z)
        => Math.Exp(-0.5 * z * z) / Math.Sqrt(2.0 * Math.PI);

    private static double StandardNormalCdf(double z)
        => 0.5 * (1.0 + Erf(z / Math.Sqrt(2.0)));

    /// <summary>
    /// Abramowitz &amp; Stegun 7.1.26 polynomial approximation of erf
    /// (max abs error 1.5e-7). Used in the standard-normal CDF for EI;
    /// .NET 9's <c>double.Erf</c> would also work but the polynomial
    /// stays portable + obvious in source.
    /// </summary>
    private static double Erf(double x)
    {
        const double p  = 0.3275911;
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        int sign = x < 0 ? -1 : 1;
        x = Math.Abs(x);
        double t = 1.0 / (1.0 + p * x);
        double poly = a1 * t + a2 * t * t + a3 * t * t * t + a4 * t * t * t * t + a5 * t * t * t * t * t;
        double y = 1.0 - poly * Math.Exp(-x * x);
        return sign * y;
    }
}
