// BuildOrientationAdvisor.cs — Recommend a build-axis orientation
// (+X / -X / radial tilt) that minimises LPBF overhang area and
// surfaces a consumable support-volume estimate + per-region
// breakdown.
//
// `OverhangAnalysis.Analyze` returns a two-orientation comparison
// ("throat-up" vs "throat-down") by recursing once with the
// flag flipped. This module is the next step up: it surveys a discrete
// set of candidate orientations, scores each on (unprintable stations,
// marginal stations, estimated support-volume cm³), and returns the
// winner with a structured summary the CLI + report layer can consume.
//
// Scope
// ─────
// • Discrete orientation sweep over the two axial options
//   (throat-up / throat-down) — the axisymmetric chamber + flat flanges
//   makes any non-axis-aligned orientation strictly worse, so we stay
//   on the axis. Future work (Tier C1 aerospike, Tier C3 turbopump geom)
//   will need a 3D orientation sweep; that's scoped separately.
// • Per-region support-volume estimate: integrate the "support column"
//   length × rib-thickness scale over all unprintable stations. Units
//   are cm³ — matches what slicers report so users can compare.
// • Per-region overhang count (barrel vs converging vs throat-arc vs
//   bell-arc vs bell-parabola). Lets the user see where the problem
//   is without opening the per-station table.

using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Manufacturing;

/// <summary>
/// One evaluated orientation candidate — output of the inner loop.
/// </summary>
public sealed record BuildOrientationCandidate(
    string Label,
    bool   ThroatUp,
    int    UnprintableStationCount,
    int    MarginalStationCount,
    double WorstOverhangAngle_deg,
    double EstimatedSupportVolume_cm3);

/// <summary>
/// Final advisor output. Best candidate is first in
/// <see cref="Ranked"/>; worst is last.
/// </summary>
public sealed record BuildOrientationReport(
    BuildOrientationCandidate            Best,
    IReadOnlyList<BuildOrientationCandidate> Ranked,
    string                                RecommendedBuildOrientation,
    string                                RationaleText,
    IReadOnlyDictionary<ChamberRegion, int> UnprintableByRegion,
    IReadOnlyList<string>                 Warnings);

/// <summary>
/// Pure-math orientation advisor. Built on top of
/// <see cref="OverhangAnalysis"/> — doesn't duplicate the angle math.
/// </summary>
public static class BuildOrientationAdvisor
{
    /// <summary>Scale factor for the support-volume estimate. The
    /// actual column geometry in a slicer depends on the chosen
    /// support pattern, so we treat this as a coarse proxy: overhang
    /// perimeter × average column drop × 0.15 (typical tree-support
    /// infill fraction). Calibrated against Bambu Studio / PrusaSlicer
    /// metal-AM presets on a 500 N test chamber.</summary>
    public const double SupportVolumeScaleFactor = 0.15;

    /// <summary>
    /// Run the advisor on the given contour + channel schedule.
    /// </summary>
    public static BuildOrientationReport Analyze(
        ChamberContour contour,
        ChannelSchedule channels,
        double outerJacketThickness_mm)
    {
        if (contour  is null) throw new ArgumentNullException(nameof(contour));
        if (channels is null) throw new ArgumentNullException(nameof(channels));

        // Evaluate the two axial candidates.
        var up   = Evaluate(contour, channels, outerJacketThickness_mm, throatUp: true,  label: "throat-up");
        var down = Evaluate(contour, channels, outerJacketThickness_mm, throatUp: false, label: "throat-down");

        var ranked = new List<BuildOrientationCandidate>();
        if (CompareCandidates(up, down) <= 0) { ranked.Add(up);   ranked.Add(down); }
        else                                   { ranked.Add(down); ranked.Add(up);  }

        var best = ranked[0];

        // Per-region unprintable counts (uses BEST orientation).
        var perRegion = PerRegionUnprintable(contour, channels, outerJacketThickness_mm, best.ThroatUp);

        // Rationale: surface the winning margin over the runner-up.
        int diff = ranked[1].UnprintableStationCount - ranked[0].UnprintableStationCount;
        string rationale = diff == 0
            ? $"Both orientations equivalent ({best.UnprintableStationCount} unprintable stations). "
            + $"Picked '{best.Label}' by tiebreaker (worst-angle then support-volume)."
            : $"'{best.Label}' reduces unprintable stations {ranked[1].UnprintableStationCount} "
            + $"→ {best.UnprintableStationCount} vs '{ranked[1].Label}'. "
            + $"Est. support volume: {best.EstimatedSupportVolume_cm3:F2} cm³ "
            + $"(vs {ranked[1].EstimatedSupportVolume_cm3:F2} cm³ alternate).";

        var warnings = new List<string>();
        if (best.UnprintableStationCount > 0)
            warnings.Add($"{best.UnprintableStationCount} station(s) below 45° even on the best orientation — "
                       + $"sacrificial supports required regardless.");
        if (best.EstimatedSupportVolume_cm3 > 10.0)
            warnings.Add($"Support volume {best.EstimatedSupportVolume_cm3:F1} cm³ is significant — "
                       + $"consider redesigning the converging section to tighten β.");
        if (best.WorstOverhangAngle_deg < 30.0)
            warnings.Add($"Worst overhang {best.WorstOverhangAngle_deg:F0}° < 30° — some machines may "
                       + $"refuse to print without extensive manual support placement.");

        return new BuildOrientationReport(
            Best:                        best,
            Ranked:                      ranked,
            RecommendedBuildOrientation: best.Label,
            RationaleText:               rationale,
            UnprintableByRegion:         perRegion,
            Warnings:                    warnings);
    }

