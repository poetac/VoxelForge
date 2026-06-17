// MonolithicFeasibility.cs — Body-intersection feasibility gate for
// the monolithic engine bundle. The router has no geometric check
// that tubes clear the chamber body on its own; this gate fills that
// slot.
//
// What this models
// ────────────────
// `FeedManifoldRouter` emits cylindrical tubes whose endpoints terminate
// on body surfaces (tank interface, pump inlet / discharge, injector
// dome, preburner inlet / outlet). The router has no knowledge of the
// chamber, pump casing, or preburner envelopes, so a midsection of a
// routed tube can cut straight through a solid body. Voxel `BoolAdd`
// silently absorbs such intersections (they become part of the wall),
// but the result is physically nonsensical — an internally-blocked
// feed line.
//
// The gate samples each tube's interior midpoint(s) and checks whether
// any sample sits inside an envelope that is NOT an endpoint body. If
// so, it emits a `MONOLITHIC_BODY_INTERSECTION` violation. Endpoints
// are deliberately NOT sampled since they legitimately terminate on
// body surfaces.
//
// Envelope approximations
// ───────────────────────
//   • Chamber — axis-aligned cylinder along +X, radius = max contour
//     outer radius, length = contour.TotalLength.
//   • Pump — axis-aligned cylinder along +Z (local frame), radius =
//     casing OD, length = total pump length. Placed at the pump origin
//     passed to `MonolithicEngineBuilder`.
//   • Preburner — axis-aligned cylinder along +X (local frame), radius
//     = outer capsule radius, length = total capsule length. Placed at
//     the preburner origin.
//
// Cylindrical hulls are conservative for the capsule (the capsule's
// hemispherical endcaps fit inside its bounding cylinder) and exact
// for the other two bodies.
//
// Line-segment sampling: each tube leg is sampled at
// `InteriorSampleCount` interior stations (strict-interior — endpoints
// skipped since they legitimately touch a body face). With 8 interior
// samples per leg the probe density captures the minimum-distance
// along the segment to within ~6 % of the tube length — well below the
// default 2 mm LPBF clearance even for a typical 200 mm tube leg.
//
// Further extensions
// ──────────────────
//   (A) Turbine wheel envelope — the turbine sits on the common shaft
//       OPPOSITE the pump inducer. With the pump anchored at
//       z ∈ [0, pump.TotalLength], the turbine sits at
//       z ∈ [−turbine.TotalLength, 0] (see `TurbineGeometryGenerator`
//       header block). In world coordinates the turbine envelope is
//       a +Z-axis cylinder at the pump's XY with z-base =
//       `pumpOrigin.Z − turbine.TotalLength` and radius =
//       `turbine.HousingOuterRadius_mm`. Added to
//       `MonolithicBodyEnvelopes`; evaluator samples both pump halves
//       AND both turbine halves.
//   (B) Tube-vs-tube intersection — for each unordered pair of tubes
//       in the layout, compute the minimum segment-to-segment distance.
//       When the gap falls below `clearance + tube_a.OuterRadius +
//       tube_b.OuterRadius` the gate emits
//       `MONOLITHIC_TUBE_INTERSECTION`. Per-pair aggregation emits at
//       most one violation per (A, B) pair; pairs that share an
//       endpoint are whitelisted (legitimate branch joints at a pump
//       discharge or a preburner inlet).
//
// Not modelled
// ────────────
//   • Flange body envelopes — intentionally skipped: the flange hugs
//     the pump casing so any tube routed around the pump clears the
//     flange by construction.
//   • Turbine scroll / volute casing — the housing radius is modeled
//     as the wheel's outer shell; the asymmetric inlet scroll is not
//     modelled as an envelope (future work if router ever emits tubes
//     in that region).

using System.Numerics;
using Voxelforge.Chamber;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;
using Voxelforge.Turbopump;

namespace Voxelforge.Geometry;

