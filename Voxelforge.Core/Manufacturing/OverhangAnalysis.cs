// OverhangAnalysis.cs — Per-station LPBF overhang-angle check vs the
// standard 45° rule.
//
// Rule recap. In powder-bed fusion each layer must be supported by the
// layer below. For a surface element whose outward normal makes angle
// θ with the build direction, it needs support if the angle β between
// the surface and the horizontal build plate is less than ~45°:
//
//   build axis (+X) ────────────►
//   β = 90° − θ    (angle between surface and horizontal)
//   surface with β < 45°  ⇒ overhang ⇒ needs support
//
// For an axisymmetric chamber printed with the X axis vertical, the
// meridional contour slope `dr/dx` maps directly to the overhang metric:
//
//   |dr/dx| > 1  ⇒  β < 45°  ⇒  needs support
//   |dr/dx| < 1  ⇒  β > 45°  ⇒  self-supporting (up to tolerance)
//
// Two surfaces matter:
//   • Inner wall (gas-side): overhangs into the combustion cavity when
//     dr_inner/dx is NEGATIVE in the print direction. Typical converging
//     section has slope = −tan(30°) = −0.577 → β = 60°, fine.
//   • Outer wall (jacket): overhangs into free air when dr_outer/dx is
//     POSITIVE (wall flaring out as layer height rises). Typical bell
//     cone slope with 30° θ_n tops out around +0.577 → β = 60°, fine.
//
// This module evaluates both sides at every station and reports:
//   • worst overhang angle β_min on each surface
//   • number of stations where β < 45° (unprintable)
//   • number of stations where β < 55° (marginal — consider supports)
//   • recommended build orientation (throat-up vs throat-down chosen by
//     which orientation minimises the unprintable-station count).

using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Manufacturing;

public sealed record OverhangReport(
    double WorstOverhangAngle_deg_InnerWall,
    double WorstOverhangAngle_deg_OuterWall,
    int UnprintableStationCount,      // β < 45° on either surface
    int MarginalStationCount,         // 45° ≤ β < 55°
    bool AllSelfSupporting,
    string RecommendedBuildOrientation,
    string[] Warnings,
    OverhangStation[] PerStation);

/// <summary>One station's overhang data. Angles are β (from horizontal);
/// negative slopes on a surface are non-overhanging for that surface and
/// reported as 90° (fully supported).</summary>
public readonly record struct OverhangStation(
    int Index,
    double X_mm,
    double R_mm,
    double InnerSlope,            // dr_inner / dx
    double OuterSlope,            // dr_outer / dx
    double InnerAngle_deg,        // β of inner surface (90° when non-overhanging)
    double OuterAngle_deg,
    bool InnerOverhang,
    bool OuterOverhang,
    ChamberRegion Region);

public static class OverhangAnalysis
{
    /// <summary>β threshold below which a surface is deemed unprintable
    /// without sacrificial supports. Industry practice is 45° but some
    /// machines can do 30° with tuned parameters — keep the threshold
    /// conservative for preliminary design.</summary>
    public const double UnprintableThreshold_deg = 45.0;

    /// <summary>β threshold for "marginal" — printable but likely to have
    /// cosmetic dross on the down-facing surface.</summary>
    public const double MarginalThreshold_deg = 55.0;

