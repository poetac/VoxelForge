// SolarCollectorDesign.cs — Sprint ST.W1 solar-thermal collector design.
//
// Sized to bracket both ends of the cluster: a typical 4 m² domestic
// flat-plate panel and an Andasol-class parabolic-trough loop.

using System;

namespace Voxelforge.SolarThermal;

/// <summary>
/// Design parameters for a solar-thermal collector (Sprint ST.W1
/// scaffold). Standalone — does not integrate with the
/// <c>IEngine&lt;,,&gt;</c> stack yet.
/// </summary>
/// <param name="Kind">Collector topology.</param>
/// <param name="ApertureArea_m2">Aperture area A [m²]. Flat-plate
/// physical area; parabolic-trough aperture-after-concentration area.</param>
/// <param name="DirectNormalIrradiance_W_m2">DNI = G [W/m²] for
/// parabolic-trough; effectively GHI for flat-plate (Wave-1 simplification).</param>
/// <param name="CollectorTemperature_C">T_collector [°C] — fluid-side
/// operating temperature at the receiver.</param>
/// <param name="AmbientTemperature_C">T_ambient [°C].</param>
internal sealed record SolarCollectorDesign(
    SolarCollectorKind Kind,
    double ApertureArea_m2,
    double DirectNormalIrradiance_W_m2,
    double CollectorTemperature_C,
    double AmbientTemperature_C)
{
    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (Kind == SolarCollectorKind.None)
            throw new ArgumentException(
                "Kind must be set (None sentinel is reserved).", nameof(Kind));
        if (ApertureArea_m2 <= 0)
            throw new ArgumentException("ApertureArea_m2 must be > 0.",
                nameof(ApertureArea_m2));
        if (DirectNormalIrradiance_W_m2 < 0)
            throw new ArgumentException(
                "DirectNormalIrradiance_W_m2 must be ≥ 0 (negative is non-physical).",
                nameof(DirectNormalIrradiance_W_m2));
        if (CollectorTemperature_C < AmbientTemperature_C)
            throw new ArgumentException(
                $"CollectorTemperature_C ({CollectorTemperature_C:F1}) must be ≥ "
              + $"AmbientTemperature_C ({AmbientTemperature_C:F1}); otherwise the "
              + "collector is a net heat sink.",
                nameof(CollectorTemperature_C));
    }
}
