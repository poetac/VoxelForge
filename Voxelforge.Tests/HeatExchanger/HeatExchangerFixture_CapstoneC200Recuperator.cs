// HeatExchangerFixture_CapstoneC200Recuperator.cs — Sprint A.66 Phase 3
// published-anchor cluster-validation fixture for the HeatExchanger
// pillar.
//
// Anchors the Wave-1 plate-fin ε-NTU model to the **Capstone C200**
// microturbine recuperator cluster (Treece et al. 2002 ASME GT2002-30404,
// McDonald 2003 "Recuperator considerations for future higher-efficiency
// microturbines," Manley 2003 SAE 2003-01-2497). The C200 cluster anchors:
//   - 200 kWe net electrical, recuperated single-shaft Brayton
//   - Recuperator effectiveness ε ≈ 0.85 (cluster 0.83–0.88)
//   - Recuperator hot inlet ≈ 575 °C (850 K cluster anchor)
//   - Recuperator cold inlet ≈ 200 °C (480 K compressor-discharge cluster)
//   - Air mass flow ≈ 1.3 kg/s (cluster anchor for the 200 kWe class)
//   - Pressure ratio ≈ 4:1; cold side at ~ 4 bar, hot side at ~ 1 bar
//
// Phase-3 coverage backfill on the HeatExchanger pillar — first published-
// anchor fixture in the framing-B Phase 3 second-anchor pattern (track
// C.1 thermal-management triple: HeatExchanger / Radiator / HeatPipe).
//
// Model-vs-product calibration disclaimer (ADR-036 D3.2):
//
//   Real C200 hardware uses a **primary-surface** recuperator geometry
//   (cellular polymer-foil with very-thin-wall metal corrugations, see
//   McDonald 2000 GT2000-167). The Wave-1 PlateFin solver instead applies
//   the canonical Kays-London **offset-strip-fin** j/f-factor cluster
//   correlations to a plate-fin geometry that brackets the same Manley
//   2003 plate-fin recuperator cluster (PlateSpacing 6.35 mm, FinPitch
//   1.69 mm — industry-standard 1/4″ × ~ 600 fpm). The model therefore
//   predicts an effectiveness *above* the published C200 0.85 cluster
//   anchor because (1) it assumes perfect-conductor fins (η_fin = 1 in
//   Wave-1), (2) it neglects header / manifold pressure-recovery losses,
//   (3) it neglects per-side fouling and side leakage. The Wave-2 fin-
//   efficiency correction (`EnableFinEfficiencyCorrection`) reduces this
//   over-prediction; this fixture exercises both Wave-1 and Wave-2 paths.
//
//   Per ADR-036 D3.2, the test bands describe **what the Wave-1 model
//   predicts at the C200-class design point**, with cluster-scatter
//   margin around the model output rather than around the published
//   product effectiveness. The fixture fingerprints the model's behaviour
//   at a real-world-anchored geometry; downstream `EpsilonNtuSolverTests`
//   already covers the closed-form ε(NTU, C_r) bit-correctness.

using Voxelforge.HeatExchanger;
using Xunit;

namespace Voxelforge.Tests.HeatExchanger;

public sealed class HeatExchangerFixture_CapstoneC200Recuperator
{
    // ── Effectiveness + heat duty in recuperator-class band ────────────

    [Fact]
    public void CapstoneC200_DesignPoint_EffectivenessInRecuperatorClusterBand()
    {
        // Capstone C200 published effectiveness 0.85 (cluster 0.83–0.88).
        // The Wave-1 model over-predicts because it assumes η_fin = 1.0 +
        // no fouling — ε lands in the upper recuperator regime [0.85, 1.0]
        // when run at the C200-class plate-fin geometry. Wave-2 fin-
        // efficiency correction (separate test below) reduces this toward
        // the published value.
        var r = EpsilonNtuSolver.Solve(CapstoneC200Recuperator());
        Assert.InRange(r.Effectiveness, 0.85, 1.00);
    }

