// HydrogenStorageKind.cs — Sprint H2T.W1 hydrogen-storage discriminator.
//
// Wave-1 ships the two dominant commercial storage modes:
//
//   CompressedGas — Type IV composite tank @ 350 or 700 bar (Toyota
//                   Mirai, Hyundai Nexo class). H₂ density 24 kg/m³ at
//                   350 bar / 40 kg/m³ at 700 bar (real gas, Z ≈ 1.3).
//   LiquidCryogenic — LH₂ at ~ 20 K, ~ 1 atm (BMW Hydrogen 7, NASA
//                     Centaur upper stage). H₂ density 70.85 kg/m³.
//                     Heat-leak through MLI → continuous boil-off.
//
// Wave-2+ will add metal hydride (LaNi₅ / MgH₂) and cryo-compressed
// hybrid (BMW HyperCar concept).

namespace Voxelforge.HydrogenStorage;

/// <summary>
/// Sub-classification of hydrogen storage mode (Sprint H2T.W1).
/// </summary>
internal enum HydrogenStorageKind
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>
    /// Compressed-gas storage in a Type-IV composite tank (carbon-fibre-
    /// wrapped polymer liner). Wave-1 baseline: 350 / 700 bar @ 25 °C.
    /// Real-gas density via the H₂ compressibility factor Z(P, T).
    /// </summary>
    CompressedGas = 1,

    /// <summary>
    /// Cryogenic liquid hydrogen (LH₂) at ~ 20 K, 1 atm. Stored in
    /// MLI-insulated double-walled tanks; continuous boil-off through
    /// thermal heat-leak.
    /// </summary>
    LiquidCryogenic = 2,

    /// <summary>
    /// Metal hydride storage (Sprint H2T.W2) — H₂ chemisorbed into a
    /// metal lattice (LaNi₅, MgH₂, Mg₂Ni). Effective ρ_H₂ ≈ 100 kg/m³
    /// (higher than compressed gas) but gravimetric efficiency low
    /// (1-7 % mass fraction H₂) because the bulk metal carries the
    /// volume. Refuel-via-heating (release H₂ at 200-400 °C); cold-
    /// storage is stable indefinitely (no boil-off). Used in research
    /// applications + back-up stationary storage.
    /// </summary>
    MetalHydride = 3,
}
