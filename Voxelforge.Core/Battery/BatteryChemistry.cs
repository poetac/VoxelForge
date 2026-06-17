// BatteryChemistry.cs — Sprint BP.W1 battery chemistry discriminator.
//
// Wave-1 ships two commercial lithium-ion chemistries: NMC-class (high
// energy density, sloped OCV(SoC), EV-class) and LFP / LiFePO₄ (lower
// energy density, flat OCV plateau, longer cycle life, stationary +
// commercial-EV class). Wave-2+ will add NCA, LTO (lithium titanate),
// solid-state, sodium-ion, and lead-acid as the storage-portfolio grows.
//
// Chemistry choice drives:
//   - nominal cell voltage + OCV span (V_min, V_max)
//   - per-cell internal resistance R_int (cluster mid-band)
//   - per-cell nominal capacity (cluster, not vendor-bound)
//
// All three property triplets live on BatteryChemistryProperties,
// queried via BatteryChemistryRegistry.For(BatteryChemistry).

namespace Voxelforge.Battery;

/// <summary>
/// Lithium-class chemistry for a battery cell / pack (Sprint BP.W1).
/// </summary>
internal enum BatteryChemistry
{
    /// <summary>Degenerate sentinel — not a valid design choice.</summary>
    None = 0,

    /// <summary>
    /// Nickel-manganese-cobalt (NMC) — high energy density, sloped OCV.
    /// Tesla Model 3 / Y / S long-range pack class. Nominal 3.7 V/cell,
    /// OCV ∈ [3.0, 4.2] V, R_int ≈ 30 mΩ per cell.
    /// </summary>
    NickelManganeseCobalt = 1,

    /// <summary>
    /// Lithium iron phosphate (LFP / LiFePO₄) — flat OCV plateau, lower
    /// energy density but longer cycle life + cheaper + safer. Tesla
    /// Model 3 SR / BYD Blade class. Nominal 3.2 V/cell, OCV ∈ [2.5, 3.65]
    /// V (with a long flat region 3.2-3.3 V), R_int ≈ 20 mΩ per cell.
    /// </summary>
    LithiumIronPhosphate = 2,
}
