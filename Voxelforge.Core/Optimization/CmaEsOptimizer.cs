// CmaEsOptimizer.cs — Covariance Matrix Adaptation Evolution Strategy.
// Issue #157 (T1.3 from CLAUDE.md optimization-infra Tier 1).
//
// CMA-ES is the gold-standard gradient-free optimizer for continuous
// problems with dim ∈ [10, 100]. Typical literature claims 5-20×
// fewer evaluations than SA to reach the same basin on the continuous
// local-refinement phase. Implements the textbook (Hansen 2001) form
// with the standard hyperparameter calibration:
//
//   λ = 4 + ⌊3 ln(n)⌋               (population)
//   μ = ⌊λ / 2⌋                     (parents)
//   wᵢ = ln(μ + 0.5) - ln(i + 1)    (recombination weights)
//   μ_eff = (Σ wᵢ)² / Σ wᵢ²         (effective parents)
//   c_σ, d_σ, c_c, c_1, c_μ          (default schedules)
//
// Per generation:
//   1. Eigendecompose C = B · D² · B^T
//   2. Sample λ candidates xᵢ = m + σ · B · D · zᵢ where zᵢ ~ N(0, I)
//   3. Evaluate f(xᵢ)
//   4. Sort, take top μ, compute new mean m
//   5. Update evolution paths p_σ, p_c
//   6. Update step size σ ← σ · exp((c_σ/d_σ) · (||p_σ||/E[||N(0,I)||] - 1))
//   7. Update covariance C via rank-1 + rank-μ updates
//   8. Repeat until convergence or budget exhausted.
//
// Plug-in compatibility: consumes the IObjective interface from #155
// (the IObjective decoupling). Engine-family-agnostic — the optimizer
// never sees the rocket-shaped record. Hybrid SA-outer + CMA-ES-inner
// composition is a follow-on (T1.3 issue's two-step plan).
//
// References:
//   Hansen "The CMA Evolution Strategy: A Comparing Review" (2001).
//   Hansen "The CMA Evolution Strategy: A Tutorial" (2016).
//   https://cma-es.github.io/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Voxelforge.Optimization;

/// <summary>
/// CMA-ES (Covariance Matrix Adaptation Evolution Strategy) optimizer.
/// Single-process, deterministic given a fixed seed. Consumes
/// <see cref="IObjective"/> for the engine-family-agnostic
/// optimizer/oracle boundary established in #155.
/// </summary>
[Deterministic]
public sealed class CmaEsOptimizer
{
    private readonly IObjective _objective;
    private readonly int _dim;
    private readonly int _maxGenerations;
    private readonly Random _rng;

    // Hyperparameters derived from dim at construction time.
    private readonly int _lambda;       // offspring per generation
    private readonly int _mu;           // parent count
    private readonly double[] _weights; // recombination weights, length _mu
    private readonly double _mu_eff;
    private readonly double _c_sigma;
    private readonly double _d_sigma;
    private readonly double _c_c;
    private readonly double _c_1;
    private readonly double _c_mu;
    private readonly double _expectedNormN; // E[||N(0, I)||]

    // Algorithm state — mutated each generation.
    private readonly double[]   _mean;
    private double               _sigma;
    private readonly double[,]   _C;        // covariance, n×n
    private readonly double[]    _pSigma;   // evolution path for sigma
    private readonly double[]    _pC;       // evolution path for C

    // Working buffers (reused per generation to avoid allocations).
    private readonly double[,] _B;
    private readonly double[]  _D;
    private readonly double[,] _Cwork;
    private readonly double[]  _eigenvalues;
    private readonly double[]  _meanShift;
    private readonly double[]  _meanShiftInBasis;
    private readonly double[]  _cInvSqrtMs;

    // Phase 2 of #627 (tracked under #743). When true, infeasible
    // candidates' +∞ scores are replaced by SoftPenalty.Compute(eval) —
    // a sigmoid-saturated soft penalty (penalty_scale · Σ tanh(|SignedBreachMagnitude| / scale)).
    private readonly bool _useSoftPenalty;

