// ReactorFixture_MethylAcetateHydrolysis.cs — Sprint A.76 Phase 3
// published-anchor cluster-validation fixture for the ChemicalReactor
// pillar.
//
// Anchors the Wave-1 first-order CSTR / PFR / Batch ideal-reactor model
// to the **methyl-acetate hydrolysis** textbook example (Levenspiel
// 1999 *Chemical Reaction Engineering* 3rd ed. chap 5; Fogler 2020
// *Elements of Chemical Reaction Engineering* 5th ed. chap 4 example
// 4-4; Smith 1981 *Chemical Engineering Kinetics* 3rd ed. §2.4) — the
// canonical pseudo-first-order acid-catalyzed hydrolysis example used
// to introduce CSTR and PFR design equations:
//
//   CH₃COOCH₃ + H₂O  →  CH₃COOH + CH₃OH      (acid-catalyzed)
//
// At the cluster-mid Arrhenius parameters (A = 1.85 × 10¹⁰ s⁻¹,
// E_a = 86 kJ/mol per Levenspiel 3e Table A2 methyl-acetate hydrolysis
// values) and 60 °C (333 K), the rate constant lands k ≈ 5.9 × 10⁻⁴
// s⁻¹. For an industrial-scale CSTR (V = 5 m³, Q = 1 L/s, C_A0 = 1 M),
// τ = 5000 s and Da_1 = k·τ ≈ 2.96 → X_CSTR ≈ 0.75 (75 % conversion).
//
// Phase-3 coverage backfill on the ChemicalReactor pillar — Cohort 4
// continuation after A.75 Aerostructures. The Wave-1 closed-form model
// captures Arrhenius rate constant + Damkohler scaling + CSTR-vs-PFR
// conversion difference exactly; non-isothermal effects, heat-transfer
// limitations, and non-ideal mixing are deferred to CHM.W2+ (CHM.W2
// already adds second-order kinetics + Batch reactor).
//
// Per ADR-036 D3.2, each [Fact] carries a rationale comment with
// either a closed-form derivation or a Levenspiel / Fogler / Smith
// textbook citation.
//
// Q3 multi-component physics-calibration watchpoint does NOT apply —
// the closed-form first-order reaction model is exact at the textbook
// anchor; no second-component split is meaningful for an A → B kinetic
// scheme.

using Voxelforge.Chemical;
using Xunit;

namespace Voxelforge.Tests.Chemical;

public sealed class ReactorFixture_MethylAcetateHydrolysis
{
    // ── Closed-form Arrhenius + Damkohler fingerprints ─────────────────

    [Fact]
    public void MethylAcetate_DesignPoint_RateConstantMatchesArrhenius()
    {
        // k = A · exp(−E_a / (R · T)). At A = 1.85e10, E_a = 86 000,
        // T = 333.15: k ≈ 1.85e10 · exp(−31.05) ≈ 5.92 × 10⁻⁴ s⁻¹.
        var d = MethylAcetateHydrolysisCstr();
        var r = ReactorSolver.Solve(d);
        double expected = d.ArrheniusPreExponential_per_s
                        * System.Math.Exp(-d.ActivationEnergy_J_mol
                                         / (ReactorSolver.R_J_molK
                                          * d.OperatingTemperature_K));
        Assert.Equal(expected, r.RateConstant_per_s, precision: 12);
    }

    [Fact]
    public void MethylAcetate_DesignPoint_ResidenceTimeEqualsVOverQ()
    {
        // τ = V / Q for steady-state CSTR / PFR. At V=5, Q=0.001: τ=5000 s.
        var d = MethylAcetateHydrolysisCstr();
        var r = ReactorSolver.Solve(d);
        Assert.Equal(d.ReactorVolume_m3 / d.VolumetricFlowRate_m3s,
                     r.ResidenceTime_s,
                     precision: 9);
    }

    [Fact]
    public void MethylAcetate_DesignPoint_DamkohlerEqualsKTimesTau()
    {
        // Da_1 = k · τ for first-order kinetics.
        var r = ReactorSolver.Solve(MethylAcetateHydrolysisCstr());
        Assert.Equal(r.RateConstant_per_s * r.ResidenceTime_s,
                     r.DamkohlerNumber,
                     precision: 9);
    }

    [Fact]
    public void MethylAcetate_DesignPoint_CstrConversionMatchesClosedForm()
    {
        // X_CSTR = Da / (1 + Da). At Da ≈ 2.96, X ≈ 0.747.
        var r = ReactorSolver.Solve(MethylAcetateHydrolysisCstr());
        Assert.Equal(r.DamkohlerNumber / (1.0 + r.DamkohlerNumber),
                     r.Conversion,
                     precision: 9);
    }

