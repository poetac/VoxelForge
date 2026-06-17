// RadiationLossSolver.cs — Stefan-Boltzmann radiation losses from the
// resistojet chamber outer wall (and optional radiative-cooled niobium
// nozzle).
//
// Pure functional API. q_rad = ε · σ · A · (T_wall⁴ − T_∞⁴). Returns
// W. Per pillar spec §5.4 + Holman "Heat Transfer" 10e §8.
//
// Determinism: pure function, no allocations, no state.

using System;
using Voxelforge.ElectricPropulsion.Thermo;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Stefan-Boltzmann radiation-loss solver for the resistojet thermal
/// budget. Computes q_rad from the chamber outer wall (the dominant
/// vacuum-side energy sink) and, when the nozzle is radiatively cooled,
/// the second emission surface from the diverging-section wall.
/// </summary>
public static class RadiationLossSolver
{
    /// <summary>
    /// Cosmic-microwave-background temperature [K] used as <c>T_∞</c> for
    /// vacuum operation. Negligible vs T_wall⁴ at any meaningful resistojet
    /// chamber temperature; included for dimensional rigour.
    /// </summary>
    public const double T_CosmicBackground_K = 3.0;

    /// <summary>
    /// Compute the chamber outer-wall radiation flux [W].
    /// </summary>
    /// <param name="emissivity">Wall emissivity ε ∈ (0, 1].</param>
    /// <param name="surfaceArea_m2">Outer-wall surface area [m²].</param>
    /// <param name="T_wall_K">Outer-wall steady-state temperature [K].</param>
    /// <param name="T_ambient_K">Ambient sink temperature [K]. Use <see cref="T_CosmicBackground_K"/> for vacuum.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="emissivity"/> is NaN or not in (0, 1],
    /// when <paramref name="surfaceArea_m2"/> is NaN or non-positive, or
    /// when <paramref name="T_wall_K"/> / <paramref name="T_ambient_K"/>
    /// is NaN or out of physical range.
    /// </exception>
    public static double ChamberWallRadiation_W(
        double emissivity,
        double surfaceArea_m2,
        double T_wall_K,
        double T_ambient_K)
    {
        if (double.IsNaN(emissivity) || emissivity <= 0 || emissivity > 1)
            throw new ArgumentOutOfRangeException(nameof(emissivity),
                $"Emissivity must be in (0, 1]; got ε={emissivity:F3}.");
        if (double.IsNaN(surfaceArea_m2) || surfaceArea_m2 <= 0)
            throw new ArgumentOutOfRangeException(nameof(surfaceArea_m2),
                $"Surface area must be positive; got A={surfaceArea_m2:E3} m².");
        if (double.IsNaN(T_wall_K) || T_wall_K <= 0 || double.IsNaN(T_ambient_K) || T_ambient_K < 0)
            throw new ArgumentOutOfRangeException(nameof(T_wall_K),
                $"Wall and ambient temperatures must be positive; got T_wall={T_wall_K:F1} K, T_amb={T_ambient_K:F1} K.");

        double T_wall_4 = T_wall_K * T_wall_K * T_wall_K * T_wall_K;
        double T_amb_4  = T_ambient_K * T_ambient_K * T_ambient_K * T_ambient_K;
        return emissivity * PropellantTables.Sigma_SB * surfaceArea_m2 * (T_wall_4 - T_amb_4);
    }

    /// <summary>
    /// Compute the optional radiatively-cooled nozzle emission [W]. Real
    /// flown resistojets use niobium-walled nozzles operating at
    /// ~1500 K wall temperature. Returns 0 if the design uses an actively-
    /// cooled (Wave-2-only) nozzle.
    /// </summary>
    /// <param name="isRadiativelyCooled">From <see cref="ElectricPropulsionEngineDesign.RadiativelyCooledNozzle"/>.</param>
    /// <param name="emissivity">Nozzle wall emissivity (typically 0.5–0.7 for niobium).</param>
    /// <param name="nozzleSurfaceArea_m2">Outer-wall surface area of the diverging section [m²].</param>
    /// <param name="T_nozzleWall_K">Nozzle wall temperature [K]. ~1500 K typical.</param>
    /// <param name="T_ambient_K">Ambient sink temperature [K].</param>
    public static double NozzleWallRadiation_W(
        bool isRadiativelyCooled,
        double emissivity,
        double nozzleSurfaceArea_m2,
        double T_nozzleWall_K,
        double T_ambient_K)
    {
        if (!isRadiativelyCooled) return 0.0;
        return ChamberWallRadiation_W(emissivity, nozzleSurfaceArea_m2, T_nozzleWall_K, T_ambient_K);
    }

    /// <summary>
    /// Total radiation loss [W] = chamber wall + nozzle wall.
    /// </summary>
    public static double TotalRadiation_W(
        double chamberEmissivity,
        double chamberSurfaceArea_m2,
        double T_chamberWall_K,
        bool   nozzleRadiativelyCooled,
        double nozzleEmissivity,
        double nozzleSurfaceArea_m2,
        double T_nozzleWall_K,
        double T_ambient_K)
    {
        return ChamberWallRadiation_W(chamberEmissivity, chamberSurfaceArea_m2, T_chamberWall_K, T_ambient_K)
             + NozzleWallRadiation_W(nozzleRadiativelyCooled, nozzleEmissivity, nozzleSurfaceArea_m2, T_nozzleWall_K, T_ambient_K);
    }
}
