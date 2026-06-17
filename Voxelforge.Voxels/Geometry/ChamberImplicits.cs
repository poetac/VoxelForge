// ChamberImplicits.cs — Signed-distance implicit geometry primitives for
// the regen chamber. Standalone — does not share any code with the
// heat-exchanger project.
//
// The chamber is built from a handful of IImplicit shapes:
//
//   • RevolvedContourImplicit — body of revolution around the X axis,
//     radius given by a piecewise-linear function R(x). Used for every
//     cylindrically-symmetric body (inner gas wall, outer jacket).
//   • AxialChannelImplicit    — a curved rectangular channel that hugs
//     the outer face of the inner wall, at a given angular position θ_k.
//   • BoxImplicit             — axis-aligned box. Used for bounding / caps.
//   • CylinderImplicit        — finite cylinder. Used for ports.
//
// All distances are signed (negative inside, positive outside).
// Point coordinates are in millimetres — PicoGK is configured with a
// millimetre voxel grid.
//
// The X axis is the chamber centerline (injector at x = 0, exit at x = L).

using System.Numerics;
using PicoGK;

namespace Voxelforge.Geometry;

/// <summary>
/// Body of revolution around the X axis, specified by a sorted array of
/// (x, R(x)) control points. Inside the contour, SDF is negative
/// (approximate — signed by radial distance from R(x) at the point's x).
/// Outside the x extent returns a large positive distance.
/// </summary>
public sealed class RevolvedContourImplicit : IImplicit
{
    private readonly float[] _x;
    private readonly float[] _r;

    public RevolvedContourImplicit(IEnumerable<(double x_mm, double r_mm)> contourPoints)
    {
        var pts = contourPoints.OrderBy(p => p.x_mm).ToArray();
        if (pts.Length < 2) throw new ArgumentException("Need at least two contour points");
        _x = new float[pts.Length];
        _r = new float[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            _x[i] = (float)pts[i].x_mm;
            _r[i] = (float)pts[i].r_mm;
        }
    }

    public float fSignedDistance(in Vector3 p)
    {
        float x = p.X;
        if (x < _x[0] || x > _x[^1])
        {
            float axialGap = x < _x[0] ? _x[0] - x : x - _x[^1];
            return axialGap;     // outside axially
        }
        float R = InterpR(x);
        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        return r - R;            // inside if r < R
    }

    private float InterpR(float x)
    {
        // Binary search
        int lo = 0, hi = _x.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (_x[mid] <= x) lo = mid; else hi = mid;
        }
        float t = (x - _x[lo]) / MathF.Max(_x[hi] - _x[lo], 1e-6f);
        return _r[lo] + t * (_r[hi] - _r[lo]);
    }

    public float RadiusAt(float x) => InterpR(x);
    public float XMin => _x[0];
    public float XMax => _x[^1];
}

/// <summary>
/// Curved rectangular cooling channel that follows the outer face of the
/// inner chamber wall. Parametrized by:
///   • the inner-wall contour R(x)
///   • wall thickness t_wall (channel's inner radial face sits at R(x)+t_wall)
///   • channel height h(x)    (radial extent, outward)
///   • channel width  w(x)    (circumferential extent at channel mid-radius)
///   • angular center θ_k     (this channel of the bundle)
///   • axial extent [x_start, x_end]
///
/// Approximation: the cross-section is rectangular in (arc-length, radial)
/// coordinates, not in local (w, h). For channels with small angular extent
/// (typically 2π/N with N = 40–200 channels) this is indistinguishable from
/// a true swept profile at voxel resolutions of 0.25 mm.
/// </summary>
public sealed class AxialChannelImplicit : IImplicit
{
    private readonly RevolvedContourImplicit _innerWall;
    private readonly float _tWall_mm;
    private readonly float[] _hHeightAnchors;     // at x = [xStart, xThroat, xEnd]
    private readonly float _xStart, _xThroat, _xEnd;
    private readonly int _channelCount;
    private readonly float _ribThickness_mm;
    private readonly float _thetaCenter;
    private readonly float _filletRadius_mm;      // 0 = sharp ends (pre-upgrade behaviour)
    private readonly float _tanHelix;             // PHASE 4: 0 = pure axial; tan(α) otherwise

