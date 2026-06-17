// CompressorFixture_GeJ85TurbojetCompressor.cs — Sprint A.71 Phase 3
// published-anchor cluster-validation fixture for the Compressor pillar.
//
// Anchors the Wave-1 isentropic-then-corrected centrifugal/axial
// compressor model to the **General Electric J85-GE-21** turbojet
// compressor section (Mattingly J.D., Heiser W.H., Pratt D.T. 2002.
// "Aircraft Engine Design," 2nd ed., AIAA Education Series, Appendix B;
// Hill P., Peterson C. 1992. "Mechanics and Thermodynamics of
// Propulsion," 2nd ed., Addison-Wesley, §5.8; GE Aviation J85-GE-21
// engine performance brochure):
//   - 9-stage axial-flow compressor
//   - Overall pressure ratio π_c ≈ 7.0 (cluster 6.5-7.5 for the J85
//     family across J85-5 / -13 / -17 / -21 / -GE-21 variants)
//   - Isentropic efficiency η_c ≈ 0.82 (cluster 0.80-0.85 for axial-
//     flow military-turbojet cluster, post-1960s vintage)
//   - Sea-level static mass flow ≈ 21 kg/s
//   - Standard-atmosphere inlet: T_t1 = 288.15 K, P_t1 = 101 325 Pa
//   - Working gas: cold air, γ = 1.40, cp = 1005 J/(kg·K)
//
// Phase-3 coverage backfill on the Compressor pillar — Cohort 3
// rotating-machinery triple lead (Compressor → Pump → Refrigeration).
// Test bands describe what the Wave-1 lumped model predicts at the
// J85-class design point; the lumped isentropic-then-corrected
// formulation captures bulk thermodynamics exactly (no model-vs-
// hardware gap for π_c, T_t2, P_t2, P_shaft, ΔT_actual / ΔT_is = 1/η).
// Per-stage matching + surge-margin + per-stage efficiency variation
// are deferred to CMP.W2 multi-stage.
//
// Per ADR-036 D3.2, each [Fact] carries a rationale comment with
// either a closed-form derivation or a cluster-anchor citation.

using Voxelforge.Compressor;
using Xunit;

namespace Voxelforge.Tests.Compressor;

public sealed class CompressorFixture_GeJ85TurbojetCompressor
{
    // ── Closed-form thermodynamic fingerprints ─────────────────────────

    [Fact]
    public void J85_DesignPoint_ExitPressureMatchesPiTimesInlet()
    {
        // P_t2 = π_c · P_t1 exactly — this is the definition of the
        // pressure-ratio knob. Test asserts the solver doesn't drift the
        // exit pressure away from the closed-form value.
        var d = GeJ85Compressor();
        var r = CentrifugalCompressorSolver.Solve(d);
        Assert.Equal(d.PressureRatio * d.InletTotalPressure_Pa,
                     r.ExitTotalPressure_Pa,
                     precision: 3);
    }

    [Fact]
    public void J85_DesignPoint_IsentropicExitTemperatureMatchesClosedForm()
    {
        // T_t2_is = T_t1 · π^((γ−1)/γ).
        // At T_t1 = 288.15, π = 7.0, γ = 1.40: ratio = 7^(0.4/1.4) =
        // 7^0.2857 ≈ 1.7466. T_t2_is ≈ 288.15 × 1.7466 ≈ 503.3 K.
        var d = GeJ85Compressor();
        var r = CentrifugalCompressorSolver.Solve(d);
        double exponent = (d.WorkingGasGamma - 1.0) / d.WorkingGasGamma;
        double expected = d.InletTotalTemperature_K
                        * System.Math.Pow(d.PressureRatio, exponent);
        Assert.Equal(expected, r.IsentropicExitTemperature_K, precision: 6);
    }

    [Fact]
    public void J85_DesignPoint_ActualTemperatureRiseEqualsIsentropicOverEfficiency()
    {
        // ΔT_actual = ΔT_is / η. This is the *definition* of isentropic
        // efficiency for a compressor (more work than ideal → higher
        // actual exit T at fixed π). Closed-form fingerprint.
        var d = GeJ85Compressor();
        var r = CentrifugalCompressorSolver.Solve(d);
        Assert.Equal(r.IsentropicTemperatureRise_K / d.IsentropicEfficiency,
                     r.ActualTemperatureRise_K,
                     precision: 6);
    }

