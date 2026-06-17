// AerospikeInjectorFaceThermal.cs — Sprint 8 Track A (2026-04-22):
// Aerospike analogue of HeatTransfer.InjectorFaceThermal.Estimate.
// Produces a first-order equilibrium injector-face temperature for an
// aerospike pre-throat combustion chamber, so the Sprint 8
// AEROSPIKE_INJECTOR_FACE_TEMP gate (in AerospikeFeasibility.Evaluate)
// has something to compare against the wall material limit.
//
// Model
// ─────
// Equilibrium steady-state balance on the face:
//   h_g · (T_aw − T_face) = h_back · (T_face − T_prop)
//
//   T_aw     = chamber stagnation T × recovery factor (≈ 0.9 Pr^(1/3))
//   h_g      = Bartz-ish chamber-wall HTC at the injector face —
//              deliberately conservative: treats the pre-throat
//              annular chamber as a straight duct of diameter
//              2·R_chamber, scaled by the standard Bartz curvature /
//              boundary-layer factor.
//   h_back   = Dittus-Boelter on the bore-velocity scale (same
//              formula as regen InjectorFaceThermal), weighted by the
//              bore-area fraction of the face.
//   T_prop   = flow-weighted mean of ox and fuel injection T.
//
// Fidelity + scope caveats
// ────────────────────────
// This is a first-order estimator suitable for a feasibility gate —
// it is NOT a CFD face-heat-flux prediction. It uses:
//   • Chamber stagnation T from the propellant table at the design
//     MR / Pc — not per-station temperatures (no regen thermal march
//     exists yet for the pre-throat aerospike chamber).
//   • A constant recovery factor (0.9) rather than the Pr^(1/3) model
//     the regen-chamber Bartz solver uses.
//   • Average (not local) face bore coverage — rows are treated as a
//     single annular band on the face.
// Output confidence is the same as the regen InjectorFaceThermal
// (±150 K at the 50 % film-cooling envelope; tighter with a CFD
// refinement pass) but is a legitimate gate signal: if this crude
// model predicts face burnout, a production-fidelity analysis will
// almost certainly agree.

using System.Collections.Generic;

namespace Voxelforge.HeatTransfer;

/// <summary>
/// Result of <see cref="AerospikeInjectorFaceThermal.Estimate"/>.
/// Mirrors <see cref="InjectorFaceResult"/> but keyed to the
/// aerospike pre-throat chamber geometry.
/// </summary>
public sealed record AerospikeInjectorFaceResult(
    double TFace_K,
    double TAwCore_K,
    double TPropAvg_K,
    double HeatFlux_Wm2,
    double HGasSide_Wm2K,
    double HPropSide_Wm2K,
    double FaceArea_cm2,
    double BoreAreaFraction,
    string Method,
    string[] Warnings,
    // PH-35 aerospike-face follow-on (2026-04-29 — closes #234): face
    // material max-service-T (K). Pre-PH-35 the AEROSPIKE_INJECTOR_FACE_TEMP
    // gate keyed on `material.MaxServiceTemp_K` (chamber-wall liner limit).
    // Post-PH-35 the gate reads this field — defaulting to 1200 K (IN625/SS
    // face per A1-followon) and overrideable via
    // `OperatingConditions.InjectorFaceMaxTemp_K_Override`. Mirrors the
    // bell-chamber `InjectorFaceResult.MaxServiceTemp_K` semantics shipped
    // in PR #229.
    double MaxServiceTemp_K = 1200.0);

/// <summary>
/// Pure-math aerospike injector-face thermal estimator.
/// Deterministic; thread-safe; no PicoGK dependency. Safe to call from
/// xUnit — consumes only <see cref="Geometry.AerospikeBuildResult"/>
/// and a <see cref="Combustion.PropellantState"/>.
/// </summary>
public static class AerospikeInjectorFaceThermal
{
    /// <summary>
    /// Mirrors <see cref="InjectorFaceThermal.MinBoreAreaFraction"/>:
    /// below this coverage, the face is effectively uncooled and the
    /// estimator emits a low-confidence warning.
    /// </summary>
    public const double MinBoreAreaFraction = 0.005;

    /// <summary>
    /// Legacy constant recovery factor (turbulent flat-plate value at M ≈ 0.1).
    /// The live model now uses <see cref="Combustion.PropellantTables.AdiabaticWallTemp"/>
    /// via <see cref="FaceMachNumber"/>; this constant is retained for reference.
    /// </summary>
    public const double RecoveryFactor = 0.90;

    /// <summary>
    /// Sprint 37b / PH-14 (2026-04-25): face Mach number used in the
    /// recovery-temperature evaluation. The injector face sits at the
    /// upstream end of the pre-throat chamber where flow is essentially
    /// stagnant. Pre-Sprint-37b model collapsed M ≈ 0 to a fixed
    /// recovery factor of 0.90, which is a turbulent-flat-plate value
    /// at significant Mach — wrong direction at low M. With the proper
    /// Pr^(1/3) recovery formula (PropellantTables.AdiabaticWallTemp),
    /// T_aw ≈ T_chamber at M = 0.1 within 0.1 %.
    /// </summary>
    public const double FaceMachNumber = 0.1;

