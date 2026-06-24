// SpacecraftRadiatorSolverTests — pins the closed-form Stefan-Boltzmann
// flat-panel radiator math (gross emission, back-radiation, parasitic solar,
// net rejection, two-sided area doubling, area sizing) on the cross-platform
// Linux CI leg. Pure closed form, no calibration constants → exact assertions.
// Backfills coverage that previously ran only on the offline self-hosted runner.

using Voxelforge.Radiator;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class SpacecraftRadiatorSolverTests
{
    private static readonly double Sigma = SpacecraftRadiatorSolver.StefanBoltzmann_W_m2K4;

    [Fact]
    public void Solve_FlatPanel_StefanBoltzmannEmissionToDeepSpace()
    {
        // ε=1, A=1 m², T=300 K, deep-space sink (T_sink=0 → no back-radiation),
        // no solar load: Q_emitted = σ·A·T⁴ and Q_net = Q_emitted.
        var d = new SpacecraftRadiatorDesign(
            RadiatorKind.FlatPanel,
            PanelArea_m2: 1.0, OperatingTemperature_K: 300.0, SinkTemperature_K: 0.0,
            Emissivity: 1.0, SolarAbsorptivity: 0.0, IncidentSolarFlux_W_m2: 0.0);

        var r = SpacecraftRadiatorSolver.Solve(d);
        double expected = Sigma * (300.0 * 300.0 * 300.0 * 300.0);
        Assert.Equal(expected, r.GrossRadiatedHeat_W, 6);
        Assert.Equal(0.0, r.SinkBackradiation_W, 12);
        Assert.Equal(0.0, r.ParasiticSolarHeat_W, 12);
        Assert.Equal(expected, r.NetHeatRejectionRate_W, 6);
    }

    [Fact]
    public void Solve_TwoSidedDeployable_DoublesRadiativeAreaButNotSolar()
    {
        var flat = new SpacecraftRadiatorDesign(
            RadiatorKind.FlatPanel,
            PanelArea_m2: 1.0, OperatingTemperature_K: 300.0, SinkTemperature_K: 0.0,
            Emissivity: 1.0, SolarAbsorptivity: 0.5, IncidentSolarFlux_W_m2: 1000.0);
        var two = flat with { Kind = RadiatorKind.TwoSidedDeployable };

        var rf = SpacecraftRadiatorSolver.Solve(flat);
        var rt = SpacecraftRadiatorSolver.Solve(two);

        // Both faces radiate (2×A); the sun only loads the single sun-facing face.
        Assert.Equal(2.0 * rf.GrossRadiatedHeat_W, rt.GrossRadiatedHeat_W, 6);
        Assert.Equal(rf.ParasiticSolarHeat_W, rt.ParasiticSolarHeat_W, 9);
        Assert.Equal(500.0, rt.ParasiticSolarHeat_W, 9);     // α·A·G = 0.5·1·1000
    }

    [Fact]
    public void SolveForRequiredArea_InvertsPerUnitAreaNetRejection()
    {
        // q_net/m² = ε·σ·(T_p⁴−T_s⁴) − α·G. With ε=1, T_p=300, T_s=0, α=0,
        // the area to reject 2·(σ·300⁴) W is exactly 2 m².
        double qPerM2 = Sigma * (300.0 * 300.0 * 300.0 * 300.0);
        double area = SpacecraftRadiatorSolver.SolveForRequiredArea(
            targetHeatRejection_W: 2.0 * qPerM2,
            operatingTemperature_K: 300.0, sinkTemperature_K: 0.0,
            emissivity: 1.0, solarAbsorptivity: 0.0, incidentSolarFlux_W_m2: 0.0);
        Assert.Equal(2.0, area, 9);
    }

    [Fact]
    public void SolveForRequiredArea_SinkHotterThanPanel_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            SpacecraftRadiatorSolver.SolveForRequiredArea(
                targetHeatRejection_W: 100.0,
                operatingTemperature_K: 250.0, sinkTemperature_K: 300.0,
                emissivity: 1.0, solarAbsorptivity: 0.0, incidentSolarFlux_W_m2: 0.0));
    }
}
