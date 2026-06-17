// PvPanelSolverTests.cs — Sprint PV.W1 unit tests for the closed-form
// photovoltaic panel performance snapshot.

using System;
using Voxelforge.Photovoltaic;
using Xunit;

namespace Voxelforge.Tests.Photovoltaic;

public sealed class PvPanelSolverTests
{
    // ── Registry ─────────────────────────────────────────────────────────

    [Fact]
    public void Registry_Mono_HasHigherEfficiencyAnchorsThanPoly()
    {
        var mono = PhotovoltaicCellRegistry.Monocrystalline;
        var poly = PhotovoltaicCellRegistry.Polycrystalline;
        // Monocrystalline has higher I_sc, V_oc, and FF than poly.
        Assert.True(mono.ShortCircuitCurrent_A > poly.ShortCircuitCurrent_A);
        Assert.True(mono.OpenCircuitVoltage_V  > poly.OpenCircuitVoltage_V);
        Assert.True(mono.FillFactor            > poly.FillFactor);
    }

    [Fact]
    public void Registry_For_ThrowsOnNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PhotovoltaicCellRegistry.For(PhotovoltaicCellType.None));
    }

    [Fact]
    public void Registry_VoltageTemperatureCoefficient_IsNegative()
    {
        // dV_oc/dT < 0 for all silicon technologies (bandgap shrinks
        // with T).
        Assert.True(PhotovoltaicCellRegistry.Monocrystalline
                       .VoltageTemperatureCoefficient_V_perK < 0);
        Assert.True(PhotovoltaicCellRegistry.Polycrystalline
                       .VoltageTemperatureCoefficient_V_perK < 0);
    }

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneCellType()
    {
        var d = SunPowerX22_STC() with { CellType = PhotovoltaicCellType.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsExtremeTemperature()
    {
        Assert.Throws<ArgumentException>(
            () => (SunPowerX22_STC() with { CellTemperature_C = -100 }).ValidateSelf());
        Assert.Throws<ArgumentException>(
            () => (SunPowerX22_STC() with { CellTemperature_C =  200 }).ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNegativeIrradiance()
    {
        Assert.Throws<ArgumentException>(
            () => (SunPowerX22_STC() with { Irradiance_W_m2 = -100 }).ValidateSelf());
    }

    // ── SunPower X22-class STC baseline ──────────────────────────────────

    [Fact]
    public void SunPowerX22_AtSTC_EfficiencyInClusterBand()
    {
        // SunPower X22 advertised: 22.7 %. My cluster fit lands ~ 20.6 %.
        // Cluster band [18, 24] %.
        var r = PvPanelSolver.Solve(SunPowerX22_STC());
        Assert.InRange(r.ConversionEfficiency, 0.18, 0.24);
    }

    [Fact]
    public void SunPowerX22_AtSTC_MaxPowerInClusterBand()
    {
        // SunPower X22 advertised: 360 W. Cluster band [280, 380] W.
        var r = PvPanelSolver.Solve(SunPowerX22_STC());
        Assert.InRange(r.MaxPower_W, 280.0, 380.0);
    }

    [Fact]
    public void SunPowerX22_AtSTC_OpenCircuitVoltageInClusterBand()
    {
        // 96 cells × ~ 0.68 V = ~ 65 V at STC.
        var r = PvPanelSolver.Solve(SunPowerX22_STC());
        Assert.InRange(r.OpenCircuitVoltage_V, 60.0, 70.0);
    }

    [Fact]
    public void SunPowerX22_AtSTC_ShortCircuitCurrentInClusterBand()
    {
        // ~ 6.2 A at STC, single-string.
        var r = PvPanelSolver.Solve(SunPowerX22_STC());
        Assert.InRange(r.ShortCircuitCurrent_A, 5.8, 6.6);
    }

    [Fact]
    public void SunPowerX22_PowerIsBoundedByFillFactorIdentity()
    {
        // P_mp = FF · V_oc · I_sc (approximately, since my FF cluster is
        // 0.79 ≈ 0.85·0.93). Verify identity within ~ 1 %.
        var r = PvPanelSolver.Solve(SunPowerX22_STC());
        double ff_implied = r.MaxPower_W / (r.OpenCircuitVoltage_V * r.ShortCircuitCurrent_A);
        Assert.InRange(ff_implied, 0.75, 0.82);
    }

    // ── Irradiance + temperature scaling ─────────────────────────────────

    [Fact]
    public void PowerScalesLinearlyWithIrradiance_AtConstantTemperature()
    {
        var lo = PvPanelSolver.Solve(SunPowerX22_STC() with { Irradiance_W_m2 =  500.0 });
        var hi = PvPanelSolver.Solve(SunPowerX22_STC() with { Irradiance_W_m2 = 1000.0 });
        // V_mp doesn't change with G (V_oc fixed at constant T → V_mp =
        // 0.85·V_oc fixed); I_mp doubles with G. So P_mp doubles.
        Assert.Equal(2.0, hi.MaxPower_W / lo.MaxPower_W, precision: 4);
    }

    [Fact]
    public void PowerDropsAtHighTemperature_VsSTC()
    {
        // Rooftop hot-day cluster: 65 °C cell. Expect ~ 10-15 % P drop vs STC.
        var stc = PvPanelSolver.Solve(SunPowerX22_STC());
        var hot = PvPanelSolver.Solve(SunPowerX22_STC() with { CellTemperature_C = 65.0 });
        double drop = (stc.MaxPower_W - hot.MaxPower_W) / stc.MaxPower_W;
        Assert.InRange(drop, 0.05, 0.20);
    }

    [Fact]
    public void OpenCircuitVoltageMonotonicallyDecreasesWithTemperature()
    {
        var cold = PvPanelSolver.Solve(SunPowerX22_STC() with { CellTemperature_C =  0.0 });
        var stc  = PvPanelSolver.Solve(SunPowerX22_STC());
        var hot  = PvPanelSolver.Solve(SunPowerX22_STC() with { CellTemperature_C = 65.0 });
        Assert.True(cold.OpenCircuitVoltage_V > stc.OpenCircuitVoltage_V);
        Assert.True(stc.OpenCircuitVoltage_V  > hot.OpenCircuitVoltage_V);
    }

    [Fact]
    public void ZeroIrradiance_ProducesZeroPower()
    {
        var r = PvPanelSolver.Solve(SunPowerX22_STC() with { Irradiance_W_m2 = 0.0 });
        Assert.Equal(0.0, r.MaxPower_W,             precision: 9);
        Assert.Equal(0.0, r.ShortCircuitCurrent_A,  precision: 9);
        Assert.Equal(0.0, r.ConversionEfficiency,   precision: 9);
    }

    // ── Mono vs poly cluster comparison ─────────────────────────────────

    [Fact]
    public void Mono_HigherPower_ThanPoly_AtSameTopologyAndConditions()
    {
        var mono = PvPanelSolver.Solve(SunPowerX22_STC()
            with { CellType = PhotovoltaicCellType.Monocrystalline });
        var poly = PvPanelSolver.Solve(SunPowerX22_STC()
            with { CellType = PhotovoltaicCellType.Polycrystalline });
        Assert.True(mono.MaxPower_W > poly.MaxPower_W,
            $"Mono P_mp ({mono.MaxPower_W:F1} W) expected > poly P_mp "
          + $"({poly.MaxPower_W:F1} W) at same topology and (G, T).");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // SunPower Maxeon X22-class baseline: 96 series, 1 parallel, ~ 1.55 m²
    // aperture. At STC: ~ 320-360 W, ~ 20-23 % efficiency.
    private static PvPanelDesign SunPowerX22_STC() => new(
        CellType:           PhotovoltaicCellType.Monocrystalline,
        CellsInSeries:      96,
        StringsInParallel:  1,
        CellArea_cm2:       161.5,    // 96 cells × 161.5 cm² = 1.55 m²
        Irradiance_W_m2:    1000.0,
        CellTemperature_C:  25.0);
}
