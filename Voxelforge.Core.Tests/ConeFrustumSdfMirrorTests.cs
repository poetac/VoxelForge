// ConeFrustumSdfMirrorTests.cs — pins the canonical Inigo-Quilez sdCappedCone
// flank invariant for the horn-antenna cone-frustum SDF (red-team round-4
// finding). The real implementation, HornAntennaVoxelBuilder.ConeFrustumImplicit,
// lives in the net9.0-windows + PicoGK Voxels project which can't build/run on
// this Linux 'core' leg, so this mirrors its math with plain doubles to verify
// the fix's invariant and document the bug.
//
// The bug: IQ's sdCappedCone uses k2 = (r2−r1, 2·h) with h = the HALF-height.
// The code computed k2y = 2·(full height) — exactly double — which mislocated
// the zero-isosurface along the flared flank. The fix uses 2·halfH.

using System;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class ConeFrustumSdfMirrorTests
{
    // Mirror of ConeFrustumImplicit.fSignedDistance; `buggy` selects the old
    // k2y = 2·fullHeight vs the fixed k2y = 2·halfHeight.
    private static double Sdf(double qr, double z, double r0, double r1, double h, bool buggy)
    {
        double qz    = z - h * 0.5;
        double halfH = h * 0.5;
        double k2x   = r1 - r0;
        double k2y   = buggy ? 2.0 * h : 2.0 * halfH;

        double cax = qr - Math.Min(qr, qz < 0 ? r0 : r1);
        double cay = Math.Abs(qz) - halfH;

        double k1qx = r1 - qr, k1qy = halfH - qz;
        double k2dot = k2x * k2x + k2y * k2y;
        double t = Math.Clamp((k1qx * k2x + k1qy * k2y) / k2dot, 0.0, 1.0);
        double cbx = qr - r1 + k2x * t;
        double cby = qz - halfH + k2y * t;

        bool inside = cbx < 0.0 && cay < 0.0;
        double dist2 = Math.Min(cax * cax + cay * cay, cbx * cbx + cby * cby);
        return inside ? -Math.Sqrt(dist2) : Math.Sqrt(dist2);
    }

    [Fact]
    public void FlankPoints_HaveZeroSdf_WithFix()
    {
        // Frustum: r0=10 at z=0, r1=30 at z=40. The flank is r(z)=r0+(r1−r0)z/h;
        // every point on it must lie on the zero-isosurface.
        const double r0 = 10.0, r1 = 30.0, h = 40.0;
        for (double z = 5.0; z <= 35.0; z += 5.0)
        {
            double r = r0 + (r1 - r0) * z / h;
            double d = Sdf(r, z, r0, r1, h, buggy: false);
            Assert.True(Math.Abs(d) < 1e-4, $"fixed SDF on flank at z={z} should be ~0; got {d:F4}");
        }
    }

    [Fact]
    public void FlankPoints_AreMislocated_WithFactorTwoBug()
    {
        // The old k2y = 2·fullHeight puts the flank point well off the surface.
        const double r0 = 10.0, r1 = 30.0, h = 40.0;
        double rMid = r0 + (r1 - r0) * 20.0 / h;   // z=20 → r=20
        double dBuggy = Sdf(rMid, 20.0, r0, r1, h, buggy: true);
        Assert.True(Math.Abs(dBuggy) > 1.0,
            $"buggy SDF on flank should be far from 0 (it was ~−4.9 mm); got {dBuggy:F4}");
    }
}
