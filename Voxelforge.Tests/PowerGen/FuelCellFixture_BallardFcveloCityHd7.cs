// FuelCellFixture_BallardFcveloCityHd7.cs — Sprint B.11 published-
// product validation fixture for the heavy-duty bus / truck path
// through the PEM fuel cell pillar.
//
// Anchors the model to **Ballard FCveloCity HD7**, the company's
// 85-kW continuous-duty heavy-duty fuel-cell module deployed in
// fuel-cell electric buses worldwide (~ 4000 units shipped through
// 2024). Public datasheet
// (https://www.ballard.com/fuel-cell-solutions/fuel-cell-power-products/motive-modules/fcvelocity-hd):
//   - 85 kW continuous DC power output
//   - 400-580 V DC operational range
//   - Designed for transit-bus duty cycle (constant ~ 50-100 kW load)
//   - Operating temperature 60-80 °C
//   - Air-breathing cathode, humidified inlet
//
// Second anchor for the PEM fuel cell pillar — Wave-1 anchor in
// `PemFuelCellSolverTests` is the **Toyota Mirai** passenger-car class
// (~ 114 kW peak, ~ 370 cells, ~ 230 cm²). Ballard HD7 exercises a
// distinct operating regime: roughly 2× the cell count, smaller
// active area per cell, lower current density (continuous vs peak
// duty), higher stack voltage (transit-bus drive-system fit).
//
// Sprint B.11 in framing-B Phase 3 coverage backfill — third in the
// second-anchor pattern after B.9 (Tesla Megapack Battery) and B.10
// (SunPower X22-360 Photovoltaic). Pure-additive: zero pillar code
// touched, bit-identity preserved.

using Voxelforge.PowerGen;
using Xunit;

namespace Voxelforge.Tests.PowerGen;

public sealed class FuelCellFixture_BallardFcveloCityHd7
{
    // ── Nameplate at heavy-duty continuous operating point ────────────

    [Fact]
    public void BallardHd7_AtDesignPoint_StackPowerNearNameplate()
    {
        // Ballard FCveloCity HD7 nameplate: 85 kW continuous DC.
        // Modelled at i = 0.7 A/cm², T = 70 °C, P = 2.0 bar with a
        // 600-cell × 280 cm² stack → P_elec ≈ 86 kW. Cluster band
        // [60, 110] kW.
        var r = PemFuelCellSolver.Solve(BallardHd7Class());
        double kW = r.StackElectricPower_W / 1.0e3;
        Assert.InRange(kW, 60.0, 110.0);
    }

    [Fact]
    public void BallardHd7_AtDesignPoint_StackVoltageInTransitBusRange()
    {
        // Ballard FCveloCity HD7 datasheet: 400-580 V DC operational
        // range. Modelled V_stack = 600 × V_cell ≈ 441 V at the
        // continuous operating point. Cluster band [380, 580] V —
        // floor slightly below datasheet to accommodate cluster scatter.
        var r = PemFuelCellSolver.Solve(BallardHd7Class());
        Assert.InRange(r.StackVoltage_V, 380.0, 580.0);
    }

    [Fact]
    public void BallardHd7_AtDesignPoint_CellVoltageInTypicalPemBand()
    {
        // PEM cell voltage at moderate load (i ≈ 0.7 A/cm²) lands in
        // the [0.65, 0.80] V cluster band — well clear of the high-i
        // mass-transport pinch (V_cell would collapse near i = i_L =
        // 2.0 A/cm²).
        var r = PemFuelCellSolver.Solve(BallardHd7Class());
        Assert.InRange(r.CellVoltage_V, 0.65, 0.80);
    }

    [Fact]
    public void BallardHd7_AtDesignPoint_LhvEfficiencyInClusterBand()
    {
        // η_LHV = V_cell / V_LHV. At V_cell ≈ 0.735 V → η ≈ 0.587.
        // Commercial heavy-duty PEM cluster 50-65 % LHV.
        var r = PemFuelCellSolver.Solve(BallardHd7Class());
        Assert.InRange(r.LhvEfficiency, 0.50, 0.65);
    }

    [Fact]
    public void BallardHd7_AtDesignPoint_HeatRejectionMatchesEnergyBalance()
    {
        // Q_heat = N_cells · (V_LHV - V_cell) · I_stack must balance
        // exactly against the per-cell LHV deficit times current.
        var r = PemFuelCellSolver.Solve(BallardHd7Class());
        double expected = 600 * (PemFuelCellSolver.LhvThermoNeutralVoltage_V - r.CellVoltage_V)
                        * r.StackCurrent_A;
        Assert.Equal(expected, r.HeatRejectionPower_W, precision: 6);
    }

