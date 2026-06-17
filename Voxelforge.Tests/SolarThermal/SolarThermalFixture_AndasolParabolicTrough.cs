// SolarThermalFixture_AndasolParabolicTrough.cs — Sprint A.77 Phase 3
// published-anchor cluster-validation fixture for the SolarThermal
// pillar.
//
// Anchors the Wave-1 Hottel-Whillier-Bliss collector model to the
// **Andasol-1 parabolic-trough single Solar Collector Assembly (SCA)**
// — the canonical utility-scale CSP cluster anchor (Wave-1
// SolarCollectorDesign.cs header explicitly cites Andasol-class;
// Burkholder & Kutscher 2009 NREL/TP-550-45633 SAM model parameters;
// Geyer & Pitz-Paal 2002 *Solar Engineering* Andasol-1 design data;
// Mosbah et al. 2017 *Energy Procedia* 105 Andasol operating point).
//
// Cluster anchors for a single SCA at the Andasol-1 design point:
//   - Kind: ParabolicTrough (line-focus, evacuated-tube receiver)
//   - Aperture area ≈ 830 m² per SCA (Andasol Eurotrough ET-150
//     geometry, 144 m × 5.77 m aperture-width)
//   - DNI ≈ 850 W/m² (Andalusia mid-day annual-average cluster anchor;
//     Mosbah 2017 Table 2)
//   - Collector operating T ≈ 393 °C (HTF VP-1 thermal oil mid-loop;
//     Burkholder 2009)
//   - Ambient T ≈ 25 °C
//   - F_R = 0.85, τα = 0.85, U_L = 0.5 W/(m²·K) (Wave-1 ParabolicTrough
//     cluster; matches Burkholder NREL parameters within ± 10 %)
//
// Phase-3 coverage backfill on the SolarThermal pillar — **CLOSES
// Cohort 4 + closes Track C.1** (HeatExchanger A.66 ✓ → Radiator A.68 ✓
// → HeatPipe A.69 ✓ → Compressor A.71 ✓ → Pump A.72 ✓ →
// Refrigeration A.73 ✓ → Tankage A.74 ✓ → Aerostructures A.75 ✓ →
// ChemicalReactor A.76 ✓ → SolarThermal A.77 ✓ — all 10 Wave-1
// second-anchor fixtures landed).
//
// The Wave-1 Hottel-Whillier-Bliss closed-form model captures
// (Q_incident, Q_absorbed, Q_loss, Q_useful, η) exactly at the design
// operating point; per-section receiver heat-loss variations,
// transient warm-up dynamics, and HTF property variations are deferred
// to ST.W2+. Per ADR-036 D3.2, the file-header rationale documents the
// Wave-1 model-vs-Andasol-actual gap (model assumes constant F_R + τα
// + U_L at all operating points; real Andasol shows ~ 5 % seasonal
// drift in F_R · τα as solar incidence angle varies). Q3 multi-
// component physics-calibration watchpoint does NOT apply — the
// Hottel-Whillier-Bliss formulation is exact for a single-receiver
// architecture.

using Voxelforge.SolarThermal;
using Xunit;

namespace Voxelforge.Tests.SolarThermal;

public sealed class SolarThermalFixture_AndasolParabolicTrough
{
    // ── Closed-form Hottel-Whillier-Bliss fingerprints ─────────────────

    [Fact]
    public void Andasol_DesignPoint_IncidentSolarPowerMatchesGTimesArea()
    {
        // Q_incident = G · A. Closed form.
        // 850 × 830 = 705 500 W = 705.5 kW.
        var d = AndasolSca();
        var r = SolarCollectorSolver.Solve(d);
        Assert.Equal(d.DirectNormalIrradiance_W_m2 * d.ApertureArea_m2,
                     r.IncidentSolarPower_W,
                     precision: 3);
    }

    [Fact]
    public void Andasol_DesignPoint_AbsorbedSolarMatchesTauAlphaTimesIncident()
    {
        // Q_absorbed = τα · G · A. ParabolicTrough τα = 0.85.
        // 0.85 × 705 500 ≈ 599 675 W ≈ 600 kW.
        var d = AndasolSca();
        var r = SolarCollectorSolver.Solve(d);
        var props = SolarCollectorRegistry.For(d.Kind);
        Assert.Equal(props.TransmittanceAbsorptanceProduct
                       * r.IncidentSolarPower_W,
                     r.AbsorbedSolarPower_W,
                     precision: 3);
    }

    [Fact]
    public void Andasol_DesignPoint_ThermalLossMatchesULTimesAreaTimesDeltaT()
    {
        // Q_loss = U_L · A · (T_collector − T_ambient). ParabolicTrough
        // U_L = 0.5 W/(m²·K). ΔT = 393 − 25 = 368 K. Q_loss = 0.5 ·
        // 830 · 368 ≈ 152 720 W ≈ 153 kW.
        var d = AndasolSca();
        var r = SolarCollectorSolver.Solve(d);
        var props = SolarCollectorRegistry.For(d.Kind);
        double dT = d.CollectorTemperature_C - d.AmbientTemperature_C;
        Assert.Equal(props.OverallLossCoefficient_W_m2K * d.ApertureArea_m2 * dT,
                     r.ThermalLossPower_W,
                     precision: 3);
    }

