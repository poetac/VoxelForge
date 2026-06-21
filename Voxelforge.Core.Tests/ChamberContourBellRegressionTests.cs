// ChamberContourBellRegressionTests.cs — regression guard for the bell-parabola
// control-point bug (red-team round-4 finding). PicoGK-free → runs on the Linux
// CI 'core' leg.
//
// The quadratic-Bezier control point Q for the diverging bell is the
// intersection of the wall tangents at the bell entrance N (slope +tan θ_n) and
// the exit E (slope +tan θ_e): xQ = (R_e − rN + m1·xN − m2·xExit)/(m1 − m2).
// The code used (… + m2·xExit)/(m1 + m2) instead — a control point that does NOT
// lie on the exit tangent. The result: the bell overshot the exit radius by
// ~1.9 % near the end and the exit wall slope came out −tan θ_e (curving INWARD)
// instead of +tan θ_e (diverging). The existing contour tests only checked the
// endpoints + axial monotonicity, so the overshoot slipped through.

using System;
using Voxelforge.Chamber;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class ChamberContourBellRegressionTests
{
    private static ChamberContour SingleBell() =>
        ChamberContourGenerator.Generate(
            throatRadius_mm: 10.0, contractionRatio: 8.0, expansionRatio: 25.0,
            characteristicLength_m: 1.1, thetaN_deg: 35.0, thetaE_deg: 8.0,
            bellLengthFraction: 0.8, stationCount: 240);

    [Fact]
    public void BellExitSlope_DivergesAtThetaE_NotInverted()
    {
        var c = SingleBell();
        double expected = Math.Tan(8.0 * Math.PI / 180.0);   // +tan θ_e
        double exitSlope = c.Stations[^1].Slope;

        // Old code: exit slope = −tan θ_e (wall curving inward at the exit).
        Assert.True(exitSlope > 0.0,
            $"bell exit wall slope must diverge (positive); got {exitSlope:F4}");
        Assert.True(Math.Abs(exitSlope - expected) <= 0.02 * expected,
            $"exit slope {exitSlope:F4} should be ≈ +tan(8°)={expected:F4}");
    }

    [Fact]
    public void BellRadius_IsMonotonic_AndNeverOvershootsExit()
    {
        var c = SingleBell();
        for (int i = c.ThroatIndex; i < c.Stations.Length; i++)
        {
            Assert.True(c.Stations[i].R_mm <= c.ExitRadius_mm + 1e-6,
                $"station {i} R={c.Stations[i].R_mm:F3} overshoots R_e={c.ExitRadius_mm:F3}");
            if (i > c.ThroatIndex)
                Assert.True(c.Stations[i].R_mm >= c.Stations[i - 1].R_mm - 1e-6,
                    $"bell radius must be non-decreasing; broke at station {i}");
        }
        // Endpoints still exact (the fix must not move them).
        Assert.True(Math.Abs(c.Stations[^1].R_mm - c.ExitRadius_mm) <= 0.05,
            $"exit R={c.Stations[^1].R_mm:F3} must match R_e={c.ExitRadius_mm:F3}");
    }

    [Fact]
    public void DualBell_SecondParabolaExitSlope_Diverges()
    {
        var c = ChamberContourGenerator.Generate(
            throatRadius_mm: 10.0, contractionRatio: 8.0, expansionRatio: 60.0,
            characteristicLength_m: 1.1, thetaN_deg: 35.0, thetaE_deg: 6.0,
            bellLengthFraction: 0.8, stationCount: 300,
            dualBell: true, seaLevelExpansionRatio: 20.0, inflectionAngleDeg: 7.0);

        Assert.True(c.IsDualBell, "fixture must be a dual-bell contour");
        double expected = Math.Tan(6.0 * Math.PI / 180.0);
        double exitSlope = c.Stations[^1].Slope;
        Assert.True(exitSlope > 0.0,
            $"dual-bell exit wall slope must diverge (positive); got {exitSlope:F4}");
        Assert.True(Math.Abs(exitSlope - expected) <= 0.02 * expected,
            $"dual-bell exit slope {exitSlope:F4} should be ≈ +tan(6°)={expected:F4}");
    }
}
