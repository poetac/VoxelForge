// SolarCollectorSolverTests.cs — Sprint ST.W1 unit tests for the
// closed-form solar-thermal collector performance snapshot.

using System;
using Voxelforge.SolarThermal;
using Xunit;

namespace Voxelforge.Tests.SolarThermal;

public sealed class SolarCollectorSolverTests
{
    // ── Registry ─────────────────────────────────────────────────────────

    [Fact]
    public void Registry_FlatPlate_HasExpectedClusterAnchors()
    {
        var p = SolarCollectorRegistry.FlatPlate;
        Assert.Equal(1.0, p.ConcentrationRatio, precision: 6);
        Assert.InRange(p.TransmittanceAbsorptanceProduct, 0.70, 0.85);
        Assert.InRange(p.OverallLossCoefficient_W_m2K, 3.0, 7.0);
        Assert.InRange(p.MaxOperatingTemperature_C, 80.0, 150.0);
    }

    [Fact]
    public void Registry_ParabolicTrough_HasLowerLossThanFlatPlate()
    {
        // Evacuated-tube receiver → much lower U_L than flat-plate.
        Assert.True(SolarCollectorRegistry.ParabolicTrough.OverallLossCoefficient_W_m2K
                  < SolarCollectorRegistry.FlatPlate.OverallLossCoefficient_W_m2K);
        // Concentration ratio > 1 (defining feature).
        Assert.True(SolarCollectorRegistry.ParabolicTrough.ConcentrationRatio
                  > SolarCollectorRegistry.FlatPlate.ConcentrationRatio);
    }

