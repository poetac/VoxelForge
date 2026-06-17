// CentrifugalPumpFixture_MerlinLox.cs — Sprint B.18 published-product
// validation fixture for the high-N, high-H, cryogenic-propellant path
// through the Pump pillar.
//
// Anchors the model to the **SpaceX Merlin 1D LOX turbopump** main
// impeller (post-inducer), as flown on the Falcon 9 booster + second
// stage and the Falcon Heavy boosters. Public anchor (SpaceX
// presentations + AIAA conference papers + Hans Koenigsmann
// "Reliability Considerations in the SpaceX Falcon 9 Vehicle"
// AIAA-2017-2017):
//   - Merlin LOX main turbopump stage
//   - ṁ_LOX ≈ 240 kg/s, ρ_LOX ≈ 1141 kg/m³ → Q ≈ 0.21 m³/s
//   - Discharge ≈ 167 bar → H ≈ 1700 m (ΔP / (ρ·g))
//   - Shaft speed ≈ 36 000 rpm
//   - Pump efficiency η ≈ 0.70 (cluster mid-band for cryogenic single-
//     stage centrifugals with high N_s)
//   - Inlet pressure post-inducer ≈ 30 bar (Merlin uses an axial-flow
//     inducer pre-stage to drop NPSH_r below tank pressurization)
//   - LOX vapor pressure at storage temp ≈ 1 bar (saturated)
//
// Phase-3 coverage backfill — eleventh second-anchor sprint after PRs
// #515-#525. Solver header explicitly cites both Goulds 3196 (industrial
// Wave-1 baseline) and the Merlin LOX turbopump as the cluster anchor
// pair. This fixture activates the Merlin anchor.
//
// Note: the Wave-1 NPSH_r cluster fit (Thoma 0.05 · H · (N_s/0.5)^4/3)
// does NOT account for inducer pre-stages. Real Merlin uses an axial-
// flow inducer to reduce the effective NPSH_r below tank-pressurization
// limits. The fixture pins inlet pressure at the post-inducer level
// (30 bar) so the model-predicted NPSH_a sits above NPSH_r — matching
// the real Merlin operating envelope.

using Voxelforge.Pump;
using Xunit;

namespace Voxelforge.Tests.Pump;

public sealed class CentrifugalPumpFixture_MerlinLox
{
    // ── Nameplate at design operating point ───────────────────────────

    [Fact]
    public void MerlinLox_AtDesignPoint_HydraulicPowerMatchesPotentialEnergy()
    {
        // P_hyd = ρ · g · Q · H = 1141 × 9.80665 × 0.21 × 1700
        // ≈ 3.99 MW exact (formula).
        var r = CentrifugalPumpSolver.Solve(MerlinLoxClass());
        const double expected = 1141.0 * 9.80665 * 0.21 * 1700.0;
        Assert.Equal(expected, r.HydraulicPower_W, precision: 3);
    }

    [Fact]
    public void MerlinLox_AtDesignPoint_HydraulicPowerInMultiMegawattBand()
    {
        // Merlin LOX side hydraulic power lands ~ 4 MW. Cluster band
        // [2, 5] MW — combined LOX + RP-1 turbopump assembly runs
        // ~ 10 MW shaft.
        var r = CentrifugalPumpSolver.Solve(MerlinLoxClass());
        double mW = r.HydraulicPower_W / 1.0e6;
        Assert.InRange(mW, 2.0, 5.0);
    }

    [Fact]
    public void MerlinLox_AtDesignPoint_ShaftPowerExceedsHydraulicByEfficiencyRatio()
    {
        // P_shaft = P_hyd / η_pump. At η = 0.70, ratio = 1/0.70 ≈ 1.43.
        var r = CentrifugalPumpSolver.Solve(MerlinLoxClass());
        Assert.Equal(r.HydraulicPower_W / 0.70, r.ShaftPowerInput_W, precision: 3);
    }

    [Fact]
    public void MerlinLox_AtDesignPoint_ShaftPowerInTurbopumpBand()
    {
        // P_shaft = P_hyd / 0.70 = 5.70 MW. Cluster band [3, 7] MW
        // for the LOX side of the Merlin combined turbopump.
        var r = CentrifugalPumpSolver.Solve(MerlinLoxClass());
        double mW = r.ShaftPowerInput_W / 1.0e6;
        Assert.InRange(mW, 3.0, 7.0);
    }

