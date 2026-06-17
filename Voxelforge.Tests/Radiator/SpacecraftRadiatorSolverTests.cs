// SpacecraftRadiatorSolverTests.cs — Sprint RAD.W1 unit tests for the
// closed-form spacecraft flat-panel radiator solver.

using System;
using Voxelforge.Radiator;
using Xunit;

namespace Voxelforge.Tests.Radiator;

public sealed class SpacecraftRadiatorSolverTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneKind()
    {
        var d = IssPanelEclipse() with { Kind = RadiatorKind.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsEmissivityOutOfBand()
    {
        Assert.Throws<ArgumentException>(
            () => (IssPanelEclipse() with { Emissivity = 0.0 }).ValidateSelf());
        Assert.Throws<ArgumentException>(
            () => (IssPanelEclipse() with { Emissivity = 1.5 }).ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsAbsorptivityOutOfBand()
    {
        Assert.Throws<ArgumentException>(
            () => (IssPanelEclipse() with { SolarAbsorptivity = -0.1 }).ValidateSelf());
        Assert.Throws<ArgumentException>(
            () => (IssPanelEclipse() with { SolarAbsorptivity =  1.5 }).ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNegativeSinkTemperature()
    {
        Assert.Throws<ArgumentException>(
            () => (IssPanelEclipse() with { SinkTemperature_K = -50 }).ValidateSelf());
    }

    // ── ISS-class panel baseline ─────────────────────────────────────────

    [Fact]
    public void IssPanel_InEclipse_NetHeatRejectionInClusterBand()
    {
        // 30 m², ε=0.85, T_panel=320 K, T_sink=240 K, G=0 (eclipse) →
        // Q_net ≈ 10.4 kW. Cluster band [8 kW, 14 kW] swallows variation
        // in real ISS panels reporting ~ 14 kW.
        var r = SpacecraftRadiatorSolver.Solve(IssPanelEclipse());
        Assert.InRange(r.NetHeatRejectionRate_W, 8000.0, 14000.0);
    }

    [Fact]
    public void IssPanel_InEclipse_HeatRejectionDensityInClusterBand()
    {
        var r = SpacecraftRadiatorSolver.Solve(IssPanelEclipse());
        Assert.InRange(r.HeatRejectionDensity_W_m2, 250.0, 500.0);
    }

    [Fact]
    public void IssPanel_InEclipse_AlphaOverEpsilonReportsCoatingFigureOfMerit()
    {
        // White paint: α/ε = 0.20/0.85 ≈ 0.235 — typical first-life
        // value. End-of-life α can degrade to ~ 0.40 (α/ε ≈ 0.47).
        var r = SpacecraftRadiatorSolver.Solve(IssPanelEclipse());
        Assert.Equal(0.20 / 0.85, r.AlphaOverEpsilonRatio, precision: 6);
    }

    [Fact]
    public void IssPanel_InEclipse_SolarParasiticIsZero()
    {
        var r = SpacecraftRadiatorSolver.Solve(IssPanelEclipse());
        Assert.Equal(0.0, r.ParasiticSolarHeat_W, precision: 9);
    }

    [Fact]
    public void IssPanel_InFullSun_NetRejectionDropsVsEclipse()
    {
        // Full solar on white paint → ~ 8 kW parasitic over 30 m².
        var eclipse = SpacecraftRadiatorSolver.Solve(IssPanelEclipse());
        var fullSun = SpacecraftRadiatorSolver.Solve(IssPanelFullSun());
        Assert.True(fullSun.NetHeatRejectionRate_W < eclipse.NetHeatRejectionRate_W,
            $"Full-sun Q_net ({fullSun.NetHeatRejectionRate_W:F0} W) expected < "
          + $"eclipse Q_net ({eclipse.NetHeatRejectionRate_W:F0} W).");
        // Parasitic should be ~ α · A · G = 0.20 · 30 · 1361 = 8166 W.
        Assert.InRange(fullSun.ParasiticSolarHeat_W, 7500.0, 9000.0);
    }

    // ── Stefan-Boltzmann scaling sanity ──────────────────────────────────

    [Fact]
    public void GrossEmittedHeat_ScalesAsTPanelToTheFourth()
    {
        // Doubling T_panel → 16× Q_emitted (T⁴ scaling).
        var lo = SpacecraftRadiatorSolver.Solve(IssPanelEclipse() with { OperatingTemperature_K = 200.0 });
        var hi = SpacecraftRadiatorSolver.Solve(IssPanelEclipse() with { OperatingTemperature_K = 400.0 });
        Assert.Equal(16.0, hi.GrossRadiatedHeat_W / lo.GrossRadiatedHeat_W, precision: 4);
    }

    [Fact]
    public void GrossEmittedHeat_LinearInPanelArea()
    {
        var smaller = SpacecraftRadiatorSolver.Solve(IssPanelEclipse() with { PanelArea_m2 = 15.0 });
        var bigger  = SpacecraftRadiatorSolver.Solve(IssPanelEclipse() with { PanelArea_m2 = 30.0 });
        Assert.Equal(2.0, bigger.GrossRadiatedHeat_W / smaller.GrossRadiatedHeat_W, precision: 4);
    }

    [Fact]
    public void GrossEmittedHeat_LinearInEmissivity()
    {
        var lowEm  = SpacecraftRadiatorSolver.Solve(IssPanelEclipse() with { Emissivity = 0.40 });
        var highEm = SpacecraftRadiatorSolver.Solve(IssPanelEclipse() with { Emissivity = 0.80 });
        Assert.Equal(2.0, highEm.GrossRadiatedHeat_W / lowEm.GrossRadiatedHeat_W, precision: 6);
    }

    [Fact]
    public void DeepSpaceSink_BackradiationApproachesZero()
    {
        // T_sink = 3 K → T_sink⁴ ≈ 81 → back-radiation negligible vs
        // T_panel⁴ ≈ 1e10. Ratio of Q_back / Q_emitted < 1e-7.
        var deepSpace = IssPanelEclipse() with { SinkTemperature_K = 3.0 };
        var r = SpacecraftRadiatorSolver.Solve(deepSpace);
        Assert.True(r.SinkBackradiation_W / r.GrossRadiatedHeat_W < 1e-7);
    }

    // ── SolveForRequiredArea ─────────────────────────────────────────────

    [Fact]
    public void SolveForRequiredArea_RoundTripsAgainstSolve()
    {
        // Using the snapshot-Solve output as the target → SolveForRequiredArea
        // should return the input panel area.
        var d = IssPanelEclipse();
        var r = SpacecraftRadiatorSolver.Solve(d);
        double areaBack = SpacecraftRadiatorSolver.SolveForRequiredArea(
            targetHeatRejection_W:    r.NetHeatRejectionRate_W,
            operatingTemperature_K:   d.OperatingTemperature_K,
            sinkTemperature_K:        d.SinkTemperature_K,
            emissivity:               d.Emissivity,
            solarAbsorptivity:        d.SolarAbsorptivity,
            incidentSolarFlux_W_m2:   d.IncidentSolarFlux_W_m2);
        Assert.Equal(d.PanelArea_m2, areaBack, precision: 4);
    }

    [Fact]
    public void SolveForRequiredArea_RejectsTSinkAtOrAboveTPanel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SpacecraftRadiatorSolver.SolveForRequiredArea(
                targetHeatRejection_W:    1000.0,
                operatingTemperature_K:   240.0,
                sinkTemperature_K:        240.0,    // == panel
                emissivity:               0.85,
                solarAbsorptivity:        0.20,
                incidentSolarFlux_W_m2:   0.0));
    }

    [Fact]
    public void SolveForRequiredArea_ThrowsWhenSolarLoadExceedsCapacity()
    {
        // High α + low ε panel facing sun: parasitic > radiative
        // capacity → no positive area satisfies the heat balance.
        Assert.Throws<InvalidOperationException>(
            () => SpacecraftRadiatorSolver.SolveForRequiredArea(
                targetHeatRejection_W:    100.0,
                operatingTemperature_K:   280.0,    // close to T_sink
                sinkTemperature_K:        270.0,
                emissivity:               0.10,    // low emissivity
                solarAbsorptivity:        0.95,    // high absorptivity (hot dirty surface)
                incidentSolarFlux_W_m2:   1361.0));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // ISS-class single-panel baseline in eclipse (G_solar = 0):
    // 30 m², ε = 0.85 (white paint), α = 0.20 (white paint),
    // T_panel = 320 K (operating set-point), T_sink = 240 K (LEO Earth-IR
    // dominated), 0 W/m² incident solar.
    private static SpacecraftRadiatorDesign IssPanelEclipse() => new(
        Kind:                    RadiatorKind.FlatPanel,
        PanelArea_m2:            30.0,
        OperatingTemperature_K:  320.0,
        SinkTemperature_K:       240.0,
        Emissivity:              0.85,
        SolarAbsorptivity:       0.20,
        IncidentSolarFlux_W_m2:    0.0);

    // ISS-class panel sun-facing: 1361 W/m² incident solar.
    private static SpacecraftRadiatorDesign IssPanelFullSun() =>
        IssPanelEclipse() with { IncidentSolarFlux_W_m2 = 1361.0 };
}
