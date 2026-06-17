// SensorBossClash.cs — Sprint 28 (2026-04-24):
// Pure-math clash detection for instrumentation bosses drilled through
// the regen chamber outer jacket. Closes the hot-fire-readiness gap
// noted in SensorBossPresets.cs: "No clash detection with cooling
// channels — users place bosses visually in the STL." Now the
// INSTRUMENTATION_TAP_INTERFERENCE gate (FeasibilityGate.cs) consumes
// this module's output and surfaces any violation to the SA optimizer
// + UI.
//
// What gets checked
// ─────────────────
//   1. Channel clash — for axial topology, regen channels sit at N
//      equal azimuthal positions θ_k = 2π·k/N. A boss drilled radially
//      at (axialFraction, θ_b) clashes with channel k when the arc
//      distance at the chamber-wall radius is below
//        boss_bore_radius + effective_channel_half_width + clearance.
//      Channel half-width is estimated as ChannelDutyFraction × half-
//      pitch arc: typical LPBF regen jackets have ~50 % rib-to-channel
//      ratio at the chamber (rib ≈ 0.8 mm, channel ≈ 1.0 mm at
//      representative pitch). This is a heuristic — a future sprint
//      could read the actual ChannelSchedule to get the per-station
//      channel width exactly. The heuristic errs on the side of
//      flagging fewer false positives: a design that fails the check
//      with the heuristic will still be infeasible against the real
//      channel widths.
//
//   2. Boss-vs-boss clash — two bosses placed at the same
//      (axialFraction, azimuth) within (larger of the two boss ODs +
//      clearance) arc distance on the local chamber radius trigger
//      the gate.
//
// What is deliberately NOT checked here (yet)
// ───────────────────────────────────────────
//   • Helical channel topology. The θ(x) function varies along the
//     axial coordinate and the gate would need to evaluate the boss
//     azimuth against the helix angle at the boss's axial station.
//     Returning "no clash" in the helical case is the conservative
//     direction — a future sprint should add the helix check if a
//     design lands on instrumentation for a helical jacket.
//   • TPMS topology. No discrete channels to clash with; every point
//     on the jacket is part of the lattice. User-facing guidance for
//     TPMS is "don't drill bosses in TPMS regions; pick a pre-manifold
//     location" — captured in the gate's description, not as a
//     numeric clash.
//   • Aerospike plug-envelope clash. Aerospike bosses aren't wired
//     end-to-end yet (the SensorBosses field is consumed by
//     ChamberVoxelBuilder, which only runs on regen topologies). When
//     aerospike bosses are wired in a later sprint, this module gains
//     an aerospike-pathway.
//
// Threading / PicoGK
// ──────────────────
// Pure math. No PicoGK library instantiation, no Voxels, no
// IImplicit. Safe to call from xUnit (no ADR-005 risk) and from any
// SA candidate evaluation.

using Voxelforge.Chamber;

namespace Voxelforge.Geometry;

/// <summary>
/// Sprint 28 (2026-04-24): one entry in the sensor-boss clash report.
/// A design can produce zero, one, or many of these; the caller
/// surfaces each one as a separate <c>INSTRUMENTATION_TAP_INTERFERENCE</c>
/// feasibility violation.
/// </summary>
/// <param name="Kind">What the boss clashed with — a channel at a
/// specific θ index, or another boss at a specific list index.</param>
/// <param name="BossIndex">Index into the original <c>SensorBosses</c>
/// list identifying the offending boss.</param>
/// <param name="OtherIndex">For channel clashes: the channel index
/// (0 .. N-1). For boss-vs-boss clashes: the other boss's list index.
/// -1 when not applicable.</param>
/// <param name="ArcDistance_mm">Observed arc distance at the clash
/// station.</param>
/// <param name="MinClearance_mm">Required arc distance for a non-
/// clashing placement (boss OD + channel half-width or peer boss OD +
/// <see cref="SensorBossClashEvaluator.LpbfFloor_mm"/>).</param>
/// <param name="Description">Human-readable summary for the gate
/// violation description.</param>
public readonly record struct SensorBossClashReport(
    SensorBossClashKind Kind,
    int BossIndex,
    int OtherIndex,
    double ArcDistance_mm,
    double MinClearance_mm,
    string Description);

