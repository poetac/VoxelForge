// FeedManifoldRouter.cs — Feed-line routing from tank interfaces →
// turbopump inlet → injector manifold, producing voxel-fusable tube
// geometry for the monolithic engine assembly.
//
// What this is
// ────────────
// The final piece of the "monolithic printable engine" promise.
// Earlier versions of the codebase produced a chamber + a turbopump +
// a preburner as separate bodies. This router emits the tube network
// that connects them — tank interface to pump inlet, pump discharge
// to injector dome, preburner exhaust to main chamber — so the final
// STL reads as one functionally-integrated part rather than a bag of
// components.
//
// Scope
// ─────
//   • Straight-line + single-bend cylindrical tubes. No spline
//     routing, no bundled harnesses, no flange / quick-disconnect
//     detail — only the outer hydraulic envelope.
//   • Two tube families per cycle:
//     - Feed lines (tank → pump inlet, one per propellant)
//     - Discharge lines (pump discharge → injector dome)
//   • StagedCombustion / FullFlow cycles also get a preburner
//     exhaust duct (preburner → injector / main chamber).
//
// What is NOT modelled
// ────────────────────
//   • Tank geometry itself (hooks into a "tank interface" face only).
//   • Valves, filters, umbilicals (already sized in the pressure-stackup;
//     voxel geometry is out-of-scope).
//   • Bend fidelity — we emit axis-aligned 90° elbows, not smooth
//     curvature.
//   • Wall thickness optimisation — uniform 2 mm default.
//   • CFD-quality pressure-drop assertion on the routed geometry
//     (`PressureStackup` still owns the ΔP accounting; the geometry is
//     a visual + print-ready output, not a new ΔP source).
//
// All dimensions in millimetres.

using System.Numerics;
using PicoGK;
using Voxelforge.Geometry;

namespace Voxelforge.FeedSystem;

/// <summary>
/// A single routed tube segment — cylindrical outer envelope. Inside
/// the envelope the SDF is negative. Terminates cleanly at both
/// endpoints (flat caps).
/// </summary>
public sealed class FeedTubeImplicit : IImplicit
{
    private readonly Vector3 _p0;         // start
    private readonly Vector3 _p1;         // end
    private readonly Vector3 _axis;       // normalised
    private readonly float _length;
    private readonly float _outerR;

    public FeedTubeImplicit(Vector3 p0, Vector3 p1, float outerRadius_mm)
    {
        if (outerRadius_mm <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(outerRadius_mm),
                "tube radius must be positive");
        _p0 = p0;
        _p1 = p1;
        var delta = p1 - p0;
        _length = delta.Length();
        if (_length < 1e-4f)
            throw new System.ArgumentException("tube endpoints are coincident");
        _axis = delta / _length;
        _outerR = outerRadius_mm;
    }

    public float fSignedDistance(in Vector3 p)
    {
        var d = p - _p0;
        float axial = Vector3.Dot(d, _axis);
        var radialVec = d - _axis * axial;
        float radial = radialVec.Length();

        float axialDist = axial < 0 ? -axial
                      : axial > _length ? axial - _length
                      : 0;
        float radialDist = radial - _outerR;

        if (axialDist > 0 && radialDist <= 0)
            return axialDist;          // past a cap, still inside radial envelope
        if (axialDist == 0 && radialDist <= 0)
            return radialDist;          // inside — return radial distance (negative)
        // Outside both — euclidean distance to nearest point on cap ring.
        return MathF.Sqrt(axialDist * axialDist + MathF.Max(radialDist, 0) * MathF.Max(radialDist, 0));
    }
}

/// <summary>
/// Bent tube — two segments meeting at a corner. Supports both a
/// simple union (mitred corner) and an optional toroidal fillet at the
/// junction to remove the small "crease" the mitred union leaves
/// behind and improve LPBF printability at the corner. The fillet
/// degenerates to the mitred behaviour when the requested radius is
/// zero or negative.
///
/// Geometry: a torus sits with its centre on the bisector of the two
/// tube axes, its axis perpendicular to the bend plane, and its tube-
/// radius matching the tube's outer radius. The torus is clipped so
/// only the bend-inside arc contributes to the SDF — the opposite
/// side of the torus is ignored. This avoids overlap with the
/// straight portions of the two segments.
/// </summary>
public sealed class FeedBendImplicit : IImplicit
{
    private readonly FeedTubeImplicit _seg1;
    private readonly FeedTubeImplicit _seg2;
    private readonly bool _hasFillet;
    private readonly Vector3 _filletCentre;
    private readonly Vector3 _filletAxis;     // normal to bend plane
    private readonly float _filletMajorR;     // bend radius (distance from torus centre to tube axis)
    private readonly float _filletMinorR;     // tube outer radius
    private readonly Vector3 _filletToStart;  // unit vector from corner toward start along seg1
    private readonly Vector3 _filletToEnd;    // unit vector from corner toward end along seg2

