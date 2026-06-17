// RefrigerationFixture_SandenEcoCuteHeatPump.cs — Sprint A.73 Phase 3
// published-anchor cluster-validation fixture for the Refrigeration
// pillar.
//
// Anchors the Wave-1 Carnot-bounded 2nd-law vapor-compression model
// to the **Sanden ECO-CUTE GUS-A45HOL** R-744 (CO₂ transcritical)
// heat-pump water heater — the canonical residential CO₂ heat-pump
// water-heater cluster anchor (Sanden ECO-CUTE product manual; ASHRAE
// Handbook Refrigeration 2022 chap 3 "CO₂ Systems"; Hwang Y., Radermacher
// R. 1999. "Theoretical evaluation of CO₂ refrigeration cycle." Int. J.
// Refrigeration 22, 217-230; Itoh Y., Saikawa M. 2005. "Heat-pump water
// heaters in Japan: CO₂ transcritical cycle." IEA Heat Pump Centre
// Newsletter 23-3). Cluster anchors at the JIS C 9220 rating point:
//   - Heating mode (R-744 transcritical, gas-cooler-side hot-water side)
//   - Outdoor air (cold reservoir) ≈ 280 K (7 °C JIS C 9220 mid rating)
//   - Hot water delivery (hot reservoir) ≈ 338 K (65 °C target)
//   - Compressor power input ≈ 1.0 kW (residential 4.5 kW heat-pump)
//   - Rated heating COP_heating ≈ 3.5 (Sanden GUS-A45HOL spec; JIS rated
//     3.2-3.8 across the CO₂ heat-pump cluster)
//
// Phase-3 coverage backfill on the Refrigeration pillar — Cohort 3
// rotating-machinery close-out (Compressor A.71 → Pump A.72 →
// Refrigeration A.73). Closes Cohort 3.
//
// Per ADR-036 D3.2, each [Fact] carries a rationale comment with either
// a closed-form derivation or a cluster-anchor citation. The Wave-1
// model is exact for the Carnot bounds + energy balance; the η_2nd-law
// = 0.50 cluster fit for R-744 (Wave-1 RefrigerantRegistry) gives
// real-cycle COP_heating = η × COP_Carnot,cooling + 1 ≈ 3.41 at the
// JIS rating point — within ±5 % of the published 3.5 anchor.
//
// Q3 multi-component physics-calibration watchpoint does NOT apply
// — the published 3.5 COP_heating is well-matched by the single
// component η_2nd-law model at the rating point.

using Voxelforge.Refrigeration;
using Xunit;

namespace Voxelforge.Tests.Refrigeration;

public sealed class RefrigerationFixture_SandenEcoCuteHeatPump
{
    // ── Closed-form Carnot + energy-balance fingerprints ───────────────

    [Fact]
    public void Sanden_DesignPoint_CarnotCoolingCopMatchesClosedForm()
    {
        // COP_Carnot,cooling = T_cold / (T_hot − T_cold).
        // At T_cold = 280, T_hot = 338: ΔT = 58, COP = 280/58 ≈ 4.828.
        var d = SandenEcoCute();
        var r = RefrigerationSolver.Solve(d);
        double expected = d.ColdReservoirTemperature_K
                        / (d.HotReservoirTemperature_K - d.ColdReservoirTemperature_K);
        Assert.Equal(expected, r.CarnotCoolingCop, precision: 6);
    }

    [Fact]
    public void Sanden_DesignPoint_CarnotHeatingExceedsCoolingByExactlyOne()
    {
        // COP_Carnot,heating = T_hot / ΔT = (T_cold + ΔT) / ΔT = T_cold/ΔT
        // + 1 = COP_Carnot,cooling + 1. Closed-form identity from first-
        // law energy balance.
        var r = RefrigerationSolver.Solve(SandenEcoCute());
        Assert.Equal(r.CarnotCoolingCop + 1.0, r.CarnotHeatingCop, precision: 9);
    }

    [Fact]
    public void Sanden_DesignPoint_HeatingExceedsCoolingByExactlyOne()
    {
        // COP_heating = COP_cooling + 1 (Q_hot = Q_cold + W energy balance
        // → divide by W). Holds for the real cycle, not just Carnot bound.
        var r = RefrigerationSolver.Solve(SandenEcoCute());
        Assert.Equal(r.CoolingCop + 1.0, r.HeatingCop, precision: 9);
    }

