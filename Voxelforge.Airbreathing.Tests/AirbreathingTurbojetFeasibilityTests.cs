// AirbreathingTurbojetFeasibilityTests.cs — Sprint A7 acceptance for
// the turbojet-specific feasibility gates inlined into
// AirbreathingFeasibility.

using Voxelforge.Airbreathing;

namespace Voxelforge.Airbreathing.Tests;

public sealed class AirbreathingTurbojetFeasibilityTests
{
    private static AirbreathingEngineDesign Design(double phi = 0.22, double piC = 8.0)
        => new(
            Kind:                       AirbreathingEngineKind.Turbojet,
            InletThroatArea_m2:         0.115,
            CombustorArea_m2:           0.10,
            CombustorLength_m:          0.30,
            NozzleThroatArea_m2:        0.060,
            NozzleExitArea_m2:          0.078,
            EquivalenceRatio:           phi,
            CompressorPressureRatio:    piC);

    private static FlightConditions Cond()
        => new(0.0, 0.001, AirbreathingFuel.Jp8);

    [Fact]
    public void NominalDesign_PassesAllGates()
    {
        var r = AirbreathingOptimization.GenerateWith(Design(), Cond());
        Assert.True(r.IsFeasible,
            $"Expected feasibility; got: {string.Join(", ", System.Linq.Enumerable.Select(r.Violations, v => v.ConstraintId))}");
    }

    [Fact]
    public void TooLowCompressorRatio_FiresOutOfBandGate()
    {
        var r = AirbreathingOptimization.GenerateWith(Design(piC: 1.5), Cond());
        Assert.Contains(r.Violations, v => v.ConstraintId == "COMPRESSOR_RATIO_OUT_OF_BAND");
    }

    [Fact]
    public void TooHighCompressorRatio_FiresOutOfBandGate()
    {
        var r = AirbreathingOptimization.GenerateWith(Design(piC: 60.0), Cond());
        Assert.Contains(r.Violations, v => v.ConstraintId == "COMPRESSOR_RATIO_OUT_OF_BAND");
    }

    [Fact]
    public void HighEquivalenceRatio_FiresTitExceededGate()
    {
        // φ = 0.55 with Jp8 + π_c = 8 + cp(T)-aware combustor drives
        // T_t4 to ≈ 1770 K — above the 1700 K turbine-inlet uncooled-
        // blade ceiling. φ=0.40 (the original test value) gave T_t4 ≈
        // 1657 K under constant-cp physics and 1500 K under cp(T), both
        // below the gate threshold; φ=0.55 fires the gate cleanly.
        var r = AirbreathingOptimization.GenerateWith(Design(phi: 0.55), Cond());
        Assert.Contains(r.Violations, v => v.ConstraintId == "TIT_EXCEEDED");
    }

    [Fact]
    public void LeanBlowout_FiresOnTurbojetSameAsRamjet()
    {
        // Cross-kind sanity: equivalence-ratio band gates fire
        // regardless of kind.
        var r = AirbreathingOptimization.GenerateWith(Design(phi: 0.10), Cond());
        Assert.Contains(r.Violations, v => v.ConstraintId == "COMBUSTOR_BLOWOUT_LEAN");
    }

    [Fact]
    public void NominalDesign_HasNoSurgeMarginAdvisory()
    {
        // J85 design point (π_c=8, ṁ_corr=20) sits at ~22 % surge
        // margin — well above the 10 % advisory floor. No advisory
        // should fire on a healthy design.
        var r = AirbreathingOptimization.GenerateWith(Design(), Cond());
        Assert.DoesNotContain(r.Advisories, v => v.ConstraintId == "SURGE_MARGIN_INSUFFICIENT");
    }

    [Fact]
    public void NearSurge_FiresAdvisoryWithoutBlockingFeasibility()
    {
        // π_c=9.0 sits inside the 100 % N speed line but with surge
        // margin < 10 % — fires the advisory, does not affect
        // IsFeasible.
        var r = AirbreathingOptimization.GenerateWith(Design(piC: 9.0), Cond());
        Assert.Contains(r.Advisories, v => v.ConstraintId == "SURGE_MARGIN_INSUFFICIENT");
        // Advisory does NOT gate IsFeasible — engine still runs.
        Assert.True(r.IsFeasible,
            $"Advisory-level gate should not block feasibility; got violations: "
          + $"{string.Join(", ", System.Linq.Enumerable.Select(r.Violations, v => v.ConstraintId))}");
    }

    [Fact]
    public void AboveSurgeLine_FiresHardCorrectedMassFlowGate()
    {
        // π_c=10.0 is above the 100 % N surge peak (9.4) — operating
        // point is past the surge line. CORRECTED_MASS_FLOW_OUT_OF_MAP
        // fires as a hard infeasibility.
        var r = AirbreathingOptimization.GenerateWith(Design(piC: 10.0), Cond());
        // Note: COMPRESSOR_RATIO_OUT_OF_BAND only fires above 50, so
        // π=10 doesn't trip it — the surge gate is the load-bearing
        // check here.
        Assert.Contains(r.Violations, v => v.ConstraintId == "CORRECTED_MASS_FLOW_OUT_OF_MAP");
        Assert.False(r.IsFeasible);
    }
}
