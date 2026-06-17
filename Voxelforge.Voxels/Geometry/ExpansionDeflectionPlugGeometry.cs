// ExpansionDeflectionPlugGeometry.cs — Solid inner plug for E-D nozzle topology.
//
// The expansion-deflection (E-D) nozzle uses an annular throat: flow passes
// through the gap between the outer cowl wall and a central truncated-cone
// plug. PR #328 shipped the physics model (outer bell + gate); this file adds
// the matching voxel body so the printed STL is a monolithic assembly.
//
// Plug shape: at each bell station i (ThroatIndex .. end), the plug radius is
//   r_plug(x_i) = innerOuterRatio × r_cowl(x_i)
// which keeps the annular-to-total area ratio constant along the expansion
// section — physically correct per the Angelino geometry.
//
// The plug is a solid body of revolution (RevolvedContourImplicit returns
// negative SDF for r < R(x)) fused into the outer shell via BoolAdd.
// RevolvedContourImplicit naturally returns a positive (outside) distance for
// x outside [x_throat, x_exit], so no manual axial clipping is needed beyond
// scoping the voxelization BBox to the bell region for performance.

using System;
using System.Linq;
using System.Numerics;
using PicoGK;
using Voxelforge.Chamber;

namespace Voxelforge.Geometry;

public static class ExpansionDeflectionPlugGeometry
{
    /// <summary>
    /// Adds a solid truncated-cone inner plug to <paramref name="shell"/>.
    /// Must be called after the outer-bell shell and cooling channels are
    /// built but before the smoothing pass so the plug edges blend correctly.
    /// </summary>
    /// <param name="shell">Accumulated outer-bell voxel body; mutated in place.</param>
    /// <param name="bounds">Voxelization bounds from <c>ChamberVoxelBuilder.Build</c>.</param>
    /// <param name="contour">Chamber contour for the E-D outer bell (throat radius
    ///   already inflated by √(1/(1−ratio²)) upstream).</param>
    /// <param name="innerOuterRatio">R_plug / R_cowl at each station. Must be in (0, 1).</param>
    public static void AddPlug(
        Voxels shell,
        BBox3 bounds,
        ChamberContour contour,
        double innerOuterRatio)
    {
        if (innerOuterRatio <= 0.0) return;  // ratio ≤ 0 → no plug, no-op
        if (innerOuterRatio >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(innerOuterRatio),
                $"Must be in (0, 1); got {innerOuterRatio}");

        // Collect bell stations (throat → exit), scaling each radius by the ratio.
        var plugPts = contour.Stations
            .Skip(contour.ThroatIndex)
            .Select(s => (x_mm: s.X_mm, r_mm: s.R_mm * innerOuterRatio))
            .ToList();

        if (plugPts.Count < 2)
            return; // degenerate contour (throat == exit station) — no plug to build

        // Solid body of revolution: r < R(x) is inside (negative SDF).
        var plugImpl = new RevolvedContourImplicit(plugPts);

        // Scope the voxelization to the bell region for performance.
        // RevolvedContourImplicit already returns positive distance outside
        // [x_throat, x_exit], so this is purely an optimisation.
        float throatX = (float)plugPts[0].x_mm - 1f;
        float exitX   = (float)plugPts[^1].x_mm + 1f;
        var plugBounds = new BBox3(
            new Vector3(throatX, bounds.vecMin.Y, bounds.vecMin.Z),
            new Vector3(exitX,   bounds.vecMax.Y, bounds.vecMax.Z));

        var plugVox = LibraryScope.MakeVoxels(plugImpl, plugBounds);
        shell.BoolAdd(plugVox);
    }
}
