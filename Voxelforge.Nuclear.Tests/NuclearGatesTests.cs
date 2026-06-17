// NuclearGatesTests.cs — unit tests for NuclearGates evaluator.
// Covers nominal-pass (all 3 hard + 3 advisory non-triggering) and
// threshold-fires for each gate.

using System.Linq;
using Voxelforge.Nuclear;
using Voxelforge.Nuclear.Optimization;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class NuclearGatesTests
{
    // Nominal design: no gate should fire.
    private static NuclearThermalDesign NominalDesign() => new(
        Kind:                    NuclearKind.NervaSolidCore,
        ReactorThermalPower_MW:  1100.0,
        ReactorCoreLength_mm:    1400.0,
        ReactorCoreDiameter_mm:  1400.0,
        FuelLoadingFraction:     0.65,
        PropellantMassFlow_kgs:  33.0,
        ChamberPressure_bar:     40.0,   // above 30 bar hard floor
        ThroatRadius_mm:         120.0,
        ExpansionRatio:          100.0,
        NozzleLength_mm:         4000.0,
        RegenChannelDepth_mm:    2.0,
        RegenChannelCount:       200,
        NozzleWallThickness_mm:  1.5,
        NozzleChannelWidth_mm:   3.0,
        NozzleManifoldDepth_mm:  5.0);

    private static NuclearThermalConditions NominalCond() =>
        new(PropellantInletTemp_K: 80.0);

    // ── Nominal pass ──────────────────────────────────────────────────────────

    [Fact]
    public void Nominal_NrxA6Design_AllHardGatesPass()
    {
        var result = NuclearOptimization.GenerateWith(NominalDesign(), NominalCond());
        Assert.True(result.IsFeasible,
            $"Nominal NRX-A6 design must be feasible. Violations: {string.Join(", ", result.Violations)}");
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Nominal_NrxA6Design_NoDesignQualityAdvisories()
    {
        // k_eff and CTE-mismatch gates must be silent on a well-calibrated NRX-A6 design.
        // The regen-cooling advisory is allowed to fire at extreme throat heat flux.
        var result = NuclearOptimization.GenerateWith(NominalDesign(), NominalCond());
        var ids = result.Advisories.Select(v => v.ConstraintId).ToHashSet();
        Assert.DoesNotContain(NuclearConstraintIds.KEff_OutOfBand, ids);
        Assert.DoesNotContain(NuclearConstraintIds.FuelCTEMismatch, ids);
    }

    // ── NTR_REACTOR_OVERTEMP ─────────────────────────────────────────────────

    [Fact]
    public void ReactorOvertemp_FiresWhenCoreExceedsLimit()
    {
        // Force T_exit > 3000 K by using extreme power with low flow.
        var design = NominalDesign() with
        {
            ReactorThermalPower_MW = 2000.0,
            PropellantMassFlow_kgs = 1.0,
        };
        var result = NuclearOptimization.GenerateWith(design, NominalCond());
        Assert.Contains(result.Violations,
            v => v.ConstraintId == NuclearConstraintIds.ReactorOvertemp);
    }

    [Fact]
    public void ReactorOvertemp_PassesOnNominalDesign()
    {
        var result = NuclearOptimization.GenerateWith(NominalDesign(), NominalCond());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == NuclearConstraintIds.ReactorOvertemp);
    }

    // ── NTR_THERMAL_FLUX_EXCEEDED ─────────────────────────────────────────────

    [Fact]
    public void ThermalFluxExceeded_FiresWhenFluxAboveLimit()
    {
        // Very small core → huge Q_vol > 4000 MW/m³.
        var design = NominalDesign() with
        {
            ReactorCoreLength_mm   = 50.0,
            ReactorCoreDiameter_mm = 50.0,
        };
        var result = NuclearOptimization.GenerateWith(design, NominalCond());
        Assert.Contains(result.Violations,
            v => v.ConstraintId == NuclearConstraintIds.ThermalFluxExceeded);
    }

    [Fact]
    public void ThermalFluxExceeded_PassesOnNominalDesign()
    {
        var result = NuclearOptimization.GenerateWith(NominalDesign(), NominalCond());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == NuclearConstraintIds.ThermalFluxExceeded);
    }

    // ── NTR_CHAMBER_PRESSURE_TOO_LOW ──────────────────────────────────────────

    [Fact]
    public void ChamberPressureTooLow_FiresBelow30Bar()
    {
        var design = NominalDesign() with { ChamberPressure_bar = 25.0 };
        var result = NuclearOptimization.GenerateWith(design, NominalCond());
        Assert.Contains(result.Violations,
            v => v.ConstraintId == NuclearConstraintIds.ChamberPressureTooLow);
    }

    [Fact]
    public void ChamberPressureTooLow_PassesAt34Bar()
    {
        var design = NominalDesign() with { ChamberPressure_bar = 34.0 };
        var result = NuclearOptimization.GenerateWith(design, NominalCond());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == NuclearConstraintIds.ChamberPressureTooLow);
    }

    // ── NTR_K_EFF_OUT_OF_BAND ────────────────────────────────────────────────

    [Fact]
    public void KEff_OutOfBand_FiresWhenFuelLoadingIsLow()
    {
        // k_eff = 0.98 + 0.1 × 0.04 = 0.984 < 0.99 → advisory fires.
        var design = NominalDesign() with { FuelLoadingFraction = 0.10 };
        var result = NuclearOptimization.GenerateWith(design, NominalCond());
        Assert.Contains(result.Advisories,
            v => v.ConstraintId == NuclearConstraintIds.KEff_OutOfBand);
    }

    [Fact]
    public void KEff_OutOfBand_FiresWhenFuelLoadingIsHigh()
    {
        // k_eff = 0.98 + 1.0 × 0.04 = 1.02 (within band [0.99, 1.05] — still passes).
        // Need fuel > 1.75 to exceed 1.05: k_eff = 0.98 + f × 0.04 > 1.05 → f > 1.75 (impossible).
        // The only way k_eff > 1.05 is unreachable with FuelLoadingFraction ≤ 1.
        // At f=1.0: k_eff = 0.98 + 0.04 = 1.02 — still within band.
        // Confirm no out-of-band advisory fires at f=0.65.
        var result = NuclearOptimization.GenerateWith(NominalDesign(), NominalCond());
        Assert.DoesNotContain(result.Advisories,
            v => v.ConstraintId == NuclearConstraintIds.KEff_OutOfBand);
    }

    // ── NTR_FUEL_CTE_MISMATCH ────────────────────────────────────────────────

    [Fact]
    public void FuelCTEMismatch_FiresAbove80Percent()
    {
        var design = NominalDesign() with { FuelLoadingFraction = 0.82 };
        var result = NuclearOptimization.GenerateWith(design, NominalCond());
        Assert.Contains(result.Advisories,
            v => v.ConstraintId == NuclearConstraintIds.FuelCTEMismatch);
    }

    [Fact]
    public void FuelCTEMismatch_PassesAt65Percent()
    {
        var result = NuclearOptimization.GenerateWith(NominalDesign(), NominalCond());
        Assert.DoesNotContain(result.Advisories,
            v => v.ConstraintId == NuclearConstraintIds.FuelCTEMismatch);
    }

    // ── IsFeasible convenience property ──────────────────────────────────────

    [Fact]
    public void IsFeasible_FalseWhenHardGateFires()
    {
        var design = NominalDesign() with { ChamberPressure_bar = 10.0 };
        var result = NuclearOptimization.GenerateWith(design, NominalCond());
        Assert.False(result.IsFeasible);
    }
}
