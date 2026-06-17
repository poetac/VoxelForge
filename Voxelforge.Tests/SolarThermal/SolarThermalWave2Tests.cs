// SolarThermalWave2Tests.cs — Sprint ST.W2 unit tests for the
// evacuated-tube collector kind.

using Voxelforge.SolarThermal;
using Xunit;

namespace Voxelforge.Tests.SolarThermal;

public sealed class SolarThermalWave2Tests
{
    [Fact]
    public void EvacuatedTube_LowerU_L_ThanFlatPlate()
    {
        // Vacuum insulation → ~ 3× lower U_L than flat-plate.
        Assert.True(SolarCollectorRegistry.EvacuatedTube.OverallLossCoefficient_W_m2K
                  < SolarCollectorRegistry.FlatPlate.OverallLossCoefficient_W_m2K);
    }

    [Fact]
    public void EvacuatedTube_Concentration_IsUnity()
    {
        // Non-concentrating like flat-plate.
        Assert.Equal(1.0,
            SolarCollectorRegistry.EvacuatedTube.ConcentrationRatio,
            precision: 9);
    }

    [Fact]
    public void EvacuatedTube_BeatsFlatPlate_AtElevatedTemperature()
    {
        // At T = 80 °C, the flat-plate Q_useful drops further than the
        // ET due to higher U_L. ET should win.
        var flat = SolarCollectorSolver.Solve(new SolarCollectorDesign(
            Kind:                         SolarCollectorKind.FlatPlate,
            ApertureArea_m2:              4.0,
            DirectNormalIrradiance_W_m2:  800.0,
            CollectorTemperature_C:      100.0,    // hot — flat-plate stagnation
            AmbientTemperature_C:         20.0));
        var et = SolarCollectorSolver.Solve(new SolarCollectorDesign(
            Kind:                         SolarCollectorKind.EvacuatedTube,
            ApertureArea_m2:              4.0,
            DirectNormalIrradiance_W_m2:  800.0,
            CollectorTemperature_C:      100.0,
            AmbientTemperature_C:         20.0));
        Assert.True(et.CollectorEfficiency > flat.CollectorEfficiency);
    }

    [Fact]
    public void EvacuatedTube_HigherStagnationTemperature_ThanFlatPlate()
    {
        double T_stag_flat = SolarCollectorSolver.ComputeStagnationTemperature(
            SolarCollectorKind.FlatPlate, irradiance_W_m2: 800, ambientTemperature_C: 20);
        double T_stag_et = SolarCollectorSolver.ComputeStagnationTemperature(
            SolarCollectorKind.EvacuatedTube, irradiance_W_m2: 800, ambientTemperature_C: 20);
        Assert.True(T_stag_et > T_stag_flat);
    }

    [Fact]
    public void EvacuatedTube_InClusterEnvelope_At150C()
    {
        var d = new SolarCollectorDesign(
            Kind:                         SolarCollectorKind.EvacuatedTube,
            ApertureArea_m2:              4.0,
            DirectNormalIrradiance_W_m2:  800.0,
            CollectorTemperature_C:      150.0,
            AmbientTemperature_C:         20.0);
        var r = SolarCollectorSolver.Solve(d);
        Assert.True(r.OperatingTemperatureInValidEnvelope);
    }
}
