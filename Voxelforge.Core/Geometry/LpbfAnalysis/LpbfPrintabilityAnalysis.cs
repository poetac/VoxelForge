// LpbfPrintabilityAnalysis.cs — Sprint 27 (2026-04-23): composite entry
// point that runs overhang + trapped-powder + drain-path + orientation
// advisor in sequence and packs the three gate-ready flags onto one
// result the FeasibilityGate can consume.
//
// Synthesis approach
// ──────────────────
// The regen-chamber pipeline doesn't materialise a PicoGK voxel field
// in the fast SA path (ADR-005 keeps voxel ops on the task thread, not
// the xUnit-under-SA path). Instead this module accepts the abstract
// inputs the three analyses need; a helper below synthesises them from
// a `ChamberContour` + `ChannelSchedule`. Tests construct the inputs
// directly without touching the chamber synthesis helper.

using System;
using System.Collections.Generic;
using System.Numerics;
using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Geometry.LpbfAnalysis;

/// <summary>Composite printability result exposed on
/// <see cref="Voxelforge.Optimization.RegenGenerationResult"/>.</summary>
public sealed record LpbfPrintabilityResult(
    LpbfMaterialProfile     Material,
    OverhangReport          Overhang,
    TrappedPowderReport?    TrappedPowder,
    DrainPathReport         DrainPath,
    PrintOrientationReport? Orientation)
{
    /// <summary>Convenience flag for the OVERHANG_ANGLE_EXCEEDED gate.</summary>
    public bool HasOverhangViolation => Overhang.ViolationCount > 0;

    /// <summary>Convenience flag for the TRAPPED_POWDER_REGION gate. Null
    /// trapped-powder report is treated as "no trapped powder" — the
    /// voxel flood-fill is the most expensive analysis in the family
    /// and the caller is allowed to skip it on fast-path evaluations.</summary>
    public bool HasTrappedPowder => TrappedPowder is { PocketCount: > 0 };

    /// <summary>Convenience flag for the DRAIN_PATH_MISSING gate.</summary>
    public bool HasDrainPathViolation => DrainPath.ViolationCount > 0;
}

/// <summary>
/// Sprint 27 (2026-04-23): composite LPBF printability analysis entry
/// point. Runs the three voxel-free gate analyses + the orientation
/// advisor and packs the result into a record consumable by
/// <see cref="Voxelforge.Optimization.FeasibilityGate.Evaluate"/>.
/// </summary>
public static class LpbfPrintabilityAnalysis
{
    /// <summary>
    /// Full-fat entry point — takes pre-built inputs. Tests exercise
    /// this overload; <see cref="ForChamber"/> is the convenience
    /// wrapper for production callers.
    /// </summary>
    public static LpbfPrintabilityResult Run(
        IReadOnlyList<SurfaceSample> samples,
        Vector3                      buildAxis,
        LpbfMaterialProfile          material,
        VoxelFieldSnapshot?          voxelField   = null,
        IEnumerable<OpeningPort>?    openings     = null,
        LpbfRoutingGraph?            routingGraph = null,
        bool                         runOrientationAdvisor = true)
    {
        if (samples  is null) throw new ArgumentNullException(nameof(samples));
        if (material is null) throw new ArgumentNullException(nameof(material));

        var overhang = OverhangAnalysis.Analyze(samples, buildAxis, material);
        var trapped  = voxelField is null
            ? null
            : TrappedPowderAnalysis.Analyze(voxelField, openings,
                minFlaggedPocketVolume_mm3: material.MinFlaggedPocketVolume_mm3);
        var drain    = DrainPathAnalysis.Analyze(routingGraph ?? LpbfRoutingGraph.Empty);
        var advisor  = runOrientationAdvisor
            ? PrintOrientationAdvisor.Analyze(samples, material)
            : null;

        return new LpbfPrintabilityResult(
            Material:      material,
            Overhang:      overhang,
            TrappedPowder: trapped,
            DrainPath:     drain,
            Orientation:   advisor);
    }

    /// <summary>
    /// Convenience helper for the regen-chamber pipeline: synthesise a
    /// surface-sample list from the chamber contour + channel schedule
    /// (axisymmetric unwrap at a configurable azimuthal density), then
    /// run the full analysis with routing graph derived from the coolant
    /// + purge + igniter topology. Voxel field is omitted on the fast
    /// path — the trapped-powder check is opt-in via <paramref name="voxelField"/>.
    /// </summary>
    public static LpbfPrintabilityResult ForChamber(
        ChamberContour             contour,
        ChannelSchedule            channels,
        LpbfMaterialProfile        material,
        Vector3                    buildAxis,
        LpbfRoutingGraph?          routingGraph = null,
        VoxelFieldSnapshot?        voxelField   = null,
        IEnumerable<OpeningPort>?  openings     = null,
        int                        azimuthalSamples = 24)
    {
        var samples = SampleAxisymmetricSurface(contour, channels, azimuthalSamples);
        return Run(samples,
                   buildAxis,
                   material,
                   voxelField:            voxelField,
                   openings:              openings,
                   routingGraph:          routingGraph,
                   runOrientationAdvisor: true);
    }

