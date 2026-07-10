// HeatPipeSolverTests — pins the closed-form heat-pipe transport math:
// per-limit throughputs Q_i = q_i·A_cross, thermal resistance L/(k_eff·A),
// end-to-end ΔT = Q·R, the HP.W2 governing-limit min(), and the
// temperature-based fluid auto-selection. The sodium case reproduces the
// #548-C anchor (4 kW over 1 m × 25 mm → ΔT ≈ 45 K < 50 K). Pure closed
// form → exact assertions. Backfills coverage onto the Linux 'core' CI leg.

using System;
using Voxelforge.HeatPipe;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class HeatPipeSolverTests
{
    // Na-stainless pipe at the #548-C demo point: D = 25 mm, L = 1 m,
    // Q = 4 kW, T = 700 K. Sodium: q_cap = 5e7, k_eff = 180 000,
    // q_sonic = 2e7, q_entrain = 8e7.
    private static HeatPipeDesign SodiumPipe()
        => new(HeatPipeFluid.Sodium, InternalDiameter_m: 0.025, Length_m: 1.0,
               HeatThroughput_W: 4000.0, OperatingTemperature_K: 700.0);

    private static readonly double SodiumArea = Math.PI * 0.025 * 0.025 * 0.25;

    [Fact]
    public void Solve_Sodium_CapillaryLimitAndMargin()
    {
        var r = HeatPipeSolver.Solve(SodiumPipe());
        Assert.Equal(5.0e7 * SodiumArea, r.CapillaryLimit_W, 6);   // ≈ 24 544 W
        Assert.Equal(5.0e7 * SodiumArea / 4000.0, r.CapillaryMargin, 12);
        Assert.True(r.OperatingTemperatureInValidEnvelope);        // 700 ∈ [673, 1073]
    }

    [Fact]
    public void Solve_Sodium_EndToEndDeltaT_ReproducesResolvedAnchor()
    {
        var r = HeatPipeSolver.Solve(SodiumPipe());
        // R = L/(k_eff·A) ≈ 0.01132 K/W → ΔT = Q·R ≈ 45.27 K.
        Assert.Equal(1.0 / (180_000.0 * SodiumArea), r.ThermalResistance_K_W, 15);
        Assert.Equal(45.27073936836133, r.EndToEndDeltaT_K, 9);
        // #548-C cluster anchor: the corrected k_eff keeps ΔT below 50 K.
        Assert.True(r.EndToEndDeltaT_K < 50.0);
    }

    [Fact]
    public void Solve_Sodium_GoverningLimitIsSonic()
    {
        // Sodium constants order q_sonic (2e7) < q_cap (5e7) < q_entrain (8e7),
        // so the binding constraint is the sonic limit, not capillary.
        var r = HeatPipeSolver.Solve(SodiumPipe());
        Assert.Equal(2.0e7 * SodiumArea, r.SonicLimit_W, 6);
        Assert.Equal(8.0e7 * SodiumArea, r.EntrainmentLimit_W, 6);
        Assert.Equal(r.SonicLimit_W, r.GoverningLimit_W, 9);
        Assert.True(r.GoverningLimit_W < r.CapillaryLimit_W);
        Assert.Equal(2.0e7 * SodiumArea / 4000.0, r.GoverningMargin, 12);
    }

    [Fact]
    public void Solve_Water_GoverningLimitIsCapillary()
    {
        // Water orders q_cap (1e7) < q_entrain (5e7) < q_sonic (1e8).
        var r = HeatPipeSolver.Solve(new HeatPipeDesign(
            HeatPipeFluid.Water, 0.006, 0.3, 100.0, 350.0));
        Assert.Equal(r.CapillaryLimit_W, r.GoverningLimit_W, 9);
        Assert.True(r.OperatingTemperatureInValidEnvelope);        // 350 ∈ [283, 473]
    }

    [Fact]
    public void Solve_OutOfEnvelopeTemperature_IsFlaggedNotRejected()
    {
        var r = HeatPipeSolver.Solve(SodiumPipe() with { OperatingTemperature_K = 300.0 });
        Assert.False(r.OperatingTemperatureInValidEnvelope);       // 300 < 673 floor
    }

    [Fact]
    public void ComputeMaximumHeatThroughput_ScalesWithBoreArea()
    {
        // 6 mm Cu-water pipe: A = π·0.006²/4 → Q_max = 1e7·A ≈ 282.7 W.
        double a = Math.PI * 0.006 * 0.006 * 0.25;
        Assert.Equal(1.0e7 * a,
            HeatPipeSolver.ComputeMaximumHeatThroughput(HeatPipeFluid.Water, 0.006), 9);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HeatPipeSolver.ComputeMaximumHeatThroughput(HeatPipeFluid.Water, 0.0));
    }

    [Fact]
    public void SelectFluidForTemperature_PicksClusterByEnvelopeFloor()
    {
        Assert.Equal(HeatPipeFluid.Water, HeatPipeFluidRegistry.SelectFluidForTemperature(300.0));
        Assert.Equal(HeatPipeFluid.Sodium, HeatPipeFluidRegistry.SelectFluidForTemperature(700.0));
        Assert.Equal(HeatPipeFluid.Lithium, HeatPipeFluidRegistry.SelectFluidForTemperature(1300.0));
        // Boundary values belong to the higher-T cluster (>= comparisons).
        Assert.Equal(HeatPipeFluid.Sodium, HeatPipeFluidRegistry.SelectFluidForTemperature(673.0));
        Assert.Equal(HeatPipeFluid.Lithium, HeatPipeFluidRegistry.SelectFluidForTemperature(1273.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HeatPipeFluidRegistry.SelectFluidForTemperature(0.0));
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() =>
            HeatPipeSolver.Solve(SodiumPipe() with { HeatThroughput_W = 0.0 }));
        Assert.Throws<ArgumentException>(() =>
            HeatPipeSolver.Solve(SodiumPipe() with { Fluid = HeatPipeFluid.None }));
        Assert.Throws<ArgumentException>(() =>
            HeatPipeSolver.Solve(SodiumPipe() with { InternalDiameter_m = -0.01 }));
    }
}