public enum SensorBossClashKind
{
    ChannelOverlap,
    BossOverlap,
}

/// <summary>
/// Sprint 28: pure-math clash evaluator for instrumentation bosses on
/// the regen chamber jacket. Returns an empty list on every non-regen
/// topology (aerospike, TPMS, None) and on designs with no bosses or
/// no channels — so wiring it unconditionally into the feasibility
/// evaluator is a free no-op for every pre-Sprint-28 design.
/// </summary>
public static class SensorBossClashEvaluator
{
    /// <summary>
    /// Universal LPBF feature-size floor. Matches
    /// <see cref="LpbfFeatureFloor_mm"/> used by the element-clearance
    /// gate so sensor-boss clearance inherits the same print-scale
    /// discipline. Exposed as a constant rather than pulled from the
    /// material-specific Sprint 27 profiles because that data is for
    /// overhangs, not for clearance.
    /// </summary>
    public const double LpbfFloor_mm = 0.30;

    /// <summary>
    /// Additional clearance (mm) added to every arc-distance threshold.
    /// Keeps the gate well clear of the hard "bore would nick channel
    /// wall" boundary so SA candidates near the threshold don't land
    /// infeasible against numerical-precision jitter. 1 mm ≈ ½ voxel
    /// at default 0.8 mm voxel size + an LPBF dimensional-tolerance
    /// allowance.
    /// </summary>
    public const double SafetyClearance_mm = 1.0;

    /// <summary>
    /// Effective channel-width fraction of the pitch arc (0..1).
    /// ~50 % matches typical LPBF regen-jacket rib-to-channel ratios
    /// at chamber stations (rib 0.8 mm, channel 1.0 mm at
    /// representative pitches). Tuning knob if the heuristic needs
    /// to tighten for a specific design.
    /// </summary>
    public const double ChannelDutyFraction = 0.50;