    public AxialChannelImplicit(
        RevolvedContourImplicit innerWall,
        float tWall_mm,
        float hChamber, float hThroat, float hExit,
        float xStart_mm, float xThroat_mm, float xEnd_mm,
        int channelCount,
        float ribThickness_mm,
        float thetaCenterRad,
        float manifoldFilletRadius_mm = 0f,
        float helixPitchAngle_deg = 0f)
    {
        _innerWall = innerWall;
        _tWall_mm = tWall_mm;
        _hHeightAnchors = new[] { hChamber, hThroat, hExit };
        _xStart = xStart_mm;
        _xThroat = xThroat_mm;
        _xEnd = xEnd_mm;
        _channelCount = Math.Max(channelCount, 1);
        _ribThickness_mm = ribThickness_mm;
        _thetaCenter = thetaCenterRad;
        _filletRadius_mm = MathF.Max(manifoldFilletRadius_mm, 0f);
        _tanHelix = MathF.Tan(helixPitchAngle_deg * MathF.PI / 180f);
    }

    public float fSignedDistance(in Vector3 p)
    {
        // Axial range check
        float dxStart = _xStart - p.X;
        float dxEnd = p.X - _xEnd;
        float dxOut = MathF.Max(dxStart, dxEnd);

        // Geometry at this axial position
        float R = _innerWall.RadiusAt(MathF.Max(MathF.Min(p.X, _xEnd), _xStart));
        float h = HeightAt(p.X);
        float rInner = R + _tWall_mm;
        float rOuter = rInner + h;
        float rMid = 0.5f * (rInner + rOuter);

        float pitch = 2f * MathF.PI * rMid / _channelCount;
        float w = MathF.Max(pitch - _ribThickness_mm, 0.3f);

        // Manifold fillet: within `_filletRadius_mm` of either axial end,
        // widen the circumferential extent from nominal w toward full pitch
        // via a quarter-circle profile. The rib ends are rounded, removing
        // the sharp 90° stress-concentrator corner where the channel meets
        // the plenum. No change to radial extent h (plenum and channel share
        // radial band), and channel floor/ceiling remain flat.
        if (_filletRadius_mm > 1e-4f)
        {
            float axialMargin = MathF.Min(p.X - _xStart, _xEnd - p.X);
            if (axialMargin >= 0 && axialMargin < _filletRadius_mm)
            {
                float u = axialMargin / _filletRadius_mm;              // 0 at end → 1 at nominal
                // arc = 1 at u=0 (full pitch), 0 at u=1 (nominal w)
                float arc = 1f - MathF.Sqrt(MathF.Max(1f - (1f - u) * (1f - u), 0f));
                w = w + arc * (pitch - w);
            }
        }

        // Radial
        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        float dRadialCenter = r - rMid;
        float drOut = MathF.Abs(dRadialCenter) - 0.5f * h;

        // Circumferential (arc distance at r_mid).
        // PHASE 4: helical channels offset the effective center angle with x
        // via θ_c(x) = θ_center + x · tan(α) / r_mid. α = 0 reproduces the
        // pure-axial path; increasing α makes the channel spiral around the
        // inner wall. The channel cross-section stays perpendicular to the
        // chamber axis — a simplification, but visually and thermally close
        // enough at the 5–25° pitch angles we care about.
        float theta = MathF.Atan2(p.Z, p.Y);
        float thetaAt = _thetaCenter;
        if (_tanHelix != 0f)
            thetaAt += (p.X - _xStart) * _tanHelix / MathF.Max(rMid, 1e-3f);
        float dTheta = theta - thetaAt;
        // wrap into [-π, π]
        while (dTheta > MathF.PI)  dTheta -= 2f * MathF.PI;
        while (dTheta < -MathF.PI) dTheta += 2f * MathF.PI;
        float arc_dist = dTheta * rMid;
        float dArcOut = MathF.Abs(arc_dist) - 0.5f * w;

        float outsideX = MathF.Max(dxOut, 0);
        float outsideR = MathF.Max(drOut, 0);
        float outsideA = MathF.Max(dArcOut, 0);
        float outside = MathF.Sqrt(outsideX * outsideX + outsideR * outsideR + outsideA * outsideA);

        float inside = MathF.Max(dxOut, MathF.Max(drOut, dArcOut));
        return outside > 0 ? outside : inside;
    }