    [Fact]
    public void Andasol_DesignPoint_UsefulHeatMatchesHottelWhillierBliss()
    {
        // Q_useful = F_R · [τα·G − U_L·ΔT] · A. ParabolicTrough F_R =
        // 0.85. At anchor: Q_useful = 0.85 × (599 675 − 152 720) =
        // 0.85 × 446 955 ≈ 379 912 W ≈ 380 kW.
        var d = AndasolSca();
        var r = SolarCollectorSolver.Solve(d);
        var props = SolarCollectorRegistry.For(d.Kind);
        double dT = d.CollectorTemperature_C - d.AmbientTemperature_C;
        double absorbedPerArea = props.TransmittanceAbsorptanceProduct
                               * d.DirectNormalIrradiance_W_m2;
        double lossPerArea = props.OverallLossCoefficient_W_m2K * dT;
        double expected = props.HeatRemovalFactor
                        * (absorbedPerArea - lossPerArea)
                        * d.ApertureArea_m2;
        Assert.Equal(expected, r.UsefulHeatPower_W, precision: 3);
    }

    [Fact]
    public void Andasol_DesignPoint_EfficiencyMatchesUsefulOverIncident()
    {
        // η = Q_useful / Q_incident. At Andasol anchor: 379 912 / 705 500
        // ≈ 0.539 (54 % thermal efficiency — matches Burkholder NREL
        // operating cluster).
        var r = SolarCollectorSolver.Solve(AndasolSca());
        Assert.Equal(r.UsefulHeatPower_W / r.IncidentSolarPower_W,
                     r.CollectorEfficiency,
                     precision: 9);
    }

    // ── Cluster-anchor band fingerprints ───────────────────────────────

    [Fact]
    public void Andasol_DesignPoint_EfficiencyInCspClusterBand()
    {
        // Parabolic-trough CSP thermal efficiency cluster at design
        // operating point: η ∈ [0.4, 0.7] (Burkholder & Kutscher 2009
        // NREL Table 4; Geyer & Pitz-Paal 2002 Andasol design tables).
        // Wave-1 model lands ~ 0.54 at the Andasol anchor.
        var r = SolarCollectorSolver.Solve(AndasolSca());
        Assert.InRange(r.CollectorEfficiency, 0.4, 0.7);
    }

    [Fact]
    public void Andasol_DesignPoint_UsefulPowerInSingleScaClusterBand()
    {
        // Single SCA at Andasol design point delivers ~ 380 kW thermal.
        // Cluster band [250, 500] kW captures DNI seasonal variation
        // (DNI ≈ 600–1000 W/m² across Andalusia annual envelope) and
        // F_R · τα cluster scatter (Burkholder 2009 Fig 7).
        var r = SolarCollectorSolver.Solve(AndasolSca());
        Assert.InRange(r.UsefulHeatPower_W, 250_000.0, 500_000.0);
    }

    [Fact]
    public void Andasol_DesignPoint_NetUsefulHeatStrictlyPositive()
    {
        // At the design operating point, the trough MUST be net heat-
        // delivering (Q_useful > 0). A non-positive Q_useful would mean
        // the receiver loses more heat than the absorbed solar can
        // sustain — equivalent to operating the field at night.
        var r = SolarCollectorSolver.Solve(AndasolSca());
        Assert.True(r.UsefulHeatPower_W > 0,
            $"Q_useful ({r.UsefulHeatPower_W / 1000:F0} kW) must be > 0 "
          + "at the Andasol-class design operating point.");
    }

    // ── Categorical + envelope fingerprints ────────────────────────────

    [Fact]
    public void Andasol_UsesParabolicTroughKind()
    {
        // Andasol-1 is the canonical utility-scale parabolic-trough
        // CSP plant (50 MWe net, Eurotrough ET-150 collector geometry;
        // Geyer & Pitz-Paal 2002).
        Assert.Equal(SolarCollectorKind.ParabolicTrough, AndasolSca().Kind);
    }

    [Fact]
    public void Andasol_DesignPoint_TemperatureInsideValidEnvelope()
    {
        // T_collector = 393 °C < ParabolicTrough cluster MaxOperatingTemperature
        // (450 °C). Operating envelope flag should be true.
        var r = SolarCollectorSolver.Solve(AndasolSca());
        Assert.True(r.OperatingTemperatureInValidEnvelope,
            "Andasol-class T_collector (393 °C) must sit inside the "
          + "ParabolicTrough validity envelope (≤ 450 °C).");
    }

    // ── Operating-envelope sensitivities ───────────────────────────────