    /// <summary>
    /// Evaluate boss-vs-channel and boss-vs-boss clashes on a regen
    /// chamber. Returns an empty list when there's nothing to check.
    /// </summary>
    /// <param name="bosses">Sensor-boss list (from <c>RegenChamberDesign.SensorBosses</c>).</param>
    /// <param name="channelCount">Regen channel count (from <c>RegenChamberDesign.ChannelCount</c>). Zero skips the channel check.</param>
    /// <param name="topology">Channel topology. Only
    /// <see cref="Optimization.ChannelTopology.Axial"/> runs the
    /// channel-overlap check; every other value (Helical, None,
    /// TPMS*, Aerospike, LinearAerospike) short-circuits that branch.</param>
    /// <param name="contour">Chamber contour for station-to-x + local-radius lookup.</param>
    public static System.Collections.Generic.IReadOnlyList<SensorBossClashReport> Evaluate(
        System.Collections.Generic.IReadOnlyList<SensorBoss>? bosses,
        int channelCount,
        Optimization.ChannelTopology topology,
        ChamberContour contour)
    {
        if (bosses is null || bosses.Count == 0)
            return System.Array.Empty<SensorBossClashReport>();

        var reports = new System.Collections.Generic.List<SensorBossClashReport>(4);
        double L_total = contour.TotalLength_mm;

        for (int i = 0; i < bosses.Count; i++)
        {
            var b = bosses[i];
            if (!SensorBossPresets.All.TryGetValue(b.Type, out var spec)) continue;

            double x_mm      = System.Math.Clamp(b.AxialFraction, 0.0, 1.0) * L_total;
            double rLocal_mm = InterpRadius(contour, x_mm);
            if (rLocal_mm <= 0.0) continue;

            double boreRadius_mm = 0.5 * spec.BoreDiameter_mm;
            double bossAz_rad    = DegreesToRadians(b.AzimuthDeg);

            // ── Boss-vs-channel check (axial topology only) ───────────
            // Channels sit at equal azimuthal spacing. Closest channel
            // θ to boss θ_b is at θ_k where k = round(θ_b · N / (2π))
            // (wrapped into [0, N)). Arc distance at r_local between
            // boss and channel centerline.
            if (topology == Optimization.ChannelTopology.Axial
                && channelCount > 0)
            {
                double channelPitch_rad   = 2.0 * System.Math.PI / channelCount;
                double halfChannelPitch_mm = 0.5 * channelPitch_rad * rLocal_mm;
                // Effective half-width of the channel at this station:
                // ChannelDutyFraction × half-pitch. Physical meaning:
                // if channels occupy 50 % of the pitch circumference,
                // each channel is ≈ ½-pitch wide, so its half-width is
                // ¼-pitch at r_local.
                double effectiveHalfChannelWidth_mm =
                    ChannelDutyFraction * halfChannelPitch_mm;

                // Azimuthal distance to nearest channel centerline.
                double azDelta = AzimuthalDelta(bossAz_rad, channelPitch_rad);
                double arcToChannel_mm = System.Math.Abs(azDelta) * rLocal_mm;

                // Required clearance: boss bore radius + channel
                // half-width (physical barrier) + safety clearance +
                // LPBF floor.
                double minClearance_mm = boreRadius_mm
                                       + effectiveHalfChannelWidth_mm
                                       + SafetyClearance_mm + LpbfFloor_mm;

                if (arcToChannel_mm < minClearance_mm)
                {
                    int channelIdx = NearestChannelIndex(bossAz_rad, channelCount);
                    reports.Add(new SensorBossClashReport(
                        Kind:           SensorBossClashKind.ChannelOverlap,
                        BossIndex:      i,
                        OtherIndex:     channelIdx,
                        ArcDistance_mm: arcToChannel_mm,
                        MinClearance_mm: minClearance_mm,
                        Description:
                            $"Sensor boss {i} ({spec.DisplayName}) at "
                          + $"x={x_mm:F1} mm, az={b.AzimuthDeg:F1}° has arc distance "
                          + $"{arcToChannel_mm:F2} mm to channel {channelIdx} "
                          + $"(pitch {(channelPitch_rad * 180.0 / System.Math.PI):F1}°); "
                          + $"needs ≥ {minClearance_mm:F2} mm. Remediation: "
                          + $"shift boss azimuth toward mid-rib, reduce channel "
                          + $"count, or move the boss to the pre-manifold region."));
                }
            }

            // ── Boss-vs-boss check (topology-agnostic) ───────────────
            // Two bosses at the same axial station clash when their
            // arc-distance at r_local falls below (larger boss OD +
            // LPBF floor). Axial offset relaxes the check: if the
            // axial separation already exceeds the smaller boss height,
            // the radial drillings don't intersect.
            for (int j = i + 1; j < bosses.Count; j++)
            {
                var bj = bosses[j];
                if (!SensorBossPresets.All.TryGetValue(bj.Type, out var specJ)) continue;

                double xj_mm = System.Math.Clamp(bj.AxialFraction, 0.0, 1.0) * L_total;

                // Axial clearance: if bosses are far apart in x, the
                // two radial bore cylinders don't share any volume so
                // the arc check is vacuous. Threshold = 0.5 × (OD_i +
                // OD_j) — if |Δx| exceeds that, treat as non-clashing.
                double axialGap_mm = System.Math.Abs(x_mm - xj_mm);
                double halfOdSum   = 0.5 * (spec.BossOuterDiameter_mm + specJ.BossOuterDiameter_mm);
                if (axialGap_mm > halfOdSum) continue;

                double bossAzJ_rad = DegreesToRadians(bj.AzimuthDeg);
                double azDelta     = WrapPi(bossAzJ_rad - bossAz_rad);
                double arc_mm      = System.Math.Abs(azDelta) * rLocal_mm;

                double minClearance_mm = System.Math.Max(
                                             spec.BossOuterDiameter_mm,
                                             specJ.BossOuterDiameter_mm)
                                       + SafetyClearance_mm + LpbfFloor_mm;
                if (arc_mm < minClearance_mm)
                {
                    reports.Add(new SensorBossClashReport(
                        Kind:           SensorBossClashKind.BossOverlap,
                        BossIndex:      i,
                        OtherIndex:     j,
                        ArcDistance_mm: arc_mm,
                        MinClearance_mm: minClearance_mm,
                        Description:
                            $"Sensor bosses {i} ({spec.DisplayName}) and "
                          + $"{j} ({specJ.DisplayName}) are only {arc_mm:F2} mm "
                          + $"apart on the chamber surface (need ≥ {minClearance_mm:F2} mm). "
                          + $"Remediation: separate them axially, or place them "
                          + $"on opposite sides of the chamber (~180° apart)."));
                }
            }
        }
        return reports;
    }

