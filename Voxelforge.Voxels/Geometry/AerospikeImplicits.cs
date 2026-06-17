// AerospikeImplicits.cs — Implicit SDF primitives for the aerospike /
// plug-nozzle geometry
// pipeline. Composable with the existing ChamberImplicits primitives
// through PicoGK's standard IImplicit interface.
//
// Three primitives
// ────────────────
//   • RevolvedPlugImplicit — body of revolution bounded on the OUTSIDE
//     by a piecewise-linear plug profile r(x) and on the INSIDE by the
//     chamber axis. Inside the plug body the SDF is negative; outside
//     positive. Handles truncated plugs (flat base) cleanly.
//   • AnnularThroatImplicit — short axial cylinder between an inner
//     and outer radius, stamped at the throat plane. Used as the mass
//     source for the combustion chamber aft-end that feeds the
//     annular throat.
//   • AerospikeAssemblyImplicit — composite SDF that unions the
//     (combustion chamber + annular throat) with the plug to produce
//     the full aerospike engine shell in a single SDF. Consumers
//     voxelise once.
//
// Distance quality
// ────────────────
// Same convention as the existing ChamberImplicits primitives
// (RevolvedContourImplicit, AxialChannelImplicit): inside the solid
// phase the SDF is negative; outside the solid phase the SDF is the
// true Euclidean distance to the nearest surface within ±0.5 mm of
// the surface, and a conservative larger-magnitude estimate elsewhere.
// PicoGK's marching-cubes meshing picks up the surface from the sign
// crossing; magnitude quality only matters within ±voxel of the
// surface.

using System.Numerics;
using PicoGK;
using Voxelforge.Chamber;

namespace Voxelforge.Geometry;

/// <summary>
/// Body of revolution along +X whose outer radius r(x) is given by
/// the supplied <see cref="AerospikeContour"/>. Inside the plug body
/// the SDF is negative. Plug is truncated at
/// <see cref="AerospikeContour.PlugTruncatedLength_mm"/> with a flat
/// base (orthogonal cut). Plug axial extent is
/// [<c>offsetX_mm</c>, <c>offsetX_mm + PlugTruncatedLength_mm</c>].
/// </summary>
public sealed class RevolvedPlugImplicit : IImplicit
{
    private readonly float[] _x;      // axial stations along plug
    private readonly float[] _r;      // plug radius at each station
    private readonly float _xMin;
    private readonly float _xMax;
    private readonly float _baseR;    // plug radius at truncation

    public RevolvedPlugImplicit(AerospikeContour contour, float offsetX_mm = 0f)
    {
        int n = contour.Stations.Length;
        _x = new float[n];
        _r = new float[n];
        for (int i = 0; i < n; i++)
        {
            _x[i] = (float)contour.Stations[i].X_mm + offsetX_mm;
            _r[i] = (float)contour.Stations[i].R_inner_mm;
        }
        _xMin = _x[0];
        _xMax = _x[^1];
        _baseR = _r[^1];
    }

    public float fSignedDistance(in Vector3 p)
    {
        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);

        // Outside the plug's axial extent — report the Euclidean
        // distance to the nearest end cap. Downstream axial from the
        // truncation: we treat it as outside (positive) so a "behind
        // the plug" point correctly reads as "outside the solid".
        if (p.X < _xMin)
        {
            // Distance to the throat-tip disc (cap at _xMin).
            float dx = _xMin - p.X;
            float rCap = _r[0];
            if (r <= rCap) return dx;                           // on-axis
            float dr = r - rCap;
            return MathF.Sqrt(dx * dx + dr * dr);
        }
        if (p.X > _xMax)
        {
            float dx = p.X - _xMax;
            if (r <= _baseR) return dx;                         // behind truncation, on-axis
            float dr = r - _baseR;
            return MathF.Sqrt(dx * dx + dr * dr);
        }

        // Inside axial extent — linear-interpolate plug radius r(x).
        float rLocal = InterpR(p.X);
        if (r <= rLocal)
        {
            // Inside the plug body. Distance-to-surface is the minimum
            // of (rLocal − r) and distance to the truncation cap.
            float dRadial = rLocal - r;
            float dAxialToTrunc = _xMax - p.X;
            return -MathF.Min(dRadial, dAxialToTrunc);
        }
        // Outside the plug body — Euclidean distance to the surface of
        // revolution. For a gently-tapered plug r − rLocal is a tight
        // approximation.
        return r - rLocal;
    }

    private float InterpR(float x)
    {
        // Plug x-stations are strictly ascending; binary search is
        // overkill for ≲ 100 stations — linear is fine + cache-friendly.
        for (int i = 0; i < _x.Length - 1; i++)
        {
            if (x >= _x[i] && x <= _x[i + 1])
            {
                float t = (x - _x[i]) / MathF.Max(_x[i + 1] - _x[i], 1e-6f);
                return _r[i] + t * (_r[i + 1] - _r[i]);
            }
        }
        // Out-of-range shouldn't happen because the caller gated on
        // [_xMin, _xMax], but return the end-station radius defensively.
        return x < _xMin ? _r[0] : _baseR;
    }
}