    /// <summary>
    /// Run the overhang check on the given contour + channel schedule.
    /// </summary>
    /// <param name="contour">Inner-wall contour (gas side).</param>
    /// <param name="channels">Channel schedule; used to reconstruct the
    /// outer-jacket contour via inner + t_wall + h(x) + t_jacket.</param>
    /// <param name="outerJacketThickness_mm">Jacket wall thickness.</param>
    /// <param name="throatUp">True = throat at top of build (converging
    /// flares down as layers build up). Try both orientations to pick
    /// the better one.</param>
    public static OverhangReport Analyze(
        ChamberContour contour,
        ChannelSchedule channels,
        double outerJacketThickness_mm,
        bool throatUp = true)
    {
        int N = contour.Stations.Length;
        var stationsOut = new OverhangStation[N];
        double worstInner = 90.0;
        double worstOuter = 90.0;
        int unprintable = 0;
        int marginal = 0;
        var warnings = new List<string>();

        double xThroat = contour.Stations[contour.ThroatIndex].X_mm;
        double xExit = contour.TotalLength_mm;

        for (int i = 0; i < N; i++)
        {
            var s = contour.Stations[i];

            // Inner-wall slope from contour (positive = flaring out with +x).
            double slopeInner = s.Slope;
            // Outer-jacket slope: dr_outer/dx = slopeInner + d(h_ch)/dx.
            // Channel height varies piecewise-linearly with x; central-diff
            // gives a clean approximation.
            double slopeOuter = ApproximateOuterSlope(contour, channels, i, xThroat, xExit);

            // When the build axis is flipped (throat-down), the sign of the
            // overhang direction flips for both surfaces.
            double effectiveInner = throatUp ? slopeInner : -slopeInner;
            double effectiveOuter = throatUp ? slopeOuter : -slopeOuter;

            // Inner wall overhangs when dr_inner/dx < 0 (wall collapsing
            // inward as layer rises). Outer wall overhangs when > 0.
            bool innerOverhang = effectiveInner < 0;
            bool outerOverhang = effectiveOuter > 0;

            double innerAngle = innerOverhang
                ? Math.Atan2(1.0, Math.Abs(effectiveInner)) * 180.0 / Math.PI
                : 90.0;
            double outerAngle = outerOverhang
                ? Math.Atan2(1.0, Math.Abs(effectiveOuter)) * 180.0 / Math.PI
                : 90.0;

            worstInner = Math.Min(worstInner, innerAngle);
            worstOuter = Math.Min(worstOuter, outerAngle);

            double worstHere = Math.Min(innerAngle, outerAngle);
            if (worstHere < UnprintableThreshold_deg) unprintable++;
            else if (worstHere < MarginalThreshold_deg) marginal++;

            stationsOut[i] = new OverhangStation(
                Index: i,
                X_mm: s.X_mm,
                R_mm: s.R_mm,
                InnerSlope: slopeInner,
                OuterSlope: slopeOuter,
                InnerAngle_deg: innerAngle,
                OuterAngle_deg: outerAngle,
                InnerOverhang: innerOverhang,
                OuterOverhang: outerOverhang,
                Region: s.Region);
        }

        // Pick the better of (throat-up, throat-down) automatically by
        // recursing without the recommendation once. Cheap — N ≈ 240.
        string orientation = throatUp ? "throat-up" : "throat-down";
        if (throatUp && unprintable > 0)
        {
            var alt = Analyze(contour, channels, outerJacketThickness_mm, throatUp: false);
            if (alt.UnprintableStationCount < unprintable)
                orientation = $"throat-down (alt orientation reduces unprintable count {unprintable} → {alt.UnprintableStationCount})";
        }

        if (unprintable > 0)
            warnings.Add($"{unprintable}/{N} stations overhang beyond the 45° rule — require sacrificial supports or re-orient build.");
        if (marginal > 0 && unprintable == 0)
            warnings.Add($"{marginal}/{N} stations between 45°–55° — expect cosmetic dross on down-facing surfaces.");
        if (worstInner < 30 || worstOuter < 30)
            warnings.Add($"Worst overhang {Math.Min(worstInner, worstOuter):F0}° well below 30°. Supports unavoidable even with tuned parameters.");

        return new OverhangReport(
            WorstOverhangAngle_deg_InnerWall: worstInner,
            WorstOverhangAngle_deg_OuterWall: worstOuter,
            UnprintableStationCount: unprintable,
            MarginalStationCount: marginal,
            AllSelfSupporting: unprintable == 0 && marginal == 0,
            RecommendedBuildOrientation: orientation,
            Warnings: warnings.ToArray(),
            PerStation: stationsOut);
    }

    /// <summary>
    /// Outer jacket slope ≈ inner-wall slope plus the local change in
    /// channel height per mm of axial distance. For the 3-anchor schedule
    /// used throughout the project, h varies linearly in two segments
    /// (chamber→throat, throat→exit) so analytic slope is trivial.
    /// </summary>
    private static double ApproximateOuterSlope(
        ChamberContour contour, ChannelSchedule ch, int i, double xThroat, double xExit)
    {
        var s = contour.Stations[i];
        double x = s.X_mm;
        double dh_dx = (x <= xThroat)
            ? (ch.ChannelHeightAtThroat_mm - ch.ChannelHeightAtChamber_mm)
              / Math.Max(xThroat, 1e-6)
            : (ch.ChannelHeightAtExit_mm - ch.ChannelHeightAtThroat_mm)
              / Math.Max(xExit - xThroat, 1e-6);
        return s.Slope + dh_dx;
    }
}
