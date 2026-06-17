// AxialChannelPatternEquivalenceTests.cs — Pin
// AxialChannelPatternImplicit's SDF to the min-over-N reference value
// produced by N separate AxialChannelImplicit instances. The pattern
// class is a perf optimisation over the per-channel loop in
// ChamberVoxelBuilder + ChamberAxialTileBuilder; this fixture is the
// regression net that catches drift in the modular-θ math (helix
// included) without needing the PicoGK voxel kernel.

using System;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry;

namespace Voxelforge.Tests;

public class AxialChannelPatternEquivalenceTests
{
    private static RevolvedContourImplicit BarrelContour() =>
        new(new[] { (0.0, 12.0), (20.0, 12.0), (40.0, 12.0) });

    private static RevolvedContourImplicit TaperedContour() =>
        new(new[]
        {
            (0.0, 12.0), (5.0, 12.0), (15.0, 8.0),
            (25.0, 9.5), (40.0, 11.0)
        });

    /// <summary>
    /// Reference: build N AxialChannelImplicit instances with thetaCenter
    /// = k·2π/N + phase and return the smallest SDF — i.e. the
    /// set-theoretic union of per-channel SDFs the production loop
    /// voxelises today.
    /// </summary>
    private static float MinPerChannelSdf(
        RevolvedContourImplicit inner,
        int n, float phase,
        float tWall, float hCham, float hThr, float hExit,
        float xStart, float xThroat, float xEnd,
        float rib, float fillet, float helixDeg,
        Vector3 p)
    {
        float best = float.PositiveInfinity;
        for (int k = 0; k < n; k++)
        {
            float theta = phase + 2f * MathF.PI * k / n;
            var ch = new AxialChannelImplicit(
                inner, tWall, hCham, hThr, hExit,
                xStart, xThroat, xEnd, n, rib, theta,
                manifoldFilletRadius_mm: fillet,
                helixPitchAngle_deg: helixDeg);
            float d = ch.fSignedDistance(p);
            if (d < best) best = d;
        }
        return best;
    }

    [Theory]
    [InlineData(4, 0f)]
    [InlineData(40, 0f)]
    [InlineData(80, 0f)]
    [InlineData(179, 0f)]
    [InlineData(40, 0.37f)]   // off-axis pattern phase
    [InlineData(179, 0.91f)]
    public void Pattern_MatchesMinOverPerChannel_NoFillet_NoHelix(int n, float phase)
    {
        var inner = BarrelContour();
        const float tWall = 1.0f, hCham = 2.0f, hThr = 1.5f, hExit = 2.5f;
        const float xStart = 5f, xThroat = 18f, xEnd = 35f;
        const float rib = 0.7f;

        var pattern = new AxialChannelPatternImplicit(
            inner, tWall, hCham, hThr, hExit,
            xStart, xThroat, xEnd, n, rib,
            phaseOffsetRad: phase);

        var rng = new Random(1234 + n);
        for (int i = 0; i < 200; i++)
        {
            float x = (float)rng.NextDouble() * 50f - 5f;       // span includes outside extents
            float r = (float)rng.NextDouble() * 20f;             // 0..20 mm radial
            float th = (float)(rng.NextDouble() * 2.0 * Math.PI);
            var p = new Vector3(x, r * MathF.Cos(th), r * MathF.Sin(th));

            float patVal = pattern.fSignedDistance(p);
            float refVal = MinPerChannelSdf(
                inner, n, phase, tWall, hCham, hThr, hExit,
                xStart, xThroat, xEnd, rib, 0f, 0f, p);

            // Sub-millimetre agreement — the modular-θ form is algebraically
            // identical to the per-channel min outside numerical noise.
            // 1e-3 mm absolute tolerance — well below voxel resolution
            // (0.4 mm). Direct |a-b| comparison avoids xUnit precision's
            // banker's-rounding edge cases at the .5 boundary.
            Assert.True(MathF.Abs(refVal - patVal) < 1e-3f,
                $"|{refVal} - {patVal}| = {MathF.Abs(refVal - patVal)} at p={p}");
        }
    }