    [Fact]
    public void Andasol_HigherDni_RaisesUsefulHeat()
    {
        // Q_useful = F_R · (τα·G − U_L·ΔT) · A. At fixed ΔT, raising G
        // monotonically raises Q_useful (linear scaling with G). Doubling
        // DNI from 850 to 1700 W/m² should roughly double Q_useful when
        // U_L·ΔT term is much smaller than τα·G (true here at 153 kW
        // loss vs 600 kW absorbed).
        var nominal = SolarCollectorSolver.Solve(AndasolSca());
        var highDni = SolarCollectorSolver.Solve(
            AndasolSca() with { DirectNormalIrradiance_W_m2 = 1700.0 });
        Assert.True(highDni.UsefulHeatPower_W > nominal.UsefulHeatPower_W,
            $"Q_useful must rise monotonically with DNI. Nominal "
          + $"{nominal.UsefulHeatPower_W / 1000:F0} kW; doubled-DNI "
          + $"{highDni.UsefulHeatPower_W / 1000:F0} kW.");
    }

    [Fact]
    public void Andasol_HigherCollectorTemp_LowersUsefulHeat()
    {
        // At fixed G, raising T_collector raises Q_loss (U_L·ΔT) which
        // reduces Q_useful. Going from 393 °C to 440 °C (near upper
        // ParabolicTrough envelope) drops Q_useful — the thermal-loss
        // penalty for higher-T operation.
        var nominal = SolarCollectorSolver.Solve(AndasolSca());
        var hotter  = SolarCollectorSolver.Solve(
            AndasolSca() with { CollectorTemperature_C = 440.0 });
        Assert.True(hotter.UsefulHeatPower_W < nominal.UsefulHeatPower_W,
            $"Q_useful must drop with hotter T_collector (higher U_L·ΔT). "
          + $"Nominal {nominal.UsefulHeatPower_W / 1000:F0} kW vs hotter "
          + $"{hotter.UsefulHeatPower_W / 1000:F0} kW.");
    }

    [Fact]
    public void Andasol_NightTime_ZeroDniGivesZeroUsefulHeat()
    {
        // At G = 0 (night), the Hottel-Whillier-Bliss model gives
        // Q_useful = F_R · (0 − U_L·ΔT) · A = negative; the solver
        // clamps at 0 per the result-record convention (a collector
        // running hotter than its irradiance can sustain is net-losing
        // heat; the absorber side of the loop simply stops contributing).
        var night = SolarCollectorSolver.Solve(
            AndasolSca() with { DirectNormalIrradiance_W_m2 = 0.0 });
        Assert.Equal(0.0, night.UsefulHeatPower_W);
        Assert.Equal(0.0, night.CollectorEfficiency, precision: 9);
    }

    [Fact]
    public void Andasol_FlatPlate_LowerEfficiency_Than_ParabolicTrough()
    {
        // FlatPlate U_L = 5 W/(m²·K) (10× higher than ParabolicTrough's
        // 0.5); at 393 °C collector T the loss term dominates → would
        // give negative Q_useful (clamped to 0). FlatPlate cluster
        // validity tops out at 100 °C — operating it at 393 °C is far
        // outside its envelope. The model still computes a number, but
        // η drops to 0.
        var trough = SolarCollectorSolver.Solve(AndasolSca());
        var flat   = SolarCollectorSolver.Solve(
            AndasolSca() with { Kind = SolarCollectorKind.FlatPlate });
        Assert.True(flat.CollectorEfficiency < trough.CollectorEfficiency,
            $"FlatPlate η ({flat.CollectorEfficiency:F3}) must be < "
          + $"ParabolicTrough η ({trough.CollectorEfficiency:F3}) when "
          + "operated at 393 °C — high U_L makes flat-plate net-losing "
          + "at this T.");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    // Andasol-1 single SCA (Solar Collector Assembly) — Wave-1 header
    // cluster anchor; Burkholder & Kutscher 2009 NREL/TP-550-45633 SAM
    // model parameters; Geyer & Pitz-Paal 2002 *Solar Engineering* Andasol
    // design data; Mosbah et al. 2017 *Energy Procedia* 105.
    //   - Kind: ParabolicTrough (line-focus, evacuated-tube receiver)
    //   - Aperture: 830 m² (Eurotrough ET-150 geometry: 144 m × 5.77 m)
    //   - DNI: 850 W/m² (Andalusia mid-day annual-average cluster)
    //   - T_collector: 393 °C (HTF VP-1 thermal oil mid-loop)
    //   - T_ambient: 25 °C
    private static SolarCollectorDesign AndasolSca() => new(
        Kind:                          SolarCollectorKind.ParabolicTrough,
        ApertureArea_m2:               830.0,
        DirectNormalIrradiance_W_m2:   850.0,
        CollectorTemperature_C:        393.0,
        AmbientTemperature_C:          25.0);
}
