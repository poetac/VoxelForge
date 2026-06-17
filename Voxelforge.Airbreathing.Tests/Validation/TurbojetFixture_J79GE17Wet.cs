// TurbojetFixture_J79GE17Wet.cs — validation fixture for the GE J79-GE-17
// augmented turbojet, Wave-2 afterburner sub-task (issue #428 sub-task 3).
//
// Reference source (open literature):
//   GE Aviation J79 spec sheet (declassified) + Mattingly "Elements of Gas
//   Turbine Propulsion" §12 F-4 Phantom II / B-58 Hustler derivative data.
//
// Dry J79-GE-17A at sea-level static (mil power):
//   Thrust:   52.0 kN  (11,870 lbf)
//   TSFC dry: 0.84 lb/lbf/h → Isp ≈ 4280 s
//   (NaN in AirbreathingFixtures — constant-cp overestimates ~18 % at T_t4=1254 K)
//
// Wet J79-GE-17A at sea-level static (afterburner):
//   Thrust:   79.6 kN  (17,900 lbf)    tolerance ±25 %  → [59.7, 99.5] kN
//   TSFC wet: 2.40 lb/lbf/h            → Isp ≈ 1498 s
//   Isp: NaN (skip) — constant-cp Brayton overcounts at T_t7 >> T_t5;
//         the qualitative physics (thrust up, Isp down) is verified instead.
//
// Design parameters mirror AirbreathingFixtures.J79_SeaLevelStatic:
//   InletThroatArea_m2 = 0.428, φ = 0.209, π_c = 13.5, TurbineCoolingFraction = 0.08
//   f_ab = 0.025 (mid-range J79 wet ratio — Mattingly §12 reference)
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Afterburning turbojet variant under ADR-036 § Air-breathing
// pillar (turbojet row ±15 % thrust + ±10 % TSFC; widened per D3.2 to ±25 %
// thrust below for the augmented case). Bands cite specific physics gaps:
//   • Constant-cp (γ=1.4, cp=1004.5 J/kg·K) overestimates V_9 at high T_t7;
//     thrust augmentation ratio from the model (wet/dry) is expected to be
//     higher than the published 1.53× (79.6/52). Tolerance band covers this.
//   • The fixture checks structural physics (wet > dry thrust, feasibility,
//     T_t7 within liner limit) rather than absolute number matching.
//   • Wide ±25 % tolerance on thrust absorbs the constant-cp error at T_t7.

using System.Linq;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Validation;

public sealed class TurbojetFixture_J79GE17Wet
{
    // ── reference values ─────────────────────────────────────────────────

    private const double Published_ThrustWet_N    = 79_600.0;   // 17,900 lbf
    private const double Tolerance_Thrust          = 0.25;       // ±25 % (constant-cp model)
    private const double AfterburnerMaxLinerTemp_K = 2100.0;     // Inconel 625 hard limit

    // ── design helpers ────────────────────────────────────────────────────
    // Parameters match AirbreathingFixtures.J79_SeaLevelStatic exactly so the
    // dry baseline is cross-validated against the existing fixture library.

    private static AirbreathingEngineDesign DryDesign() => new(
        Kind:                    AirbreathingEngineKind.Turbojet,
        InletThroatArea_m2:      0.428,
        CombustorArea_m2:        0.45,
        CombustorLength_m:       0.50,
        NozzleThroatArea_m2:     0.200,
        NozzleExitArea_m2:       0.270,
        EquivalenceRatio:        0.209,
        CompressorPressureRatio: 13.5)
    {
        TurbineCoolingFraction = 0.08,  // J79 ~8 % compressor-bleed turbine cooling
    };

    private static AirbreathingEngineDesign WetDesign() => DryDesign() with
    {
        EnableAfterburner       = true,
        AfterburnerFuelAirRatio = 0.025,
    };

    private static FlightConditions SeaLevelStatic() =>
        new(Altitude_m: 0.0, MachNumber: 0.001, Fuel: AirbreathingFuel.Jp8);

    // ── 1. Wet thrust within ±25 % of published 79.6 kN ─────────────────