    [Fact]
    public void Sanden_DesignPoint_ColdSideHeatRemovalMatchesCopTimesPower()
    {
        // Q_cold = COP_cooling · W. Closed form.
        var d = SandenEcoCute();
        var r = RefrigerationSolver.Solve(d);
        Assert.Equal(r.CoolingCop * d.CompressorPowerInput_W,
                     r.ColdSideHeatRemoval_W,
                     precision: 6);
    }

    [Fact]
    public void Sanden_DesignPoint_HotSideHeatDeliveryMatchesEnergyBalance()
    {
        // Q_hot = Q_cold + W. First-law balance — vapor-compression cycle
        // delivers all electrical input PLUS extracted heat to the hot
        // reservoir.
        var d = SandenEcoCute();
        var r = RefrigerationSolver.Solve(d);
        Assert.Equal(r.ColdSideHeatRemoval_W + d.CompressorPowerInput_W,
                     r.HotSideHeatDelivery_W,
                     precision: 6);
    }

    // ── Cluster-anchor bands ──────────────────────────────────────────

    [Fact]
    public void Sanden_DesignPoint_HeatingCopInPublishedClusterBand()
    {
        // Sanden GUS-A45HOL rated COP_heating ≈ 3.5 (JIS C 9220 rating).
        // CO₂ heat-pump water-heater cluster (Itoh & Saikawa 2005)
        // spans 3.2-3.8 across vendors and seasonal-average ratings.
        // The Wave-1 model predicts ~ 3.41 at the JIS rating point with
        // η_2nd-law = 0.50 — inside the cluster band [3.0, 4.0].
        var r = RefrigerationSolver.Solve(SandenEcoCute());
        Assert.InRange(r.HeatingCop, 3.0, 4.0);
    }

    [Fact]
    public void Sanden_DesignPoint_CoolingCopInTranscriticalCo2ClusterBand()
    {
        // Real COP_cooling = η_2nd · COP_Carnot,cooling ≈ 0.50 · 4.83
        // ≈ 2.41. R-744 transcritical cycle COP_cooling is intrinsically
        // lower than subcritical (R-410A, R-134a) because the gas-cooler
        // loses ε vs subcritical condensation — but the high T_glide is
        // ideal for hot-water heating (which boosts COP_heating).
        var r = RefrigerationSolver.Solve(SandenEcoCute());
        Assert.InRange(r.CoolingCop, 2.0, 3.0);
    }

    [Fact]
    public void Sanden_DesignPoint_HotSideHeatDeliveryInResidentialBand()
    {
        // Q_hot ≈ COP_heating · W ≈ 3.41 · 1 000 = 3 410 W. Residential
        // heat-pump water-heater cluster: 3-5 kW thermal output (Sanden
        // GUS family + competitors).
        var r = RefrigerationSolver.Solve(SandenEcoCute());
        Assert.InRange(r.HotSideHeatDelivery_W, 2500.0, 4500.0);
    }

    [Fact]
    public void Sanden_DesignPoint_SecondLawEfficiencyMatchesR744Cluster()
    {
        // Compute η_2nd-law = COP_cooling / COP_Carnot,cooling and check
        // it matches the published R-744 cluster (0.45-0.55, Hwang &
        // Radermacher 1999; Wave-1 RefrigerantRegistry: 0.50).
        var r = RefrigerationSolver.Solve(SandenEcoCute());
        double eta_2nd = r.CoolingCop / r.CarnotCoolingCop;
        Assert.InRange(eta_2nd, 0.45, 0.55);
    }

    // ── Categorical + operating-envelope fingerprints ──────────────────

    [Fact]
    public void Sanden_UsesHeatingMode()
    {
        // Sanden ECO-CUTE is a heat-pump water heater — Heating mode is
        // the design intent. Categorical fingerprint.
        Assert.Equal(RefrigerationMode.Heating, SandenEcoCute().Mode);
    }

    [Fact]
    public void Sanden_UsesR744Refrigerant()
    {
        // CO₂ transcritical is the defining ECO-CUTE feature (natural
        // refrigerant, sub-unity GWP). Categorical fingerprint.
        Assert.Equal(Refrigerant.R744, SandenEcoCute().Refrigerant);
    }

