// HydroTurbineFixture_BieudronPelton.cs — Sprint B.15 published-product
// validation fixture for the high-head Pelton path through the
// Hydroelectric pillar.
//
// Anchors the model to **Bieudron Hydroelectric Power Station**
// (Cleuson-Dixence complex, Valais, Switzerland) — the world-record
// hydroelectric head installation. Public anchor (Grande Dixence SA
// + Voith Hydro published case studies):
//   - 3 turbines × 423.3 MW = 1 269 MW total installed
//   - 1 883 m net hydraulic head (world record)
//   - ~ 25 m³/s flow per turbine at design point
//   - Vertical-axis 6-jet Pelton runners (Voith / Andritz)
//   - Direct-coupled synchronous generators, ~ 98 % η
//   - Commissioned 1998 (offline 2000-2010 after distributor failure,
//     restored to service)
//
// Second anchor for the Hydroelectric pillar — Wave-1 anchor cited in
// the solver is **Three Gorges** (700 MW Francis @ 80 m head). Bieudron
// exercises a completely different operating regime: Pelton kind (vs
// Francis), 23× the head, 1/34× the flow, similar shaft-power scale.
//
// Phase-3 coverage backfill — eighth second-anchor sprint in the
// framing-B Phase 3 pattern after B.3 (AEM), B.9 (Megapack), B.10
// (SunPower PV), B.11 (Ballard FC), B.12 (Haliade-X wind),
// B.13 (GPHS-RTG TEG), B.14 (Beacon Power flywheel). First fixture to
// exercise the Pelton kind in the Wave-1 / Wave-2 pillar (Three Gorges
// is Francis only).

using Voxelforge.Hydroelectric;
using Xunit;

namespace Voxelforge.Tests.Hydroelectric;

public sealed class HydroTurbineFixture_BieudronPelton
{
    // ── Nameplate at design point ────────────────────────────────────

    [Fact]
    public void Bieudron_AtDesignPoint_HydraulicPowerMatchesPotentialEnergy()
    {
        // P_hydraulic = ρ × g × Q × H = 1000 × 9.80665 × 25 × 1883
        // = 461 638 MW × 1e-6 ≈ 461.6 MW exact (formula).
        var r = HydroTurbineSolver.Solve(BieudronClass());
        const double expected = 1000.0 * 9.80665 * 25.0 * 1883.0;
        Assert.Equal(expected, r.HydraulicPower_W, precision: 3);
    }

    [Fact]
    public void Bieudron_AtDesignPoint_ElectricalPowerNearNameplate()
    {
        // Bieudron nameplate per turbine: 423.3 MW DC. The Wave-1 model
        // produces ~ 407 MW (0.90 turbine × 0.98 generator = 0.882
        // overall × 461.6 MW = 407 MW). Real Bieudron achieves ~ 91-92 %
        // overall η, so 423 MW is slightly above the model's
        // conservative cluster anchor. Cluster band [350, 450] MW
        // catches both.
        var r = HydroTurbineSolver.Solve(BieudronClass());
        double mW = r.ElectricalPower_W / 1.0e6;
        Assert.InRange(mW, 350.0, 450.0);
    }

    [Fact]
    public void Bieudron_AtDesignPoint_PeltonEfficiencyMatchesClusterPeak()
    {
        // Pelton cluster peak η_turbine = 0.90 (USBR Engineering
        // Monograph 39 + ASME PTC 18). At Bieudron's 1883 m head, in
        // the validity envelope [200, 2000] m → η_turbine = 0.90 exact.
        var r = HydroTurbineSolver.Solve(BieudronClass());
        const double peltonClusterPeak = 0.90;
        Assert.Equal(peltonClusterPeak, r.HydraulicEfficiency, precision: 6);
    }

    [Fact]
    public void Bieudron_AtDesignPoint_OverallEfficiencyDecomposes()
    {
        // η_overall = η_turbine × η_generator.
        var r = HydroTurbineSolver.Solve(BieudronClass());
        Assert.Equal(r.HydraulicEfficiency * r.GeneratorEfficiency,
                     r.OverallEfficiency, precision: 9);
    }

    [Fact]
    public void Bieudron_AtDesignPoint_GeneratorEfficiencyEchoedFromInput()
    {
        // Bieudron uses modern direct-coupled synchronous generators at
        // η ~ 0.98. The solver must echo the input η_generator unchanged.
        var r = HydroTurbineSolver.Solve(BieudronClass());
        Assert.Equal(0.98, r.GeneratorEfficiency, precision: 9);
    }

