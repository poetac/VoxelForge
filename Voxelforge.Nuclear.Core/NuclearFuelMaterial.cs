// NuclearFuelMaterial.cs — Sprint NU.W4 fuel material discriminator.
//
// NERVA-class NTR designs span three published fuel material families:
//
//   UO2_Cermet  — UO₂ particles dispersed in a refractory metal matrix
//                 (W or Mo). High thermal conductivity from the matrix
//                 phase; sustained T_centerline up to ~3200 K. Wave-1
//                 default. NERVA NRX-A6 historical baseline.
//
//   UC2_Graphite — Uranium carbide in a graphite matrix. Higher melting
//                 point than cermet (~3500 K centreline) but lower
//                 thermal conductivity, leading to higher ΔT_cs gradients.
//                 NERVA early designs (Kiwi, Phoebus).
//
//   UN_Refractory — Uranium nitride pin in refractory cladding. Very
//                 high conductivity (~25 W/(m·K)) but melting point
//                 lower (~2800 K). Modern advanced-cermet concepts
//                 (e.g. NASA HEU MELTED tier proposals).
//
// Per-material data lives in the static dictionary on
// <see cref="NuclearFuelMaterials"/>; the design record carries only
// the enum discriminator. Wave-1/Wave-2 designs that leave the field at
// <see cref="None"/> default to UO2_Cermet behaviour (the prior Wave-2
// per-pin model constants).

namespace Voxelforge.Nuclear;

/// <summary>
/// Fuel material discriminator for the per-pin heat-conduction model
/// (Sprint NU.W2). Drives the material-specific thermal conductivity and
/// hard-gate temperature limit.
/// </summary>
public enum NuclearFuelMaterial
{
    /// <summary>
    /// Sentinel — non-W2 designs default here. The per-pin model uses
    /// the UO₂-cermet anchors when this is selected (preserves Wave-1/W2
    /// behaviour bit-identically).
    /// </summary>
    None = 0,

    /// <summary>
    /// UO₂ in W or Mo matrix. k_eff ≈ 16 W/(m·K), T_max ≈ 3200 K. NERVA
    /// NRX-A6 historical baseline.
    /// </summary>
    UO2Cermet = 1,

    /// <summary>
    /// Uranium carbide in graphite matrix. k_eff ≈ 8 W/(m·K), T_max
    /// ≈ 3500 K. NERVA Kiwi / Phoebus early designs.
    /// </summary>
    UC2Graphite = 2,

    /// <summary>
    /// Uranium nitride in refractory cladding. k_eff ≈ 25 W/(m·K),
    /// T_max ≈ 2800 K. Modern advanced-cermet concepts.
    /// </summary>
    UNRefractory = 3,
}

/// <summary>
/// Per-material data for the three nuclear fuel materials. Used by the
/// per-pin heat-conduction model and by the
/// <c>NTR_FUEL_PIN_OVERTEMP</c> gate to pick the right ΔT_cs anchor and
/// max-T limit.
/// </summary>
public sealed record NuclearFuelMaterialData(
    double ThermalConductivity_WmK,
    double CenterlineTempLimit_K);

/// <summary>
/// Static registry of per-material thermal data.
/// </summary>
public static class NuclearFuelMaterials
{
    /// <summary>
    /// UO₂-cermet anchors. k_eff at ~2500 K matrix-dominated; T_max from
    /// Lyon NASA-CR-72757 sustained-operation envelope.
    /// </summary>
    public static readonly NuclearFuelMaterialData UO2Cermet =
        new(ThermalConductivity_WmK: 16.0, CenterlineTempLimit_K: 3200.0);

    /// <summary>
    /// UC₂-graphite anchors. Lower conductivity than cermet (single-phase
    /// carbide; no metal matrix). T_max higher because graphite + UC₂
    /// melt above 3500 K. Source: Bennett 1972 NERVA Kiwi/Phoebus.
    /// </summary>
    public static readonly NuclearFuelMaterialData UC2Graphite =
        new(ThermalConductivity_WmK: 8.0, CenterlineTempLimit_K: 3500.0);

    /// <summary>
    /// UN-refractory anchors. High conductivity from the nitride matrix
    /// but lower T_max because UN dissociates ~2900 K.
    /// </summary>
    public static readonly NuclearFuelMaterialData UNRefractory =
        new(ThermalConductivity_WmK: 25.0, CenterlineTempLimit_K: 2800.0);

    /// <summary>
    /// Resolve material data, with the <see cref="NuclearFuelMaterial.None"/>
    /// sentinel mapping to <see cref="UO2Cermet"/> for backwards compat
    /// with Wave-1 / Wave-2 designs.
    /// </summary>
    public static NuclearFuelMaterialData For(NuclearFuelMaterial material) => material switch
    {
        NuclearFuelMaterial.UC2Graphite  => UC2Graphite,
        NuclearFuelMaterial.UNRefractory => UNRefractory,
        // None defaults to UO2-cermet for Wave-1/Wave-2 compat.
        _                                => UO2Cermet,
    };
}