/// <summary>
/// Immutable snapshot of the engine body envelopes consumed by
/// <see cref="MonolithicFeasibility.Evaluate"/>. Pure data. Includes
/// optional fuel / ox <see cref="TurbineGeometry"/> envelopes (axial
/// extent = pumpOrigin.Z − turbine.TotalLength up to pumpOrigin.Z).
/// Default <c>null</c> so legacy callers keep compiling; the
/// evaluator skips turbine sampling when either the geometry or the
/// matching pump origin is absent.
/// </summary>
public sealed record MonolithicBodyEnvelopes(
    double ChamberOuterRadius_mm,
    double ChamberLength_mm,
    TurbopumpGeometry? FuelPumpGeometry,
    Vector3 FuelPumpOrigin,
    TurbopumpGeometry? OxPumpGeometry,
    Vector3 OxPumpOrigin,
    PreburnerVoxelGeometry? PreburnerGeometry,
    Vector3 PreburnerOrigin,
    TurbineGeometry? FuelTurbineGeometry = null,
    TurbineGeometry? OxTurbineGeometry = null,
    // Optional aerospike plug envelope for the monolithic-aerospike
    // composition path. When non-null,
    // the plug occupies the axisymmetric volume rooted at
    // <see cref="AerospikePlugOrigin"/>, extending along +X for
    // <see cref="AerospikeBuildResult.PlugTruncatedLength_mm"/> with
    // radius linearly tapering from the throat-outer-radius to the
    // plug-base radius (Angelino conical approximation — same fidelity
    // as the builder's voxel assembly). Pre-throat combustion chamber
    // is covered by the existing <see cref="ChamberOuterRadius_mm"/> /
    // <see cref="ChamberLength_mm"/> fields, so a monolithic aerospike
    // design populates both.
    AerospikeBuildResult? AerospikePlug = null,
    Vector3 AerospikePlugOrigin = default);

/// <summary>
/// Body-intersection evaluator for monolithic engine bundles. Returns
/// a standard <see cref="FeasibilityGateResult"/> so UI / CLI surface
/// the violations identically to regen + aerospike gates.
/// </summary>
public static class MonolithicFeasibility
{
    /// <summary>
    /// Default minimum clearance (mm) a tube interior must maintain
    /// from any non-endpoint body envelope. Violations emit
    /// <c>MONOLITHIC_BODY_INTERSECTION</c>. Tunable; default 2 mm
    /// matches the LPBF feature floor so a slicer treats the
    /// violation as a genuine collision rather than a borderline pass.
    /// </summary>
    public const double DefaultClearance_mm = 2.0;

    /// <summary>
    /// Number of strict-interior samples taken along each tube leg.
    /// Endpoints (touch the body surface legitimately) are skipped via
    /// the endpoint-touch whitelist. At N=8 the inter-sample spacing
    /// on a 200 mm leg is 25 mm — well below a typical body radius
    /// (≥ 25 mm) and the 2 mm clearance, so a tube that clips the edge
    /// of a body cannot slip through undetected.
    /// </summary>
    public const int InteriorSampleCount = 8;

