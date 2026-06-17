// ThermoelectricMaterial.cs — Sprint TEG.W1 thermoelectric material
// discriminator.
//
// Wave-1 ships the three commercial / space-flight TEG materials,
// each at a distinct hot-side temperature envelope:
//
//   Bi₂Te₃ (low-T, < 200 °C):  ZT ≈ 1.0, η_max ≈ 5 %. Ground-based
//                               waste-heat recovery + Peltier coolers.
//   PbTe (mid-T, 300-500 °C):   ZT ≈ 1.5, η_max ≈ 10 %. Voyager,
//                               Cassini RTG era.
//   SiGe (high-T, > 600 °C):    ZT ≈ 0.8, η_max ≈ 8 %. Modern GPHS-RTG
//                               (Curiosity, Perseverance MMRTG segments).
//
// Wave-2+ will add half-Heusler, skutterudite, and clathrate
// nanostructured cluster materials (ZT > 2 lab-scale).

namespace Voxelforge.Thermoelectric;

/// <summary>
/// Sub-classification of TEG material (Sprint TEG.W1).
/// </summary>
internal enum ThermoelectricMaterial
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>Bi₂Te₃ — low-temperature commercial cluster.</summary>
    BismuthTelluride = 1,

    /// <summary>PbTe — mid-temperature space-RTG cluster (Voyager / Cassini era).</summary>
    LeadTelluride = 2,

    /// <summary>SiGe — high-temperature space-RTG cluster (modern GPHS era).</summary>
    SiliconGermanium = 3,
}
