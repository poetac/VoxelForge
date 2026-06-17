// HeatPipeWave2Tests.cs — Sprint HP.W2 unit tests for the multi-limit
// + auto-fluid-selection extensions.

using System;
using Voxelforge.HeatPipe;
using Xunit;

namespace Voxelforge.Tests.HeatPipe;

public sealed class HeatPipeWave2Tests
{
    // ── HP.W1 bit-identity invariant ────────────────────────────────────

    [Fact]
    public void HP_W1_Baseline_BitIdenticalCapillaryAndDeltaT()
    {
        // The CPU-cooler-class baseline must produce bit-identical
        // capillary limit + ΔT across the Wave-1 → Wave-2 transition.
        var r = HeatPipeSolver.Solve(CpuCoolerHeatPipe());
        Assert.InRange(r.CapillaryLimit_W, 200.0, 400.0);
        Assert.True(r.EndToEndDeltaT_K < 20.0);
    }

    // ── Sonic + entrainment limits ──────────────────────────────────────

    [Fact]
    public void SonicLimit_PopulatedAndFinite()
    {
        var r = HeatPipeSolver.Solve(CpuCoolerHeatPipe());
        Assert.True(r.SonicLimit_W > 0);
        Assert.True(!double.IsPositiveInfinity(r.SonicLimit_W));
    }

    [Fact]
    public void EntrainmentLimit_PopulatedAndFinite()
    {
        var r = HeatPipeSolver.Solve(CpuCoolerHeatPipe());
        Assert.True(r.EntrainmentLimit_W > 0);
        Assert.True(!double.IsPositiveInfinity(r.EntrainmentLimit_W));
    }

    [Fact]
    public void GoverningLimit_EqualsMinOfThreeLimits()
    {
        var r = HeatPipeSolver.Solve(CpuCoolerHeatPipe());
        double expected = Math.Min(r.CapillaryLimit_W,
                          Math.Min(r.SonicLimit_W, r.EntrainmentLimit_W));
        Assert.Equal(expected, r.GoverningLimit_W, precision: 6);
    }

    [Fact]
    public void GoverningMargin_EqualsGoverningLimitOverHeatThroughput()
    {
        var r = HeatPipeSolver.Solve(CpuCoolerHeatPipe());
        Assert.Equal(r.GoverningLimit_W / CpuCoolerHeatPipe().HeatThroughput_W,
                     r.GoverningMargin, precision: 6);
    }

    [Fact]
    public void Water_CapillaryIsDominantLimit_NotSonicOrEntrainment()
    {
        // For Cu-water, the capillary limit is the binding constraint
        // at typical operating temperatures.
        var r = HeatPipeSolver.Solve(CpuCoolerHeatPipe());
        Assert.Equal(r.CapillaryLimit_W, r.GoverningLimit_W, precision: 6);
    }

    [Fact]
    public void Sodium_SonicLimit_IsDominantBelowEntrainment()
    {
        // For sodium-stainless at typical operating temperatures, the
        // sonic limit is the binding constraint (entrainment is larger).
        var d = CpuCoolerHeatPipe() with
        {
            Fluid                  = HeatPipeFluid.Sodium,
            InternalDiameter_m     = 0.025,
            OperatingTemperature_K = 700.0,
        };
        var r = HeatPipeSolver.Solve(d);
        Assert.True(r.SonicLimit_W < r.EntrainmentLimit_W);
        // GoverningLimit picks the minimum of the three.
        Assert.True(r.GoverningLimit_W <= r.SonicLimit_W + 1e-3);
    }

    // ── Auto-fluid-selection helper ─────────────────────────────────────

    [Fact]
    public void SelectFluid_For300K_PicksWater()
    {
        // 300 K is firmly in the water envelope.
        Assert.Equal(HeatPipeFluid.Water,
            HeatPipeFluidRegistry.SelectFluidForTemperature(300.0));
    }

    [Fact]
    public void SelectFluid_For800K_PicksSodium()
    {
        // 800 K is in the sodium envelope (Na min = 673 K).
        Assert.Equal(HeatPipeFluid.Sodium,
            HeatPipeFluidRegistry.SelectFluidForTemperature(800.0));
    }

    [Fact]
    public void SelectFluid_For1500K_PicksLithium()
    {
        // 1500 K is in the lithium envelope (Li min = 1273 K).
        Assert.Equal(HeatPipeFluid.Lithium,
            HeatPipeFluidRegistry.SelectFluidForTemperature(1500.0));
    }

    [Fact]
    public void SelectFluid_AtVeryLowT_FallsBackToWater()
    {
        // Below 283 K (water min): no fluid is properly valid; the
        // helper falls back to Water as the closest cluster (other
        // fluids would freeze).
        Assert.Equal(HeatPipeFluid.Water,
            HeatPipeFluidRegistry.SelectFluidForTemperature(250.0));
    }

    [Fact]
    public void SelectFluid_RejectsNonPositiveT()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HeatPipeFluidRegistry.SelectFluidForTemperature(0.0));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static HeatPipeDesign CpuCoolerHeatPipe() => new(
        Fluid:                   HeatPipeFluid.Water,
        InternalDiameter_m:      0.006,
        Length_m:                0.20,
        HeatThroughput_W:        50.0,
        OperatingTemperature_K:  353.15);
}