    /// <summary>
    /// Sprint 37b / PH-14 (2026-04-25): wall-temperature seed for the
    /// Bartz σ correction at the face. The face thermal solver does not
    /// iterate on T_wall — Bartz's (T_wg/T_c)^… correction is bounded
    /// over the typical 600-1200 K face-T band, so a single seed at
    /// 1000 K yields h_g within ±10 % of a fully-iterated value.
    /// </summary>
    public const double FaceWallTempSeed_K = 1000.0;

    /// <summary>
    /// Legacy ad-hoc Bartz-scale constant. The live model now calls
    /// <see cref="HeatTransfer.BartzHeatFlux.HeatTransferCoefficient"/>
    /// with a chamber-radius "throat" substitute; this constant is retained
    /// for reference.
    /// </summary>
    public const double BartzChamberScale = 0.026;

    /// <summary>
    /// Estimate face T_face for an aerospike build with injector sizing.
    /// Returns null when <see cref="Geometry.AerospikeBuildResult.InjectorSizing"/>
    /// is absent (no pattern to back-cool the face with). The sized
    /// <see cref="Geometry.AerospikeInjectorSizing"/> provides bore-area
    /// totals + per-element bore diameter + fuel / ox velocity.
    /// </summary>
    public static AerospikeInjectorFaceResult? Estimate(
        Geometry.AerospikeBuildResult build,
        Combustion.PropellantState gas,
        double coolantInletTemp_K,
        double coolantInletPressure_Pa,
        Combustion.PropellantPair propellantPair,
        double mixtureRatio,
        // PH-36 aerospike-face follow-on (2026-04-29 — closes #233): per-pair
        // oxidizer injection T. Default 0 → DefaultOxidizerInjectionT_K(pair).
        double oxidizerInletTemp_K = 0.0,
        // PH-35 aerospike-face follow-on (2026-04-29 — closes #234): face
        // material max-service-T override. Default 0 →
        // InjectorFaceThermal.DefaultInjectorFaceMaxTemp_K (1200 K, IN625/SS).
        double injectorFaceMaxTemp_K_Override = 0.0)
    {
        if (build is null) throw new System.ArgumentNullException(nameof(build));
        if (build.InjectorSizing is null) return null;

        // Sprint 14 / Track I / P9: pre-size at 4 — see RegenCoolingSolver.
        var warnings = new List<string>(4);
        var sizing = build.InjectorSizing!;
        var pairMeta = Combustion.PropellantPairs.GetMeta(propellantPair);
        var fluid = Coolant.CoolantRegistry.Get(pairMeta.CoolantFluidKey);

        // Sprint 37b / PH-14 (2026-04-25): hot-side adiabatic wall T at
        // the face via PropellantTables.AdiabaticWallTemp with the
        // proper Pr^(1/3) recovery model and M = FaceMachNumber. At
        // M = 0.1, T_aw ≈ T_chamber within 0.1 % — the pre-Sprint-37b
        // 0.90 × T_chamber form was a turbulent-flat-plate value at
        // significant Mach (wrong direction at low M; ~10 % low on T_aw,
        // which propagates a similar magnitude into the predicted face
        // temperature).
        double T_aw = Combustion.PropellantTables.AdiabaticWallTemp(
            gas.ChamberTemp_K, FaceMachNumber, gas.Gamma, gas.Prandtl);

        // Sprint 37b / PH-14 (2026-04-25): hot-side h_g at the face via
        // BartzHeatFlux with a chamber-radius "throat" substitute. The
        // pre-Sprint-37b ad-hoc form (h_g = 0.026 · ρ·u · cp · (T_aw/T_c)^-0.5)
        // missed Pr^-0.6, μ^0.2, (Pc/C*)^0.8 — none of those are negligible.
        // Calling the same Bartz machinery the regen solver uses unifies
        // the gas-side model across topologies. T_wg seed = 1000 K is
        // bounded over the typical 600-1200 K face-T band (σ varies < 10 %).
        double R_chamber_m = build.ChamberRadius_mm * 1e-3;
        double A_throat_mm2 = build.Contour.ThroatAnnulusArea_mm2;
        double A_throat_m2  = A_throat_mm2 * 1e-6;
        double mdot_est = A_throat_m2 * gas.ChamberPressure_Pa
                        / System.Math.Max(gas.CStar_ms, 1e-6);
        double A_chamber_m2 = System.Math.PI * R_chamber_m * R_chamber_m;
        double areaRatio = A_throat_m2 / System.Math.Max(A_chamber_m2, 1e-9);
        double D_chamber_m = 2.0 * R_chamber_m;
        // Conservative wide curvature (effectively flat) — face is the
        // upstream end of a converging chamber, not a curved throat.
        double r_curv_face_m = 1.5 * R_chamber_m;
        double h_g = HeatTransfer.BartzHeatFlux.HeatTransferCoefficient(
            gas:                   gas,
            throatDiameter_m:      D_chamber_m,
            throatCurvature_m:     r_curv_face_m,
            areaRatioToThroat:     areaRatio,
            localMach:             FaceMachNumber,
            wallTempGas_K:         FaceWallTempSeed_K);

        // Cold-side: propellant at the face, flow-weighted mean.
        var fuelBulk = fluid.GetState(coolantInletTemp_K, coolantInletPressure_Pa);
        // PH-36 aerospike-face follow-on (2026-04-29 — closes #233):
        // per-pair oxidizer injection T. Override > 0 short-circuits the
        // per-pair default (90.18 K for LOX-based pairs, 290-293 K for
        // storables). Mirrors the bell-chamber path's PH-36 plumbing
        // (PR #227). All current production pairs use LOX → ~90 K (within
        // 0.18 K of the pre-PH-36 hardcoded 90.0, no functional change).
        double T_ox_inj = oxidizerInletTemp_K > 0
            ? oxidizerInletTemp_K
            : InjectorFaceThermal.DefaultOxidizerInjectionT_K(propellantPair);
        double T_prop_avg =
            (mdot_est * mixtureRatio / (1.0 + mixtureRatio)) * T_ox_inj
          + (mdot_est * 1.0          / (1.0 + mixtureRatio)) * coolantInletTemp_K;
        T_prop_avg /= System.Math.Max(mdot_est, 1e-9);

        // h_back: Dittus-Boelter on bore scale. Largest of ox / fuel
        // equivalent diameter dominates the back-side convection —
        // matches the regen InjectorFaceThermal convention.
        double D_bore_mm = System.Math.Max(
            sizing.PatternSizing.PerElementResult.OxEquivDiameter_mm,
            sizing.PatternSizing.PerElementResult.FuelEquivDiameter_mm);
        double D_bore_m = System.Math.Max(D_bore_mm * 1e-3, 1e-4);
        double v_bore = sizing.PatternSizing.PerElementResult.FuelVelocity_ms > 0
                      ? sizing.PatternSizing.PerElementResult.FuelVelocity_ms
                      : sizing.PatternSizing.PerElementResult.OxVelocity_ms;
        v_bore = System.Math.Max(v_bore, 1.0);
        double Re_bore = fuelBulk.Density_kgm3 * v_bore * D_bore_m
                       / System.Math.Max(fuelBulk.Viscosity_PaS, 1e-8);
        double Pr_fuel = System.Math.Max(fuelBulk.Prandtl, 1e-3);
        double Nu = 0.023 * System.Math.Pow(Re_bore, 0.8) * System.Math.Pow(Pr_fuel, 0.4);
        double h_back = Nu * fuelBulk.Conductivity_WmK / D_bore_m;

        // Face geometry — annular face between plug-base radius and
        // chamber radius. The plug occupies the centre; bores sit on
        // the pitch circle within the annular band.
        double A_face_m2 = A_chamber_m2;  // conservative (ignores plug footprint)
        double A_face_cm2 = A_face_m2 * 1e4;
        double A_bores_m2 = (sizing.PatternSizing.TotalOxArea_mm2 + sizing.PatternSizing.TotalFuelArea_mm2) * 1e-6;
        double boreFrac = A_bores_m2 / System.Math.Max(A_face_m2, 1e-9);

        if (boreFrac < MinBoreAreaFraction)
            warnings.Add($"Aerospike face bore-area fraction {boreFrac:P2} below "
                       + $"{MinBoreAreaFraction:P1} minimum — T_face estimate is a lower bound, "
                       + "real face may run hotter.");

        double h_back_eff = h_back * System.Math.Max(boreFrac, MinBoreAreaFraction);

        double T_face = (h_g * T_aw + h_back_eff * T_prop_avg)
                      / System.Math.Max(h_g + h_back_eff, 1e-9);
        double q_face = h_g * (T_aw - T_face);

        // PH-35 aerospike-face follow-on (2026-04-29 — closes #234): face
        // material max-T. Override > 0 short-circuits the IN625/SS default
        // (1200 K) with a per-design value. Mirrors the bell-chamber
        // PR #229 plumbing.
        double maxServiceT_K = injectorFaceMaxTemp_K_Override > 0
            ? injectorFaceMaxTemp_K_Override
            : InjectorFaceThermal.DefaultInjectorFaceMaxTemp_K;

        return new AerospikeInjectorFaceResult(
            TFace_K:         T_face,
            TAwCore_K:       T_aw,
            TPropAvg_K:      T_prop_avg,
            HeatFlux_Wm2:    q_face,
            HGasSide_Wm2K:   h_g,
            HPropSide_Wm2K:  h_back_eff,
            FaceArea_cm2:    A_face_cm2,
            BoreAreaFraction: boreFrac,
            Method:          "aerospike-face-equilibrium-v1",
            Warnings:        warnings.ToArray(),
            MaxServiceTemp_K: maxServiceT_K);
    }
}
