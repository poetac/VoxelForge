// HydroTurbineProperties.cs — Sprint HE.W1 per-kind operational envelope
// + cluster-anchored peak hydraulic efficiency.

using System;

namespace Voxelforge.Hydroelectric;

/// <summary>
/// Cluster-anchored properties per hydroelectric turbine kind (Sprint
/// HE.W1).
/// </summary>
/// <param name="MinimumHead_m">Lower edge of the head validity envelope [m].</param>
/// <param name="MaximumHead_m">Upper edge of the head validity envelope [m].</param>
/// <param name="PeakHydraulicEfficiency">Peak η_turbine at design point [-].
/// Cluster mid-band; real units span ±5 %.</param>
internal sealed record HydroTurbineProperties(
    double MinimumHead_m,
    double MaximumHead_m,
    double PeakHydraulicEfficiency);

/// <summary>Static registry of per-kind hydraulic-turbine properties.</summary>
internal static class HydroTurbineRegistry
{
    /// <summary>Pelton cluster.</summary>
    internal static readonly HydroTurbineProperties Pelton =
        new(MinimumHead_m: 200.0, MaximumHead_m: 2000.0, PeakHydraulicEfficiency: 0.90);

    /// <summary>Francis cluster.</summary>
    internal static readonly HydroTurbineProperties Francis =
        new(MinimumHead_m: 10.0,  MaximumHead_m: 700.0,  PeakHydraulicEfficiency: 0.93);

    /// <summary>Kaplan cluster.</summary>
    internal static readonly HydroTurbineProperties Kaplan =
        new(MinimumHead_m: 2.0,   MaximumHead_m: 40.0,   PeakHydraulicEfficiency: 0.91);

    /// <summary>Resolve per-kind properties.</summary>
    internal static HydroTurbineProperties For(HydroTurbineKind kind) => kind switch
    {
        HydroTurbineKind.Pelton  => Pelton,
        HydroTurbineKind.Francis => Francis,
        HydroTurbineKind.Kaplan  => Kaplan,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
                $"Unknown HydroTurbineKind '{kind}'."),
    };
}
