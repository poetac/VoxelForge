// BreachDirection.cs — Phase 2 of #627 (tracked under #743). Three-valued
// sign convention enum classifying how a FeasibilityViolation breaches its
// limit. Looked up per ConstraintId via ConstraintDirections.For; consumed
// by FeasibilityViolation.SignedBreachMagnitude.
//
// Non-SA optimizers (CmaEsOptimizer, BayesianOptimizer) use the signed
// magnitude for soft-penalty shaping instead of the +∞ hard-cliff score
// SA tolerates. SA stays bit-identical because it never reads this enum
// or the derived signed magnitude.

namespace Voxelforge.Optimization;

/// <summary>
/// Direction in which a <see cref="FeasibilityViolation"/> breaches its
/// limit. Phase 2 of [#627](https://github.com/poetac/voxelforge/issues/627)
/// (tracked under [#743](https://github.com/poetac/voxelforge/issues/743)).
/// </summary>
/// <remarks>
/// The convention is set per <c>ConstraintId</c> in
/// <see cref="ConstraintDirections"/> and consumed by
/// <see cref="FeasibilityViolation.SignedBreachMagnitude"/>. Foundation PR
/// seeds every known production <c>ConstraintId</c> at
/// <see cref="AboveLimit"/>; per-pillar refinement PRs change individual
/// entries to <see cref="BelowLimit"/> or <see cref="Categorical"/>
/// based on each emit-site predicate.
/// </remarks>
public enum BreachDirection
{
    /// <summary>
    /// <c>actual &gt; limit</c> ⇒ infeasible. Examples: <c>WALL_TEMP</c>,
    /// <c>COOLANT_T_EXCEEDED</c>, <c>BURST_MARGIN_INSUFFICIENT</c> (as
    /// "actual_safety_factor &gt; required" once inverted).
    /// </summary>
    AboveLimit = 0,

    /// <summary>
    /// <c>actual &lt; limit</c> ⇒ infeasible. Examples:
    /// <c>NPSH_INSUFFICIENT</c>, <c>YIELD_EXCEEDED</c> (safety factor
    /// below 1.0), <c>FEED_PRESSURE_INSUFFICIENT</c>.
    /// </summary>
    BelowLimit = 1,

    /// <summary>
    /// Non-numeric / sentinel comparison. Examples: <c>STABILITY_FAIL</c>
    /// (categorical rating), <c>IGNITER_MISSING</c> (boolean predicate),
    /// out-of-band gates whose <c>Limit</c> is the nearest violated bound
    /// (the sign of the breach has no consistent meaning across calls).
    /// <see cref="FeasibilityViolation.SignedBreachMagnitude"/> returns
    /// <c>NaN</c> for these.
    /// </summary>
    Categorical = 2,
}