    /// <summary>
    /// Linear-interpolate the chamber wall radius at axial position
    /// <paramref name="x_mm"/>. Mirrors the interpolation used by the
    /// voxel builder when placing sensor bosses, so the clash check
    /// agrees with the geometry that will actually be printed.
    /// </summary>
    private static double InterpRadius(ChamberContour contour, double x_mm)
    {
        var stations = contour.Stations;
        int n = stations.Length;
        if (n == 0) return 0;
        if (x_mm <= stations[0].X_mm) return stations[0].R_mm;
        if (x_mm >= stations[^1].X_mm) return stations[^1].R_mm;

        for (int k = 0; k < n - 1; k++)
        {
            double x0 = stations[k].X_mm;
            double x1 = stations[k + 1].X_mm;
            if (x_mm >= x0 && x_mm <= x1)
            {
                double t = (x_mm - x0) / System.Math.Max(x1 - x0, 1e-9);
                return stations[k].R_mm + t * (stations[k + 1].R_mm - stations[k].R_mm);
            }
        }
        return stations[^1].R_mm;
    }

    private static double DegreesToRadians(double deg)
        => deg * System.Math.PI / 180.0;

    /// <summary>
    /// Wrap an angle into (−π, π].
    /// </summary>
    private static double WrapPi(double a)
    {
        while (a > System.Math.PI)  a -= 2.0 * System.Math.PI;
        while (a <= -System.Math.PI) a += 2.0 * System.Math.PI;
        return a;
    }

    /// <summary>
    /// Shortest signed azimuthal distance from the boss angle to the
    /// nearest channel centerline, given equal spacing
    /// <paramref name="channelPitch_rad"/>. Result in (−pitch/2,
    /// pitch/2].
    /// </summary>
    private static double AzimuthalDelta(double bossAz_rad, double channelPitch_rad)
    {
        double nearest = System.Math.Round(bossAz_rad / channelPitch_rad) * channelPitch_rad;
        double delta   = bossAz_rad - nearest;
        // Wrap into (−pitch/2, pitch/2].
        double half = 0.5 * channelPitch_rad;
        while (delta >  half) delta -= channelPitch_rad;
        while (delta <= -half) delta += channelPitch_rad;
        return delta;
    }

    private static int NearestChannelIndex(double bossAz_rad, int channelCount)
    {
        double pitch = 2.0 * System.Math.PI / channelCount;
        int idx = (int)System.Math.Round(bossAz_rad / pitch);
        // Wrap into [0, N).
        idx %= channelCount;
        if (idx < 0) idx += channelCount;
        return idx;
    }
}