    [Fact]
    public void MethylAcetate_DesignPoint_OutletConcentrationFromConversion()
    {
        // C_A = C_A0 · (1 − X).
        var d = MethylAcetateHydrolysisCstr();
        var r = ReactorSolver.Solve(d);
        Assert.Equal(d.InletConcentration_mol_m3 * (1.0 - r.Conversion),
                     r.OutletConcentration_mol_m3,
                     precision: 9);
    }

    [Fact]
    public void MethylAcetate_DesignPoint_ProductRateMatchesClosedForm()
    {
        // ṅ_B = Q · C_A0 · X.
        var d = MethylAcetateHydrolysisCstr();
        var r = ReactorSolver.Solve(d);
        Assert.Equal(d.VolumetricFlowRate_m3s
                   * d.InletConcentration_mol_m3
                   * r.Conversion,
                     r.ProductFormationRate_mol_s,
                     precision: 9);
    }

    // ── Cluster-anchor band fingerprints ───────────────────────────────

    [Fact]
    public void MethylAcetate_DesignPoint_ConversionInIndustrialBand()
    {
        // 60 °C operating point with τ = 5000 s gives Da ≈ 3, X ≈ 0.75.
        // Industrial methyl-acetate hydrolysis cluster: X ∈ [0.6, 0.9]
        // depending on T + catalyst concentration (Smith 1981 §2.4
        // cluster).
        var r = ReactorSolver.Solve(MethylAcetateHydrolysisCstr());
        Assert.InRange(r.Conversion, 0.6, 0.9);
    }

    [Fact]
    public void MethylAcetate_DesignPoint_DamkohlerInModerateRegime()
    {
        // Industrial reactor design targets Da ∈ [1, 10] for CSTR — high
        // enough for usable conversion, low enough to avoid excessive
        // reactor volume. Lever and Bates 1990 process-design cluster.
        var r = ReactorSolver.Solve(MethylAcetateHydrolysisCstr());
        Assert.InRange(r.DamkohlerNumber, 1.0, 10.0);
    }

    [Fact]
    public void MethylAcetate_DesignPoint_RateConstantInMethylAcetateClusterBand()
    {
        // Acid-catalyzed methyl-acetate hydrolysis k at 333 K: cluster
        // 1e-4 to 1e-2 s⁻¹ depending on HCl molarity (Smith 1981 §2.4).
        // Wave-1 prediction at Levenspiel anchor lands ≈ 5.9e-4 — mid-
        // cluster.
        var r = ReactorSolver.Solve(MethylAcetateHydrolysisCstr());
        Assert.InRange(r.RateConstant_per_s, 1e-4, 1e-2);
    }

    // ── Categorical + reactor-topology fingerprints ────────────────────

    [Fact]
    public void MethylAcetate_DesignPoint_UsesCstrKind()
    {
        // Levenspiel chap 5 + Fogler chap 4 examples cast methyl-acetate
        // hydrolysis on a CSTR as the canonical design problem.
        // Categorical fingerprint.
        Assert.Equal(ReactorKind.Cstr, MethylAcetateHydrolysisCstr().Kind);
    }

    [Fact]
    public void MethylAcetate_DesignPoint_UsesFirstOrderKinetics()
    {
        // Acid-catalyzed methyl-acetate hydrolysis is pseudo-first-order
        // in methyl acetate when water is in large excess (Smith 1981
        // §2.4). Categorical default fingerprint.
        Assert.Equal(ReactionOrder.First, MethylAcetateHydrolysisCstr().Order);
    }

    [Fact]
    public void MethylAcetate_PfrConversionExceedsCstr_AtSameDamkohler()
    {
        // At positive Da, X_PFR = 1 − exp(−Da) > Da / (1 + Da) = X_CSTR
        // for any Da > 0. Levenspiel 1999 Fig 5.3 classic plot. The gap
        // collapses at Da → 0 + Da → ∞.
        var cstr = ReactorSolver.Solve(MethylAcetateHydrolysisCstr());
        var pfr  = ReactorSolver.Solve(
            MethylAcetateHydrolysisCstr() with { Kind = ReactorKind.Pfr });
        Assert.True(pfr.Conversion > cstr.Conversion,
            $"PFR X ({pfr.Conversion:F3}) must exceed CSTR X "
          + $"({cstr.Conversion:F3}) at Da = {pfr.DamkohlerNumber:F2}.");
    }

    // ── Operating-envelope fingerprints ────────────────────────────────