    private float HeightAt(float x)
    {
        if (x <= _xThroat)
        {
            float t = MathF.Max(MathF.Min((x - _xStart) / MathF.Max(_xThroat - _xStart, 1e-6f), 1f), 0f);
            return _hHeightAnchors[0] + t * (_hHeightAnchors[1] - _hHeightAnchors[0]);
        }
        else
        {
            float t = MathF.Max(MathF.Min((x - _xThroat) / MathF.Max(_xEnd - _xThroat, 1e-6f), 1f), 0f);
            return _hHeightAnchors[1] + t * (_hHeightAnchors[2] - _hHeightAnchors[1]);
        }
    }
}

/// <summary>
/// Pattern-mode N-channel cooling SDF — one signed-distance function that
/// represents the entire bundle of N evenly-spaced axial (or helical)
/// channels around the chamber axis. Replaces the per-channel loop of
/// <see cref="AxialChannelImplicit"/> with a single voxelise + single
/// BoolSubtract: at point p, the angular offset to the nearest channel
/// centre is found via modular arithmetic on θ instead of by enumerating
/// every channel's <see cref="AxialChannelImplicit.fSignedDistance"/>.
/// <para>
/// Parity guarantee — for a given (x, y, z), this returns
/// <c>min over k of AxialChannelImplicit(thetaCenterRad = k·2π/N + phase).fSignedDistance(p)</c>
/// to within float precision. Verified by
/// <c>Voxelforge.Tests/AxialChannelPatternEquivalenceTests.cs</c>.
/// </para>
/// <para>
/// Helix support — the per-station angular offset
/// <c>θ_helix(x) = (x − xStart) · tan(α) / r_mid</c> is applied as a
/// **phase shift** to the modular reduction so a query point at axial x
/// finds the nearest helically-displaced channel centre. Identical to
/// <see cref="AxialChannelImplicit"/>'s helix math, just with the wrap
/// done modulo 2π/N instead of 2π.
/// </para>
/// </summary>
public sealed class AxialChannelPatternImplicit : IImplicit
{
    private readonly RevolvedContourImplicit _innerWall;
    private readonly float _tWall_mm;
    private readonly float[] _hHeightAnchors;     // at x = [xStart, xThroat, xEnd]
    private readonly float _xStart, _xThroat, _xEnd;
    private readonly int _channelCount;
    private readonly float _ribThickness_mm;
    private readonly float _phaseOffsetRad;       // pattern rotation; 0 places channel 0 at theta = 0
    private readonly float _filletRadius_mm;
    private readonly float _tanHelix;

    public AxialChannelPatternImplicit(
        RevolvedContourImplicit innerWall,
        float tWall_mm,
        float hChamber, float hThroat, float hExit,
        float xStart_mm, float xThroat_mm, float xEnd_mm,
        int channelCount,
        float ribThickness_mm,
        float phaseOffsetRad = 0f,
        float manifoldFilletRadius_mm = 0f,
        float helixPitchAngle_deg = 0f)
    {
        if (channelCount < 1)
            throw new System.ArgumentOutOfRangeException(nameof(channelCount),
                "channel count must be ≥ 1");
        _innerWall = innerWall;
        _tWall_mm = tWall_mm;
        _hHeightAnchors = new[] { hChamber, hThroat, hExit };
        _xStart = xStart_mm;
        _xThroat = xThroat_mm;
        _xEnd = xEnd_mm;
        _channelCount = channelCount;
        _ribThickness_mm = ribThickness_mm;
        _phaseOffsetRad = phaseOffsetRad;
        _filletRadius_mm = MathF.Max(manifoldFilletRadius_mm, 0f);
        _tanHelix = MathF.Tan(helixPitchAngle_deg * MathF.PI / 180f);
    }

