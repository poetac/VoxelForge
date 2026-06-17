// ThermoelectricProperties.cs — Sprint TEG.W1 per-material property
// registry.

using System;

namespace Voxelforge.Thermoelectric;

/// <summary>
/// Cluster-anchored figure-of-merit + temperature envelope per TEG
/// material (Sprint TEG.W1).
/// </summary>
/// <param name="FigureOfMerit_ZT">Dimensionless ZT = α²·σ·T/k [-] — cluster
/// mid-band averaged across the operating temperature range.</param>
/// <param name="MinHotSideTemperature_K">Lower edge of valid T_hot envelope [K].</param>
/// <param name="MaxHotSideTemperature_K">Upper edge of valid T_hot envelope [K].</param>
internal sealed record ThermoelectricProperties(
    double FigureOfMerit_ZT,
    double MinHotSideTemperature_K,
    double MaxHotSideTemperature_K);

/// <summary>Static registry of per-material TEG properties.</summary>
internal static class ThermoelectricMaterialRegistry
{
    /// <summary>Bi₂Te₃ — low-T cluster.</summary>
    internal static readonly ThermoelectricProperties BismuthTelluride =
        new(FigureOfMerit_ZT: 1.0,
            MinHotSideTemperature_K: 273.0,
            MaxHotSideTemperature_K: 473.0);   // up to 200 °C

    /// <summary>PbTe — Voyager / Cassini-era mid-T cluster.</summary>
    internal static readonly ThermoelectricProperties LeadTelluride =
        new(FigureOfMerit_ZT: 1.5,
            MinHotSideTemperature_K: 473.0,    // 200 °C
            MaxHotSideTemperature_K: 773.0);   // 500 °C

    /// <summary>SiGe — modern GPHS-RTG high-T cluster.</summary>
    internal static readonly ThermoelectricProperties SiliconGermanium =
        new(FigureOfMerit_ZT: 0.8,
            MinHotSideTemperature_K: 773.0,    // 500 °C
            MaxHotSideTemperature_K: 1273.0);  // 1000 °C

    /// <summary>Resolve per-material TEG properties.</summary>
    internal static ThermoelectricProperties For(ThermoelectricMaterial material) => material switch
    {
        ThermoelectricMaterial.BismuthTelluride => BismuthTelluride,
        ThermoelectricMaterial.LeadTelluride    => LeadTelluride,
        ThermoelectricMaterial.SiliconGermanium => SiliconGermanium,
        _ => throw new ArgumentOutOfRangeException(nameof(material), material,
                $"Unknown ThermoelectricMaterial '{material}'."),
    };
}
