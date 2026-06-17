// AerospikeFeasibility.cs — Aerospike-equivalent feasibility gate.
// Mirrors the regen `FeasibilityGate.Evaluate` pattern but operates on
// an `AerospikeBuildResult` instead of `RegenGenerationResult`.
//
// Gates:
//   • AEROSPIKE_PLUG_WALL_TEMP — only when thermal solve ran AND the
//     peak gas-side plug-wall T exceeds the material service limit.
//     Analogue of regen `WALL_TEMP`.
//   • AEROSPIKE_COOLANT_CAVITATION_RISK — fires when the plug-cooling
//     march's coolant pressure fell below the cavitation floor
//     (AerospikePlugCooling.CavitationPressureFloor_Pa = 0.1 MPa) at
//     any station. The clamp previously only emitted a warning and
//     the march silently continued at floor pressure, hiding a
//     credible failure mode (cavitation / film-boiling in the plug
//     channels). Surfacing it as a gate makes the SA optimizer reject
//     the candidate and the user see why.
//   • AEROSPIKE_ELEMENT_CLEARANCE — fires when
//     AerospikeSpec.InjectorPattern was supplied but the sized
//     elements cannot physically fit on the pre-throat face at their
//     requested ElementCount — the arc spacing on the chosen pitch
//     circle is below (element OD + 2 mm LPBF floor). Remediation is
//     any of: fewer elements, a larger chamber (raise contraction
//     ratio or thrust), a smaller per-element flow (split elements
//     into rows), or a thinner element housing factor.
//   • AEROSPIKE_INJECTOR_FACE_TEMP — aerospike analogue of regen gate
//     INJECTOR_FACE_T_EXCEEDED. Fires when
//     AerospikeInjectorFaceThermal.Estimate's predicted T_face exceeds
//     the wall-material service limit. Only fires when a pattern was
//     supplied (InjectorFace non-null). Remediation: raise the
//     outer-row film-cooling fraction, switch to a higher-temperature
//     wall material, pick an element type with bigger bore coverage,
//     or reduce chamber pressure.
//
// Why a separate file instead of extending `FeasibilityGate`
// ─────────────────────────────────────────────────────────
// The regen `FeasibilityGate.Evaluate(RegenGenerationResult)` is tied
// to the regen record shape (stations, injector face, feed stackup,
// ...). The aerospike pipeline doesn't produce most of those; it has
// a minimal surface (`AerospikeBuildResult.Thermal`). Cleaner to
// stamp a parallel evaluator than to stretch the regen signature.
// A common `IEngineResult` supertype could unify these in future.

using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;

namespace Voxelforge.Geometry;

