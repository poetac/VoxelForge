// TankShellType.cs — Sprint TANK.W1 pressure-vessel shell-construction
// discriminator.
//
// Wave-1 ships three commercial shell-material clusters at thin-wall
// geometry (R/t > 10). Wave-2+ will add thick-wall Lamé physics (for
// gun-barrel + high-Pratio H₂ vessels) and composite-overwrapped
// (Type-III metallic-liner-with-CF-overwrap) topologies.

namespace Voxelforge.Tankage;

/// <summary>
/// Sub-classification of pressure-vessel shell construction (Sprint TANK.W1).
/// </summary>
internal enum TankShellType
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>
    /// AISI 4130 chromoly steel monocoque. Falcon 9 stage-1 LOX / RP-1
    /// tank class. σ_y = 460 MPa, ρ = 7850 kg/m³.
    /// </summary>
    Steel4130 = 1,

    /// <summary>
    /// Aluminum 6061-T6 monocoque. Atlas V common-bulkhead class.
    /// σ_y = 280 MPa, ρ = 2700 kg/m³.
    /// </summary>
    Aluminum6061 = 2,

    /// <summary>
    /// Carbon-fibre / epoxy filament-wound composite (Type-IV). H₂
    /// automotive tank class. σ_uts = 1200 MPa (axial), ρ = 1500 kg/m³.
    /// </summary>
    CarbonFibreComposite = 3,
}