    /// <summary>
    /// Per-generation diagnostic record.
    /// </summary>
    public sealed record GenerationRecord(
        int    Generation,
        double BestScore,
        double WorstScore,
        double Sigma,
        double MeanScore,
        long   TotalEvaluations);

    /// <summary>
    /// Final result. <see cref="BestParams"/> is the best vector seen
    /// across all generations; <see cref="BestScore"/> the corresponding
    /// score. <see cref="History"/> carries per-generation diagnostics.
    /// </summary>
    public sealed record Result(
        double[]                BestParams,
        double                  BestScore,
        EvaluationResult?       BestEvaluation,
        int                     GenerationsCompleted,
        long                    TotalEvaluations,
        long                    ElapsedMilliseconds,
        IReadOnlyList<GenerationRecord> History);

    /// <summary>
    /// Construct a CMA-ES optimizer wrapping <paramref name="objective"/>.
    /// </summary>
    /// <param name="objective">Engine-family-agnostic oracle.</param>
    /// <param name="initialMean">
    /// Seed point in n-dim space. Length must equal
    /// <c>objective.DimensionCount</c>.
    /// </param>
    /// <param name="initialSigma">
    /// Initial step size. Reasonable default: 0.3 × the smaller side of
    /// each variable's [Min, Max] band — broad enough to explore, narrow
    /// enough to converge in a few hundred generations.
    /// </param>
    /// <param name="maxGenerations">Stop after this many generations regardless of convergence.</param>
    /// <param name="seed">RNG seed for reproducibility.</param>
    /// <param name="lambdaOverride">
    /// Optional: override the default population size. The default
    /// (4 + ⌊3 ln(n)⌋) is calibrated for general-purpose CMA-ES; tests
    /// sometimes need tighter populations for predictable behaviour.
    /// </param>
    public CmaEsOptimizer(
        IObjective objective,
        double[] initialMean,
        double initialSigma,
        int maxGenerations,
        int seed = 42,
        int? lambdaOverride = null,
        bool useSoftPenalty = false)
    {
        if (objective is null) throw new ArgumentNullException(nameof(objective));
        if (initialMean is null) throw new ArgumentNullException(nameof(initialMean));
        if (initialMean.Length != objective.DimensionCount)
            throw new ArgumentException(
                $"initialMean length {initialMean.Length} != objective.DimensionCount {objective.DimensionCount}",
                nameof(initialMean));
        if (initialSigma <= 0)
            throw new ArgumentOutOfRangeException(nameof(initialSigma), "initialSigma must be positive");
        if (maxGenerations < 1)
            throw new ArgumentOutOfRangeException(nameof(maxGenerations), "maxGenerations must be ≥ 1");

        _objective = objective;
        _dim       = objective.DimensionCount;
        _maxGenerations = maxGenerations;
        _rng       = new Random(seed);
        _useSoftPenalty = useSoftPenalty;

        // Hyperparameter schedules per Hansen (2016) tutorial §3.
        _lambda = lambdaOverride ?? (4 + (int)Math.Floor(3.0 * Math.Log(_dim)));
        _mu     = _lambda / 2;
        _weights = new double[_mu];
        double wsum = 0.0;
        for (int i = 0; i < _mu; i++)
        {
            _weights[i] = Math.Log(_mu + 0.5) - Math.Log(i + 1.0);
            wsum += _weights[i];
        }
        for (int i = 0; i < _mu; i++) _weights[i] /= wsum;
        double w2sum = 0.0;
        for (int i = 0; i < _mu; i++) w2sum += _weights[i] * _weights[i];
        _mu_eff = 1.0 / w2sum;

        _c_sigma = (_mu_eff + 2.0) / (_dim + _mu_eff + 5.0);
        _d_sigma = 1.0 + 2.0 * Math.Max(0.0, Math.Sqrt((_mu_eff - 1.0) / (_dim + 1.0)) - 1.0) + _c_sigma;
        _c_c     = (4.0 + _mu_eff / _dim) / (_dim + 4.0 + 2.0 * _mu_eff / _dim);
        _c_1     = 2.0 / Math.Pow(_dim + 1.3, 2) + _mu_eff;
        _c_1     = 2.0 / (Math.Pow(_dim + 1.3, 2) + _mu_eff);
        _c_mu    = Math.Min(
            1.0 - _c_1,
            2.0 * (_mu_eff - 2.0 + 1.0 / _mu_eff) / (Math.Pow(_dim + 2.0, 2) + _mu_eff));

        _expectedNormN = Math.Sqrt(_dim) * (1.0 - 1.0 / (4.0 * _dim) + 1.0 / (21.0 * _dim * _dim));

        // Initialise state.
        _mean = (double[])initialMean.Clone();
        _sigma = initialSigma;
        _C = new double[_dim, _dim];
        for (int i = 0; i < _dim; i++) _C[i, i] = 1.0;  // identity
        _pSigma = new double[_dim];
        _pC     = new double[_dim];

        _B           = new double[_dim, _dim];
        _D           = new double[_dim];
        _Cwork       = new double[_dim, _dim];
        _eigenvalues = new double[_dim];
        _meanShift        = new double[_dim];
        _meanShiftInBasis = new double[_dim];
        _cInvSqrtMs       = new double[_dim];
    }

