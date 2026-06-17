// AemElectrolyserSolverTests.cs — Sprint EL.W2 unit tests for the
// closed-form AEM electrolyser stack performance snapshot.

using System;
using Voxelforge.Electrolyser;
using Xunit;

namespace Voxelforge.Tests.Electrolyser;

public sealed class AemElectrolyserSolverTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneKind()
    {
        var d = EnapterEl21Class() with { Kind = ElectrolyserKind.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsPemKind()
    {
        // AEM design must NOT accept PEM kind — parallel-class
        // pattern, each kind owns its design record.
        var d = EnapterEl21Class() with { Kind = ElectrolyserKind.Pem };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositiveCurrentDensity()
    {
        var d = EnapterEl21Class() with { OperatingCurrentDensity_A_cm2 = -0.1 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositivePressure()
    {
        var d = EnapterEl21Class() with { OperatingPressure_bar = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── Enapter EL-2.1-class baseline ────────────────────────────────────

    [Fact]
    public void EnapterEl21Class_AtDesignPoint_CellVoltageInClusterBand()
    {
        // i = 0.6 A/cm², T = 60 °C, P = 35 bar → V_cell ≈ 1.84 V.
        // Cluster band [1.70, 2.00] V for commercial AEM stacks
        // at 0.5-1.0 A/cm² (Vincent & Bessarabov 2018,
        // Henkensmeier et al. 2021).
        var r = AemElectrolyserSolver.Solve(EnapterEl21Class());
        Assert.InRange(r.CellVoltage_V, 1.70, 2.00);
    }

    [Fact]
    public void EnapterEl21Class_AtDesignPoint_CellVoltageAboveNernst()
    {
        // The defining EL property: V_cell ≥ E_Nernst.
        var r = AemElectrolyserSolver.Solve(EnapterEl21Class());
        Assert.True(r.CellVoltage_V > r.NernstVoltage_V,
            $"V_cell ({r.CellVoltage_V:F4}) must exceed E_Nernst "
          + $"({r.NernstVoltage_V:F4}) for electrolysis to occur.");
    }

    [Fact]
    public void EnapterEl21Class_AtDesignPoint_HhvEfficiencyInStackOnlyClusterBand()
    {
        // η_HHV = 1.481 / V_cell. At V_cell ≈ 1.84 V → η ≈ 0.80
        // stack-only. Commercial AEM SYSTEMS report ~ 70 % HHV
        // because BOP (pumps, electronics, water purification)
        // eats ~ 10-15 %. The solver gives stack-only; system
        // efficiency is a higher-level concern.
        var r = AemElectrolyserSolver.Solve(EnapterEl21Class());
        Assert.InRange(r.HhvEfficiency, 0.70, 0.85);
    }

    [Fact]
    public void EnapterEl21Class_AtDesignPoint_HydrogenProductionMatchesDatasheet()
    {
        // Enapter EL-2.1 rated production: 0.5 Nm³/h H₂ at 35 bar
        // and 2.4 kW input (system). Stack-only design point lands
        // ~ 0.5 Nm³/h at the modelled 40 cells × 50 cm² × 0.6 A/cm²
        // configuration. Cluster band [0.4, 0.7] Nm³/h.
        var r = AemElectrolyserSolver.Solve(EnapterEl21Class());
        Assert.InRange(r.HydrogenProductionRate_Nm3_h, 0.4, 0.7);
    }

    [Fact]
    public void EnapterEl21Class_LossBreakdownReconstructsCellVoltage()
    {
        // V_cell = E_Nernst + η_act + η_ohm.
        var r = AemElectrolyserSolver.Solve(EnapterEl21Class());
        double reconstructed = r.NernstVoltage_V + r.ActivationLoss_V + r.OhmicLoss_V;
        Assert.Equal(r.CellVoltage_V, reconstructed, precision: 9);
    }

    // ── Loss + scaling sanity (mirrors PEM tests) ────────────────────────

    [Fact]
    public void OhmicLoss_LinearInCurrentDensity()
    {
        var lo = AemElectrolyserSolver.Solve(EnapterEl21Class()
            with { OperatingCurrentDensity_A_cm2 = 0.30 });
        var hi = AemElectrolyserSolver.Solve(EnapterEl21Class()
            with { OperatingCurrentDensity_A_cm2 = 0.60 });
        Assert.Equal(2.0, hi.OhmicLoss_V / lo.OhmicLoss_V, precision: 6);
    }

    [Fact]
    public void ActivationLoss_TafelLogScalingInCurrentDensity()
    {
        // Δη_act = (b/ln10) · ln(i₂/i₁).
        var lo = AemElectrolyserSolver.Solve(EnapterEl21Class()
            with { OperatingCurrentDensity_A_cm2 = 0.30 });
        var hi = AemElectrolyserSolver.Solve(EnapterEl21Class()
            with { OperatingCurrentDensity_A_cm2 = 0.60 });
        double expected = (AemElectrolyserSolver.TafelSlope_V_dec / Math.Log(10.0))
                        * Math.Log(2.0);
        Assert.Equal(expected, hi.ActivationLoss_V - lo.ActivationLoss_V, precision: 6);
    }

    [Fact]
    public void HhvEfficiency_DecreasesAsCellVoltageRises()
    {
        var lo_i = AemElectrolyserSolver.Solve(EnapterEl21Class()
            with { OperatingCurrentDensity_A_cm2 = 0.30 });
        var hi_i = AemElectrolyserSolver.Solve(EnapterEl21Class()
            with { OperatingCurrentDensity_A_cm2 = 1.00 });
        Assert.True(hi_i.HhvEfficiency < lo_i.HhvEfficiency,
            $"η_HHV at high i ({hi_i.HhvEfficiency:F4}) expected < "
          + $"η_HHV at low i ({lo_i.HhvEfficiency:F4}).");
    }

    [Fact]
    public void HydrogenProduction_LinearInStackCurrent()
    {
        var n20 = AemElectrolyserSolver.Solve(EnapterEl21Class() with { CellCount = 20 });
        var n40 = AemElectrolyserSolver.Solve(EnapterEl21Class() with { CellCount = 40 });
        Assert.Equal(2.0,
            n40.HydrogenProductionRate_kgs / n20.HydrogenProductionRate_kgs,
            precision: 9);
    }

    [Fact]
    public void NernstVoltage_IncreasesWithPressure()
    {
        var lowP  = AemElectrolyserSolver.Solve(EnapterEl21Class()
            with { OperatingPressure_bar =  1.0 });
        var highP = AemElectrolyserSolver.Solve(EnapterEl21Class()
            with { OperatingPressure_bar = 35.0 });
        Assert.True(highP.NernstVoltage_V > lowP.NernstVoltage_V);
    }

    [Fact]
    public void NernstVoltage_DecreasesWithTemperature()
    {
        var coolStack = AemElectrolyserSolver.Solve(EnapterEl21Class()
            with { OperatingTemperature_C = 30.0 });
        var hotStack  = AemElectrolyserSolver.Solve(EnapterEl21Class()
            with { OperatingTemperature_C = 70.0 });
        Assert.True(hotStack.NernstVoltage_V < coolStack.NernstVoltage_V);
    }

    [Fact]
    public void StackInputPower_IsPositive()
    {
        var r = AemElectrolyserSolver.Solve(EnapterEl21Class());
        Assert.True(r.StackElectricPower_W > 0,
            $"AEM EL input power must be positive; got {r.StackElectricPower_W:F0} W.");
    }

    // ── AEM vs PEM differentiator: membrane resistance ───────────────────

    [Fact]
    public void AemAreaSpecificResistance_HigherThanPem()
    {
        // The fundamental AEM physics anchor: anion conduction in
        // Sustainion / Aemion / Piperion is ~ 50 % as fast as proton
        // conduction in Nafion, so R_AS is ~ 2× higher.
        Assert.True(
            AemElectrolyserSolver.AreaSpecificResistance_OhmCm2
          > PemElectrolyserSolver.AreaSpecificResistance_OhmCm2,
            "AEM R_AS must exceed PEM R_AS (anion conduction slower than proton).");
    }

    [Fact]
    public void OhmicLoss_HigherThanPemAtEqualCurrentDensity()
    {
        // At identical i, AEM ohmic loss = i · 0.30 vs PEM i · 0.15.
        // Ratio is exactly R_AS_aem / R_AS_pem.
        double iCommon = 0.6;
        var aem = AemElectrolyserSolver.Solve(new AemElectrolyserDesign(
            Kind:                          ElectrolyserKind.Aem,
            CellCount:                     1,
            ActiveAreaPerCell_cm2:         100.0,
            OperatingCurrentDensity_A_cm2: iCommon,
            OperatingTemperature_C:        60.0,
            OperatingPressure_bar:         10.0));
        var pem = PemElectrolyserSolver.Solve(new PemElectrolyserDesign(
            Kind:                          ElectrolyserKind.Pem,
            CellCount:                     1,
            ActiveAreaPerCell_cm2:         100.0,
            OperatingCurrentDensity_A_cm2: iCommon,
            OperatingTemperature_C:        60.0,
            OperatingPressure_bar:         10.0));
        Assert.True(aem.OhmicLoss_V > pem.OhmicLoss_V,
            $"AEM ohmic loss {aem.OhmicLoss_V:F4} V should exceed PEM "
          + $"{pem.OhmicLoss_V:F4} V at i = {iCommon} A/cm².");
        double expectedRatio =
            AemElectrolyserSolver.AreaSpecificResistance_OhmCm2
          / PemElectrolyserSolver.AreaSpecificResistance_OhmCm2;
        Assert.Equal(expectedRatio, aem.OhmicLoss_V / pem.OhmicLoss_V, precision: 9);
    }

    [Fact]
    public void NernstVoltage_MatchesPemAtIdenticalThermoState()
    {
        // E_Nernst is a thermodynamic property of water-splitting, not
        // the membrane — AEM and PEM must produce identical Nernst at
        // identical T + P.
        var aem = AemElectrolyserSolver.Solve(new AemElectrolyserDesign(
            Kind:                          ElectrolyserKind.Aem,
            CellCount:                     1,
            ActiveAreaPerCell_cm2:         100.0,
            OperatingCurrentDensity_A_cm2: 0.5,
            OperatingTemperature_C:        70.0,
            OperatingPressure_bar:         10.0));
        var pem = PemElectrolyserSolver.Solve(new PemElectrolyserDesign(
            Kind:                          ElectrolyserKind.Pem,
            CellCount:                     1,
            ActiveAreaPerCell_cm2:         100.0,
            OperatingCurrentDensity_A_cm2: 0.5,
            OperatingTemperature_C:        70.0,
            OperatingPressure_bar:         10.0));
        Assert.Equal(pem.NernstVoltage_V, aem.NernstVoltage_V, precision: 9);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Enapter EL-2.1-class single-stack baseline. Anchors to the
    // commercial 2.4-kW, 0.5-Nm³/h product at 35 bar. Stack
    // configuration (N, A) inferred from rated production via
    // Faraday's law; commercial datasheet does not publish stack
    // internals.
    private static AemElectrolyserDesign EnapterEl21Class() => new(
        Kind:                          ElectrolyserKind.Aem,
        CellCount:                     40,
        ActiveAreaPerCell_cm2:         50.0,
        OperatingCurrentDensity_A_cm2: 0.6,
        OperatingTemperature_C:        60.0,
        OperatingPressure_bar:         35.0);
}
