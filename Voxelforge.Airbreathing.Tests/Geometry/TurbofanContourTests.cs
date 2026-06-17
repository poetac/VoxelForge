// TurbofanContourTests.cs — coverage for the turbofan axisymmetric
// contour-derivation. Audit 05-test-gaps.md Section 2 High: all three
// public types (TurbofanCoreStation, TurbofanContour, TurbofanGeometry)
// previously had zero references in test code.

using System;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Geometry;

namespace Voxelforge.Airbreathing.Tests.Geometry;

public sealed class TurbofanContourTests
{
    private static AirbreathingEngineDesign F404LikeDesign(double bpr = 0.34) =>
        new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Turbofan,
            InletThroatArea_m2:      0.40,
            CombustorArea_m2:        0.25,
            CombustorLength_m:       0.50,
            NozzleThroatArea_m2:     0.18,
            NozzleExitArea_m2:       0.30,
            EquivalenceRatio:        0.80,
            CompressorPressureRatio: 25.0,
            BypassRatio:             bpr);

    [Fact]
    public void From_F404Like_ProducesFiveCoreStations()
    {
        var contour = TurbofanGeometry.From(F404LikeDesign());
        Assert.Equal(5, contour.CoreStations.Length);
    }

    [Fact]
    public void From_F404Like_BypassRadiiArrayLengthMatchesCore()
    {
        var contour = TurbofanGeometry.From(F404LikeDesign());
        Assert.Equal(contour.CoreStations.Length, contour.BypassOuterRadii_m.Length);
    }

    [Fact]
    public void From_F404Like_StationsMonotoneInX()
    {
        var contour = TurbofanGeometry.From(F404LikeDesign());
        for (int i = 1; i < contour.CoreStations.Length; i++)
        {
            Assert.True(contour.CoreStations[i].X_m >= contour.CoreStations[i - 1].X_m,
                $"Stations not monotone: x[{i-1}]={contour.CoreStations[i-1].X_m}, " +
                $"x[{i}]={contour.CoreStations[i].X_m}");
        }
    }

    [Fact]
    public void From_F404Like_SectionEnumOrderingFollowsCanonicalLayout()
    {
        var contour = TurbofanGeometry.From(F404LikeDesign());
        Assert.Equal(TurbofanCoreSection.Inlet,            contour.CoreStations[0].Section);
        Assert.Equal(TurbofanCoreSection.FanFace,          contour.CoreStations[1].Section);
        Assert.Equal(TurbofanCoreSection.CompressorExit,   contour.CoreStations[2].Section);
        Assert.Equal(TurbofanCoreSection.CoreNozzleThroat, contour.CoreStations[3].Section);
        Assert.Equal(TurbofanCoreSection.CoreExit,         contour.CoreStations[4].Section);
    }

    [Fact]
    public void From_F404Like_ThroatIndexPointsToSmallestRadiusStation()
    {
        var contour = TurbofanGeometry.From(F404LikeDesign());
        double rThroat = contour.CoreThroatStation.R_m;
        foreach (var s in contour.CoreStations)
            Assert.True(s.R_m >= rThroat,
                $"Found station r={s.R_m} below claimed throat r={rThroat}");
    }

    [Fact]
    public void From_F404Like_TotalLengthPositive()
    {
        var contour = TurbofanGeometry.From(F404LikeDesign());
        Assert.True(contour.TotalLength_m > 0);
        Assert.Equal(contour.CoreStations[^1].X_m, contour.TotalLength_m, precision: 9);
    }

    [Fact]
    public void From_BypassZero_BypassRadiusEqualsCoreRadius()
    {
        // BPR = 0 degenerates to turbojet limit: r_outer = r_core at every station.
        var contour = TurbofanGeometry.From(F404LikeDesign(bpr: 0.0));
        for (int i = 0; i < contour.CoreStations.Length; i++)
        {
            Assert.Equal(contour.CoreStations[i].R_m,
                         contour.BypassOuterRadii_m[i],
                         precision: 9);
        }
    }

    [Fact]
    public void From_BypassPositive_BypassRadiusExceedsCoreRadius()
    {
        // For BPR > 0: r_outer = √(r_core² · (1 + BPR)) > r_core.
        var contour = TurbofanGeometry.From(F404LikeDesign(bpr: 1.0));
        for (int i = 0; i < contour.CoreStations.Length; i++)
        {
            Assert.True(contour.BypassOuterRadii_m[i] > contour.CoreStations[i].R_m,
                $"Bypass radius {contour.BypassOuterRadii_m[i]} should exceed core radius " +
                $"{contour.CoreStations[i].R_m} at station {i}");
        }
    }

    [Fact]
    public void From_BypassRatioFormula_HoldsAtFanFace()
    {
        // At every station: r_bypass_outer = √(r_core² · (1 + BPR)).
        const double bpr = 0.50;
        var contour = TurbofanGeometry.From(F404LikeDesign(bpr: bpr));
        for (int i = 0; i < contour.CoreStations.Length; i++)
        {
            double r = contour.CoreStations[i].R_m;
            double expected = Math.Sqrt(r * r * (1.0 + bpr));
            Assert.Equal(expected, contour.BypassOuterRadii_m[i], precision: 9);
        }
    }

    [Fact]
    public void From_NonTurbofanKind_ThrowsArgumentException()
    {
        var ramjet = new AirbreathingEngineDesign(
            AirbreathingEngineKind.Ramjet,
            0.40, 0.25, 0.50, 0.18, 0.30, 0.80);
        Assert.Throws<ArgumentException>(() => TurbofanGeometry.From(ramjet));
    }

    [Fact]
    public void From_NullDesign_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TurbofanGeometry.From(null!));
    }

    [Fact]
    public void From_NaNCombustorArea_ThrowsArgumentOutOfRange()
    {
        var design = F404LikeDesign() with { CombustorArea_m2 = double.NaN };
        Assert.Throws<ArgumentOutOfRangeException>(() => TurbofanGeometry.From(design));
    }

    [Fact]
    public void From_ZeroNozzleThroatArea_ThrowsArgumentOutOfRange()
    {
        var design = F404LikeDesign() with { NozzleThroatArea_m2 = 0.0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => TurbofanGeometry.From(design));
    }

    [Fact]
    public void From_NegativeBypassRatio_ThrowsArgumentOutOfRange()
    {
        var design = F404LikeDesign(bpr: -0.10);
        Assert.Throws<ArgumentOutOfRangeException>(() => TurbofanGeometry.From(design));
    }

    [Fact]
    public void CoreStation_Area_EqualsPiRSquared()
    {
        var s = new TurbofanCoreStation(X_m: 0.5, R_m: 0.30, TurbofanCoreSection.FanFace);
        Assert.Equal(Math.PI * 0.30 * 0.30, s.Area_m2, precision: 9);
    }

    [Fact]
    public void CoreExitStation_IsLastInArray()
    {
        var contour = TurbofanGeometry.From(F404LikeDesign());
        Assert.Equal(contour.CoreStations[^1], contour.CoreExitStation);
    }
}
