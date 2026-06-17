// CylHemiFairingGeometry.cs — cylindrical hull with hemispherical endcaps.
//
// Geometry (closed-form):
//   r(x):
//     Nose hemi  x ∈ [0, R]      : sqrt(R² − (R−x)²)
//     Cylinder   x ∈ [R, L−R]    : R
//     Tail hemi  x ∈ [L−R, L]    : sqrt(R² − (x−(L−R))²)
//
//   S_wet = π·D·L  (sphere surface = πD², cylinder lateral = πD(L−D); sum = πDL)
//   V_ext = (π/6)·D³ + (π/4)·D²·(L−D)
//
// Windenburg-Trilling formula applies to the cylindrical section (conservative
// for hemispherical caps, which buckle at higher pressure).
//
// References:
//   Timoshenko & Gere (1961). Theory of Elastic Stability. §11.
//   ADR-026 §Marine pillar.

using System;

namespace Voxelforge.Marine.Hydrodynamics;

/// <summary>
/// Geometry for a cylindrical hull with hemispherical endcaps.
/// </summary>
public static class CylHemiFairingGeometry
{
    /// <summary>
    /// Compute fairing geometry for the supplied design.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when Diameter_m ≥ Length_m (caps cannot fit).</exception>
    public static FairingGeometry Compute(MarineDesign design)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));

        double l = design.Length_m;
        double d = design.Diameter_m;
        double r = d / 2.0;

        if (d >= l)
            throw new ArgumentException(
                $"CylindricalHemi hull requires Diameter_m ({d:F4}) < Length_m ({l:F4}) "
              + "so both hemispherical endcaps fit within the hull length.");

        double sWet  = Math.PI * d * l;
        double cylLen = l - d;                       // L − 2R
        double vExt  = (Math.PI / 6.0) * d * d * d  // sphere = (4/3)π R³ = (π/6)D³
                     + (Math.PI / 4.0) * d * d * cylLen;

        return new FairingGeometry(
            Length_m:          l,
            Diameter_m:        d,
            NoseLength_m:      r,    // hemisphere depth = R
            TailLength_m:      r,
            MidBodyLength_m:   cylLen,
            WettedArea_m2:     sWet,
            ExternalVolume_m3: vExt);
    }

    /// <summary>
    /// Radial profile r(x) [m] at axial station x [m].
    /// Used by <see cref="Voxelforge.Marine.Geometry.MarineHullVoxelBuilder"/> via InternalsVisibleTo.
    /// </summary>
    internal static double RadiusAt(double x, double totalLength, double radius)
    {
        double tailStart = totalLength - radius;

        if (x <= radius)
        {
            double dx = radius - x;
            return Math.Sqrt(Math.Max(0.0, radius * radius - dx * dx));
        }
        if (x >= tailStart)
        {
            double dx = x - tailStart;
            return Math.Sqrt(Math.Max(0.0, radius * radius - dx * dx));
        }
        return radius;
    }
}
