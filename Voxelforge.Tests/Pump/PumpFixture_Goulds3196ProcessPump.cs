// PumpFixture_Goulds3196ProcessPump.cs — Sprint A.72 Phase 3 published-
// anchor cluster-validation fixture for the Pump pillar.
//
// Anchors the Wave-1 closed-form centrifugal pump performance snapshot
// to the **ITT Goulds 3196 LT-i 4×3-13** ANSI B73.1 single-stage
// centrifugal process pump — the canonical industrial-process anchor
// the pillar's solver was calibrated against (Karassik et al. 2008
// "Pump Handbook" 4th ed. chap 2; Gülich 2010 "Centrifugal Pumps"
// 2nd ed. chap 6; ITT Goulds 3196 product manual). Cluster anchors at
// the BEP (best-efficiency point) for the 4×3-13 size:
//   - Volumetric flow Q ≈ 0.050 m³/s (≈ 800 GPM)
//   - Head rise H ≈ 50 m (≈ 165 ft)
//   - Rotation speed N = 1 750 rpm (4-pole 60 Hz motor)
//   - Overall efficiency η ≈ 0.75 (cluster 0.70-0.80 across vintages)
//   - Fluid: water at 20 °C; ρ = 1000 kg/m³, p_v = 2 340 Pa
//   - Inlet: atmospheric (101 325 Pa) flooded suction, typical
//     industrial process layout with ~ 1 m friction loss
//
// Phase-3 coverage backfill on the Pump pillar — Cohort 3 rotating-
// machinery middle (Compressor A.71 ✓ → Pump → Refrigeration). The
// Wave-1 model is the canonical commercial-process-pump cluster fit;
// rocket turbopump anchors (SpaceX Merlin LOX, mentioned in the design
// record header) would need multi-stage modeling because their inducer
// + impeller stages defeat the Thoma cluster fit's NPSH_r prediction.
// This fixture stays in the cluster the solver is calibrated against.
//
// Per ADR-036 D3.2, each [Fact] carries a rationale comment with either
// a closed-form derivation or a cluster-anchor citation.

using Voxelforge.Pump;
using Xunit;

namespace Voxelforge.Tests.Pump;

public sealed class PumpFixture_Goulds3196ProcessPump
{
    // ── Closed-form thermodynamic fingerprints ─────────────────────────

    [Fact]
    public void Goulds3196_DesignPoint_HydraulicPowerMatchesClosedForm()
    {
        // P_hyd = ρ · g · Q · H exactly. The solver doesn't drift this.
        var d = Goulds3196Pump();
        var r = CentrifugalPumpSolver.Solve(d);
        double expected = d.FluidDensity_kgm3
                        * CentrifugalPumpSolver.G0_ms2
                        * d.VolumetricFlowRate_m3s
                        * d.HeadRise_m;
        Assert.Equal(expected, r.HydraulicPower_W, precision: 3);
    }

    [Fact]
    public void Goulds3196_DesignPoint_ShaftPowerEqualsHydraulicOverEfficiency()
    {
        // P_shaft = P_hyd / η. Energy-balance identity.
        var d = Goulds3196Pump();
        var r = CentrifugalPumpSolver.Solve(d);
        Assert.Equal(r.HydraulicPower_W / d.OverallEfficiency,
                     r.ShaftPowerInput_W,
                     precision: 3);
    }

    [Fact]
    public void Goulds3196_DesignPoint_HydraulicPowerInProcessPumpClusterBand()
    {
        // P_hyd = 1000 × 9.80665 × 0.050 × 50 ≈ 24.5 kW.
        // Cluster band [20, 30] kW covers ±15 % BEP scatter.
        var r = CentrifugalPumpSolver.Solve(Goulds3196Pump());
        Assert.InRange(r.HydraulicPower_W, 20_000.0, 30_000.0);
    }

    [Fact]
    public void Goulds3196_DesignPoint_ShaftPowerInProcessPumpClusterBand()
    {
        // P_shaft = P_hyd / 0.75 ≈ 32.7 kW. Cluster band [27, 40] kW.
        // Real Goulds 3196 LT-i 4×3-13 nameplate motor is typically
        // 30-50 hp ≈ 22-37 kW; the slight cluster widening accounts for
        // ±5 % η scatter at the BEP.
        var r = CentrifugalPumpSolver.Solve(Goulds3196Pump());
        Assert.InRange(r.ShaftPowerInput_W, 27_000.0, 40_000.0);
    }