    [Fact]
    public void J85_DesignPoint_SpecificWorkMatchesCpTimesDeltaT()
    {
        // w = cp · ΔT_actual — direct first-law-on-an-open-system check.
        var d = GeJ85Compressor();
        var r = CentrifugalCompressorSolver.Solve(d);
        Assert.Equal(d.WorkingGasSpecificHeat_J_kgK * r.ActualTemperatureRise_K,
                     r.SpecificWork_J_kg,
                     precision: 3);
    }

    [Fact]
    public void J85_DesignPoint_ShaftPowerEqualsMassFlowTimesSpecificWork()
    {
        // P_shaft = ṁ · w.
        var d = GeJ85Compressor();
        var r = CentrifugalCompressorSolver.Solve(d);
        Assert.Equal(d.MassFlow_kgs * r.SpecificWork_J_kg,
                     r.ShaftPowerInput_W,
                     precision: 3);
    }

    [Fact]
    public void J85_DesignPoint_ActualExitTemperatureExceedsIsentropic()
    {
        // The real compressor needs MORE temperature rise than ideal to
        // achieve the same pressure ratio (η < 1 means kinetic-energy +
        // viscous losses heat the gas above the reversible path).
        var r = CentrifugalCompressorSolver.Solve(GeJ85Compressor());
        Assert.True(r.ActualExitTemperature_K > r.IsentropicExitTemperature_K,
            $"Actual T_t2 ({r.ActualExitTemperature_K:F1} K) must exceed "
          + $"isentropic T_t2 ({r.IsentropicExitTemperature_K:F1} K).");
    }

    // ── Cluster-anchor bands ──────────────────────────────────────────

    [Fact]
    public void J85_DesignPoint_ExitTotalTemperatureInClusterBand()
    {
        // At η_c = 0.82 the actual T_t2 ≈ 288.15 + (503.3 − 288.15)/0.82
        // ≈ 550.6 K. J85 family cluster spans 530-580 K at the design
        // π = 7 cluster (slight variation in η between -5/-13/-21 vintages).
        var r = CentrifugalCompressorSolver.Solve(GeJ85Compressor());
        Assert.InRange(r.ActualExitTemperature_K, 500.0, 600.0);
    }

    [Fact]
    public void J85_DesignPoint_SpecificWorkInClusterBand()
    {
        // w = cp · ΔT_actual ≈ 1005 · 262 ≈ 263 kJ/kg.
        // Cluster band [200, 320] kJ/kg covers J85 family efficiency
        // scatter + the GT3582R-style centrifugal lower-efficiency end.
        var r = CentrifugalCompressorSolver.Solve(GeJ85Compressor());
        Assert.InRange(r.SpecificWork_J_kg, 200_000.0, 320_000.0);
    }

    [Fact]
    public void J85_DesignPoint_ShaftPowerInTurbojetClusterBand()
    {
        // P_shaft = 21 kg/s × 263 kJ/kg ≈ 5.52 MW. Published J85-21
        // compressor power consumption at SLS ≈ 5.5 MW (Mattingly App B).
        // Cluster band [4, 8] MW covers ±25 % scatter from η + ṁ
        // variation across the J85 family vintages.
        var r = CentrifugalCompressorSolver.Solve(GeJ85Compressor());
        Assert.InRange(r.ShaftPowerInput_W, 4_000_000.0, 8_000_000.0);
    }

    [Fact]
    public void J85_DesignPoint_DensityRatioInCompressorClusterBand()
    {
        // ρ_2 / ρ_1 = (P_2 / P_1) · (T_1 / T_2) = 7 · (288.15 / 550.6)
        // ≈ 3.66. Captures the design-point density jump across the
        // compressor (well above unity — compressor compresses the gas).
        var r = CentrifugalCompressorSolver.Solve(GeJ85Compressor());
        Assert.InRange(r.DensityRatio, 3.0, 4.5);
    }

    // ── Categorical + operating-envelope fingerprints ──────────────────

