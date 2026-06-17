// PemElectrolyserSolverTests.cs — Sprint EL.W1 unit tests for the
// closed-form PEM electrolyser stack performance snapshot.

using System;
using Voxelforge.Electrolyser;
using Xunit;

namespace Voxelforge.Tests.Electrolyser;

public sealed class PemElectrolyserSolverTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneKind()
    {
        var d = NelA485Class() with { Kind = ElectrolyserKind.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositiveCurrentDensity()
    {
        var d = NelA485Class() with { OperatingCurrentDensity_A_cm2 = -0.1 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositivePressure()
    {
        var d = NelA485Class() with { OperatingPressure_bar = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── Nel A485-class baseline ──────────────────────────────────────────

    [Fact]
    public void NelA485Class_AtDesignPoint_CellVoltageInClusterBand()
    {
        // i = 1.5 A/cm², T = 70 °C, P = 10 bar → V_cell ≈ 1.88 V.
        // Cluster band [1.70, 2.10] V for commercial PEM EL stacks at 1-2 A/cm².
        var r = PemElectrolyserSolver.Solve(NelA485Class());
        Assert.InRange(r.CellVoltage_V, 1.70, 2.10);
    }

    [Fact]
    public void NelA485Class_AtDesignPoint_CellVoltageAboveNernst()
    {
        // The defining EL property: V_cell ≥ E_Nernst (otherwise no
        // water-splitting occurs).
        var r = PemElectrolyserSolver.Solve(NelA485Class());
        Assert.True(r.CellVoltage_V > r.NernstVoltage_V,
            $"V_cell ({r.CellVoltage_V:F4}) must exceed E_Nernst "
          + $"({r.NernstVoltage_V:F4}) for electrolysis to occur.");
    }

    [Fact]
    public void NelA485Class_AtDesignPoint_HhvEfficiencyInClusterBand()
    {
        // η_HHV = 1.481 / V_cell. At V_cell ≈ 1.88 V → η ≈ 0.79.
        // Commercial cluster 65-85 % HHV.
        var r = PemElectrolyserSolver.Solve(NelA485Class());
        Assert.InRange(r.HhvEfficiency, 0.65, 0.85);
    }

    [Fact]
    public void NelA485Class_AtDesignPoint_HydrogenProductionInClusterBand()
    {
        // Single 100-cell stack at 300 A → ~ 1.13 kg/h H₂ ≈ 12.5 Nm³/h.
        // Cluster band [9, 16] Nm³/h per stack.
        var r = PemElectrolyserSolver.Solve(NelA485Class());
        Assert.InRange(r.HydrogenProductionRate_Nm3_h, 9.0, 16.0);
    }

    [Fact]
    public void NelA485Class_LossBreakdownReconstructsCellVoltage()
    {
        // V_cell = E_Nernst + η_act + η_ohm.
        var r = PemElectrolyserSolver.Solve(NelA485Class());
        double reconstructed = r.NernstVoltage_V + r.ActivationLoss_V + r.OhmicLoss_V;
        Assert.Equal(r.CellVoltage_V, reconstructed, precision: 9);
    }

    // ── Loss + scaling sanity ────────────────────────────────────────────

    [Fact]
    public void OhmicLoss_LinearInCurrentDensity()
    {
        var lo = PemElectrolyserSolver.Solve(NelA485Class()
            with { OperatingCurrentDensity_A_cm2 = 0.75 });
        var hi = PemElectrolyserSolver.Solve(NelA485Class()
            with { OperatingCurrentDensity_A_cm2 = 1.5  });
        Assert.Equal(2.0, hi.OhmicLoss_V / lo.OhmicLoss_V, precision: 6);
    }

    [Fact]
    public void ActivationLoss_TafelLogScalingInCurrentDensity()
    {
        // Δη_act = (b/ln10) · ln(i₂/i₁).
        var lo = PemElectrolyserSolver.Solve(NelA485Class()
            with { OperatingCurrentDensity_A_cm2 = 0.75 });
        var hi = PemElectrolyserSolver.Solve(NelA485Class()
            with { OperatingCurrentDensity_A_cm2 = 1.5  });
        double expected = (PemElectrolyserSolver.TafelSlope_V_dec / Math.Log(10.0))
                        * Math.Log(2.0);
        Assert.Equal(expected, hi.ActivationLoss_V - lo.ActivationLoss_V, precision: 6);
    }

    [Fact]
    public void HhvEfficiency_DecreasesAsCellVoltageRises()
    {
        // η_HHV = 1.481 / V_cell → higher i → higher V_cell → lower η.
        var lo_i = PemElectrolyserSolver.Solve(NelA485Class()
            with { OperatingCurrentDensity_A_cm2 = 0.75 });
        var hi_i = PemElectrolyserSolver.Solve(NelA485Class()
            with { OperatingCurrentDensity_A_cm2 = 2.0  });
        Assert.True(hi_i.HhvEfficiency < lo_i.HhvEfficiency,
            $"η_HHV at high i ({hi_i.HhvEfficiency:F4}) expected < "
          + $"η_HHV at low i ({lo_i.HhvEfficiency:F4}).");
    }

    [Fact]
    public void HydrogenProduction_LinearInStackCurrent()
    {
        // ṁ_H2 = N · I · M_H2 / (2F). Doubling N doubles ṁ.
        var n100 = PemElectrolyserSolver.Solve(NelA485Class() with { CellCount = 100 });
        var n200 = PemElectrolyserSolver.Solve(NelA485Class() with { CellCount = 200 });
        Assert.Equal(2.0,
            n200.HydrogenProductionRate_kgs / n100.HydrogenProductionRate_kgs,
            precision: 9);
    }

    [Fact]
    public void HydrogenProduction_LinearInActiveArea()
    {
        // I_stack scales with A → ṁ_H2 scales with A.
        var a100 = PemElectrolyserSolver.Solve(NelA485Class()
            with { ActiveAreaPerCell_cm2 = 100.0 });
        var a200 = PemElectrolyserSolver.Solve(NelA485Class()
            with { ActiveAreaPerCell_cm2 = 200.0 });
        Assert.Equal(2.0,
            a200.HydrogenProductionRate_kgs / a100.HydrogenProductionRate_kgs,
            precision: 9);
    }

    [Fact]
    public void NernstVoltage_IncreasesWithPressure()
    {
        // ln(P/P_ref) term is positive — higher P_back means harder to
        // split water (Le Chatelier).
        var lowP  = PemElectrolyserSolver.Solve(NelA485Class()
            with { OperatingPressure_bar =  1.0 });
        var highP = PemElectrolyserSolver.Solve(NelA485Class()
            with { OperatingPressure_bar = 30.0 });
        Assert.True(highP.NernstVoltage_V > lowP.NernstVoltage_V);
    }

    [Fact]
    public void NernstVoltage_DecreasesWithTemperature()
    {
        // dE/dT < 0 — higher T thermodynamically favours splitting.
        var coolStack = PemElectrolyserSolver.Solve(NelA485Class()
            with { OperatingTemperature_C = 30.0 });
        var hotStack  = PemElectrolyserSolver.Solve(NelA485Class()
            with { OperatingTemperature_C = 80.0 });
        Assert.True(hotStack.NernstVoltage_V < coolStack.NernstVoltage_V);
    }

    // ── PEM EL vs PEM FC sign / symmetry invariants ─────────────────────

    [Fact]
    public void StackInputPower_IsPositive()
    {
        // EL is a power CONSUMER (V_stack · I_stack > 0).
        var r = PemElectrolyserSolver.Solve(NelA485Class());
        Assert.True(r.StackElectricPower_W > 0,
            $"PEM EL input power must be positive; got {r.StackElectricPower_W:F0} W.");
    }

    [Fact]
    public void CellVoltage_AlwaysAboveHhvAtPracticalCurrents()
    {
        // At i ≥ 1 A/cm², V_cell typically > 1.481 (HHV-neutral) →
        // η_HHV < 1.0. The "thermo-neutral voltage" 1.481 V is the
        // crossover where ohmic dissipation exactly matches the
        // electrolysis enthalpy demand.
        var r = PemElectrolyserSolver.Solve(NelA485Class());
        Assert.True(r.CellVoltage_V > 1.481);
        Assert.True(r.HhvEfficiency < 1.0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Nel A485-class single-stack baseline (~ 56 kW / ~ 12.5 Nm³/h /
    // ~ 1.88 V/cell). Real Nel A485 is multi-stack; this is one stack.
    private static PemElectrolyserDesign NelA485Class() => new(
        Kind:                          ElectrolyserKind.Pem,
        CellCount:                     100,
        ActiveAreaPerCell_cm2:         200.0,
        OperatingCurrentDensity_A_cm2: 1.5,
        OperatingTemperature_C:        70.0,
        OperatingPressure_bar:         10.0);
}