/// <summary>
/// Evaluator for Phase-2 aerospike feasibility gates. Returns the
/// standard <see cref="FeasibilityGateResult"/> so UI + CLI can
/// consume it identically to regen gates.
/// </summary>
public static class AerospikeFeasibility
{
    /// <summary>
    /// Evaluate the Phase-2 aerospike feasibility gates against the
    /// supplied build result. When
    /// <see cref="AerospikeBuildResult.Thermal"/> is null (Phase 1
    /// geometry-only path), no gate fires — the result is trivially
    /// feasible.
    /// </summary>
    public static FeasibilityGateResult Evaluate(
        AerospikeBuildResult build, int wallMaterialIndex)
    {
        var violations = new System.Collections.Generic.List<FeasibilityViolation>();

        if (build.Thermal is { } thermal)
        {
            var material = WallMaterials.All[System.Math.Clamp(
                wallMaterialIndex, 0, WallMaterials.All.Length - 1)];
            if (thermal.PeakGasSideWallT_K > material.MaxServiceTemp_K)
            {
                violations.Add(new FeasibilityViolation(
                    ConstraintId: "AEROSPIKE_PLUG_WALL_TEMP",
                    Description:
                        $"Aerospike plug peak wall T {thermal.PeakGasSideWallT_K:F0} K "
                      + $"(@ x = {thermal.PeakStation_X_mm:F1} mm) exceeds "
                      + $"{material.Name} service limit {material.MaxServiceTemp_K:F0} K. "
                      + $"Increase channel depth, raise coolant mass flow, or "
                      + $"switch to a higher-temperature wall material.",
                    ActualValue:  thermal.PeakGasSideWallT_K,
                    Limit:        material.MaxServiceTemp_K));
            }

            // Cavitation-risk gate. Fires when the plug-cooling march
            // clamped the coolant pressure at the 0.1 MPa floor on at
            // least one station. `MinCoolantPressure_Pa` is the raw
            // (unclamped) minimum from the march, so it can be below
            // the floor when the clamp engaged.
            if (thermal.CavitationRiskStationCount > 0)
            {
                double floor_Pa = HeatTransfer.AerospikePlugCooling.CavitationPressureFloor_Pa;
                violations.Add(new FeasibilityViolation(
                    ConstraintId: "AEROSPIKE_COOLANT_CAVITATION_RISK",
                    Description:
                        $"Aerospike plug-cooling coolant pressure fell below the "
                      + $"{floor_Pa / 1e6:F1} MPa cavitation floor at "
                      + $"{thermal.CavitationRiskStationCount} station(s); minimum "
                      + $"{thermal.MinCoolantPressure_Pa / 1e6:F2} MPa. "
                      + $"Raise coolant inlet pressure, enlarge channel "
                      + $"cross-section, or reduce coolant mass flow.",
                    ActualValue:  thermal.MinCoolantPressure_Pa,
                    Limit:        floor_Pa));
            }
        }

        // Sprint 7 Track A (2026-04-22): injector-element clearance
        // gate. Fires when the sized pattern packs elements closer than
        // the (element OD + 2 mm LPBF floor) clearance. Skipped when
        // no pattern was supplied (AerospikeSpec.InjectorPattern null →
        // InjectorSizing null).
        if (build.InjectorSizing is { ClearanceOk: false } injSizing)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "AEROSPIKE_ELEMENT_CLEARANCE",
                Description:
                    $"Aerospike injector elements cannot fit on the pre-throat "
                  + $"chamber face: {injSizing.PatternSizing.ElementCount} elements "
                  + $"need {injSizing.MinClearance_mm:F2} mm arc spacing on "
                  + $"R={injSizing.PitchCircleRadius_mm:F1} mm pitch circle but "
                  + $"only have {injSizing.ArcSpacing_mm:F2} mm. "
                  + $"Remediation: fewer elements, raise chamber contraction / "
                  + $"pitch-circle radius, split into multiple rows, or switch "
                  + $"element type to one with a smaller housing footprint.",
                ActualValue:  injSizing.ArcSpacing_mm,
                Limit:        injSizing.MinClearance_mm));
        }

        // Sprint 8 Track A (2026-04-22): injector-face thermal gate.
        // Fires when the equilibrium T_face exceeds the face-material
        // service limit. Skipped when no InjectorFace result was
        // produced (pattern null → no sizing → no face thermal).
        //
        // PH-35 aerospike-face follow-on (2026-04-29 — closes #234): the
        // gate now reads `face.MaxServiceTemp_K` (defaults to 1200 K
        // IN625/SS, overrideable via
        // `OperatingConditions.InjectorFaceMaxTemp_K_Override`) instead of
        // `material.MaxServiceTemp_K` (the chamber-wall liner limit).
        // Brings the aerospike gate semantics in line with the bell-chamber
        // INJECTOR_FACE_T_EXCEEDED gate shipped in PR #229.
        if (build.InjectorFace is { } face)
        {
            double faceLimit_K = face.MaxServiceTemp_K > 0
                ? face.MaxServiceTemp_K
                : HeatTransfer.InjectorFaceThermal.DefaultInjectorFaceMaxTemp_K;
            if (face.TFace_K > faceLimit_K)
            {
                violations.Add(new FeasibilityViolation(
                    ConstraintId: "AEROSPIKE_INJECTOR_FACE_TEMP",
                    Description:
                        $"Aerospike injector face predicted T_face "
                      + $"{face.TFace_K:F0} K exceeds face-material service "
                      + $"limit {faceLimit_K:F0} K "
                      + $"(bore coverage {face.BoreAreaFraction:P1}, "
                      + $"h_g={face.HGasSide_Wm2K / 1e3:F1} kW·m⁻²·K⁻¹). "
                      + $"Raise outer-row film fraction, pick a higher-T face alloy "
                      + $"(via `OperatingConditions.InjectorFaceMaxTemp_K_Override`), "
                      + $"choose an element type with larger bore coverage, "
                      + $"or reduce chamber pressure.",
                    ActualValue:  face.TFace_K,
                    Limit:        faceLimit_K));
            }
        }

        // Sprint 26 (2026-04-23): linear-aerospike aspect-ratio gate.
        // Fires only when the contour is linear — the classic
        // axisymmetric plug reads IsLinear=false and short-circuits.
        // The [0.30, 5.00] band is the X-33 XRS-2200 programme's
        // documented envelope: below 0.30 the side-wall recirculation
        // bubble covers more than half the expansion surface (Angelino
        // 2D model loses physical meaning); above 5.00 the plug
        // becomes a long-span cantilever whose thermal-bending
        // stiffness can't be managed at LPBF print scale with the
        // shipped wall-material library. Only the truncated-plug
        // length vs plug-width is checked — the aspect ratio is a
        // geometry scalar baked into the contour at generation time.
        if (build.Contour.IsLinear)
        {
            double aspect = build.Contour.LinearAspectRatio;
            double floor = Chamber.LinearAerospikeContourGenerator.MinAspectRatio;
            double ceiling = Chamber.LinearAerospikeContourGenerator.MaxAspectRatio;
            if (aspect < floor || aspect > ceiling)
            {
                bool below = aspect < floor;
                violations.Add(new FeasibilityViolation(
                    ConstraintId: "LINEAR_AEROSPIKE_ASPECT_RATIO",
                    Description:
                        $"Linear aerospike aspect ratio (plug length / width) "
                      + $"{aspect:F2} outside admissible band [{floor:F2}, {ceiling:F2}]. "
                      + (below
                            ? "Below floor: side-wall recirculation bubble would cover more than "
                            + "half the expansion surface (X-33 XRS-2200 heritage). Remediation: "
                            + "reduce plug transverse width, raise expansion ratio, or raise plug-length ratio."
                            : "Above ceiling: plug becomes a long-span cantilever with unmanageable "
                            + "thermal-bending stiffness at LPBF scale. Remediation: increase plug "
                            + "transverse width, reduce expansion ratio, or reduce plug-length ratio."),
                    ActualValue: aspect,
                    Limit:       below ? floor : ceiling));
            }
        }

        return new FeasibilityGateResult(
            IsFeasible: violations.Count == 0,
            Violations: violations.ToArray());
    }
}
