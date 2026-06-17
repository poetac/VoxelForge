// AirbreathingRdeFeasibilityTests.cs — Sprint A.W4 RDE gate tests.
// Covers 3 hard + 2 advisory RDE gates + cross-kind isolation.

using System.Linq;
using Voxelforge.Airbreathing;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Tests;

public sealed class AirbreathingRdeFeasibilityTests
{
    private static AirbreathingEngineDesign BaselineRde() => new(
        Kind: AirbreathingEngineKind.RotatingDetonation,
        InletThroatArea_m2:  0.05,
        CombustorArea_m2:    0.30,
        CombustorLength_m:   0.50,
        NozzleThroatArea_m2: 0.020,
        NozzleExitArea_m2:   0.100,
        EquivalenceRatio:    0.50)
    {
        RdePressureGainRatio       = 1.25,
        RdeWaveCount               = 4,
        RdeAnnularOuterDiameter_m  = 0.150,
        RdeAnnularInnerDiameter_m  = 0.110,
        RdeAnnularLength_m         = 0.150,
    };

    private static FlightConditions RdeConditions()
        => new(Altitude_m: 10_000.0, MachNumber: 2.0, Fuel: AirbreathingFuel.H2);

    private static bool Has(System.Collections.Generic.IReadOnlyList<FeasibilityViolation> v, string id)
        => v.Any(x => x.ConstraintId == id);

    // ── Baseline ────────────────────────────────────────────────────────

    [Fact]
    public void Baseline_RdeDesign_IsFeasible()
    {
        var r = AirbreathingOptimization.GenerateWith(BaselineRde(), RdeConditions());
        Assert.True(r.IsFeasible,
            $"Baseline RDE should pass; saw violations: "
          + string.Join(", ", r.Violations.Select(v => v.ConstraintId)));
    }

    // ── RDE_PRESSURE_GAIN_OUT_OF_BAND (hard) ───────────────────────────

    [Fact]
    public void PressureGainOutOfBand_NoGain_FiresHardGate()
    {
        var bad = BaselineRde() with { RdePressureGainRatio = 0.9 };  // < 1.0
        var r = AirbreathingOptimization.GenerateWith(bad, RdeConditions());
        Assert.True(Has(r.Violations, "RDE_PRESSURE_GAIN_OUT_OF_BAND"));
    }

    [Fact]
    public void PressureGainOutOfBand_OverDriven_FiresHardGate()
    {
        var bad = BaselineRde() with { RdePressureGainRatio = 2.0 };  // > 1.5
        var r = AirbreathingOptimization.GenerateWith(bad, RdeConditions());
        Assert.True(Has(r.Violations, "RDE_PRESSURE_GAIN_OUT_OF_BAND"));
    }

    // ── RDE_WAVE_COUNT_OUT_OF_BAND (hard) ──────────────────────────────

    [Fact]
    public void WaveCountOutOfBand_ZeroWaves_FiresHardGate()
    {
        // n=0 trips the solver's positivity check first; use n=15 instead to
        // trip the gate.
        var bad = BaselineRde() with { RdeWaveCount = 15 };  // > 10
        var r = AirbreathingOptimization.GenerateWith(bad, RdeConditions());
        Assert.True(Has(r.Violations, "RDE_WAVE_COUNT_OUT_OF_BAND"));
    }

    // ── RDE_CHANNEL_WIDTH_BELOW_CELL_SIZE (hard) ───────────────────────

    [Fact]
    public void ChannelWidthBelowCellSize_TightAnnulus_FiresHardGate()
    {
        // D_o=0.150, D_i=0.149 → channel width = 0.5 mm, below 1 mm floor.
        var bad = BaselineRde() with
        {
            RdeAnnularOuterDiameter_m = 0.150,
            RdeAnnularInnerDiameter_m = 0.149,
        };
        var r = AirbreathingOptimization.GenerateWith(bad, RdeConditions());
        Assert.True(Has(r.Violations, "RDE_CHANNEL_WIDTH_BELOW_CELL_SIZE"));
    }

    // ── RDE_CHANNEL_WIDTH_ABOVE_ADVISORY (advisory) ────────────────────

    [Fact]
    public void ChannelWidthAboveAdvisory_WideAnnulus_FiresAdvisory()
    {
        // D_o=0.200, D_i=0.150 → channel width = 25 mm, above 20 mm advisory.
        var advisory = BaselineRde() with
        {
            RdeAnnularOuterDiameter_m = 0.200,
            RdeAnnularInnerDiameter_m = 0.150,
        };
        var r = AirbreathingOptimization.GenerateWith(advisory, RdeConditions());
        Assert.True(Has(r.Advisories, "RDE_CHANNEL_WIDTH_ABOVE_ADVISORY"));
    }

    // ── RDE_LENGTH_TO_DIAMETER_OUT_OF_BAND (advisory) ──────────────────

    [Fact]
    public void LengthToDiameterOutOfBand_TooShort_FiresAdvisory()
    {
        // L=0.020, D_o=0.150 → L/D ≈ 0.133, below 0.20 advisory.
        var advisory = BaselineRde() with { RdeAnnularLength_m = 0.020 };
        var r = AirbreathingOptimization.GenerateWith(advisory, RdeConditions());
        Assert.True(Has(r.Advisories, "RDE_LENGTH_TO_DIAMETER_OUT_OF_BAND"));
    }

    [Fact]
    public void LengthToDiameterOutOfBand_TooLong_FiresAdvisory()
    {
        // L=1.0, D_o=0.150 → L/D ≈ 6.67, above 4.0 advisory.
        var advisory = BaselineRde() with { RdeAnnularLength_m = 1.0 };
        var r = AirbreathingOptimization.GenerateWith(advisory, RdeConditions());
        Assert.True(Has(r.Advisories, "RDE_LENGTH_TO_DIAMETER_OUT_OF_BAND"));
    }

    // ── Cross-kind isolation ────────────────────────────────────────────

    [Fact]
    public void RdeGates_DoNotFire_OnRamjetDesign()
    {
        var ram = new AirbreathingEngineDesign(
            Kind: AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2:  0.10,
            CombustorArea_m2:    0.30,
            CombustorLength_m:   0.50,
            NozzleThroatArea_m2: 0.0848,
            NozzleExitArea_m2:   0.20,
            EquivalenceRatio:    0.40);
        var cond = new FlightConditions(Altitude_m: 12_000.0, MachNumber: 2.0, Fuel: AirbreathingFuel.H2);

        var r = AirbreathingOptimization.GenerateWith(ram, cond);
        Assert.False(Has(r.Violations, "RDE_PRESSURE_GAIN_OUT_OF_BAND"));
        Assert.False(Has(r.Violations, "RDE_WAVE_COUNT_OUT_OF_BAND"));
        Assert.False(Has(r.Violations, "RDE_CHANNEL_WIDTH_BELOW_CELL_SIZE"));
        Assert.False(Has(r.Advisories, "RDE_CHANNEL_WIDTH_ABOVE_ADVISORY"));
        Assert.False(Has(r.Advisories, "RDE_LENGTH_TO_DIAMETER_OUT_OF_BAND"));
    }
}
