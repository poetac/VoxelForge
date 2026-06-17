// GradientPolishOptimizer.cs — T4.1 finite-difference gradient polish.
//
// Roadmap item (Tier-4 Optimization Infrastructure):
// "After SA converges, run 10-20 FD gradient steps on the scalar score for
// each elite. Uses the existing Evaluate oracle; no physics rewrite.
// 1-5% score improvement, near-zero risk. 1-day experiment."
//
// Algorithm: symmetric central-difference gradient descent.
//   g[i] = (f(x + h_i·e_i) - f(x - h_i·e_i)) / (2·h_i)
//   h_i   = relativeStepSize · (Max_i - Min_i)
//   x_new = Clamp(x - α·g, Min, Max)
//
// Cost per step: 2·DimensionCount + 1 evaluations.
// For 34 SA dims: 69 evals/step × 15 steps = 1 035 calls.
//
// Best-tracking: the walk always steps in the gradient direction (may
// accept temporarily worse candidates). A separate best-seen tracker is
// updated only when an improvement is found — BestScore ≤ initialScore
// is guaranteed regardless of landscape noise.
//
// [Deterministic] on Polish(): the FD step sizes depend only on the
// objective bounds (deterministic), and the oracle is deterministic by
// the IObjective contract. The method is fully reproducible for equal
// (initialParams, objective) pairs.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Voxelforge.Optimization;

/// <summary>
/// Applies symmetric finite-difference gradient descent to polish an SA
/// winner. Cheap (~1 035 evaluations for 34 dims × 15 steps) and
/// oracle-agnostic; callers choose whether to use the PreScreen path or
/// the full <see cref="IObjective.Evaluate"/> physics oracle.
/// </summary>
public sealed class GradientPolishOptimizer
{
    private readonly IObjective _objective;
    private readonly int    _maxSteps;
    private readonly double _relativeStepSize;
    private readonly double _learningRate;
    private readonly int    _dim;
    private readonly double[] _lo;
    private readonly double[] _hi;

    /// <param name="maxSteps">Maximum gradient steps (default 15).</param>
    /// <param name="relativeStepSize">
    /// FD step h_i = relativeStepSize × (Max_i − Min_i). Default 1e-3.
    /// </param>
    /// <param name="learningRate">
    /// Gradient descent step size α. Default 0.05.
    /// </param>
    /// <param name="seed">
    /// Reserved for future stochastic tie-breaking; not currently used.
    /// Kept for API consistency with other optimizer constructors.
    /// </param>
    public GradientPolishOptimizer(
        IObjective objective,
        int    maxSteps         = 15,
        double relativeStepSize = 1e-3,
        double learningRate     = 0.05,
        int    seed             = 42)
    {
        if (objective is null) throw new ArgumentNullException(nameof(objective));
        if (maxSteps < 0)      throw new ArgumentOutOfRangeException(nameof(maxSteps), "maxSteps must be ≥ 0");
        if (relativeStepSize <= 0) throw new ArgumentOutOfRangeException(nameof(relativeStepSize));
        if (learningRate < 0)      throw new ArgumentOutOfRangeException(nameof(learningRate));

        _objective        = objective;
        _maxSteps         = maxSteps;
        _relativeStepSize = relativeStepSize;
        _learningRate     = learningRate;
        _dim = objective.DimensionCount;

        _lo = new double[_dim];
        _hi = new double[_dim];
        for (int i = 0; i < _dim; i++)
        {
            _lo[i] = objective.Variables[i].Min;
            _hi[i] = objective.Variables[i].Max;
        }
    }

    /// <summary>
    /// Polish <paramref name="initialParams"/> for up to
    /// <see cref="_maxSteps"/> gradient steps.
    /// </summary>
    [Deterministic]
    public PolishResult Polish(
        double[] initialParams,
        double   initialScore,
        CancellationToken cancellationToken = default)
    {
        if (initialParams is null) throw new ArgumentNullException(nameof(initialParams));
        if (initialParams.Length != _dim)
            throw new ArgumentException(
                $"initialParams length {initialParams.Length} ≠ DimensionCount {_dim}",
                nameof(initialParams));

        if (_maxSteps == 0)
        {
            return new PolishResult(
                BestParams:          (double[])initialParams.Clone(),
                BestScore:           initialScore,
                StepsCompleted:      0,
                ImprovementFraction: 0.0);
        }

        var x    = (double[])initialParams.Clone();
        var best = ((double[])x.Clone(), initialScore);

        var probe = new double[_dim];
        var grad  = new double[_dim];
        int stepsCompleted = 0;

        for (int step = 0; step < _maxSteps; step++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Compute symmetric FD gradient.
            double fCenter = _objective.Evaluate(x, cancellationToken).Score;

            for (int i = 0; i < _dim; i++)
            {
                double range = _hi[i] - _lo[i];
                if (range < 1e-14)
                {
                    grad[i] = 0.0;
                    continue;
                }
                double h = _relativeStepSize * range;

                // Forward probe.
                Array.Copy(x, probe, _dim);
                probe[i] = Math.Clamp(x[i] + h, _lo[i], _hi[i]);
                double fPlus = _objective.Evaluate(probe, cancellationToken).Score;

                // Backward probe.
                Array.Copy(x, probe, _dim);
                probe[i] = Math.Clamp(x[i] - h, _lo[i], _hi[i]);
                double fMinus = _objective.Evaluate(probe, cancellationToken).Score;

                // Use actual h achieved (may be clipped at boundary).
                double actualHFwd = Math.Abs(Math.Clamp(x[i] + h, _lo[i], _hi[i]) - x[i]);
                double actualHBwd = Math.Abs(x[i] - Math.Clamp(x[i] - h, _lo[i], _hi[i]));
                double denomH = actualHFwd + actualHBwd;
                grad[i] = denomH > 1e-14 ? (fPlus - fMinus) / denomH : 0.0;
            }

            if (cancellationToken.IsCancellationRequested) break;

            // Gradient step.
            var xNew = new double[_dim];
            for (int i = 0; i < _dim; i++)
                xNew[i] = Math.Clamp(x[i] - _learningRate * grad[i], _lo[i], _hi[i]);

            double scoreNew = _objective.Evaluate(xNew, cancellationToken).Score;
            stepsCompleted++;

            // Update best-seen if improved.
            if (scoreNew < best.Item2)
                best = ((double[])xNew.Clone(), scoreNew);

            x = xNew;
        }

        double denom = Math.Max(Math.Abs(initialScore), 1e-12);
        double improvementFraction = (initialScore - best.Item2) / denom;

        return new PolishResult(
            BestParams:          best.Item1,
            BestScore:           best.Item2,
            StepsCompleted:      stepsCompleted,
            ImprovementFraction: improvementFraction);
    }
}

/// <summary>Result of a <see cref="GradientPolishOptimizer.Polish"/> run.</summary>
/// <param name="BestParams">
/// Parameter vector achieving <see cref="BestScore"/>. Always satisfies
/// bounds; always has score ≤ initialScore (best-seen tracking).
/// </param>
/// <param name="BestScore">Best score seen across all gradient steps.</param>
/// <param name="StepsCompleted">
/// Number of gradient steps completed (≤ maxSteps; may be less if cancelled).
/// </param>
/// <param name="ImprovementFraction">
/// (initialScore − BestScore) / |initialScore|. Positive = improvement;
/// negative = worsened (can happen when initialScore is already the minimum).
/// </param>
public sealed record PolishResult(
    double[] BestParams,
    double   BestScore,
    int      StepsCompleted,
    double   ImprovementFraction);
