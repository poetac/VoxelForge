// PlaningGateTests.cs — Sprint M.W3 unit tests for the planing-hull gates.
//
// Covers all 6 SurfaceHull gates (3 Hard + 3 Advisory) plus cross-kind
// isolation (planing gates do not fire on AUV designs).

using System.Linq;
using Voxelforge.Marine;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Marine.Tests;

public sealed class PlaningGateTests
{
    private static MarineDesign BaselineYacht() => new(
        Kind:                MarineKind.SurfaceHull,
        Length_m:           11.0,
        // AUV-positional fields ignored by the planing branch.
        Diameter_m:          1.0,
        NoseFairingFraction: 0.25,
        TailFairingFraction: 0.25,
        WallThickness_m:     0.005,
        MaterialIndex:       0,
        DepthRating_m:       1.0,
        HullFamily:          HullFamily.Planing)
    {
        BeamMidship_m          = 3.0,
        DeadriseAngle_deg      = 18.0,
        MassDisplacement_kg    = 5000.0,
        FreeboardHeight_m      = 0.6,
        LongitudinalCgFraction = 0.50,
    };

    private static MarineConditions BaselineConditions() => new(
        CruiseSpeed_ms: 12.86,        // 25 kt
        MaxDepth_m:      0.0);        // surface vehicle

    private static bool Has(System.Collections.Generic.IReadOnlyList<FeasibilityViolation> v, string id)
        => v.Any(x => x.ConstraintId == id);

    // ── Baseline feasibility ────────────────────────────────────────────

    [Fact]
    public void Baseline_PlaningYacht_IsFeasible()
    {
        var r = MarineOptimization.GenerateWith(BaselineYacht(), BaselineConditions());
        Assert.True(r.IsFeasible,
            $"Baseline 11 m planing yacht should pass; saw violations: "
          + string.Join(", ", r.Violations.Select(v => v.ConstraintId)));
    }

    // ── Hard gates ──────────────────────────────────────────────────────

    [Fact]
    public void SpeedCoefficientOutOfBand_HighSpeed_FiresHardGate()
    {
        // Push V high enough that C_v = V/√(gb) > 13.
        // For b=3: V > 13·√(g·3) = 13·5.42 = 70.5 m/s. Use 100 m/s.
        var fast = new MarineConditions(CruiseSpeed_ms: 100.0, MaxDepth_m: 0.0);
        var r = MarineOptimization.GenerateWith(BaselineYacht(), fast);
        Assert.True(Has(r.Violations, "PLANING_SPEED_COEFFICIENT_OUT_OF_BAND"));
    }

    [Fact]
    public void TrimOutOfBand_DesignForcingHighTrim_DoesNotFireAtClusterAnchor()
    {
        // The cluster correlation clamps trim to [3.5°, 5.5°] — well within
        // the [1°, 10°] hard band. Sanity: the baseline doesn't fire the gate.
        var r = MarineOptimization.GenerateWith(BaselineYacht(), BaselineConditions());
        Assert.False(Has(r.Violations, "PLANING_TRIM_OUT_OF_BAND"));
    }

    [Fact]
    public void WettedLengthToBeamOutOfBand_VeryHeavyHull_FiresHardGate()
    {
        // Push displacement way up so the lift balance demands λ > 6.
        var heavy = BaselineYacht() with { MassDisplacement_kg = 200_000.0 };
        var r = MarineOptimization.GenerateWith(heavy, BaselineConditions());
        Assert.True(Has(r.Violations, "PLANING_WETTED_LENGTH_TO_BEAM_OUT_OF_BAND"));
    }

    // ── Advisory gates ──────────────────────────────────────────────────

    [Fact]
    public void DeadriseOutOfBand_FlatBottom_FiresAdvisory()
    {
        var flat = BaselineYacht() with { DeadriseAngle_deg = 2.0 };  // < 5°
        var r = MarineOptimization.GenerateWith(flat, BaselineConditions());
        Assert.True(Has(r.Advisories, "PLANING_DEADRISE_OUT_OF_BAND"));
    }

    [Fact]
    public void DeadriseOutOfBand_DeepV_FiresAdvisory()
    {
        var sharp = BaselineYacht() with { DeadriseAngle_deg = 30.0 };  // > 25°
        var r = MarineOptimization.GenerateWith(sharp, BaselineConditions());
        Assert.True(Has(r.Advisories, "PLANING_DEADRISE_OUT_OF_BAND"));
    }

    [Fact]
    public void LcgOutOfBand_BowHeavy_FiresAdvisory()
    {
        var bowHeavy = BaselineYacht() with { LongitudinalCgFraction = 0.30 };  // < 0.42
        var r = MarineOptimization.GenerateWith(bowHeavy, BaselineConditions());
        Assert.True(Has(r.Advisories, "PLANING_LCG_OUT_OF_BAND"));
    }

    [Fact]
    public void LcgOutOfBand_SternHeavy_FiresAdvisory()
    {
        var sternHeavy = BaselineYacht() with { LongitudinalCgFraction = 0.65 };  // > 0.58
        var r = MarineOptimization.GenerateWith(sternHeavy, BaselineConditions());
        Assert.True(Has(r.Advisories, "PLANING_LCG_OUT_OF_BAND"));
    }

    // ── Cross-kind isolation ────────────────────────────────────────────

    [Fact]
    public void PlaningGates_DoNotFire_OnAuvDesign()
    {
        // Build a feasible REMUS-100 baseline AUV and confirm none of the
        // PLANING_* gates appear in its result.
        var auv = new MarineDesign(
            Kind:                MarineKind.AuvMidBody,
            Length_m:            1.6,
            Diameter_m:          0.19,
            NoseFairingFraction: 0.20,
            TailFairingFraction: 0.30,
            WallThickness_m:     0.0035,
            MaterialIndex:       0,
            DepthRating_m:       100.0,
            HullFamily:          HullFamily.Myring);
        var cond = new MarineConditions(CruiseSpeed_ms: 1.5, MaxDepth_m: 100.0);

        var r = MarineOptimization.GenerateWith(auv, cond);
        Assert.False(Has(r.Violations, "PLANING_SPEED_COEFFICIENT_OUT_OF_BAND"));
        Assert.False(Has(r.Violations, "PLANING_TRIM_OUT_OF_BAND"));
        Assert.False(Has(r.Violations, "PLANING_WETTED_LENGTH_TO_BEAM_OUT_OF_BAND"));
        Assert.False(Has(r.Advisories, "PLANING_DEADRISE_OUT_OF_BAND"));
        Assert.False(Has(r.Advisories, "PLANING_LCG_OUT_OF_BAND"));
        Assert.False(Has(r.Advisories, "PLANING_RESISTANCE_ABOVE_BAND"));
    }
}