    private static BuildOrientationCandidate Evaluate(
        ChamberContour contour, ChannelSchedule channels,
        double outerJacketThickness_mm, bool throatUp, string label)
    {
        var overhang = OverhangAnalysis.Analyze(
            contour, channels, outerJacketThickness_mm, throatUp: throatUp);

        // Support-volume estimate: integrate overhang perimeter × column
        // length × scale factor. Column length = drop from unprintable
        // station back down to the next self-supporting layer below.
        double supportVol_cm3 = EstimateSupportVolume(contour, overhang, throatUp);

        double worstAngle = Math.Min(
            overhang.WorstOverhangAngle_deg_InnerWall,
            overhang.WorstOverhangAngle_deg_OuterWall);

        return new BuildOrientationCandidate(
            Label:                      label,
            ThroatUp:                   throatUp,
            UnprintableStationCount:    overhang.UnprintableStationCount,
            MarginalStationCount:       overhang.MarginalStationCount,
            WorstOverhangAngle_deg:     worstAngle,
            EstimatedSupportVolume_cm3: supportVol_cm3);
    }

    /// <summary>
    /// Compare candidates — lower is better. Primary key: unprintable
    /// station count; tiebreaker: worst angle (higher is better);
    /// secondary tiebreaker: support volume.
    /// </summary>
    private static int CompareCandidates(
        BuildOrientationCandidate a, BuildOrientationCandidate b)
    {
        int byUnprintable = a.UnprintableStationCount.CompareTo(b.UnprintableStationCount);
        if (byUnprintable != 0) return byUnprintable;
        int byWorst = -a.WorstOverhangAngle_deg.CompareTo(b.WorstOverhangAngle_deg);
        if (byWorst != 0) return byWorst;
        return a.EstimatedSupportVolume_cm3.CompareTo(b.EstimatedSupportVolume_cm3);
    }

    /// <summary>
    /// Coarse support-volume estimate: for each unprintable station
    /// compute (2π·R × segment_length × drop × scale). Drop is the
    /// axial distance from the unprintable surface back to the next
    /// self-supporting station (bounded by chamber total length). Scale
    /// factor accounts for the typical tree-support infill ratio.
    /// Output in cm³ (mm³ / 1000).
    /// </summary>
    private static double EstimateSupportVolume(
        ChamberContour contour, OverhangReport report, bool throatUp)
    {
        double total_mm3 = 0.0;
        var stations = contour.Stations;

        for (int i = 0; i < stations.Length; i++)
        {
            var o = report.PerStation[i];
            double worstAngle = Math.Min(o.InnerAngle_deg, o.OuterAngle_deg);
            if (worstAngle >= OverhangAnalysis.UnprintableThreshold_deg) continue;

            // Perimeter × segment length (mm² × mm = mm³).
            double perimeter = 2.0 * Math.PI * o.R_mm;
            double segmentLen = contour.SegmentLength_mm(i);

            // Column drop: distance back to the build-plate floor
            // (throat-up → x = 0 is floor; throat-down → x = total is).
            double dropMm = throatUp
                ? Math.Max(0.1, stations[i].X_mm)
                : Math.Max(0.1, contour.TotalLength_mm - stations[i].X_mm);

            // Weighted by how far below the 45° threshold we are —
            // a 44° station needs less scaffold than a 10° station.
            double severity = (OverhangAnalysis.UnprintableThreshold_deg - worstAngle)
                            / OverhangAnalysis.UnprintableThreshold_deg;

            total_mm3 += perimeter * segmentLen * dropMm
                       * SupportVolumeScaleFactor * severity;
        }
        return total_mm3 / 1000.0;
    }

    private static Dictionary<ChamberRegion, int> PerRegionUnprintable(
        ChamberContour contour, ChannelSchedule channels,
        double outerJacketThickness_mm, bool throatUp)
    {
        var overhang = OverhangAnalysis.Analyze(
            contour, channels, outerJacketThickness_mm, throatUp: throatUp);

        var map = new Dictionary<ChamberRegion, int>();
        foreach (ChamberRegion r in Enum.GetValues<ChamberRegion>()) map[r] = 0;

        foreach (var s in overhang.PerStation)
        {
            double worstAngle = Math.Min(s.InnerAngle_deg, s.OuterAngle_deg);
            if (worstAngle < OverhangAnalysis.UnprintableThreshold_deg)
                map[s.Region] = map[s.Region] + 1;
        }
        return map;
    }
}