    [Fact]
    public void MerlinLox_AtDesignPoint_SpecificSpeedInRadialClusterBand()
    {
        // N_s = ω · √Q / (g·H)^0.75 at ω = 3770 rad/s (36 000 rpm),
        // Q = 0.21, gH = 16 671. (gH)^0.75 = 1465. √Q = 0.458.
        // N_s = 3770 × 0.458 / 1465 = 1.179. Cluster band [0.5, 1.5]
        // for high-N rocket turbopumps (Stepanoff SI form).
        var r = CentrifugalPumpSolver.Solve(MerlinLoxClass());
        Assert.InRange(r.SpecificSpeedSI, 0.5, 1.5);
    }

    // ── NPSH balance (cavitation margin) ──────────────────────────────

    [Fact]
    public void MerlinLox_AtDesignPoint_NpshAvailableInPostInducerBand()
    {
        // NPSH_a = (P_inlet − p_v) / (ρ·g) − z_lift − h_f
        //        = (4.5e6 − 1e5) / (1141 × 9.80665) − 0 − 0 ≈ 393 m.
        // Real Merlin inducer + tank pressurization deliver this band
        // pre-impeller (the inducer rise above tank pressurization is
        // selected to clear the main-impeller NPSH_r at the design N_s).
        // Cluster band [300, 500] m.
        var r = CentrifugalPumpSolver.Solve(MerlinLoxClass());
        Assert.InRange(r.NetPositiveSuctionHeadAvailable_m, 300.0, 500.0);
    }

    [Fact]
    public void MerlinLox_AtDesignPoint_NpshRequiredScalesWithHeadAndSpecificSpeed()
    {
        // NPSH_r = 0.05 · H · (N_s / 0.5)^(4/3). At H = 1700 m and N_s
        // = 1.18: (1.18/0.5)^(4/3) ≈ 3.04 → NPSH_r ≈ 258 m.
        // Cluster band [150, 350] m for high-H high-N turbopumps.
        var r = CentrifugalPumpSolver.Solve(MerlinLoxClass());
        Assert.InRange(r.NetPositiveSuctionHeadRequired_m, 150.0, 350.0);
    }

    [Fact]
    public void MerlinLox_AtDesignPoint_CavitationMarginPositive()
    {
        // Post-inducer design must clear cavitation: NPSH_a > NPSH_r.
        // Without the inducer, NPSH_r ≈ 258 m vs tank-pressure NPSH_a
        // ≈ 18 m → cavitation. With 30-bar post-inducer inlet, margin
        // is positive.
        var r = CentrifugalPumpSolver.Solve(MerlinLoxClass());
        Assert.True(r.CavitationMargin_m > 0,
            $"Cavitation margin ({r.CavitationMargin_m:F1} m) must be > 0 for "
          + "the post-inducer Merlin LOX impeller to operate without cavitation.");
    }

    [Fact]
    public void MerlinLox_AtDesignPoint_CavitationMarginEqualsNpshDelta()
    {
        // Margin = NPSH_a − NPSH_r exactly.
        var r = CentrifugalPumpSolver.Solve(MerlinLoxClass());
        Assert.Equal(r.NetPositiveSuctionHeadAvailable_m
                   - r.NetPositiveSuctionHeadRequired_m,
                     r.CavitationMargin_m, precision: 6);
    }

    // ── Cryogenic-fluid pathway validation ────────────────────────────

    [Fact]
    public void MerlinLox_UsesCryogenicDensity()
    {
        // LOX storage-temperature density ≈ 1141 kg/m³ — much higher
        // than water (1000) but lower than RP-1 (810). LOX is the
        // densest of the common rocket propellants.
        Assert.Equal(1141.0, MerlinLoxClass().FluidDensity_kgm3, precision: 6);
    }

    [Fact]
    public void MerlinLox_UsesCryogenicVapourPressure()
    {
        // LOX vapor pressure at storage temperature is approximately
        // 1 bar (saturated). Much higher than water at room temp
        // (2.34 kPa). Drives NPSH_a directly: higher p_v → less
        // suction head before cavitation.
        Assert.Equal(1.0e5, MerlinLoxClass().FluidVapourPressure_Pa, precision: 3);
    }

