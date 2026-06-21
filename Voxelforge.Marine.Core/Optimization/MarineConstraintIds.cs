// MarineConstraintIds.cs — canonical string constants for every marine gate ID.
// No magic strings in gate predicates. Prefixed MARINE_* or HULL_* per ADR-026.

namespace Voxelforge.Marine.Optimization;

internal static class MarineConstraintIds
{
    // ── Hard gates ─────────────────────────────────────────────────────────────

    /// <summary>Hull net buoyancy is negative — vessel sinks.</summary>
    internal const string HullBuoyancyNegative = "HULL_BUOYANCY_NEGATIVE";

    /// <summary>Pressure-hull buckling safety factor below ASME UG-28 floor of 1.5.</summary>
    internal const string HullBucklingInsufficient = "HULL_BUCKLING_INSUFFICIENT";

    /// <summary>Wall thickness below LPBF minimum feature + margin (1.5 mm).</summary>
    internal const string HullWatertightIntegrity = "HULL_WATERTIGHT_INTEGRITY";

    /// <summary>Operating depth exceeds the declared depth rating of the design.</summary>
    internal const string DepthRatingExceeded = "DEPTH_RATING_EXCEEDED";

    /// <summary>Fineness ratio (L/D) outside hydrodynamically viable band [4, 15].</summary>
    internal const string FinenessTooExtremeHard = "HULL_FINENESS_EXTREME";

    // ── Advisory gates ─────────────────────────────────────────────────────────

    /// <summary>Total drag coefficient above Hoerner §6 upper band for slender bodies.</summary>
    internal const string HullDragAboveBand = "HULL_DRAG_ABOVE_BAND";

    /// <summary>Fineness ratio (L/D) outside Hoerner §6-2 optimum band [5, 12].</summary>
    internal const string FinenesRatioOutOfBand = "HULL_FINENESS_OUT_OF_BAND";

    /// <summary>CG/CB axial offset exceeds 5 % of diameter (AUV stability rule-of-thumb).</summary>
    internal const string CgCbOffsetLarge = "HULL_CG_CB_OFFSET_LARGE";

    /// <summary>LPBF hull wall thinner than 2.0 mm recommended floor.</summary>
    internal const string LpbfHullWallTooThin = "HULL_LPBF_WALL_TOO_THIN";

    /// <summary>Buckling safety factor in the 1.5–2.0 advisory band (passes hard gate, marginal).</summary>
    internal const string BucklingSfMarginal = "HULL_BUCKLING_SF_MARGINAL";

    // ── Planing (SurfaceHull) gates — Sprint M.W3 ──────────────────────────────

    /// <summary>Beam-Froude C_v outside the Savitsky planing envelope [1, 13].</summary>
    internal const string PlaningSpeedCoefficientOutOfBand = "PLANING_SPEED_COEFFICIENT_OUT_OF_BAND";

    /// <summary>Equilibrium trim angle τ outside the operational band [1°, 10°].</summary>
    internal const string PlaningTrimOutOfBand = "PLANING_TRIM_OUT_OF_BAND";

    /// <summary>Wetted-length-to-beam ratio λ outside the Savitsky-validity band [1, 6].</summary>
    internal const string PlaningWettedLengthToBeamOutOfBand = "PLANING_WETTED_LENGTH_TO_BEAM_OUT_OF_BAND";

    /// <summary>Deadrise angle β outside the typical hard-chine planing band [5°, 25°].</summary>
    internal const string PlaningDeadriseOutOfBand = "PLANING_DEADRISE_OUT_OF_BAND";

    /// <summary>LCG fraction outside the operational band [0.42, 0.58] (planing trim instability).</summary>
    internal const string PlaningLcgOutOfBand = "PLANING_LCG_OUT_OF_BAND";

    /// <summary>Resistance coefficient above the cluster ceiling — design is hydrodynamically inefficient.</summary>
    internal const string PlaningResistanceAboveBand = "PLANING_RESISTANCE_ABOVE_BAND";

    // ── DisplacementSurface (Holtrop-Mennen) gates — Sprint M.W4 ───────────────

    /// <summary>Froude number outside the Holtrop-Mennen displacement validity envelope [0.05, 0.40].</summary>
    internal const string HoltropFroudeOutOfBand = "HOLTROP_FROUDE_OUT_OF_BAND";

    /// <summary>Length-to-beam ratio outside the displacement-hull cluster band [4, 12].</summary>
    internal const string HoltropLengthToBeamOutOfBand = "HOLTROP_LENGTH_TO_BEAM_OUT_OF_BAND";

    /// <summary>Beam-to-draft ratio outside the displacement-hull cluster band [1.5, 5.0].</summary>
    internal const string HoltropBeamToDraftOutOfBand = "HOLTROP_BEAM_TO_DRAFT_OUT_OF_BAND";

    /// <summary>Form factor (1 + k₁) above 1.30 — cluster ceiling for round-bilge hulls.</summary>
    internal const string HoltropFormFactorAboveBand = "HOLTROP_FORM_FACTOR_ABOVE_BAND";

    /// <summary>Wave-making dominates total resistance (> 60 %) — operating above hump speed.</summary>
    internal const string HoltropWaveMakingDominant = "HOLTROP_WAVE_MAKING_DOMINANT";

    // ── DisplacementSurface — semi-displacement transition gate (Sprint M.W5) ──

    /// <summary>
    /// Sprint M.W5 advisory. Fires when the design is operating in the
    /// semi-displacement Froude band (Fn > 0.30) with the
    /// EnableSemiDisplacementCorrection flag active. Informational —
    /// flags that the high-Fn correction is being applied and the
    /// validation envelope is broadened beyond the pure-displacement
    /// regime.
    /// </summary>
    internal const string HoltropSemiDisplacementRegime = "HOLTROP_SEMI_DISPLACEMENT_REGIME";
}
