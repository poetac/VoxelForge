// AnodeMaterial.cs — Wave-2 HET anode wall material.
//
// Drives the maximum sustained anode-wall temperature the structural
// gate `HET_ANODE_OVERHEAT` enforces. Three families cover the BPT-4000
// / SPT-100 / PPS-1350 cluster.

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Anode wall / discharge-channel wall material for a Hall-Effect
/// Thruster. Drives the Hard gate <c>HET_ANODE_OVERHEAT</c>.
/// </summary>
public enum AnodeMaterial
{
    /// <summary>
    /// Sentinel for non-HET designs. Resistojet leaves this default;
    /// HET designs MUST select one of the real materials below.
    /// </summary>
    None = 0,

    /// <summary>
    /// Polycrystalline graphite (BPT-4000, SPT-100 wall material).
    /// T_max ≈ 2000 K sustained operation.
    /// </summary>
    Graphite = 1,

    /// <summary>
    /// Boron nitride (PPS-1350, SPT-140 wall material). T_max ≈ 1500 K
    /// sustained — lower than graphite but lower secondary-electron
    /// emission yield, which improves discharge stability.
    /// </summary>
    BoronNitride = 2,

    /// <summary>
    /// Alumina-silicon-carbide composite (research thrusters; experimental
    /// long-life articles). T_max ≈ 1900 K.
    /// </summary>
    AluminaSiC = 3,
}
