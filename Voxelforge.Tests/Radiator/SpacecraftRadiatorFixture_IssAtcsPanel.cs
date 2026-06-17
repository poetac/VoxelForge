// SpacecraftRadiatorFixture_IssAtcsPanel.cs — Sprint A.68 Phase 3
// published-anchor cluster-validation fixture for the Radiator pillar.
//
// Anchors the Wave-1+2 spacecraft flat-panel radiator model to the
// **International Space Station Active Thermal Control System (ATCS)
// deployable radiator panel** (Park C., Cole G. 2014. "An Overview of
// the International Space Station Cooling System." AIAA 2014-3414;
// Gilmore D.G. 2002. "Spacecraft Thermal Control Handbook," vol 1
// chap 5; NASA ISS Thermal Control System spec). The ATCS cluster
// anchors:
//   - 6 deployable two-sided panels per cluster
//   - Single-face area ≈ 84 m² (24.7 m × 3.4 m honeycomb-aluminum panel)
//   - Two-sided deployable construction (radiates from both faces)
//   - Ammonia-loop operating temperature 273-310 K (panel surface
//     ≈ 275 K in nominal heat-rejection mode)
//   - LEO effective sink temperature ≈ 240 K (accounts for Earth-IR
//     contribution + albedo-driven thermal averaging; Gilmore 2002 §5.2)
//   - Z-93 white-paint coating: ε ≈ 0.84 BOL, α ≈ 0.18 BOL (cluster
//     0.15-0.22; degrades to α ≈ 0.27 EOL after 10 years UV exposure)
//   - Orbital-averaged direct + albedo solar load ≈ 200 W/m² on the
//     sun-facing side (40 % sunlit duty cycle × 1361 W/m² × cosθ_avg)
//   - Cluster per-panel rejection ≈ 14 kW thermal at the 280 K design
//     point (Park & Cole 2014 Table 2)
//
// Phase-3 coverage backfill on the Radiator pillar — second second-
// anchor fixture in the framing-B Phase 3 thermal-management triple
// (after A.66 HeatExchanger). Uses the Wave-2 TwoSidedDeployable
// kind, which doubles the radiative area while keeping single-side
// solar absorption (per the solver header convention).
//
// Per ADR-036 D3.2, each [Fact] carries a rationale comment naming
// the published anchor + the cluster scatter that motivates the
// Assert.InRange band. The Wave-1+2 model is exact for the Stefan-
// Boltzmann balance (no model-vs-hardware gap to document) — the
// per-quantity bands reflect cluster scatter (paint-property tolerance,
// sink-temperature seasonal variation, orbital-average solar load).

using Voxelforge.Radiator;
using Xunit;

namespace Voxelforge.Tests.Radiator;

public sealed class SpacecraftRadiatorFixture_IssAtcsPanel
{
    // ── Per-component heat balance ─────────────────────────────────────

    [Fact]
    public void IssAtcs_DesignPoint_GrossRadiatedHeatInClusterBand()
    {
        // Q_emitted = ε · σ · A_eff · T_panel⁴.
        // A_eff = 2 · 84 = 168 m² (TwoSidedDeployable).
        // At T_panel = 275 K, ε = 0.84: σ·T⁴ = 324 W/m² →
        // Q_emitted ≈ 0.84 × 168 × 324 = 45.7 kW.
        // Cluster scatter ±20 % from paint emissivity ± panel-T variation
        // → band [35, 60] kW.
        var r = SpacecraftRadiatorSolver.Solve(IssAtcsPanel());
        Assert.InRange(r.GrossRadiatedHeat_W, 35_000.0, 60_000.0);
    }

    [Fact]
    public void IssAtcs_DesignPoint_SinkBackradiationInClusterBand()
    {
        // Q_back = ε · σ · A_eff · T_sink⁴.
        // At T_sink = 240 K: σ·T⁴ = 188 W/m² →
        // Q_back ≈ 0.84 × 168 × 188 = 26.5 kW.
        // Cluster scatter ±25 % from LEO sink-T seasonal variation
        // (eclipse ≈ 200 K, full Earth-IR exposure ≈ 280 K) → band
        // [15, 35] kW.
        var r = SpacecraftRadiatorSolver.Solve(IssAtcsPanel());
        Assert.InRange(r.SinkBackradiation_W, 15_000.0, 35_000.0);
    }

