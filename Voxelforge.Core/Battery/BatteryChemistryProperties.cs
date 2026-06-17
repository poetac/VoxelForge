// BatteryChemistryProperties.cs — Sprint BP.W1 per-chemistry property
// registry. Mirrors the pattern used by NuclearFuelMaterial /
// UraniumEnrichmentTiers / HybridFuelRegistry.

using System;

namespace Voxelforge.Battery;

/// <summary>
/// Cluster-anchored property triplet for a battery chemistry.
/// </summary>
/// <param name="OcvMin_V">Open-circuit voltage at 0 % SoC [V].</param>
/// <param name="OcvMax_V">Open-circuit voltage at 100 % SoC [V].</param>
/// <param name="InternalResistance_Ohm">Per-cell internal resistance R_int [Ω].
/// Cluster mid-band at 25 °C; aged cells run higher.</param>
/// <param name="NominalCapacity_Ah">Per-cell nominal capacity at 1C
/// discharge [Ah]. Cluster mid-band — vendor cells span ±50 %.</param>
internal sealed record BatteryChemistryProperties(
    double OcvMin_V,
    double OcvMax_V,
    double InternalResistance_Ohm,
    double NominalCapacity_Ah);

/// <summary>Static registry of per-chemistry property triplets.</summary>
internal static class BatteryChemistryRegistry
{
    /// <summary>NMC cluster — Tesla 18650/21700 / Panasonic 4680-class.</summary>
    internal static readonly BatteryChemistryProperties NickelManganeseCobalt =
        new(OcvMin_V:               3.0,
            OcvMax_V:               4.2,
            InternalResistance_Ohm: 0.030,
            NominalCapacity_Ah:     5.0);

    /// <summary>LFP cluster — BYD Blade / CATL 280Ah-class (per-cell normalised).</summary>
    internal static readonly BatteryChemistryProperties LithiumIronPhosphate =
        new(OcvMin_V:               2.5,
            OcvMax_V:               3.65,
            InternalResistance_Ohm: 0.020,
            NominalCapacity_Ah:     5.0);

    /// <summary>Resolve chemistry properties.</summary>
    internal static BatteryChemistryProperties For(BatteryChemistry chemistry) => chemistry switch
    {
        BatteryChemistry.NickelManganeseCobalt => NickelManganeseCobalt,
        BatteryChemistry.LithiumIronPhosphate  => LithiumIronPhosphate,
        _ => throw new ArgumentOutOfRangeException(nameof(chemistry), chemistry,
                $"Unknown BatteryChemistry '{chemistry}'."),
    };
}
