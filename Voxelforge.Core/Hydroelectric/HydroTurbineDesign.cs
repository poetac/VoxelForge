// HydroTurbineDesign.cs — Sprint HE.W1 hydroelectric turbine design record.
//
// Sized to bracket Three Gorges Dam Francis units (700 MW @ 80 m head,
// 850 m³/s). Standalone scaffold under Voxelforge.Hydroelectric.

using System;

namespace Voxelforge.Hydroelectric;

/// <summary>
/// Design parameters for a hydroelectric turbine + generator unit
/// (Sprint HE.W1 scaffold). Standalone — does not integrate with the
/// <c>IEngine&lt;,,&gt;</c> stack yet.
/// </summary>
/// <param name="Kind">Turbine topology.</param>
/// <param name="Head_m">Net hydraulic head H [m].</param>
/// <param name="VolumetricFlowRate_m3s">Q [m³/s] through the turbine.</param>
/// <param name="GeneratorEfficiency">η_generator [-] — combined generator + power-electronics + transformer.</param>
/// <param name="WaterDensity_kgm3">ρ [kg/m³]. Default 1000 (fresh water at standard temperature).</param>
internal sealed record HydroTurbineDesign(
    HydroTurbineKind Kind,
    double Head_m,
    double VolumetricFlowRate_m3s,
    double GeneratorEfficiency,
    double WaterDensity_kgm3 = 1000.0)
{
    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (Kind == HydroTurbineKind.None)
            throw new ArgumentException(
                "Kind must be set (None sentinel is reserved).", nameof(Kind));
        if (Head_m <= 0)
            throw new ArgumentException("Head_m must be > 0.", nameof(Head_m));
        if (VolumetricFlowRate_m3s <= 0)
            throw new ArgumentException("VolumetricFlowRate_m3s must be > 0.",
                nameof(VolumetricFlowRate_m3s));
        if (GeneratorEfficiency <= 0 || GeneratorEfficiency > 1.0)
            throw new ArgumentException(
                "GeneratorEfficiency must be in (0, 1].", nameof(GeneratorEfficiency));
        if (WaterDensity_kgm3 <= 0)
            throw new ArgumentException("WaterDensity_kgm3 must be > 0.",
                nameof(WaterDensity_kgm3));
    }
}