    public float fSignedDistance(in Vector3 p)
    {
        // Axial range check (identical to AxialChannelImplicit).
        float dxStart = _xStart - p.X;
        float dxEnd = p.X - _xEnd;
        float dxOut = MathF.Max(dxStart, dxEnd);

        // Geometry at this axial position.
        float R = _innerWall.RadiusAt(MathF.Max(MathF.Min(p.X, _xEnd), _xStart));
        float h = HeightAt(p.X);
        float rInner = R + _tWall_mm;
        float rOuter = rInner + h;
        float rMid = 0.5f * (rInner + rOuter);

        float pitch = 2f * MathF.PI * rMid / _channelCount;
        float w = MathF.Max(pitch - _ribThickness_mm, 0.3f);

        // Manifold fillet (identical to AxialChannelImplicit — purely axial).
        if (_filletRadius_mm > 1e-4f)
        {
            float axialMargin = MathF.Min(p.X - _xStart, _xEnd - p.X);
            if (axialMargin >= 0 && axialMargin < _filletRadius_mm)
            {
                float u = axialMargin / _filletRadius_mm;
                float arc = 1f - MathF.Sqrt(MathF.Max(1f - (1f - u) * (1f - u), 0f));
                w = w + arc * (pitch - w);
            }
        }

        // Radial.
        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        float dRadialCenter = r - rMid;
        float drOut = MathF.Abs(dRadialCenter) - 0.5f * h;

        // Circumferential — pattern reduction by modular θ.
        // Per-channel form (AxialChannelImplicit): dTheta = θ − thetaCenter[k]
        //   then wrap to [-π, π], where thetaCenter[k] = k·2π/N + helixPhase(x).
        // Pattern form: subtract the helical phase, then wrap to the nearest
        //   channel centre via mod 2π/N → result is in [-π/N, π/N].
        float theta = MathF.Atan2(p.Z, p.Y);
        float thetaStep = 2f * MathF.PI / _channelCount;

        float helixPhase = _phaseOffsetRad;
        if (_tanHelix != 0f)
            helixPhase += (p.X - _xStart) * _tanHelix / MathF.Max(rMid, 1e-3f);

        float dThetaToZero = theta - helixPhase;
        // Mod into [-thetaStep/2, +thetaStep/2] — the nearest channel centre.
        // Round-half-to-even on .NET 9 matches double; we use MathF.Round which
        // rounds-half-to-even on net9.0. The exact tie-breaking does not
        // affect SDF values because at the half-step boundary the channel
        // pattern is symmetric (equally close to two channels).
        float kNearest = MathF.Round(dThetaToZero / thetaStep);
        float dTheta = dThetaToZero - kNearest * thetaStep;

        float arc_dist = dTheta * rMid;
        float dArcOut = MathF.Abs(arc_dist) - 0.5f * w;

        // Compose (identical to AxialChannelImplicit).
        float outsideX = MathF.Max(dxOut, 0);
        float outsideR = MathF.Max(drOut, 0);
        float outsideA = MathF.Max(dArcOut, 0);
        float outside = MathF.Sqrt(outsideX * outsideX + outsideR * outsideR + outsideA * outsideA);

        float inside = MathF.Max(dxOut, MathF.Max(drOut, dArcOut));
        return outside > 0 ? outside : inside;
    }

    private float HeightAt(float x)
    {
        if (x <= _xThroat)
        {
            float t = MathF.Max(MathF.Min((x - _xStart) / MathF.Max(_xThroat - _xStart, 1e-6f), 1f), 0f);
            return _hHeightAnchors[0] + t * (_hHeightAnchors[1] - _hHeightAnchors[0]);
        }
        else
        {
            float t = MathF.Max(MathF.Min((x - _xThroat) / MathF.Max(_xEnd - _xThroat, 1e-6f), 1f), 0f);
            return _hHeightAnchors[1] + t * (_hHeightAnchors[2] - _hHeightAnchors[1]);
        }
    }
}

/// <summary>Axis-aligned box SDF.</summary>
public sealed class BoxImplicit : IImplicit
{
    private readonly Vector3 _min, _max;
    public BoxImplicit(Vector3 min, Vector3 max) { _min = min; _max = max; }

    public float fSignedDistance(in Vector3 p)
    {
        float dx = MathF.Max(_min.X - p.X, p.X - _max.X);
        float dy = MathF.Max(_min.Y - p.Y, p.Y - _max.Y);
        float dz = MathF.Max(_min.Z - p.Z, p.Z - _max.Z);
        float outsideX = MathF.Max(dx, 0);
        float outsideY = MathF.Max(dy, 0);
        float outsideZ = MathF.Max(dz, 0);
        float outside = MathF.Sqrt(outsideX * outsideX + outsideY * outsideY + outsideZ * outsideZ);
        float inside = MathF.Max(dx, MathF.Max(dy, dz));
        return outside > 0 ? outside : inside;
    }
}

