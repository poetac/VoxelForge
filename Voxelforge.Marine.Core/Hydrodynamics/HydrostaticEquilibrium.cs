// HydrostaticEquilibrium.cs — buoyancy and hull mass for a fully-submerged AUV.
//
// Computes Archimedes uplift and structural shell mass for the M1 AuvMidBody
// variant. Surface-hull metacentric height stability (M4-M5) is deferred.
//
// Formulas:
//   Displaced mass: m_displaced = ρ_water × V_ext
//   Shell volume (thin-wall approx): V_shell ≈ S_wet × t_wall
//   Hull mass: m_hull = V_shell × ρ_material
//   Buoyancy force: F_b = m_displaced × g   (g = 9.80665 m/s²)
//   Buoyant weight: ΔF = F_b − m_hull × g   (+ = net positive buoyancy)
//   CG/CB offset: 0 for symmetric hull (M1)
//
// Material densities (LPBF-grade per NASA PURS / Brush-Wellman data):
//   Ti-6Al-4V : 4430 kg/m³
//   Al-6061   : 2700 kg/m³
//   AISI-316L : 7950 kg/m³

using System;

namespace Voxelforge.Marine.Hydrodynamics;

/// <summary>
/// Result of the hydrostatic equilibrium calculation.
/// </summary>
public sealed record HydrostaticResult(
    double BuoyancyForce_N,
    double DisplacedVolume_m3,
    double BuoyantWeight_N,
    double HullMass_kg,
    double ShellVolume_m3);

/// <summary>
/// Computes buoyancy and structural hull mass for a fully-submerged AUV.
/// </summary>
public static class HydrostaticEquilibrium
{
    private const double GravityMs2 = 9.80665;

    private static readonly double[] MaterialDensity_kgm3 =
    {
        4430.0,  // 0 = Ti-6Al-4V
        2700.0,  // 1 = Al-6061
        7950.0,  // 2 = AISI-316L (LPBF-grade)
    };

    /// <summary>
    /// Compute hydrostatic equilibrium for the supplied hull geometry.
    /// </summary>
    public static HydrostaticResult Solve(
        FairingGeometry fairing,
        MarineDesign design,
        MarineConditions cond)
    {
        if (fairing is null) throw new ArgumentNullException(nameof(fairing));
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (cond is null) throw new ArgumentNullException(nameof(cond));

        int matIdx = Math.Clamp(design.MaterialIndex, 0, MaterialDensity_kgm3.Length - 1);
        double rhoMat = MaterialDensity_kgm3[matIdx];
        double rhoWater = cond.WaterDensity_kgm3;

        double vExt = fairing.ExternalVolume_m3;

        // Shell volume: thin-wall approximation V_shell ≈ S_wet × t
        double vShell = fairing.WettedArea_m2 * design.WallThickness_m;

        // Structural hull mass
        double mHull = vShell * rhoMat;

        // Displaced water mass + buoyancy
        double mDisplaced = rhoWater * vExt;
        double fBuoyancy = mDisplaced * GravityMs2;

        // Net buoyant weight (+ = positively buoyant)
        double fBuoyantWeight = fBuoyancy - mHull * GravityMs2;

        return new HydrostaticResult(
            BuoyancyForce_N:  fBuoyancy,
            DisplacedVolume_m3: vExt,
            BuoyantWeight_N:  fBuoyantWeight,
            HullMass_kg:      mHull,
            ShellVolume_m3:   vShell);
    }
}