    [Theory]
    [InlineData(40, 1.5f)]    // moderate fillet
    [InlineData(80, 2.5f)]    // large fillet
    [InlineData(179, 1.0f)]
    public void Pattern_MatchesMinOverPerChannel_WithFillet(int n, float fillet)
    {
        var inner = TaperedContour();
        const float tWall = 1.0f, hCham = 2.0f, hThr = 1.5f, hExit = 2.5f;
        const float xStart = 5f, xThroat = 18f, xEnd = 35f;
        const float rib = 0.7f;

        var pattern = new AxialChannelPatternImplicit(
            inner, tWall, hCham, hThr, hExit,
            xStart, xThroat, xEnd, n, rib,
            phaseOffsetRad: 0f,
            manifoldFilletRadius_mm: fillet);

        var rng = new Random(4321 + n);
        for (int i = 0; i < 200; i++)
        {
            float x = (float)rng.NextDouble() * 50f - 5f;
            float r = (float)rng.NextDouble() * 20f;
            float th = (float)(rng.NextDouble() * 2.0 * Math.PI);
            var p = new Vector3(x, r * MathF.Cos(th), r * MathF.Sin(th));

            float patVal = pattern.fSignedDistance(p);
            float refVal = MinPerChannelSdf(
                inner, n, 0f, tWall, hCham, hThr, hExit,
                xStart, xThroat, xEnd, rib, fillet, 0f, p);

            // 1e-3 mm absolute tolerance — well below voxel resolution
            // (0.4 mm). Direct |a-b| comparison avoids xUnit precision's
            // banker's-rounding edge cases at the .5 boundary.
            Assert.True(MathF.Abs(refVal - patVal) < 1e-3f,
                $"|{refVal} - {patVal}| = {MathF.Abs(refVal - patVal)} at p={p}");
        }
    }

    [Theory]
    [InlineData(40, 5f)]
    [InlineData(80, 15f)]
    [InlineData(120, 25f)]
    public void Pattern_MatchesMinOverPerChannel_WithHelix(int n, float helixDeg)
    {
        var inner = BarrelContour();
        const float tWall = 1.0f, hCham = 2.0f, hThr = 1.5f, hExit = 2.5f;
        const float xStart = 5f, xThroat = 18f, xEnd = 35f;
        const float rib = 0.7f;

        var pattern = new AxialChannelPatternImplicit(
            inner, tWall, hCham, hThr, hExit,
            xStart, xThroat, xEnd, n, rib,
            phaseOffsetRad: 0f,
            helixPitchAngle_deg: helixDeg);

        var rng = new Random(7777 + n);
        for (int i = 0; i < 200; i++)
        {
            float x = (float)rng.NextDouble() * 50f - 5f;
            float r = (float)rng.NextDouble() * 20f;
            float th = (float)(rng.NextDouble() * 2.0 * Math.PI);
            var p = new Vector3(x, r * MathF.Cos(th), r * MathF.Sin(th));

            float patVal = pattern.fSignedDistance(p);
            float refVal = MinPerChannelSdf(
                inner, n, 0f, tWall, hCham, hThr, hExit,
                xStart, xThroat, xEnd, rib, 0f, helixDeg, p);

            // 1e-3 mm absolute tolerance — well below voxel resolution
            // (0.4 mm). Direct |a-b| comparison avoids xUnit precision's
            // banker's-rounding edge cases at the .5 boundary.
            Assert.True(MathF.Abs(refVal - patVal) < 1e-3f,
                $"|{refVal} - {patVal}| = {MathF.Abs(refVal - patVal)} at p={p}");
        }
    }

    /// <summary>
    /// Sanity: a single-channel pattern (N=1) at phase φ must equal a
    /// single AxialChannelImplicit at thetaCenter=φ everywhere. Catches a
    /// degenerate-N regression that the multi-channel cases miss.
    /// </summary>
    [Fact]
    public void Pattern_N1_EqualsSingleChannel()
    {
        var inner = BarrelContour();
        const float tWall = 1.0f, h = 2.0f;
        const float xStart = 5f, xThroat = 18f, xEnd = 35f, rib = 0.7f;
        const float phase = 0.5f;

        var pattern = new AxialChannelPatternImplicit(
            inner, tWall, h, h, h, xStart, xThroat, xEnd, 1, rib,
            phaseOffsetRad: phase);
        var single = new AxialChannelImplicit(
            inner, tWall, h, h, h, xStart, xThroat, xEnd, 1, rib, phase);

        var rng = new Random(999);
        for (int i = 0; i < 50; i++)
        {
            float x = (float)rng.NextDouble() * 40f;
            float y = (float)rng.NextDouble() * 30f - 15f;
            float z = (float)rng.NextDouble() * 30f - 15f;
            var p = new Vector3(x, y, z);
            Assert.Equal(single.fSignedDistance(p), pattern.fSignedDistance(p), precision: 4);
        }
    }

    /// <summary>
    /// Constructor must reject channelCount &lt; 1 to mirror the
    /// per-channel implementation's clamp behaviour, but explicitly —
    /// a zero N would silently divide by zero in the modulo.
    /// </summary>
    [Fact]
    public void Pattern_ZeroCount_Throws()
    {
        var inner = BarrelContour();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AxialChannelPatternImplicit(
                inner, 1f, 2f, 2f, 2f, 0f, 15f, 30f, 0, 0.7f));
    }
}
