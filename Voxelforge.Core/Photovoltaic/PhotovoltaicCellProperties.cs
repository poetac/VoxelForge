// PhotovoltaicCellProperties.cs — Sprint PV.W1 per-cell-type property
// registry.

using System;

namespace Voxelforge.Photovoltaic;

/// <summary>
/// Cluster-anchored per-cell properties at Standard Test Conditions
/// (STC: 1000 W/m² irradiance, 25 °C, AM1.5G spectrum).
/// </summary>
/// <param name="ShortCircuitCurrent_A">I_sc per cell at STC [A].</param>
/// <param name="OpenCircuitVoltage_V">V_oc per cell at STC [V].</param>
/// <param name="FillFactor">FF = P_mp / (V_oc · I_sc) [-]. Silicon
/// cluster mid-band 0.75-0.82.</param>
/// <param name="CurrentTemperatureCoefficient_perK">α_I [1/K] —
/// fractional I_sc change per K above 25 °C. Small (+0.05 %/K).</param>
/// <param name="VoltageTemperatureCoefficient_V_perK">β_V [V/K] —
/// absolute V_oc change per K. Negative (~ −2 mV/K).</param>
internal sealed record PhotovoltaicCellProperties(
    double ShortCircuitCurrent_A,
    double OpenCircuitVoltage_V,
    double FillFactor,
    double CurrentTemperatureCoefficient_perK,
    double VoltageTemperatureCoefficient_V_perK);

/// <summary>Static registry of per-cell-type properties at STC.</summary>
internal static class PhotovoltaicCellRegistry
{
    /// <summary>Monocrystalline silicon cluster — SunPower Maxeon class.</summary>
    internal static readonly PhotovoltaicCellProperties Monocrystalline =
        new(ShortCircuitCurrent_A:              6.20,
            OpenCircuitVoltage_V:               0.68,
            FillFactor:                         0.80,
            CurrentTemperatureCoefficient_perK: 0.0005,
            VoltageTemperatureCoefficient_V_perK: -0.0023);

    /// <summary>Polycrystalline silicon cluster.</summary>
    internal static readonly PhotovoltaicCellProperties Polycrystalline =
        new(ShortCircuitCurrent_A:              5.80,
            OpenCircuitVoltage_V:               0.62,
            FillFactor:                         0.76,
            CurrentTemperatureCoefficient_perK: 0.0006,
            VoltageTemperatureCoefficient_V_perK: -0.0028);

    /// <summary>Resolve per-cell-type properties.</summary>
    internal static PhotovoltaicCellProperties For(PhotovoltaicCellType type) => type switch
    {
        PhotovoltaicCellType.Monocrystalline => Monocrystalline,
        PhotovoltaicCellType.Polycrystalline => Polycrystalline,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type,
                $"Unknown PhotovoltaicCellType '{type}'."),
    };
}