    [Fact]
    public void CapstoneC200_DesignPoint_HeatDutyInClusterBand()
    {
        // Q = ε · C_min · (T_hot_in − T_cold_in). At ε ≈ 0.99 (model),
        // C_min ≈ 1339 W/K (cold-side ṁ·cp), ΔT = 370 K → Q ≈ 490 kW.
        // Published C200 recuperator heat duty 500–600 kW thermal at
        // nameplate (Treece 2002 §4); the model's lower-end prediction
        // reflects the missing pressure-recovery and primary-surface
        // area gains the real hardware achieves. Cluster band [400, 700]
        // kW swallows both ends.
        var r = EpsilonNtuSolver.Solve(CapstoneC200Recuperator());
        Assert.InRange(r.HeatDuty_W, 400_000.0, 700_000.0);
    }

    [Fact]
    public void CapstoneC200_DesignPoint_OverallUInPlateFinClusterBand()
    {
        // Industrial plate-fin recuperator U typically 200–800 W/(m²·K)
        // (Shah-Sekulić 2003 Table 9.4 cluster). At the C200-class design
        // point, the model lands near 400 W/(m²·K) — mid-cluster.
        var r = EpsilonNtuSolver.Solve(CapstoneC200Recuperator());
        Assert.InRange(r.OverallHeatTransferCoefficient_W_m2K, 200.0, 800.0);
    }

    [Fact]
    public void CapstoneC200_DesignPoint_PerSideHTCsInPlateFinClusterBand()
    {
        // Offset-strip-fin per-side h ≈ 500–1500 W/(m²·K) at Re ≈ 1000–
        // 2000 (Manglik-Bergles 1995 cluster). Hot side ~ 880 W/(m²·K) at
        // ρ=0.5 / μ=3.8e-5; cold side ~ 730 W/(m²·K) at ρ=2.5 / μ=2.7e-5.
        var r = EpsilonNtuSolver.Solve(CapstoneC200Recuperator());
        Assert.InRange(r.HotSideHTC_W_m2K,  400.0, 1500.0);
        Assert.InRange(r.ColdSideHTC_W_m2K, 400.0, 1500.0);
    }

    // ── Reynolds in offset-strip-fin valid band ────────────────────────

    [Fact]
    public void CapstoneC200_DesignPoint_ReynoldsInOffsetStripFinClusterBand()
    {
        // Kays-London offset-strip-fin j/f correlations apply for
        // Re ≈ 200–5000 (the laminar-transition regime). At C200-class
        // mass flow and the chosen plate-fin geometry, both sides land
        // Re ≈ 1000–2000 — exactly the regime where the correlations
        // are well-anchored.
        var r = EpsilonNtuSolver.Solve(CapstoneC200Recuperator());
        Assert.InRange(r.HotReynolds,  500.0, 3000.0);
        Assert.InRange(r.ColdReynolds, 500.0, 3000.0);
    }

    // ── Second-law + energy-balance fingerprint ────────────────────────

    [Fact]
    public void CapstoneC200_DesignPoint_OutletsObeySecondLaw()
    {
        var d = CapstoneC200Recuperator();
        var r = EpsilonNtuSolver.Solve(d);
        // Hot-side outlet can't drop below cold inlet.
        Assert.True(r.HotOutletTemperature_K  >= d.ColdInletTemperature_K);
        // Cold-side outlet can't rise above hot inlet.
        Assert.True(r.ColdOutletTemperature_K <= d.HotInletTemperature_K);
        // Both outlets must lie strictly inside the inlet temperature
        // interval (non-trivial HX with finite ε).
        Assert.True(r.HotOutletTemperature_K  < d.HotInletTemperature_K);
        Assert.True(r.ColdOutletTemperature_K > d.ColdInletTemperature_K);
    }

    [Fact]
    public void CapstoneC200_DesignPoint_EnergyBalancePreserved()
    {
        var d = CapstoneC200Recuperator();
        var r = EpsilonNtuSolver.Solve(d);
        double Q_hot  = d.HotMassFlow_kgs  * d.HotCp_JkgK
                      * (d.HotInletTemperature_K - r.HotOutletTemperature_K);
        double Q_cold = d.ColdMassFlow_kgs * d.ColdCp_JkgK
                      * (r.ColdOutletTemperature_K - d.ColdInletTemperature_K);
        // Both sides' Q must equal the solver's HeatDuty_W within float
        // round-off — the solver derives the outlets from Q + capacity
        // rates, so this is a fingerprint test for that derivation.
        Assert.Equal(Q_hot,  r.HeatDuty_W, precision: 3);
        Assert.Equal(Q_cold, r.HeatDuty_W, precision: 3);
    }

