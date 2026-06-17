// BraytonGasLoopSolverTests.cs — Sprint NU.W3 unit tests for the closed-
// cycle He Brayton gas-loop solver.

using System;
using Voxelforge.Nuclear.Brayton;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class BraytonGasLoopSolverTests
{
    // ── Smoke / SP-100-anchored band ─────────────────────────────────────

    [Fact]
    public void Solve_Sp100Anchor_Converges()
    {
        var r = BraytonGasLoopSolver.Solve(
            reactorThermalPower_MW:   1.5,
            electricPowerTarget_kWe: 100.0,
            turbineInletTemp_K:     1300.0,
            hePressure_bar:          120.0,
            alternatorRpm:        45_000.0);
        Assert.True(r.ElectricPowerOutput_kWe > 0);
        Assert.True(r.ThermalEfficiency > 0 && r.ThermalEfficiency < 1.0);
        Assert.True(r.HeMassFlow_kgs > 0);
        Assert.True(r.ReactorPowerToBrayton_MW > 0);
    }

    [Fact]
    public void Solve_Sp100Anchor_EfficiencyInClusterBand()
    {
        // SP-100 cluster anchor 0.20 thermal efficiency. With T_hot=1300 K
        // and T_cold=400 K → Carnot = 0.692. Real efficiency = 0.88·0.86 ·
        // 0.692 · 0.96 · 0.95 = 0.477. Cap at Carnot. Should land between
        // 0.15 (cluster floor) and 0.50 (Carnot fraction with realistic
        // components).
        var r = BraytonGasLoopSolver.Solve(1.5, 100.0, 1300.0, 120.0, 45_000.0);
        Assert.InRange(r.ThermalEfficiency, 0.15, 0.55);
    }

    [Fact]
    public void Solve_Sp100Anchor_CarnotBoundsRealEfficiency()
    {
        var r = BraytonGasLoopSolver.Solve(1.5, 100.0, 1300.0, 120.0, 45_000.0);
        Assert.True(r.ThermalEfficiency <= r.CarnotEfficiency,
            $"Real η ({r.ThermalEfficiency:F3}) must be ≤ Carnot ({r.CarnotEfficiency:F3}).");
    }

    [Fact]
    public void Solve_Sp100Anchor_ReactorPowerCapsAtTotal()
    {
        // Request electric power so high it would need > total reactor power.
        // E.g. 5 MWe target with only 1 MW reactor → solver caps Q_brayton.
        var r = BraytonGasLoopSolver.Solve(1.0, 5000.0, 1300.0, 120.0, 45_000.0);
        Assert.Equal(1.0, r.ReactorPowerToBrayton_MW, precision: 3);
    }

    // ── Carnot scaling ───────────────────────────────────────────────────

    [Fact]
    public void Solve_HigherHotSideTemp_RaisesCarnotEfficiency()
    {
        var rLow  = BraytonGasLoopSolver.Solve(1.5, 100.0, 1100.0, 120.0, 45_000.0);
        var rHigh = BraytonGasLoopSolver.Solve(1.5, 100.0, 1400.0, 120.0, 45_000.0);
        Assert.True(rHigh.CarnotEfficiency > rLow.CarnotEfficiency);
        Assert.True(rHigh.ThermalEfficiency > rLow.ThermalEfficiency);
    }

    [Fact]
    public void Solve_HigherRecuperatorEffectiveness_RaisesRealEfficiency()
    {
        var rLow  = BraytonGasLoopSolver.Solve(1.5, 100.0, 1300.0, 120.0, 45_000.0,
            recuperatorEffectiveness: 0.70);
        var rHigh = BraytonGasLoopSolver.Solve(1.5, 100.0, 1300.0, 120.0, 45_000.0,
            recuperatorEffectiveness: 0.95);
        Assert.True(rHigh.ThermalEfficiency > rLow.ThermalEfficiency);
    }

    // ── Energy balance ───────────────────────────────────────────────────

    [Fact]
    public void Solve_HeMassFlow_GrowsWithElectricPowerTarget()
    {
        var rLo = BraytonGasLoopSolver.Solve(2.0,  50.0, 1300.0, 120.0, 45_000.0);
        var rHi = BraytonGasLoopSolver.Solve(2.0, 200.0, 1300.0, 120.0, 45_000.0);
        Assert.True(rHi.HeMassFlow_kgs > rLo.HeMassFlow_kgs);
    }

    [Fact]
    public void Solve_EnergyBalance_HeFlowMatchesQover_CpDeltaT()
    {
        var r = BraytonGasLoopSolver.Solve(1.5, 100.0, 1300.0, 120.0, 45_000.0);
        // Q_brayton = m_He · cp · (T_hot − T_cold)
        double T_cold = BraytonGasLoopSolver.ColdSideTempAnchor_K;
        double Q_W = r.ReactorPowerToBrayton_MW * 1e6;
        double mDot_expected = Q_W / (BraytonGasLoopSolver.HeliumCp_JkgK * (r.TurbineInletTemp_K - T_cold));
        Assert.Equal(mDot_expected, r.HeMassFlow_kgs, precision: 6);
    }

    // ── NaN-trap + bound-check behaviour ────────────────────────────────

    [Fact]
    public void Solve_NonPositiveReactorPower_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            BraytonGasLoopSolver.Solve(0.0, 100.0, 1300.0, 120.0, 45_000.0));

    [Fact]
    public void Solve_NonPositiveElectricTarget_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            BraytonGasLoopSolver.Solve(1.5, 0.0, 1300.0, 120.0, 45_000.0));

    [Fact]
    public void Solve_TurbineInletBelowColdSide_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            BraytonGasLoopSolver.Solve(1.5, 100.0, 350.0, 120.0, 45_000.0));

    [Fact]
    public void Solve_NonPositiveHePressure_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            BraytonGasLoopSolver.Solve(1.5, 100.0, 1300.0, 0.0, 45_000.0));

    [Fact]
    public void Solve_NonPositiveAlternatorRpm_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            BraytonGasLoopSolver.Solve(1.5, 100.0, 1300.0, 120.0, 0.0));

    [Fact]
    public void Solve_NegativeRecuperator_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            BraytonGasLoopSolver.Solve(1.5, 100.0, 1300.0, 120.0, 45_000.0,
                recuperatorEffectiveness: -0.1));

    [Fact]
    public void Solve_RecuperatorAboveOne_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            BraytonGasLoopSolver.Solve(1.5, 100.0, 1300.0, 120.0, 45_000.0,
                recuperatorEffectiveness: 1.5));

    [Fact]
    public void Solve_NaNRecuperator_UsesDefault()
    {
        var rNaN     = BraytonGasLoopSolver.Solve(1.5, 100.0, 1300.0, 120.0, 45_000.0,
            recuperatorEffectiveness: double.NaN);
        var rDefault = BraytonGasLoopSolver.Solve(1.5, 100.0, 1300.0, 120.0, 45_000.0,
            recuperatorEffectiveness: BraytonGasLoopSolver.DefaultRecuperatorEffectiveness);
        Assert.Equal(rDefault.ThermalEfficiency, rNaN.ThermalEfficiency, precision: 6);
    }
}
