// PemPolarisationCurveTests.cs — Sprint PG.W2 unit tests for the
// SolveAtCurrentDensity + SolvePolarisationCurve API extensions on
// the PEM fuel cell solver.

using System;
using System.Linq;
using Voxelforge.PowerGen;
using Xunit;

namespace Voxelforge.Tests.PowerGen;

public sealed class PemPolarisationCurveTests
{
    // ── SolveAtCurrentDensity — pure unit tests ─────────────────────────

    [Fact]
    public void SolveAtCurrentDensity_AtDesignI_MatchesSolve()
    {
        // Calling SolveAtCurrentDensity at the design's operating current
        // density must produce the same snapshot as Solve(design).
        var d = Mirai();
        var bySolve = PemFuelCellSolver.Solve(d);
        var byAtI   = PemFuelCellSolver.SolveAtCurrentDensity(d, d.OperatingCurrentDensity_A_cm2);
        Assert.Equal(bySolve.CellVoltage_V,       byAtI.CellVoltage_V,       precision: 9);
        Assert.Equal(bySolve.StackElectricPower_W, byAtI.StackElectricPower_W, precision: 4);
    }

    [Fact]
    public void SolveAtCurrentDensity_AtZeroCurrent_VoltageEqualsNernst()
    {
        // Open-circuit: i = 0 ⇒ no activation / ohmic / concentration
        // losses ⇒ V_cell = E_Nernst exactly.
        var r = PemFuelCellSolver.SolveAtCurrentDensity(Mirai(), 0.0);
        Assert.Equal(r.NernstVoltage_V, r.CellVoltage_V, precision: 9);
        Assert.Equal(0.0, r.ActivationLoss_V,    precision: 9);
        Assert.Equal(0.0, r.OhmicLoss_V,         precision: 9);
        Assert.Equal(0.0, r.ConcentrationLoss_V, precision: 9);
        // P = V · 0 · A = 0 at open-circuit.
        Assert.Equal(0.0, r.StackElectricPower_W, precision: 9);
    }

