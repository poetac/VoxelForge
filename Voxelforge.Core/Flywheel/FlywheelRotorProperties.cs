// FlywheelRotorProperties.cs — Sprint FW.W1 per-material + per-shape
// registries.

using System;

namespace Voxelforge.Flywheel;

/// <summary>Flywheel rotor material (Sprint FW.W1).</summary>
internal enum FlywheelMaterial
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>Steel 4340 — Beacon Power 100 kWh cluster (utility).</summary>
    Steel4340 = 1,

    /// <summary>Carbon-fibre composite — Boeing / Active Power-class
    /// modern high-specific-energy rotor.</summary>
    CarbonFibreComposite = 2,
}

/// <summary>Cluster-anchored rotor-material properties.</summary>
/// <param name="YieldStrength_Pa">σ_y [Pa] — defines the burst speed.</param>
/// <param name="Density_kgm3">ρ [kg/m³].</param>
internal sealed record FlywheelMaterialPropertiesData(
    double YieldStrength_Pa,
    double Density_kgm3);

/// <summary>Static registry of per-material flywheel rotor properties.</summary>
internal static class FlywheelMaterialRegistry
{
    /// <summary>Steel 4340 (high-strength alloy steel).</summary>
    internal static readonly FlywheelMaterialPropertiesData Steel4340 =
        new(YieldStrength_Pa: 690e6, Density_kgm3: 7850.0);

    /// <summary>Carbon-fibre composite — UTS / 2.0 SF design allowable.</summary>
    internal static readonly FlywheelMaterialPropertiesData CarbonFibreComposite =
        new(YieldStrength_Pa: 1000e6, Density_kgm3: 1500.0);

    /// <summary>Resolve per-material properties.</summary>
    internal static FlywheelMaterialPropertiesData For(FlywheelMaterial material) => material switch
    {
        FlywheelMaterial.Steel4340            => Steel4340,
        FlywheelMaterial.CarbonFibreComposite => CarbonFibreComposite,
        _ => throw new ArgumentOutOfRangeException(nameof(material), material,
                $"Unknown FlywheelMaterial '{material}'."),
    };
}

/// <summary>Static helper for per-shape geometric factors.</summary>
internal static class FlywheelShapeFactors
{
    /// <summary>
    /// Shape factor K [-] used in specific-energy formula
    /// E/m = K · σ / ρ. Thin-rim: K = 0.5; solid disk: K = 0.606.
    /// </summary>
    internal static double For(FlywheelShape shape) => shape switch
    {
        FlywheelShape.ThinRim   => 0.5,
        FlywheelShape.SolidDisk => 0.606,
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape,
                $"Unknown FlywheelShape '{shape}'."),
    };

    /// <summary>
    /// Moment-of-inertia coefficient α [-] used as I = α · m · R².
    /// Thin-rim: α = 1; solid disk: α = 0.5.
    /// </summary>
    internal static double MomentOfInertiaCoefficient(FlywheelShape shape) => shape switch
    {
        FlywheelShape.ThinRim   => 1.0,
        FlywheelShape.SolidDisk => 0.5,
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape,
                $"Unknown FlywheelShape '{shape}'."),
    };
}
