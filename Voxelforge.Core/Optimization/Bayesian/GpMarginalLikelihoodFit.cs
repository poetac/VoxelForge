// GpMarginalLikelihoodFit.cs — Issue #258 (OOB-4 follow-up).
//
// Maximum-likelihood fit of GaussianProcessSurrogate hyperparameters
// (per-dim length scales, signal variance, noise variance) by maximising
// the log marginal likelihood:
//
//     log p(y | X, θ) = -½ y^T (K_y)^(-1) y − ½ log|K_y| − (n/2) log(2π)
//                       └── data fit ──┘    └ complexity ┘
//
// Following Rasmussen & Williams "Gaussian Processes for Machine Learning"
// §2.2 eq. 2.30, with `K_y = K(X, X; θ) + σ_n² · I`. Computed via Cholesky:
// `α = K_y⁻¹ y` is a back-solve, `log|K_y| = 2 Σ_i log(L[i, i])`.
//
// Optimisation: L-BFGS via MathNet.Numerics. Hyperparameters optimised in
// log-domain (`θ = log(actual)`) to enforce positivity without bounds.
// Determinism is required (issue #258); BfgsMinimizer is gradient-driven
// from the supplied initial point with no internal RNG, so identical
// (X, y, initialTheta, opts) produce bit-identical OptimizedTheta.

using System;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;

namespace Voxelforge.Optimization.Bayesian;

/// <summary>
/// Tunable knobs for <see cref="GpMarginalLikelihoodFit"/>. Defaults
/// match MathNet's recommended L-BFGS tolerances.
/// </summary>
public sealed record GpMleFitOptions(
    int MaxIterations = 100,
    double GradientTolerance = 1e-5,
    double ParameterTolerance = 1e-8,
    double FunctionProgressTolerance = 1e-8);

/// <summary>
/// Outcome of a single MLE fit. <c>OptimizedTheta</c> is in log-domain;
/// the caller (typically <see cref="GaussianProcessSurrogate.RefitHyperparameters"/>)
/// exponentiates back to actual hyperparameter values.
/// </summary>
public sealed record GpMleFitResult(
    double[] OptimizedTheta,
    double FinalLogMarginalLikelihood,
    int Iterations,
    bool Converged);

public static class GpMarginalLikelihoodFit
{
    /// <summary>
    /// Numerical-stability floor on the noise variance σ_n² applied inside
    /// the optimisation objective and the post-fit rebuild. Without this,
    /// BFGS can drive σ_n² toward 0 in log-domain and hit a Cholesky-
    /// singular K_y for any training set with even mild correlation — the
    /// gradient explodes there. The floor scales with kernel signal
    /// variance: 1e-6 is a standard "jitter" magnitude in the GP literature
    /// (Rasmussen &amp; Williams §5; GPy / GPflow defaults). In practice MLE
    /// pulls real noise estimates well above the floor anyway.
    /// </summary>
    internal const double NoiseFloor = 1e-6;

