// SolarCollectorProperties.cs — Sprint ST.W1 per-kind property registry.

using System;

namespace Voxelforge.SolarThermal;

/// <summary>
/// Cluster-anchored properties per solar-thermal collector kind
/// (Sprint ST.W1).
/// </summary>
/// <param name="HeatRemovalFactor">F_R [-] — collector heat-removal
/// factor (Hottel-Whillier-Bliss). Cluster mid-band 0.85-0.95.</param>
/// <param name="TransmittanceAbsorptanceProduct">τα [-] — combined
/// glazing transmittance · absorber absorptance.</param>
/// <param name="OverallLossCoefficient_W_m2K">U_L [W/(m²·K)] — heat-
/// loss coefficient referenced to the collector aperture area.</param>
/// <param name="ConcentrationRatio">CR [-] = A_aperture / A_receiver.
/// 1.0 for flat-plate (no concentration); 20-100 for parabolic trough.</param>
/// <param name="MaxOperatingTemperature_C">Upper edge of the cluster
/// validity envelope for T_collector [°C].</param>
internal sealed record SolarCollectorProperties(
    double HeatRemovalFactor,
    double TransmittanceAbsorptanceProduct,
    double OverallLossCoefficient_W_m2K,
    double ConcentrationRatio,
    double MaxOperatingTemperature_C);

/// <summary>Static registry of per-kind solar-collector properties.</summary>
internal static class SolarCollectorRegistry
{
    /// <summary>Flat-plate cluster (domestic hot-water class).</summary>
    internal static readonly SolarCollectorProperties FlatPlate =
        new(HeatRemovalFactor:                  0.90,
            TransmittanceAbsorptanceProduct:    0.75,
            OverallLossCoefficient_W_m2K:       5.0,
            ConcentrationRatio:                 1.0,
            MaxOperatingTemperature_C:          100.0);

    /// <summary>Parabolic-trough cluster (Andasol / Mojave Solar class).</summary>
    internal static readonly SolarCollectorProperties ParabolicTrough =
        new(HeatRemovalFactor:                  0.85,
            TransmittanceAbsorptanceProduct:    0.85,
            OverallLossCoefficient_W_m2K:       0.5,
            ConcentrationRatio:                 40.0,
            MaxOperatingTemperature_C:          450.0);

    /// <summary>Evacuated-tube cluster (Sprint ST.W2) — commercial domestic /
    /// process-heat class. Vacuum insulation cuts U_L vs flat-plate by ~ 3×.</summary>
    internal static readonly SolarCollectorProperties EvacuatedTube =
        new(HeatRemovalFactor:                  0.85,
            TransmittanceAbsorptanceProduct:    0.78,
            OverallLossCoefficient_W_m2K:       1.5,
            ConcentrationRatio:                 1.0,
            MaxOperatingTemperature_C:          200.0);

    /// <summary>Resolve per-kind properties.</summary>
    internal static SolarCollectorProperties For(SolarCollectorKind kind) => kind switch
    {
        SolarCollectorKind.FlatPlate       => FlatPlate,
        SolarCollectorKind.ParabolicTrough => ParabolicTrough,
        SolarCollectorKind.EvacuatedTube   => EvacuatedTube,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
                $"Unknown SolarCollectorKind '{kind}'."),
    };
}