    /// <summary>
    /// Evaluate body-intersection gates against a routed manifold
    /// layout + set of body envelopes. Returns a
    /// <see cref="FeasibilityGateResult"/> — <see cref="FeasibilityGateResult.IsFeasible"/>
    /// is true when no tube midpoint sits inside a non-endpoint body
    /// AND no pair of tubes interferes with each other.
    /// </summary>
    public static FeasibilityGateResult Evaluate(
        FeedManifoldLayout layout,
        MonolithicBodyEnvelopes envelopes,
        double clearance_mm = DefaultClearance_mm)
    {
        if (layout is null) throw new System.ArgumentNullException(nameof(layout));
        if (envelopes is null) throw new System.ArgumentNullException(nameof(envelopes));

        var violations = new System.Collections.Generic.List<FeasibilityViolation>();

        // Turbine envelopes live on the same shaft as the pumps.
        // World-frame z-base sits `totalLength` below the pump origin so
        // the shaft runs through both. See `TurbineGeometryGenerator`
        // header for the coordinate convention.
        Vector3 fuelTurbineOrigin = envelopes.FuelTurbineGeometry is { } ft
            ? envelopes.FuelPumpOrigin - new Vector3(0, 0, (float)ft.TotalLength_mm)
            : Vector3.Zero;
        Vector3 oxTurbineOrigin = envelopes.OxTurbineGeometry is { } ot
            ? envelopes.OxPumpOrigin - new Vector3(0, 0, (float)ot.TotalLength_mm)
            : Vector3.Zero;

        foreach (var tube in layout.Tubes)
        {
            // False-positive guard: a tube legitimately terminates on a
            // body's surface (e.g. fuel-discharge ends AT the injector
            // dome, which is the chamber's X=0 face). Skip intersection
            // checks for any body the tube's endpoints or corner touch —
            // those are expected contact points.
            bool touchesChamber = PointTouchesCylinderAlongX(tube.Start_mm, Vector3.Zero,
                envelopes.ChamberOuterRadius_mm, envelopes.ChamberLength_mm, tube.OuterRadius_mm)
                || PointTouchesCylinderAlongX(tube.End_mm, Vector3.Zero,
                    envelopes.ChamberOuterRadius_mm, envelopes.ChamberLength_mm, tube.OuterRadius_mm)
                || (tube.Corner_mm is { } cc && PointTouchesCylinderAlongX(cc, Vector3.Zero,
                    envelopes.ChamberOuterRadius_mm, envelopes.ChamberLength_mm, tube.OuterRadius_mm));

            bool touchesFuelPump = envelopes.FuelPumpGeometry is { } fpEp &&
                (PointTouchesCylinderAlongZ(tube.Start_mm, envelopes.FuelPumpOrigin,
                    fpEp.CasingOuterRadius_mm, fpEp.TotalLength_mm, tube.OuterRadius_mm)
                 || PointTouchesCylinderAlongZ(tube.End_mm, envelopes.FuelPumpOrigin,
                    fpEp.CasingOuterRadius_mm, fpEp.TotalLength_mm, tube.OuterRadius_mm));

            bool touchesOxPump = envelopes.OxPumpGeometry is { } opEp &&
                (PointTouchesCylinderAlongZ(tube.Start_mm, envelopes.OxPumpOrigin,
                    opEp.CasingOuterRadius_mm, opEp.TotalLength_mm, tube.OuterRadius_mm)
                 || PointTouchesCylinderAlongZ(tube.End_mm, envelopes.OxPumpOrigin,
                    opEp.CasingOuterRadius_mm, opEp.TotalLength_mm, tube.OuterRadius_mm));

            bool touchesPreburner = envelopes.PreburnerGeometry is { } preEp &&
                (PointTouchesCylinderAlongX(tube.Start_mm, envelopes.PreburnerOrigin,
                    preEp.OuterRadius_mm, preEp.TotalLength_mm, tube.OuterRadius_mm)
                 || PointTouchesCylinderAlongX(tube.End_mm, envelopes.PreburnerOrigin,
                    preEp.OuterRadius_mm, preEp.TotalLength_mm, tube.OuterRadius_mm));

            bool touchesFuelTurbine = envelopes.FuelTurbineGeometry is { } ftEp &&
                (PointTouchesCylinderAlongZ(tube.Start_mm, fuelTurbineOrigin,
                    ftEp.HousingOuterRadius_mm, ftEp.TotalLength_mm, tube.OuterRadius_mm)
                 || PointTouchesCylinderAlongZ(tube.End_mm, fuelTurbineOrigin,
                    ftEp.HousingOuterRadius_mm, ftEp.TotalLength_mm, tube.OuterRadius_mm));

            bool touchesOxTurbine = envelopes.OxTurbineGeometry is { } otEp &&
                (PointTouchesCylinderAlongZ(tube.Start_mm, oxTurbineOrigin,
                    otEp.HousingOuterRadius_mm, otEp.TotalLength_mm, tube.OuterRadius_mm)
                 || PointTouchesCylinderAlongZ(tube.End_mm, oxTurbineOrigin,
                    otEp.HousingOuterRadius_mm, otEp.TotalLength_mm, tube.OuterRadius_mm));

            // Aerospike plug touch check. Plug is axisymmetric along
            // +X in the envelope frame; the
            // touch test reuses the same ±tube.OuterRadius tolerance the
            // cylinder checks use, reading the plug radius at the
            // sampled x-station via the station interpolator.
            bool touchesPlug = envelopes.AerospikePlug is { } plEp &&
                (PointTouchesPlugEnvelope(tube.Start_mm, plEp, envelopes.AerospikePlugOrigin, tube.OuterRadius_mm)
                 || PointTouchesPlugEnvelope(tube.End_mm, plEp, envelopes.AerospikePlugOrigin, tube.OuterRadius_mm)
                 || (tube.Corner_mm is { } cc2 && PointTouchesPlugEnvelope(cc2, plEp, envelopes.AerospikePlugOrigin, tube.OuterRadius_mm)));

            // Dense interior sampling per leg. Straight tubes
            // (no corner) have one leg (Start → End); bent tubes have
            // two (Start → Corner, Corner → End). On each leg we take
            // `InteriorSampleCount` strict-interior points at t = k/(N+1)
            // for k = 1..N — endpoints skipped since they legitimately
            // touch the body whose intersection is whitelisted.
            // Per-body worst-case aggregation: at most one violation per
            // tube × body so we don't emit 16 clones of the same tube
            // cutting through the same chamber.
            Vector3[] legStarts;
            Vector3[] legEnds;
            if (tube.Corner_mm is { } corner)
            {
                legStarts = new[] { tube.Start_mm, corner };
                legEnds   = new[] { corner,        tube.End_mm };
            }
            else
            {
                legStarts = new[] { tube.Start_mm };
                legEnds   = new[] { tube.End_mm };
            }

            bool violatesChamber      = false;   Vector3 chamberSample      = default;
            bool violatesFuelPump     = false;   Vector3 fuelPumpSample     = default;
            bool violatesOxPump       = false;   Vector3 oxPumpSample       = default;
            bool violatesPreburner    = false;   Vector3 preburnerSample    = default;
            bool violatesFuelTurbine  = false;   Vector3 fuelTurbineSample  = default;
            bool violatesOxTurbine    = false;   Vector3 oxTurbineSample    = default;
            bool violatesPlug         = false;   Vector3 plugSample         = default;

            for (int leg = 0; leg < legStarts.Length; leg++)
            {
                Vector3 a = legStarts[leg];
                Vector3 b = legEnds[leg];
                for (int k = 1; k <= InteriorSampleCount; k++)
                {
                    float t = k / (float)(InteriorSampleCount + 1);
                    Vector3 s = a + t * (b - a);

                    if (!violatesChamber && !touchesChamber && IsInsideCylinderAlongX(s, Vector3.Zero,
                            envelopes.ChamberOuterRadius_mm,
                            envelopes.ChamberLength_mm, clearance_mm))
                    {
                        violatesChamber = true;
                        chamberSample = s;
                    }
                    if (!violatesFuelPump && !touchesFuelPump && envelopes.FuelPumpGeometry is { } fp &&
                        IsInsideCylinderAlongZ(s, envelopes.FuelPumpOrigin,
                            fp.CasingOuterRadius_mm, fp.TotalLength_mm, clearance_mm))
                    {
                        violatesFuelPump = true;
                        fuelPumpSample = s;
                    }
                    if (!violatesOxPump && !touchesOxPump && envelopes.OxPumpGeometry is { } op &&
                        IsInsideCylinderAlongZ(s, envelopes.OxPumpOrigin,
                            op.CasingOuterRadius_mm, op.TotalLength_mm, clearance_mm))
                    {
                        violatesOxPump = true;
                        oxPumpSample = s;
                    }
                    if (!violatesPreburner && !touchesPreburner && envelopes.PreburnerGeometry is { } pre &&
                        IsInsideCylinderAlongX(s, envelopes.PreburnerOrigin,
                            pre.OuterRadius_mm, pre.TotalLength_mm, clearance_mm))
                    {
                        violatesPreburner = true;
                        preburnerSample = s;
                    }
                    if (!violatesFuelTurbine && !touchesFuelTurbine && envelopes.FuelTurbineGeometry is { } ftb &&
                        IsInsideCylinderAlongZ(s, fuelTurbineOrigin,
                            ftb.HousingOuterRadius_mm, ftb.TotalLength_mm, clearance_mm))
                    {
                        violatesFuelTurbine = true;
                        fuelTurbineSample = s;
                    }
                    if (!violatesOxTurbine && !touchesOxTurbine && envelopes.OxTurbineGeometry is { } otb &&
                        IsInsideCylinderAlongZ(s, oxTurbineOrigin,
                            otb.HousingOuterRadius_mm, otb.TotalLength_mm, clearance_mm))
                    {
                        violatesOxTurbine = true;
                        oxTurbineSample = s;
                    }
                    if (!violatesPlug && !touchesPlug && envelopes.AerospikePlug is { } plg &&
                        IsInsidePlugEnvelope(s, plg, envelopes.AerospikePlugOrigin, clearance_mm))
                    {
                        violatesPlug = true;
                        plugSample = s;
                    }
                }
            }

            if (violatesChamber)      violations.Add(MakeViolation(tube, "chamber",      chamberSample));
            if (violatesFuelPump)     violations.Add(MakeViolation(tube, "fuel pump",    fuelPumpSample));
            if (violatesOxPump)       violations.Add(MakeViolation(tube, "ox pump",      oxPumpSample));
            if (violatesPreburner)    violations.Add(MakeViolation(tube, "preburner",    preburnerSample));
            if (violatesFuelTurbine)  violations.Add(MakeViolation(tube, "fuel turbine", fuelTurbineSample));
            if (violatesOxTurbine)    violations.Add(MakeViolation(tube, "ox turbine",   oxTurbineSample));
            if (violatesPlug)         violations.Add(MakeViolation(tube, "aerospike plug", plugSample));
        }

        // Tube-vs-tube pair sweep. O(N²) over unique pairs; N is
        // the routed-tube count (≤ 8 in practice, so ≤ 28 pair probes).
        // Shared-endpoint pairs are whitelisted (legitimate branch joint
        // at a discharge or an injector dome). Per-pair single-violation
        // aggregation mirrors the per-body-and-tube convention above.
        var tubeList = layout.Tubes is System.Collections.Generic.IList<FeedTube> il
            ? il
            : new System.Collections.Generic.List<FeedTube>(layout.Tubes);
        for (int i = 0; i < tubeList.Count; i++)
        {
            for (int j = i + 1; j < tubeList.Count; j++)
            {
                var a = tubeList[i];
                var b = tubeList[j];
                if (TubesShareEndpoint(a, b))
                    continue;   // legitimate branch joint

                double rSum = a.OuterRadius_mm + b.OuterRadius_mm;
                double threshold = rSum + clearance_mm;

                if (TryFindTubePairInterference(a, b, threshold,
                        out Vector3 sampleA, out Vector3 sampleB, out double gap_mm))
                {
                    violations.Add(new FeasibilityViolation(
                        ConstraintId: "MONOLITHIC_TUBE_INTERSECTION",
                        Description:
                            $"Tubes '{a.Label}' and '{b.Label}' clash at "
                          + $"({sampleA.X:F1}, {sampleA.Y:F1}, {sampleA.Z:F1}) mm ↔ "
                          + $"({sampleB.X:F1}, {sampleB.Y:F1}, {sampleB.Z:F1}) mm: "
                          + $"gap {gap_mm:F1} mm < r_a+r_b+clearance ({threshold:F1} mm). "
                          + $"Re-route one of the tubes or insert a dogleg.",
                        ActualValue: gap_mm,
                        Limit:       threshold));
                }
            }
        }

        return new FeasibilityGateResult(
            IsFeasible: violations.Count == 0,
            Violations: violations.ToArray());
    }

