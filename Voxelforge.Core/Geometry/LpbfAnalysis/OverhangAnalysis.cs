// OverhangAnalysis.cs — Sprint 27 (2026-04-23): voxel-surface-normal
// LPBF overhang screen.
//
// How it differs from Manufacturing/OverhangAnalysis
// ──────────────────────────────────────────────────
// The pre-existing `Manufacturing/OverhangAnalysis` walks the axisymmetric
// meridional contour and checks |dr/dx| at every station against the
// universal 45° rule. That's cheap + covers the inner + outer wall of
// an axisymmetric chamber, but it doesn't see surfaces that live outside
// the contour (instrumentation bosses, igniter cavities, feed-manifold
// stubs, non-axisymmetric plug geometry) and it can't vary the threshold
// per material.
//
// This module works on an abstract bag of `SurfaceSample`s (point +
// outward normal + patch area). Production callers synthesise the bag
// from whatever surface they care about — chamber shell, monolithic
// engine envelope, aerospike plug body. Tests build the bag directly
// to exercise the angle + material-threshold logic.
//
// Algorithm
// ─────────
// For each sample with outward normal n̂ and build-axis direction b̂:
//   • cos θ = n̂ · b̂ (positive when the normal points roughly with the build axis)
//   • β (from horizontal build plate) = 90° − angle between n̂ and -b̂
//     (the "downward-facing" angle — a surface that points straight
//     down relative to the build direction has β = 0 and needs full
//     sacrificial support).
//   • Flag when β < material.MinUnsupportedOverhangAngle_deg AND the
//     surface is at least partially down-facing (n̂ · (-b̂) > 0).
// Area of each flagged sample accumulates into
// `TotalOverhangArea_mm2`. Worst β is surfaced separately for the
// "how bad is the worst spot?" question.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Voxelforge.Geometry.LpbfAnalysis;

/// <summary>One flagged overhang sample surfaced in <see cref="OverhangReport.Violations"/>.</summary>
public readonly record struct OverhangViolation(
    Vector3 Point,
    Vector3 Normal,
    double  OverhangAngle_deg,   // β from horizontal; lower = worse
    double  Area_mm2);

/// <summary>Output of <see cref="OverhangAnalysis.Analyze"/>.</summary>
public sealed record OverhangReport(
    LpbfMaterialProfile Material,
    Vector3             BuildAxis,
    int                 ViolationCount,
    double              WorstOverhangAngle_deg,  // NaN when no samples are down-facing
    double              TotalOverhangArea_mm2,
    System.Collections.Generic.IReadOnlyList<OverhangViolation> Violations,
    // PH-34 (2026-04-29) — diagnostic count of sub-threshold overhang
    // samples filtered out via Material.MinFlaggedOverhangPatchArea_mm2.
    // Useful for distinguishing "design has zero real overhangs" from
    // "design has real overhangs but they're all noise-sized" (the
    // former should ship; the latter warrants a voxel-resolution check).
    // Defaults to 0 for back-compat when callers construct the record
    // without the new fields.
    int                 BelowThresholdPatchCount = 0,
    double              BelowThresholdPatchArea_mm2 = 0.0)
{
    /// <summary>True when no sample falls below the material's unsupported-angle floor.</summary>
    public bool IsPrintable => ViolationCount == 0;
}

/// <summary>
/// Sprint 27 (2026-04-23): voxel-surface-normal overhang screen. See the
/// file-level comment above for the design rationale.
/// </summary>
public static class OverhangAnalysis
{
    /// <summary>
    /// Run the screen. <paramref name="buildAxis"/> must be a unit vector
    /// pointing in the direction build layers are deposited (part grows
    /// ALONG this axis — typically +Z). Samples with a normal that is
    /// exactly perpendicular to the build axis (β = 90°, fully vertical
    /// wall) are self-supporting; samples with a normal pointing against
    /// the build axis (β = 0°, pure down-facing) are the worst case.
    /// </summary>
    public static OverhangReport Analyze(
        IEnumerable<SurfaceSample> samples,
        Vector3                    buildAxis,
        LpbfMaterialProfile        material)
    {
        if (samples  is null) throw new ArgumentNullException(nameof(samples));
        if (material is null) throw new ArgumentNullException(nameof(material));

        // Normalise the build axis so dot-products give cos(angle) directly.
        float axisLen = buildAxis.Length();
        if (axisLen < 1e-6f)
            throw new ArgumentException("buildAxis must be non-zero", nameof(buildAxis));
        Vector3 b = buildAxis / axisLen;

        double floor_deg = material.MinUnsupportedOverhangAngle_deg;
        // PH-34 (2026-04-29) — sub-threshold patches (e.g. 0.05-0.5 mm²
        // single-voxel surface jitter) self-support thermally during LPBF
        // and shouldn't fire the OVERHANG gate. Counter + cumulative area
        // stay in the report so the user can see how much was filtered.
        double minPatchArea_mm2 = material.MinFlaggedOverhangPatchArea_mm2;
        var violations = new List<OverhangViolation>();
        double worstBeta = double.NaN;
        double totalArea = 0.0;
        int    belowThresholdCount = 0;
        double belowThresholdArea = 0.0;

        foreach (var s in samples)
        {
            // Normalise normal in case the caller passed an unnormalised one.
            float nLen = s.Normal.Length();
            if (nLen < 1e-6f) continue;     // skip degenerate samples
            Vector3 n = s.Normal / nLen;

            // cos(angle between normal and -buildAxis): positive when the
            // surface points at least partially DOWN relative to the build
            // direction. Up-facing and side-facing samples are fine.
            double cosDown = -Vector3.Dot(n, b);
            if (cosDown <= 0) continue;     // up-facing / purely lateral — no overhang

            // β from horizontal = angle between n and -b (ψ). A pure
            // down-facing surface (n ∥ -b) has ψ = 0, which is the worst
            // overhang (β = 0°, surface is horizontal). A side wall
            // (n ⊥ b, cosDown = 0) has ψ = 90°, β = 90°, self-supporting.
            double beta_deg = Math.Acos(Math.Clamp(cosDown, -1.0, 1.0))
                            * 180.0 / Math.PI;

            if (double.IsNaN(worstBeta) || beta_deg < worstBeta)
                worstBeta = beta_deg;

            if (beta_deg < floor_deg)
            {
                if (s.Area_mm2 < minPatchArea_mm2)
                {
                    // Sub-threshold noise patch — count + area but DON'T
                    // emit a Violation. Sibling pattern to PH-3
                    // MinFlaggedPocketVolume_mm3 in TrappedPowderAnalysis.
                    belowThresholdCount++;
                    belowThresholdArea += s.Area_mm2;
                    continue;
                }
                violations.Add(new OverhangViolation(
                    Point:             s.Point,
                    Normal:            n,
                    OverhangAngle_deg: beta_deg,
                    Area_mm2:          s.Area_mm2));
                totalArea += s.Area_mm2;
            }
        }

        return new OverhangReport(
            Material:                    material,
            BuildAxis:                   b,
            ViolationCount:              violations.Count,
            WorstOverhangAngle_deg:      worstBeta,
            TotalOverhangArea_mm2:       totalArea,
            Violations:                  violations,
            BelowThresholdPatchCount:    belowThresholdCount,
            BelowThresholdPatchArea_mm2: belowThresholdArea);
    }
}
