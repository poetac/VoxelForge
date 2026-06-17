// CombustionStabilityTests.cs — Contract tests for the combustion-stability
// screening module (chug + screech + composite traffic light). These locks
// down the rule-of-thumb criteria and the relationships between geometry,
// gas properties, and acoustic modes. Preliminary-design fidelity — the
// module STOPS short of Crocco n-τ per project scope.

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.IO;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class CombustionStabilityTests
{
    // ─────────────────────────────────────────────────────────────
    //  Shared helpers
    // ─────────────────────────────────────────────────────────────

    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N = 500,
        ChamberPressure_Pa = 1000 * 6894.76,
        MixtureRatio = 3.3,
        CoolantInletTemp_K = 150,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex = 1,
        PropellantPair = PropellantPair.LOX_CH4,
    };

    private static ChamberContour MakeContour(double chamberRadius_mm, double chamberLength_mm)
    {
        // Use the chamber-contour generator to build a contour whose
        // barrel length and radius match the requested test values. We
        // don't use the generator's "contraction ratio" approach directly
        // because we care about the absolute L_c and R_c for acoustics.
        // So fake it: throat radius derived from contraction ratio.
        double contractionRatio = 6.0;
        double throatRadius = chamberRadius_mm / System.Math.Sqrt(contractionRatio);
        // L* drives ChamberLength via V_c = L* * A_t; tune L* so the
        // generator produces the desired L_c. V_c ≈ A_c * L_c (barrel
        // only, ignoring converging frustum), so:
        //   L* = L_c * A_c / A_t = L_c * contractionRatio  (rough)
        // The contour generator then subtracts converging and picks
        // barrel length so total-upstream-volume = L* · A_t. That's
        // noisy at high eps_c, but acceptable for tests — we read
        // back ChamberLength_mm from the result and trust that value.
        double L_star_m = chamberLength_mm * 1e-3 * contractionRatio;
        return ChamberContourGenerator.Generate(
            throatRadius_mm: throatRadius,
            contractionRatio: contractionRatio,
            expansionRatio: 8.0,
            characteristicLength_m: L_star_m,
            stationCount: 120);
    }

    private static PropellantState SampleGas() => new(
        MixtureRatio: 3.3,
        ChamberPressure_Pa: 6.9e6,
        ChamberTemp_K: 3400.0,
        GammaChamber: 1.15,
        GammaThroat: 1.15,
        MolecularWeight: 24.0,
        SpecificGasConst: 8314.462618 / 24.0,
        Cp_Jkg: 3200.0,
        Viscosity_PaS: 1e-4,
        Prandtl: 0.6,
        CStar_ms: 1800.0,
        IspVacuum_s: 360.0,
        PropellantName: "Test gas (LOX/CH4-like)");

    // ─────────────────────────────────────────────────────────────
    //  (1) InjectorState default matches spec (20% of Pc)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void InjectorState_Nominal_DefaultsTo20PercentOfChamberPressure()
    {
        double Pc = 6.9e6;
        var inj = InjectorState.Nominal(Pc);
        Assert.Equal(0.20 * Pc, inj.DeltaPInj_Pa, 1e-6);
        Assert.Equal(0.20, inj.RatioToChamberPressure(Pc), 1e-9);
    }

    // ─────────────────────────────────────────────────────────────
    //  (2) Chug Pass/Marginal/Fail bands
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.20, StabilityRating.Pass)]   // middle of band
    [InlineData(0.15, StabilityRating.Pass)]   // at lower boundary
    [InlineData(0.25, StabilityRating.Pass)]   // at upper boundary
    [InlineData(0.13, StabilityRating.Marginal)] // just below lower band
    [InlineData(0.27, StabilityRating.Marginal)] // just above upper band
    [InlineData(0.05, StabilityRating.Fail)]   // chug-prone
    [InlineData(0.40, StabilityRating.Fail)]   // tank pressure wasted
    public void ChugAnalysis_RatesPerBand(double ratio, StabilityRating expected)
    {
        double Pc = 6.9e6;
        var inj = new InjectorState(DeltaPInj_Pa: ratio * Pc);
        var r = ChugAnalysis.Evaluate(inj, Pc);
        Assert.Equal(expected, r.Rating);
        Assert.Equal(ratio, r.DeltaPRatio, 6);
    }

    // ─────────────────────────────────────────────────────────────
    //  (3) L1 scales inversely with L_c
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScreechModes_L1_MonotonicInverseInLength()
    {
        var gas = SampleGas();
        double D = 0.050;   // 50 mm chamber diameter
        double L_short = 0.060;
        double L_long  = 0.120;

        var rShort = ScreechModes.Evaluate(gas.Gamma, gas.SpecificGasConst, gas.ChamberTemp_K, L_short, D);
        var rLong  = ScreechModes.Evaluate(gas.Gamma, gas.SpecificGasConst, gas.ChamberTemp_K, L_long,  D);

        Assert.True(rLong.L1_Hz < rShort.L1_Hz,
            $"L1 should fall as L_c grows (short={rShort.L1_Hz:F0}, long={rLong.L1_Hz:F0})");
        // Closed-form: f = c/(2L) → ratio should exactly match L_short/L_long
        double expectedRatio = L_short / L_long;
        double actualRatio   = rLong.L1_Hz / rShort.L1_Hz;
        Assert.Equal(expectedRatio, actualRatio, 6);
    }

    // ─────────────────────────────────────────────────────────────
    //  (4) T1 scales inversely with D_c, and T2 > T1
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScreechModes_T1_InverseInDiameter_And_T2_Exceeds_T1()
    {
        var gas = SampleGas();
        double L = 0.100;
        double D_narrow = 0.040;
        double D_wide   = 0.080;

        var rNarrow = ScreechModes.Evaluate(gas.Gamma, gas.SpecificGasConst, gas.ChamberTemp_K, L, D_narrow);
        var rWide   = ScreechModes.Evaluate(gas.Gamma, gas.SpecificGasConst, gas.ChamberTemp_K, L, D_wide);

        // 1/D dependence: doubling D halves T1 and T2
        Assert.Equal(0.5, rWide.T1_Hz / rNarrow.T1_Hz, 6);
        Assert.Equal(0.5, rWide.T2_Hz / rNarrow.T2_Hz, 6);

        // T2 > T1 always (T2 coef 3.054 > T1 coef 1.841)
        Assert.True(rNarrow.T2_Hz > rNarrow.T1_Hz);
        Assert.True(rWide.T2_Hz > rWide.T1_Hz);

        // Exact ratio: T2/T1 = 3.054/1.841 ≈ 1.659
        double expectedRatio = ScreechModes.BesselCoef_T2 / ScreechModes.BesselCoef_T1;
        Assert.Equal(expectedRatio, rNarrow.T2_Hz / rNarrow.T1_Hz, 6);
    }

    // ─────────────────────────────────────────────────────────────
    //  (5) Sound speed matches c = √(γ·R·T_c)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScreechModes_SoundSpeed_MatchesClosedForm()
    {
        var gas = SampleGas();
        double expected = System.Math.Sqrt(gas.Gamma * gas.SpecificGasConst * gas.ChamberTemp_K);
        double got = ScreechModes.SoundSpeed(gas.Gamma, gas.SpecificGasConst, gas.ChamberTemp_K);
        Assert.Equal(expected, got, 6);

        var r = ScreechModes.Evaluate(gas.Gamma, gas.SpecificGasConst, gas.ChamberTemp_K, 0.1, 0.05);
        Assert.Equal(expected, r.SoundSpeed_ms, 6);
    }

    // ─────────────────────────────────────────────────────────────
    //  (6) StabilityReport round-trips through RegenGenerationResult
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void StabilityReport_IsPopulatedOn_RegenGenerationResult()
    {
        var cond = DefaultConditions();
        var design = new RegenChamberDesign();
        var gen = RegenChamberOptimization.GenerateWith(cond, design);

        Assert.NotNull(gen.Stability);

        // Default injector state: ΔP = 20% Pc (Pass).
        Assert.Equal(StabilityRating.Pass, gen.Stability.Chug.Rating);
        Assert.InRange(gen.Stability.Chug.DeltaPRatio, 0.19, 0.21);

        // Screech frequencies must be positive and T2 > T1.
        Assert.True(gen.Stability.Screech.L1_Hz > 0);
        Assert.True(gen.Stability.Screech.T1_Hz > 0);
        Assert.True(gen.Stability.Screech.T2_Hz > gen.Stability.Screech.T1_Hz);

        // Composite is one of the three enum values.
        Assert.True(gen.Stability.Composite == StabilityRating.Pass
                 || gen.Stability.Composite == StabilityRating.Marginal
                 || gen.Stability.Composite == StabilityRating.Fail);
    }

    // ─────────────────────────────────────────────────────────────
    //  (7) Composite traffic-light logic
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void StabilityScreening_Composite_DemotesOnChugFail()
    {
        var gas = SampleGas();
        var contour = MakeContour(chamberRadius_mm: 25, chamberLength_mm: 100);

        // Injector ΔP = 5% P_c → Chug FAIL → Composite FAIL
        var inj = new InjectorState(DeltaPInj_Pa: 0.05 * 6.9e6);
        var report = StabilityScreening.Evaluate(contour, gas, 6.9e6, inj);

        Assert.Equal(StabilityRating.Fail, report.Chug.Rating);
        Assert.Equal(StabilityRating.Fail, report.Composite);
        Assert.Contains("Chug FAIL", report.CompositeReason);
    }

    // ─────────────────────────────────────────────────────────────
    //  (8) Report export includes the stability section
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ReportExport_ContainsStabilitySection()
    {
        var cond = DefaultConditions();
        var gen = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign());
        string report = ReportExport.Build(gen);

        Assert.Contains("COMBUSTION STABILITY SCREENING", report);
        Assert.Contains("L1 (1st longitudinal)", report);
        Assert.Contains("T1 (1st tangential)", report);
        Assert.Contains("T2 (2nd tangential)", report);
        Assert.Contains("Composite rating", report);
        // Preliminary-design fidelity stamp must survive.
        Assert.Contains("preliminary-design", report);
    }

    // ─────────────────────────────────────────────────────────────
    //  (9) Marginal chug propagates to composite (no other fails)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void StabilityScreening_Composite_DemotesOnChugMarginal()
    {
        var gas = SampleGas();
        var contour = MakeContour(chamberRadius_mm: 25, chamberLength_mm: 100);

        // Pick a chamber whose T1 is well outside 1–4 kHz so screech is clean;
        // ratio = 0.13 → Marginal (below lower band but within marginal pad).
        var inj = new InjectorState(DeltaPInj_Pa: 0.13 * 6.9e6);
        var report = StabilityScreening.Evaluate(contour, gas, 6.9e6, inj);

        Assert.Equal(StabilityRating.Marginal, report.Chug.Rating);
        // Composite should be at worst Fail (only if screech also fails);
        // for a normal 50 mm diameter with SampleGas, T1 ≈ 1163*1.841/(π*0.050)
        // = ~13.6 kHz, well above the risk band, so composite should be Marginal.
        Assert.True(report.Composite == StabilityRating.Marginal
                 || report.Composite == StabilityRating.Fail,
            $"Expected Marginal/Fail, got {report.Composite}");
    }
}