    /// <summary>
    /// True when any endpoint of <paramref name="a"/> (Start, Corner,
    /// End) sits within <see cref="DefaultClearance_mm"/> of any endpoint
    /// of <paramref name="b"/>. Shared endpoints model legitimate branch
    /// joints (e.g. two lines teeing into a common manifold node) and
    /// are whitelisted from the tube-vs-tube gate.
    /// </summary>
    private static bool TubesShareEndpoint(FeedTube a, FeedTube b)
    {
        var aPts = CollectEndpoints(a);
        var bPts = CollectEndpoints(b);
        double tol = DefaultClearance_mm;
        foreach (var pa in aPts)
            foreach (var pb in bPts)
                if (Vector3.Distance(pa, pb) <= tol)
                    return true;
        return false;
    }

    private static Vector3[] CollectEndpoints(FeedTube t)
    {
        return t.Corner_mm is { } c
            ? new[] { t.Start_mm, c, t.End_mm }
            : new[] { t.Start_mm, t.End_mm };
    }

    /// <summary>
    /// Scan every segment pair across the two tubes (1–2 legs each) and
    /// return the closest clash (gap &lt; <paramref name="threshold"/>).
    /// Returns true on the first pair below the threshold; the caller
    /// aggregates one violation per (A, B) pair so the O(N²) sweep
    /// never emits duplicates.
    /// </summary>
    private static bool TryFindTubePairInterference(
        FeedTube a, FeedTube b, double threshold,
        out Vector3 sampleA, out Vector3 sampleB, out double gap_mm)
    {
        sampleA = default;
        sampleB = default;
        gap_mm = double.PositiveInfinity;

        var aLegs = TubeLegs(a);
        var bLegs = TubeLegs(b);

        foreach (var (pa0, pa1) in aLegs)
        {
            foreach (var (pb0, pb1) in bLegs)
            {
                SegmentSegmentClosest(
                    pa0, pa1, pb0, pb1,
                    out Vector3 qa, out Vector3 qb, out double dist);
                if (dist < threshold && dist < gap_mm)
                {
                    sampleA = qa;
                    sampleB = qb;
                    gap_mm = dist;
                }
            }
        }
        return gap_mm < threshold;
    }

