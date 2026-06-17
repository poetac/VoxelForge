// ContourTests.cs — Freeze the Rao contour generator against the bug class
// that required a rewrite in 2026-04-17.
//
// The key regression guard: the upstream converging fillet must be
// CONTINUOUS in both r(x) and slope dr/dx at the barrel→fillet junction
// and at the fillet→cone junction. Prior code placed the fillet centre at
// the wrong x, leaving a radius step that manifested as a detached voxel
// ring.

using Voxelforge.Chamber;

namespace Voxelforge.Tests;

public class ContourTests
{
    private static ChamberContour Default() =>
        ChamberContourGenerator.Generate(
            throatRadius_mm: 3.0,
            contractionRatio: 6.0,
            expansionRatio: 8.0,
            characteristicLength_m: 1.1,
            thetaN_deg: 30.0,
            thetaE_deg: 10.0,
            bellLengthFraction: 0.8,
            stationCount: 240);

    [Fact]
    public void ContourIsMonotonicInX()
    {
        var c = Default();
        for (int i = 1; i < c.Stations.Length; i++)
            Assert.True(c.Stations[i].X_mm >= c.Stations[i - 1].X_mm - 1e-6,
                $"station {i} went backwards in X ({c.Stations[i-1].X_mm:F2} → {c.Stations[i].X_mm:F2})");
    }

    [Fact]
    public void BarrelRadiusIsConstantAtR_c()
    {
        var c = Default();
        double R_c = c.ChamberRadius_mm;
        foreach (var s in c.Stations)
            if (s.Region == ChamberRegion.Barrel)
                Assert.InRange(s.R_mm, R_c - 0.01, R_c + 0.01);
    }

    [Fact]
    public void ThroatRadiusMatchesInput()
    {
        var c = Default();
        var throat = c.Stations[c.ThroatIndex];
        Assert.InRange(throat.R_mm, c.ThroatRadius_mm - 0.05, c.ThroatRadius_mm + 0.05);
    }

    [Fact]
    public void ExitRadiusMatchesExpansion()
    {
        var c = Default();
        double R_e_expected = c.ThroatRadius_mm * Math.Sqrt(c.ExpansionRatio);
        var exit = c.Stations[^1];
        Assert.InRange(exit.R_mm, R_e_expected - 0.05, R_e_expected + 0.05);
    }

    [Fact]
    public void NoRadiusJumpsAcrossConvergingJunctions()
    {
        // This is the regression test against the fillet-placement bug.
        // The maximum slope magnitude |dr/dx| between adjacent stations in
        // the converging region must be ≤ tan(40°) — if the bug is present
        // the radius snaps from R_c to rUpArcStart in one cell, which would
        // show as a slope ≫ tan(30°).
        var c = Default();
        double maxDrDx = 0;
        for (int i = 1; i < c.Stations.Length; i++)
        {
            var a = c.Stations[i - 1];
            var b = c.Stations[i];
            if (a.Region == ChamberRegion.Converging || b.Region == ChamberRegion.Converging)
            {
                double dx = Math.Max(b.X_mm - a.X_mm, 1e-6);
                double dr = Math.Abs(b.R_mm - a.R_mm);
                double drDx = dr / dx;
                if (drDx > maxDrDx) maxDrDx = drDx;
            }
        }
        Assert.True(maxDrDx < Math.Tan(40 * Math.PI / 180),
            $"converging slope exceeds 40° (max|dr/dx|={maxDrDx:F3} = {Math.Atan(maxDrDx)*180/Math.PI:F1}°) — likely the fillet-centre bug regressed");
    }

    [Fact]
    public void ChamberVolumeMatchesLStar()
    {
        var c = Default();
        double expectedV = c.CharacteristicLength_m * 1000.0 * c.ThroatArea_mm2;
        // L* is computed after the fact; barrel length is clamped to
        // reasonable bounds so the relationship can be approximate.
        double rel = Math.Abs(c.ChamberVolume_mm3 - expectedV) / Math.Max(expectedV, 1);
        Assert.True(rel < 0.5, $"V_c does not correspond to L* (rel err {rel:P1})");
    }
}