    // ── Recuperator design-class fingerprints ──────────────────────────

    [Fact]
    public void CapstoneC200_DesignPoint_NearBalancedFlow()
    {
        // C200 recuperator runs ṁ_hot ≈ ṁ_cold; cp differs slightly with
        // temperature → C_r should sit near 0.95 (cold cp lower at 480 K
        // than hot cp at 850 K). Recuperator design-class fingerprint.
        var r = EpsilonNtuSolver.Solve(CapstoneC200Recuperator());
        Assert.InRange(r.CapacityRateRatio, 0.90, 1.00);
    }

    [Fact]
    public void CapstoneC200_DesignPoint_HighNtuFingerprint()
    {
        // Recuperator design class targets NTU ≥ 5 to push ε above 0.80.
        // The C200-class plate-fin geometry produces NTU ≈ 32 under the
        // Wave-1 perfect-conductor-fin assumption; Wave-2 fin-efficiency
        // correction (η_fin ≈ 0.30 for thin Inconel fins) reduces NTU
        // toward the published 5–10 regime.
        var r = EpsilonNtuSolver.Solve(CapstoneC200Recuperator());
        Assert.True(r.NumberOfTransferUnits >= 5.0,
            $"NTU ({r.NumberOfTransferUnits:F2}) must exceed 5 for a "
          + "recuperator-class HX (ε > 0.80 at C_r ≈ 1).");
    }

    [Fact]
    public void CapstoneC200_DesignPoint_PressureDropsPositiveAndOrdered()
    {
        // ΔP > 0 on both sides (any finite flow through any finite-length
        // channel produces friction loss). Hot side velocity ≈ 38 m/s at
        // ρ_hot = 0.5 kg/m³ produces substantially higher ΔP than cold
        // side at ρ_cold = 2.5 kg/m³ (the low-pressure exhaust side has
        // higher kinetic-pressure loss per unit f-factor).
        var r = EpsilonNtuSolver.Solve(CapstoneC200Recuperator());
        Assert.True(r.HotPressureDrop_Pa  > 0);
        Assert.True(r.ColdPressureDrop_Pa > 0);
        Assert.True(r.HotPressureDrop_Pa > r.ColdPressureDrop_Pa,
            $"Hot-side ΔP ({r.HotPressureDrop_Pa:F0} Pa) should exceed "
          + $"cold-side ΔP ({r.ColdPressureDrop_Pa:F0} Pa) because the "
          + "low-density hot exhaust runs at higher velocity for the "
          + "same channel geometry.");
    }

    // ── Wave-2 fin-efficiency correction (HX.W2) ───────────────────────

    [Fact]
    public void CapstoneC200_Wave2FinEfficiency_ReducesEffectiveHTCsAndHeatDuty()
    {
        // Wave-1 assumes perfect-conductor fins (η_fin = 1). Wave-2 admits
        // η_fin = tanh(mL)/(mL) per side. For a thin Inconel-718 fin
        // (k = 12 W/m·K, t = 0.1 mm, half-height = 3.175 mm) at h ≈
        // 700 W/(m²·K), η_fin ≈ 0.30 — substantial degradation. h_eff
        // drops by ~ 3×, U drops, NTU drops, ε drops, Q drops. This
        // qualitative ordering must hold.
        var w1 = EpsilonNtuSolver.Solve(CapstoneC200Recuperator());
        var w2 = EpsilonNtuSolver.Solve(
            CapstoneC200Recuperator() with { EnableFinEfficiencyCorrection = true });

        Assert.True(w2.HotSideHTC_W_m2K  < w1.HotSideHTC_W_m2K,
            "Wave-2 hot-side h_eff must be < Wave-1 (η_fin < 1).");
        Assert.True(w2.ColdSideHTC_W_m2K < w1.ColdSideHTC_W_m2K,
            "Wave-2 cold-side h_eff must be < Wave-1 (η_fin < 1).");
        Assert.True(w2.OverallHeatTransferCoefficient_W_m2K
                  < w1.OverallHeatTransferCoefficient_W_m2K,
            "Wave-2 U must be < Wave-1 (h_eff drops both sides).");
        Assert.True(w2.HeatDuty_W < w1.HeatDuty_W,
            $"Wave-2 Q ({w2.HeatDuty_W:F0} W) must be < Wave-1 Q "
          + $"({w1.HeatDuty_W:F0} W) — η_fin < 1 lowers UA and ε.");
    }