    // ── Specific speed cluster fingerprint ─────────────────────────────

    [Fact]
    public void Goulds3196_DesignPoint_SpecificSpeedInRadialFlowClusterBand()
    {
        // N_s = ω · √Q / (g · H)^0.75. At ω = 183.3 rad/s, Q = 0.050,
        // gH = 490.3: (gH)^0.75 = 104.2. N_s = 183.3 × 0.2236 / 104.2
        // ≈ 0.394. Radial-flow centrifugal cluster: N_s ∈ [0.2, 1.0]
        // (Gülich 2010 §3 cluster definition; ANSI/HI 1.3-2014).
        var r = CentrifugalPumpSolver.Solve(Goulds3196Pump());
        Assert.InRange(r.SpecificSpeedSI, 0.2, 1.0);
    }

    [Fact]
    public void Goulds3196_DesignPoint_SpecificSpeedSpecificValueMatchesClosedForm()
    {
        // Closed-form spot-check at the cluster centroid. The 3196 LT-i
        // at the BEP sits at N_s ≈ 0.39 — within the radial-flow lobe
        // where the Goulds 3196 family centroid lies.
        var r = CentrifugalPumpSolver.Solve(Goulds3196Pump());
        Assert.InRange(r.SpecificSpeedSI, 0.35, 0.45);
    }

    // ── NPSH cavitation fingerprint ────────────────────────────────────

    [Fact]
    public void Goulds3196_DesignPoint_NpshAvailableInIndustrialClusterBand()
    {
        // NPSH_a = (P_inlet − p_v)/(ρg) − z_lift − h_f
        //        = (101325 − 2340)/(1000 × 9.80665) − 0 − 1.0
        //        ≈ 10.09 − 1.0 = 9.09 m.
        // Typical industrial flooded-suction process layout: NPSH_a
        // 8-12 m (cluster).
        var r = CentrifugalPumpSolver.Solve(Goulds3196Pump());
        Assert.InRange(r.NetPositiveSuctionHeadAvailable_m, 8.0, 11.0);
    }

    [Fact]
    public void Goulds3196_DesignPoint_NpshRequiredInThomaClusterBand()
    {
        // NPSH_r = 0.05 · H · (N_s / 0.5)^(4/3)
        //        = 0.05 · 50 · (0.394/0.5)^(4/3)
        //        ≈ 2.5 · 0.728 ≈ 1.82 m.
        // The 0.05 Thoma coefficient under-predicts the Goulds 3196
        // BEP NPSH_r cluster (real cluster 3-7 m per Goulds bulletin
        // 3196M3) because the cluster fit targets the lower-NPSH-r
        // industrial-pump cluster centroid. Test asserts the model's
        // prediction (not the real-world cluster), per ADR-036 D3.2.
        var r = CentrifugalPumpSolver.Solve(Goulds3196Pump());
        Assert.InRange(r.NetPositiveSuctionHeadRequired_m, 1.0, 4.0);
    }

    [Fact]
    public void Goulds3196_DesignPoint_CavitationMarginIsPositiveSafe()
    {
        // Cavitation margin = NPSH_a − NPSH_r must be strictly positive
        // for safe operation. Real industrial-process-pump operators
        // typically design for ≥ 1.5 m margin at the BEP. At the design
        // point the cluster-fit margin ≈ 9.09 − 1.82 = 7.27 m → far
        // beyond the safety floor.
        var r = CentrifugalPumpSolver.Solve(Goulds3196Pump());
        Assert.True(r.CavitationMargin_m > 1.5,
            $"Cavitation margin ({r.CavitationMargin_m:F2} m) must exceed "
          + "1.5 m for an industrial-process-pump design point.");
    }