    [Fact]
    public void Registry_For_ThrowsOnNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SolarCollectorRegistry.For(SolarCollectorKind.None));
    }

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsCollectorBelowAmbient()
    {
        var d = FlatPlate60C() with
        {
            CollectorTemperature_C = 10.0,
            AmbientTemperature_C   = 20.0,
        };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroAperture()
    {
        var d = FlatPlate60C() with { ApertureArea_m2 = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNegativeIrradiance()
    {
        var d = FlatPlate60C() with { DirectNormalIrradiance_W_m2 = -100 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── Flat-plate domestic-hot-water baseline ──────────────────────────

    [Fact]
    public void FlatPlate_DomesticHotWater_EfficiencyInClusterBand()
    {
        // G = 800 W/m², T_coll = 60 °C, T_amb = 20 °C → η ≈ 0.45.
        // Cluster band [0.30, 0.60] for typical domestic flat-plate.
        var r = SolarCollectorSolver.Solve(FlatPlate60C());
        Assert.InRange(r.CollectorEfficiency, 0.30, 0.60);
    }

    [Fact]
    public void FlatPlate_DomesticHotWater_UsefulHeatInClusterBand()
    {
        // 4 m² panel × ~ 360 W/m² useful = ~ 1.4 kW thermal.
        var r = SolarCollectorSolver.Solve(FlatPlate60C());
        Assert.InRange(r.UsefulHeatPower_W, 1000.0, 1800.0);
    }

    [Fact]
    public void FlatPlate_OperatingTemperatureInEnvelope()
    {
        // 60 °C sits inside flat-plate envelope [T_amb, 100 °C].
        var r = SolarCollectorSolver.Solve(FlatPlate60C());
        Assert.True(r.OperatingTemperatureInValidEnvelope);
    }

    // ── Parabolic-trough Andasol-class baseline ─────────────────────────

    [Fact]
    public void ParabolicTrough_400C_EfficiencyInClusterBand()
    {
        // G = 800 W/m², T_coll = 400 °C, T_amb = 25 °C → η ≈ 0.52.
        // Andasol cluster mid-band [0.45, 0.65] for thermal η at the
        // receiver (solar-to-electric is much lower after Rankine cycle).
        var r = SolarCollectorSolver.Solve(AndasolElement());
        Assert.InRange(r.CollectorEfficiency, 0.45, 0.65);
    }

    [Fact]
    public void ParabolicTrough_OperatesAt400C_InEnvelope()
    {
        var r = SolarCollectorSolver.Solve(AndasolElement());
        Assert.True(r.OperatingTemperatureInValidEnvelope);
    }

    [Fact]
    public void ParabolicTrough_BeatsFlatPlate_AtHighTemperature()
    {
        // At T_coll = 200 °C: flat-plate U_L = 5 vs trough U_L = 0.5 →
        // trough wins. Flat-plate at 200 °C is also out-of-envelope.
        var flatPlate = SolarCollectorSolver.Solve(FlatPlate60C() with
        {
            Kind = SolarCollectorKind.FlatPlate,
            CollectorTemperature_C = 200.0,
        });
        var trough = SolarCollectorSolver.Solve(FlatPlate60C() with
        {
            Kind = SolarCollectorKind.ParabolicTrough,
            CollectorTemperature_C = 200.0,
        });
        Assert.True(trough.CollectorEfficiency > flatPlate.CollectorEfficiency,
            $"Parabolic-trough η ({trough.CollectorEfficiency:F4}) at 200 °C "
          + $"expected > flat-plate η ({flatPlate.CollectorEfficiency:F4}).");
    }

    // ── Hottel-Whillier-Bliss scaling ───────────────────────────────────

    [Fact]
    public void HigherCollectorTemperature_ReducesEfficiency()
    {
        // η = F_R·τα − F_R·U_L·ΔT / G is monotonically decreasing in ΔT.
        var cool = SolarCollectorSolver.Solve(FlatPlate60C() with { CollectorTemperature_C = 30 });
        var warm = SolarCollectorSolver.Solve(FlatPlate60C() with { CollectorTemperature_C = 60 });
        var hot  = SolarCollectorSolver.Solve(FlatPlate60C() with { CollectorTemperature_C = 90 });
        Assert.True(cool.CollectorEfficiency > warm.CollectorEfficiency);
        Assert.True(warm.CollectorEfficiency > hot.CollectorEfficiency);
    }

    [Fact]
    public void IncidentSolarLinearInAperture_AtConstantG()
    {
        var small = SolarCollectorSolver.Solve(FlatPlate60C() with { ApertureArea_m2 = 2.0 });
        var big   = SolarCollectorSolver.Solve(FlatPlate60C() with { ApertureArea_m2 = 4.0 });
        Assert.Equal(2.0, big.IncidentSolarPower_W / small.IncidentSolarPower_W, precision: 6);
    }

    [Fact]
    public void IncidentSolarLinearInIrradiance_AtConstantA()
    {
        var lo = SolarCollectorSolver.Solve(FlatPlate60C() with { DirectNormalIrradiance_W_m2 = 400 });
        var hi = SolarCollectorSolver.Solve(FlatPlate60C() with { DirectNormalIrradiance_W_m2 = 800 });
        Assert.Equal(2.0, hi.IncidentSolarPower_W / lo.IncidentSolarPower_W, precision: 6);
    }

    [Fact]
    public void UsefulHeatClampsAtZero_WhenCollectorRunsHot()
    {
        // Drive flat-plate near stagnation: T_coll - T_amb very large
        // for the given G. Stagnation T ≈ T_amb + (τα·G)/U_L = 20 +
        // 0.75·400/5 = 80 °C. Operate at 100 °C (above stagnation) →
        // useful heat clamps at zero.
        var d = FlatPlate60C() with
        {
            DirectNormalIrradiance_W_m2 = 400.0,
            CollectorTemperature_C      = 100.0,
        };
        var r = SolarCollectorSolver.Solve(d);
        Assert.Equal(0.0, r.UsefulHeatPower_W, precision: 9);
        Assert.Equal(0.0, r.CollectorEfficiency, precision: 9);
        // But ThermalLossPower_W is still positive (the loss component
        // continues even when no useful heat is produced).
        Assert.True(r.ThermalLossPower_W > 0);
    }

    // ── Stagnation temperature ───────────────────────────────────────────

    [Fact]
    public void StagnationTemperature_FlatPlate_AtTypicalCondition()
    {
        // T_stag = T_amb + (τα·G)/U_L = 20 + 0.75·800/5 = 140 °C.
        double T_stag = SolarCollectorSolver.ComputeStagnationTemperature(
            SolarCollectorKind.FlatPlate, irradiance_W_m2: 800, ambientTemperature_C: 20);
        Assert.Equal(140.0, T_stag, precision: 1);
    }

    [Fact]
    public void StagnationTemperature_ParabolicTroughMuchHigherThanFlatPlate()
    {
        // ParabolicTrough U_L ≈ 0.5 vs FlatPlate ≈ 5 → ~ 10× higher
        // stagnation T (also higher τα).
        double T_stag_flat = SolarCollectorSolver.ComputeStagnationTemperature(
            SolarCollectorKind.FlatPlate, irradiance_W_m2: 800, ambientTemperature_C: 20);
        double T_stag_trough = SolarCollectorSolver.ComputeStagnationTemperature(
            SolarCollectorKind.ParabolicTrough, irradiance_W_m2: 800, ambientTemperature_C: 20);
        Assert.True(T_stag_trough > T_stag_flat * 5.0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Typical domestic flat-plate panel: 4 m² aperture, 800 W/m² noon,
    // T_collector = 60 °C (hot water), T_ambient = 20 °C.
    private static SolarCollectorDesign FlatPlate60C() => new(
        Kind:                         SolarCollectorKind.FlatPlate,
        ApertureArea_m2:              4.0,
        DirectNormalIrradiance_W_m2:  800.0,
        CollectorTemperature_C:       60.0,
        AmbientTemperature_C:         20.0);

    // Andasol-class parabolic-trough single-aperture element: 1 m² of
    // aperture at 800 W/m² DNI, T_HTF = 400 °C, T_amb = 25 °C.
    private static SolarCollectorDesign AndasolElement() => new(
        Kind:                         SolarCollectorKind.ParabolicTrough,
        ApertureArea_m2:              1.0,
        DirectNormalIrradiance_W_m2:  800.0,
        CollectorTemperature_C:       400.0,
        AmbientTemperature_C:         25.0);
}