    /// <summary>
    /// Mitred-only constructor — produces a mitred union of two
    /// straight segments. No fillet.
    /// </summary>
    public FeedBendImplicit(Vector3 start, Vector3 corner, Vector3 end, float outerRadius_mm)
        : this(start, corner, end, outerRadius_mm, filletRadius_mm: 0f) { }

    /// <summary>
    /// Fillet-aware constructor — optionally blends a toroidal fillet
    /// into the corner. A non-positive
    /// <paramref name="filletRadius_mm"/> disables the fillet and
    /// reproduces the mitred behaviour bit-identically.
    /// </summary>
    public FeedBendImplicit(Vector3 start, Vector3 corner, Vector3 end,
        float outerRadius_mm, float filletRadius_mm)
    {
        _seg1 = new FeedTubeImplicit(start, corner, outerRadius_mm);
        _seg2 = new FeedTubeImplicit(corner, end, outerRadius_mm);

        // Build the fillet only when requested AND the two segments are
        // non-collinear (a straight-through "bend" has no corner to
        // fillet). Fall back to pure-union in the degenerate cases.
        Vector3 u = start - corner;
        Vector3 v = end - corner;
        float uLen = u.Length();
        float vLen = v.Length();
        if (filletRadius_mm <= 0 || uLen < 1e-4f || vLen < 1e-4f)
        {
            _hasFillet = false;
            return;
        }
        Vector3 uHat = u / uLen;
        Vector3 vHat = v / vLen;
        float cosAngle = Vector3.Dot(uHat, vHat);
        // Collinear (straight-through) or near-collinear: no fillet.
        if (cosAngle > 0.999f || cosAngle < -0.999f)
        {
            _hasFillet = false;
            return;
        }
        // Bisector direction (points into the bend's interior half-space).
        Vector3 bisector = Vector3.Normalize(uHat + vHat);
        // Half-angle between the two tube directions.
        float halfAngle = MathF.Acos(MathF.Max(-1f, MathF.Min(1f, cosAngle))) * 0.5f;
        if (halfAngle < 1e-3f) { _hasFillet = false; return; }
        // Torus centre distance from corner along the bisector so the
        // torus axis of revolution sits where a tube axis — offset by
        // filletRadius — intersects the bisector. For a bend turning by
        // angle (π − 2·halfAngle), the centre sits at
        //   d = fillet / sin(halfAngle)
        // from the corner along the bisector.
        float dCentre = filletRadius_mm / MathF.Sin(halfAngle);
        _filletCentre = corner + bisector * dCentre;

        // Torus axis: perpendicular to the bend plane.
        Vector3 axis = Vector3.Cross(uHat, vHat);
        float axisLen = axis.Length();
        if (axisLen < 1e-4f) { _hasFillet = false; return; }
        _filletAxis = axis / axisLen;

        _filletMajorR = filletRadius_mm;
        _filletMinorR = outerRadius_mm;
        _filletToStart = uHat;
        _filletToEnd = vHat;
        _hasFillet = true;
    }

    public float fSignedDistance(in Vector3 p)
    {
        float dSegs = MathF.Min(_seg1.fSignedDistance(p), _seg2.fSignedDistance(p));
        if (!_hasFillet) return dSegs;

        // Point relative to the torus centre.
        Vector3 q = p - _filletCentre;
        // Axial component along the torus axis.
        float axial = Vector3.Dot(q, _filletAxis);
        // Planar component (lies in the bend plane).
        Vector3 planar = q - _filletAxis * axial;
        float planarLen = planar.Length();
        // Distance from the torus tube's centre line:
        //   dTorus = sqrt((planarLen − major)² + axial²) − minor
        float dr = planarLen - _filletMajorR;
        float dTorus = MathF.Sqrt(dr * dr + axial * axial) - _filletMinorR;

        // Clip: only keep the arc on the inside of the bend. A
        // torus-quadrant mask rejects the far half of the torus so it
        // doesn't reintroduce material outside the bend.
        if (planarLen > 1e-4f)
        {
            Vector3 radialHat = planar / planarLen;
            // The torus lies in the bend plane; the arc that matters is
            // the one whose outward radial direction is opposite the
            // bisector (toward the corner). Points whose radial falls on
            // the other side of the bisector are the "far half" — mask
            // those out so the torus never adds material past the two
            // tube endpoints.
            float dot = Vector3.Dot(radialHat, _filletToStart) + Vector3.Dot(radialHat, _filletToEnd);
            if (dot > 0) return dSegs;    // far half of torus — use segment union only
        }
        // Union of segments and the filletted corner.
        return MathF.Min(dSegs, dTorus);
    }
}