    /// <summary>
    /// Run CMA-ES until <see cref="_maxGenerations"/> or cancellation.
    /// </summary>
    [Deterministic]
    public Result Run(CancellationToken cancellationToken = default)
    {
        long swStart = Stopwatch.GetTimestamp();
        long evaluations = 0;
        var history = new List<GenerationRecord>(_maxGenerations);

        // Track best-ever across all generations.
        double[] bestParams = (double[])_mean.Clone();
        double bestScore = double.PositiveInfinity;
        EvaluationResult? bestEval = null;

        var samples = new double[_lambda][];
        var z       = new double[_lambda][];
        var fitness = new double[_lambda];
        var idx     = new int[_lambda];
        var meanOld = new double[_dim];

        for (int gen = 0; gen < _maxGenerations; gen++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // 1. Eigendecompose C = B · D² · B^T (Cwork is mutated).
            for (int i = 0; i < _dim; i++)
            for (int j = 0; j < _dim; j++)
                _Cwork[i, j] = _C[i, j];
            JacobiEigen.Decompose(_Cwork, _B, _eigenvalues);
            for (int i = 0; i < _dim; i++)
                _D[i] = Math.Sqrt(Math.Max(_eigenvalues[i], 1e-30));

            // 2. Sample λ candidates, evaluate them.
            for (int k = 0; k < _lambda; k++)
            {
                if (samples[k] is null) samples[k] = new double[_dim];
                if (z[k]       is null) z[k]       = new double[_dim];

                // z ~ N(0, I)
                for (int i = 0; i < _dim; i++) z[k][i] = SampleStandardNormal();

                // x = m + σ · B · D · z   (= m + σ · BD-rotation of z)
                for (int i = 0; i < _dim; i++)
                {
                    double sumi = 0.0;
                    for (int j = 0; j < _dim; j++) sumi += _B[i, j] * _D[j] * z[k][j];
                    samples[k][i] = _mean[i] + _sigma * sumi;
                }

                // Bounded sampling — Hansen 2016 §3.3 reflection-at-bound.
                // Folds out-of-bounds samples back into [Min, Max] via the
                // saw-tooth modular reflection. Distribution statistics (σ, C,
                // p_σ, p_c) remain valid because the algorithm trains on the
                // reflected (evaluated) samples; the convex-combination mean
                // update over reflected samples then stays in-bounds too.
                for (int i = 0; i < _dim; i++)
                {
                    var v = _objective.Variables[i];
                    samples[k][i] = ReflectIntoBounds(samples[k][i], v.Min, v.Max);
                }

                var eval = _objective.Evaluate(samples[k], cancellationToken);
                double score = _useSoftPenalty ? SoftPenalty.Compute(eval) : eval.Score;
                fitness[k] = score;
                evaluations++;

                if (score < bestScore && !double.IsNaN(score))
                {
                    bestScore = score;
                    bestParams = (double[])samples[k].Clone();
                    bestEval = eval;
                }
            }

            // 3. Sort by fitness ascending. Tie-break by original index so
            //    that ties (common with +Inf-clamped infeasible candidates)
            //    produce a deterministic ordering — equal fitness values
            //    otherwise leave the recombination weight assignment at
            //    line 274 undefined, drifting subsequent generations.
            for (int k = 0; k < _lambda; k++) idx[k] = k;
            Array.Sort(idx, (a, b) =>
            {
                int c = fitness[a].CompareTo(fitness[b]);
                return c != 0 ? c : a.CompareTo(b);
            });

            // 4. Save old mean and compute new mean.
            for (int i = 0; i < _dim; i++) meanOld[i] = _mean[i];
            for (int i = 0; i < _dim; i++)
            {
                double sumi = 0.0;
                for (int k = 0; k < _mu; k++) sumi += _weights[k] * samples[idx[k]][i];
                _mean[i] = sumi;
            }

            // 5. Update evolution paths.
            //    p_σ ← (1-c_σ)*p_σ + sqrt(c_σ*(2-c_σ)*μ_eff) * (B * D^-1 * B^T) * (m_new - m_old) / σ
            //    For numerical stability: weighted mean of the selected z's.
            //    z_avg = sum(w_k * z[idx[k]])
            //    p_σ ← (1-c_σ)*p_σ + sqrt(c_σ*(2-c_σ)*μ_eff) * z_avg ... wait no.
            //    The simpler equivalent: use the B*z_avg form since
            //    B * D^-1 * B^T * (m_new - m_old) = σ * B * z_avg is what z_avg captures
            //    when the sampled candidates haven't been scaled away.
            //    Direct: compute Cinv_sqrt · displacement.
            // (m_new - m_old) / σ
            for (int i = 0; i < _dim; i++)
                _meanShift[i] = (_mean[i] - meanOld[i]) / _sigma;

            // Project meanShift through B^T → divide by D → project back via B.
            // Equivalent to: C^(-1/2) · meanShift.
            for (int i = 0; i < _dim; i++)
            {
                double sumi = 0.0;
                for (int j = 0; j < _dim; j++) sumi += _B[j, i] * _meanShift[j];  // B^T · ms
                _meanShiftInBasis[i] = sumi / Math.Max(_D[i], 1e-30);
            }
            for (int i = 0; i < _dim; i++)
            {
                double sumi = 0.0;
                for (int j = 0; j < _dim; j++) sumi += _B[i, j] * _meanShiftInBasis[j];
                _cInvSqrtMs[i] = sumi;
            }
            double psFactor = Math.Sqrt(_c_sigma * (2.0 - _c_sigma) * _mu_eff);
            for (int i = 0; i < _dim; i++)
                _pSigma[i] = (1.0 - _c_sigma) * _pSigma[i] + psFactor * _cInvSqrtMs[i];

            // 6. Step-size update.
            double pSigmaNorm = 0.0;
            for (int i = 0; i < _dim; i++) pSigmaNorm += _pSigma[i] * _pSigma[i];
            pSigmaNorm = Math.Sqrt(pSigmaNorm);
            _sigma *= Math.Exp((_c_sigma / _d_sigma) * (pSigmaNorm / _expectedNormN - 1.0));

            // Indicator h_σ for p_c update — suppresses rank-1 update when sigma is jumping.
            double genFraction = (gen + 1.0) / _maxGenerations;
            double h_sigma = pSigmaNorm /
                Math.Sqrt(1.0 - Math.Pow(1.0 - _c_sigma, 2.0 * (gen + 1)))
                < (1.4 + 2.0 / (_dim + 1.0)) * _expectedNormN ? 1.0 : 0.0;
            _ = genFraction;  // reserved for future schedule tweaks

            // 7. p_c update.
            double pcFactor = Math.Sqrt(_c_c * (2.0 - _c_c) * _mu_eff);
            for (int i = 0; i < _dim; i++)
                _pC[i] = (1.0 - _c_c) * _pC[i] + h_sigma * pcFactor * _meanShift[i];

            // 8. Covariance update: C ← (1 - c_1 - c_μ * Σw)·C + c_1·(p_c·p_c^T) + c_μ·Σ wᵢ·yᵢ·yᵢ^T
            //    where yᵢ = (xᵢ - m_old) / σ for top-μ.
            double sumW = 0.0;
            for (int i = 0; i < _mu; i++) sumW += _weights[i];
            double cFactor = (1.0 - _c_1 - _c_mu * sumW)
                           + (1.0 - h_sigma) * _c_1 * _c_c * (2.0 - _c_c);
            for (int i = 0; i < _dim; i++)
            for (int j = 0; j < _dim; j++)
                _C[i, j] *= cFactor;

            // Rank-1 update: + c_1 · p_c · p_c^T
            for (int i = 0; i < _dim; i++)
            for (int j = 0; j < _dim; j++)
                _C[i, j] += _c_1 * _pC[i] * _pC[j];

            // Rank-μ update: + c_μ · Σ wᵢ · yᵢ · yᵢ^T
            for (int k = 0; k < _mu; k++)
            {
                double[] sample = samples[idx[k]];
                for (int i = 0; i < _dim; i++)
                {
                    double yi = (sample[i] - meanOld[i]) / _sigma;
                    for (int j = 0; j < _dim; j++)
                    {
                        double yj = (sample[j] - meanOld[j]) / _sigma;
                        _C[i, j] += _c_mu * _weights[k] * yi * yj;
                    }
                }
            }

            // Symmetrise C (drift accumulates in floating-point).
            for (int i = 0; i < _dim; i++)
            for (int j = i + 1; j < _dim; j++)
            {
                double avg = 0.5 * (_C[i, j] + _C[j, i]);
                _C[i, j] = _C[j, i] = avg;
            }

            // Diagnostics.
            double meanFit = 0.0;
            for (int k = 0; k < _lambda; k++) meanFit += fitness[k];
            meanFit /= _lambda;
            history.Add(new GenerationRecord(
                Generation:       gen,
                BestScore:        fitness[idx[0]],
                WorstScore:       fitness[idx[_lambda - 1]],
                Sigma:            _sigma,
                MeanScore:        meanFit,
                TotalEvaluations: evaluations));
        }

        long swEnd = Stopwatch.GetTimestamp();
        long elapsedMs = (swEnd - swStart) * 1000 / Stopwatch.Frequency;

        return new Result(
            BestParams:           bestParams,
            BestScore:            bestScore,
            BestEvaluation:       bestEval,
            GenerationsCompleted: history.Count,
            TotalEvaluations:     evaluations,
            ElapsedMilliseconds:  elapsedMs,
            History:              history);
    }

