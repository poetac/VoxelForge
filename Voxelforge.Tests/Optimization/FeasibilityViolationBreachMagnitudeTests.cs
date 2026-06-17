// FeasibilityViolationBreachMagnitudeTests — Phase 1 of #627.
//
// Pins the BreachMagnitude derived property: |Actual - Limit| for
// numeric gates, NaN for categorical (NaN-input) gates. Phase 2 will
// add per-gate signed magnitudes (with a known sign convention per
// ConstraintId); Phase 1 just exposes the unsigned breach distance
// non-SA optimizers can already use for soft-penalty shaping.

using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Optimization;

public sealed class FeasibilityViolationBreachMagnitudeTests
{
    [Fact]
    public void BreachMagnitude_NumericGate_ActualAboveLimit_IsAbsDifference()
    {
        // WALL_TEMP-style gate: actual > limit means infeasible.
        var v = new FeasibilityViolation(
            ConstraintId: "WALL_TEMP",
            Description:  "Peak wall T 1500 K > limit 1200 K",
            ActualValue:  1500.0,
            Limit:        1200.0);
        Assert.Equal(300.0, v.BreachMagnitude, precision: 6);
    }

    [Fact]
    public void BreachMagnitude_NumericGate_ActualBelowLimit_IsAbsDifference()
    {
        // SF-style gate: actual < limit means infeasible.
        var v = new FeasibilityViolation(
            ConstraintId: "YIELD_EXCEEDED",
            Description:  "Min safety factor 0.8 < limit 1.0",
            ActualValue:  0.8,
            Limit:        1.0);
        // Magnitude is unsigned per Phase 1's design — 0.2, not -0.2.
        Assert.Equal(0.2, v.BreachMagnitude, precision: 6);
    }

    [Fact]
    public void BreachMagnitude_CategoricalGate_NanActual_ReturnsNan()
    {
        // Categorical gates (e.g. STABILITY_FAIL) carry NaN for ActualValue.
        var v = new FeasibilityViolation(
            ConstraintId: "STABILITY_FAIL",
            Description:  "Composite stability rating: Fail",
            ActualValue:  double.NaN,
            Limit:        double.NaN);
        Assert.True(double.IsNaN(v.BreachMagnitude));
    }

    [Fact]
    public void BreachMagnitude_CategoricalGate_NanLimit_ReturnsNan()
    {
        // Edge case: numeric ActualValue but NaN Limit (e.g. an
        // un-anchored categorical), defensive Phase 1 says NaN here too.
        var v = new FeasibilityViolation(
            ConstraintId: "EXOTIC",
            Description:  "Test",
            ActualValue:  42.0,
            Limit:        double.NaN);
        Assert.True(double.IsNaN(v.BreachMagnitude));
    }

    [Fact]
    public void BreachMagnitude_ZeroBreach_ReturnsZero()
    {
        // Hypothetical gate where actual exactly equals limit — magnitude is 0.
        var v = new FeasibilityViolation(
            ConstraintId: "EXACT",
            Description:  "Exact",
            ActualValue:  5.0,
            Limit:        5.0);
        Assert.Equal(0.0, v.BreachMagnitude, precision: 12);
    }
}
