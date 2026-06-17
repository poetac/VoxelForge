// TopologyOptimizedChannelImplicit.cs — OOB-2 Sprint 2 (2026-05-04).
//
// Variable-pitch axial channel pattern. Sibling to AxialChannelPatternImplicit,
// but instead of a single fixed channel count it carries a per-station
// count field (n_channels(x), from the SIMP solver in
// Voxelforge.Optimization.TopologyOptimizedChannels) and interpolates
// linearly between samples. The modular-θ math from
// AxialChannelPatternImplicit generalises cleanly: the local θ-step is
// 2π / N_local(x) where N_local is the smooth interpolation of the
// per-station integer field.
//
// Why this works geometrically. As N varies along x, the apparent
// channel pitch widens or narrows but each query point still snaps to
// the nearest channel centre at the local pitch. Two pitches that
// straddle a station boundary produce a continuous SDF (the channel
// "centres" drift slightly between adjacent samples, but the wall
// distance is C⁰-continuous). For SIMP-typical density fields where
// N varies by ≤ 2× over a chamber length, the resulting voxel mesh
// looks like normal axial channels with widely-spaced "wider" channels
// in low-density (barrel / exit) regions and tightly-spaced "narrower"
// channels at the throat — exactly the geometric encoding the SIMP
// objective rewards (more cooling area where Bartz heat flux is highest).
//
// Sprint scope (2026-05-04):
//   - Implicit primitive only. No manifold-fillet handoff (fillet path
//     reuses the AxialChannelPatternImplicit shape, which assumes
//     uniform N near the manifold boundaries — fine when the SIMP
//     density is near-baseline at the chamber endpoints, which is the
//     common case for OC-method outputs).
//   - No helix support (helical + variable N is a Sprint 3+ feature).
//   - No printability post-pass (Sprint 3 — TOPOLOGY_CHANNEL_NOT_PRINTABLE
//     advisory gate per ADR-024).

using System;
using System.Numerics;
using PicoGK;

namespace Voxelforge.Geometry;

/// <summary>
/// Variable-pitch axial channel pattern implicit. Each axial slice has
/// its own channel count interpolated from the per-station field; pitch
/// = 2π·r / N_local(x).
/// </summary>
public sealed class TopologyOptimizedChannelImplicit : IImplicit
{
    private readonly RevolvedContourImplicit _innerWall;
    private readonly float _tWall_mm;
    private readonly float[] _hHeightAnchors;     // [hChamber, hThroat, hExit]
    private readonly float _xStart, _xThroat, _xEnd;
    private readonly float[] _xCoords_mm;         // axial sample positions (sorted ascending)
    private readonly float[] _nChannels;          // smoothed channel count per sample (kept as float to support interp)
    private readonly float _ribThickness_mm;
    private readonly float _filletRadius_mm;
    private readonly float _phaseOffsetRad;

    public TopologyOptimizedChannelImplicit(
        RevolvedContourImplicit innerWall,
        float tWall_mm,
        float hChamber, float hThroat, float hExit,
        float xStart_mm, float xThroat_mm, float xEnd_mm,
        System.Collections.Generic.IReadOnlyList<double> xCoords_mm,
        System.Collections.Generic.IReadOnlyList<int> channelsPerStation,
        float ribThickness_mm,
        float phaseOffsetRad = 0f,
        float manifoldFilletRadius_mm = 0f)
    {
        if (innerWall is null) throw new ArgumentNullException(nameof(innerWall));
        if (xCoords_mm is null) throw new ArgumentNullException(nameof(xCoords_mm));
        if (channelsPerStation is null) throw new ArgumentNullException(nameof(channelsPerStation));
        if (xCoords_mm.Count != channelsPerStation.Count)
            throw new ArgumentException(
                $"xCoords ({xCoords_mm.Count}) and channelsPerStation ({channelsPerStation.Count}) length mismatch.");
        if (xCoords_mm.Count < 2)
            throw new ArgumentException("Need at least 2 sample points for axial interpolation.");

        _innerWall = innerWall;
        _tWall_mm = tWall_mm;
        _hHeightAnchors = new[] { hChamber, hThroat, hExit };
        _xStart = xStart_mm;
        _xThroat = xThroat_mm;
        _xEnd = xEnd_mm;
        _ribThickness_mm = ribThickness_mm;
        _phaseOffsetRad = phaseOffsetRad;
        _filletRadius_mm = MathF.Max(manifoldFilletRadius_mm, 0f);

        _xCoords_mm = new float[xCoords_mm.Count];
        _nChannels = new float[channelsPerStation.Count];
        for (int i = 0; i < xCoords_mm.Count; i++)
        {
            _xCoords_mm[i] = (float)xCoords_mm[i];
            int n = channelsPerStation[i];
            if (n < 1)
                throw new ArgumentOutOfRangeException(nameof(channelsPerStation),
                    $"Channel count must be >= 1 at every sample (got {n} at index {i}).");
            _nChannels[i] = n;
        }

        // Verify ascending x.
        for (int i = 1; i < _xCoords_mm.Length; i++)
        {
            if (!(_xCoords_mm[i] > _xCoords_mm[i - 1]))
                throw new ArgumentException(
                    $"xCoords must be strictly ascending (sample {i} = {_xCoords_mm[i]} <= {_xCoords_mm[i - 1]}).");
        }
    }

