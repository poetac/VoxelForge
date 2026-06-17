// PrintOrientationAdvisor.cs — Sprint 27 (2026-04-23): sweep the six
// axis-aligned candidate orientations and pick the one that minimises
// support volume + overhang area.
//
// Scope: ±X, ±Y, ±Z. For an axisymmetric chamber the two meaningful
// orientations are ±X (chamber axis = build axis), but the six-way sweep
// is cheap + generalises cleanly to monolithic engines with asymmetric
// plumbing. Optional finer sweeps (15°-cone at the best axis) are
// deferred — the coarse sweep is enough for a report recommendation.
//
// Scoring (lower is better):
//   score = overhang_area_mm2
//         + support_volume_cm3 * SupportVolumeWeight
//
// SupportVolumeWeight is 100 (so 1 cm³ of support ≈ 100 mm² of overhang
// area — roughly the vertical face area one column of tree-support
// replaces). This is a coarse knob; the absolute number doesn't matter
// as long as candidates rank correctly.
//
// Output is a score breakdown so the UI + report can explain "we picked
// +X because it reduces support volume by N cm³ vs the runner-up."

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Voxelforge.Geometry.LpbfAnalysis;

public readonly record struct AxisCandidate(
    string  Label,
    Vector3 BuildAxis,
    int     OverhangViolationCount,
    double  TotalOverhangArea_mm2,
    double  EstimatedSupportVolume_cm3,
    double  Score);

public sealed record PrintOrientationReport(
    AxisCandidate                                      Best,
    System.Collections.Generic.IReadOnlyList<AxisCandidate> Ranked,
    string                                             Rationale)
{
    /// <summary>Short human-readable label for the recommended build axis.</summary>
    public string RecommendedAxis => Best.Label;
}

/// <summary>
/// Sprint 27 (2026-04-23): six-axis print-orientation advisor. Cost is
/// 6 × overhang-screen + 6 × support-volume estimate; kept fast (&lt; 1 s
/// on the default sample density) so it's safe to call from the opt-in
/// printability block in <c>RegenChamberOptimization.Evaluate</c>.
/// </summary>
public static class PrintOrientationAdvisor
{
    /// <summary>Weight converting support-volume cm³ to the overhang-area
    /// mm² unit used in the score. 100 mm²/cm³ — tuned so the two
    /// contributions are comparable on the 500 N test chamber.</summary>
    public const double SupportVolumeWeight = 100.0;

    /// <summary>Default sweep: the six axis-aligned cardinal directions.</summary>
    public static readonly (string Label, Vector3 Axis)[] DefaultAxes =
    {
        ("+X", new Vector3( 1, 0, 0)),
        ("-X", new Vector3(-1, 0, 0)),
        ("+Y", new Vector3( 0, 1, 0)),
        ("-Y", new Vector3( 0,-1, 0)),
        ("+Z", new Vector3( 0, 0, 1)),
        ("-Z", new Vector3( 0, 0,-1)),
    };

    public static PrintOrientationReport Analyze(
        IReadOnlyList<SurfaceSample> samples,
        LpbfMaterialProfile          material,
        (string Label, Vector3 Axis)[]? axes = null)
    {
        if (samples  is null) throw new ArgumentNullException(nameof(samples));
        if (material is null) throw new ArgumentNullException(nameof(material));

        axes ??= DefaultAxes;
        var candidates = new List<AxisCandidate>(axes.Length);

        foreach (var (label, axis) in axes)
        {
            var report = OverhangAnalysis.Analyze(samples, axis, material);
            double supportVol = EstimateSupportVolume_cm3(report);
            double score = report.TotalOverhangArea_mm2
                         + supportVol * SupportVolumeWeight;

            candidates.Add(new AxisCandidate(
                Label:                      label,
                BuildAxis:                  axis,
                OverhangViolationCount:     report.ViolationCount,
                TotalOverhangArea_mm2:      report.TotalOverhangArea_mm2,
                EstimatedSupportVolume_cm3: supportVol,
                Score:                      score));
        }

        // Stable tie-break by Label (string Ordinal) so ties produce a
        // deterministic ordering — flagged by VFD015 (#565) on flat-score
        // axisymmetric geometries.
        candidates.Sort((a, b) =>
        {
            int c = a.Score.CompareTo(b.Score);
            return c != 0 ? c : string.CompareOrdinal(a.Label, b.Label);
        });
        var best = candidates[0];

        string rationale;
        if (candidates.Count > 1 && candidates[1].Score > best.Score)
        {
            rationale =
                $"Best axis '{best.Label}' scores {best.Score:F1} "
              + $"(overhang area {best.TotalOverhangArea_mm2:F0} mm² + "
              + $"support {best.EstimatedSupportVolume_cm3:F2} cm³) "
              + $"vs runner-up '{candidates[1].Label}' at "
              + $"{candidates[1].Score:F1}.";
        }
        else
        {
            rationale =
                $"All candidates score within noise; picked '{best.Label}' "
              + $"by tiebreaker order. Support volume {best.EstimatedSupportVolume_cm3:F2} cm³.";
        }

        return new PrintOrientationReport(
            Best:      best,
            Ranked:    candidates,
            Rationale: rationale);
    }

    /// <summary>
    /// Coarse support-volume estimate. For each overhang violation we
    /// approximate the support column as (patch area × severity × unit
    /// drop 5 mm). Severity scales linearly from 0 (at the floor) to 1
    /// (at 0° pure down-facing). Output in cm³.
    /// </summary>
    private static double EstimateSupportVolume_cm3(OverhangReport report)
    {
        double floor = report.Material.MinUnsupportedOverhangAngle_deg;
        double mm3 = 0;
        foreach (var v in report.Violations)
        {
            double severity = Math.Clamp(
                (floor - v.OverhangAngle_deg) / Math.Max(floor, 1.0),
                0.0, 1.0);
            // Unit drop 5 mm matches what a typical slicer places on an
            // unsupported face before the column merges with an adjacent
            // support. The result is proportional; the absolute value
            // scales the rank.
            mm3 += v.Area_mm2 * severity * 5.0;
        }
        return mm3 / 1000.0;    // mm³ → cm³
    }
}
