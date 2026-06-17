// BimodalNtrGateTests.cs — Sprint NU.W3 unit tests for the 4 new bimodal
// gates wired through NuclearOptimization. Each gate has one fire + one
// non-fire test.

using System.Linq;
using Voxelforge.Nuclear;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class BimodalNtrGateTests
{
    private static NuclearThermalDesign BaselineBimodal() => new NuclearThermalDesign(
        Kind:                    NuclearKind.BimodalNtr,
        ReactorThermalPower_MW:  1.5,
        ReactorCoreLength_mm:    500.0,
        ReactorCoreDiameter_mm:  300.0,
        FuelLoadingFraction:     0.65,
        PropellantMassFlow_kgs:  0.5,
        ChamberPressure_bar:     40.0,
        ThroatRadius_mm:         50.0,
        ExpansionRatio:          100.0,
        NozzleLength_mm:         2000.0,
        RegenChannelDepth_mm:    2.0,
        RegenChannelCount:       80,
        NozzleWallThickness_mm:  1.5,
        NozzleChannelWidth_mm:   3.0,
        NozzleManifoldDepth_mm:  5.0) with
    {
        BimodalMode                  = BimodalMode.Hybrid,
        ElectricPowerTarget_kWe      = 100.0,
        BraytonTurbineInletTemp_K    = 1300.0,
        BraytonHePressure_bar        = 120.0,
        AlternatorRpm                = 45_000.0,
    };

    private static NuclearThermalConditions Cond() =>
        new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    private static bool Has(System.Collections.Generic.IReadOnlyList<FeasibilityViolation> v, string id)
        => v.Any(x => x.ConstraintId == id);

    // ── Baseline ─────────────────────────────────────────────────────────

    [Fact]
    public void Baseline_BimodalDesign_IsFeasible()
    {
        var r = NuclearOptimization.GenerateWith(BaselineBimodal(), Cond());
        Assert.True(r.IsFeasible,
            $"Baseline bimodal NTR should pass; saw: "
          + string.Join(", ", r.Violations.Select(v => v.ConstraintId)));
    }

    [Fact]
    public void Baseline_PopulatesBraytonResultFields()
    {
        var r = NuclearOptimization.GenerateWith(BaselineBimodal(), Cond());
        Assert.True(double.IsFinite(r.ElectricPowerOutput_kWe));
        Assert.True(r.ElectricPowerOutput_kWe > 0);
        Assert.True(double.IsFinite(r.BraytonThermalEfficiency));
        Assert.True(double.IsFinite(r.ReactorPowerToBrayton_MW));
    }

    // ── NTR_BIMODAL_BRAYTON_TURBINE_OVERTEMP (hard) ─────────────────────

    [Fact]
    public void BraytonTurbineOvertemp_HotInletAboveLimit_FiresHardGate()
    {
        var bad = BaselineBimodal() with { BraytonTurbineInletTemp_K = 1700.0 };  // > 1500 K hard
        var r = NuclearOptimization.GenerateWith(bad, Cond());
        Assert.True(Has(r.Violations, "NTR_BIMODAL_BRAYTON_TURBINE_OVERTEMP"));
    }

    // ── NTR_BIMODAL_ALTERNATOR_RPM_OUT_OF_BAND (hard) ───────────────────

    [Fact]
    public void AlternatorRpmOutOfBand_TooLow_FiresHardGate()
    {
        var bad = BaselineBimodal() with { AlternatorRpm = 5_000.0 };  // < 10k hard floor
        var r = NuclearOptimization.GenerateWith(bad, Cond());
        Assert.True(Has(r.Violations, "NTR_BIMODAL_ALTERNATOR_RPM_OUT_OF_BAND"));
    }

    [Fact]
    public void AlternatorRpmOutOfBand_TooHigh_FiresHardGate()
    {
        var bad = BaselineBimodal() with { AlternatorRpm = 150_000.0 };  // > 100k hard ceiling
        var r = NuclearOptimization.GenerateWith(bad, Cond());
        Assert.True(Has(r.Violations, "NTR_BIMODAL_ALTERNATOR_RPM_OUT_OF_BAND"));
    }

    // ── NTR_BIMODAL_BRAYTON_THERMAL_EFFICIENCY_LOW (advisory) ───────────

    [Fact]
    public void BraytonThermalEfficiencyLow_LowHotSide_FiresAdvisory()
    {
        // T_hot=500 K → Carnot = 1 - 400/500 = 0.20. With 0.88·0.86·0.96·0.95
        // × 0.20 ≈ 0.138 — below 0.15 advisory floor.
        var advisory = BaselineBimodal() with { BraytonTurbineInletTemp_K = 500.0 };
        var r = NuclearOptimization.GenerateWith(advisory, Cond());
        Assert.True(Has(r.Advisories, "NTR_BIMODAL_BRAYTON_THERMAL_EFFICIENCY_LOW"));
    }

    // ── NTR_BIMODAL_REACTOR_TAP_EXCESSIVE (advisory) ────────────────────

    [Fact]
    public void ReactorTapExcessive_HighElectricTarget_FiresAdvisory()
    {
        // Reactor 1.5 MW. Brayton η_real at T_hot=1300, T_cold=400 ≈ 0.48
        // (Carnot 0.69 × 0.88·0.86·0.96·0.95 component derating). Requesting
        // an electric target that demands more than the full reactor power
        // forces the solver's reactor-cap path:
        //   Q_brayton needed = 1500 / 0.48 ≈ 3.1 MW > 1.5 MW reactor → cap.
        // After cap, ReactorPowerToBrayton = 1.5 MW → tap ratio = 1.0 → fires.
        var advisory = BaselineBimodal() with { ElectricPowerTarget_kWe = 1500.0 };
        var r = NuclearOptimization.GenerateWith(advisory, Cond());
        Assert.True(Has(r.Advisories, "NTR_BIMODAL_REACTOR_TAP_EXCESSIVE"));
    }

    // ── Cross-mode isolation ─────────────────────────────────────────────

    [Fact]
    public void BimodalGates_DoNotFire_OnThrustMode()
    {
        // Even with bimodal Kind, BimodalMode=Thrust should skip the Brayton
        // pipeline and produce no bimodal gates.
        var thrustOnly = BaselineBimodal() with { BimodalMode = BimodalMode.Thrust };
        var r = NuclearOptimization.GenerateWith(thrustOnly, Cond());
        Assert.False(Has(r.Violations, "NTR_BIMODAL_BRAYTON_TURBINE_OVERTEMP"));
        Assert.False(Has(r.Violations, "NTR_BIMODAL_ALTERNATOR_RPM_OUT_OF_BAND"));
        Assert.False(Has(r.Advisories, "NTR_BIMODAL_BRAYTON_THERMAL_EFFICIENCY_LOW"));
        Assert.False(Has(r.Advisories, "NTR_BIMODAL_REACTOR_TAP_EXCESSIVE"));
        // Bimodal result fields should be NaN.
        Assert.True(double.IsNaN(r.ElectricPowerOutput_kWe));
        Assert.True(double.IsNaN(r.BraytonThermalEfficiency));
    }

    [Fact]
    public void BimodalGates_DoNotFire_OnNervaSolidCore()
    {
        var nerva = new NuclearThermalDesign(
            Kind:                    NuclearKind.NervaSolidCore,
            ReactorThermalPower_MW:  1100.0,
            ReactorCoreLength_mm:    1400.0,
            ReactorCoreDiameter_mm:  1400.0,
            FuelLoadingFraction:     0.65,
            PropellantMassFlow_kgs:  33.0,
            ChamberPressure_bar:     34.0,
            ThroatRadius_mm:         120.0,
            ExpansionRatio:          100.0,
            NozzleLength_mm:         4000.0,
            RegenChannelDepth_mm:    2.0,
            RegenChannelCount:       200,
            NozzleWallThickness_mm:  1.5,
            NozzleChannelWidth_mm:   3.0,
            NozzleManifoldDepth_mm:  5.0);
        var r = NuclearOptimization.GenerateWith(nerva, Cond());
        Assert.False(Has(r.Violations, "NTR_BIMODAL_BRAYTON_TURBINE_OVERTEMP"));
        Assert.False(Has(r.Violations, "NTR_BIMODAL_ALTERNATOR_RPM_OUT_OF_BAND"));
        Assert.False(Has(r.Advisories, "NTR_BIMODAL_BRAYTON_THERMAL_EFFICIENCY_LOW"));
        Assert.False(Has(r.Advisories, "NTR_BIMODAL_REACTOR_TAP_EXCESSIVE"));
    }
}
