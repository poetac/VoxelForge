// HexArrayGeometryTests.cs — Sprint NU.W2 unit tests for HexArrayGeometry.

using System;
using Voxelforge.Nuclear.FuelPin;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class HexArrayGeometryTests
{
    // ── PinCountForRings ─────────────────────────────────────────────────

    [Fact]
    public void PinCountForRings_ZeroRings_ReturnsOne()
        => Assert.Equal(1, HexArrayGeometry.PinCountForRings(0));

    [Fact]
    public void PinCountForRings_FirstRing_ReturnsSeven()
        => Assert.Equal(7, HexArrayGeometry.PinCountForRings(1));

    [Fact]
    public void PinCountForRings_SecondRing_ReturnsNineteen()
        => Assert.Equal(19, HexArrayGeometry.PinCountForRings(2));

    [Fact]
    public void PinCountForRings_ThirdRing_ReturnsThirtySeven()
        => Assert.Equal(37, HexArrayGeometry.PinCountForRings(3));

    [Fact]
    public void PinCountForRings_NegativeRings_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => HexArrayGeometry.PinCountForRings(-1));

    // ── ElementOuterFlatMm ──────────────────────────────────────────────

    [Fact]
    public void ElementOuterFlatMm_TwoRingsThreePointTwoPitch_ReturnsEightMm()
    {
        // 2 · (2 + 0.5) · 3.2 = 16 mm
        Assert.Equal(16.0, HexArrayGeometry.ElementOuterFlatMm(2, 3.2), precision: 6);
    }

    [Fact]
    public void ElementOuterFlatMm_NonPositivePitch_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => HexArrayGeometry.ElementOuterFlatMm(2, 0.0));

    // ── TriangularSubChannelDh_mm ───────────────────────────────────────

    [Fact]
    public void TriangularSubChannelDh_KnownGeometry_MatchesClosedForm()
    {
        // pitch=3.2, diameter=2.5:
        //   A_flow = (√3/4)·3.2² − (π/8)·2.5² = 4.434 − 2.454 = 1.980
        //   P_wet  = π·2.5/2 = 3.927
        //   D_h    = 4·1.980 / 3.927 = 2.017
        double dh = HexArrayGeometry.TriangularSubChannelDh_mm(3.2, 2.5);
        Assert.InRange(dh, 2.0, 2.05);
    }

    [Fact]
    public void TriangularSubChannelDh_GrowsWithPitch_AtFixedDiameter()
    {
        double dhTight = HexArrayGeometry.TriangularSubChannelDh_mm(3.0, 2.5);
        double dhWide  = HexArrayGeometry.TriangularSubChannelDh_mm(4.0, 2.5);
        Assert.True(dhWide > dhTight);
    }

    [Fact]
    public void TriangularSubChannelDh_PitchEqualToDiameter_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => HexArrayGeometry.TriangularSubChannelDh_mm(2.5, 2.5));

    // ── FuelVolumeFractionFor ───────────────────────────────────────────

    [Fact]
    public void FuelVolumeFractionFor_KnownGeometry_ReturnsExpected()
    {
        // 19 pins, d=2.5, hex flat=16:
        //   A_fuel = 19 · π/4 · 2.5² = 93.27
        //   A_hex  = (√3/2) · 16² = 221.7
        //   F = 93.27 / 221.7 = 0.421
        double f = HexArrayGeometry.FuelVolumeFractionFor(19, 2.5, 16.0);
        Assert.InRange(f, 0.40, 0.44);
    }

    [Fact]
    public void FuelVolumeFractionFor_FuelFractionPlusCoolantSumsToOne_AfterResolve()
    {
        var g = HexArrayGeometry.Resolve(hexRings: 2, pinDiameter_mm: 2.5, pinPitch_mm: 3.2);
        Assert.Equal(1.0, g.FuelVolumeFraction + g.CoolantVolumeFraction, precision: 6);
    }

    [Fact]
    public void FuelVolumeFractionFor_NonPositivePinCount_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => HexArrayGeometry.FuelVolumeFractionFor(0, 2.5, 16.0));

    // ── Resolve ─────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NrxA6LikeGeometry_HasNineteenPins()
    {
        var g = HexArrayGeometry.Resolve(hexRings: 2, pinDiameter_mm: 2.5, pinPitch_mm: 3.2);
        Assert.Equal(19, g.PinCount);
        Assert.Equal(2, g.HexRings);
    }

    [Fact]
    public void Resolve_NrxA6LikeGeometry_FieldsConsistent()
    {
        var g = HexArrayGeometry.Resolve(hexRings: 2, pinDiameter_mm: 2.5, pinPitch_mm: 3.2);
        Assert.Equal(2.5, g.PinDiameter_mm, precision: 6);
        Assert.Equal(3.2, g.PinPitch_mm, precision: 6);
        // Element flat = 2 · (rings + 0.5) · pitch = 16 mm
        Assert.Equal(16.0, g.ElementOuterFlat_mm, precision: 6);
        Assert.True(g.ChannelHydraulicDiameter_mm > 0);
        Assert.True(g.FuelVolumeFraction is > 0.0 and < 1.0);
    }
}
