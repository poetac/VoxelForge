// GaussianProcessSurrogate.cs — Issue #199 (OOB-4 Bayesian optimization).
//
// Gaussian Process regression with squared-exponential (RBF) kernel and
// per-dimension length scales (Automatic Relevance Determination, ARD).
// Inputs are scaled to [0, 1]^D internally so the length-scale parameters
// are dimension-agnostic — a length scale of 0.2 means "20 % of the
// variable's [Min, Max] band."
//
// The surrogate fits a posterior over a noiseless target function from
// (X, y) training pairs:
//
//   K[i, j] = σ_f² · exp(-0.5 · Σ_d (x_i,d - x_j,d)² / l_d²)
//   K_y    = K + σ_n² · I             (noise-augmented)
//   α      = K_y^(-1) · y             (precomputed via Cholesky)
//
//   μ(x) = k(x, X)^T · α
//   σ²(x) = k(x, x) - k(x, X)^T · K_y^(-1) · k(X, x)
//
// Cholesky decomposition K_y = L · L^T provides numerically stable
// inversion for all GP queries. ~250 LOC; pure C#, no external
// dependencies.

using System;
using System.Collections.Generic;

namespace Voxelforge.Optimization.Bayesian;

/// <summary>
/// Gaussian Process surrogate with squared-exponential (RBF) kernel and
/// per-dimension length scales (ARD). Inputs are scaled to <c>[0, 1]^D</c>
/// internally; callers pass real-units vectors and the surrogate handles
/// the scaling transparently using the bounds passed at construction.
/// </summary>
public sealed class GaussianProcessSurrogate
{
    private readonly int _dim;
    private readonly double[] _min;
    private readonly double[] _span;          // span[i] = max[i] - min[i]
    // Issue #258 (2026-04-29): hyperparameter fields relaxed from readonly so
    // RefitHyperparameters can mutate them after a marginal-likelihood fit.
    // External code never read these directly (only the constructor wrote
    // them), so the relaxation is internal-only.
    private double[] _lengthScales;           // in scaled [0, 1] units
    private double _signalVariance;
    private double _noiseVariance;

    // Set by Fit — null before the first fit.
    private double[][]? _Xs;        // training inputs in scaled space
    private double[]?   _y;         // training outputs (real values)
    private double[][]? _L;         // Cholesky factor of K_y, lower triangular
    private double[]?   _alpha;     // K_y^(-1) y

    // Predict scratch buffers — hoisted to fields so steady-state Predict
    // calls allocate zero arrays in the method body. _kStarBuf is resized in
    // Fit to match the training-set size; _xsBuf is sized once at ctor time.
    private readonly double[] _xsBuf;
    private double[] _kStarBuf;

    /// <summary>
    /// Number of training points. Zero before <see cref="Fit"/> is called.
    /// </summary>
    public int TrainingSize => _y?.Length ?? 0;

    /// <summary>Dimension of the input space.</summary>
    public int DimensionCount => _dim;

