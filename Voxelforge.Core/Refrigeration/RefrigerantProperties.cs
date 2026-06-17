// RefrigerantProperties.cs — Sprint RFG.W1 per-refrigerant property
// registry.

using System;

namespace Voxelforge.Refrigeration;

/// <summary>
/// Cluster-anchored properties per refrigerant (Sprint RFG.W1).
/// </summary>
/// <param name="SecondLawEfficiency">η_2nd-law ∈ (0, 1] [-]. The
/// real cycle's COP relative to the Carnot bound at the same
/// reservoir temperatures.</param>
/// <param name="GlobalWarmingPotential">GWP [-] vs CO₂ at 100-year
/// horizon. R-744 = 1; R-134a = 1430; R-1234yf < 1.</param>
internal sealed record RefrigerantProperties(
    double SecondLawEfficiency,
    double GlobalWarmingPotential);

/// <summary>Static registry of per-refrigerant properties.</summary>
internal static class RefrigerantRegistry
{
    /// <summary>R-134a cluster — medium-T HVAC default.</summary>
    internal static readonly RefrigerantProperties R134a =
        new(SecondLawEfficiency: 0.55, GlobalWarmingPotential: 1430.0);

    /// <summary>R-410A cluster — residential split AC.</summary>
    internal static readonly RefrigerantProperties R410A =
        new(SecondLawEfficiency: 0.58, GlobalWarmingPotential: 2088.0);

    /// <summary>R-1234yf cluster — automotive low-GWP replacement.</summary>
    internal static readonly RefrigerantProperties R1234yf =
        new(SecondLawEfficiency: 0.55, GlobalWarmingPotential: 0.4);

    /// <summary>R-744 (CO₂ transcritical) cluster.</summary>
    internal static readonly RefrigerantProperties R744 =
        new(SecondLawEfficiency: 0.50, GlobalWarmingPotential: 1.0);

    /// <summary>Resolve per-refrigerant properties.</summary>
    internal static RefrigerantProperties For(Refrigerant refrigerant) => refrigerant switch
    {
        Refrigerant.R134a   => R134a,
        Refrigerant.R410A   => R410A,
        Refrigerant.R1234yf => R1234yf,
        Refrigerant.R744    => R744,
        _ => throw new ArgumentOutOfRangeException(nameof(refrigerant), refrigerant,
                $"Unknown Refrigerant '{refrigerant}'."),
    };
}