    private static System.Collections.Generic.IEnumerable<(Vector3, Vector3)> TubeLegs(FeedTube t)
    {
        if (t.Corner_mm is { } c)
        {
            yield return (t.Start_mm, c);
            yield return (c, t.End_mm);
        }
        else
        {
            yield return (t.Start_mm, t.End_mm);
        }
    }

    /// <summary>
    /// Closest-point on segment (p1→p2) to segment (p3→p4). Numerically
    /// robust variant of the Eberly / Goldman line-line closest-points
    /// algorithm with parallel-segment fallback. Returns the closest
    /// points on each segment plus their Euclidean distance.
    /// </summary>
    private static void SegmentSegmentClosest(
        Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4,
        out Vector3 closestOnA, out Vector3 closestOnB, out double distance)
    {
        Vector3 d1 = p2 - p1;
        Vector3 d2 = p4 - p3;
        Vector3 r  = p1 - p3;
        double a = Vector3.Dot(d1, d1);
        double e = Vector3.Dot(d2, d2);
        double f = Vector3.Dot(d2, r);

        double s, t;
        const double eps = 1e-9;

        if (a <= eps && e <= eps)
        {
            closestOnA = p1;
            closestOnB = p3;
            distance = (double)(closestOnA - closestOnB).Length();
            return;
        }
        if (a <= eps)
        {
            s = 0.0;
            t = System.Math.Clamp(f / e, 0.0, 1.0);
        }
        else
        {
            double c = Vector3.Dot(d1, r);
            if (e <= eps)
            {
                t = 0.0;
                s = System.Math.Clamp(-c / a, 0.0, 1.0);
            }
            else
            {
                double b = Vector3.Dot(d1, d2);
                double denom = a * e - b * b;
                s = denom > eps
                    ? System.Math.Clamp((b * f - c * e) / denom, 0.0, 1.0)
                    : 0.0;
                t = (b * s + f) / e;
                if (t < 0.0) { t = 0.0; s = System.Math.Clamp(-c / a, 0.0, 1.0); }
                else if (t > 1.0) { t = 1.0; s = System.Math.Clamp((b - c) / a, 0.0, 1.0); }
            }
        }

        closestOnA = p1 + (float)s * d1;
        closestOnB = p3 + (float)t * d2;
        distance = (double)(closestOnA - closestOnB).Length();
    }

