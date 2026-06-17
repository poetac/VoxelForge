// AlkalineElectrolyserSolverTests.cs — Sprint B.2-Alk unit tests for
// the closed-form alkaline electrolyser stack performance snapshot.

using System;
using Voxelforge.Electrolyser;
using Xunit;

namespace Voxelforge.Tests.Electrolyser;

public sealed class AlkalineElectrolyserSolverTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneKind()
    {
        var d = NelA485Class() with { Kind = ElectrolyserKind.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsPemKind()
    {
        // Alkaline design must NOT accept PEM kind — parallel-class
        // pattern, each kind owns its design record.
        var d = NelA485Class() with { Kind = ElectrolyserKind.Pem };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsAemKind()
    {
        var d = NelA485Class() with { Kind = ElectrolyserKind.Aem };
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
        // i = 0.25 A/cm², T = 80 °C, P = 1 bar → V_cell ≈ 1.82 V.
        // Cluster band [1.70, 2.00] V for commercial alkaline stacks
        // at 0.2-0.4 A/cm² (Vincent & Bessarabov 2018, Schalenbach
        // et al. 2016, LeRoy 1983).
        var r = AlkalineElectrolyserSolver.Solve(NelA485Class());
        Assert.InRange(r.CellVoltage_V, 1.70, 2.00);
    }

    [Fact]
    public void NelA485Class_AtDesignPoint_CellVoltageAboveNernst()
    {
        // The defining EL property: V_cell ≥ E_Nernst.
        var r = AlkalineElectrolyserSolver.Solve(NelA485Class());
        Assert.True(r.CellVoltage_V > r.NernstVoltage_V,
            $"V_cell ({r.CellVoltage_V:F4}) must exceed E_Nernst "
          + $"({r.NernstVoltage_V:F4}) for electrolysis to occur.");
    }

    [Fact]
    public void NelA485Class_AtDesignPoint_HhvEfficiencyInStackOnlyClusterBand()
    {
        // η_HHV = 1.481 / V_cell. At V_cell ≈ 1.82 V → η ≈ 0.81
        // stack-only. Commercial alkaline SYSTEMS report ~ 65-75 %
        // because BOP (electrolyte circulation, gas/liquid separators,
        // KOH cooling, water purification) eats ~ 10-15 %. The solver
        // gives stack-only; system efficiency is a higher-level
        // concern.
        var r = AlkalineElectrolyserSolver.Solve(NelA485Class());
        Assert.InRange(r.HhvEfficiency, 0.70, 0.85);
    }

    [Fact]
    public void NelA485Class_AtDesignPoint_HydrogenProductionPositive()
    {
        // Sanity floor: at any in-band operating point Faraday's law
        // must produce > 0 H₂.
        var r = AlkalineElectrolyserSolver.Solve(NelA485Class());
        Assert.True(r.HydrogenProductionRate_kgs > 0,
            $"H₂ production must be positive; got {r.HydrogenProductionRate_kgs:E3} kg/s.");
        Assert.True(r.HydrogenProductionRate_Nm3_h > 0,
            $"H₂ production must be positive; got {r.HydrogenProductionRate_Nm3_h:F2} Nm³/h.");
    }

    [Fact]
    public void NelA485Class_LossBreakdownReconstructsCellVoltage()
    {
        // V_cell = E_Nernst + η_act + η_ohm.
        var r = AlkalineElectrolyserSolver.Solve(NelA485Class());
        double reconstructed = r.NernstVoltage_V + r.ActivationLoss_V + r.OhmicLoss_V;
        Assert.Equal(r.CellVoltage_V, reconstructed, precision: 9);
    }

    // ── Loss + scaling sanity (mirrors PEM / AEM tests) ──────────────────

    [Fact]
    public void OhmicLoss_LinearInCurrentDensity()
    {
        var lo = AlkalineElectrolyserSolver.Solve(NelA485Class()
            with { OperatingCurrentDensity_A_cm2 = 0.20 });
        var hi = AlkalineElectrolyserSolver.Solve(NelA485Class()
            with { OperatingCurrentDensity_A_cm2 = 0.40 });
        Assert.Equal(2.0, hi.OhmicLoss_V / lo.OhmicLoss_V, precision: 6);
    }

    [Fact]
    public void ActivationLoss_TafelLogScalingInCurrentDensity()
    {
        // Δη_act = (b/ln10) · ln(i₂/i₁).
        var lo = AlkalineElectrolyserSolver.Solve(NelA485Class()
            with { OperatingCurrentDensity_A_cm2 = 0.20 });
        var hi = AlkalineElectrolyserSolver.Solve(NelA485Class()
            with { OperatingCurrentDensity_A_cm2 = 0.40 });
        double expected = (AlkalineElectrolyserSolver.TafelSlope_V_dec / Math.Log(10.0))
                        * Math.Log(2.0);
        Assert.Equal(expected, hi.ActivationLoss_V - lo.ActivationLoss_V, precision: 6);
    }

    [Fact]
    public void HhvEfficiency_DecreasesAsCellVoltageRises()
    {
        var lo_i = AlkalineElectrolyserSolver.Solve(NelA485Class()
            with { OperatingCurrentDensity_A_cm2 = 0.20 });
        var hi_i = AlkalineElectrolyserSolver.Solve(NelA485Class()
            with { OperatingCurrentDensity_A_cm2 = 0.50 });
        Assert.True(hi_i.HhvEfficiency < lo_i.HhvEfficiency,
            $"η_HHV at high i ({hi_i.HhvEfficiency:F4}) expected < "
          + $"η_HHV at low i ({lo_i.HhvEfficiency:F4}).");
    }

    [Fact]
    public void HydrogenProduction_LinearInStackCurrent()
    {
        var n100 = AlkalineElectrolyserSolver.Solve(NelA485Class() with { CellCount = 100 });
        var n200 = AlkalineElectrolyserSolver.Solve(NelA485Class() with { CellCount = 200 });
        Assert.Equal(2.0,
            n200.HydrogenProductionRate_kgs / n100.HydrogenProductionRate_kgs,
            precision: 9);
    }

    [Fact]
    public void NernstVoltage_IncreasesWithPressure()
    {
        var lowP  = AlkalineElectrolyserSolver.Solve(NelA485Class()
            with { OperatingPressure_bar =  1.0 });
        var highP = AlkalineElectrolyserSolver.Solve(NelA485Class()
            with { OperatingPressure_bar = 30.0 });
        Assert.True(highP.NernstVoltage_V > lowP.NernstVoltage_V);
    }

    [Fact]
    public void NernstVoltage_DecreasesWithTemperature()
    {
        var coolStack = AlkalineElectrolyserSolver.Solve(NelA485Class()
            with { OperatingTemperature_C = 30.0 });
        var hotStack  = AlkalineElectrolyserSolver.Solve(NelA485Class()
            with { OperatingTemperature_C = 80.0 });
        Assert.True(hotStack.NernstVoltage_V < coolStack.NernstVoltage_V);
    }

    [Fact]
    public void StackInputPower_IsPositive()
    {
        var r = AlkalineElectrolyserSolver.Solve(NelA485Class());
        Assert.True(r.StackElectricPower_W > 0,
            $"Alkaline EL input power must be positive; got {r.StackElectricPower_W:F0} W.");
    }

    // ── Alkaline vs PEM / AEM differentiators ────────────────────────────

    [Fact]
    public void TafelSlope_HigherThanPemAndAem()
    {
        // The fundamental alkaline physics anchor: Ni-OER cluster
        // Tafel slope is ~ 90 mV/dec at cell level, vs ~ 60 mV/dec for
        // IrO₂ (PEM) and NiFe-LDH (AEM). Pin the ordering.
        Assert.True(
            AlkalineElectrolyserSolver.TafelSlope_V_dec
          > PemElectrolyserSolver.TafelSlope_V_dec,
            "Alkaline Tafel slope must exceed PEM (Ni-OER vs IrO₂).");
        Assert.True(
            AlkalineElectrolyserSolver.TafelSlope_V_dec
          > AemElectrolyserSolver.TafelSlope_V_dec,
            "Alkaline Tafel slope must exceed AEM (Ni-OER vs NiFe-LDH).");
    }

    [Fact]
    public void AreaSpecificResistance_BetweenPemAndAem()
    {
        // R_AS ordering: PEM Nafion (0.15) < Alkaline Zirfon (0.25) <
        // AEM Sustainion (0.30). Pin the inequality chain.
        Assert.True(
            AlkalineElectrolyserSolver.AreaSpecificResistance_OhmCm2
          > PemElectrolyserSolver.AreaSpecificResistance_OhmCm2,
            "Alkaline R_AS must exceed PEM (Zirfon + KOH vs Nafion).");
        Assert.True(
            AlkalineElectrolyserSolver.AreaSpecificResistance_OhmCm2
          < AemElectrolyserSolver.AreaSpecificResistance_OhmCm2,
            "Alkaline R_AS must be below AEM (Zirfon + KOH vs Sustainion).");
    }

    [Fact]
    public void ActivationLoss_HigherThanPemAtEqualCurrentDensity()
    {
        // At identical i, alkaline η_act > PEM η_act because of the
        // larger Tafel slope (90 vs 60 mV/dec). Ratio is exactly
        // b_alkaline / b_pem = 90/60 = 1.5 at the same (i/i_0).
        double iCommon = 0.4;
        var alk = AlkalineElectrolyserSolver.Solve(new AlkalineElectrolyserDesign(
            Kind:                          ElectrolyserKind.Alkaline,
            CellCount:                     1,
            ActiveAreaPerCell_cm2:         100.0,
            OperatingCurrentDensity_A_cm2: iCommon,
            OperatingTemperature_C:        80.0,
            OperatingPressure_bar:         1.0));
        var pem = PemElectrolyserSolver.Solve(new PemElectrolyserDesign(
            Kind:                          ElectrolyserKind.Pem,
            CellCount:                     1,
            ActiveAreaPerCell_cm2:         100.0,
            OperatingCurrentDensity_A_cm2: iCommon,
            OperatingTemperature_C:        80.0,
            OperatingPressure_bar:         1.0));
        Assert.True(alk.ActivationLoss_V > pem.ActivationLoss_V,
            $"Alkaline η_act {alk.ActivationLoss_V:F4} V should exceed PEM "
          + $"{pem.ActivationLoss_V:F4} V at i = {iCommon} A/cm² (higher Tafel slope).");
        double expectedRatio =
            AlkalineElectrolyserSolver.TafelSlope_V_dec
          / PemElectrolyserSolver.TafelSlope_V_dec;
        Assert.Equal(expectedRatio, alk.ActivationLoss_V / pem.ActivationLoss_V, precision: 9);
    }

    [Fact]
    public void NernstVoltage_MatchesPemAndAem_AtIdenticalThermoState()
    {
        // E_Nernst is a thermodynamic property of water-splitting, not
        // the catalyst or electrolyte — all three pillars must produce
        // identical Nernst at identical T + P.
        const double iCommon = 0.5;
        const double tCommon = 70.0;
        const double pCommon = 10.0;
        var alk = AlkalineElectrolyserSolver.Solve(new AlkalineElectrolyserDesign(
            Kind:                          ElectrolyserKind.Alkaline,
            CellCount:                     1,
            ActiveAreaPerCell_cm2:         100.0,
            OperatingCurrentDensity_A_cm2: iCommon,
            OperatingTemperature_C:        tCommon,
            OperatingPressure_bar:         pCommon));
        var pem = PemElectrolyserSolver.Solve(new PemElectrolyserDesign(
            Kind:                          ElectrolyserKind.Pem,
            CellCount:                     1,
            ActiveAreaPerCell_cm2:         100.0,
            OperatingCurrentDensity_A_cm2: iCommon,
            OperatingTemperature_C:        tCommon,
            OperatingPressure_bar:         pCommon));
        var aem = AemElectrolyserSolver.Solve(new AemElectrolyserDesign(
            Kind:                          ElectrolyserKind.Aem,
            CellCount:                     1,
            ActiveAreaPerCell_cm2:         100.0,
            OperatingCurrentDensity_A_cm2: iCommon,
            OperatingTemperature_C:        tCommon,
            OperatingPressure_bar:         pCommon));
        Assert.Equal(pem.NernstVoltage_V, alk.NernstVoltage_V, precision: 9);
        Assert.Equal(aem.NernstVoltage_V, alk.NernstVoltage_V, precision: 9);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Nel A485-class scaled-down single-stack baseline. Anchors to the
    // commercial atmospheric alkaline class (Nel A485, Thyssenkrupp,
    // Asahi-Kasei). Stack-internal configuration is not published in
    // datasheets; the (N, A, i) here are chosen so V_cell ≈ 1.82 V
    // lands in the cluster band [1.70, 2.00] at the documented 80 °C
    // / 1 bar / 0.25 A/cm² operating point, and stack production
    // scales correctly per Faraday's law.
    private static AlkalineElectrolyserDesign NelA485Class() => new(
        Kind:                          ElectrolyserKind.Alkaline,
        CellCount:                     200,
        ActiveAreaPerCell_cm2:         500.0,
        OperatingCurrentDensity_A_cm2: 0.25,
        OperatingTemperature_C:        80.0,
        OperatingPressure_bar:         1.0);
}