    [Fact]
    public void SolveAtCurrentDensity_RejectsNegativeI()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PemFuelCellSolver.SolveAtCurrentDensity(Mirai(), -0.1));
    }

    [Fact]
    public void SolveAtCurrentDensity_AboveLimit_CellVoltageNegativeInfinity()
    {
        // i ≥ i_L ⇒ η_conc = +∞ ⇒ V_cell = E_Nernst − η_act − η_ohm − ∞ = −∞.
        var r = PemFuelCellSolver.SolveAtCurrentDensity(Mirai(),
            PemFuelCellSolver.LimitingCurrentDensity_A_cm2);
        Assert.True(double.IsNegativeInfinity(r.CellVoltage_V));
    }

    // ── SolvePolarisationCurve — sweep tests ─────────────────────────────

    [Fact]
    public void Sweep_RejectsEmptyArray()
    {
        Assert.Throws<ArgumentException>(
            () => PemFuelCellSolver.SolvePolarisationCurve(Mirai(), Array.Empty<double>()));
    }

    [Fact]
    public void Sweep_RejectsUnsortedArray()
    {
        Assert.Throws<ArgumentException>(
            () => PemFuelCellSolver.SolvePolarisationCurve(Mirai(),
                new[] { 0.1, 0.5, 0.3 /* unsorted */ }));
    }

    [Fact]
    public void Sweep_RejectsNegativeSample()
    {
        Assert.Throws<ArgumentException>(
            () => PemFuelCellSolver.SolvePolarisationCurve(Mirai(),
                new[] { -0.01, 0.5, 1.0 }));
    }

    [Fact]
    public void Sweep_OutputLengthEqualsInputLength()
    {
        var samples = new[] { 0.0, 0.1, 0.5, 1.0, 1.5 };
        var curve = PemFuelCellSolver.SolvePolarisationCurve(Mirai(), samples);
        Assert.Equal(samples.Length, curve.Length);
    }

    [Fact]
    public void Sweep_CellVoltageMonotonicallyDecreasesWithI()
    {
        // The Tafel / ohmic / concentration losses are all monotonically
        // non-decreasing in i → V_cell is monotonically non-increasing.
        var curve = PemFuelCellSolver.SolvePolarisationCurve(Mirai(),
            new[] { 0.0, 0.05, 0.20, 0.50, 1.00, 1.50, 1.90 });
        for (int k = 1; k < curve.Length; k++)
        {
            Assert.True(curve[k].CellVoltage_V <= curve[k - 1].CellVoltage_V,
                $"V_cell at i={curve[k].CurrentDensity_A_cm2:F3} "
              + $"({curve[k].CellVoltage_V:F4}) expected ≤ "
              + $"V_cell at i={curve[k - 1].CurrentDensity_A_cm2:F3} "
              + $"({curve[k - 1].CellVoltage_V:F4}).");
        }
    }

    [Fact]
    public void Sweep_PowerDensityHasInteriorPeak()
    {
        // P_density = V_cell · i. At i = 0 → P = 0; at i → i_L →
        // V_cell → 0 (and below) ⇒ P → 0 or negative. So there must be
        // an interior maximum.
        var samples = new[] { 0.05, 0.20, 0.40, 0.60, 0.80, 1.00, 1.20, 1.40, 1.60, 1.80 };
        var curve = PemFuelCellSolver.SolvePolarisationCurve(Mirai(), samples);
        int peakIdx = 0;
        double peakP = double.NegativeInfinity;
        for (int k = 0; k < curve.Length; k++)
        {
            if (curve[k].PowerDensity_W_cm2 > peakP)
            {
                peakP = curve[k].PowerDensity_W_cm2;
                peakIdx = k;
            }
        }
        // The peak must be strictly interior to the sweep (not the first
        // or last sample) — the classical fuel-cell polarisation-curve
        // figure of merit.
        Assert.InRange(peakIdx, 1, curve.Length - 2);
        Assert.True(peakP > 0,
            $"Peak power density ({peakP:F4} W/cm²) expected > 0.");
    }

    [Fact]
    public void Sweep_PowerDensityEqualsCellVoltageTimesCurrentDensity()
    {
        var curve = PemFuelCellSolver.SolvePolarisationCurve(Mirai(),
            new[] { 0.1, 0.5, 1.0 });
        foreach (var p in curve)
            Assert.Equal(p.CellVoltage_V * p.CurrentDensity_A_cm2,
                         p.PowerDensity_W_cm2, precision: 9);
    }

    [Fact]
    public void Sweep_StackPowerEqualsStackVoltageTimesI()
    {
        // Stack power = N · V_cell · i · A_active. Sanity check on the
        // roll-up arithmetic, exercised across the curve.
        var d = Mirai();
        var curve = PemFuelCellSolver.SolvePolarisationCurve(d,
            new[] { 0.0, 0.5, 1.0 });
        foreach (var p in curve)
        {
            double expected = d.CellCount * p.CellVoltage_V
                            * p.CurrentDensity_A_cm2 * d.ActiveAreaPerCell_cm2;
            Assert.Equal(expected, p.StackElectricPower_W, precision: 4);
        }
    }

    [Fact]
    public void Sweep_OpenCircuitVoltageIsHighest()
    {
        // V_cell at i=0 (= E_Nernst) is the highest point on the curve.
        var curve = PemFuelCellSolver.SolvePolarisationCurve(Mirai(),
            new[] { 0.0, 0.1, 0.5, 1.0, 1.5 });
        double openCircuitV = curve[0].CellVoltage_V;
        Assert.True(curve.Skip(1).All(p => p.CellVoltage_V < openCircuitV),
            $"Open-circuit V ({openCircuitV:F4}) expected > all other sweep points.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static PemFuelCellDesign Mirai() => new(
        Kind:                         PowerGenKind.PemFuelCell,
        CellCount:                    330,
        ActiveAreaPerCell_cm2:        200.0,
        OperatingCurrentDensity_A_cm2: 1.0,
        OperatingTemperature_C:       80.0,
        OperatingPressure_bar:         2.5);
}
