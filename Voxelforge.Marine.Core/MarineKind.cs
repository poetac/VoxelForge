// MarineKind.cs — sub-classification enum for the marine pillar.
//
// Analogous to AirbreathingEngineKind on the air-breathing side.
// Sub-classifications (AUV vs displacement hull vs planing hull) live
// here so IEngine<MarineDesign,MarineConditions,MarineResult> only needs
// to dispatch on the family ("marine"), while variant-specific behaviour
// keys on Kind. Wave 1 ships AuvMidBody only.

namespace Voxelforge.Marine;

/// <summary>
/// Sub-classification of marine vehicle within the marine pillar.
/// Wave 1 ships <see cref="AuvMidBody"/> only. Surface hulls
/// (Wave 2+) and propulsion devices (Wave 3+) are reserved.
/// </summary>
public enum MarineKind
{
    /// <summary>Degenerate sentinel — not a valid design kind.</summary>
    None = 0,

    /// <summary>
    /// Fully-submerged AUV with Myring-faired cylindrical pressure hull.
    /// M1 variant. Wave 1 + Wave 2 (CylindricalHemi
    /// added as a HullFamily within this kind).
    /// </summary>
    AuvMidBody = 1,

    /// <summary>
    /// Planing surface hull (hard-chine prismatic). Wave 3 (Sprint M.W3).
    /// Pairs with <see cref="HullFamily.Planing"/>;
    /// physics via Savitsky 1964. Distinct dispatch path from
    /// <see cref="AuvMidBody"/>: no submerged-pressure, no Myring/CylHemi
    /// fairing, no buckling-of-pressure-hull gate.
    /// </summary>
    SurfaceHull = 2,

    /// <summary>
    /// Displacement-mode round-bilge surface hull. Wave 3 (Sprint M.W4).
    /// Pairs with <see cref="HullFamily.DisplacementSurface"/>; physics via
    /// simplified Holtrop-Mennen 1984. Bridges the AUV (Wave-1/2) and
    /// Planing (Wave-3) regimes. Applicable to Fn ∈ [0.05, 0.40]
    /// (cargo / fishing / motor-vessel cluster). Distinct dispatch path
    /// from <see cref="AuvMidBody"/> (no submerged pressure hull) and from
    /// <see cref="SurfaceHull"/> (different resistance regime).
    /// </summary>
    DisplacementSurface = 3,
}
