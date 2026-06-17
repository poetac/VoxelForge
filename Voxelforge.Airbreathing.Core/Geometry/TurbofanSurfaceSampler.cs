// TurbofanSurfaceSampler.cs — synthesise SurfaceSample[] from a
// TurbofanContour for the LPBF printability pass.
//
// Sibling to RamjetSurfaceSampler. Walks two concentric flow paths
// (core + bypass duct) and emits four wall-sample sets per (station ×
// azimuthal-slot):
//
//   1. Core inner wall (gas side, normal points toward axis)
//   2. Core outer wall (= bypass-duct inner wall geometric surface,
//      but distinct from #3 because it sees core-side material — the
//      annular core shell sits between #1 and #2)
//   3. Bypass-duct inner wall (cold-stream side of the outer shell)
//   4. Bypass-duct outer wall (atmosphere side, normal points outward)
//
// Walls #1, #4 are the surfaces that bound printed material seen from
// outside the part; walls #2, #3 are the inner surfaces of the two
// annular shells. All four matter for LPBF overhang analysis since
// each could have downward-facing patches that need support.
//
// Same axisymmetric ceiling as RamjetSurfaceSampler: when turbofan
// gains non-axisymmetric features (struts, splitter vanes, struts)
// switch to a voxel-walking sampler.

using System;
using System.Collections.Generic;
using System.Numerics;
using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Contour-driven surface-sample synthesiser for the turbofan two-shell
/// geometry. Emits <c>4 × stations × azimuthalSamples</c> samples.
/// </summary>
internal static class TurbofanSurfaceSampler
{
    /// <summary>
    /// Walk both concentric flow paths and emit wall samples for the
    /// four annular surfaces (core inner / core outer / bypass inner /
    /// bypass outer). Outward normals follow the same convention as
    /// <c>RamjetSurfaceSampler</c>.
    /// </summary>
    /// <param name="contour">Pure-data turbofan contour from Core (metres).</param>
    /// <param name="coreWallThickness_mm">Core-shell wall thickness [mm].</param>
    /// <param name="bypassDuctWallThickness_mm">Bypass-duct wall thickness [mm].</param>
    /// <param name="azimuthalSamples">Azimuthal density per station; clamped ≥ 4.</param>
    internal static IReadOnlyList<SurfaceSample> SampleAxisymmetric(
        TurbofanContour contour,
        double coreWallThickness_mm,
        double bypassDuctWallThickness_mm,
        int azimuthalSamples = 64)
    {
        if (contour is null) throw new ArgumentNullException(nameof(contour));
        if (coreWallThickness_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(coreWallThickness_mm),
                "Core wall thickness must be positive.");
        if (bypassDuctWallThickness_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(bypassDuctWallThickness_mm),
                "Bypass-duct wall thickness must be positive.");
        if (azimuthalSamples < 4) azimuthalSamples = 4;

        var stations = contour.CoreStations;
        int N = stations.Length;
        var samples = new List<SurfaceSample>(N * azimuthalSamples * 4);
        double twoPi = 2.0 * Math.PI;

        // Pre-compute four radii arrays (mm).
        var x_mm           = new double[N];
        var rCoreInner_mm  = new double[N];
        var rCoreOuter_mm  = new double[N];
        var rBypInner_mm   = new double[N];
        var rBypOuter_mm   = new double[N];
        for (int i = 0; i < N; i++)
        {
            x_mm[i]          = stations[i].X_m * 1000.0;
            rCoreInner_mm[i] = stations[i].R_m * 1000.0;
            rCoreOuter_mm[i] = rCoreInner_mm[i] + coreWallThickness_mm;
            rBypInner_mm[i]  = contour.BypassOuterRadii_m[i] * 1000.0;
            rBypOuter_mm[i]  = rBypInner_mm[i] + bypassDuctWallThickness_mm;
        }

        for (int i = 0; i < N; i++)
        {
            double dx = ForwardBackwardCentredDx(x_mm, i, N);
            double slopeCoreInner = ForwardBackwardCentredSlope(rCoreInner_mm, x_mm, i, N);
            double slopeCoreOuter = ForwardBackwardCentredSlope(rCoreOuter_mm, x_mm, i, N);
            double slopeBypInner  = ForwardBackwardCentredSlope(rBypInner_mm,  x_mm, i, N);
            double slopeBypOuter  = ForwardBackwardCentredSlope(rBypOuter_mm,  x_mm, i, N);

            double segLen = HalfNeighbourSegLen(x_mm, rCoreInner_mm, i, N);
            if (segLen <= 0) segLen = 1e-3;

            double aCi = twoPi * rCoreInner_mm[i] * segLen / azimuthalSamples;
            double aCo = twoPi * rCoreOuter_mm[i] * segLen / azimuthalSamples;
            double aBi = twoPi * rBypInner_mm[i]  * segLen / azimuthalSamples;
            double aBo = twoPi * rBypOuter_mm[i]  * segLen / azimuthalSamples;

            for (int a = 0; a < azimuthalSamples; a++)
            {
                double phi = twoPi * a / azimuthalSamples;
                float cphi = (float)Math.Cos(phi);
                float sphi = (float)Math.Sin(phi);

                // Core inner — normal toward axis.
                samples.Add(new SurfaceSample(
                    AxisymmetricPoint(x_mm[i], rCoreInner_mm[i], cphi, sphi),
                    MeridionalNormal(slopeCoreInner, cphi, sphi, gasSide: true),
                    aCi));

                // Core outer — normal away from axis (faces bypass annulus).
                samples.Add(new SurfaceSample(
                    AxisymmetricPoint(x_mm[i], rCoreOuter_mm[i], cphi, sphi),
                    MeridionalNormal(slopeCoreOuter, cphi, sphi, gasSide: false),
                    aCo));

                // Bypass inner — normal toward axis (faces bypass annulus).
                samples.Add(new SurfaceSample(
                    AxisymmetricPoint(x_mm[i], rBypInner_mm[i], cphi, sphi),
                    MeridionalNormal(slopeBypInner, cphi, sphi, gasSide: true),
                    aBi));

                // Bypass outer — normal outward (atmosphere side).
                samples.Add(new SurfaceSample(
                    AxisymmetricPoint(x_mm[i], rBypOuter_mm[i], cphi, sphi),
                    MeridionalNormal(slopeBypOuter, cphi, sphi, gasSide: false),
                    aBo));
            }
        }

        return samples;
    }

