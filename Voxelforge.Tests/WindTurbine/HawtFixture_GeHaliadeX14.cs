// HawtFixture_GeHaliadeX14.cs — Sprint B.12 published-product validation
// fixture for the utility-scale offshore-wind path through the WindTurbine
// pillar.
//
// Anchors the model to **GE Haliade-X 14 MW**, the world's largest
// commercially-deployed offshore wind turbine as of 2024. Public
// datasheet (https://www.ge.com/renewableenergy/wind-energy/offshore-wind/haliade-x-offshore-turbine):
//   - 14 MW rated DC capacity (also 12 MW + 13 MW + 14.7 MW variants)
//   - 220 m rotor diameter → 110 m rotor radius
//   - 3-bladed direct-drive (no gearbox)
//   - Hub height 138 m (above sea level at base)
//   - Cut-in ≈ 3 m/s, rated ≈ 12 m/s, cut-out ≈ 25 m/s
//   - Permanent-magnet synchronous generator (PMSG), ~ 95 % drivetrain η
//
// Second anchor for the WindTurbine pillar — Wave-1 anchor in
// `HawtSolverTests` (and the solver's cluster fit) is the NREL 5 MW
// reference rotor. Haliade-X is roughly 3× the rated power, 2.4× the
// swept area, taller hub, direct-drive instead of geared — a distinct
// regime that validates the Wave-1 BEM-lite Gaussian-C_p(λ) fit on
// next-generation offshore scale.
//
// Sprint B.12 in framing-B Phase 3 coverage backfill — fifth in the
// second-anchor pattern after B.3 (AEM electrolyser), B.9 (Tesla
// Megapack), B.10 (SunPower PV), B.11 (Ballard fuel cell). Pure-
// additive: zero pillar code touched, bit-identity preserved.
//
// Model-vs-product caveat: the Wave-1 model holds λ fixed and doesn't
// pitch-control to limit power at high V. At V > rated, P_elec scales
// as V³ all the way to cut-out (real Haliade-X plateaus at 14 MW from
// rated through cut-out via blade-pitch control). Test bands assert
// nominal rated-point performance + below/above cut-in/out parked
// behaviour; they do NOT validate the rated-power plateau (a Wave-2
// pitch-control feature).

using Voxelforge.WindTurbine;
using Xunit;

namespace Voxelforge.Tests.WindTurbine;

public sealed class HawtFixture_GeHaliadeX14
{
    // ── Nameplate at rated wind speed ─────────────────────────────────

    [Fact]
    public void HaliadeX_AtRatedWindSpeed_ElectricalPowerNearNameplate()
    {
        // Haliade-X 14 MW nameplate. At V = 11 m/s (close to GE's
        // 12 m/s published rated wind) and design λ = 7.5 with
        // η_drivetrain = 0.95, the Wave-1 model produces ≈ 14 MW.
        // The test band [10, 18] MW is a **model-conservative** band
        // around the nameplate — it is NOT a published Haliade-X
        // tolerance. The band absorbs (a) the fixture's choice to
        // evaluate at V = 11 m/s rather than GE's 12 m/s rated point,
        // and (b) the absence of pitch-control limiting in the Wave-1
        // solver. Both flagged in the model-vs-product caveat below.
        var r = HawtSolver.Solve(HaliadeX(), windSpeed_ms: 11.0);
        double mW = r.ElectricalPower_W / 1.0e6;
        Assert.InRange(mW, 10.0, 18.0);
    }

    [Fact]
    public void HaliadeX_AtRatedWindSpeed_PowerCoefficientAtPeak()
    {
        // The Wave-1 model holds λ fixed at design (variable-speed
        // turbine assumption). At design λ = 7.5 the Gaussian-C_p(λ)
        // fit is at its peak ≈ 0.48 — Jonkman 2009 NREL 5 MW cluster.
        var r = HawtSolver.Solve(HaliadeX(), windSpeed_ms: 11.0);
        Assert.Equal(HawtSolver.PeakPowerCoefficient, r.PowerCoefficient,
                     precision: 6);
    }

    [Fact]
    public void HaliadeX_AtRatedWindSpeed_BelowBetzLimit()
    {
        // Sanity invariant: C_p ≤ 16/27 always (actuator-disk limit).
        var r = HawtSolver.Solve(HaliadeX(), windSpeed_ms: 11.0);
        Assert.True(r.PowerCoefficient < HawtSolver.BetzLimit,
            $"C_p ({r.PowerCoefficient:F4}) exceeds Betz limit "
          + $"{HawtSolver.BetzLimit:F4}.");
    }

    [Fact]
    public void HaliadeX_AtRatedWindSpeed_TipSpeedInClusterBand()
    {
        // v_tip = λ × V = 7.5 × 11 = 82.5 m/s. Modern utility-scale
        // turbines cap tip speed at ~ 80-90 m/s to limit aeroacoustic
        // emissions + blade-leading-edge erosion.
        var r = HawtSolver.Solve(HaliadeX(), windSpeed_ms: 11.0);
        Assert.InRange(r.TipSpeed_ms, 70.0, 95.0);
    }