    [Fact]
    public void IssAtcs_DesignPoint_ParasiticSolarHeatInClusterBand()
    {
        // Q_solar = α · A_single · G_solar.
        // At α = 0.18, A_single = 84 m², G = 200 W/m² (orbital average) →
        // Q_solar ≈ 0.18 × 84 × 200 = 3.02 kW.
        // BOL/EOL Z-93 degradation widens α to [0.15, 0.27] → band
        // [2, 6] kW.
        var r = SpacecraftRadiatorSolver.Solve(IssAtcsPanel());
        Assert.InRange(r.ParasiticSolarHeat_W, 1_500.0, 6_000.0);
    }

    [Fact]
    public void IssAtcs_DesignPoint_NetHeatRejectionInClusterBand()
    {
        // Q_net = Q_emitted - Q_back - Q_solar.
        // ≈ 45.7 - 26.5 - 3.0 = 16.2 kW per panel.
        // Park & Cole 2014 cite 14 kW per-panel anchor at the design
        // point; cluster scatter ±30 % spans paint + sink + solar
        // averaging → band [8, 25] kW.
        var r = SpacecraftRadiatorSolver.Solve(IssAtcsPanel());
        Assert.InRange(r.NetHeatRejectionRate_W, 8_000.0, 25_000.0);
    }

    [Fact]
    public void IssAtcs_DesignPoint_HeatRejectionDensityInClusterBand()
    {
        // Q_density = Q_net / A_single_panel ≈ 16200 / 84 ≈ 193 W/m².
        // Spacecraft flat-panel radiator cluster typically 150-300 W/m²
        // (Gilmore 2002 §5.4 cluster anchors).
        var r = SpacecraftRadiatorSolver.Solve(IssAtcsPanel());
        Assert.InRange(r.HeatRejectionDensity_W_m2, 100.0, 350.0);
    }

    [Fact]
    public void IssAtcs_DesignPoint_AlphaOverEpsilonInZ93Cluster()
    {
        // α/ε is the design figure of merit for paint selection.
        // Z-93 BOL: 0.18 / 0.84 = 0.214. Z-93 EOL: 0.27 / 0.84 = 0.321.
        // OSR (optical solar reflector): 0.08 / 0.79 = 0.101. Cluster
        // [0.15, 0.30] covers Z-93 BOL → mid-life.
        var r = SpacecraftRadiatorSolver.Solve(IssAtcsPanel());
        Assert.InRange(r.AlphaOverEpsilonRatio, 0.15, 0.30);
    }

    // ── Wave-2 + categorical fingerprints ──────────────────────────────

    [Fact]
    public void IssAtcs_UsesTwoSidedDeployableKind()
    {
        // ISS ATCS panels deploy from the truss and radiate from both
        // faces; the Wave-2 TwoSidedDeployable kind must be selected.
        Assert.Equal(RadiatorKind.TwoSidedDeployable, IssAtcsPanel().Kind);
    }

    [Fact]
    public void IssAtcs_TwoSidedDeployable_ExceedsFlatPanelByMoreThanDoubling()
    {
        // TwoSidedDeployable doubles A_eff for emission + back-radiation
        // but keeps A_single for solar absorption (sun-facing side only).
        // Therefore Q_net_2side = 2·(Q_emit_single - Q_back_single) -
        // Q_solar; Q_net_flat = Q_emit_single - Q_back_single - Q_solar.
        // Let X = Q_emit_single - Q_back_single. Ratio = (2X - Q_solar)
        // / (X - Q_solar). For ISS-ATCS-class design (X ≈ 9.6 kW,
        // Q_solar ≈ 3 kW), ratio ≈ 16.2 / 6.6 ≈ 2.46 — strictly above
        // 2.0× because the fixed solar parasitic disproportionately
        // bites the smaller FlatPanel rejection. Cluster band [2.0, 3.0].
        var twoSided = SpacecraftRadiatorSolver.Solve(IssAtcsPanel());
        var flatPanel = SpacecraftRadiatorSolver.Solve(
            IssAtcsPanel() with { Kind = RadiatorKind.FlatPanel });
        double ratio = twoSided.NetHeatRejectionRate_W
                     / flatPanel.NetHeatRejectionRate_W;
        Assert.InRange(ratio, 2.0, 3.0);
    }

    // ── Operating-envelope fingerprints ────────────────────────────────

