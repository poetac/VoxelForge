// DUPLICATED — unify in post-Wave-1 wrap-up.
//
// Lifted verbatim from Voxelforge.Airbreathing.Voxels/Geometry/RevolvedContourImplicit.cs
// per the parallel-pillar policy in ADR-026 §2. The body-of-revolution
// SDF math is identical regardless of pillar — once 3+ concrete pillars
// own a copy, the right home is a shared Voxelforge.Voxels.Shared
// project.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using PicoGK;

namespace Voxelforge.ElectricPropulsion.Geometry;

/// <summary>
/// Body of revolution around the X axis, specified by a sorted array of
/// (x, R(x)) control points. Inside the contour, SDF is negative;
/// outside the x extent returns a large positive distance.
/// </summary>
internal sealed class RevolvedContourImplicit : IImplicit
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
            return axialGap;
        }
        float R = InterpR(x);
        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        return r - R;
    }

    private float InterpR(float x)
    {
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
