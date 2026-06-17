// BearingType.cs — Sprint FW.W2 flywheel bearing-system discriminator.
//
// Wave-2 adds bearing-loss physics, which is the dominant self-discharge
// mechanism for flywheel-energy-storage rotors. The bearing choice drives
// the windage + friction coefficient and therefore the "leak rate"
// (auto-discharge half-life) of a parked rotor.
//
// Cluster:
//   Mechanical ball bearings: τ_loss ~ minutes-to-hours (high leak).
//   Magnetic levitation:      τ_loss ~ days-to-weeks (very low leak).
//   Superconducting magnetic: τ_loss ~ weeks-to-months (lowest leak).

namespace Voxelforge.Flywheel;

/// <summary>
/// Bearing-system technology (Sprint FW.W2).
/// </summary>
internal enum BearingType
{
    /// <summary>
    /// Mechanical ball-/roller-bearing. Cheap, high losses. Cluster
    /// anchor: parasitic-drag torque ≈ 1 % of design torque.
    /// </summary>
    Mechanical = 0,

    /// <summary>
    /// Active magnetic-levitation bearing. Spec-grade for flywheel
    /// energy storage. Cluster anchor: ≈ 0.05 % of design torque
    /// (windage in vacuum + small magnetic-control current).
    /// </summary>
    MagneticLevitation = 1,

    /// <summary>
    /// Superconducting magnetic-levitation bearing (passive HTS YBCO
    /// pinning). Cryo-cooled. Cluster: ≈ 0.005 % of design torque.
    /// </summary>
    SuperconductingMagneticLevitation = 2,
}
