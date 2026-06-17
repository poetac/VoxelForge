// MaeckerKovityaArcModelTests.cs — physics-level unit tests for the Wave-2
// arcjet thermal-arc model. Sibling to BuschDischargeModelTests on the HET side.

using System;
using Voxelforge.ElectricPropulsion.Solvers;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class MaeckerKovityaArcModelTests
{
    // MR-509 ATOS anchor (matches the Validation fixture).
    private static MaeckerKovityaResult Mr509() => MaeckerKovityaArcModel.Solve(
        arcVoltage_V:           100.0,
        arcCurrent_A:            18.0,
        arcGap_mm:                2.0,
        propellantMassFlow_kgs:  3.9e-5,
        nozzleThroatRadius_mm:   0.5,
        chamberLength_mm:       12.0,
        chamberRadius_mm:        4.0,
        thermalEfficiency:      MaeckerKovityaArcModel.DefaultThermalEfficiency);

    [Fact]
    public void Solve_Mr509_ProducesPositivePerformance()
    {
        var r = Mr509();
        Assert.True(r.Thrust_N > 0);
        Assert.True(r.IspVacuum_s > 0);
        Assert.True(r.ExitVelocity_ms > 0);
        Assert.True(r.Converged);
    }

    [Fact]
    public void Solve_Mr509_ArcPower_MatchesVtimesI()
    {
        var r = Mr509();
        Assert.Equal(100.0 * 18.0, r.ArcPower_W, precision: 6);
    }

    [Fact]
    public void Solve_Mr509_GasEnthalpyEqualsEtaThermalTimesArcPower()
    {
        var r = Mr509();
        double expected = MaeckerKovityaArcModel.DefaultThermalEfficiency * r.ArcPower_W;
        Assert.Equal(expected, r.GasEnthalpyGain_W, precision: 6);
    }

    [Fact]
    public void Solve_Mr509_PlumeAngle_InClusterBand()
    {
        // Sutton 9e §16.3: 15-25° band for low-power arcjets.
        var r = Mr509();
        double thetaDeg = r.PlumeDivergenceHalfAngle_rad * 180.0 / Math.PI;
        Assert.InRange(thetaDeg, 15.0, 25.0);
    }

    [Fact]
    public void Solve_Mr509_AnodeWallTemp_BelowTungstenLimit()
    {
        var r = Mr509();
        // MR-509 with tungsten anode should land well below the 3650 K limit;
        // ~1500-2500 K is typical for a 1.8 kW arcjet at 40% η_thermal.
        Assert.InRange(r.AnodeWallTemp_K, 500.0, 3650.0);
    }

    [Fact]
    public void Solve_HigherThermalEfficiency_YieldsHigherIsp()
    {
        // ∂Isp/∂η_thermal > 0: more energy in the gas → higher V_exit.
        var rLow  = MaeckerKovityaArcModel.Solve(100.0, 18.0, 2.0, 3.9e-5, 0.5, 12.0, 4.0, 0.30);
        var rHigh = MaeckerKovityaArcModel.Solve(100.0, 18.0, 2.0, 3.9e-5, 0.5, 12.0, 4.0, 0.50);
        Assert.True(rHigh.IspVacuum_s > rLow.IspVacuum_s);
    }

    [Fact]
    public void Solve_HigherMassFlow_AtFixedPower_LowersIsp()
    {
        // ∂Isp/∂ṁ < 0 at fixed P_arc: V_exit ∝ √(P/ṁ), Isp ∝ V_eff/g₀.
        var rLow  = MaeckerKovityaArcModel.Solve(100.0, 18.0, 2.0, 3.0e-5, 0.5, 12.0, 4.0, 0.40);
        var rHigh = MaeckerKovityaArcModel.Solve(100.0, 18.0, 2.0, 5.0e-5, 0.5, 12.0, 4.0, 0.40);
        Assert.True(rLow.IspVacuum_s > rHigh.IspVacuum_s);
    }

    [Fact]
    public void Solve_DoublingPower_RaisesExitVelocityBySqrtTwo()
    {
        // V_exit = √(2·η·P/ṁ); doubling P_arc raises V_exit by exactly √2.
        var rBase = MaeckerKovityaArcModel.Solve(100.0, 18.0, 2.0, 3.9e-5, 0.5, 12.0, 4.0, 0.40);
        var rDbl  = MaeckerKovityaArcModel.Solve(200.0, 18.0, 2.0, 3.9e-5, 0.5, 12.0, 4.0, 0.40);
        double ratio = rDbl.ExitVelocity_ms / rBase.ExitVelocity_ms;
        Assert.InRange(ratio, Math.Sqrt(2.0) - 1e-9, Math.Sqrt(2.0) + 1e-9);
    }

    [Fact]
    public void Solve_Deterministic()
    {
        var r1 = Mr509();
        var r2 = Mr509();
        Assert.Equal(r1.Thrust_N,         r2.Thrust_N);
        Assert.Equal(r1.IspVacuum_s,      r2.IspVacuum_s);
        Assert.Equal(r1.ExitVelocity_ms,  r2.ExitVelocity_ms);
        Assert.Equal(r1.AnodeWallTemp_K,  r2.AnodeWallTemp_K);
    }

    // ── Validation guards ────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(-50.0)]
    public void Solve_NonPositiveVoltage_Throws(double voltage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MaeckerKovityaArcModel.Solve(voltage, 18.0, 2.0, 3.9e-5, 0.5, 12.0, 4.0, 0.40));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Solve_NonPositiveCurrent_Throws(double current)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MaeckerKovityaArcModel.Solve(100.0, current, 2.0, 3.9e-5, 0.5, 12.0, 4.0, 0.40));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    public void Solve_NonPositiveArcGap_Throws(double gap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MaeckerKovityaArcModel.Solve(100.0, 18.0, gap, 3.9e-5, 0.5, 12.0, 4.0, 0.40));
    }

    [Fact]
    public void Solve_NonPositiveMassFlow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MaeckerKovityaArcModel.Solve(100.0, 18.0, 2.0, 0.0, 0.5, 12.0, 4.0, 0.40));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.10)]
    [InlineData(1.5)]
    public void Solve_ThermalEfficiencyOutOfRange_Throws(double eta)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MaeckerKovityaArcModel.Solve(100.0, 18.0, 2.0, 3.9e-5, 0.5, 12.0, 4.0, eta));
    }

    [Fact]
    public void Solve_NonPositiveThroat_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MaeckerKovityaArcModel.Solve(100.0, 18.0, 2.0, 3.9e-5, 0.0, 12.0, 4.0, 0.40));
    }

    [Fact]
    public void Solve_NonPositiveChamberLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MaeckerKovityaArcModel.Solve(100.0, 18.0, 2.0, 3.9e-5, 0.5, 0.0, 4.0, 0.40));
    }

    [Fact]
    public void Solve_NonPositiveChamberRadius_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MaeckerKovityaArcModel.Solve(100.0, 18.0, 2.0, 3.9e-5, 0.5, 12.0, 0.0, 0.40));
    }
}