    private static FeasibilityViolation MakeViolation(FeedTube tube, string body, Vector3 sample)
    {
        return new FeasibilityViolation(
            ConstraintId: "MONOLITHIC_BODY_INTERSECTION",
            Description:
                $"Tube '{tube.Label}' midpoint ({sample.X:F1}, {sample.Y:F1}, {sample.Z:F1}) "
              + $"mm passes through the {body} body. Re-route the tube around the body "
              + $"or add a dogleg via a second corner.",
            ActualValue:  0.0,   // semantic = "inside" → nominal 0 mm clearance
            Limit:        DefaultClearance_mm);
    }

    /// <summary>
    /// Tests whether <paramref name="p"/> is inside a cylinder whose
    /// axis runs along +X from <paramref name="origin"/> for
    /// <paramref name="length_mm"/>, with radius <paramref name="radius_mm"/>.
    /// <paramref name="clearance_mm"/> inflates the envelope so a
    /// sample just-outside still flags. Returns true when inside.
    /// </summary>
    private static bool IsInsideCylinderAlongX(
        Vector3 p, Vector3 origin, double radius_mm, double length_mm, double clearance_mm)
    {
        if (radius_mm <= 0 || length_mm <= 0) return false;
        double localX = p.X - origin.X;
        if (localX < -clearance_mm || localX > length_mm + clearance_mm) return false;
        double dy = p.Y - origin.Y;
        double dz = p.Z - origin.Z;
        double r = System.Math.Sqrt(dy * dy + dz * dz);
        return r < radius_mm + clearance_mm;
    }