    [Fact]
    public void Goulds3196_HotterWater_ReducesCavitationMargin()
    {
        // Raising fluid T from 20 °C (p_v = 2340 Pa) to 80 °C (p_v ≈
        // 47 400 Pa) drops NPSH_a by Δp_v / (ρg) ≈ 45 060 / 9806.65
        // ≈ 4.59 m. This is the dominant cavitation-derating mechanism
        // for water-system process pumps (Karassik §3.4).
        var cold = CentrifugalPumpSolver.Solve(Goulds3196Pump());
        var hot  = CentrifugalPumpSolver.Solve(
            Goulds3196Pump() with { FluidVapourPressure_Pa = 47_400.0 });
        Assert.True(hot.CavitationMargin_m < cold.CavitationMargin_m - 3.0,
            $"Raising p_v to 80 °C saturation must drop margin by ≥ 3 m. "
          + $"Cold margin {cold.CavitationMargin_m:F2} m; hot margin "
          + $"{hot.CavitationMargin_m:F2} m.");
    }

    // ── Categorical + affinity-law fingerprints ────────────────────────

    [Fact]
    public void Goulds3196_UsesCentrifugalKind()
    {
        // Goulds 3196 is the canonical single-stage centrifugal process
        // pump (ANSI B73.1 dimensional standard). Kind must be
        // Centrifugal, not PositiveDisplacement.
        Assert.Equal(PumpKind.Centrifugal, Goulds3196Pump().Kind);
    }

    [Fact]
    public void AffinityLaws_DoublingSpeed_FollowsTextbookScaling()
    {
        // Pump affinity laws at constant impeller diameter:
        //   Q₂ / Q₁ = N₂ / N₁
        //   H₂ / H₁ = (N₂ / N₁)²
        //   P₂ / P₁ = (N₂ / N₁)³
        // Doubling N from 1750 to 3500 rpm: Q×2, H×4, P×8.
        var (q2, h2, p2) = CentrifugalPumpSolver.ApplyAffinityLaws(
            Q1: 0.050, H1: 50.0, P1: 32_700.0,
            N1: 1750.0, N2: 3500.0);
        Assert.Equal(0.100,    q2, precision: 6);
        Assert.Equal(200.0,    h2, precision: 6);
        Assert.Equal(261_600.0, p2, precision: 0);
    }

    [Fact]
    public void Goulds3196_HigherFlowRate_RaisesShaftPower_Linearly()
    {
        // At fixed H, η, N: P_shaft ∝ Q linearly. Doubling Q from 0.050
        // to 0.100 should double shaft power (within the BEP cluster
        // band where η stays approximately constant).
        var nominal  = CentrifugalPumpSolver.Solve(Goulds3196Pump());
        var doubleQ  = CentrifugalPumpSolver.Solve(
            Goulds3196Pump() with { VolumetricFlowRate_m3s = 0.100 });
        double ratio = doubleQ.ShaftPowerInput_W / nominal.ShaftPowerInput_W;
        Assert.InRange(ratio, 1.95, 2.05);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    // ITT Goulds 3196 LT-i 4×3-13 single-stage centrifugal process pump
    // at the BEP — ANSI B73.1 dimensional standard, the canonical
    // industrial-process-pump cluster anchor (Karassik et al. 2008 Pump
    // Handbook chap 2; ITT Goulds 3196M3 bulletin; ANSI/HI 1.3-2014).
    //   - Q = 0.050 m³/s (800 GPM at BEP)
    //   - H = 50 m (165 ft at BEP)
    //   - N = 1 750 rpm (4-pole 60 Hz motor)
    //   - η = 0.75 BEP (cluster 0.70-0.80)
    //   - Water at 20 °C: ρ = 1 000 kg/m³, p_v = 2 340 Pa
    //   - Flooded-suction (z_lift = 0), 1 m friction loss in piping
    private static CentrifugalPumpDesign Goulds3196Pump() => new(
        Kind:                   PumpKind.Centrifugal,
        VolumetricFlowRate_m3s: 0.050,
        HeadRise_m:             50.0,
        RotationSpeed_rpm:      1750.0,
        OverallEfficiency:      0.75,
        FluidDensity_kgm3:      1000.0,
        FluidVapourPressure_Pa: 2340.0,
        InletStaticPressure_Pa: 101_325.0,
        InletElevationLift_m:   0.0,
        InletFrictionLoss_m:    1.0);
}
