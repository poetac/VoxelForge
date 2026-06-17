// TankShellProperties.cs — Sprint TANK.W1 per-shell-type property registry.

using System;

namespace Voxelforge.Tankage;

/// <summary>
/// Cluster-anchored shell-material properties per
/// <see cref="TankShellType"/> (Sprint TANK.W1).
/// </summary>
/// <param name="YieldStrength_Pa">σ_yield [Pa]. For composites this is
/// the allowable working stress at the design margin (not the actual
/// fibre UTS; we anchor at σ_uts / safety-factor 2.5).</param>
/// <param name="Density_kgm3">ρ [kg/m³].</param>
/// <param name="MinPracticalWallThickness_m">Manufacturing floor for
/// shell thickness [m]. Drives the LPBF / sheet-metal lower bound.</param>
internal sealed record TankShellProperties(
    double YieldStrength_Pa,
    double Density_kgm3,
    double MinPracticalWallThickness_m);

/// <summary>Static registry of per-shell-type tank properties.</summary>
internal static class TankShellRegistry
{
    /// <summary>AISI 4130 chromoly steel cluster.</summary>
    internal static readonly TankShellProperties Steel4130 =
        new(YieldStrength_Pa:             460e6,
            Density_kgm3:                  7850.0,
            MinPracticalWallThickness_m:   0.0008);    // 0.8 mm sheet

    /// <summary>Al-6061-T6 cluster.</summary>
    internal static readonly TankShellProperties Aluminum6061 =
        new(YieldStrength_Pa:             280e6,
            Density_kgm3:                  2700.0,
            MinPracticalWallThickness_m:   0.0015);    // 1.5 mm sheet

    /// <summary>Carbon-fibre / epoxy composite cluster (Type-IV tank wall).</summary>
    internal static readonly TankShellProperties CarbonFibreComposite =
        new(YieldStrength_Pa:             480e6,       // σ_uts / 2.5 SF
            Density_kgm3:                  1500.0,
            MinPracticalWallThickness_m:   0.005);     // 5 mm minimum wrap

    /// <summary>Resolve per-shell-type properties.</summary>
    internal static TankShellProperties For(TankShellType shellType) => shellType switch
    {
        TankShellType.Steel4130            => Steel4130,
        TankShellType.Aluminum6061         => Aluminum6061,
        TankShellType.CarbonFibreComposite => CarbonFibreComposite,
        _ => throw new ArgumentOutOfRangeException(nameof(shellType), shellType,
                $"Unknown TankShellType '{shellType}'."),
    };
}