/// <summary>
/// Annular throat body — a short axial cylinder between rInner and
/// rOuter over [xMin, xMax]. Used to stamp the outer throat lip
/// around the plug nose. Inside the annulus the SDF is negative.
/// </summary>
public sealed class AnnularThroatImplicit : IImplicit
{
    private readonly float _xMin, _xMax, _rInner, _rOuter;

    public AnnularThroatImplicit(
        float xMin_mm, float xMax_mm, float rInner_mm, float rOuter_mm)
    {
        if (rOuter_mm <= rInner_mm)
            throw new System.ArgumentException(
                $"rOuter ({rOuter_mm}) must exceed rInner ({rInner_mm})");
        _xMin = xMin_mm;
        _xMax = xMax_mm;
        _rInner = rInner_mm;
        _rOuter = rOuter_mm;
    }

    public float fSignedDistance(in Vector3 p)
    {
        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        float dxOut = MathF.Max(_xMin - p.X, p.X - _xMax);
        float drInnerOut = _rInner - r;   // + when inside the plug hole
        float drOuterOut = r - _rOuter;   // + when outside outer cylinder
        float drOut = MathF.Max(drInnerOut, drOuterOut);

        float outsideX = MathF.Max(dxOut, 0);
        float outsideR = MathF.Max(drOut, 0);
        float outside = MathF.Sqrt(outsideX * outsideX + outsideR * outsideR);
        float inside = MathF.Max(dxOut, drOut);
        return outside > 0 ? outside : inside;
    }
}

/// <summary>
/// N radial spider struts at the throat plane that bridge the annular
/// throat — connecting the plug to the throat plate. Without these the
/// plug body is a free-floating cone with no structural connection to
/// the chamber; in a real aerospike the plug is held in place by either
/// a centerbody (axial pillar through the chamber) or N spider struts
/// at the throat. We use struts because they preserve the open-chamber
/// topology and only block a few percent of throat area.
///
/// Each strut is a thin radial wall with:
///   • radial extent  r ∈ [rInner, rOuter]
///   • angular extent narrow azimuthal slot of half-width δθ
///     (≈ thickness_mm / meanRadius_mm)
///   • axial extent   x ∈ [xMin, xMax] (typically the throat-plate band)
/// </summary>
public sealed class SpiderStrutsImplicit : IImplicit
{
    private readonly float _xMin, _xMax, _rInner, _rOuter;
    private readonly float _thickness_mm;
    private readonly int _count;
    private readonly float _phaseRad;

    public SpiderStrutsImplicit(
        float xMin_mm, float xMax_mm,
        float rInner_mm, float rOuter_mm,
        float thickness_mm,
        int count,
        float phaseRad = 0f)
    {
        if (rOuter_mm <= rInner_mm)
            throw new System.ArgumentException(
                $"rOuter ({rOuter_mm}) must exceed rInner ({rInner_mm})");
        if (count < 1)
            throw new System.ArgumentException("strut count must be ≥ 1");
        _xMin = xMin_mm;
        _xMax = xMax_mm;
        _rInner = rInner_mm;
        _rOuter = rOuter_mm;
        _thickness_mm = thickness_mm;
        _count = count;
        _phaseRad = phaseRad;
    }

    public float fSignedDistance(in Vector3 p)
    {
        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        float dxOut = MathF.Max(_xMin - p.X, p.X - _xMax);
        float drInnerOut = _rInner - r;
        float drOuterOut = r - _rOuter;
        float drOut = MathF.Max(drInnerOut, drOuterOut);

        // Azimuthal distance to nearest strut centre. Each strut is a
        // radial slab perpendicular to a chord at angle θ_k = 2π·k/N.
        // In Cartesian: distance to the radial line at θ_k equals
        // |r · sin(θ - θ_k)| where (θ - θ_k) is the angular offset to
        // the nearest strut. We compute that offset directly.
        float theta = MathF.Atan2(p.Z, p.Y);
        float spacing = 2f * MathF.PI / _count;
        // wrap (theta - phase) to [-spacing/2, +spacing/2]
        float local = theta - _phaseRad;
        local -= MathF.Round(local / spacing) * spacing;
        float dTangent = MathF.Abs(r * MathF.Sin(local));
        float dAzimOut = dTangent - _thickness_mm * 0.5f;

        // Combine: the strut is solid when inside all three constraints
        // (axial, radial, azimuthal). SDF outside is Euclidean distance
        // to the slab.
        float outsideX = MathF.Max(dxOut, 0);
        float outsideR = MathF.Max(drOut, 0);
        float outsideA = MathF.Max(dAzimOut, 0);
        float outside = MathF.Sqrt(
            outsideX * outsideX + outsideR * outsideR + outsideA * outsideA);
        float inside = MathF.Max(dxOut, MathF.Max(drOut, dAzimOut));
        return outside > 0 ? outside : inside;
    }
}