    /// <summary>
    /// Construct a GP surrogate with anisotropic length scales (ARD).
    /// </summary>
    /// <param name="bounds">Per-dim <c>(Min, Max)</c>. Inputs are scaled
    /// to <c>[0, 1]^D</c> using these bounds.</param>
    /// <param name="lengthScales">Per-dim length scale, expressed in
    /// scaled [0, 1] units (i.e. fractions of the variable band). Length
    /// must equal <c>bounds.Length</c>. Common default: <c>0.2</c>
    /// uniformly — kernel decays to ~37 % at 20 % of the band.</param>
    /// <param name="signalVariance">σ_f². Vertical scale of GP samples.
    /// Reasonable default: <c>1.0</c> when scores are O(1); rescale
    /// otherwise.</param>
    /// <param name="noiseVariance">σ_n². Diagonal jitter on K to make
    /// the matrix numerically positive-definite. Reasonable default:
    /// <c>1e-6</c>. Larger if observations are noisy.</param>
    public GaussianProcessSurrogate(
        (double Min, double Max)[] bounds,
        double[] lengthScales,
        double signalVariance,
        double noiseVariance)
    {
        if (bounds is null) throw new ArgumentNullException(nameof(bounds));
        if (lengthScales is null) throw new ArgumentNullException(nameof(lengthScales));
        if (bounds.Length != lengthScales.Length)
            throw new ArgumentException(
                $"bounds.Length ({bounds.Length}) != lengthScales.Length ({lengthScales.Length})");
        if (bounds.Length == 0)
            throw new ArgumentException("bounds must be non-empty", nameof(bounds));
        if (signalVariance <= 0)
            throw new ArgumentOutOfRangeException(nameof(signalVariance), "signalVariance must be positive");
        if (noiseVariance < 0)
            throw new ArgumentOutOfRangeException(nameof(noiseVariance), "noiseVariance must be ≥ 0");

        _dim = bounds.Length;
        _min  = new double[_dim];
        _span = new double[_dim];
        _lengthScales = new double[_dim];
        for (int i = 0; i < _dim; i++)
        {
            if (bounds[i].Max <= bounds[i].Min)
                throw new ArgumentException(
                    $"bounds[{i}]: Max ({bounds[i].Max}) must be > Min ({bounds[i].Min})", nameof(bounds));
            if (lengthScales[i] <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(lengthScales), $"lengthScales[{i}] = {lengthScales[i]} must be positive");
            _min[i]  = bounds[i].Min;
            _span[i] = bounds[i].Max - bounds[i].Min;
            _lengthScales[i] = lengthScales[i];
        }
        _signalVariance = signalVariance;
        _noiseVariance  = noiseVariance;
        _xsBuf = new double[_dim];
        _kStarBuf = Array.Empty<double>();
    }

    /// <summary>
    /// Convenience constructor with a single isotropic length scale.
    /// </summary>
    public GaussianProcessSurrogate(
        (double Min, double Max)[] bounds,
        double lengthScale,
        double signalVariance,
        double noiseVariance)
        : this(bounds, FillArray(bounds?.Length ?? 0, lengthScale), signalVariance, noiseVariance) { }

    private static double[] FillArray(int n, double v)
    {
        var a = new double[n];
        for (int i = 0; i < n; i++) a[i] = v;
        return a;
    }

    /// <summary>
    /// Fit the GP to a training set. Replaces any previous training data.
    /// Cost: O(N³) for Cholesky decomposition.
    /// </summary>
    public void Fit(double[][] X, double[] y)
    {
        if (X is null) throw new ArgumentNullException(nameof(X));
        if (y is null) throw new ArgumentNullException(nameof(y));
        if (X.Length != y.Length)
            throw new ArgumentException($"X.Length ({X.Length}) != y.Length ({y.Length})");
        int n = X.Length;
        if (n == 0)
        {
            _Xs = Array.Empty<double[]>();
            _y = Array.Empty<double>();
            _L = Array.Empty<double[]>();
            _alpha = Array.Empty<double>();
            return;
        }

        // Scale inputs to [0, 1]^D and copy.
        var Xs = new double[n][];
        for (int i = 0; i < n; i++)
        {
            if (X[i] is null) throw new ArgumentException($"X[{i}] is null", nameof(X));
            if (X[i].Length != _dim)
                throw new ArgumentException(
                    $"X[{i}].Length ({X[i].Length}) != dim ({_dim})", nameof(X));
            Xs[i] = ScaleToUnit(X[i]);
        }

        // Build K_y = K + σ_n² I.
        var Ky = new double[n][];
        for (int i = 0; i < n; i++)
        {
            Ky[i] = new double[n];
            for (int j = 0; j <= i; j++)
            {
                double k = KernelScaled(Xs[i], Xs[j]);
                if (i == j) k += _noiseVariance;
                Ky[i][j] = k;
                if (i != j) Ky[j] = Ky[j] ?? new double[n]; // ensure non-null in symmetric path
            }
        }
        // Mirror for symmetry (Cholesky uses lower triangle only; mirroring
        // is for clarity/debug — could be skipped).
        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
            Ky[i][j] = Ky[j][i];

        // Cholesky decomposition: K_y = L · L^T.
        var L = Cholesky(Ky);

        // Solve K_y · α = y  →  L L^T α = y.
        var yCopy = (double[])y.Clone();
        var alpha = CholeskySolve(L, yCopy);

        _Xs = Xs;
        _y = yCopy;
        _L = L;
        _alpha = alpha;
        if (_kStarBuf.Length != n) _kStarBuf = new double[n];
    }

