// ConstraintDirections.cs — Phase 2 of #627 (tracked under #743). Central
// SSOT mapping ConstraintId → BreachDirection across every production
// pillar (rocket / airbreathing / EP / marine / nuclear) plus aerospike /
// monolithic / voxel-resolution gates and the optimizer-wrapper
// exception-capture IDs.
//
// Phase 2 strategy (per the audit): emit sites stay unchanged. Sign
// conventions live in this lookup; SignedBreachMagnitude reads them at
// access time. This keeps all 183+ emit sites untouched and locates the
// convention in a single SSOT.
//
// Foundation PR (this file's initial state) populates every known
// ConstraintId at AboveLimit as the safe default. Per-pillar refinement
// PRs change individual entries to BelowLimit or Categorical based on
// each emit-site predicate (e.g. NPSH_INSUFFICIENT → BelowLimit,
// STABILITY_FAIL → Categorical). For unknown ConstraintIds (test
// fixtures, future gates not yet registered), For() falls back to
// AboveLimit.

using System;
using System.Collections.Generic;

namespace Voxelforge.Optimization;

/// <summary>
/// Central registry mapping <c>ConstraintId</c> →
/// <see cref="BreachDirection"/>. Phase 2 of
/// [#627](https://github.com/poetac/voxelforge/issues/627) (tracked under
/// [#743](https://github.com/poetac/voxelforge/issues/743)).
/// </summary>
/// <remarks>
/// <para>
/// The Map is the SSOT for per-gate sign conventions across all 5
/// production pillars plus aerospike / monolithic / voxel-resolution
/// gates and the optimizer-wrapper exception-capture IDs. Per-pillar
/// refinement PRs change entries from the foundation default of
/// <see cref="BreachDirection.AboveLimit"/> to the correct direction
/// inferred from each emit-site predicate.
/// </para>
/// <para>
/// Unknown ConstraintIds (e.g. test fixtures with synthetic IDs) fall
/// back to <see cref="BreachDirection.AboveLimit"/> via
/// <see cref="For"/>. The Map uses <see cref="StringComparer.Ordinal"/>
/// — lookups are case-sensitive and culture-invariant.
/// </para>
/// </remarks>
public static class ConstraintDirections
{
    private static readonly Dictionary<string, BreachDirection> s_map = new(StringComparer.Ordinal)
    {
        // ── Rocket pillar (regen bell + monoprop) — RocketGates.cs +
        //    FeasibilityGate.cs PreScreen ────────────────────────────────────
        { "ABLATIVE_BURNTHROUGH",                  BreachDirection.AboveLimit },
        { "ABLATIVE_REGEN_INTERFACE_OVERTEMP",     BreachDirection.AboveLimit },
        { "ABLATIVE_THROAT_RECESSION_EXCEEDS_BUDGET", BreachDirection.AboveLimit },
        { "ACOUSTIC_DAMPER_DETUNED",               BreachDirection.AboveLimit },
        { "ACOUSTIC_DAMPER_OVERSIZED",             BreachDirection.AboveLimit },
        { "BIMETALLIC_BOND_ZONE_SHEAR",            BreachDirection.AboveLimit },
        { "BLOW_DOWN_INSUFFICIENT",                BreachDirection.AboveLimit },
        { "BURST_MARGIN_INSUFFICIENT",             BreachDirection.AboveLimit },
        { "CHANNEL_ASPECT_RATIO_EXCEEDED",         BreachDirection.AboveLimit },
        { "CHILLDOWN_BUDGET_EXCEEDED",             BreachDirection.AboveLimit },
        { "COMBINED_AXIAL_BENDING_INSUFFICIENT",   BreachDirection.AboveLimit },
        { "COMMON_SHAFT_RPM_INCONSISTENT",         BreachDirection.AboveLimit },
        { "CONTRACTION_RATIO_OUT_OF_BAND",         BreachDirection.AboveLimit },
        { "COOLANT_T_EXCEEDED",                    BreachDirection.AboveLimit },
        { "DRAIN_PATH_MISSING",                    BreachDirection.AboveLimit },
        { "ELEMENT_DENSITY_TOO_HIGH",              BreachDirection.AboveLimit },
        { "EXPANDER_TURBINE_ENTHALPY_DEFICIT",     BreachDirection.AboveLimit },
        { "EXPANSION_DEFLECTION_PLUG_CLEARANCE",   BreachDirection.AboveLimit },
        { "FEATURE_TOO_SMALL",                     BreachDirection.AboveLimit },
        { "FEED_PRESSURE_INSUFFICIENT",            BreachDirection.AboveLimit },
        { "FINITE_RATE_ISP_PENALTY_LARGE",         BreachDirection.AboveLimit },
        { "G_INJ_TOO_HIGH",                        BreachDirection.AboveLimit },
        { "G_INJ_TOO_LOW",                         BreachDirection.AboveLimit },
        { "HARD_START_RISK",                       BreachDirection.AboveLimit },
        { "IGNITER_ENERGY_INSUFFICIENT",           BreachDirection.AboveLimit },
        { "IGNITER_MISSING",                       BreachDirection.AboveLimit },
        { "IGNITER_MODALITY_UNSUITABLE",           BreachDirection.AboveLimit },
        { "INJECTOR_FACE_T_EXCEEDED",              BreachDirection.AboveLimit },
        { "INSTRUMENTATION_TAP_INTERFERENCE",      BreachDirection.AboveLimit },
        { "INSTRUMENTATION_THERMAL_BRIDGE_RISK",   BreachDirection.AboveLimit },
        { "L_STAR_BELOW_PROPELLANT_MIN",           BreachDirection.AboveLimit },
        { "LCF_LIFE_INSUFFICIENT",                 BreachDirection.AboveLimit },
        { "MONOPROP_CATALYST_OVERLOADED",          BreachDirection.AboveLimit },
        { "MONOPROP_CHAMBER_TEMP_EXCEEDS_BED",     BreachDirection.AboveLimit },
        { "NPSH_INSUFFICIENT",                     BreachDirection.AboveLimit },
        { "ORSC_PREBURNER_OXCORROSION",            BreachDirection.AboveLimit },
        { "OVERHANG_ANGLE_EXCEEDED",               BreachDirection.AboveLimit },
        { "PINTLE_BLOCKAGE_OUT_OF_BAND",           BreachDirection.AboveLimit },
        { "PINTLE_TMR_OUT_OF_BAND",                BreachDirection.AboveLimit },
        { "PREBURNER_WALL_TEMP",                   BreachDirection.AboveLimit },
        { "PUMP_PRESSURE_INVERTED",                BreachDirection.AboveLimit },
        { "PUMP_SPECIFIC_SPEED_OFF_BAND",          BreachDirection.AboveLimit },
        { "PURGE_FLOW_INSUFFICIENT",               BreachDirection.AboveLimit },
        { "RDE_ANNULUS_FILL_STARVED",              BreachDirection.AboveLimit },
        { "RDE_WAVE_COUNT_BELOW_MINIMUM",          BreachDirection.AboveLimit },
        { "SHAFT_WHIRL",                           BreachDirection.AboveLimit },
        { "STABILITY_FAIL",                        BreachDirection.AboveLimit },
        { "TAPOFF_HOT_GAS_TOO_HOT",                BreachDirection.AboveLimit },
        { "TOPOLOGY_CHANNEL_NOT_PRINTABLE",        BreachDirection.AboveLimit },
        { "TPMS_AND_MANIFOLD_OVERLAP",             BreachDirection.AboveLimit },
        { "TPMS_CELL_FEATURE_TOO_SMALL",           BreachDirection.AboveLimit },
        { "TRANSPIRATION_BLEED_EXCESSIVE",         BreachDirection.AboveLimit },
        { "TRAPPED_POWDER_REGION",                 BreachDirection.AboveLimit },
        { "TURBINE_POWER_DEFICIT",                 BreachDirection.AboveLimit },
        { "TURBINE_UNCHOKED",                      BreachDirection.AboveLimit },
        { "WALL_TEMP",                             BreachDirection.AboveLimit },
        { "YIELD_EXCEEDED",                        BreachDirection.AboveLimit },

        // ── Aerospike (rocket-variant) — AerospikeFeasibility.cs ──────────
        { "AEROSPIKE_COOLANT_CAVITATION_RISK",     BreachDirection.AboveLimit },
        { "AEROSPIKE_ELEMENT_CLEARANCE",           BreachDirection.AboveLimit },
        { "AEROSPIKE_INJECTOR_FACE_TEMP",          BreachDirection.AboveLimit },
        { "AEROSPIKE_PLUG_WALL_TEMP",              BreachDirection.AboveLimit },
        { "LINEAR_AEROSPIKE_ASPECT_RATIO",         BreachDirection.AboveLimit },

        // ── Monolithic body composition — MonolithicFeasibility.cs ────────
        { "MONOLITHIC_BODY_INTERSECTION",          BreachDirection.AboveLimit },
        { "MONOLITHIC_TUBE_INTERSECTION",          BreachDirection.AboveLimit },

        // ── Optimizer-level wrappers ──────────────────────────────────────
        { "OBJECTIVE_EVALUATION_TIMEOUT",          BreachDirection.AboveLimit },
        { "VOXEL_RESOLUTION",                      BreachDirection.AboveLimit },

        // ── Airbreathing pillar — AirbreathingFeasibility.cs +
        //    AirbreathingGates.cs + per-cycle objective exception captures ─
        { "AFTERBURNER_LINER_OVERTEMP",            BreachDirection.AboveLimit },
        { "BYPASS_DUCT_CHOKED",                    BreachDirection.AboveLimit },
        { "BYPASS_MIXER_ENTHALPY_IMBALANCE",       BreachDirection.AboveLimit },
        { "BYPASS_RATIO_OUT_OF_BAND",              BreachDirection.AboveLimit },
        { "COMBUSTION_EFFICIENCY_BELOW_FLOOR",     BreachDirection.AboveLimit },
        { "COMBUSTOR_BLOWOUT_LEAN",                BreachDirection.AboveLimit },
        { "COMBUSTOR_BLOWOUT_RICH",                BreachDirection.AboveLimit },
        { "COMPRESSOR_RATIO_OUT_OF_BAND",          BreachDirection.AboveLimit },
        { "CORRECTED_MASS_FLOW_OUT_OF_MAP",        BreachDirection.AboveLimit },
        { "FAN_STALL",                             BreachDirection.AboveLimit },
        { "GAS_TURBINE_EFFICIENCY_BELOW_FLOOR",    BreachDirection.AboveLimit },
        { "GAS_TURBINE_NET_WORK_NEGATIVE",         BreachDirection.AboveLimit },
        { "GAS_TURBINE_RECUPERATOR_OVERTEMPERATURE", BreachDirection.AboveLimit },
        { "INLET_UNSTART",                         BreachDirection.AboveLimit },
        { "ISOLATOR_UNSTART",                      BreachDirection.AboveLimit },
        { "LACE_AIR_LIQUEFACTION_INSUFFICIENT",    BreachDirection.AboveLimit },
        { "LACE_AIR_TO_FUEL_OUT_OF_ADVISORY",      BreachDirection.AboveLimit },
        { "LACE_AIR_TO_FUEL_OUT_OF_BAND",          BreachDirection.AboveLimit },
        { "LACE_CHAMBER_PRESSURE_OUT_OF_BAND",     BreachDirection.AboveLimit },
        { "LACE_PRECOOLER_EFFECTIVENESS_LOW",      BreachDirection.AboveLimit },
        { "LACE_PRECOOLER_FROST_LINE_RISK",        BreachDirection.AboveLimit },
        { "NOZZLE_INSUFFICIENT_DRIVE_PRESSURE",    BreachDirection.AboveLimit },
        { "PULSEJET_ACOUSTIC_OVERPRESSURE",        BreachDirection.AboveLimit },
        { "PULSEJET_BLOWOUT_LEAN",                 BreachDirection.AboveLimit },
        { "RAMJET_EVAL_EXCEPTION",                 BreachDirection.AboveLimit },
        { "RBCC_EVAL_EXCEPTION",                   BreachDirection.AboveLimit },
        { "RBCC_MODE_OUT_OF_ENVELOPE",             BreachDirection.AboveLimit },
        { "RDE_CHANNEL_WIDTH_ABOVE_ADVISORY",      BreachDirection.AboveLimit },
        { "RDE_CHANNEL_WIDTH_BELOW_CELL_SIZE",     BreachDirection.AboveLimit },
        { "RDE_LENGTH_TO_DIAMETER_OUT_OF_BAND",    BreachDirection.AboveLimit },
        { "RDE_PRESSURE_GAIN_OUT_OF_BAND",         BreachDirection.AboveLimit },
        { "RDE_WAVE_COUNT_OUT_OF_BAND",            BreachDirection.AboveLimit },
        { "SCRAMJET_EVAL_EXCEPTION",               BreachDirection.AboveLimit },
        { "STATIC_T_T_RATIO_OUT_OF_BAND",          BreachDirection.AboveLimit },
        { "STEAM_CONDENSE_BELOW_VACUUM",           BreachDirection.AboveLimit },
        { "SURGE_MARGIN_INSUFFICIENT",             BreachDirection.AboveLimit },
        { "T_T4_EXCEEDS_LIMIT",                    BreachDirection.AboveLimit },
        { "THERMAL_CHOKING",                       BreachDirection.AboveLimit },
        { "TIT_EXCEEDED",                          BreachDirection.AboveLimit },
        { "TURBOFAN_EVAL_EXCEPTION",               BreachDirection.AboveLimit },
        { "TURBOJET_EVAL_EXCEPTION",               BreachDirection.AboveLimit },
        { "TURBOPROP_SHAFT_POWER_INSUFFICIENT",    BreachDirection.AboveLimit },

        // ── Electric Propulsion pillar — ElectricPropulsionFeasibility.cs ─
        { "ARCJET_ANODE_OVERHEAT",                 BreachDirection.AboveLimit },
        { "ARCJET_FROZEN_FLOW_LOSS_EXCESSIVE",     BreachDirection.AboveLimit },
        { "ARCJET_THERMAL_EFFICIENCY_LOW",         BreachDirection.AboveLimit },
        { "ARCJET_VOLTAGE_OUT_OF_BAND",            BreachDirection.AboveLimit },
        { "GIT_BEAM_VOLTAGE_OUT_OF_BAND",          BreachDirection.AboveLimit },
        { "GIT_GRID_LIFETIME_BELOW_FLOOR",         BreachDirection.AboveLimit },
        { "GIT_NEUTRALIZER_CURRENT_MISMATCH",      BreachDirection.AboveLimit },
        { "GIT_PERVEANCE_LIMIT_EXCEEDED",          BreachDirection.AboveLimit },
        { "GIT_PLUME_DIVERGENCE_EXCESSIVE",        BreachDirection.AboveLimit },
        { "HET_ANODE_OVERHEAT",                    BreachDirection.AboveLimit },
        { "HET_CATHODE_LIFE_LIMIT",                BreachDirection.AboveLimit },
        { "HET_DISCHARGE_VOLTAGE_OUT_OF_BAND",     BreachDirection.AboveLimit },
        { "HET_MAGNETIC_FIELD_INSUFFICIENT",       BreachDirection.AboveLimit },
        { "HET_MASS_UTILIZATION_LOW",              BreachDirection.AboveLimit },
        { "HET_PLUME_DIVERGENCE_EXCESSIVE",        BreachDirection.AboveLimit },
        { "MPD_APPLIED_FIELD_DOMINATES",           BreachDirection.AboveLimit },
        { "MPD_APPLIED_FIELD_OUT_OF_BAND",         BreachDirection.AboveLimit },
        { "MPD_ARC_CURRENT_OUT_OF_BAND",           BreachDirection.AboveLimit },
        { "MPD_CATHODE_OVERHEAT",                  BreachDirection.AboveLimit },
        { "MPD_GEOMETRY_INVERTED",                 BreachDirection.AboveLimit },
        { "MPD_ONSET_PARAMETER_EXCESSIVE",         BreachDirection.AboveLimit },
        { "MPD_THRUST_EFFICIENCY_LOW",             BreachDirection.AboveLimit },
        { "PPT_ABLATION_RATE_EXCESSIVE",           BreachDirection.AboveLimit },
        { "PPT_CAPACITOR_ENERGY_OUT_OF_BAND",      BreachDirection.AboveLimit },
        { "PPT_IMPULSE_BIT_BELOW_FLOOR",           BreachDirection.AboveLimit },
        { "PPT_NO_BREAKDOWN",                      BreachDirection.AboveLimit },
        { "RESISTOJET_AREA_RATIO_OUT_OF_BAND",     BreachDirection.AboveLimit },
        { "RESISTOJET_EFFICIENCY_BELOW_FLOOR",     BreachDirection.AboveLimit },
        { "RESISTOJET_FROZEN_FLOW_LOSS_EXCESSIVE", BreachDirection.AboveLimit },
        { "RESISTOJET_HEAT_LEAK_EXCEEDS_INPUT",    BreachDirection.AboveLimit },
        { "RESISTOJET_HEATER_TEMP_EXCEEDED",       BreachDirection.AboveLimit },
        { "RESISTOJET_ISP_BELOW_FLOOR",            BreachDirection.AboveLimit },
        { "RESISTOJET_NOZZLE_UNCHOKED",            BreachDirection.AboveLimit },
        { "RESISTOJET_PROPELLANT_DECOMPOSITION",   BreachDirection.AboveLimit },
        { "RESISTOJET_RADIATION_FRACTION_EXCESSIVE", BreachDirection.AboveLimit },
        { "RESISTOJET_THRUST_BELOW_MIN",           BreachDirection.AboveLimit },

        // ── Marine pillar — MarineGates.cs + per-mode objective exception
        //    captures (DisplacementHullObjective / PlaningHullObjective) ───
        { "DEPTH_RATING_EXCEEDED",                 BreachDirection.AboveLimit },
        { "HOLTROP_BEAM_TO_DRAFT_OUT_OF_BAND",     BreachDirection.AboveLimit },
        { "HOLTROP_FORM_FACTOR_ABOVE_BAND",        BreachDirection.AboveLimit },
        { "HOLTROP_FROUDE_OUT_OF_BAND",            BreachDirection.AboveLimit },
        { "HOLTROP_LENGTH_TO_BEAM_OUT_OF_BAND",    BreachDirection.AboveLimit },
        { "HOLTROP_SEMI_DISPLACEMENT_REGIME",      BreachDirection.AboveLimit },
        { "HOLTROP_WAVE_MAKING_DOMINANT",          BreachDirection.AboveLimit },
        { "HULL_BUCKLING_INSUFFICIENT",            BreachDirection.AboveLimit },
        { "HULL_BUCKLING_SF_MARGINAL",             BreachDirection.AboveLimit },
        { "HULL_BUOYANCY_NEGATIVE",                BreachDirection.AboveLimit },
        { "HULL_CG_CB_OFFSET_LARGE",               BreachDirection.AboveLimit },
        { "HULL_DRAG_ABOVE_BAND",                  BreachDirection.AboveLimit },
        { "HULL_FINENESS_EXTREME",                 BreachDirection.AboveLimit },
        { "HULL_FINENESS_OUT_OF_BAND",             BreachDirection.AboveLimit },
        { "HULL_LPBF_WALL_TOO_THIN",               BreachDirection.AboveLimit },
        { "HULL_WATERTIGHT_INTEGRITY",             BreachDirection.AboveLimit },
        { "MARINE_DESIGN_INVALID",                 BreachDirection.AboveLimit },
        { "MARINE_EVAL_EXCEPTION",                 BreachDirection.AboveLimit },
        { "PLANING_DEADRISE_OUT_OF_BAND",          BreachDirection.AboveLimit },
        { "PLANING_LCG_OUT_OF_BAND",               BreachDirection.AboveLimit },
        { "PLANING_RESISTANCE_ABOVE_BAND",         BreachDirection.AboveLimit },
        { "PLANING_SPEED_COEFFICIENT_OUT_OF_BAND", BreachDirection.AboveLimit },
        { "PLANING_TRIM_OUT_OF_BAND",              BreachDirection.AboveLimit },
        { "PLANING_WETTED_LENGTH_TO_BEAM_OUT_OF_BAND", BreachDirection.AboveLimit },

        // ── Nuclear pillar — NuclearGates.cs ──────────────────────────────
        { "NTR_BIMODAL_ALTERNATOR_RPM_OUT_OF_BAND",       BreachDirection.AboveLimit },
        { "NTR_BIMODAL_BRAYTON_THERMAL_EFFICIENCY_LOW",   BreachDirection.AboveLimit },
        { "NTR_BIMODAL_BRAYTON_TURBINE_OVERTEMP",         BreachDirection.AboveLimit },
        { "NTR_BIMODAL_REACTOR_TAP_EXCESSIVE",            BreachDirection.AboveLimit },
        { "NTR_CHAMBER_PRESSURE_TOO_LOW",                 BreachDirection.AboveLimit },
        { "NTR_FUEL_CTE_MISMATCH",                        BreachDirection.AboveLimit },
        { "NTR_FUEL_PIN_OVERTEMP",                        BreachDirection.AboveLimit },
        { "NTR_FUEL_PIN_SURFACE_OVERTEMP",                BreachDirection.AboveLimit },
        { "NTR_HOT_CHANNEL_FACTOR_EXCESSIVE",             BreachDirection.AboveLimit },
        { "NTR_K_EFF_OUT_OF_BAND",                        BreachDirection.AboveLimit },
        { "NTR_PER_PIN_POWER_ABOVE_BAND",                 BreachDirection.AboveLimit },
        { "NTR_PIN_PITCH_RATIO_OUT_OF_BAND",              BreachDirection.AboveLimit },
        { "NTR_REACTOR_OVERTEMP",                         BreachDirection.AboveLimit },
        { "NTR_REGEN_COOLING_BUDGET",                     BreachDirection.AboveLimit },
        { "NTR_THERMAL_FLUX_EXCEEDED",                    BreachDirection.AboveLimit },
    };

    /// <summary>
    /// All registered <c>ConstraintId</c>s and their per-gate
    /// <see cref="BreachDirection"/>. Foundation PR seeds every entry at
    /// <see cref="BreachDirection.AboveLimit"/>; per-pillar refinement
    /// PRs change individual entries to <see cref="BreachDirection.BelowLimit"/>
    /// or <see cref="BreachDirection.Categorical"/> per the emit-site
    /// predicate.
    /// </summary>
    public static IReadOnlyDictionary<string, BreachDirection> Map => s_map;

    /// <summary>
    /// Returns the <see cref="BreachDirection"/> for the given
    /// <c>ConstraintId</c>. Unknown IDs (e.g. test fixtures with synthetic
    /// IDs, or future gates not yet registered) fall back to
    /// <see cref="BreachDirection.AboveLimit"/>. Lookups are case-sensitive
    /// (Ordinal comparison).
    /// </summary>
    public static BreachDirection For(string constraintId) =>
        s_map.TryGetValue(constraintId, out var direction) ? direction : BreachDirection.AboveLimit;
}