    [Fact]
    public void Sanden_WarmerOutdoorAir_RaisesCop()
    {
        // Raising T_cold from 280 K (7 °C JIS mid) to 295 K (22 °C summer)
        // shrinks ΔT from 58 K to 43 K → COP_Carnot rises from 4.83 to
        // 6.86. Real COP_heating rises correspondingly. Standard heat-
        // pump derating-vs-ambient fingerprint.
        var winter = RefrigerationSolver.Solve(SandenEcoCute());
        var summer = RefrigerationSolver.Solve(
            SandenEcoCute() with { ColdReservoirTemperature_K = 295.0 });
        Assert.True(summer.HeatingCop > winter.HeatingCop,
            $"Summer COP ({summer.HeatingCop:F2}) must exceed winter "
          + $"COP ({winter.HeatingCop:F2}) — narrower ΔT → less compressor "
          + "lift required for the same heat delivery.");
    }

    [Fact]
    public void Sanden_HigherTargetWaterTemp_LowersCop()
    {
        // Raising T_hot from 338 K (65 °C) to 358 K (85 °C max ECO-CUTE
        // output) widens ΔT from 58 to 78 K → COP_Carnot drops from
        // 4.83 to 3.59. Real COP_heating drops accordingly.
        var nominal = RefrigerationSolver.Solve(SandenEcoCute());
        var sanitize = RefrigerationSolver.Solve(
            SandenEcoCute() with { HotReservoirTemperature_K = 358.0 });
        Assert.True(sanitize.HeatingCop < nominal.HeatingCop,
            $"Sanitizing-mode COP ({sanitize.HeatingCop:F2}) must be < "
          + $"nominal ({nominal.HeatingCop:F2}) — wider ΔT lift = more "
          + "compressor work per heat delivery.");
    }

    // ── Wave-2 subcooling + superheat (RFG.W2) ─────────────────────────

    [Fact]
    public void Sanden_Wave2Subcooling_BoostsCopByExpectedFraction()
    {
        // Sprint RFG.W2 cluster fit: COP rises 0.6 % per K of subcooling.
        // 10 K subcooling → +6 % COP boost.
        var nominal = RefrigerationSolver.Solve(SandenEcoCute());
        var subcooled = RefrigerationSolver.Solve(
            SandenEcoCute() with { SubcoolingDepth_K = 10.0 });
        double ratio = subcooled.CoolingCop / nominal.CoolingCop;
        Assert.InRange(ratio, 1.04, 1.08);
    }

    [Fact]
    public void Sanden_Wave2Superheat_ReducesCopByExpectedFraction()
    {
        // Sprint RFG.W2 cluster fit: COP drops 0.2 % per K of superheat.
        // 10 K superheat → −2 % COP penalty.
        var nominal = RefrigerationSolver.Solve(SandenEcoCute());
        var superheated = RefrigerationSolver.Solve(
            SandenEcoCute() with { SuperheatDepth_K = 10.0 });
        double ratio = superheated.CoolingCop / nominal.CoolingCop;
        Assert.InRange(ratio, 0.97, 0.99);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    // Sanden ECO-CUTE GUS-A45HOL — residential R-744 (CO₂ transcritical)
    // heat-pump water heater at the JIS C 9220 rating point (Sanden
    // product manual; ASHRAE Handbook Refrigeration 2022 chap 3 "CO₂
    // Systems"; Itoh & Saikawa 2005 IEA Heat Pump Centre Newsletter
    // 23-3; Hwang & Radermacher 1999 IJR 22, 217-230).
    //   - Heating mode (gas-cooler side delivers hot water)
    //   - Outdoor air 280 K (7 °C JIS mid-rating); hot water 338 K (65 °C)
    //   - Compressor 1.0 kW (residential 4.5 kW thermal-output class)
    //   - Rated heating COP ≈ 3.5 (cluster 3.2-3.8)
    private static RefrigerationDesign SandenEcoCute() => new(
        Mode:                       RefrigerationMode.Heating,
        Refrigerant:                Refrigerant.R744,
        ColdReservoirTemperature_K: 280.0,
        HotReservoirTemperature_K:  338.0,
        CompressorPowerInput_W:     1000.0);
}