    /// <summary>
    /// Predict the GP posterior at <paramref name="x"/>. Returns
    /// <c>(Mean, Variance)</c>. Variance is always non-negative
    /// (clamped at 0 for numerical safety; can be exactly 0 at training
    /// points).
    /// </summary>
    /// <remarks>
    /// Not thread-safe: reuses per-instance scratch buffers. Callers that
    /// share a surrogate across threads must serialise <see cref="Predict"/>
    /// externally. The Bayesian acquisition loop is single-threaded by
    /// design (sequential Sobol sweep), so this is not a current caller
    /// concern.
    /// </remarks>
    public (double Mean, double Variance) Predict(double[] x)
    {
        if (x is null) throw new ArgumentNullException(nameof(x));
        if (x.Length != _dim)
            throw new ArgumentException($"x.Length ({x.Length}) != dim ({_dim})", nameof(x));
        if (_Xs is null || _y is null || _L is null || _alpha is null)
            throw new InvalidOperationException("Surrogate has not been fit; call Fit() first.");

        int n = _Xs.Length;
        if (n == 0)
        {
            // Empty fit: prior is N(0, σ_f²).
            return (0.0, _signalVariance);
        }

        ScaleToUnitInto(x, _xsBuf);
        var kStar = _kStarBuf;
        for (int i = 0; i < n; i++) kStar[i] = KernelScaled(_xsBuf, _Xs[i]);

        // Mean: μ = kStar · α
        double mean = 0.0;
        for (int i = 0; i < n; i++) mean += kStar[i] * _alpha[i];

        // Variance: σ² = k(x, x) - kStar^T · K_y^(-1) · kStar
        //               = σ_f² - kStar^T · v   where L L^T v = kStar
        // CholeskySolve reads its b argument without mutation, so passing
        // kStar directly is safe — no defensive clone needed.
        var v = CholeskySolve(_L, kStar);
        double dot = 0.0;
        for (int i = 0; i < n; i++) dot += kStar[i] * v[i];
        double variance = _signalVariance - dot;
        if (variance < 0.0) variance = 0.0;  // numerical safety

        return (mean, variance);
    }

    private double KernelScaled(double[] a, double[] b)
    {
        // Squared-exponential / RBF with ARD: σ_f² · exp(-0.5 · Σ d_i² / l_i²).
        double s = 0.0;
        for (int i = 0; i < _dim; i++)
        {
            double d = a[i] - b[i];
            s += d * d / (_lengthScales[i] * _lengthScales[i]);
        }
        return _signalVariance * Math.Exp(-0.5 * s);
    }

    /// <summary>
    /// Issue #258 (2026-04-29): refit kernel hyperparameters by maximising
    /// the log marginal likelihood on the current training set via L-BFGS
    /// (MathNet.Numerics). Mutates internal hyperparameter state and rebuilds
    /// the Cholesky factor / α vector so subsequent <see cref="Predict"/>
    /// calls reflect the new fit. No-op if <see cref="Fit"/> has not been
    /// called or training set is empty (returns a result with
    /// <c>Converged = false</c>).
    /// </summary>
    /// <remarks>
    /// Hyperparameters are optimised in log-domain to enforce positivity
    /// without bounds: <c>θ = (log ℓ_1, …, log ℓ_d, log σ_f², log σ_n²)</c>.
    /// </remarks>
    public GpMleFitResult RefitHyperparameters(GpMleFitOptions opts)
    {
        if (opts is null) throw new ArgumentNullException(nameof(opts));
        if (_Xs is null || _y is null || _Xs.Length == 0)
        {
            return new GpMleFitResult(
                OptimizedTheta: Array.Empty<double>(),
                FinalLogMarginalLikelihood: double.NaN,
                Iterations: 0,
                Converged: false);
        }

        var initialTheta = new double[_dim + 2];
        for (int i = 0; i < _dim; i++) initialTheta[i] = Math.Log(_lengthScales[i]);
        initialTheta[_dim]     = Math.Log(_signalVariance);
        initialTheta[_dim + 1] = Math.Log(Math.Max(_noiseVariance, 1e-12));

        var fit = GpMarginalLikelihoodFit.FitFromScaled(_Xs, _y, initialTheta, _dim, opts);

        // Snapshot the current hyperparams in case the rebuild fails and we
        // need to revert (e.g., BFGS landed in a near-singular region of θ
        // that the in-loop NoiseFloor wasn't sufficient to stabilise).
        double[] backupLs = (double[])_lengthScales.Clone();
        double backupSf = _signalVariance;
        double backupSn = _noiseVariance;

        // Mutate hyperparameter state from the fit result. Apply the same
        // NoiseFloor stability minimum the optimisation closure used so the
        // post-fit Cholesky rebuild has the best chance of succeeding.
        for (int i = 0; i < _dim; i++) _lengthScales[i] = Math.Exp(fit.OptimizedTheta[i]);
        _signalVariance = Math.Exp(fit.OptimizedTheta[_dim]);
        _noiseVariance  = Math.Max(
            Math.Exp(fit.OptimizedTheta[_dim + 1]),
            GpMarginalLikelihoodFit.NoiseFloor);

        // Rebuild Cholesky + α with the new hyperparameters against the
        // existing scaled training set. On Cholesky failure (singular K_y at
        // the optimised θ — rare but possible with extreme length scales)
        // revert to the pre-fit hyperparams so the surrogate stays usable.
        if (!TryRebuildKernelAndAlpha())
        {
            _lengthScales = backupLs;
            _signalVariance = backupSf;
            _noiseVariance = backupSn;
            // Force one more rebuild on the backup; this MUST succeed since
            // the original Fit() succeeded with these hyperparams.
            TryRebuildKernelAndAlpha();
            return fit with { Converged = false };
        }
        return fit;
    }

