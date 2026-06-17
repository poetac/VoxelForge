// HeatPipeDesign.cs — Sprint HP.W1 heat-pipe design record.

using System;

namespace Voxelforge.HeatPipe;

/// <summary>
/// Design parameters for a single heat-pipe device (Sprint HP.W1 scaffold).
/// </summary>
/// <param name="Fluid">Working-fluid choice.</param>
/// <param name="InternalDiameter_m">D [m] — vapour-core inner diameter.</param>
/// <param name="Length_m">L [m] — overall heat-pipe length (evaporator + adiabatic + condenser).</param>
/// <param name="HeatThroughput_W">Q [W] — heat flux being transported.</param>
/// <param name="OperatingTemperature_K">T [K] — typical mean fluid temperature.</param>
internal sealed record HeatPipeDesign(
    HeatPipeFluid Fluid,
    double InternalDiameter_m,
    double Length_m,
    double HeatThroughput_W,
    double OperatingTemperature_K)
{
    /// <summary>Cross-sectional area of the vapour core [m²].</summary>
    public double CrossSectionArea_m2 => Math.PI * InternalDiameter_m * InternalDiameter_m * 0.25;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (Fluid == HeatPipeFluid.None)
            throw new ArgumentException(
                "Fluid must be set (None sentinel is reserved).", nameof(Fluid));
        if (InternalDiameter_m <= 0)
            throw new ArgumentException("InternalDiameter_m must be > 0.",
                nameof(InternalDiameter_m));
        if (Length_m <= 0)
            throw new ArgumentException("Length_m must be > 0.", nameof(Length_m));
        if (HeatThroughput_W <= 0)
            throw new ArgumentException("HeatThroughput_W must be > 0.",
                nameof(HeatThroughput_W));
        if (OperatingTemperature_K <= 0)
            throw new ArgumentException("OperatingTemperature_K must be > 0.",
                nameof(OperatingTemperature_K));
    }
}