    /// <summary>
    /// Sample from N(0, 1) via Box–Muller. Cached spare value handled
    /// via a paired return so two normals come out per pair of uniforms.
    /// </summary>
    private double _spareNormal;
    private bool   _hasSpareNormal;
    private double SampleStandardNormal()
    {
        if (_hasSpareNormal)
        {
            _hasSpareNormal = false;
            return _spareNormal;
        }
        double u1, u2;
        do { u1 = _rng.NextDouble(); } while (u1 <= 0.0);
        u2 = _rng.NextDouble();
        double r = Math.Sqrt(-2.0 * Math.Log(u1));
        double theta = 2.0 * Math.PI * u2;
        _spareNormal    = r * Math.Sin(theta);
        _hasSpareNormal = true;
        return r * Math.Cos(theta);
    }

    // Read-only state accessors for diagnostics + test introspection.
    public int    DimensionCount  => _dim;
    public int    PopulationSize  => _lambda;
    public int    ParentCount     => _mu;
    public double CurrentSigma    => _sigma;

    /// <summary>
    /// Reflect a scalar back into <c>[min, max]</c> via the saw-tooth modular
    /// fold from Hansen (2016) §3.3. Handles arbitrary-magnitude excursions
    /// in O(1) (no iterative reflection loop). Degenerate band (max ≤ min)
    /// pins to <paramref name="min"/>.
    /// </summary>
    internal static double ReflectIntoBounds(double x, double min, double max)
    {
        if (max <= min) return min;
        double span = max - min;
        double y = x - min;
        // Fold into [0, 2*span). Math.Floor handles negative y correctly
        // (Floor(-0.15) = -1, so y mod 2*span lands in the right half-open
        // interval regardless of sign).
        double folded = y - 2.0 * span * Math.Floor(y / (2.0 * span));
        if (folded > span) folded = 2.0 * span - folded;
        return min + folded;
    }
}
