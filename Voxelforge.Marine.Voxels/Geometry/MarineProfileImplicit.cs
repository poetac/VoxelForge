// MarineProfileImplicit.cs — body-of-revolution SDF for the Myring AUV hull.
//
// Marine pillar's own revolved-contour IImplicit. Does NOT import from
// Voxelforge.Airbreathing.Voxels (VFA001 isolation rule — ADR-026).
// Functionally equivalent to RevolvedContourImplicit in that project but
// authored independently for the marine pillar.
//
// The SDF is approximate: for any 3D point the signed distance is measured
// from the radial distance to the interpolated hull profile R(x), which is
// exact on the rotation axis but underestimates the true Euclidean distance
// near sharp inflections. For smooth Myring profiles (n=2, m=1.5, p=0.5)
// this approximation is conservative and safe for voxelisation.
//
// All coordinates are in millimetres (PicoGK convention).
//
// References:
//   Myring, D. F. (1976). Aeronautical Quarterly 27(3), 186-194.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using PicoGK;

namespace Voxelforge.Marine.Geometry;

/// <summary>
/// IImplicit body of revolution for a sampled hull profile. Control points
/// (x_mm, r_mm) define the axisymmetric shape; points outside the x-extent
/// are outside the hull.
/// </summary>
internal sealed class MarineProfileImplicit : IImplicit
{
    private readonly float[] _x;
    private readonly float[] _r;

    /// <param name="profilePoints">
    /// Ordered (x_mm, r_mm) profile pairs sampled along the axial direction.
    /// At least two points required.
    /// </param>
    internal MarineProfileImplicit(IEnumerable<(double x_mm, double r_mm)> profilePoints)
    {
        var pts = profilePoints.OrderBy(p => p.x_mm).ToArray();
        if (pts.Length < 2)
            throw new ArgumentException("At least two profile points are required.", nameof(profilePoints));

        _x = new float[pts.Length];
        _r = new float[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            _x[i] = (float)pts[i].x_mm;
            _r[i] = (float)pts[i].r_mm;
        }
    }

    /// <summary>Axial extent minimum [mm].</summary>
    internal float XMin => _x[0];

    /// <summary>Axial extent maximum [mm].</summary>
    internal float XMax => _x[^1];

    /// <summary>Maximum radial extent [mm].</summary>
    internal float RMax => _r.Max();

    public float fSignedDistance(in Vector3 p)
    {
        float x = p.X;

        // Outside the axial extent — return a distance to the nearest cap.
        if (x < _x[0])  return _x[0] - x;
        if (x > _x[^1]) return x - _x[^1];

        float profileR = InterpR(x);
        float radial   = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        return radial - profileR;   // negative inside, positive outside
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
}