    /// <summary>
    /// Same as <see cref="IsInsideCylinderAlongX"/> but for a cylinder
    /// whose axis runs along +Z (the turbopump shaft convention).
    /// </summary>
    private static bool IsInsideCylinderAlongZ(
        Vector3 p, Vector3 origin, double radius_mm, double length_mm, double clearance_mm)
    {
        if (radius_mm <= 0 || length_mm <= 0) return false;
        double localZ = p.Z - origin.Z;
        if (localZ < -clearance_mm || localZ > length_mm + clearance_mm) return false;
        double dx = p.X - origin.X;
        double dy = p.Y - origin.Y;
        double r = System.Math.Sqrt(dx * dx + dy * dy);
        return r < radius_mm + clearance_mm;
    }

    /// <summary>
    /// Tests whether <paramref name="p"/> sits on or inside the
    /// cylinder's ±<paramref name="touchMargin_mm"/> surface band. Used
    /// to detect tube endpoints that legitimately terminate on a body
    /// surface (via the tube's own outer radius as the margin).
    /// </summary>
    private static bool PointTouchesCylinderAlongX(
        Vector3 p, Vector3 origin, double radius_mm, double length_mm, double touchMargin_mm)
    {
        return IsInsideCylinderAlongX(p, origin, radius_mm, length_mm, touchMargin_mm);
    }