/// <summary>
/// One routed tube descriptor. The generator emits a list of these
/// per cycle; the monolithic engine builder unions them into the
/// engine body. <see cref="FilletRadius_mm"/> controls the
/// toroidal-bend fillet — 0 disables it (matches mitred behaviour
/// bit-identically). Only meaningful when <see cref="Corner_mm"/> is
/// set.
/// </summary>
public sealed record FeedTube(
    string   Label,                 // "fuel-feed", "ox-discharge", etc.
    Vector3  Start_mm,
    Vector3? Corner_mm,             // null → straight; non-null → bent
    Vector3  End_mm,
    double   OuterRadius_mm,
    double   FilletRadius_mm = 0.0);

/// <summary>
/// Output of <see cref="FeedManifoldRouter.Route"/>: a list of
/// <see cref="FeedTube"/> descriptors covering the full feed-system
/// tube network, plus a summary.
/// </summary>
public sealed record FeedManifoldLayout(
    EngineCycle Cycle,
    System.Collections.Generic.IReadOnlyList<FeedTube> Tubes,
    double TotalTubeLength_mm,
    double EstimatedTubeMass_g,
    string Notes);

/// <summary>
/// Pure-math feed-manifold router. Deterministic; thread-safe; no
/// PicoGK dependency in the routing path. The implicits are only
/// built on demand via <see cref="BuildImplicits"/>.
/// </summary>
public static class FeedManifoldRouter
{
    /// <summary>Default tube outer radius (mm). 8 mm OD for 20 kN class.</summary>
    public const double DefaultTubeOuterRadius_mm = 8.0;

    /// <summary>GRCop-42 density for analytical mass (g/cm³).</summary>
    public const double TubeMaterialDensity_gcm3 = 8.9;

    /// <summary>Default tube wall thickness (mm).</summary>
    public const double DefaultTubeWallThickness_mm = 2.0;

    /// <summary>Default toroidal fillet radius (mm). 0 = mitred behaviour; 8 mm matches a 1×OD bend radius.</summary>
    public const double DefaultBendFilletRadius_mm = 8.0;