    [Fact]
    public void HaliadeX_AtRatedWindSpeed_RotorThrustInOffshoreBand()
    {
        // Thrust at rated wind for a 14 MW class turbine lands ~ 1.4 MN.
        // Cluster band [0.8, 2.0] MN — accommodates the rated-wind
        // scatter across the offshore class.
        var r = HawtSolver.Solve(HaliadeX(), windSpeed_ms: 11.0);
        double mN = r.RotorThrust_N / 1.0e6;
        Assert.InRange(mN, 0.8, 2.0);
    }

    [Fact]
    public void HaliadeX_AtRatedWindSpeed_DrivetrainEfficiencyApplied()
    {
        // P_elec = η_drivetrain × P_rotor exactly.
        var r = HawtSolver.Solve(HaliadeX(), windSpeed_ms: 11.0);
        Assert.Equal(0.95, r.ElectricalPower_W / r.RotorPower_W, precision: 6);
    }

    // ── Swept area / geometry ─────────────────────────────────────────

    [Fact]
    public void HaliadeX_SweptArea_MatchesRotorGeometry()
    {
        // Rotor diameter 220 m → R = 110 m → A = π × R² = 38 013 m².
        // The Haliade-X swept area exceeds 4 football fields (38 000 m²).
        const double expected = System.Math.PI * 110.0 * 110.0;
        Assert.Equal(expected, HaliadeX().SweptArea_m2, precision: 3);
    }

    [Fact]
    public void HaliadeX_AtRatedWindSpeed_SpecificPowerInOffshoreCluster()
    {
        // Specific power = P_rated / A. For Haliade-X: 14 MW / 38 013 m²
        // ≈ 368 W/m². Modern offshore turbines cluster 300-400 W/m²
        // (a lower specific power = larger rotor = higher capacity
        // factor in low-wind regimes).
        var r = HawtSolver.Solve(HaliadeX(), windSpeed_ms: 11.0);
        double specificPower_W_m2 = r.ElectricalPower_W / HaliadeX().SweptArea_m2;
        Assert.InRange(specificPower_W_m2, 250.0, 500.0);
    }

    // ── Cut-in / cut-out parked behaviour ─────────────────────────────

    [Fact]
    public void HaliadeX_BelowCutIn_ParkedNoPower()
    {
        // V = 2 m/s is below the 3 m/s cut-in → turbine parked,
        // P_elec = 0, C_p = 0, T = 0.
        var r = HawtSolver.Solve(HaliadeX(), windSpeed_ms: 2.0);
        Assert.Equal(0.0, r.ElectricalPower_W,   precision: 9);
        Assert.Equal(0.0, r.PowerCoefficient,    precision: 9);
        Assert.Equal(0.0, r.RotorThrust_N,       precision: 9);
        Assert.Equal(0.0, r.TipSpeed_ms,         precision: 9);
    }

    [Fact]
    public void HaliadeX_AboveCutOut_ParkedNoPower()
    {
        // V = 26 m/s is above the 25 m/s cut-out (storm-protect) →
        // turbine parked. Wind kinetic energy is still flowing through
        // the disk (AvailablePower > 0) but the turbine extracts none.
        var r = HawtSolver.Solve(HaliadeX(), windSpeed_ms: 26.0);
        Assert.True(r.AvailablePower_W > 0,
            "Available wind power is non-zero above cut-out (V³ flux exists)");
        Assert.Equal(0.0, r.ElectricalPower_W, precision: 9);
        Assert.Equal(0.0, r.PowerCoefficient,  precision: 9);
        Assert.Equal(0.0, r.RotorThrust_N,     precision: 9);
    }

    [Fact]
    public void HaliadeX_OperatingBandSpansCommercialRange()
    {
        // Modern offshore turbines cut in at 3-4 m/s + cut out at
        // 25-30 m/s. Haliade-X-class lands at 3 + 25.
        var design = HaliadeX();
        Assert.InRange(design.CutInWindSpeed_ms,  2.5,  5.0);
        Assert.InRange(design.CutOutWindSpeed_ms, 22.0, 30.0);
    }

    // ── Topology validation ───────────────────────────────────────────

    [Fact]
    public void HaliadeX_IsThreeBladedHawt()
    {
        Assert.Equal(WindTurbineKind.HorizontalAxis, HaliadeX().Kind);
        Assert.Equal(3, HaliadeX().BladeCount);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    // GE Haliade-X 14 MW — utility-scale offshore wind turbine.
    // Public datasheet specs: 220 m rotor diameter (R = 110 m), 3 blades,
    // 138 m hub height, cut-in 3 m/s, cut-out 25 m/s. Rated wind speed
    // ~ 12 m/s nominal; the Wave-1 cluster fit produces 14 MW at V = 11
    // m/s and λ = 7.5 (variable-speed PMSG drivetrain at η = 0.95).
    private static HawtDesign HaliadeX() => new(
        Kind:                          WindTurbineKind.HorizontalAxis,
        RotorRadius_m:                 110.0,
        BladeCount:                    3,
        HubHeight_m:                   138.0,
        DesignWindSpeed_ms:            11.0,
        DesignTipSpeedRatio:           7.5,
        GearboxAndGeneratorEfficiency: 0.95,
        CutInWindSpeed_ms:             3.0,
        CutOutWindSpeed_ms:            25.0);
}