    [Fact]
    public void IssAtcs_Eclipse_NetRejectionExceedsSunlit()
    {
        // In eclipse (G_solar = 0), the parasitic solar load disappears
        // and the panel rejects more heat. This is a fundamental design-
        // mode fingerprint — radiators run hotter / reject more in
        // eclipse and trim back in full sunlight.
        var sunlit  = SpacecraftRadiatorSolver.Solve(IssAtcsPanel());
        var eclipse = SpacecraftRadiatorSolver.Solve(
            IssAtcsPanel() with { IncidentSolarFlux_W_m2 = 0.0 });
        Assert.True(eclipse.NetHeatRejectionRate_W
                  > sunlit.NetHeatRejectionRate_W,
            $"Eclipse Q_net ({eclipse.NetHeatRejectionRate_W:F0} W) must "
          + $"exceed sunlit Q_net ({sunlit.NetHeatRejectionRate_W:F0} W) — "
          + "loss of solar parasitic raises capacity.");
    }

    [Fact]
    public void IssAtcs_ColderSink_IncreasesNetRejection()
    {
        // Deep-space sink (T_sink = 3 K) eliminates back-radiation,
        // raising Q_net by ~ Q_back ≈ 26.5 kW. Useful for GEO / cis-
        // lunar missions where Earth-IR is negligible.
        var leo       = SpacecraftRadiatorSolver.Solve(IssAtcsPanel());
        var deepSpace = SpacecraftRadiatorSolver.Solve(
            IssAtcsPanel() with { SinkTemperature_K = 3.0 });
        Assert.True(deepSpace.NetHeatRejectionRate_W
                  > leo.NetHeatRejectionRate_W + 15_000.0,
            $"Deep-space Q_net ({deepSpace.NetHeatRejectionRate_W:F0} W) "
          + $"must exceed LEO Q_net ({leo.NetHeatRejectionRate_W:F0} W) "
          + "by ≥ 15 kW (back-radiation elimination at near-3 K sink).");
    }

    [Fact]
    public void IssAtcs_HotterPanel_IncreasesRejection_T4Scaling()
    {
        // Q_emitted ∝ T⁴. Raising T_panel from 275 K to 310 K (high end
        // of ATCS operating range) should raise Q_emitted by
        // (310/275)⁴ ≈ 1.62×.
        var nominal = SpacecraftRadiatorSolver.Solve(IssAtcsPanel());
        var highT   = SpacecraftRadiatorSolver.Solve(
            IssAtcsPanel() with { OperatingTemperature_K = 310.0 });
        double ratio = highT.GrossRadiatedHeat_W / nominal.GrossRadiatedHeat_W;
        Assert.InRange(ratio, 1.50, 1.75);
    }

    [Fact]
    public void IssAtcs_DesignPoint_NetRejectionStrictlyPositive()
    {
        // At nominal operating point, the panel MUST be net-rejecting
        // (Q_emitted > Q_back + Q_solar). A non-positive Q_net would
        // mean the panel can't reject the heat-load mission demands —
        // a non-functional design.
        var r = SpacecraftRadiatorSolver.Solve(IssAtcsPanel());
        Assert.True(r.NetHeatRejectionRate_W > 0,
            $"ISS ATCS panel must net-reject heat at the nominal design "
          + $"point. Got Q_net = {r.NetHeatRejectionRate_W:F0} W.");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    // ISS ATCS deployable radiator panel — published anchor for the
    // large-scale LEO two-sided deployable radiator cluster (Park & Cole
    // 2014, AIAA 2014-3414; Gilmore 2002 Spacecraft Thermal Control
    // Handbook vol 1 chap 5; NASA ISS Thermal Control System spec).
    //   - Two-sided deployable construction (Wave-2 RadiatorKind)
    //   - Single-face area 84 m² (24.7 m × 3.4 m honeycomb panel)
    //   - Operating panel temperature 275 K (ammonia loop, mid-operating)
    //   - LEO effective sink 240 K (Gilmore 2002 §5.2 cluster anchor)
    //   - Z-93 white-paint BOL: ε = 0.84, α = 0.18
    //   - Orbital-averaged solar flux 200 W/m² on the sun-facing side
    private static SpacecraftRadiatorDesign IssAtcsPanel() => new(
        Kind:                    RadiatorKind.TwoSidedDeployable,
        PanelArea_m2:            84.0,
        OperatingTemperature_K:  275.0,
        SinkTemperature_K:       240.0,
        Emissivity:              0.84,
        SolarAbsorptivity:       0.18,
        IncidentSolarFlux_W_m2:  200.0);
}