    [Fact]
    public void Bieudron_AtDesignPoint_ShaftPowerEqualsTurbineEtaTimesHydraulic()
    {
        var r = HydroTurbineSolver.Solve(BieudronClass());
        Assert.Equal(r.HydraulicEfficiency * r.HydraulicPower_W,
                     r.ShaftPower_W, precision: 6);
    }

    [Fact]
    public void Bieudron_AtDesignPoint_ElectricalPowerEqualsOverallEtaTimesHydraulic()
    {
        var r = HydroTurbineSolver.Solve(BieudronClass());
        Assert.Equal(r.OverallEfficiency * r.HydraulicPower_W,
                     r.ElectricalPower_W, precision: 6);
    }

    // ── Pelton kind + envelope validation ─────────────────────────────

    [Fact]
    public void Bieudron_IsPeltonKind()
    {
        // High-head installations (> 200 m) call for Pelton impulse
        // turbines (impulse runner driven by free-jet of pressurised
        // water). Francis + Kaplan are reaction turbines for medium /
        // low head.
        Assert.Equal(HydroTurbineKind.Pelton, BieudronClass().Kind);
    }

    [Fact]
    public void Bieudron_HeadInsidePeltonEnvelope()
    {
        // Pelton envelope [200, 2000] m. Bieudron at 1883 m is near
        // (but below) the upper bound.
        var r = HydroTurbineSolver.Solve(BieudronClass());
        Assert.True(r.HeadInValidEnvelope,
            "Bieudron head 1883 m must land inside the Pelton envelope [200, 2000].");
    }

    [Fact]
    public void Bieudron_HeadExceedsAllPeltonAlternativesEnvelopes()
    {
        // Bieudron's 1883 m head is well above both Francis (max 700 m)
        // and Kaplan (max 40 m) operating envelopes. Out-of-envelope
        // selection should produce de-rated η.
        var francis = HydroTurbineSolver.Solve(BieudronClass()
            with { Kind = HydroTurbineKind.Francis });
        Assert.False(francis.HeadInValidEnvelope,
            "Bieudron head 1883 m is above Francis envelope max (700 m).");

        var kaplan = HydroTurbineSolver.Solve(BieudronClass()
            with { Kind = HydroTurbineKind.Kaplan });
        Assert.False(kaplan.HeadInValidEnvelope,
            "Bieudron head 1883 m is above Kaplan envelope max (40 m).");
    }

    [Fact]
    public void Bieudron_PeltonKindAutoSelectedForHighHead()
    {
        // SelectKindForHead should pick Pelton for any head ≥ Pelton min
        // (200 m). Bieudron's 1883 m lands cleanly in the Pelton band.
        var selected = HydroTurbineSolver.SelectKindForHead(1883.0);
        Assert.Equal(HydroTurbineKind.Pelton, selected);
    }

    // ── High-head signature: high specific energy per unit volume ─────

    [Fact]
    public void Bieudron_PowerScalesLinearlyWithHead()
    {
        // P_hydraulic ∝ H. Doubling H doubles P_hydraulic at fixed Q.
        var nominal = HydroTurbineSolver.Solve(BieudronClass());
        var halfHead = HydroTurbineSolver.Solve(BieudronClass()
            with { Head_m = BieudronClass().Head_m / 2.0 });
        // Note: half-head 941.5 m is still in Pelton envelope, so
        // hydraulic efficiency stays at peak.
        Assert.Equal(2.0, nominal.HydraulicPower_W / halfHead.HydraulicPower_W,
                     precision: 6);
    }

    [Fact]
    public void Bieudron_PowerScalesLinearlyWithFlow()
    {
        // P_hydraulic ∝ Q. Doubling Q doubles P_hydraulic at fixed H.
        var nominal = HydroTurbineSolver.Solve(BieudronClass());
        var doubleFlow = HydroTurbineSolver.Solve(BieudronClass()
            with { VolumetricFlowRate_m3s = BieudronClass().VolumetricFlowRate_m3s * 2.0 });
        Assert.Equal(2.0,
            doubleFlow.HydraulicPower_W / nominal.HydraulicPower_W,
            precision: 6);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    // Bieudron / Cleuson-Dixence — world-record-head hydroelectric.
    // Public anchor: 3 × 423.3 MW Pelton, 1883 m head, ~ 25 m³/s per
    // turbine, direct-coupled synchronous generator at η ≈ 0.98.
    // Fresh-water density default (1000 kg/m³). Operating point
    // selected at the design-point mid-band of the published
    // operating envelope (turbine can ramp Q between ~ 10-30 m³/s).
    private static HydroTurbineDesign BieudronClass() => new(
        Kind:                   HydroTurbineKind.Pelton,
        Head_m:                 1883.0,
        VolumetricFlowRate_m3s: 25.0,
        GeneratorEfficiency:    0.98);
}