    // ── numerical helpers ───────────────────────────────────────────────

    private static double ForwardBackwardCentredDx(double[] x, int i, int N) => i == 0
        ? Math.Max(x[1] - x[0], 1e-6)
        : i == N - 1
            ? Math.Max(x[N - 1] - x[N - 2], 1e-6)
            : Math.Max(x[i + 1] - x[i - 1], 1e-6);

    private static double ForwardBackwardCentredSlope(double[] r, double[] x, int i, int N)
    {
        double dr = i == 0
            ? r[1] - r[0]
            : i == N - 1
                ? r[N - 1] - r[N - 2]
                : r[i + 1] - r[i - 1];
        double dx = ForwardBackwardCentredDx(x, i, N);
        return dr / dx;
    }

    private static double HalfNeighbourSegLen(double[] x, double[] r, int i, int N)
    {
        double segPrev = i == 0
            ? 0.0
            : 0.5 * Math.Sqrt(
                (x[i] - x[i - 1]) * (x[i] - x[i - 1])
              + (r[i] - r[i - 1]) * (r[i] - r[i - 1]));
        double segNext = i == N - 1
            ? 0.0
            : 0.5 * Math.Sqrt(
                (x[i + 1] - x[i]) * (x[i + 1] - x[i])
              + (r[i + 1] - r[i]) * (r[i + 1] - r[i]));
        return segPrev + segNext;
    }

    private static Vector3 AxisymmetricPoint(double x, double r, float cphi, float sphi)
        => new((float)x, (float)(r * cphi), (float)(r * sphi));

    /// <summary>
    /// 3D outward normal of an axisymmetric surface from its meridional
    /// slope dr/dx. <c>gasSide=true</c> flips the sign so the normal
    /// points toward the axis.
    /// </summary>
    private static Vector3 MeridionalNormal(double slope, float cphi, float sphi, bool gasSide)
    {
        double mag = Math.Sqrt(slope * slope + 1.0);
        double nx = gasSide ? (slope  / mag) : (-slope / mag);
        double nr = gasSide ? (-1.0   / mag) :  (1.0  / mag);
        return new Vector3((float)nx, (float)(nr * cphi), (float)(nr * sphi));
    }
}
