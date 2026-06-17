// GateKind.cs — Categorical metadata for FeasibilityGate constraint IDs.
//
// Z3 #16 / external-audit F-8 (2026-04-28): the FeasibilityGate.cs header
// described every gate as a "physics-based hard constraint", but ~15 of
// the 47 active gates are actually empirical bands calibrated against
// literature data (Sutton, Huzel & Huang, Karassik / Stepanoff, Dressler /
// Heister, vendor handbooks) — calibrations of model validity, not
// first-principles physics. Designs outside the band may still be
// physically valid; the model just can't predict their behaviour
// reliably.
//
// Adding GateKind makes the distinction explicit so consumers (UI,
// reports, external analysts) can filter on it — e.g., "show me only
// PhysicsLimit failures" vs "include EmpiricalBand warnings". The
// FeasibilityViolation record itself is intentionally untouched; the
// kind is a static lookup queried via FeasibilityGate.GetGateKind so
// the existing on-the-wire contract stays bit-identical.

namespace Voxelforge.Optimization;

/// <summary>
/// Categorical kind for a feasibility-gate <c>ConstraintId</c>. Lets
/// downstream consumers (UI, reports, sensitivity analysts) distinguish
/// hard physics failures from empirical-band warnings,
/// manufacturability floors, and design rules of thumb.
/// </summary>
/// <remarks>
/// External-audit F-8 (2026-04-28): the four buckets are deliberately
/// coarse. A finer taxonomy (e.g. splitting "structural physics" from
/// "thermal physics") would be more accurate but harder to triage at a
/// glance. The current bucketing answers the practical question every
/// reviewer asks first: "does this design fail real hardware, or does
/// it just sit outside our calibration data?"
/// </remarks>
public enum GateKind
{
    /// <summary>
    /// Strict physics limit derived from first-principles. The ground
    /// truth is unambiguous — exceeding the limit is a failure of
    /// physics (e.g., wall material has a known yield strength;
    /// exceeding it means the wall will fail; a pump with non-positive
    /// head rise is not a pump). If you had infinitely accurate CFD /
    /// FEA, the limit would still hold.
    /// </summary>
    /// <remarks>
    /// Examples: <c>WALL_TEMP</c>, <c>YIELD_EXCEEDED</c>,
    /// <c>BURST_MARGIN_INSUFFICIENT</c>, <c>COOLANT_T_EXCEEDED</c>,
    /// <c>NPSH_INSUFFICIENT</c>, <c>PUMP_PRESSURE_INVERTED</c>,
    /// <c>TURBINE_POWER_DEFICIT</c> (1st-law violation),
    /// <c>EXPANDER_TURBINE_ENTHALPY_DEFICIT</c>,
    /// <c>TURBINE_UNCHOKED</c> (γ-derived choke condition).
    /// </remarks>
    PhysicsLimit,

    /// <summary>
    /// Empirical correlation bound calibrated against literature data
    /// (Sutton, Huzel &amp; Huang, NASA SPs, Karassik / Stepanoff,
    /// Dressler / Heister, vendor handbooks). The "limit" represents
    /// the edge of the band where the model has data, not a hard
    /// physics wall. Designs outside the band may be physically valid;
    /// the model just can't predict their behaviour reliably.
    /// </summary>
    /// <remarks>
    /// Examples: <c>G_INJ_TOO_LOW</c> / <c>G_INJ_TOO_HIGH</c>
    /// (Sutton §6.3), <c>L_STAR_BELOW_PROPELLANT_MIN</c> (95% of pair
    /// nominal), <c>CONTRACTION_RATIO_OUT_OF_BAND</c>
    /// (Sutton §8.2 / Huzel &amp; Huang §4.1),
    /// <c>PINTLE_BLOCKAGE_OUT_OF_BAND</c> (Dressler / Heister),
    /// <c>PINTLE_TMR_OUT_OF_BAND</c>, <c>PUMP_SPECIFIC_SPEED_OFF_BAND</c>
    /// (Karassik §2.5 / Stepanoff §2.7), <c>ELEMENT_DENSITY_TOO_HIGH</c>
    /// (Huzel &amp; Huang §8.2 rule of thumb), <c>STABILITY_FAIL</c>
    /// (Crocco N-τ + chug / buzz / screech empirical screens),
    /// <c>ORSC_PREBURNER_OXCORROSION</c> (RD-180 50 K heritage margin),
    /// <c>LINEAR_AEROSPIKE_ASPECT_RATIO</c> (X-33 XRS-2200 heritage).
    /// </remarks>
    EmpiricalBand,

    /// <summary>
    /// Manufacturability or LPBF process floor. Not a physics limit —
    /// the design could exist in another manufacturing process — but
    /// for the project's LPBF target, the floor must be cleared. Also
    /// covers geometric printability concerns (overhang, trapped
    /// powder, drain paths) and voxel-fidelity floors that are
    /// pipeline-specific, not physics-fundamental.
    /// </summary>
    /// <remarks>
    /// Examples: <c>FEATURE_TOO_SMALL</c> (0.30 mm universal LPBF floor),
    /// <c>TPMS_CELL_FEATURE_TOO_SMALL</c> (2.0 mm strut floor),
    /// <c>VOXEL_RESOLUTION</c> (2/3-voxel rule),
    /// <c>OVERHANG_ANGLE_EXCEEDED</c>, <c>TRAPPED_POWDER_REGION</c>,
    /// <c>DRAIN_PATH_MISSING</c>, <c>CHANNEL_ASPECT_RATIO_EXCEEDED</c>
    /// (LPBF rib-buckling per EOS / Wolfram process maps),
    /// <c>AEROSPIKE_ELEMENT_CLEARANCE</c>,
    /// <c>MONOLITHIC_BODY_INTERSECTION</c>,
    /// <c>MONOLITHIC_TUBE_INTERSECTION</c>.
    /// </remarks>
    ManufacturabilityFloor,

    /// <summary>
    /// Heuristic / soft advisory derived from cross-cutting design
    /// rules of thumb. Less rigorous than <see cref="EmpiricalBand"/> —
    /// may be disabled, tuned, or over-ridden by an experienced
    /// designer, often with user-specified thresholds. Frequently opt-in.
    /// </summary>
    /// <remarks>
    /// Examples: <c>IGNITER_MISSING</c>,
    /// <c>IGNITER_ENERGY_INSUFFICIENT</c>,
    /// <c>IGNITER_MODALITY_UNSUITABLE</c>,
    /// <c>INSTRUMENTATION_TAP_INTERFERENCE</c>,
    /// <c>INSTRUMENTATION_THERMAL_BRIDGE_RISK</c>,
    /// <c>TPMS_AND_MANIFOLD_OVERLAP</c> (Z3 #20: pre-empts pitfall #2
    /// for small-chamber TPMS designs),
    /// <c>CHILLDOWN_BUDGET_EXCEEDED</c> (user-supplied budget),
    /// <c>HARD_START_RISK</c> (user-supplied factor),
    /// <c>SHAFT_WHIRL</c> (±20% bandwidth heuristic).
    /// </remarks>
    AdvisoryHeuristic,
}
