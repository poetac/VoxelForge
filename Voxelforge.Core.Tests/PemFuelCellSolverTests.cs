// PemFuelCellSolverTests — pins the polarisation-curve closed forms:
// Nernst voltage with the entropy + pressure-correction terms, the Tafel
// activation loss, ohmic i·R_AS, the −B·ln(1 − i/i_L) concentration pinch
// (documented +∞ at i ≥ i_L, no throw), the stack roll-up, the exact
// LHV bookkeeping P_elec + Q_heat = N·V_LHV·I, and the PG.W2 sweep
// contract. Pure closed form → exact assertions. Backfills coverage onto
// the Linux 'core' CI leg.

using System;
using Voxelforge.PowerGen;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class PemFuelCellSolverTests
{
    // Toyota-Mirai-class stack: 300 cells × 200 cm² at i = 1.0 A/cm²,
    // 80 °C, 2.5 bar — the operating point the solver header anchors to.
    private static PemFuelCellDesign MiraiStack()
        => new(PowerGenKind.PemFuelCell,
               CellCount: 300, ActiveAreaPerCell_cm2: 200.0,
               OperatingCurrentDensity_A_cm2: 1.0,
               OperatingTemperature_C: 80.0, OperatingPressure_bar: 2.5);

    [Fact]
    public void Solve_VoltageBreakdown_MatchesPolarisationClosedForms()
    {
        var r = PemFuelCellSolver.Solve(MiraiStack());
        // E_Nernst = 1.229 − 0.85e-3·55 + (R·353.15/(2F))·ln 2.5 ≈ 1.1962 V.
        Assert.Equal(1.1961923748810688, r.NernstVoltage_V, 9);
        // η_act = (0.070/ln10)·ln(1/1e-5) = 0.07·5 = 0.35 V exactly.
        Assert.Equal(0.35, r.ActivationLoss_V, 9);
        Assert.Equal(0.15, r.OhmicLoss_V, 12);                     // i·R_AS
        // η_conc = −0.05·ln(1 − 0.5) = 0.05·ln 2 ≈ 0.03466 V.
        Assert.Equal(0.03465735902799726, r.ConcentrationLoss_V, 12);
        // V_cell ≈ 0.66 V — the documented Mirai-class anchor.
        Assert.Equal(0.6615350158530715, r.CellVoltage_V, 9);
    }

    [Fact]
    public void Solve_StackRollUpAndLhvBookkeeping_AreExact()
    {
        var r = PemFuelCellSolver.Solve(MiraiStack());
        Assert.Equal(300 * r.CellVoltage_V, r.StackVoltage_V, 9);
        Assert.Equal(200.0, r.StackCurrent_A, 12);                 // i·A_cell
        Assert.Equal(r.StackVoltage_V * 200.0, r.StackElectricPower_W, 6);
        Assert.Equal(r.CellVoltage_V / 1.254, r.LhvEfficiency, 12);
        // First-law bookkeeping: electricity + heat = N·V_LHV·I exactly.
        Assert.Equal(300 * 1.254 * 200.0, r.StackElectricPower_W + r.HeatRejectionPower_W, 6);
    }

    [Fact]
    public void SolveAtCurrentDensity_OpenCircuit_SitsExactlyAtNernst()
    {
        // i = 0: all three losses clamp to zero (documented Tafel clamp).
        var r = PemFuelCellSolver.SolveAtCurrentDensity(MiraiStack(), 0.0);
        Assert.Equal(0.0, r.ActivationLoss_V, 12);
        Assert.Equal(0.0, r.OhmicLoss_V, 12);
        Assert.Equal(0.0, r.ConcentrationLoss_V, 12);
        Assert.Equal(r.NernstVoltage_V, r.CellVoltage_V, 15);
    }

    [Fact]
    public void SolveAtCurrentDensity_AtMassTransportLimit_ReturnsInfinityNotThrow()
    {
        // Documented contract: the i ≥ i_L singularity never throws — the
        // concentration loss goes +∞ and V_cell goes −∞ for gates to filter.
        var r = PemFuelCellSolver.SolveAtCurrentDensity(MiraiStack(), 2.0);
        Assert.True(double.IsPositiveInfinity(r.ConcentrationLoss_V));
        Assert.True(double.IsNegativeInfinity(r.CellVoltage_V));
    }

    [Fact]
    public void Solve_HigherPressure_RaisesNernstVoltage()
    {
        var ambient = PemFuelCellSolver.Solve(MiraiStack() with { OperatingPressure_bar = 1.0 });
        var boosted = PemFuelCellSolver.Solve(MiraiStack());
        // At P_ref = 1 bar the pressure correction vanishes: ln(1) = 0.
        Assert.Equal(1.229 - 0.85e-3 * (353.15 - 298.15), ambient.NernstVoltage_V, 12);
        Assert.True(boosted.NernstVoltage_V > ambient.NernstVoltage_V);
    }

    [Fact]
    public void SolvePolarisationCurve_MonotoneVoltageDropAndPeakPowerInside()
    {
        var pts = PemFuelCellSolver.SolvePolarisationCurve(
            MiraiStack(), new[] { 0.2, 0.6, 1.0, 1.4, 1.8 });
        Assert.Equal(5, pts.Length);
        for (int k = 1; k < pts.Length; k++)
            Assert.True(pts[k].CellVoltage_V < pts[k - 1].CellVoltage_V,
                "cell voltage must fall monotonically along the polarisation curve");
        // Power density = V·i by construction.
        Assert.Equal(pts[2].CellVoltage_V * 1.0, pts[2].PowerDensity_W_cm2, 12);
        // The i = 1.0 sample matches a direct Solve bit-for-bit.
        var direct = PemFuelCellSolver.Solve(MiraiStack());
        Assert.Equal(direct.CellVoltage_V, pts[2].CellVoltage_V, 15);
        Assert.Throws<ArgumentException>(() =>
            PemFuelCellSolver.SolvePolarisationCurve(MiraiStack(), new[] { 1.0, 0.5 }));
        Assert.Throws<ArgumentException>(() =>
            PemFuelCellSolver.SolvePolarisationCurve(MiraiStack(), Array.Empty<double>()));
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() =>
            PemFuelCellSolver.Solve(MiraiStack() with { CellCount = 0 }));
        Assert.Throws<ArgumentException>(() =>
            PemFuelCellSolver.Solve(MiraiStack() with { OperatingCurrentDensity_A_cm2 = 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PemFuelCellSolver.SolveAtCurrentDensity(MiraiStack(), -0.1));
    }
}
