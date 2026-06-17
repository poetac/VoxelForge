// RamjetSurfaceSampler.cs — synthesise SurfaceSample[] from a
// RamjetContour for the LPBF printability pass.
//
// Mirrors LpbfPrintabilityAnalysis.SampleAxisymmetricSurface
// (Voxelforge.Core/Geometry/LpbfAnalysis/LpbfPrintabilityAnalysis.cs:122)
// but stripped down for ramjet MVP: no ChannelSchedule (no cooling
// channels), no separate jacket thickness (single uniform wall), no
// pre-existing per-station Slope field (RamjetStation doesn't carry
// one — compute meridional slope via finite differences instead).
//
// CEILING: this sampler assumes axisymmetric geometry. Once ramjet
// adds non-axisymmetric features (struts, fuel injectors, mounts) the
// axisymmetric path stops covering them — switch to a voxel-walking
// surface sampler at that point.

using System;
using System.Collections.Generic;
using System.Numerics;
using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Contour-driven surface-sample synthesiser for the ramjet shell.
/// Emits <c>2 × stations × azimuthalSamples</c> samples (inner wall +
/// outer wall) with outward-pointing normals + per-sample patch areas.
/// </summary>
internal static class RamjetSurfaceSampler
{
    /// <summary>
    /// Walk the contour, emit one inner-wall + one outer-wall sample
    /// per (station × azimuthal-slot). Inner-wall normal points INTO
    /// the gas path (toward the axis); outer-wall normal points OUT
    /// of the printed shell.
    /// </summary>
    /// <param name="contour">Pure-data ramjet contour from Core (metres).</param>
    /// <param name="wallThickness_mm">Uniform shell wall thickness [mm].</param>
    /// <param name="azimuthalSamples">Azimuthal density per station; clamped ≥ 4.</param>
    internal static IReadOnlyList<SurfaceSample> SampleAxisymmetric(
        RamjetContour contour,
        double wallThickness_mm,
        int azimuthalSamples = 64)
    {
        if (contour is null) throw new ArgumentNullException(nameof(contour));
        if (wallThickness_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(wallThickness_mm),
                "Wall thickness must be positive.");
        if (azimuthalSamples < 4) azimuthalSamples = 4;

        var stations = contour.Stations;
        int N = stations.Length;
        var samples = new List<SurfaceSample>(N * azimuthalSamples * 2);
        double twoPi = 2.0 * Math.PI;

        // Pre-compute mm-denominated arrays + meridional slopes via
        // finite differences (forward at start, backward at end,
        // centred in the middle).
        var x_mm = new double[N];
        var rInner_mm = new double[N];
        var rOuter_mm = new double[N];
        for (int i = 0; i < N; i++)
        {
            x_mm[i]      = stations[i].X_m * 1000.0;
            rInner_mm[i] = stations[i].R_m * 1000.0;
            rOuter_mm[i] = rInner_mm[i] + wallThickness_mm;
        }

        for (int i = 0; i < N; i++)
        {
            double dx = i == 0
                ? Math.Max(x_mm[1] - x_mm[0], 1e-6)
                : i == N - 1
                    ? Math.Max(x_mm[N - 1] - x_mm[N - 2], 1e-6)
                    : Math.Max(x_mm[i + 1] - x_mm[i - 1], 1e-6);
            double dRInner = i == 0
                ? rInner_mm[1] - rInner_mm[0]
                : i == N - 1
                    ? rInner_mm[N - 1] - rInner_mm[N - 2]
                    : rInner_mm[i + 1] - rInner_mm[i - 1];
            double dROuter = i == 0
                ? rOuter_mm[1] - rOuter_mm[0]
                : i == N - 1
                    ? rOuter_mm[N - 1] - rOuter_mm[N - 2]
                    : rOuter_mm[i + 1] - rOuter_mm[i - 1];
            double slopeInner = dRInner / dx;
            double slopeOuter = dROuter / dx;

            // Segment length for area weighting — half the distance to
            // each neighbour, matches the rocket-side ChamberContour
            // convention (segments don't overlap or leave gaps).
            double segPrev = i == 0
                ? 0.0
                : 0.5 * Math.Sqrt(
                    (x_mm[i] - x_mm[i - 1]) * (x_mm[i] - x_mm[i - 1])
                  + (rInner_mm[i] - rInner_mm[i - 1]) * (rInner_mm[i] - rInner_mm[i - 1]));
            double segNext = i == N - 1
                ? 0.0
                : 0.5 * Math.Sqrt(
                    (x_mm[i + 1] - x_mm[i]) * (x_mm[i + 1] - x_mm[i])
                  + (rInner_mm[i + 1] - rInner_mm[i]) * (rInner_mm[i + 1] - rInner_mm[i]));
            double segLen = segPrev + segNext;
            // First / last station get half the segment of their lone neighbour.
            if (segLen <= 0) segLen = 1e-3;

            double innerCircArea = twoPi * rInner_mm[i] * segLen / azimuthalSamples;
            double outerCircArea = twoPi * rOuter_mm[i] * segLen / azimuthalSamples;

            for (int a = 0; a < azimuthalSamples; a++)
            {
                double phi = twoPi * a / azimuthalSamples;
                float cphi = (float)Math.Cos(phi);
                float sphi = (float)Math.Sin(phi);

                Vector3 innerPoint = new(
                    (float)x_mm[i],
                    (float)(rInner_mm[i] * cphi),
                    (float)(rInner_mm[i] * sphi));
                Vector3 innerNormal = MeridionalNormal(slopeInner, cphi, sphi, gasSide: true);
                samples.Add(new SurfaceSample(innerPoint, innerNormal, innerCircArea));

                Vector3 outerPoint = new(
                    (float)x_mm[i],
                    (float)(rOuter_mm[i] * cphi),
                    (float)(rOuter_mm[i] * sphi));
                Vector3 outerNormal = MeridionalNormal(slopeOuter, cphi, sphi, gasSide: false);
                samples.Add(new SurfaceSample(outerPoint, outerNormal, outerCircArea));
            }
        }

        return samples;
    }

    /// <summary>
    /// 3D outward normal of an axisymmetric surface from its meridional
    /// slope dr/dx at azimuth φ. Verbatim sibling of the rocket-side
    /// helper. <c>gasSide=true</c> flips the sign so the inner wall's
    /// normal points toward the axis (into the gas cavity).
    /// </summary>
    private static Vector3 MeridionalNormal(double slope, float cphi, float sphi, bool gasSide)
    {
        double mag = Math.Sqrt(slope * slope + 1.0);
        double nx = gasSide ? (slope  / mag) : (-slope / mag);
        double nr = gasSide ? (-1.0   / mag) :  (1.0  / mag);
        return new Vector3(
            (float)nx,
            (float)(nr * cphi),
            (float)(nr * sphi));
    }
}
