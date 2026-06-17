// SignedBreachMagnitudeTests — Phase 2 of #627 (tracked under #743). Pins
// the directional semantics of FeasibilityViolation.SignedBreachMagnitude:
//   • AboveLimit gates: positive = ActualValue − Limit
//   • BelowLimit gates: positive = Limit − ActualValue
//   • Categorical gates: NaN (no numeric direction)
//   • NaN input → NaN output (always; whichever side)
//   • Unknown ConstraintId → falls back to AboveLimit default
//
// Foundation PR seeds every known production ConstraintId at AboveLimit;
// these tests pin the lookup-based machinery so per-pillar refinement
// PRs can change individual entries without breaking the contract.

using System.Collections.Generic;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Optimization;

public sealed class SignedBreachMagnitudeTests
{
    [Fact]
    public void SignedBreachMagnitude_KnownAboveLimitGate_ReturnsActualMinusLimit()
    {
        // WALL_TEMP is AboveLimit (foundation default for every entry).
        // Sign convention: positive = depth into infeasible region.
        var v = new FeasibilityViolation(
            ConstraintId: "WALL_TEMP",
            Description:  "Peak wall T 1500 K > limit 1200 K",
            ActualValue:  1500.0,
            Limit:        1200.0);
        Assert.Equal(BreachDirection.AboveLimit, ConstraintDirections.For("WALL_TEMP"));
        Assert.Equal(300.0, v.SignedBreachMagnitude, precision: 6);
    }

    [Fact]
    public void SignedBreachMagnitude_NaNActual_ReturnsNaN()
    {
        // Categorical gates (e.g. STABILITY_FAIL) carry NaN for ActualValue.
        // Signed magnitude is NaN regardless of the per-gate direction.
        var v = new FeasibilityViolation(
            ConstraintId: "STABILITY_FAIL",
            Description:  "Composite stability rating: Fail",
            ActualValue:  double.NaN,
            Limit:        double.NaN);
        Assert.True(double.IsNaN(v.SignedBreachMagnitude));
    }

    [Fact]
    public void SignedBreachMagnitude_NaNLimit_ReturnsNaN()
    {
        // Defensive: a numeric ActualValue with NaN Limit also yields NaN.
        var v = new FeasibilityViolation(
            ConstraintId: "WALL_TEMP",
            Description:  "x",
            ActualValue:  100.0,
            Limit:        double.NaN);
        Assert.True(double.IsNaN(v.SignedBreachMagnitude));
    }

    [Fact]
    public void SignedBreachMagnitude_ZeroBreach_ReturnsZero()
    {
        // ActualValue exactly equals Limit → signed magnitude is 0 (on
        // the boundary, neither inside nor outside the infeasible region).
        var v = new FeasibilityViolation(
            ConstraintId: "WALL_TEMP",
            Description:  "Exact boundary",
            ActualValue:  1200.0,
            Limit:        1200.0);
        Assert.Equal(0.0, v.SignedBreachMagnitude, precision: 12);
    }

    [Fact]
    public void ConstraintDirections_For_UnknownConstraintId_FallsBackToAboveLimit()
    {
        // Synthetic / test ConstraintIds not in the production map default
        // to AboveLimit. This keeps test fixtures and future-gate IDs
        // from throwing or returning a special-cased value.
        Assert.Equal(BreachDirection.AboveLimit, ConstraintDirections.For("ZZZ_UNKNOWN_GATE_FOR_TEST_ONLY"));
        var v = new FeasibilityViolation(
            ConstraintId: "ZZZ_UNKNOWN_GATE_FOR_TEST_ONLY",
            Description:  "x",
            ActualValue:  10.0,
            Limit:        5.0);
        Assert.Equal(5.0, v.SignedBreachMagnitude, precision: 6);
    }

    [Fact]
    public void ConstraintDirections_For_PreservesOrdinalComparison()
    {
        // Map uses StringComparer.Ordinal — case-sensitive lookups only.
        // The lowercased version of a known ID should NOT match (and
        // hence fall back to the AboveLimit default).
        Assert.True(ConstraintDirections.Map.ContainsKey("WALL_TEMP"));
        Assert.False(ConstraintDirections.Map.ContainsKey("wall_temp"));
        Assert.Equal(BreachDirection.AboveLimit, ConstraintDirections.For("wall_temp"));
    }

    [Fact]
    public void ConstraintDirections_Map_AllPillarsRepresented()
    {
        // Spot-check one ConstraintId from each production pillar plus
        // the cross-pillar gates. Catches accidental empty-map or
        // section-deleted regressions in future refactors.
        Assert.True(ConstraintDirections.Map.ContainsKey("WALL_TEMP"),                        "Rocket WALL_TEMP missing");
        Assert.True(ConstraintDirections.Map.ContainsKey("AEROSPIKE_PLUG_WALL_TEMP"),         "Aerospike AEROSPIKE_PLUG_WALL_TEMP missing");
        Assert.True(ConstraintDirections.Map.ContainsKey("MONOLITHIC_BODY_INTERSECTION"),     "Monolithic MONOLITHIC_BODY_INTERSECTION missing");
        Assert.True(ConstraintDirections.Map.ContainsKey("VOXEL_RESOLUTION"),                 "VOXEL_RESOLUTION missing");
        Assert.True(ConstraintDirections.Map.ContainsKey("OBJECTIVE_EVALUATION_TIMEOUT"),     "OBJECTIVE_EVALUATION_TIMEOUT missing");
        Assert.True(ConstraintDirections.Map.ContainsKey("COMBUSTOR_BLOWOUT_LEAN"),           "Airbreathing COMBUSTOR_BLOWOUT_LEAN missing");
        Assert.True(ConstraintDirections.Map.ContainsKey("HET_DISCHARGE_VOLTAGE_OUT_OF_BAND"),"EP HET_DISCHARGE_VOLTAGE_OUT_OF_BAND missing");
        Assert.True(ConstraintDirections.Map.ContainsKey("HULL_BUOYANCY_NEGATIVE"),           "Marine HULL_BUOYANCY_NEGATIVE missing");
        Assert.True(ConstraintDirections.Map.ContainsKey("NTR_REACTOR_OVERTEMP"),             "Nuclear NTR_REACTOR_OVERTEMP missing");
    }

    [Fact]
    public void ConstraintDirections_Map_HasFoundationEntryCount()
    {
        // Foundation PR seeds ~180 entries (every known production gate
        // ID across the 5 pillars + aerospike/monolithic/wrapper IDs).
        // Loose lower bound catches "accidentally empty" regressions
        // without coupling the test to the exact pre-refinement count.
        Assert.True(
            ConstraintDirections.Map.Count >= 150,
            $"ConstraintDirections.Map has only {ConstraintDirections.Map.Count} entries; foundation seeds ~180");
    }

    [Fact]
    public void ConstraintDirections_Map_EveryEntryStartsAtAboveLimitInFoundation()
    {
        // Foundation PR ships every entry at AboveLimit; per-pillar PRs
        // refine individual entries to BelowLimit / Categorical. This
        // pin documents the foundation invariant — REMOVE / RELAX it
        // when the first per-pillar refinement PR lands.
        IReadOnlyDictionary<string, BreachDirection> map = ConstraintDirections.Map;
        foreach (KeyValuePair<string, BreachDirection> kv in map)
        {
            Assert.Equal(BreachDirection.AboveLimit, kv.Value);
        }
    }
}
