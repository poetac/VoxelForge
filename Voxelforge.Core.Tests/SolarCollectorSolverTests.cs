// SolarCollectorSolverTests — pins the Hottel-Whillier-Bliss closed form:
// Q_useful = F_R·A·[τα·G − U_L·ΔT], η = Q_useful/(G·A), the zero clamp at
// and beyond stagnation, and the stagnation-temperature inversion
// T_stag = T_amb + τα·G/U_L. Pure closed form → exact assertions.
// Backfills coverage onto the Linux 'core' CI leg.

using System;
using Voxelforge.SolarThermal;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class SolarCollectorSolverTests
{
    // Domestic flat-plate panel: A = 4 m², G = 800 W/m², T_c = 60 °C,
    // T_amb = 20 °C. FlatPlate: F_R = 0.90, τα = 0.75, U_L = 5.0.
    private static SolarCollectorDesign DomesticFlatPlate()
        => new(SolarCollectorKind.FlatPlate,
               ApertureArea_m2: 4.0, DirectNormalIrradiance_W_m2: 800.0,
               CollectorTemperature_C: 60.0, AmbientTemperature_C: 20.0);

    [Fact]
    public void Solve_FlatPlate_HottelWhillierBlissEnergyBudget()
    {
        var r = SolarCollectorSolver.Solve(DomesticFlatPlate());
        Assert.Equal(3200.0, r.IncidentSolarPower_W, 9);            // G·A
        Assert.Equal(2400.0, r.AbsorbedSolarPower_W, 9);            // τα·G·A
        Assert.Equal(800.0, r.ThermalLossPower_W, 9);               // U_L·A·ΔT = 5·4·40
        Assert.Equal(1440.0, r.UsefulHeatPower_W, 9);               // 0.9·(2400−800)
        Assert.Equal(0.45, r.CollectorEfficiency, 12);              // 1440/3200
        Assert.True(r.OperatingTemperatureInValidEnvelope);         // 60 ≤ 100 °C
    }

    [Fact]
    public void Solve_ParabolicTrough_AndasolClassOperatingPoint()
    {
        // A = 100 m², G = 850, T_c = 390 °C, T_amb = 25 °C.
        // Trough: F_R = 0.85, τα = 0.85, U_L = 0.5 → η = 0.54 exactly.
        var r = SolarCollectorSolver.Solve(new SolarCollectorDesign(
            SolarCollectorKind.ParabolicTrough, 100.0, 850.0, 390.0, 25.0));
        Assert.Equal(85_000.0, r.IncidentSolarPower_W, 6);
        Assert.Equal(72_250.0, r.AbsorbedSolarPower_W, 6);
        Assert.Equal(18_250.0, r.ThermalLossPower_W, 6);            // 0.5·100·365
        Assert.Equal(45_900.0, r.UsefulHeatPower_W, 6);
        Assert.Equal(0.54, r.CollectorEfficiency, 12);
        Assert.True(r.OperatingTemperatureInValidEnvelope);         // 390 ≤ 450 °C
    }

    [Fact]
    public void ComputeStagnationTemperature_InvertsTheLossBalance()
        // T_stag = T_amb + τα·G/U_L = 20 + 0.75·800/5 = 140 °C exactly.
        => Assert.Equal(140.0, SolarCollectorSolver.ComputeStagnationTemperature(
               SolarCollectorKind.FlatPlate, 800.0, 20.0), 9);

    [Fact]
    public void Solve_AtStagnation_UsefulHeatIsExactlyZero()
    {
        var r = SolarCollectorSolver.Solve(DomesticFlatPlate() with { CollectorTemperature_C = 140.0 });
        Assert.Equal(0.0, r.UsefulHeatPower_W, 12);     // absorbed == loss
        Assert.Equal(0.0, r.CollectorEfficiency, 12);
    }

    [Fact]
    public void Solve_BeyondStagnation_ClampsAtZeroAndFlagsEnvelope()
    {
        // 150 °C > stagnation (140 °C) AND > the flat-plate 100 °C envelope.
        var r = SolarCollectorSolver.Solve(DomesticFlatPlate() with { CollectorTemperature_C = 150.0 });
        Assert.Equal(0.0, r.UsefulHeatPower_W, 12);     // clamped, never negative
        Assert.False(r.OperatingTemperatureInValidEnvelope);
        // The loss channel keeps reporting the physical loss (5·4·130 = 2600 W).
        Assert.Equal(2600.0, r.ThermalLossPower_W, 9);
    }

    [Fact]
    public void Solve_ZeroIrradiance_YieldsZeroEfficiencyWithoutDividing()
    {
        var r = SolarCollectorSolver.Solve(DomesticFlatPlate() with { DirectNormalIrradiance_W_m2 = 0.0 });
        Assert.Equal(0.0, r.IncidentSolarPower_W, 12);
        Assert.Equal(0.0, r.UsefulHeatPower_W, 12);
        Assert.Equal(0.0, r.CollectorEfficiency, 12);   // guard branch, not NaN
    }

    [Fact]
    public void Solve_EvacuatedTube_CutsLossVersusFlatPlateAtSameDeltaT()
    {
        // U_L drops 5.0 → 1.5 W/(m²·K); same ΔT = 40 K, A = 4 m².
        var flat = SolarCollectorSolver.Solve(DomesticFlatPlate());
        var tube = SolarCollectorSolver.Solve(DomesticFlatPlate() with { Kind = SolarCollectorKind.EvacuatedTube });
        Assert.Equal(1.5 * 4.0 * 40.0, tube.ThermalLossPower_W, 9); // 240 W
        Assert.True(tube.ThermalLossPower_W < flat.ThermalLossPower_W);
        // Q_useful = 0.85·(0.78·3200 − 240) = 0.85·2256 = 1917.6 W.
        Assert.Equal(0.85 * (0.78 * 3200.0 - 240.0), tube.UsefulHeatPower_W, 9);
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() => SolarCollectorSolver.Solve(
            DomesticFlatPlate() with { CollectorTemperature_C = 10.0 }));   // below ambient
        Assert.Throws<ArgumentException>(() => SolarCollectorSolver.Solve(
            DomesticFlatPlate() with { ApertureArea_m2 = 0.0 }));
        Assert.Throws<ArgumentException>(() => SolarCollectorSolver.Solve(
            DomesticFlatPlate() with { Kind = SolarCollectorKind.None }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SolarCollectorSolver.ComputeStagnationTemperature(
                SolarCollectorKind.FlatPlate, -1.0, 20.0));
    }
}