    public float fSignedDistance(in Vector3 p)
    {
        // Axial range check (identical to AxialChannelPatternImplicit).
        float dxStart = _xStart - p.X;
        float dxEnd = p.X - _xEnd;
        float dxOut = MathF.Max(dxStart, dxEnd);

        // Geometry at this axial position.
        float xClamped = MathF.Max(MathF.Min(p.X, _xEnd), _xStart);
        float R = _innerWall.RadiusAt(xClamped);
        float h = HeightAt(p.X);
        float rInner = R + _tWall_mm;
        float rOuter = rInner + h;
        float rMid = 0.5f * (rInner + rOuter);

        // Local channel count via linear interpolation on _xCoords_mm.
        float nLocal = ChannelCountAt(p.X);

        // Pitch widens / narrows with N_local.
        float thetaStep = 2f * MathF.PI / nLocal;
        float pitch = thetaStep * rMid;
        float w = MathF.Max(pitch - _ribThickness_mm, 0.3f);

        // Manifold fillet (axial-only, identical to AxialChannelPatternImplicit).
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

        // Circumferential — pattern reduction with the LOCAL θ-step.
        float theta = MathF.Atan2(p.Z, p.Y);
        float dThetaToZero = theta - _phaseOffsetRad;
        float kNearest = MathF.Round(dThetaToZero / thetaStep);
        float dTheta = dThetaToZero - kNearest * thetaStep;
        float arc_dist = dTheta * rMid;
        float dArcOut = MathF.Abs(arc_dist) - 0.5f * w;

        // Compose (identical to AxialChannelPatternImplicit).
        float outsideX = MathF.Max(dxOut, 0);
        float outsideR = MathF.Max(drOut, 0);
        float outsideA = MathF.Max(dArcOut, 0);
        float outside = MathF.Sqrt(outsideX * outsideX + outsideR * outsideR + outsideA * outsideA);

        float inside = MathF.Max(dxOut, MathF.Max(drOut, dArcOut));
        return outside > 0 ? outside : inside;
    }

    /// <summary>
    /// Smooth (linearly-interpolated) channel count at axial position
    /// <paramref name="x"/>. Clamps to first / last sample at the
    /// endpoints.
    /// </summary>
    internal float ChannelCountAt(float x)
    {
        // Edge clamp.
        if (x <= _xCoords_mm[0]) return _nChannels[0];
        if (x >= _xCoords_mm[_xCoords_mm.Length - 1]) return _nChannels[_nChannels.Length - 1];

        // Binary-search-ish bucket find. Sample count is typically ~30-100
        // so a linear scan is fine + branch-predictor friendly.
        int hi = _xCoords_mm.Length - 1;
        int lo = 0;
        while (hi - lo > 1)
        {
            int mid = (hi + lo) >> 1;
            if (_xCoords_mm[mid] <= x) lo = mid;
            else hi = mid;
        }

        float x0 = _xCoords_mm[lo], x1 = _xCoords_mm[hi];
        float n0 = _nChannels[lo], n1 = _nChannels[hi];
        float t = (x - x0) / (x1 - x0);
        return n0 + t * (n1 - n0);
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