    private bool TryRebuildKernelAndAlpha()
    {
        if (_Xs is null || _y is null) return false;
        int n = _Xs.Length;
        var Ky = new double[n][];
        for (int i = 0; i < n; i++)
        {
            Ky[i] = new double[n];
            for (int j = 0; j <= i; j++)
            {
                double k = KernelScaled(_Xs[i], _Xs[j]);
                if (i == j) k += _noiseVariance;
                Ky[i][j] = k;
            }
        }
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                Ky[i][j] = Ky[j][i];
        try
        {
            _L = Cholesky(Ky);
            _alpha = CholeskySolve(_L, (double[])_y.Clone());
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private double[] ScaleToUnit(double[] x)
    {
        var s = new double[_dim];
        for (int i = 0; i < _dim; i++) s[i] = (x[i] - _min[i]) / _span[i];
        return s;
    }

    private void ScaleToUnitInto(double[] x, double[] destination)
    {
        for (int i = 0; i < _dim; i++) destination[i] = (x[i] - _min[i]) / _span[i];
    }

    /// <summary>
    /// Cholesky decomposition of a symmetric positive-definite matrix A.
    /// Returns lower-triangular L such that A = L · L^T. Throws on
    /// non-PD input (negative or zero diagonal in the partial sum).
    /// </summary>
    internal static double[][] Cholesky(double[][] A)
    {
        int n = A.Length;
        var L = new double[n][];
        for (int i = 0; i < n; i++) L[i] = new double[n];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = A[i][j];
                for (int k = 0; k < j; k++) sum -= L[i][k] * L[j][k];

                if (i == j)
                {
                    if (sum <= 0.0)
                        throw new InvalidOperationException(
                            $"Cholesky failed: matrix not positive-definite at row {i} (diagonal sum = {sum:G6}). "
                          + "Increase noiseVariance to make K_y better-conditioned.");
                    L[i][j] = Math.Sqrt(sum);
                }
                else
                {
                    L[i][j] = sum / L[j][j];
                }
            }
        }
        return L;
    }

    /// <summary>
    /// Solve L · L^T · x = b (i.e., A · x = b given Cholesky L of A).
    /// Mutates <paramref name="b"/>; returns the solution.
    /// </summary>
    internal static double[] CholeskySolve(double[][] L, double[] b)
    {
        int n = b.Length;
        // Forward solve: L · y = b.
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = b[i];
            for (int k = 0; k < i; k++) sum -= L[i][k] * y[k];
            y[i] = sum / L[i][i];
        }
        // Backward solve: L^T · x = y.
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = y[i];
            for (int k = i + 1; k < n; k++) sum -= L[k][i] * x[k];
            x[i] = sum / L[i][i];
        }
        return x;
    }
}