    /// <summary>Same as <see cref="PointTouchesCylinderAlongX"/> but for +Z-axis cylinders.</summary>
    private static bool PointTouchesCylinderAlongZ(
        Vector3 p, Vector3 origin, double radius_mm, double length_mm, double touchMargin_mm)
    {
        return IsInsideCylinderAlongZ(p, origin, radius_mm, length_mm, touchMargin_mm);
    }

    // ─────────────────────────────────────────────────────────────────
    //   Sprint 7 Track B (2026-04-22) — aerospike-plug envelope helpers
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Read the plug-body radius at axial station <paramref name="xLocal_mm"/>
    /// (measured from the throat plane at x = 0 downstream toward the
    /// plug base). Linearly interpolates between adjacent
    /// <see cref="AerospikeStation"/>s using each station's
    /// <see cref="AerospikeStation.R_inner_mm"/> (the plug-surface
    /// radius). Clamps to 0 below the throat plane and to the
    /// plug-base radius beyond the truncation.
    /// </summary>
    private static double PlugRadiusAt_mm(AerospikeBuildResult plug, double xLocal_mm)
    {
        var stations = plug.Contour.Stations;
        if (stations.Length == 0) return 0.0;
        if (xLocal_mm <= stations[0].X_mm) return stations[0].R_inner_mm;
        if (xLocal_mm >= stations[^1].X_mm) return stations[^1].R_inner_mm;

        // Linear search. N ≤ 80 in practice; cost is negligible next to
        // the outer loop cost (O(tubes × samples × bodies)).
        for (int i = 0; i < stations.Length - 1; i++)
        {
            double xa = stations[i].X_mm, xb = stations[i + 1].X_mm;
            if (xLocal_mm >= xa && xLocal_mm <= xb)
            {
                double ra = stations[i].R_inner_mm;
                double rb = stations[i + 1].R_inner_mm;
                double t = (xLocal_mm - xa) / System.Math.Max(xb - xa, 1e-12);
                return ra + t * (rb - ra);
            }
        }
        return 0.0;   // unreachable given clamps above, but safe fallback
    }

    /// <summary>
    /// Axisymmetric plug-envelope inclusion test. The plug axis runs
    /// along +X rooted at <paramref name="plugOrigin"/>. A point is
    /// "inside" when its local (x, r) falls within the
    /// <see cref="PlugRadiusAt_mm"/> envelope expanded by
    /// <paramref name="clearance_mm"/> radially — so a tube that grazes
    /// the plug surface by less than the clearance is flagged.
    /// Axial clearance applies at both ends (behind the throat plane
    /// or past the plug truncation the point is "outside" regardless).
    /// </summary>
    private static bool IsInsidePlugEnvelope(
        Vector3 p, AerospikeBuildResult plug, Vector3 plugOrigin, double clearance_mm)
    {
        float dx = p.X - plugOrigin.X;
        float dy = p.Y - plugOrigin.Y;
        float dz = p.Z - plugOrigin.Z;

        // Axial range: [0, plugTruncatedLength]. Axial clearance is the
        // same radial clearance mapped to the axis (cheap conservative).
        double xLocal = dx;
        if (xLocal < -clearance_mm) return false;
        if (xLocal > plug.PlugTruncatedLength_mm + clearance_mm) return false;

        double rLocal = System.Math.Sqrt(dy * dy + dz * dz);
        double rPlug = PlugRadiusAt_mm(plug, xLocal);

        return rLocal < rPlug + clearance_mm;
    }

    /// <summary>
    /// Endpoint-touch test: a tube terminus legitimately touching the
    /// plug surface gets whitelisted (same convention as the cylinder
    /// bodies). Uses the tube's own outer radius as the touch margin,
    /// matching <see cref="PointTouchesCylinderAlongX"/>.
    /// </summary>
    private static bool PointTouchesPlugEnvelope(
        Vector3 p, AerospikeBuildResult plug, Vector3 plugOrigin, double touchMargin_mm)
    {
        return IsInsidePlugEnvelope(p, plug, plugOrigin, touchMargin_mm);
    }
}