/// <summary>
/// Composite aerospike-engine shell: combustion chamber + annular throat
/// ring + plug body + optional spider struts. One SDF the voxel builder
/// consumes for the full external surface.
///
/// Sprint fix (2026-04-25): the chamber is now a HOLLOW SHELL via
/// outer-minus-inner subtraction. Pre-fix, the chamber SDF returned the
/// outer-revolved-contour distance only — i.e. a SOLID body of revolution
/// from r=0 to r=R_outer(x). That made the rendered chamber a smooth
/// solid dome, not a printable thin-walled combustion chamber. The fix
/// adds a `chamberInner` contour offset radially inward by the wall
/// thickness; the chamber SDF is now max(d_outer, -d_inner) = shell
/// region only. The result composes into the union with throat ring +
/// plug to form a printable hollow-chamber + plug body.
///
/// Sprint fix (2026-04-25 layer 8): added optional spider struts to
/// physically bond the plug to the throat plate. Without them an
/// STL-connectivity audit found 3 disjoint components (chamber+plate,
/// plug body, plug tip cap); with 4 struts the geometry is one piece.
///
/// No internal cooling channels — those are a follow-on (plug-channel
/// SDFs in AerospikePlugChannel).
/// </summary>
public sealed class AerospikeAssemblyImplicit : IImplicit
{
    private readonly RevolvedContourImplicit _chamberOuter;   // upstream combustion chamber outer wall
    private readonly RevolvedContourImplicit? _chamberInner;  // inner cavity (optional; null = legacy solid behaviour)
    private readonly AnnularThroatImplicit _throatRing;
    private readonly RevolvedPlugImplicit _plug;
    private readonly SpiderStrutsImplicit? _spider;
    private readonly float _chamberXMin, _chamberXMax;

    public AerospikeAssemblyImplicit(
        RevolvedContourImplicit chamberOuter,
        float chamberXMin_mm, float chamberXMax_mm,
        AnnularThroatImplicit throatRing,
        RevolvedPlugImplicit plug,
        RevolvedContourImplicit? chamberInner = null,
        SpiderStrutsImplicit? spider = null)
    {
        _chamberOuter = chamberOuter;
        _chamberInner = chamberInner;
        _chamberXMin = chamberXMin_mm;
        _chamberXMax = chamberXMax_mm;
        _throatRing = throatRing;
        _plug = plug;
        _spider = spider;
    }

    public float fSignedDistance(in Vector3 p)
    {
        // Union = min of each sub-SDF. Inside any one body → negative;
        // outside all three → minimum positive distance.
        float dChamber = ChamberSdf(p);
        float dThroat = _throatRing.fSignedDistance(p);
        float dPlug = _plug.fSignedDistance(p);
        float dMin = MathF.Min(dChamber, MathF.Min(dThroat, dPlug));
        if (_spider is { } s) dMin = MathF.Min(dMin, s.fSignedDistance(p));
        return dMin;
    }

    private float ChamberSdf(in Vector3 p)
    {
        // Chamber is a body of revolution clipped to [_chamberXMin, _chamberXMax].
        if (p.X < _chamberXMin || p.X > _chamberXMax)
        {
            float dx = System.Math.Max(_chamberXMin - p.X, p.X - _chamberXMax);
            return dx;
        }
        float dOuter = _chamberOuter.fSignedDistance(p);
        if (_chamberInner is null) return dOuter;   // legacy solid path

        // Hollow shell: inside outer (dOuter < 0) AND outside inner (dInner > 0).
        // SDF for shell = max(dOuter, -dInner) — negative only when both
        // conditions hold; positive otherwise. The throat plane (x = 0) is
        // open by construction: chamberOuter ends at x=0 just above the
        // throat ring, so the shell terminates there and the AnnularThroat
        // + plug provide the throat geometry below.
        float dInner = _chamberInner.fSignedDistance(p);
        return MathF.Max(dOuter, -dInner);
    }
}
