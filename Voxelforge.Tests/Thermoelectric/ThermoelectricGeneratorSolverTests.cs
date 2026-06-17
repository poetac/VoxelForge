// ThermoelectricGeneratorSolverTests.cs — Sprint TEG.W1 unit tests for
// the closed-form thermoelectric-generator performance snapshot.

using System;
using Voxelforge.Thermoelectric;
using Xunit;

namespace Voxelforge.Tests.Thermoelectric;

public sealed class ThermoelectricGeneratorSolverTests
{
    // ── Registry ─────────────────────────────────────────────────────────

    [Fact]
    public void Registry_BiTe_LowTemperatureClusterAnchors()
    {
        var p = ThermoelectricMaterialRegistry.BismuthTelluride;
        Assert.Equal(1.0, p.FigureOfMerit_ZT, precision: 6);
        Assert.True(p.MaxHotSideTemperature_K < 500.0);
    }

    [Fact]
    public void Registry_PbTe_MidTemperatureClusterAnchors()
    {
        var p = ThermoelectricMaterialRegistry.LeadTelluride;
        Assert.Equal(1.5, p.FigureOfMerit_ZT, precision: 6);
        Assert.InRange(p.MaxHotSideTemperature_K, 700.0, 800.0);
    }

    [Fact]
    public void Registry_SiGe_HighTemperatureClusterAnchors()
    {
        var p = ThermoelectricMaterialRegistry.SiliconGermanium;
        Assert.Equal(0.8, p.FigureOfMerit_ZT, precision: 6);
        Assert.True(p.MaxHotSideTemperature_K > 1000.0);
    }

    [Fact]
    public void Registry_For_ThrowsOnNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ThermoelectricMaterialRegistry.For(ThermoelectricMaterial.None));
    }

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsTHotAtOrBelowTCold()
    {
        var d = GphsRtgCassiniBaseline() with
        {
            HotSideTemperature_K  = 500.0,
            ColdSideTemperature_K = 600.0,
        };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroHotSideHeat()
    {
        var d = GphsRtgCassiniBaseline() with { HotSideHeatInput_W = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── Cassini GPHS-RTG baseline ────────────────────────────────────────

    [Fact]
    public void GphsRtgCassini_ConversionEfficiencyInClusterBand()
    {
        // Theoretical figure-of-merit ceiling at SiGe ZT = 0.8,
        // T_hot = 1300 K, T_cold = 575 K → η_TEG ≈ 10.7 %.
        // Cluster band [0.08, 0.15] (real RTGs run ~ 60 % of this
        // theoretical maximum due to thermal-bridge losses).
        var r = ThermoelectricGeneratorSolver.Solve(GphsRtgCassiniBaseline());
        Assert.InRange(r.ConversionEfficiency, 0.08, 0.15);
    }

    [Fact]
    public void GphsRtgCassini_ElectricPowerLessThanHeatInput()
    {
        var r = ThermoelectricGeneratorSolver.Solve(GphsRtgCassiniBaseline());
        Assert.True(r.ElectricPowerOutput_W < GphsRtgCassiniBaseline().HotSideHeatInput_W);
    }

    [Fact]
    public void GphsRtgCassini_ConversionEfficiencyBelowCarnot()
    {
        // Always: η_TEG ≤ η_Carnot (figure-of-merit < 1 enforces this).
        var r = ThermoelectricGeneratorSolver.Solve(GphsRtgCassiniBaseline());
        Assert.True(r.ConversionEfficiency < r.CarnotEfficiency,
            $"η_TEG ({r.ConversionEfficiency:F4}) must be < η_Carnot "
          + $"({r.CarnotEfficiency:F4}).");
    }

    [Fact]
    public void GphsRtgCassini_HotSideTemperatureInEnvelope()
    {
        // SiGe envelope is [773, 1273]. Cassini hot side at 1300 K is
        // marginally above; let me check the actual value used here
        // — the GPHS-RTG hot junction is ~ 1273 K (1000 °C), which
        // sits at the envelope edge. Using 1273 K test design.
        var design = GphsRtgCassiniBaseline() with { HotSideTemperature_K = 1273.0 };
        var r = ThermoelectricGeneratorSolver.Solve(design);
        Assert.True(r.HotSideTemperatureInValidEnvelope);
    }

    [Fact]
    public void GphsRtgCassini_EnergyBalance_QcoldEqualsQhotMinusPelec()
    {
        var d = GphsRtgCassiniBaseline();
        var r = ThermoelectricGeneratorSolver.Solve(d);
        Assert.Equal(d.HotSideHeatInput_W - r.ElectricPowerOutput_W,
                     r.HeatRejectedToColdSide_W, precision: 4);
    }

    // ── Figure-of-merit formula sanity ──────────────────────────────────

    [Fact]
    public void EfficiencyAtZeroZT_IsZero()
    {
        double eta = ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(
            figureOfMerit_ZT: 0.0,
            hotSideTemperature_K: 1000.0,
            coldSideTemperature_K: 500.0);
        Assert.Equal(0.0, eta, precision: 9);
    }

    [Fact]
    public void EfficiencyMonotonicallyIncreasingInZT()
    {
        double[] zts = { 0.1, 0.5, 1.0, 1.5, 2.0, 3.0, 10.0 };
        double prev = ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(
            zts[0], 1000.0, 500.0);
        for (int i = 1; i < zts.Length; i++)
        {
            double cur = ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(
                zts[i], 1000.0, 500.0);
            Assert.True(cur > prev,
                $"η at ZT={zts[i]} ({cur:F4}) expected > η at ZT={zts[i - 1]} ({prev:F4}).");
            prev = cur;
        }
    }

    [Fact]
    public void EfficiencyApproachesCarnotAsZTGoesToInfinity()
    {
        // Asymptote: at ZT → ∞, η_TEG → η_Carnot = 1 − T_cold/T_hot.
        double T_hot = 1000.0;
        double T_cold = 500.0;
        double carnot = 1.0 - T_cold / T_hot;   // = 0.5
        double atVeryHighZT = ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(
            figureOfMerit_ZT: 10000.0,
            hotSideTemperature_K: T_hot,
            coldSideTemperature_K: T_cold);
        Assert.InRange(atVeryHighZT, 0.95 * carnot, carnot);
    }

    [Fact]
    public void EfficiencyRejectsNegativeZT()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(
                -0.1, 1000.0, 500.0));
    }

    [Fact]
    public void EfficiencyRejectsTColdAtOrAboveTHot()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(
                1.0, 500.0, 500.0));
    }

    // ── Per-material envelope ────────────────────────────────────────────

    [Fact]
    public void BismuthTelluride_AtRtgTemperatures_OutOfEnvelope()
    {
        // Bi₂Te₃ envelope is < 200 °C ≈ 473 K. Cassini-class T_hot
        // = 1300 K is far above.
        var design = GphsRtgCassiniBaseline() with
        {
            Material = ThermoelectricMaterial.BismuthTelluride,
        };
        var r = ThermoelectricGeneratorSolver.Solve(design);
        Assert.False(r.HotSideTemperatureInValidEnvelope);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Cassini GPHS-RTG-class baseline (3 RTGs of ~ 290 W elec from
    // ~ 4.4 kW thermal each; Pu-238 hot junction at ~ 1300 K, cold
    // junction at ~ 575 K).
    private static ThermoelectricGeneratorDesign GphsRtgCassiniBaseline() => new(
        Material:               ThermoelectricMaterial.SiliconGermanium,
        HotSideTemperature_K:   1273.0,    // edge of SiGe envelope
        ColdSideTemperature_K:   575.0,
        HotSideHeatInput_W:     4400.0);
}