    [Fact]
    public void BallardHd7_AtDesignPoint_HeatRejectionInBusCoolingEnvelope()
    {
        // For an 85 kW stack at ~ 58 % LHV efficiency, heat rejection
        // is ~ 60 kW. Bus radiators are sized for [40, 80] kW continuous.
        var r = PemFuelCellSolver.Solve(BallardHd7Class());
        double kW_heat = r.HeatRejectionPower_W / 1.0e3;
        Assert.InRange(kW_heat, 40.0, 80.0);
    }

    [Fact]
    public void BallardHd7_LossBreakdownReconstructsCellVoltage()
    {
        // V_cell = E_Nernst − η_act − η_ohm − η_conc (sign convention
        // is OPPOSITE to PEM electrolyser — fuel cell DELIVERS power,
        // so losses are subtracted from Nernst).
        var r = PemFuelCellSolver.Solve(BallardHd7Class());
        double reconstructed = r.NernstVoltage_V
                             - r.ActivationLoss_V
                             - r.OhmicLoss_V
                             - r.ConcentrationLoss_V;
        Assert.Equal(r.CellVoltage_V, reconstructed, precision: 9);
    }

    // ── Heavy-duty-bus duty cycle (vs Mirai peak-power profile) ───────

    [Fact]
    public void BallardHd7_OperatesAtLowerIThanMiraiPeak()
    {
        // Heavy-duty bus: continuous duty at moderate current density
        // (0.5-0.8 A/cm²) for longest stack life. Toyota Mirai peaks
        // at 1.0-1.2 A/cm² for short-burst acceleration. Verify Ballard
        // operating point is at the moderate end.
        Assert.True(BallardHd7Class().OperatingCurrentDensity_A_cm2 < 0.8,
            "Ballard FCveloCity HD7 operates at lower i than Mirai peak.");
        Assert.True(BallardHd7Class().OperatingCurrentDensity_A_cm2 >= 0.5,
            "Ballard FCveloCity HD7 operates above light-load regime.");
    }

    [Fact]
    public void BallardHd7_HasHigherCellCountThanPassengerCar()
    {
        // Transit buses run at ~ 600 V drive-system voltage; the stack
        // must supply 400+ V at operating cell voltage. Ballard HD7's
        // ~ 600-cell stack is roughly 2× the Mirai 370-cell stack.
        Assert.True(BallardHd7Class().CellCount >= 500,
            $"Ballard HD7 cell count ({BallardHd7Class().CellCount}) should "
          + "be ≥ 500 for transit-bus drive-system voltage.");
    }

    // ── Thermodynamic-state validation ────────────────────────────────

    [Fact]
    public void BallardHd7_AtDesignPoint_NernstVoltageAboveOneVolt()
    {
        // E_Nernst at T = 70 °C, P = 2 bar should land near 1.20 V
        // (Larminie & Dicks 2003 §3 baseline). Any value below 1.0 V
        // would indicate a temperature/pressure misconfiguration.
        var r = PemFuelCellSolver.Solve(BallardHd7Class());
        Assert.InRange(r.NernstVoltage_V, 1.15, 1.25);
    }

    [Fact]
    public void BallardHd7_AtDesignPoint_AllLossTermsPositive()
    {
        // At any positive operating current density i, all three loss
        // terms (η_act, η_ohm, η_conc) must be positive (fuel cell
        // delivers power → losses subtract from Nernst).
        var r = PemFuelCellSolver.Solve(BallardHd7Class());
        Assert.True(r.ActivationLoss_V    > 0);
        Assert.True(r.OhmicLoss_V         > 0);
        Assert.True(r.ConcentrationLoss_V > 0);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    // Ballard FCveloCity HD7 — heavy-duty bus / truck PEM fuel cell.
    // Topology inferred from datasheet (400-580 V × 85 kW):
    //   N_cells = 600 → V_stack ≈ 441 V at continuous duty cell voltage
    //   A = 280 cm² → I_stack ≈ 196 A at i = 0.7 A/cm²
    //   T = 70 °C, P = 2.0 bar — middle of the published operating
    //     envelope (60-80 °C, 1.5-2.5 bar absolute reactant inlet)
    private static PemFuelCellDesign BallardHd7Class() => new(
        Kind:                          PowerGenKind.PemFuelCell,
        CellCount:                     600,
        ActiveAreaPerCell_cm2:         280.0,
        OperatingCurrentDensity_A_cm2: 0.7,
        OperatingTemperature_C:        70.0,
        OperatingPressure_bar:         2.0);
}
