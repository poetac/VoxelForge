// SoftPenalty.cs — Phase 2 of #627 (tracked under #743). Shared helper
// used by CmaEsOptimizer + BayesianOptimizer when constructed with
// useSoftPenalty=true. Computes a sigmoid-saturated soft penalty from
// a candidate's FeasibilityViolations as a finite-valued replacement
// for the +∞ hard-cliff score the feasibility gate normally emits.
//
// SA (MultiChainOptimizer) does NOT call this helper — its score path
// stays bit-identical with the +∞ cliff intact.

using System;

namespace Voxelforge.Optimization;

/// <summary>
/// Sigmoid-saturated soft-penalty computation for non-SA optimizers
/// (CMA-ES, Bayesian). Phase 2 of
/// [#627](https://github.com/poetac/voxelforge/issues/627) (tracked
/// under [#743](https://github.com/poetac/voxelforge/issues/743)).
/// </summary>
internal static class SoftPenalty
{
    /// <summary>
    /// Per-violation tanh saturation contributes between 0 and 1; this
    /// scale multiplies the summed contribution. Chosen large enough
    /// (1e6) that any infeasible candidate ranks below typical feasible
    /// scores in the rocket-pillar / EP / marine / airbreathing / nuclear
    /// score ranges (all observed maxima below 1e5 in production runs).
    /// </summary>
    internal const double PenaltyScale = 1.0e6;

    /// <summary>
    /// Tanh saturation point (in natural units of the violation's
    /// <c>SignedBreachMagnitude</c>). Magnitudes around 0.1 land at
    /// <c>tanh(1) ≈ 0.76</c>; magnitudes ≫ 0.1 saturate near 1.0.
    /// </summary>
    internal const double BreachScale = 0.1;

    /// <summary>
    /// Returns the soft-penalty score for <paramref name="eval"/>.
    /// When <c>Violations.Count == 0</c> (feasible candidate), returns
    /// <c>eval.Score</c> unchanged. Otherwise replaces the score with
    /// <c>PenaltyScale · Σᵢ tanh(|SignedBreachMagnitude(vᵢ)| / BreachScale)</c>;
    /// categorical violations (NaN signed magnitude) fall back to the
    /// unsigned <see cref="FeasibilityViolation.BreachMagnitude"/>, then
    /// saturate at <c>tanh(∞) = 1.0</c> if that is also NaN.
    /// </summary>
    public static double Compute(EvaluationResult eval)
    {
        if (eval.Violations is not { Count: > 0 })
            return eval.Score;

        double penalty = 0.0;
        foreach (FeasibilityViolation v in eval.Violations)
        {
            double mag = Math.Abs(v.SignedBreachMagnitude);
            if (double.IsNaN(mag))
            {
                // Categorical (NaN signed) — try unsigned magnitude;
                // if that is NaN too (categorical w/ NaN actual/limit),
                // treat as saturated infinity for tanh.
                mag = double.IsNaN(v.BreachMagnitude) ? double.PositiveInfinity : v.BreachMagnitude;
            }
            penalty += Math.Tanh(mag / BreachScale);
        }
        return PenaltyScale * penalty;
    }
}
