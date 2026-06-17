// ChannelFilletTests.cs — Ensure the manifold-end fillet on AxialChannel
// widens the circumferential extent near the axial ends without changing
// the channel core, and that zero fillet reproduces the old sharp-ended
// behaviour byte-for-byte.

using System.Numerics;
using PicoGK;
using Voxelforge.Chamber;
using Voxelforge.Geometry;

namespace Voxelforge.Tests;

public class ChannelFilletTests
{
    private static RevolvedContourImplicit BuildInnerContour()
    {
        // Straight barrel R = 10 mm from x = 0 to x = 30 mm.
        return new RevolvedContourImplicit(new[]
        {
            (0.0, 10.0), (15.0, 10.0), (30.0, 10.0)
        });
    }

    [Fact]
    public void ZeroFilletRadius_ReproducesSharpEnds()
    {
        var inner = BuildInnerContour();
        var sharp = new AxialChannelImplicit(
            inner, tWall_mm: 1.0f, hChamber: 2.0f, hThroat: 2.0f, hExit: 2.0f,
            xStart_mm: 5f, xThroat_mm: 15f, xEnd_mm: 25f,
            channelCount: 40, ribThickness_mm: 0.8f, thetaCenterRad: 0f);
        var filletedZero = new AxialChannelImplicit(
            inner, tWall_mm: 1.0f, hChamber: 2.0f, hThroat: 2.0f, hExit: 2.0f,
            xStart_mm: 5f, xThroat_mm: 15f, xEnd_mm: 25f,
            channelCount: 40, ribThickness_mm: 0.8f, thetaCenterRad: 0f,
            manifoldFilletRadius_mm: 0f);

        // Sample at many points; SDFs must be identical.
        for (int i = 0; i < 20; i++)
        {
            float x = 4f + i * 1.1f;
            var p = new Vector3(x, 11.5f, 0.3f);
            Assert.Equal(sharp.fSignedDistance(p), filletedZero.fSignedDistance(p), precision: 3);
        }
    }

    [Fact]
    public void Fillet_WidensChannelNearEnd()
    {
        var inner = BuildInnerContour();
        var sharp = new AxialChannelImplicit(
            inner, 1.0f, 2f, 2f, 2f, 5f, 15f, 25f, 40, 0.8f, 0f, 0f);
        var filleted = new AxialChannelImplicit(
            inner, 1.0f, 2f, 2f, 2f, 5f, 15f, 25f, 40, 0.8f, 0f, 1.5f);

        // At x close to the channel end (x = 24.8, axial margin 0.2 < fillet),
        // points near the rib center (thetaCenter = 0; at y, z near the edge
        // of the nominal w) should be INSIDE in the filleted version but
        // OUTSIDE (or closer to outside) in the sharp version.
        float rMid = 12f;   // t_wall=1 + h/2=1 above inner R=10
        float pitch = 2f * MathF.PI * rMid / 40;   // ≈ 1.885
        float wNom = pitch - 0.8f;                  // ≈ 1.085
        // A point at theta halfway between w/2 and pitch/2 (in the rib region).
        float theta = 0.5f * (0.5f * wNom / rMid + 0.5f * pitch / rMid);
        var p = new Vector3(24.8f, rMid * MathF.Cos(theta), rMid * MathF.Sin(theta));

        float dSharp = sharp.fSignedDistance(p);
        float dFill = filleted.fSignedDistance(p);
        // Filleted should have the channel WIDER at this x, pulling the
        // test point inside (SDF more negative or less positive).
        Assert.True(dFill < dSharp,
            $"Fillet should include more volume near ends (sharp={dSharp:F3}, filleted={dFill:F3})");
    }

    [Fact]
    public void Fillet_DoesNotChangeChannelMidsection()
    {
        var inner = BuildInnerContour();
        var sharp = new AxialChannelImplicit(
            inner, 1.0f, 2f, 2f, 2f, 5f, 15f, 25f, 40, 0.8f, 0f, 0f);
        var filleted = new AxialChannelImplicit(
            inner, 1.0f, 2f, 2f, 2f, 5f, 15f, 25f, 40, 0.8f, 0f, 1.5f);

        // Well in the middle (x = 15, far from both ends by > fillet radius)
        // the SDFs must agree.
        var p = new Vector3(15f, 11.5f, 0.0f);
        Assert.Equal(sharp.fSignedDistance(p), filleted.fSignedDistance(p), precision: 3);
    }

    [Fact]
    public void Fillet_OutsidePointsStillOutside()
    {
        var inner = BuildInnerContour();
        var filleted = new AxialChannelImplicit(
            inner, 1.0f, 2f, 2f, 2f, 5f, 15f, 25f, 40, 0.8f, 0f, 1.5f);
        // A point well beyond the axial extent must still read positive SDF.
        var p = new Vector3(30f, 11.5f, 0.0f);
        Assert.True(filleted.fSignedDistance(p) > 0);
    }
}