    /// <summary>
    /// Route the feed-system tube network for the given engine.
    ///
    /// Layout assumption (Phase 1): the chamber's injector dome is at
    /// the origin (0, 0, 0) with centerline along +X (same convention
    /// as <see cref="Chamber.ChamberContour"/>); the turbopump is
    /// mounted in the −Y half-space to the side of the chamber, with
    /// its shaft along +Z (ordinary rocket-engine plumbing). Tank
    /// interfaces are stubbed as symbolic points at fixed distances.
    /// </summary>
    public static FeedManifoldLayout Route(
        EngineCycle cycle,
        Vector3 injectorDomeCenter,
        Vector3 turbopumpFuelInlet,
        Vector3 turbopumpFuelDischarge,
        Vector3 turbopumpOxInlet,
        Vector3 turbopumpOxDischarge,
        Vector3? preburnerExhaust = null,
        double tubeOuterRadius_mm = DefaultTubeOuterRadius_mm,
        double bendFilletRadius_mm = 0.0)
    {
        var tubes = new System.Collections.Generic.List<FeedTube>();
        double totalLen = 0;

        // Tank-interface stubs: place them 200 mm upstream of the pump
        // inlets along −Z (toward the vehicle tanks). Phase-2 work
        // could wire this to actual vehicle tank positions.
        var fuelTankInterface = turbopumpFuelInlet + new Vector3(0, 0, -200);
        var oxTankInterface   = turbopumpOxInlet   + new Vector3(0, 0, -200);

        if (cycle != EngineCycle.PressureFed)
        {
            // Feed lines: tank → pump inlet (straight).
            AddStraight(tubes, ref totalLen, "fuel-feed", fuelTankInterface,
                turbopumpFuelInlet, tubeOuterRadius_mm);
            AddStraight(tubes, ref totalLen, "ox-feed", oxTankInterface,
                turbopumpOxInlet, tubeOuterRadius_mm);

            // Discharge lines: pump discharge → injector dome (with a
            // 90° bend so the tube routes back into the chamber axis).
            var fuelCorner = new Vector3(
                turbopumpFuelDischarge.X,
                injectorDomeCenter.Y,
                turbopumpFuelDischarge.Z);
            AddBent(tubes, ref totalLen, "fuel-discharge",
                turbopumpFuelDischarge, fuelCorner, injectorDomeCenter,
                tubeOuterRadius_mm, bendFilletRadius_mm);

            var oxCorner = new Vector3(
                turbopumpOxDischarge.X,
                injectorDomeCenter.Y,
                turbopumpOxDischarge.Z);
            AddBent(tubes, ref totalLen, "ox-discharge",
                turbopumpOxDischarge, oxCorner, injectorDomeCenter,
                tubeOuterRadius_mm, bendFilletRadius_mm);
        }
        else
        {
            // Pressure-fed: tubes go straight from tank to injector
            // dome (no pump). Only one fuel + one ox tube total.
            AddStraight(tubes, ref totalLen, "fuel-feed", fuelTankInterface,
                injectorDomeCenter, tubeOuterRadius_mm);
            AddStraight(tubes, ref totalLen, "ox-feed", oxTankInterface,
                injectorDomeCenter, tubeOuterRadius_mm);
        }

        // Preburner exhaust duct (staged / FFSC / GG).
        if (preburnerExhaust is { } exh
            && cycle is EngineCycle.GasGenerator
                     or EngineCycle.StagedCombustion
                     or EngineCycle.FullFlow)
        {
            // GasGenerator routes preburner exhaust overboard (stub
            // points to +Z); staged / FFSC route it back into the
            // injector dome.
            if (cycle == EngineCycle.GasGenerator)
            {
                var overboard = exh + new Vector3(0, 0, 300);
                AddStraight(tubes, ref totalLen, "preburner-exhaust-overboard",
                    exh, overboard, tubeOuterRadius_mm * 1.4);   // hotter gas → fatter duct
            }
            else
            {
                var corner = new Vector3(exh.X, injectorDomeCenter.Y, exh.Z);
                AddBent(tubes, ref totalLen, "preburner-exhaust-to-main",
                    exh, corner, injectorDomeCenter, tubeOuterRadius_mm * 1.4,
                    bendFilletRadius_mm);
            }
        }

        // Mass estimate: annular tube (outer − inner) × total length.
        double innerR = tubeOuterRadius_mm - DefaultTubeWallThickness_mm;
        double tubeCrossSection_mm2 = System.Math.PI
            * (tubeOuterRadius_mm * tubeOuterRadius_mm - innerR * innerR);
        double totalVol_mm3 = tubeCrossSection_mm2 * totalLen;
        double mass_g = totalVol_mm3 * 1e-3 * TubeMaterialDensity_gcm3;

        string notes = $"Routed {tubes.Count} tubes for {cycle} cycle — "
                     + $"total length {totalLen:F0} mm, estimated "
                     + $"mass {mass_g:F0} g at 2 mm wall thickness.";

        return new FeedManifoldLayout(
            Cycle:              cycle,
            Tubes:              tubes.AsReadOnly(),
            TotalTubeLength_mm: totalLen,
            EstimatedTubeMass_g: mass_g,
            Notes:              notes);
    }

    private static void AddStraight(
        System.Collections.Generic.List<FeedTube> tubes,
        ref double totalLen,
        string label, Vector3 start, Vector3 end, double r_mm)
    {
        double len = (end - start).Length();
        totalLen += len;
        tubes.Add(new FeedTube(label, start, null, end, r_mm));
    }

    private static void AddBent(
        System.Collections.Generic.List<FeedTube> tubes,
        ref double totalLen,
        string label, Vector3 start, Vector3 corner, Vector3 end, double r_mm,
        double fillet_mm = 0.0)
    {
        double len = (corner - start).Length() + (end - corner).Length();
        totalLen += len;
        tubes.Add(new FeedTube(label, start, corner, end, r_mm, fillet_mm));
    }

    /// <summary>
    /// Task-thread-only: build <see cref="IImplicit"/>s for each
    /// routed tube. Consumers can either voxelise them individually
    /// or union them with the chamber / pump bodies and voxelise
    /// once.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyList<IImplicit> BuildImplicits(
        FeedManifoldLayout layout)
    {
        var implicits = new System.Collections.Generic.List<IImplicit>(layout.Tubes.Count);
        foreach (var t in layout.Tubes)
        {
            if (t.Corner_mm is { } corner)
                implicits.Add(new FeedBendImplicit(
                    t.Start_mm, corner, t.End_mm,
                    (float)t.OuterRadius_mm, (float)t.FilletRadius_mm));
            else
                implicits.Add(new FeedTubeImplicit(t.Start_mm, t.End_mm, (float)t.OuterRadius_mm));
        }
        return implicits;
    }
}
