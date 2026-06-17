// ArcjetElectrodeMaterial.cs — Wave-2 Arcjet anode/cathode material.
//
// Drives the maximum sustained electrode-wall temperature the Hard gate
// `ARCJET_ANODE_OVERHEAT` enforces. Three families cover the MR-509
// ATOS / Aerojet 1.8 kW / Velarc 30 kW cluster (Sutton & Biblarz 9e §16.3).

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Anode (and by convention cathode) wall material for an Arcjet thruster.
/// Drives the Hard gate <c>ARCJET_ANODE_OVERHEAT</c>. Refractory choice
/// dominates because the constricted arc concentrates ~40 % of input power
/// on a small downstream electrode area.
/// </summary>
public enum ArcjetElectrodeMaterial
{
    /// <summary>
    /// Sentinel for non-Arcjet designs. Resistojet / HET leave this default;
    /// Arcjet designs MUST select one of the real materials below.
    /// </summary>
    None = 0,

    /// <summary>
    /// Tungsten (W). T_max ≈ 3650 K sustained. Standard low-power-arcjet
    /// electrode (MR-509 ATOS, MR-510, Aerojet 26 kW class). High thermionic
    /// emissivity for the cathode tip and high melting temperature for the
    /// anode-side arc-attachment point.
    /// </summary>
    Tungsten = 1,

    /// <summary>
    /// Molybdenum (Mo). T_max ≈ 2890 K sustained. Lower cost than tungsten
    /// and easier to machine, but lower temperature ceiling — used in
    /// short-duration / low-power test articles. NASA Lewis ARC-30 lineage.
    /// </summary>
    Molybdenum = 2,

    /// <summary>
    /// Rhenium (Re) or Re-coated tungsten composite. T_max ≈ 3460 K. Trades
    /// a small temperature ceiling for substantially lower vapor pressure
    /// at operating temperature, extending electrode life beyond plain
    /// tungsten in long-duration ammonia / hydrazine-decomposition arcjets.
    /// </summary>
    Rhenium = 3,
}