/// <summary>Finite cylinder SDF (axis and length specified).</summary>
public sealed class CylinderImplicit : IImplicit
{
    private readonly Vector3 _center;
    private readonly Vector3 _axis;
    private readonly float _radius;
    private readonly float _halfLength;

    public CylinderImplicit(Vector3 start, Vector3 direction, float radius, float length)
    {
        _axis = Vector3.Normalize(direction);
        _center = start + _axis * (length * 0.5f);
        _radius = radius;
        _halfLength = length * 0.5f;
    }

    public float fSignedDistance(in Vector3 p)
    {
        var d = p - _center;
        float axial = Vector3.Dot(d, _axis);
        var radialVec = d - _axis * axial;
        float radial = radialVec.Length();
        float axialDist = MathF.Abs(axial) - _halfLength;
        float radialDist = radial - _radius;
        float outsideA = MathF.Max(axialDist, 0);
        float outsideR = MathF.Max(radialDist, 0);
        float outside = MathF.Sqrt(outsideA * outsideA + outsideR * outsideR);
        float inside = MathF.Max(axialDist, radialDist);
        return outside > 0 ? outside : inside;
    }
}

/// <summary>
/// Sprint 14 / Track I / P13: min-SDF union of N <see cref="IImplicit"/>
/// shapes — voxelize once instead of N times. The bolt-circle pattern in
/// <see cref="ChamberVoxelBuilder"/> previously voxelised each
/// <see cref="CylinderImplicit"/> individually then BoolAdd'd them into
/// an accumulator (4-6 small voxel grids per flange × 3 flanges per
/// build). With this union, all bolts share one voxelize → one
/// BoolSubtract: same set-theoretic result, ~6× less voxel-grid alloc
/// per flange.
/// </summary>
public sealed class UnionImplicit : IImplicit
{
    private readonly IImplicit[] _parts;

    public UnionImplicit(params IImplicit[] parts)
    {
        if (parts is null || parts.Length == 0)
            throw new System.ArgumentException("must supply at least one part",
                nameof(parts));
        _parts = parts;
    }

    public float fSignedDistance(in Vector3 p)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < _parts.Length; i++)
        {
            float d = _parts[i].fSignedDistance(p);
            if (d < best) best = d;
        }
        return best;
    }
}

/// <summary>Annular ring between two concentric cylinders (fixed radii). Kept for tests.</summary>
public sealed class AnnulusImplicit : IImplicit
{
    private readonly float _xMin, _xMax, _rInner, _rOuter;

    public AnnulusImplicit(float xMin, float xMax, float rInner, float rOuter)
    { _xMin = xMin; _xMax = xMax; _rInner = rInner; _rOuter = rOuter; }

    public float fSignedDistance(in Vector3 p)
    {
        float dxOut = MathF.Max(_xMin - p.X, p.X - _xMax);
        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        float drOutInner = _rInner - r;
        float drOutOuter = r - _rOuter;
        float drOut = MathF.Max(drOutInner, drOutOuter);

        float outsideX = MathF.Max(dxOut, 0);
        float outsideR = MathF.Max(drOut, 0);
        float outside = MathF.Sqrt(outsideX * outsideX + outsideR * outsideR);
        float inside = MathF.Max(dxOut, drOut);
        return outside > 0 ? outside : inside;
    }
}

/// <summary>
/// Annular "plenum" void whose inner and outer radii track the chamber's wall
/// contour. Use this for manifold chambers so the plenum stays properly inset
/// from both the gas-side wall and the outer jacket over its axial extent.
///
/// Given a contour R_inner(x) describing the inner gas-side wall:
///   plenum inner radius = R_inner(x) + t_wall + radialClearance
///   plenum outer radius = R_inner(x) + t_wall + channelHeight_mm − radialClearance
/// The radial clearance guarantees at least `clearance` mm of solid on both
/// the inner (gas) wall face and the outer jacket face over the entire plenum.
/// </summary>
public sealed class RevolvedPlenumImplicit : IImplicit
{
    private readonly RevolvedContourImplicit _innerContour;
    private readonly float _tWall;
    private readonly float _channelHeight;
    private readonly float _clearance;
    private readonly float _xMin, _xMax;