    [Fact]
    public void MethylAcetate_HigherTemperature_RaisesRateConstant()
    {
        // Arrhenius monotonicity: dk/dT > 0 for E_a > 0. Raising T from
        // 333 K to 353 K (80 °C) should raise k by exp(86000·(1/333 −
        // 1/353)/R) = exp(86000·1.7e-4/R) = exp(86000·1.7e-4/8.314)
        // ≈ exp(1.76) ≈ 5.8×.
        var nominal = ReactorSolver.Solve(MethylAcetateHydrolysisCstr());
        var hotter  = ReactorSolver.Solve(
            MethylAcetateHydrolysisCstr() with { OperatingTemperature_K = 353.15 });
        double ratio = hotter.RateConstant_per_s / nominal.RateConstant_per_s;
        Assert.InRange(ratio, 4.0, 8.0);
    }

    [Fact]
    public void MethylAcetate_LongerResidence_RaisesConversion()
    {
        // X monotone in τ for both CSTR (Da/(1+Da)) and PFR
        // (1 − exp(−Da)) at fixed k, C_A0. Doubling V (or halving Q)
        // doubles Da, raising X.
        var nominal = ReactorSolver.Solve(MethylAcetateHydrolysisCstr());
        var doubleV = ReactorSolver.Solve(
            MethylAcetateHydrolysisCstr() with { ReactorVolume_m3 = 10.0 });
        Assert.True(doubleV.Conversion > nominal.Conversion,
            $"Doubling V (Da × 2) must raise X. Nominal {nominal.Conversion:F3} "
          + $"vs double-V {doubleV.Conversion:F3}.");
    }

    // ── Wave-2 (CHM.W2) — second-order + Batch fingerprints ────────────

    [Fact]
    public void MethylAcetate_Wave2_SecondOrderCstrConversionLowerThanFirstOrder()
    {
        // For Da_1 = Da_2 (same product k·τ if C_A0 = 1 / 1 unit), the
        // second-order CSTR conversion is LOWER than first-order because
        // the rate term is k·C_A² instead of k·C_A — the rate falls
        // faster as conversion proceeds in 2nd-order. Sprint CHM.W2
        // cross-order fingerprint.
        var first  = ReactorSolver.Solve(MethylAcetateHydrolysisCstr());
        var second = ReactorSolver.Solve(
            MethylAcetateHydrolysisCstr() with
            {
                Order = ReactionOrder.SecondInA,
                InletConcentration_mol_m3 = 1.0,  // make Da_2 = k·τ
            });
        Assert.True(second.Conversion < first.Conversion,
            $"Second-order X ({second.Conversion:F3}) must be < first-order "
          + $"X ({first.Conversion:F3}) at Da = k·τ ≈ same value.");
    }

    [Fact]
    public void MethylAcetate_Wave2_BatchConversionEqualsPfrAtSameDamkohler()
    {
        // For 1st-order kinetics in CHM.W2, Batch X(t) = 1 − exp(−k·t)
        // is identical to PFR X(τ) = 1 − exp(−k·τ) when t = τ. The
        // closed-form integrates the same ODE; Batch and PFR differ
        // only in interpretation (transient closed vs steady open).
        var d = MethylAcetateHydrolysisCstr();
        var pfr   = ReactorSolver.Solve(d with { Kind = ReactorKind.Pfr });
        var batch = ReactorSolver.Solve(d with
        {
            Kind = ReactorKind.Batch,
            BatchElapsedTime_s = pfr.ResidenceTime_s,
        });
        Assert.Equal(pfr.Conversion, batch.Conversion, precision: 9);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    // Methyl-acetate hydrolysis (acid-catalyzed pseudo-first-order)
    // industrial-scale CSTR — Levenspiel 1999 chap 5 + Fogler 2020
    // chap 4 example 4-4; Smith 1981 §2.4 cluster. Arrhenius parameters
    // mid-cluster:
    //   A = 1.85 × 10¹⁰ s⁻¹ (Levenspiel 3e Table A2)
    //   E_a = 86 kJ/mol
    //   T = 333.15 K (60 °C operating)
    // Industrial-scale CSTR:
    //   V = 5 m³ (industrial polymerization-class)
    //   Q = 1 L/s (≈ 60 L/min feed)
    //   C_A0 = 1000 mol/m³ (1 M methyl acetate)
    private static ReactorDesign MethylAcetateHydrolysisCstr() => new(
        Kind:                          ReactorKind.Cstr,
        ReactorVolume_m3:              5.0,
        VolumetricFlowRate_m3s:        0.001,
        InletConcentration_mol_m3:     1000.0,
        OperatingTemperature_K:        333.15,
        ArrheniusPreExponential_per_s: 1.85e10,
        ActivationEnergy_J_mol:        86_000.0);
}
