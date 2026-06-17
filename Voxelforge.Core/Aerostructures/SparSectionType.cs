// SparSectionType.cs — Sprint AS.W1 spar cross-section discriminator.
//
// Wave-1 ships three closed-form section geometries that cover the
// dominant wing-spar topologies. Wave-2+ will add I-beam, channel,
// hat-section, and composite-laminate-equivalent sections.

namespace Voxelforge.Aerostructures;

/// <summary>
/// Sub-classification of spar cross-section geometry (Sprint AS.W1).
/// </summary>
internal enum SparSectionType
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>
    /// Solid rectangular section (b · h). I_xx = b·h³/12. The simplest
    /// closed-form geometry; rarely used at scale (heavy) but pedagogically
    /// useful.
    /// </summary>
    SolidRectangular = 1,

    /// <summary>
    /// Hollow rectangular tube (outer b·h, inner (b-2t)·(h-2t)). I_xx =
    /// (b·h³ − (b-2t)(h-2t)³)/12. The dominant single-spar GA-aircraft
    /// (Cessna 172-class) topology.
    /// </summary>
    HollowRectangularBox = 2,

    /// <summary>
    /// Solid circular section (radius R). I_xx = π·R⁴/4. Used in
    /// helicopter blade spars + small-UAV booms.
    /// </summary>
    SolidCircular = 3,
}
