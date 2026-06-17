// AerospikePlugChannel.cs — Axial regen cooling-channel SDF for the
// aerospike plug body.
//
// Geometry
// ────────
// Each channel is a curved rectangular prism that hugs the plug's
// external (gas-facing) surface, just inside the wall. Cross-section
// is (channel-width × channel-depth) in the (θ, radial-inward)
// directions. Along +X the channel follows the plug's taper so the
// coolant sees a diminishing cross-section from throat tip (axial
// position 0, plug radius ≈ R_o) toward the truncation.
//
//   Wall stack (gas-side → coolant):
//     gas ── plug-wall (t_wall) ── coolant-channel (w × h)
//
// N channels are placed on equal angular spacing. Each channel SDF is
// negative inside the rectangular band; negative → Voxels boolean-
// subtracts the channel void from the plug body.
//
// Distance reporting
// ──────────────────
// Same convention as `AxialChannelImplicit` in the regen chamber:
// - Outside the axial range [xMin, xMax] return Euclidean distance to
//   nearest axial cap (positive).
// - Outside the radial or angular envelope return Euclidean distance
//   to the nearest clipping surface.
// - Inside all three envelopes return (max of the three signed
//   distances) — approximation but adequate for PicoGK voxelise.
//
// A single `AerospikePlugChannelArray` composite SDF unions all N
// channels into one implicit the builder voxelises + subtracts once
// (O(1) BoolSubtract vs O(N) on the main regen chamber).

using System.Numerics;
using PicoGK;
using Voxelforge.Chamber;

namespace Voxelforge.Geometry;

/// <summary>
/// Single axial cooling-channel SDF along the plug exterior. Channel
/// axis runs +X from <c>xStart_mm</c> to <c>xEnd_mm</c>; cross-section
/// is radial-inward and circumferential at each station. Plug-surface
/// r(x) is sampled from the supplied <see cref="AerospikeContour"/>.
/// </summary>
public sealed class AerospikePlugChannelImplicit : IImplicit
{
    private readonly float[] _x;           // axial stations
    private readonly float[] _rSurf;       // plug external radius at each station
    private readonly float _tWall;          // wall thickness (mm) between gas + channel
    private readonly float _height;         // channel radial depth (mm)
    private readonly float _widthHalf;      // channel arc half-width at plug centerline (mm)
    private readonly float _thetaCenter;    // nominal channel angular position (radians)
    private readonly float _xMin, _xMax;

    public AerospikePlugChannelImplicit(
        AerospikeContour contour,
        float tWall_mm,
        float depth_mm,
        float width_mm,
        float thetaCenterRad)
    {
        int n = contour.Stations.Length;
        _x = new float[n];
        _rSurf = new float[n];
        for (int i = 0; i < n; i++)
        {
            _x[i] = (float)contour.Stations[i].X_mm;
            _rSurf[i] = (float)contour.Stations[i].R_inner_mm;
        }
        _tWall = MathF.Max(tWall_mm, 0.1f);
        _height = MathF.Max(depth_mm, 0.1f);
        _widthHalf = MathF.Max(width_mm, 0.1f) * 0.5f;
        _thetaCenter = thetaCenterRad;
        _xMin = _x[0];
        _xMax = _x[^1];
    }

    public float fSignedDistance(in Vector3 p)
    {
        // Axial clip — outside [xMin, xMax] return distance to nearest cap.
        if (p.X < _xMin)
        {
            float dx = _xMin - p.X;
            return dx;
        }
        if (p.X > _xMax)
        {
            float dx = p.X - _xMax;
            return dx;
        }

        // Plug surface radius at this axial station, interpolated.
        float rSurface = InterpR(p.X);
        // Channel centerline radius: recessed by (t_wall + 0.5·h) from surface.
        float rCenter = rSurface - _tWall - 0.5f * _height;
        if (rCenter <= 0) return 1e3f;     // degenerate — channel too deep for plug

        // Sample point cylindrical coords.
        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        float theta = MathF.Atan2(p.Z, p.Y);

        // Wrap (theta − thetaCenter) into [-π, π].
        float dTheta = theta - _thetaCenter;
        while (dTheta > MathF.PI)  dTheta -= 2f * MathF.PI;
        while (dTheta < -MathF.PI) dTheta += 2f * MathF.PI;

        // Radial distance to channel centerline → signed distance to top/bottom face of channel.
        float dRadial = MathF.Abs(r - rCenter) - 0.5f * _height;
        // Arc distance at plug radius → signed distance to left/right face.
        float arc = dTheta * rCenter;
        float dArc = MathF.Abs(arc) - _widthHalf;

        // Inside all three (axial guaranteed from clip above): max = deepest negative.
        float outsideRadial = MathF.Max(dRadial, 0);
        float outsideArc = MathF.Max(dArc, 0);
        float outside = MathF.Sqrt(outsideRadial * outsideRadial + outsideArc * outsideArc);
        float inside = MathF.Max(dRadial, dArc);
        return outside > 0 ? outside : inside;
    }

    private float InterpR(float x)
    {
        for (int i = 0; i < _x.Length - 1; i++)
        {
            if (x >= _x[i] && x <= _x[i + 1])
            {
                float t = (x - _x[i]) / MathF.Max(_x[i + 1] - _x[i], 1e-6f);
                return _rSurf[i] + t * (_rSurf[i + 1] - _rSurf[i]);
            }
        }
        return x < _xMin ? _rSurf[0] : _rSurf[^1];
    }
}

/// <summary>
/// Union of N equally-spaced <see cref="AerospikePlugChannelImplicit"/>
/// channels around the plug axis. One voxelise + one BoolSubtract
/// cuts the full channel array out of the plug body.
/// </summary>
public sealed class AerospikePlugChannelArray : IImplicit
{
    private readonly AerospikePlugChannelImplicit[] _channels;

    public AerospikePlugChannelArray(
        AerospikeContour contour,
        int count,
        float tWall_mm,
        float depth_mm,
        float width_mm)
    {
        if (count <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(count),
                "channel count must be positive");
        _channels = new AerospikePlugChannelImplicit[count];
        for (int k = 0; k < count; k++)
        {
            float theta = 2f * MathF.PI * k / count;
            _channels[k] = new AerospikePlugChannelImplicit(
                contour, tWall_mm, depth_mm, width_mm, theta);
        }
    }

    public float fSignedDistance(in Vector3 p)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < _channels.Length; i++)
        {
            float d = _channels[i].fSignedDistance(p);
            if (d < best) best = d;
        }
        return best;
    }
}