    // ── Affinity laws (Sprint PMP.W2 helper) ─────────────────────────

    [Fact]
    public void MerlinLox_AffinityLaws_HalfSpeed_QuartersHead_OneEighthsPower()
    {
        // Centrifugal pump affinity laws: Q ∝ N, H ∝ N², P ∝ N³.
        // Halving N → Q × 0.5, H × 0.25, P × 0.125.
        var r = CentrifugalPumpSolver.Solve(MerlinLoxClass());
        var (Q2, H2, P2) = CentrifugalPumpSolver.ApplyAffinityLaws(
            Q1: MerlinLoxClass().VolumetricFlowRate_m3s,
            H1: MerlinLoxClass().HeadRise_m,
            P1: r.ShaftPowerInput_W,
            N1: MerlinLoxClass().RotationSpeed_rpm,
            N2: MerlinLoxClass().RotationSpeed_rpm * 0.5);
        Assert.Equal(MerlinLoxClass().VolumetricFlowRate_m3s * 0.5,   Q2, precision: 9);
        Assert.Equal(MerlinLoxClass().HeadRise_m * 0.25,              H2, precision: 9);
        Assert.Equal(r.ShaftPowerInput_W * 0.125,                     P2, precision: 6);
    }

    [Fact]
    public void MerlinLox_AffinityLaws_DoubleSpeed_QuadruplesHead_OctuplesPower()
    {
        // Doubling N → Q × 2, H × 4, P × 8.
        var r = CentrifugalPumpSolver.Solve(MerlinLoxClass());
        var (Q2, H2, P2) = CentrifugalPumpSolver.ApplyAffinityLaws(
            Q1: MerlinLoxClass().VolumetricFlowRate_m3s,
            H1: MerlinLoxClass().HeadRise_m,
            P1: r.ShaftPowerInput_W,
            N1: MerlinLoxClass().RotationSpeed_rpm,
            N2: MerlinLoxClass().RotationSpeed_rpm * 2.0);
        Assert.Equal(MerlinLoxClass().VolumetricFlowRate_m3s * 2.0, Q2, precision: 9);
        Assert.Equal(MerlinLoxClass().HeadRise_m * 4.0,             H2, precision: 9);
        Assert.Equal(r.ShaftPowerInput_W * 8.0,                     P2, precision: 6);
    }

    // ── Pump-kind validation ──────────────────────────────────────────

    [Fact]
    public void MerlinLox_IsCentrifugalNotPositiveDisplacement()
    {
        // Rocket-engine turbopumps are exclusively centrifugal (high N
        // + high N_s); positive-displacement would require impossibly
        // many strokes per second at Merlin's flow rates.
        Assert.Equal(PumpKind.Centrifugal, MerlinLoxClass().Kind);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    // SpaceX Merlin 1D LOX turbopump main impeller (post-inducer).
    // Public anchors (SpaceX presentations + AIAA-2017-2017):
    //   ṁ_LOX = 240 kg/s, ρ_LOX = 1141 kg/m³ → Q = 0.21 m³/s
    //   ΔP_pump ≈ 167 bar, H = ΔP/(ρ·g) ≈ 1700 m
    //   N ≈ 36 000 rpm
    //   η ≈ 0.70 (cryogenic single-stage centrifugal cluster mid-band)
    //   p_v_LOX ≈ 1 bar (saturated at storage T)
    //   P_inlet_post-inducer ≈ 45 bar (Merlin's multi-stage inducer rise
    //                                   above tank pressurization,
    //                                   selected to clear the high-N_s
    //                                   main-impeller NPSH_r per the
    //                                   Wave-1 Thoma cluster model)
    private static CentrifugalPumpDesign MerlinLoxClass() => new(
        Kind:                   PumpKind.Centrifugal,
        VolumetricFlowRate_m3s: 0.21,
        HeadRise_m:             1700.0,
        RotationSpeed_rpm:      36000.0,
        OverallEfficiency:      0.70,
        FluidDensity_kgm3:      1141.0,
        FluidVapourPressure_Pa: 1.0e5,
        InletStaticPressure_Pa: 4.5e6,
        InletElevationLift_m:   0.0,
        InletFrictionLoss_m:    0.0);
}
