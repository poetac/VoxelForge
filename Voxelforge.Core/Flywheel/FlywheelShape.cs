// FlywheelShape.cs — Sprint FW.W1 rotor-shape discriminator.
//
// Wave-1 ships two canonical rotor topologies + a per-shape "shape
// factor" K that drives the specific energy E/m. Wave-2+ will add
// constant-stress (Stodola) optimal-shape and laminated multi-rim.

namespace Voxelforge.Flywheel;

/// <summary>
/// Flywheel rotor shape (Sprint FW.W1).
/// </summary>
internal enum FlywheelShape
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>
    /// Thin rim — all mass concentrated near the outer radius. Shape
    /// factor K = 0.5. I = m·R² (assumed concentrated at R).
    /// </summary>
    ThinRim = 1,

    /// <summary>
    /// Solid uniform disk — mass distributed uniformly over the radius.
    /// Shape factor K ≈ 0.606. I = ½·m·R².
    /// </summary>
    SolidDisk = 2,
}
