// SparMaterialProperties.cs — Sprint AS.W1 per-material registry.

using System;

namespace Voxelforge.Aerostructures;

/// <summary>
/// Spar material discriminator (Sprint AS.W1).
/// </summary>
internal enum SparMaterial
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>Al 7075-T6 — workhorse GA-aircraft + transport-spar alloy.</summary>
    Aluminum7075 = 1,

    /// <summary>Steel 4340 — high-strength agricultural / military spar.</summary>
    Steel4340 = 2,

    /// <summary>Carbon-fibre / epoxy composite — modern sailplane / UAV spar.</summary>
    CarbonFibreComposite = 3,
}

/// <summary>
/// Cluster-anchored material properties for spar materials.
/// </summary>
/// <param name="YieldStrength_Pa">σ_yield [Pa]. For composites this is
/// the design allowable (UTS / 2.5 SF).</param>
/// <param name="YoungsModulus_Pa">E [Pa] — used for deflection.</param>
/// <param name="Density_kgm3">ρ [kg/m³].</param>
internal sealed record SparMaterialPropertiesData(
    double YieldStrength_Pa,
    double YoungsModulus_Pa,
    double Density_kgm3);

/// <summary>Static registry of per-material spar properties.</summary>
internal static class SparMaterialRegistry
{
    /// <summary>Al 7075-T6.</summary>
    internal static readonly SparMaterialPropertiesData Aluminum7075 =
        new(YieldStrength_Pa: 503e6, YoungsModulus_Pa: 71.7e9, Density_kgm3: 2810.0);

    /// <summary>Steel 4340.</summary>
    internal static readonly SparMaterialPropertiesData Steel4340 =
        new(YieldStrength_Pa: 690e6, YoungsModulus_Pa: 200e9,  Density_kgm3: 7850.0);

    /// <summary>Carbon-fibre composite.</summary>
    internal static readonly SparMaterialPropertiesData CarbonFibreComposite =
        new(YieldStrength_Pa: 600e6, YoungsModulus_Pa: 138e9,  Density_kgm3: 1600.0);

    /// <summary>Resolve per-material spar properties.</summary>
    internal static SparMaterialPropertiesData For(SparMaterial material) => material switch
    {
        SparMaterial.Aluminum7075         => Aluminum7075,
        SparMaterial.Steel4340            => Steel4340,
        SparMaterial.CarbonFibreComposite => CarbonFibreComposite,
        _ => throw new ArgumentOutOfRangeException(nameof(material), material,
                $"Unknown SparMaterial '{material}'."),
    };
}
