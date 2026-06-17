// HeatPipeSolverTests.cs — Sprint HP.W1 unit tests for the closed-form
// heat-pipe performance snapshot.

using System;
using Voxelforge.HeatPipe;
using Xunit;

namespace Voxelforge.Tests.HeatPipe;

public sealed class HeatPipeSolverTests
{
    // ── Registry ─────────────────────────────────────────────────────────

    [Fact]
    public void Registry_HighTFluids_HaveHigherCapillaryLimitThanWater()
    {
        Assert.True(HeatPipeFluidRegistry.Sodium.CapillaryLimitPerArea_W_m2
                  > HeatPipeFluidRegistry.Water.CapillaryLimitPerArea_W_m2);
        Assert.True(HeatPipeFluidRegistry.Lithium.CapillaryLimitPerArea_W_m2
                  > HeatPipeFluidRegistry.Sodium.CapillaryLimitPerArea_W_m2);
    }

    [Fact]
    public void Registry_EnvelopeBands_OrderedByTemperature()
    {
        // Water envelope max < Sodium min < Sodium max < Lithium min.
        var w  = HeatPipeFluidRegistry.Water;
        var na = HeatPipeFluidRegistry.Sodium;
        var li = HeatPipeFluidRegistry.Lithium;
        Assert.True(w.OperatingTempMax_K  < na.OperatingTempMin_K);
        Assert.True(na.OperatingTempMax_K < li.OperatingTempMin_K);
    }

    [Fact]
    public void Registry_For_ThrowsOnNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HeatPipeFluidRegistry.For(HeatPipeFluid.None));
    }

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneFluid()
    {
        var d = CpuCoolerHeatPipe() with { Fluid = HeatPipeFluid.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroDiameter()
    {
        var d = CpuCoolerHeatPipe() with { InternalDiameter_m = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroHeatThroughput()
    {
        var d = CpuCoolerHeatPipe() with { HeatThroughput_W = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── CPU-cooler-class baseline (6 mm Cu-water) ───────────────────────

    [Fact]
    public void CpuCoolerHeatPipe_CapillaryLimitInClusterBand()
    {
        // D = 6 mm → A_cross = 2.83e-5 m². q_cap · A = 1e7 · 2.83e-5
        // = 283 W. Cluster band [200, 400] W.
        var r = HeatPipeSolver.Solve(CpuCoolerHeatPipe());
        Assert.InRange(r.CapillaryLimit_W, 200.0, 400.0);
    }

    [Fact]
    public void CpuCoolerHeatPipe_CapillaryMarginPositive()
    {
        // 50 W transported vs 283 W limit → margin ≈ 5.66.
        var r = HeatPipeSolver.Solve(CpuCoolerHeatPipe());
        Assert.True(r.CapillaryMargin > 1.0,
            $"Capillary margin ({r.CapillaryMargin:F2}) must be > 1.0 for safe ops.");
    }

    [Fact]
    public void CpuCoolerHeatPipe_DeltaT_VastlyLessThanCopperRod()
    {
        // 200 mm copper rod at same A: R_Cu = L/(k_Cu·A) = 0.20/(400·2.83e-5)
        // ≈ 17.7 K/W → 50 W·17.7 ≈ 884 K (impossible!). Heat pipe ΔT
        // should be < 20 K — many orders of magnitude better.
        var r = HeatPipeSolver.Solve(CpuCoolerHeatPipe());
        Assert.True(r.EndToEndDeltaT_K < 20.0,
            $"Heat-pipe ΔT ({r.EndToEndDeltaT_K:F2} K) expected < 20 K, vs "
          + "~ 884 K for an equivalent copper rod.");
    }

    [Fact]
    public void CpuCoolerHeatPipe_OperatingTemperatureInEnvelope()
    {
        var r = HeatPipeSolver.Solve(CpuCoolerHeatPipe());
        Assert.True(r.OperatingTemperatureInValidEnvelope);
    }

    // ── Reactor-decay-heat sodium heat pipe ─────────────────────────────

    [Fact]
    public void NaHeatPipe_ReactorDecayHeat_HighThroughputInEnvelope()
    {
        var d = CpuCoolerHeatPipe() with
        {
            Fluid                = HeatPipeFluid.Sodium,
            InternalDiameter_m   = 0.025,    // 25 mm
            Length_m             = 1.5,
            HeatThroughput_W     = 5_000.0,  // 5 kW per pipe
            OperatingTemperature_K = 700.0,  // 700 K mean (~ 425 °C)
        };
        var r = HeatPipeSolver.Solve(d);
        // Sodium 5e7 W/m² · π·0.025²/4 = 5e7·4.91e-4 = 24,544 W limit.
        Assert.InRange(r.CapillaryLimit_W, 20_000.0, 30_000.0);
        Assert.True(r.CapillaryMargin > 1.0);
        Assert.True(r.OperatingTemperatureInValidEnvelope);
    }

    // ── Out-of-envelope ──────────────────────────────────────────────────

    [Fact]
    public void Water_AtHighTemperature_FlagsOutOfEnvelope()
    {
        // Water boiling-point ceiling around 200 °C = 473 K. Above this
        // the working fluid is overcritical → not valid.
        var d = CpuCoolerHeatPipe() with { OperatingTemperature_K = 600.0 };
        var r = HeatPipeSolver.Solve(d);
        Assert.False(r.OperatingTemperatureInValidEnvelope);
    }

    // ── Scaling sanity ──────────────────────────────────────────────────

    [Fact]
    public void CapillaryLimit_QuadraticInDiameter()
    {
        // A_cross ∝ D² → Q_max ∝ D². Doubling D quadruples Q_max.
        double q1 = HeatPipeSolver.ComputeMaximumHeatThroughput(HeatPipeFluid.Water, 0.003);
        double q2 = HeatPipeSolver.ComputeMaximumHeatThroughput(HeatPipeFluid.Water, 0.006);
        Assert.Equal(4.0, q2 / q1, precision: 6);
    }

    [Fact]
    public void ThermalResistance_LinearInLength()
    {
        var lo = HeatPipeSolver.Solve(CpuCoolerHeatPipe() with { Length_m = 0.10 });
        var hi = HeatPipeSolver.Solve(CpuCoolerHeatPipe() with { Length_m = 0.20 });
        Assert.Equal(2.0, hi.ThermalResistance_K_W / lo.ThermalResistance_K_W, precision: 6);
    }

    [Fact]
    public void EndToEndDeltaT_LinearInHeatThroughput()
    {
        var lo = HeatPipeSolver.Solve(CpuCoolerHeatPipe() with { HeatThroughput_W = 25.0 });
        var hi = HeatPipeSolver.Solve(CpuCoolerHeatPipe() with { HeatThroughput_W = 50.0 });
        Assert.Equal(2.0, hi.EndToEndDeltaT_K / lo.EndToEndDeltaT_K, precision: 6);
    }

    [Fact]
    public void MaxThroughputHelper_RejectsZeroDiameter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HeatPipeSolver.ComputeMaximumHeatThroughput(HeatPipeFluid.Water, 0.0));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // 6 mm Cu-water heat pipe — desktop / laptop CPU cooler workhorse.
    // Q = 50 W (typical CPU TDP), L = 200 mm, T = 80 °C operating.
    private static HeatPipeDesign CpuCoolerHeatPipe() => new(
        Fluid:                   HeatPipeFluid.Water,
        InternalDiameter_m:      0.006,
        Length_m:                0.20,
        HeatThroughput_W:        50.0,
        OperatingTemperature_K:  353.15);    // 80 °C
}
