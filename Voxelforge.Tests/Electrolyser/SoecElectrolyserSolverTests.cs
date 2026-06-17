// SoecElectrolyserSolverTests.cs — Sprint B.2-SOEC unit tests for the
// closed-form solid-oxide electrolyser stack performance snapshot.

using System;
using Voxelforge.Electrolyser;
using Xunit;

namespace Voxelforge.Tests.Electrolyser;

public sealed class SoecElectrolyserSolverTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneKind()
    {
        var d = SunfireHyLinkClass() with { Kind = ElectrolyserKind.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsPemKind()
    {
        // SOEC design must NOT accept any other kind — parallel-class
        // pattern, each kind owns its design record.
        var d = SunfireHyLinkClass() with { Kind = ElectrolyserKind.Pem };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsAemKind()
    {
        var d = SunfireHyLinkClass() with { Kind = ElectrolyserKind.Aem };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsAlkalineKind()
    {
        var d = SunfireHyLinkClass() with { Kind = ElectrolyserKind.Alkaline };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositiveCurrentDensity()
    {
        var d = SunfireHyLinkClass() with { OperatingCurrentDensity_A_cm2 = -0.1 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositivePressure()
    {
        var d = SunfireHyLinkClass() with { OperatingPressure_bar = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── Sunfire HyLink-class baseline ────────────────────────────────────

    [Fact]
    public void SunfireHyLinkClass_AtDesignPoint_CellVoltageInClusterBand()
    {
        // i = 0.5 A/cm², T = 800 °C, P = 1 bar →
        // V_cell = E_Nernst(800 °C, 1 bar) + η_act + η_ohm
        //        = 0.923 + 0 + 0.2 = 1.123 V (at i = i₀, η_act = 0).
        // Cluster band [1.05, 1.45] V for commercial SOEC stacks at
        // 0.5-1.0 A/cm² (Sunfire HyLink, Topsoe HTSE, Ceres Power class;
        // Mogensen 2008, Stempien 2013, Klotz 2017).
        var r = SoecElectrolyserSolver.Solve(SunfireHyLinkClass());
        Assert.InRange(r.CellVoltage_V, 1.05, 1.45);
    }

    [Fact]
    public void SunfireHyLinkClass_AtDesignPoint_CellVoltageAboveNernst()
    {
        // The defining EL property: V_cell ≥ E_Nernst.
        var r = SoecElectrolyserSolver.Solve(SunfireHyLinkClass());
        Assert.True(r.CellVoltage_V > r.NernstVoltage_V,
            $"V_cell ({r.CellVoltage_V:F4}) must exceed E_Nernst "
          + $"({r.NernstVoltage_V:F4}) for electrolysis to occur.");
    }

    [Fact]
    public void SunfireHyLinkClass_AtDesignPoint_CellVoltageBelowThermoNeutral()
    {
        // The defining SOEC property: V_cell < V_TN (1.481 V) at design.
        // The cell absorbs heat from the surroundings to complete the
        // endothermic reaction — the SOEC value proposition for
        // waste-heat recovery.
        var r = SoecElectrolyserSolver.Solve(SunfireHyLinkClass());
        Assert.True(r.CellVoltage_V < SoecElectrolyserSolver.HhvThermoNeutralVoltage_V,
            $"V_cell ({r.CellVoltage_V:F4}) must be below V_TN "
          + $"(1.481 V) at SOEC design point — heat absorption is the value-prop.");
    }

    [Fact]
    public void SunfireHyLinkClass_AtDesignPoint_HhvEfficiencyAboveUnity()
    {
        // η_HHV = 1.481 / V_cell. At V_cell ≈ 1.123 V → η ≈ 1.32 stack-
        // only on ELECTRIC input. The total energy balance (electric +
        // absorbed heat) never exceeds unity; this is the per-electric
        // ratio and exceeding 1.0 is the SOEC value proposition. Commercial
        // SOEC SYSTEMS report ~ 90 % HHV (with BOP losses for steam
        // production, recuperators, electronics).
        var r = SoecElectrolyserSolver.Solve(SunfireHyLinkClass());
        Assert.InRange(r.HhvEfficiency, 1.05, 1.40);
    }

    [Fact]
    public void SunfireHyLinkClass_AtDesignPoint_HydrogenProductionPositive()
    {
        // Sanity floor: at any in-band operating point Faraday's law
        // must produce > 0 H₂.
        var r = SoecElectrolyserSolver.Solve(SunfireHyLinkClass());
        Assert.True(r.HydrogenProductionRate_kgs > 0,
            $"H₂ production must be positive; got {r.HydrogenProductionRate_kgs:E3} kg/s.");
        Assert.True(r.HydrogenProductionRate_Nm3_h > 0,
            $"H₂ production must be positive; got {r.HydrogenProductionRate_Nm3_h:F2} Nm³/h.");
    }

    [Fact]
    public void SunfireHyLinkClass_LossBreakdownReconstructsCellVoltage()
    {
        // V_cell = E_Nernst + η_act + η_ohm.
        var r = SoecElectrolyserSolver.Solve(SunfireHyLinkClass());
        double reconstructed = r.NernstVoltage_V + r.ActivationLoss_V + r.OhmicLoss_V;
        Assert.Equal(r.CellVoltage_V, reconstructed, precision: 9);
    }

    // ── Loss + scaling sanity (mirrors PEM / AEM / Alkaline tests) ───────

    [Fact]
    public void OhmicLoss_LinearInCurrentDensity()
    {
        var lo = SoecElectrolyserSolver.Solve(SunfireHyLinkClass()
            with { OperatingCurrentDensity_A_cm2 = 0.50 });
        var hi = SoecElectrolyserSolver.Solve(SunfireHyLinkClass()
            with { OperatingCurrentDensity_A_cm2 = 1.00 });
        Assert.Equal(2.0, hi.OhmicLoss_V / lo.OhmicLoss_V, precision: 6);
    }

    [Fact]
    public void ActivationLoss_TafelLogScalingInCurrentDensity()
    {
        // Δη_act = (b/ln10) · ln(i₂/i₁).
        var lo = SoecElectrolyserSolver.Solve(SunfireHyLinkClass()
            with { OperatingCurrentDensity_A_cm2 = 0.50 });
        var hi = SoecElectrolyserSolver.Solve(SunfireHyLinkClass()
            with { OperatingCurrentDensity_A_cm2 = 1.00 });
        double expected = (SoecElectrolyserSolver.TafelSlope_V_dec / Math.Log(10.0))
                        * Math.Log(2.0);
        Assert.Equal(expected, hi.ActivationLoss_V - lo.ActivationLoss_V, precision: 6);
    }

    [Fact]
    public void HhvEfficiency_DecreasesAsCellVoltageRises()
    {
        var lo_i = SoecElectrolyserSolver.Solve(SunfireHyLinkClass()
            with { OperatingCurrentDensity_A_cm2 = 0.50 });
        var hi_i = SoecElectrolyserSolver.Solve(SunfireHyLinkClass()
            with { OperatingCurrentDensity_A_cm2 = 1.50 });
        Assert.True(hi_i.HhvEfficiency < lo_i.HhvEfficiency,
            $"η_HHV at high i ({hi_i.HhvEfficiency:F4}) expected < "
          + $"η_HHV at low i ({lo_i.HhvEfficiency:F4}).");
    }

    [Fact]
    public void HydrogenProduction_LinearInStackCurrent()
    {
        var n250 = SoecElectrolyserSolver.Solve(SunfireHyLinkClass() with { CellCount = 250 });
        var n500 = SoecElectrolyserSolver.Solve(SunfireHyLinkClass() with { CellCount = 500 });
        Assert.Equal(2.0,
            n500.HydrogenProductionRate_kgs / n250.HydrogenProductionRate_kgs,
            precision: 9);
    }

    [Fact]
    public void NernstVoltage_IncreasesWithPressure()
    {
        var lowP  = SoecElectrolyserSolver.Solve(SunfireHyLinkClass()
            with { OperatingPressure_bar = 1.0 });
        var highP = SoecElectrolyserSolver.Solve(SunfireHyLinkClass()
            with { OperatingPressure_bar = 5.0 });
        Assert.True(highP.NernstVoltage_V > lowP.NernstVoltage_V);
    }

    [Fact]
    public void NernstVoltage_DecreasesWithTemperature()
    {
        // SOEC slope is gentler (-0.234 mV/K) than the liquid-T kinds
        // (-0.85 mV/K) but still negative — higher T means lower
        // electrolysis voltage.
        var coolStack = SoecElectrolyserSolver.Solve(SunfireHyLinkClass()
            with { OperatingTemperature_C = 700.0 });
        var hotStack  = SoecElectrolyserSolver.Solve(SunfireHyLinkClass()
            with { OperatingTemperature_C = 850.0 });
        Assert.True(hotStack.NernstVoltage_V < coolStack.NernstVoltage_V);
    }

    [Fact]
    public void StackInputPower_IsPositive()
    {
        var r = SoecElectrolyserSolver.Solve(SunfireHyLinkClass());
        Assert.True(r.StackElectricPower_W > 0,
            $"SOEC EL input power must be positive; got {r.StackElectricPower_W:F0} W.");
    }

    // ── SOEC vs PEM/AEM/Alkaline differentiators ─────────────────────────

    [Fact]
    public void ExchangeCurrentDensity_MuchHigherThanLiquidTKinds()
    {
        // The defining SOEC kinetic anchor: at 800 °C Ni-YSZ + LSM/LSCF
        // electrode kinetics are facile, with i₀ ~ 0.5 A/cm² — three to
        // four orders of magnitude higher than PEM/AEM/Alkaline (i₀ ~
        // 1e-7 A/cm² at cell level). Pin the inequality chain.
        Assert.True(
            SoecElectrolyserSolver.ExchangeCurrentDensity_A_cm2
          > PemElectrolyserSolver.ExchangeCurrentDensity_A_cm2 * 1000.0,
            "SOEC i₀ must exceed PEM i₀ by >= 3 orders of magnitude.");
        Assert.True(
            SoecElectrolyserSolver.ExchangeCurrentDensity_A_cm2
          > AemElectrolyserSolver.ExchangeCurrentDensity_A_cm2 * 1000.0,
            "SOEC i₀ must exceed AEM i₀ by >= 3 orders of magnitude.");
        Assert.True(
            SoecElectrolyserSolver.ExchangeCurrentDensity_A_cm2
          > AlkalineElectrolyserSolver.ExchangeCurrentDensity_A_cm2 * 1000.0,
            "SOEC i₀ must exceed Alkaline i₀ by >= 3 orders of magnitude.");
    }

    [Fact]
    public void NernstReferenceTemperature_DistinctFromLiquidTKinds()
    {
        // SOEC anchors Nernst at 800 °C (steam electrolysis cluster mid-
        // band), not at 25 °C (the liquid-water reaction reference used
        // by PEM/AEM/Alkaline). This is the defining thermodynamic
        // differentiator.
        Assert.NotEqual(
            SoecElectrolyserSolver.SoecReferenceTemperature_K,
            PemElectrolyserSolver.ReferenceTemperature_K);
    }

    [Fact]
    public void NernstFormula_DivergesFromLiquidTKindsAtSoecOperatingT()
    {
        // Cross-check: at SOEC's operating temperature (800 °C), the PEM
        // linear extrapolation diverges substantially from the SOEC
        // steam-electrolysis anchor. The PEM slope -0.85 mV/K is only
        // valid near 25-80 °C — extrapolated to 800 °C it implicitly
        // keeps subtracting the liquid-water heat-capacity contribution
        // and drives Nernst far too low (~ 0.57 V vs the correct
        // steam-electrolysis value 0.92 V). The two formulae are NOT
        // interchangeable above ~ 150 °C.
        const double tCommon = 800.0;
        const double pCommon = 1.0;
        var soec = SoecElectrolyserSolver.Solve(new SoecElectrolyserDesign(
            Kind:                          ElectrolyserKind.Soec,
            CellCount:                     1,
            ActiveAreaPerCell_cm2:         100.0,
            OperatingCurrentDensity_A_cm2: 0.5,
            OperatingTemperature_C:        tCommon,
            OperatingPressure_bar:         pCommon));
        var pem = PemElectrolyserSolver.Solve(new PemElectrolyserDesign(
            Kind:                          ElectrolyserKind.Pem,
            CellCount:                     1,
            ActiveAreaPerCell_cm2:         100.0,
            OperatingCurrentDensity_A_cm2: 0.5,
            OperatingTemperature_C:        tCommon,
            OperatingPressure_bar:         pCommon));
        // SOEC anchor at 800 °C ≈ 0.92 V; PEM linear extrapolation
        // at 800 °C ≈ 0.57 V. Difference > 0.30 V — large, in the
        // direction expected (PEM linear over-corrects).
        Assert.True(soec.NernstVoltage_V - pem.NernstVoltage_V > 0.30,
            $"At {tCommon} °C, SOEC Nernst ({soec.NernstVoltage_V:F4} V) "
          + $"should exceed PEM extrapolation ({pem.NernstVoltage_V:F4} V) "
          + "by > 0.30 V — the two formulae use different temperature "
          + "anchors; the PEM linear slope is invalid at this T.");
    }

    [Fact]
    public void ActivationLoss_NearZeroAtCurrentDensityEqualToExchange()
    {
        // At i = i₀, η_act = (b/ln10) · ln(1) = 0. Sanity check the
        // Tafel-log identity at the SOEC anchor.
        var r = SoecElectrolyserSolver.Solve(SunfireHyLinkClass()
            with { OperatingCurrentDensity_A_cm2 =
                SoecElectrolyserSolver.ExchangeCurrentDensity_A_cm2 });
        Assert.Equal(0.0, r.ActivationLoss_V, precision: 12);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Sunfire HyLink-class single-stack-module baseline. Anchors to the
    // commercial 1.5-MW / 250-Nm³/h SOEC product class operated at 800 °C
    // atmospheric (Sunfire HyLink, Topsoe HTSE, Ceres Power). Stack-
    // internal configuration is not published; the (N, A, i) here are
    // chosen so V_cell ≈ 1.12 V lands in the cluster band [1.05, 1.45]
    // at the documented 800 °C / 1 bar / 0.5 A/cm² operating point, and
    // the stack-only production scales correctly per Faraday's law.
    private static SoecElectrolyserDesign SunfireHyLinkClass() => new(
        Kind:                          ElectrolyserKind.Soec,
        CellCount:                     500,
        ActiveAreaPerCell_cm2:         100.0,
        OperatingCurrentDensity_A_cm2: 0.5,
        OperatingTemperature_C:        800.0,
        OperatingPressure_bar:         1.0);
}
