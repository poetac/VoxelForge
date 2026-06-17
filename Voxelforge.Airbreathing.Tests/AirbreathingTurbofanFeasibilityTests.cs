// AirbreathingTurbofanFeasibilityTests.cs — Sprint A8 acceptance for
// the turbofan-specific feasibility gates inlined into
// AirbreathingFeasibility.

using Voxelforge.Airbreathing;

namespace Voxelforge.Airbreathing.Tests;

public sealed class AirbreathingTurbofanFeasibilityTests
{
    private static AirbreathingEngineDesign Design(
        double phi = 0.30,
        double piC = 25.0,
        double bpr = 0.34)
        => new(
            Kind:                       AirbreathingEngineKind.Turbofan,
            InletThroatArea_m2:         0.37,
            CombustorArea_m2:           0.15,
            CombustorLength_m:          0.40,
            NozzleThroatArea_m2:        0.12,
            NozzleExitArea_m2:          0.18,
            EquivalenceRatio:           phi,
            CompressorPressureRatio:    piC,
            BypassRatio:                bpr);

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
    public void TooLowBypassRatio_FiresOutOfBandGate()
    {
        var r = AirbreathingOptimization.GenerateWith(Design(bpr: 0.05), Cond());
        Assert.Contains(r.Violations, v => v.ConstraintId == "BYPASS_RATIO_OUT_OF_BAND");
    }

    [Fact]
    public void TooHighBypassRatio_FiresOutOfBandGate()
    {
        var r = AirbreathingOptimization.GenerateWith(Design(bpr: 5.0), Cond());
        Assert.Contains(r.Violations, v => v.ConstraintId == "BYPASS_RATIO_OUT_OF_BAND");
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
    public void LeanBlowout_FiresOnTurbofanSameAsTurbojet()
    {
        // Cross-kind sanity: equivalence-ratio band gates fire
        // regardless of kind.
        var r = AirbreathingOptimization.GenerateWith(Design(phi: 0.10), Cond());
        Assert.Contains(r.Violations, v => v.ConstraintId == "COMBUSTOR_BLOWOUT_LEAN");
    }

    [Fact]
    public void MixerEnthalpyImbalance_DoesNotFire_WithConstantCp()
    {
        // Phase 1 ships constant cp throughout, so the mass-flow-
        // weighted mixer balance closes by construction. The gate is
        // forward-compatible defence for Stream B's cp(T) extension —
        // it must stay silent at every nominal operating point.
        var r = AirbreathingOptimization.GenerateWith(Design(), Cond());
        Assert.DoesNotContain(r.Violations, v => v.ConstraintId == "BYPASS_MIXER_ENTHALPY_IMBALANCE");
    }
}