    /// <summary>
    /// Fit hyperparameters against an already-scaled training set
    /// (<c>X ∈ [0, 1]^D</c>, the same internal representation used by
    /// <see cref="GaussianProcessSurrogate"/>). Length scales, signal
    /// variance, and noise variance are stored in log-domain in
    /// <paramref name="initialTheta"/>: indices <c>[0, dim)</c> are
    /// <c>log ℓ_i</c>, <c>[dim]</c> is <c>log σ_f²</c>,
    /// <c>[dim + 1]</c> is <c>log σ_n²</c>.
    /// </summary>
    internal static GpMleFitResult FitFromScaled(
        double[][] scaledX,
        double[] y,
        double[] initialTheta,
        int dim,
        GpMleFitOptions opts)
    {
        if (scaledX is null) throw new ArgumentNullException(nameof(scaledX));
        if (y is null)       throw new ArgumentNullException(nameof(y));
        if (initialTheta is null) throw new ArgumentNullException(nameof(initialTheta));
        if (opts is null)    throw new ArgumentNullException(nameof(opts));
        if (initialTheta.Length != dim + 2)
            throw new ArgumentException(
                $"initialTheta.Length ({initialTheta.Length}) != dim + 2 ({dim + 2})",
                nameof(initialTheta));
        if (scaledX.Length != y.Length)
            throw new ArgumentException(
                $"scaledX.Length ({scaledX.Length}) != y.Length ({y.Length})");

        // Capture immutable fixtures for the closures.
        int n = y.Length;
        if (n == 0)
        {
            return new GpMleFitResult(
                OptimizedTheta: (double[])initialTheta.Clone(),
                FinalLogMarginalLikelihood: double.NaN,
                Iterations: 0,
                Converged: false);
        }
        double halfNlog2Pi = 0.5 * n * Math.Log(2.0 * Math.PI);

        // Objective = NEGATIVE log marginal likelihood (we minimise).
        // Gradient is computed analytically (Rasmussen & Williams eq. 5.9):
        //   d log p(y|X,θ) / dθ_k = ½ tr((α α^T − K_y⁻¹) · dK_y/dθ_k)
        // Negate to minimise.
        double Negative_LogML(Vector<double> theta, out Vector<double> gradOut)
        {
            // Unpack θ. Enforce a NoiseFloor stability minimum on σ_n² so
            // BFGS can't drive it into a Cholesky-singular regime; identical
                        // floor must apply on the post-fit rebuild in RefitHyperparameters.
            var ls = new double[dim];
            for (int i = 0; i < dim; i++) ls[i] = Math.Exp(theta[i]);
            double sigmaF2 = Math.Exp(theta[dim]);
            double sigmaN2 = Math.Max(Math.Exp(theta[dim + 1]), NoiseFloor);

            // Build K_y.
            var Ky = new double[n][];
            var KMatNoNoise = new double[n][]; // K (without σ_n²·I) for gradient
            for (int i = 0; i < n; i++)
            {
                Ky[i] = new double[n];
                KMatNoNoise[i] = new double[n];
            }
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    double sumD2OverL2 = 0.0;
                    for (int d = 0; d < dim; d++)
                    {
                        double diff = scaledX[i][d] - scaledX[j][d];
                        sumD2OverL2 += diff * diff / (ls[d] * ls[d]);
                    }
                    double k = sigmaF2 * Math.Exp(-0.5 * sumD2OverL2);
                    KMatNoNoise[i][j] = k;
                    KMatNoNoise[j][i] = k;
                    Ky[i][j] = k + (i == j ? sigmaN2 : 0.0);
                    Ky[j][i] = Ky[i][j];
                }
            }

            // Cholesky + back-solve. Catch non-PD by returning +∞ so BFGS
            // pulls back into the feasible region.
            double[][] L;
            try
            {
                L = GaussianProcessSurrogate.Cholesky(Ky);
            }
            catch (InvalidOperationException)
            {
                gradOut = Vector<double>.Build.Dense(theta.Count, 0.0);
                return double.MaxValue;
            }
            var alpha = GaussianProcessSurrogate.CholeskySolve(L, (double[])y.Clone());

            // log|K_y| = 2 Σ log(L[i,i]).
            double logDet = 0.0;
            for (int i = 0; i < n; i++) logDet += Math.Log(L[i][i]);
            logDet *= 2.0;

            // y^T α
            double yTalpha = 0.0;
            for (int i = 0; i < n; i++) yTalpha += y[i] * alpha[i];

            double logML = -0.5 * yTalpha - 0.5 * logDet - halfNlog2Pi;

            // Gradient of log p(y|X,θ) with respect to each θ_k:
            //   d/dθ_k = ½ tr((α α^T − K_y⁻¹) · dK_y/dθ_k)
            //         = ½ (α^T (dK/dθ_k) α − tr(K_y⁻¹ · dK/dθ_k))
            // Compute K_y⁻¹ explicitly via n Cholesky back-solves on the
            // identity columns. For small n this is the simplest correct
            // approach.
            var Kinv = new double[n][];
            for (int j = 0; j < n; j++)
            {
                var ej = new double[n];
                ej[j] = 1.0;
                Kinv[j] = GaussianProcessSurrogate.CholeskySolve(L, ej);
            }

            // dK / dθ_k for each hyperparameter:
            //   • k = i (length scale ℓ_i, with θ_i = log ℓ_i):
            //       dK/dθ_i = K · (Δ_i² / ℓ_i²)              (chain through log)
            //     where Δ_i² = (x_a,i − x_b,i)² (per-pair).
            //   • k = dim   (signal variance, θ = log σ_f²):
            //       dK_y/dθ = K  (multiplicative)
            //   • k = dim+1 (noise variance, θ = log σ_n²):
            //       dK_y/dθ = σ_n² · I
            var grad = new double[dim + 2];