    public RevolvedPlenumImplicit(
        RevolvedContourImplicit innerContour,
        float xMin_mm, float xMax_mm,
        float tWall_mm,
        float channelHeight_mm,
        float radialClearance_mm = 0.4f)
    {
        _innerContour = innerContour;
        _xMin = xMin_mm; _xMax = xMax_mm;
        _tWall = tWall_mm;
        _channelHeight = channelHeight_mm;
        _clearance = radialClearance_mm;
    }

    public float fSignedDistance(in Vector3 p)
    {
        float dxOut = MathF.Max(_xMin - p.X, p.X - _xMax);

        float xClamped = MathF.Max(MathF.Min(p.X, _xMax), _xMin);
        float rWall = _innerContour.RadiusAt(xClamped);
        float rInner = rWall + _tWall + _clearance;
        float rOuter = rWall + _tWall + _channelHeight - _clearance;
        if (rOuter <= rInner + 0.1f) rOuter = rInner + 0.1f;  // defensive

        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        float drOut = MathF.Max(rInner - r, r - rOuter);

        float outsideX = MathF.Max(dxOut, 0);
        float outsideR = MathF.Max(drOut, 0);
        float outside = MathF.Sqrt(outsideX * outsideX + outsideR * outsideR);
        float inside = MathF.Max(dxOut, drOut);
        return outside > 0 ? outside : inside;
    }
}

/// <summary>
/// Solid disc (short right cylinder) centred on the X axis — used for flanges
/// (injector face, nozzle mount). Axis is +X, so the disc spans
/// x ∈ [xStart, xStart + thickness] and r ∈ [0, radius].
/// </summary>
public sealed class DiscImplicit : IImplicit
{
    private readonly float _xStart, _xEnd, _radius;

    public DiscImplicit(float xStart_mm, float thickness_mm, float radius_mm)
    {
        _xStart = xStart_mm;
        _xEnd = xStart_mm + thickness_mm;
        _radius = radius_mm;
    }

    public float fSignedDistance(in Vector3 p)
    {
        float dxOut = MathF.Max(_xStart - p.X, p.X - _xEnd);
        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        float drOut = r - _radius;

        float outsideX = MathF.Max(dxOut, 0);
        float outsideR = MathF.Max(drOut, 0);
        float outside = MathF.Sqrt(outsideX * outsideX + outsideR * outsideR);
        float inside = MathF.Max(dxOut, drOut);
        return outside > 0 ? outside : inside;
    }
}

/// <summary>
/// Inlet dome — an axisymmetric plenum carved behind the injector
/// face. Modeled as a short cylinder
/// (radius <paramref name="R"/>, axial extent <paramref name="depth"/>)
/// that BoolSubtracts from the injector flange to hollow out the
/// propellant manifold. An optional anti-vortex baffle (a thin radial
/// disc) is drawn by the voxel builder separately when enabled.
///
/// Conventions:
///   • Dome sits at x ∈ [xBack, xBack + depth], with xBack &lt; 0
///     (behind the chamber face which is at x = 0).
///   • Radius R is centred on the chamber axis.
///   • Caller is responsible for union / subtract order so the dome
///     pocket ends up inside the flange, not connected to the chamber
///     through the injector bore pattern.
/// </summary>
public sealed class RevolvedDomeImplicit : IImplicit
{
    private readonly float _xStart, _xEnd, _radius;

    public RevolvedDomeImplicit(float xStart_mm, float depth_mm, float radius_mm)
    {
        _xStart = xStart_mm;
        _xEnd   = xStart_mm + depth_mm;
        _radius = MathF.Max(radius_mm, 0.5f);
    }

    public float fSignedDistance(in Vector3 p)
    {
        float dxOut = MathF.Max(_xStart - p.X, p.X - _xEnd);
        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        float drOut = r - _radius;

        float outsideX = MathF.Max(dxOut, 0);
        float outsideR = MathF.Max(drOut, 0);
        float outside = MathF.Sqrt(outsideX * outsideX + outsideR * outsideR);
        float inside = MathF.Max(dxOut, drOut);
        return outside > 0 ? outside : inside;
    }
}
