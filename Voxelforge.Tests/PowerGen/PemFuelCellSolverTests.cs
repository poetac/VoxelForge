// PemFuelCellSolverTests.cs — Sprint PG.W1 unit tests for the
// closed-form PEM fuel cell stack performance snapshot.

using System;
using Voxelforge.PowerGen;
using Xunit;

namespace Voxelforge.Tests.PowerGen;

public sealed class PemFuelCellSolverTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNonPemKind()
    {
        var d = Mirai() with { Kind = PowerGenKind.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositiveCellCount()
    {
        var d = Mirai() with { CellCount = 0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositiveCurrentDensity()
    {
        var d = Mirai() with { OperatingCurrentDensity_A_cm2 = -0.1 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── Mirai-class baseline ─────────────────────────────────────────────

    [Fact]
    public void Mirai_DesignPoint_VoltageInClusterBand()
    {
        // Toyota Mirai-class single-stack at i=1.0 A/cm², T=80 °C, P=2.5 bar:
        //   Expected V_cell ≈ 0.66 V (cluster mid-band 0.6-0.7 V).
        var r = PemFuelCellSolver.Solve(Mirai());
        Assert.InRange(r.CellVoltage_V, 0.60, 0.72);
    }

    [Fact]
    public void Mirai_DesignPoint_LhvEfficiencyAround53Percent()
    {
        // V_cell / V_LHV ≈ 0.66/1.254 ≈ 0.527.
        var r = PemFuelCellSolver.Solve(Mirai());
        Assert.InRange(r.LhvEfficiency, 0.45, 0.58);
    }

    [Fact]
    public void Mirai_DesignPoint_StackPowerInClusterBand()
    {
        // 330 cells × 0.66 V × 200 A = ~ 43.6 kW (single sub-stack of
        // the dual-stack Mirai topology; full Mirai is 2× this).
        var r = PemFuelCellSolver.Solve(Mirai());
        Assert.InRange(r.StackElectricPower_W, 30_000.0, 60_000.0);
    }

    [Fact]
    public void Mirai_DesignPoint_LossBreakdownAddsUpToNernstMinusCell()
    {
        var r = PemFuelCellSolver.Solve(Mirai());
        double reconstructed = r.NernstVoltage_V
                             - r.ActivationLoss_V
                             - r.OhmicLoss_V
                             - r.ConcentrationLoss_V;
        Assert.Equal(r.CellVoltage_V, reconstructed, precision: 9);
    }

    [Fact]
    public void Mirai_HeatRejection_PositiveAndBalancedAgainstLHV()
    {
        // Q_heat = N · (V_LHV − V_cell) · I_stack.
        var r = PemFuelCellSolver.Solve(Mirai());
        Assert.True(r.HeatRejectionPower_W > 0);
        double expected = Mirai().CellCount
            * (PemFuelCellSolver.LhvThermoNeutralVoltage_V - r.CellVoltage_V)
            * r.StackCurrent_A;
        Assert.Equal(expected, r.HeatRejectionPower_W, precision: 4);
    }

    // ── Loss-term scaling sanity ─────────────────────────────────────────

    [Fact]
    public void OhmicLoss_LinearInCurrentDensity()
    {
        var lo = PemFuelCellSolver.Solve(Mirai() with { OperatingCurrentDensity_A_cm2 = 0.5 });
        var hi = PemFuelCellSolver.Solve(Mirai() with { OperatingCurrentDensity_A_cm2 = 1.0 });
        // η_ohm = i · R_AS → doubling i doubles η_ohm.
        Assert.Equal(2.0, hi.OhmicLoss_V / lo.OhmicLoss_V, precision: 6);
    }

    [Fact]
    public void ActivationLoss_TafelLogScalingInCurrentDensity()
    {
        // η_act(i₂) − η_act(i₁) = (b/ln10) · ln(i₂/i₁).
        // For i₁ = 0.5, i₂ = 1.0 → Δη_act = (0.070/ln10) · ln(2) ≈ 0.0211 V.
        var lo = PemFuelCellSolver.Solve(Mirai() with { OperatingCurrentDensity_A_cm2 = 0.5 });
        var hi = PemFuelCellSolver.Solve(Mirai() with { OperatingCurrentDensity_A_cm2 = 1.0 });
        double expected = (PemFuelCellSolver.TafelSlope_V_dec / Math.Log(10.0)) * Math.Log(2.0);
        Assert.Equal(expected, hi.ActivationLoss_V - lo.ActivationLoss_V, precision: 6);
    }

    [Fact]
    public void ConcentrationLoss_DivergesAsCurrentApproachesLimit()
    {
        // At i → i_L = 2.0 A/cm² → η_conc → +∞.
        var nearLimit = PemFuelCellSolver.Solve(Mirai() with { OperatingCurrentDensity_A_cm2 = 1.95 });
        var farLimit  = PemFuelCellSolver.Solve(Mirai() with { OperatingCurrentDensity_A_cm2 = 1.0 });
        Assert.True(nearLimit.ConcentrationLoss_V > farLimit.ConcentrationLoss_V * 3.0,
            $"Near-limit η_conc ({nearLimit.ConcentrationLoss_V:F4}) expected ≫ "
          + $"far-limit ({farLimit.ConcentrationLoss_V:F4}).");
    }

    [Fact]
    public void ConcentrationLoss_AtOrAboveLimit_IsInfinite()
    {
        var atLimit = PemFuelCellSolver.Solve(Mirai() with { OperatingCurrentDensity_A_cm2 = 2.0 });
        Assert.True(double.IsPositiveInfinity(atLimit.ConcentrationLoss_V));
    }

    // ── Stack-level scaling ──────────────────────────────────────────────

    [Fact]
    public void StackVoltage_ScalesLinearlyInCellCount()
    {
        var single = PemFuelCellSolver.Solve(Mirai() with { CellCount = 1 });
        var stack  = PemFuelCellSolver.Solve(Mirai() with { CellCount = 330 });
        // Same V_cell × N_cells → 330× stack voltage.
        Assert.Equal(330.0, stack.StackVoltage_V / single.StackVoltage_V, precision: 6);
    }

    [Fact]
    public void StackCurrent_ScalesLinearlyInActiveArea()
    {
        var small = PemFuelCellSolver.Solve(Mirai() with { ActiveAreaPerCell_cm2 = 100.0 });
        var big   = PemFuelCellSolver.Solve(Mirai() with { ActiveAreaPerCell_cm2 = 200.0 });
        // I_stack = i · A → doubles when A doubles.
        Assert.Equal(2.0, big.StackCurrent_A / small.StackCurrent_A, precision: 6);
    }

    // ── Nernst / pressure / temperature ──────────────────────────────────

    [Fact]
    public void NernstVoltage_IncreasesWithPressure()
    {
        // E_pressure correction is +ve in ln(P/P_ref).
        var lowP  = PemFuelCellSolver.Solve(Mirai() with { OperatingPressure_bar = 1.0 });
        var highP = PemFuelCellSolver.Solve(Mirai() with { OperatingPressure_bar = 3.0 });
        Assert.True(highP.NernstVoltage_V > lowP.NernstVoltage_V);
    }

    [Fact]
    public void NernstVoltage_DecreasesWithTemperature()
    {
        // dE/dT < 0 → higher T lowers E_Nernst.
        var coolStack = PemFuelCellSolver.Solve(Mirai() with { OperatingTemperature_C = 60.0 });
        var hotStack  = PemFuelCellSolver.Solve(Mirai() with { OperatingTemperature_C = 90.0 });
        Assert.True(hotStack.NernstVoltage_V < coolStack.NernstVoltage_V);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Toyota Mirai-class single sub-stack baseline.
    private static PemFuelCellDesign Mirai() => new(
        Kind:                         PowerGenKind.PemFuelCell,
        CellCount:                    330,
        ActiveAreaPerCell_cm2:        200.0,
        OperatingCurrentDensity_A_cm2: 1.0,
        OperatingTemperature_C:       80.0,
        OperatingPressure_bar:         2.5);
}