            // Length-scale gradients.
            for (int dIdx = 0; dIdx < dim; dIdx++)
            {
                double lInv2 = 1.0 / (ls[dIdx] * ls[dIdx]);
                double accum = 0.0;
                for (int i = 0; i < n; i++)
                {
                    double aAlpha_i = alpha[i];
                    for (int j = 0; j < n; j++)
                    {
                        double diff = scaledX[i][dIdx] - scaledX[j][dIdx];
                        double dK = KMatNoNoise[i][j] * (diff * diff * lInv2);
                        // α α^T term
                        accum += aAlpha_i * dK * alpha[j];
                        // K_y⁻¹ trace term: subtract K_y⁻¹[i,j] · dK[j,i]
                        // (symmetric).
                        accum -= Kinv[i][j] * dK;
                    }
                }
                grad[dIdx] = -0.5 * accum; // negate (we minimise neg log ML)
            }

            // Signal-variance gradient. dK_y/dθ_dim = K (without noise term).
            {
                double accum = 0.0;
                for (int i = 0; i < n; i++)
                {
                    double aAlpha_i = alpha[i];
                    for (int j = 0; j < n; j++)
                    {
                        double dK = KMatNoNoise[i][j];
                        accum += aAlpha_i * dK * alpha[j];
                        accum -= Kinv[i][j] * dK;
                    }
                }
                grad[dim] = -0.5 * accum;
            }

            // Noise-variance gradient. dK_y/dθ_(dim+1) = σ_n² · I →
            // (α^T · σ_n²I · α) − tr(K_y⁻¹ · σ_n²I) = σ_n² (α^T α − tr(K_y⁻¹)).
            {
                double alphaSq = 0.0;
                double trKinv = 0.0;
                for (int i = 0; i < n; i++)
                {
                    alphaSq += alpha[i] * alpha[i];
                    trKinv  += Kinv[i][i];
                }
                grad[dim + 1] = -0.5 * sigmaN2 * (alphaSq - trKinv);
            }

            // Sanitise: if the kernel evaluation under-/overflowed in any
            // component (extreme ℓ explored by BFGS), the gradient would
            // contain NaN/Inf and BFGS's WolfeLineSearch raises
            // EvaluationException. Replace non-finite components with 0
            // and signal "no descent direction" by returning a very large
            // value — BFGS pulls back to the last good point.
            bool anyBad = false;
            for (int i = 0; i < grad.Length; i++)
            {
                if (double.IsNaN(grad[i]) || double.IsInfinity(grad[i]))
                {
                    grad[i] = 0.0;
                    anyBad = true;
                }
            }
            gradOut = Vector<double>.Build.Dense(grad);
            if (anyBad || double.IsNaN(logML) || double.IsInfinity(logML))
                return 1e300; // big finite penalty so BFGS retreats
            return -logML; // minimise negative log ML
        }

        // MathNet's ObjectiveFunction.Gradient takes separate value + gradient
        // delegates. Each BFGS line-search step calls both (so the kernel
        // matrix + Cholesky get built twice per step). For typical training
        // sizes (~50-200 points) the cost is dominated by the n³ Cholesky
        // and is acceptable; if it ever becomes a bottleneck, switch to a
        // hand-rolled IObjectiveFunction implementation that caches the
        // last (theta, value, grad).
        var objective = ObjectiveFunction.Gradient(
            function: (Vector<double> theta) =>
            {
                Negative_LogML(theta, out _);
                return Negative_LogML(theta, out _);
            },
            gradient: (Vector<double> theta) =>
            {
                Negative_LogML(theta, out var grad);
                return grad;
            });

        var minimizer = new BfgsMinimizer(
            gradientTolerance: opts.GradientTolerance,
            parameterTolerance: opts.ParameterTolerance,
            functionProgressTolerance: opts.FunctionProgressTolerance,
            maximumIterations: opts.MaxIterations);

        var initialGuess = Vector<double>.Build.Dense(initialTheta);

        try
        {
            var result = minimizer.FindMinimum(objective, initialGuess);
            var optimized = new double[result.MinimizingPoint.Count];
            for (int i = 0; i < optimized.Length; i++) optimized[i] = result.MinimizingPoint[i];

            return new GpMleFitResult(
                OptimizedTheta: optimized,
                FinalLogMarginalLikelihood: -result.FunctionInfoAtMinimum.Value,
                Iterations: result.Iterations,
                Converged: result.ReasonForExit != ExitCondition.ExceedIterations
                        && result.ReasonForExit != ExitCondition.None);
        }
        catch (MaximumIterationsException)
        {
            // BFGS hit the iteration cap before converging — return the
            // initial guess and Converged=false so the caller can re-run
            // with a higher cap or retain the previous hyperparameters.
            return new GpMleFitResult(
                OptimizedTheta: (double[])initialTheta.Clone(),
                FinalLogMarginalLikelihood: double.NaN,
                Iterations: opts.MaxIterations,
                Converged: false);
        }
    }
}
