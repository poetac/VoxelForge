// PvWave2Tests.cs — Sprint PV.W2 unit tests for the bifacial-gain extension.

using System;
using Voxelforge.Photovoltaic;
using Xunit;

namespace Voxelforge.Tests.Photovoltaic;

public sealed class PvWave2Tests
{
    [Fact]
    public void DefaultRearSideGain_IsZero_Monofacial()
    {
        Assert.Equal(0.0, SunPowerX22_STC().RearSideIrradianceGain, precision: 9);
    }

    [Fact]
    public void DefaultBifacialityFactor_IsZero_Monofacial()
    {
        Assert.Equal(0.0, SunPowerX22_STC().BifacialityFactor, precision: 9);
    }

    [Fact]
    public void StoredPower_AtPVw1Defaults_BitIdentical()
    {
        // Monofacial defaults → (1 + 0·β) = 1.0 → no boost.
        var r = PvPanelSolver.Solve(SunPowerX22_STC());
        Assert.InRange(r.MaxPower_W, 280.0, 380.0);
    }

    [Fact]
    public void BifacialGain_BoostsPower_AboveMonofacial()
    {
        var mono = PvPanelSolver.Solve(SunPowerX22_STC());
        var bifacial = PvPanelSolver.Solve(SunPowerX22_STC() with
        {
            RearSideIrradianceGain = 0.20,
            BifacialityFactor      = 0.85,
        });
        // P_bifacial / P_mono = 1 + 0.20 · 0.85 = 1.17.
        Assert.Equal(1.17, bifacial.MaxPower_W / mono.MaxPower_W, precision: 6);
    }

    [Fact]
    public void BifacialGain_ZeroBifaciality_NoBoost()
    {
        // β = 0 means no rear-side cells → no power boost regardless of φ.
        var mono = PvPanelSolver.Solve(SunPowerX22_STC());
        var withRearG = PvPanelSolver.Solve(SunPowerX22_STC() with
        {
            RearSideIrradianceGain = 0.30,
            BifacialityFactor      = 0.0,
        });
        Assert.Equal(mono.MaxPower_W, withRearG.MaxPower_W, precision: 6);
    }

    [Fact]
    public void Validate_RejectsBifacialityOutOfBand()
    {
        Assert.Throws<ArgumentException>(
            () => (SunPowerX22_STC() with { BifacialityFactor = 1.5 }).ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNegativeRearGain()
    {
        Assert.Throws<ArgumentException>(
            () => (SunPowerX22_STC() with { RearSideIrradianceGain = -0.1 }).ValidateSelf());
    }

    [Fact]
    public void BifacialGain_AccumulatesAcrossTemperatureRange()
    {
        // Bifacial boost should hold across temperatures (front-side
        // temperature derating applies to both front and rear sides).
        var cold_mono = PvPanelSolver.Solve(SunPowerX22_STC() with { CellTemperature_C = 0.0 });
        var cold_bi   = PvPanelSolver.Solve(SunPowerX22_STC() with
        {
            CellTemperature_C      = 0.0,
            RearSideIrradianceGain = 0.20,
            BifacialityFactor      = 0.85,
        });
        Assert.Equal(1.17, cold_bi.MaxPower_W / cold_mono.MaxPower_W, precision: 6);
    }

    private static PvPanelDesign SunPowerX22_STC() => new(
        CellType:           PhotovoltaicCellType.Monocrystalline,
        CellsInSeries:      96,
        StringsInParallel:  1,
        CellArea_cm2:       161.5,
        Irradiance_W_m2:    1000.0,
        CellTemperature_C:  25.0);
}