    [Fact]
    public void J85_UsesAxialFlowKind()
    {
        // J85 is a 9-stage axial-flow compressor (Mattingly App B). The
        // pillar's Wave-1 supports both Centrifugal + AxialFlow kinds
        // with the same lumped model; the categorical assertion ensures
        // the cluster anchor stays representative of the axial-flow
        // sub-family (the dominant aero-turbojet path).
        Assert.Equal(CompressorKind.AxialFlow, GeJ85Compressor().Kind);
    }

    [Fact]
    public void J85_HigherPressureRatio_IncreasesShaftPower()
    {
        // P_shaft scales ~ T_t1 · (π^((γ-1)/γ) - 1) / η. The (γ-1)/γ
        // exponent (= 0.2857 for γ = 1.40) compresses growth at high
        // π; doubling π from 7 to 14 grows P_shaft by
        //   (14^0.2857 - 1) / (7^0.2857 - 1) = 1.126 / 0.747 ≈ 1.51×,
        // NOT 2×. (The doubling-gives-2× intuition only holds at small π
        // where the exponent is approximately linear.)
        var nominal = CentrifugalCompressorSolver.Solve(GeJ85Compressor());
        var highPR  = CentrifugalCompressorSolver.Solve(
            GeJ85Compressor() with { PressureRatio = 14.0 });
        Assert.True(highPR.ShaftPowerInput_W > nominal.ShaftPowerInput_W,
            "P_shaft must increase monotonically with π_c.");
        double ratio = highPR.ShaftPowerInput_W / nominal.ShaftPowerInput_W;
        Assert.InRange(ratio, 1.40, 1.65);
    }

    [Fact]
    public void J85_HotterInletTemperature_IncreasesShaftPower()
    {
        // P_shaft ∝ T_t1 (linear scaling at fixed π_c, η, ṁ). Raising
        // inlet from 288 K (SLS) to 340 K (hot-day Mach-0.5 ram-rise)
        // should raise P_shaft by 340/288 ≈ 1.18×.
        var sls    = CentrifugalCompressorSolver.Solve(GeJ85Compressor());
        var hotDay = CentrifugalCompressorSolver.Solve(
            GeJ85Compressor() with { InletTotalTemperature_K = 340.0 });
        double ratio = hotDay.ShaftPowerInput_W / sls.ShaftPowerInput_W;
        Assert.InRange(ratio, 1.10, 1.25);
    }

    [Fact]
    public void J85_LowerEfficiency_RaisesActualExitTemperature()
    {
        // Same π_c, same ṁ, same inlet → T_t2 = T_t1 + ΔT_is/η.
        // Dropping η from 0.82 to 0.70 raises T_t2 by ΔT_is · (1/0.70 -
        // 1/0.82) ≈ 215 × 0.21 ≈ 45 K.
        var nominal = CentrifugalCompressorSolver.Solve(GeJ85Compressor());
        var lowEff  = CentrifugalCompressorSolver.Solve(
            GeJ85Compressor() with { IsentropicEfficiency = 0.70 });
        Assert.True(lowEff.ActualExitTemperature_K
                  > nominal.ActualExitTemperature_K + 20.0,
            $"Dropping η to 0.70 should raise T_t2 by ≥ 20 K vs "
          + $"η = 0.82 baseline. Got Δ = "
          + $"{lowEff.ActualExitTemperature_K - nominal.ActualExitTemperature_K:F1} K.");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    // General Electric J85-GE-21 turbojet compressor — Mattingly App B
    // anchor (J85 family at the cluster median, post-1960s vintage).
    //   - 9-stage axial-flow (Kind = AxialFlow)
    //   - Sea-level static, standard atmosphere
    //   - π_c ≈ 7.0 (cluster anchor 6.5-7.5 across J85 family)
    //   - η_isentropic ≈ 0.82 (cluster anchor 0.80-0.85)
    //   - ṁ ≈ 21 kg/s
    //   - cold-air working gas: γ = 1.40, cp = 1005 J/(kg·K)
    private static CentrifugalCompressorDesign GeJ85Compressor() => new(
        Kind:                          CompressorKind.AxialFlow,
        MassFlow_kgs:                  21.0,
        InletTotalTemperature_K:       288.15,
        InletTotalPressure_Pa:         101_325.0,
        PressureRatio:                 7.0,
        IsentropicEfficiency:          0.82,
        WorkingGasGamma:               1.40,
        WorkingGasSpecificHeat_J_kgK:  1005.0);
}
