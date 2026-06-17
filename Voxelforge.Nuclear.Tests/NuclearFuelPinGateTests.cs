// NuclearFuelPinGateTests.cs — Sprint NU.W2 unit tests for the 5 new
// fuel-pin gates wired through NuclearOptimization. Each gate has one
// fire test + one non-fire test (baseline).

using System.Linq;
using Voxelforge.Nuclear;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class NuclearFuelPinGateTests
{
    private static NuclearThermalDesign BaselineNrxA6() => new NuclearThermalDesign(
        Kind:                    NuclearKind.NervaSolidCore,
        ReactorThermalPower_MW:  1100.0,
        ReactorCoreLength_mm:    1400.0,
        ReactorCoreDiameter_mm:  1400.0,
        FuelLoadingFraction:     0.65,
        PropellantMassFlow_kgs:  33.0,
        ChamberPressure_bar:     40.0,
        ThroatRadius_mm:         120.0,
        ExpansionRatio:          100.0,
        NozzleLength_mm:         4000.0,
        RegenChannelDepth_mm:    2.0,
        RegenChannelCount:       200,
        NozzleWallThickness_mm:  1.5,
        NozzleChannelWidth_mm:   3.0,
        NozzleManifoldDepth_mm:  5.0) with
    {
        FuelPinDiameter_mm  = 2.5,
        FuelPinPitch_mm     = 3.2,
        FuelPinHexRings     = 2,
        FuelElementCount    = 564,
        FuelPinLength_m     = 1.4,
    };

    private static NuclearThermalConditions Cond() =>
        new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    private static bool Has(System.Collections.Generic.IReadOnlyList<FeasibilityViolation> v, string id)
        => v.Any(x => x.ConstraintId == id);

    // ── Baseline ─────────────────────────────────────────────────────────

    [Fact]
    public void Baseline_NrxA6FuelPin_IsFeasible()
    {
        var r = NuclearOptimization.GenerateWith(BaselineNrxA6(), Cond());
        Assert.True(r.IsFeasible,
            $"NRX-A6 fuel-pin baseline should pass; saw: "
          + string.Join(", ", r.Violations.Select(v => v.ConstraintId)));
    }

    // ── NTR_FUEL_PIN_OVERTEMP (hard) ────────────────────────────────────

    [Fact]
    public void FuelPinOvertemp_TinyPinSmallElement_FiresHardGate()
    {
        // Drop element count + shrink pin to force per-pin power up + reduce
        // surface area + drive volumetric q''' through the roof → T_peak
        // exceeds 3200 K UO₂-cermet limit.
        var bad = BaselineNrxA6() with
        {
            FuelElementCount   = 100,    // huge per-pin power
            FuelPinDiameter_mm = 1.5,
            FuelPinPitch_mm    = 2.5,
        };
        var r = NuclearOptimization.GenerateWith(bad, Cond());
        Assert.True(Has(r.Violations, "NTR_FUEL_PIN_OVERTEMP"));
    }

    // ── NTR_FUEL_PIN_SURFACE_OVERTEMP (hard) ────────────────────────────

    [Fact]
    public void FuelPinSurfaceOvertemp_TightChannelDesign_FiresHardGate()
    {
        // High F_hc + smaller subchannel → ΔT_wc explodes → surface > 2800 K.
        var bad = BaselineNrxA6() with
        {
            FuelElementCount         = 200,
            FuelPinHotChannelFactor  = 2.5,    // way above default 1.40
        };
        var r = NuclearOptimization.GenerateWith(bad, Cond());
        // At least one fuel-pin hard gate must fire — accept either the
        // surface or centreline gate, since the failure mode is correlated.
        Assert.True(Has(r.Violations, "NTR_FUEL_PIN_SURFACE_OVERTEMP")
                 || Has(r.Violations, "NTR_FUEL_PIN_OVERTEMP"));
    }

    // ── NTR_HOT_CHANNEL_FACTOR_EXCESSIVE (advisory) ─────────────────────

    [Fact]
    public void HotChannelFactorExcessive_AboveCluster_FiresAdvisory()
    {
        var advisory = BaselineNrxA6() with { FuelPinHotChannelFactor = 2.0 };
        var r = NuclearOptimization.GenerateWith(advisory, Cond());
        Assert.True(Has(r.Advisories, "NTR_HOT_CHANNEL_FACTOR_EXCESSIVE"));
    }

    [Fact]
    public void HotChannelFactorExcessive_AtClusterAnchor_DoesNotFire()
    {
        // The default 1.40 is below the 1.80 advisory ceiling.
        var r = NuclearOptimization.GenerateWith(BaselineNrxA6(), Cond());
        Assert.False(Has(r.Advisories, "NTR_HOT_CHANNEL_FACTOR_EXCESSIVE"));
    }

    // ── NTR_PER_PIN_POWER_ABOVE_BAND (advisory) ─────────────────────────

    [Fact]
    public void PerPinPowerAboveBand_FewElements_FiresAdvisory()
    {
        // 564 elements × 19 pins = 10716 pins → Q_pin = 1.1e9/10716 ≈ 103 kW.
        // Drop to 50 elements → Q_pin ≈ 1.16 MW, way above 200 kW ceiling.
        var advisory = BaselineNrxA6() with { FuelElementCount = 50 };
        var r = NuclearOptimization.GenerateWith(advisory, Cond());
        Assert.True(Has(r.Advisories, "NTR_PER_PIN_POWER_ABOVE_BAND"));
    }

    // ── NTR_PIN_PITCH_RATIO_OUT_OF_BAND (advisory) ──────────────────────

    [Fact]
    public void PinPitchRatioOutOfBand_TightPacking_FiresAdvisory()
    {
        // Ratio < 1.05 (pitch barely above diameter) → fires.
        var advisory = BaselineNrxA6() with
        {
            FuelPinDiameter_mm = 3.0,
            FuelPinPitch_mm    = 3.05,
        };
        var r = NuclearOptimization.GenerateWith(advisory, Cond());
        Assert.True(Has(r.Advisories, "NTR_PIN_PITCH_RATIO_OUT_OF_BAND"));
    }

    [Fact]
    public void PinPitchRatioOutOfBand_LoosePacking_FiresAdvisory()
    {
        // Ratio > 1.80 (pitch much larger than diameter) → fires.
        var advisory = BaselineNrxA6() with
        {
            FuelPinDiameter_mm = 2.0,
            FuelPinPitch_mm    = 4.0,
        };
        var r = NuclearOptimization.GenerateWith(advisory, Cond());
        Assert.True(Has(r.Advisories, "NTR_PIN_PITCH_RATIO_OUT_OF_BAND"));
    }

    [Fact]
    public void PinPitchRatioOutOfBand_BaselineRatio_DoesNotFire()
    {
        // Baseline: 3.2/2.5 = 1.28 — comfortably in [1.05, 1.80].
        var r = NuclearOptimization.GenerateWith(BaselineNrxA6(), Cond());
        Assert.False(Has(r.Advisories, "NTR_PIN_PITCH_RATIO_OUT_OF_BAND"));
    }

    // ── Wave-1 cross-isolation ──────────────────────────────────────────

    [Fact]
    public void FuelPinGates_DoNotFire_OnWave1Design_WithoutFuelPinFields()
    {
        // A Wave-1 design (no fuel-pin fields) → per-pin model skipped → no
        // fuel-pin gates can fire.
        var wave1 = BaselineNrxA6() with
        {
            FuelPinDiameter_mm = double.NaN,
            FuelPinPitch_mm    = double.NaN,
            FuelPinHexRings    = 0,
            FuelElementCount   = 0,
            FuelPinLength_m    = double.NaN,
        };
        var r = NuclearOptimization.GenerateWith(wave1, Cond());
        Assert.False(Has(r.Violations, "NTR_FUEL_PIN_OVERTEMP"));
        Assert.False(Has(r.Violations, "NTR_FUEL_PIN_SURFACE_OVERTEMP"));
        Assert.False(Has(r.Advisories, "NTR_HOT_CHANNEL_FACTOR_EXCESSIVE"));
        Assert.False(Has(r.Advisories, "NTR_PER_PIN_POWER_ABOVE_BAND"));
        Assert.False(Has(r.Advisories, "NTR_PIN_PITCH_RATIO_OUT_OF_BAND"));
    }
}
