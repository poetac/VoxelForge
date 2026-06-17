// DUPLICATED — unify in Step-1 wrap-up.
//
// Lifted verbatim from Voxelforge.Voxels/Geometry/ChamberImplicits.cs:32
// per the parallel-pillar policy. The rocket
// project's RevolvedContourImplicit is internal and adding the air-breathing
// Voxels project to its InternalsVisibleTo list would touch the rocket
// pipeline. Once 3+ concrete pillars exist (rocket + ramjet + turbojet)
// the right home for this primitive is a new Voxelforge.Voxels.Shared
// project per the unify-via-interfaces refactor.
//
// Air-breathing geometry is axisymmetric in the same way the rocket chamber
// is — a body of revolution around the X axis with radius given by a
// piecewise-linear function R(x) — so the SDF math is identical. All
// distances are signed (negative inside, positive outside). Point coords
// in millimetres.

using System.Numerics;
using PicoGK;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Body of revolution around the X axis, specified by a sorted array of
/// (x, R(x)) control points. Inside the contour, SDF is negative
/// (approximate — signed by radial distance from R(x) at the point's x).
/// Outside the x extent returns a large positive distance.
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
            return axialGap;     // outside axially
        }
        float R = InterpR(x);
        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        return r - R;            // inside if r < R
    }

    private float InterpR(float x)
    {
        // Binary search.
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