    [Fact]
    public void CapstoneC200_Wave2FinEfficiency_ReportedEfficienciesInPhysicalBand()
    {
        // Returned η_fin_hot / η_fin_cold must lie strictly in (0, 1] —
        // the 1-D fin model is bounded.
        var r = EpsilonNtuSolver.Solve(
            CapstoneC200Recuperator() with { EnableFinEfficiencyCorrection = true });
        Assert.InRange(r.HotFinEfficiency,  0.05, 1.00);
        Assert.InRange(r.ColdFinEfficiency, 0.05, 1.00);
    }

    [Fact]
    public void CapstoneC200_Wave1Default_ReportedFinEfficienciesAreUnity()
    {
        // Bit-identical HX.W1 backwards-compatibility: η_fin reports 1.0
        // when the correction is disabled (the default).
        var r = EpsilonNtuSolver.Solve(CapstoneC200Recuperator());
        Assert.Equal(1.0, r.HotFinEfficiency,  precision: 12);
        Assert.Equal(1.0, r.ColdFinEfficiency, precision: 12);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    // Capstone C200 microturbine recuperator — published anchor for the
    // 200 kWe-class compact-recuperator cluster (Treece et al. 2002, ASME
    // GT2002-30404; McDonald 2003, SAE 2003-01-2497; Manley 2003 SAE
    // 2003-01-2497). The geometry is the cluster-mid plate-fin form (the
    // model's Wave-1 surface family) rather than the actual C200 primary-
    // surface construction; the model-vs-hardware gap is documented in
    // the file header. Mass flow, inlet temperatures, fluid properties
    // chosen at the C200-class operating point.
    private static PlateFinDesign CapstoneC200Recuperator() => new(
        Kind:                    HeatExchangerKind.PlateFinCounterflow,
        // Core dimensions: cluster-centre 50 × 50 × 30 cm plate-fin block
        // (Manley 2003 cluster; comparable to published C30 / C200 / C1000
        // recuperator envelope volumes at the cluster median).
        CoreLength_m:            0.50,
        CoreWidth_m:             0.50,
        CoreHeight_m:            0.30,
        // Industry-standard 1/4″ plate spacing + ~ 600 fpm offset-strip-
        // fin surface (Kays-London 1984 Table 10.x cluster).
        PlateSpacing_m:          0.00635,
        FinPitch_m:              0.00169,
        FinThickness_m:          0.00010,
        // Balanced air-air recuperator flow; ~ 1.3 kg/s is the C200
        // cluster anchor (Treece 2002 §3).
        HotMassFlow_kgs:         1.30,
        ColdMassFlow_kgs:        1.30,
        // Hot inlet = turbine exhaust ≈ 575 °C; cold inlet = compressor
        // discharge ≈ 200 °C (C200 cluster anchors).
        HotInletTemperature_K:   850.0,
        ColdInletTemperature_K:  480.0,
        // Air cp averaged over each side's temperature span (NIST air
        // cluster: cp_p(850 K) ≈ 1080, cp_p(480 K) ≈ 1030 J/(kg·K)).
        HotCp_JkgK:              1080.0,
        ColdCp_JkgK:             1030.0,
        // Hot side at ~ 1 atm exhaust; cold side at ~ 4 atm compressor
        // discharge (pressure ratio ≈ 4 for C200). ρ = P / (R · T).
        HotDensity_kgm3:         0.50,
        ColdDensity_kgm3:        2.50,
        // Air dynamic viscosity from Sutherland: μ(850 K) ≈ 3.8e-5;
        // μ(480 K) ≈ 2.7e-5 Pa·s.
        HotViscosity_PaS:        3.8e-5,
        ColdViscosity_PaS:       2.7e-5);
}