    /// <summary>
    /// Synthesise surface samples from an axisymmetric chamber contour.
    /// Emits one sample per (station × azimuthal-slice) on both the
    /// inner (gas-side) wall and the outer-jacket wall. Normals are
    /// outward-pointing in 3D world coords.
    /// </summary>
    public static IReadOnlyList<SurfaceSample> SampleAxisymmetricSurface(
        ChamberContour  contour,
        ChannelSchedule channels,
        int             azimuthalSamples = 24)
    {
        if (contour   is null) throw new ArgumentNullException(nameof(contour));
        if (channels  is null) throw new ArgumentNullException(nameof(channels));
        if (azimuthalSamples < 4) azimuthalSamples = 4;

        int N = contour.Stations.Length;
        var samples = new List<SurfaceSample>(N * azimuthalSamples * 2);
        double twoPi = 2.0 * Math.PI;

        // Pre-compute outer-jacket channel-height per station (linear
        // interpolation chamber → throat → exit, matches the existing
        // `Manufacturing/OverhangAnalysis.ApproximateOuterSlope` model).
        double xThroat = contour.Stations[contour.ThroatIndex].X_mm;
        double xExit   = contour.TotalLength_mm;
        double hCh     = channels.ChannelHeightAtChamber_mm;
        double hTh     = channels.ChannelHeightAtThroat_mm;
        double hEx     = channels.ChannelHeightAtExit_mm;
        double tWall   = channels.GasSideWallThickness_mm;
        // Jacket thickness stays invariant station-to-station in the
        // regen pipeline, so it's not passed in; use a 1 mm fallback
        // (bounded by the outer-wall contribution, not printability).
        double tJacket = 1.0;

        for (int i = 0; i < N; i++)
        {
            var s = contour.Stations[i];
            // Slope along the meridional contour — used to build the
            // outward normal on both walls.
            double slopeInner = s.Slope;
            double hereH = s.X_mm <= xThroat
                ? hCh + (hTh - hCh) * (s.X_mm / Math.Max(xThroat, 1e-6))
                : hTh + (hEx - hTh)
                      * ((s.X_mm - xThroat) / Math.Max(xExit - xThroat, 1e-6));
            double rOuter = s.R_mm + tWall + hereH + tJacket;

            // Approximate outer-wall slope using inner slope + d(h)/dx.
            double dHdX = s.X_mm <= xThroat
                ? (hTh - hCh) / Math.Max(xThroat, 1e-6)
                : (hEx - hTh) / Math.Max(xExit - xThroat, 1e-6);
            double slopeOuter = slopeInner + dHdX;

            // Arc length along the contour — tiny O(1) estimate for area.
            double segLen = contour.SegmentLength_mm(i);
            double innerCircArea = twoPi * s.R_mm  * segLen / azimuthalSamples;
            double outerCircArea = twoPi * rOuter  * segLen / azimuthalSamples;

            for (int a = 0; a < azimuthalSamples; a++)
            {
                double phi = twoPi * a / azimuthalSamples;
                float cphi = (float)Math.Cos(phi);
                float sphi = (float)Math.Sin(phi);

                // Inner wall: normal points INTO the chamber (toward axis).
                // For a contour r(x) with slope dr/dx, the outward normal
                // of the gas-side surface (pointing INTO the combustion
                // cavity) has meridional component (slope, -1) normalised.
                Vector3 innerPoint = new(
                    (float)s.X_mm,
                    (float)(s.R_mm * cphi),
                    (float)(s.R_mm * sphi));
                Vector3 innerNormal = MeridionalNormal(slopeInner, cphi, sphi, gasSide: true);
                samples.Add(new SurfaceSample(innerPoint, innerNormal, innerCircArea));

                // Outer wall: normal points OUT into free air.
                Vector3 outerPoint = new(
                    (float)s.X_mm,
                    (float)(rOuter * cphi),
                    (float)(rOuter * sphi));
                Vector3 outerNormal = MeridionalNormal(slopeOuter, cphi, sphi, gasSide: false);
                samples.Add(new SurfaceSample(outerPoint, outerNormal, outerCircArea));
            }
        }

        return samples;
    }

    /// <summary>
    /// Build the 3D outward normal of an axisymmetric surface from its
    /// meridional slope dr/dx at azimuth φ. gasSide=true flips the sign
    /// so the inner wall's normal points toward the axis (into the gas
    /// cavity).
    /// </summary>
    private static Vector3 MeridionalNormal(double slope, float cphi, float sphi, bool gasSide)
    {
        // Meridional outward normal: (-slope, 1) normalised for outer;
        // (slope, -1) for gas-side. Lift to 3D by rotating (nr, nx) about
        // the X axis by φ → (nx, nr·cosφ, nr·sinφ).
        double mag = Math.Sqrt(slope * slope + 1.0);
        double nx = gasSide ? (slope  / mag) : (-slope / mag);
        double nr = gasSide ? (-1.0   / mag) :  (1.0  / mag);
        return new Vector3(
            (float)nx,
            (float)(nr * cphi),
            (float)(nr * sphi));
    }
}