    [Fact]
    public void Wet_ThrustNet_WithinPublishedBand()
    {
        var solver = new TurbojetCycleSolver();
        var result = solver.Solve(WetDesign(), SeaLevelStatic());

        double F  = result.Stations.ThrustNet_N;
        double lo = Published_ThrustWet_N * (1.0 - Tolerance_Thrust);
        double hi = Published_ThrustWet_N * (1.0 + Tolerance_Thrust);

        Assert.True(
            F >= lo && F <= hi,
            $"J79 wet thrust {F / 1000.0:F1} kN outside [{lo / 1000.0:F1}, {hi / 1000.0:F1}] kN "
          + $"(published 79.6 kN ±{Tolerance_Thrust:P0}).");
    }

    // ── 2. Wet thrust > dry thrust (augmentation verified) ────────────────

    [Fact]
    public void Wet_Thrust_ExceedsDry()
    {
        var solver = new TurbojetCycleSolver();
        var dry    = solver.Solve(DryDesign(), SeaLevelStatic());
        var wet    = solver.Solve(WetDesign(), SeaLevelStatic());

        Assert.True(
            wet.Stations.ThrustNet_N > dry.Stations.ThrustNet_N,
            $"Wet thrust {wet.Stations.ThrustNet_N / 1000.0:F1} kN should exceed "
          + $"dry thrust {dry.Stations.ThrustNet_N / 1000.0:F1} kN.");
    }

    // ── 3. Wet Isp < dry Isp (SFC penalty verified) ───────────────────────
    // Note: absolute Isp is not asserted (constant-cp overcounts at T_t7);
    // the ordering (wet < dry) is the physics invariant this test pins.

    [Fact]
    public void Wet_Isp_LessThanDry()
    {
        var solver = new TurbojetCycleSolver();
        var dry    = solver.Solve(DryDesign(), SeaLevelStatic());
        var wet    = solver.Solve(WetDesign(), SeaLevelStatic());

        Assert.True(
            wet.Stations.SpecificImpulse_s < dry.Stations.SpecificImpulse_s,
            $"Wet Isp {wet.Stations.SpecificImpulse_s:F0} s should be less than "
          + $"dry Isp {dry.Stations.SpecificImpulse_s:F0} s (SFC penalty).");
    }

    // ── 4. T_t7 finite and below liner material limit ─────────────────────

    [Fact]
    public void Wet_T_t7_BelowLinerLimit()
    {
        var solver = new TurbojetCycleSolver();
        var result = solver.Solve(WetDesign(), SeaLevelStatic());

        double T_t7 = result.Stations.Station(7).StagnationT_K;

        Assert.False(double.IsNaN(T_t7), "T_t7 should not be NaN in wet mode.");
        Assert.True(
            T_t7 < AfterburnerMaxLinerTemp_K,
            $"T_t7 = {T_t7:F0} K exceeds liner limit {AfterburnerMaxLinerTemp_K:F0} K "
          + $"at the published J79 operating point — reduce f_ab.");
    }

    // ── 5. AFTERBURNER_LINER_OVERTEMP gate does NOT fire at published f_ab ──
    // Note: IsFeasible is NOT checked here because the J79's large inlet
    // (0.428 m²) drives corrected mass flow above the J85-class stand-in
    // map's coverage (18–22.7 kg/s), which fires CORRECTED_MASS_FLOW_OUT_OF_MAP.
    // That limitation is documented in AirbreathingFixtures.J79_SeaLevelStatic;
    // this fixture mirrors the same convention — physics assertions only.

    [Fact]
    public void Wet_AfterburnerLinerOvertemp_NotFired()
    {
        var result = AirbreathingOptimization.GenerateWith(WetDesign(), SeaLevelStatic());
        var linerViolations = result.Violations
            .Concat(result.Advisories)
            .Where(v => v.ConstraintId == "AFTERBURNER_LINER_OVERTEMP")
            .ToList();
        Assert.Empty(linerViolations);
    }

    // ── 6. Wet fuel flow exceeds dry fuel flow ─────────────────────────────

    [Fact]
    public void Wet_FuelMassFlow_ExceedsDry()
    {
        var solver = new TurbojetCycleSolver();
        var dry    = solver.Solve(DryDesign(), SeaLevelStatic());
        var wet    = solver.Solve(WetDesign(), SeaLevelStatic());
        Assert.True(
            wet.Stations.FuelMassFlow_kg_s > dry.Stations.FuelMassFlow_kg_s,
            $"Wet fuel flow {wet.Stations.FuelMassFlow_kg_s:F4} kg/s should exceed "
          + $"dry {dry.Stations.FuelMassFlow_kg_s:F4} kg/s.");
    }
}
