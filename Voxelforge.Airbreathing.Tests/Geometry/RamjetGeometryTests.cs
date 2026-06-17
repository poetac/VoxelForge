// RamjetGeometryTests.cs — Sprint A6 acceptance for RamjetContour
// derivation.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Geometry;

namespace Voxelforge.Airbreathing.Tests.Geometry;

public sealed class RamjetGeometryTests
{
    private static AirbreathingEngineDesign Design()
        => new(
            Kind:                 AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2:   0.10,
            CombustorArea_m2:     0.30,
            CombustorLength_m:    0.50,
            NozzleThroatArea_m2:  0.0848,
            NozzleExitArea_m2:    0.20,
            EquivalenceRatio:     0.40);

    [Fact]
    public void From_RejectsNonRamjetKind()
    {
        var design = Design() with { Kind = AirbreathingEngineKind.Turbojet };
        Assert.Throws<System.ArgumentException>(() => RamjetGeometry.From(design));
    }

    [Fact]
    public void From_RejectsNonPositiveAreas()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => RamjetGeometry.From(Design() with { CombustorArea_m2 = 0.0 }));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => RamjetGeometry.From(Design() with { NozzleThroatArea_m2 = -1.0 }));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => RamjetGeometry.From(Design() with { CombustorLength_m = 0.0 }));
    }

    [Fact]
    public void From_ProducesFiveStations()
    {
        var contour = RamjetGeometry.From(Design());
        Assert.Equal(5, contour.Stations.Length);
    }

    [Fact]
    public void Stations_AreMonotoneInX()
    {
        var contour = RamjetGeometry.From(Design());
        for (int i = 1; i < contour.Stations.Length; i++)
        {
            Assert.True(contour.Stations[i].X_m > contour.Stations[i - 1].X_m,
                $"Station {i} X={contour.Stations[i].X_m} not after station {i - 1} X={contour.Stations[i - 1].X_m}");
        }
    }

    [Fact]
    public void StationAreas_MatchDesignKnobs()
    {
        var design = Design();
        var contour = RamjetGeometry.From(design);
        // Station 0: inlet — area matches InletThroatArea_m2
        Assert.Equal(design.InletThroatArea_m2, contour.Stations[0].Area_m2, 6);
        // Station 1 (diffuser exit) + 2 (combustor exit): match CombustorArea_m2
        Assert.Equal(design.CombustorArea_m2, contour.Stations[1].Area_m2, 6);
        Assert.Equal(design.CombustorArea_m2, contour.Stations[2].Area_m2, 6);
        // Station 3: throat
        Assert.Equal(design.NozzleThroatArea_m2, contour.Stations[3].Area_m2, 6);
        // Station 4: exit
        Assert.Equal(design.NozzleExitArea_m2, contour.Stations[4].Area_m2, 6);
    }

    [Fact]
    public void ThroatIndex_PointsAtSmallestRadius()
    {
        var contour = RamjetGeometry.From(Design());
        Assert.Equal(3, contour.ThroatIndex);
        var throat = contour.ThroatStation;
        for (int i = 0; i < contour.Stations.Length; i++)
        {
            if (i == contour.ThroatIndex) continue;
            Assert.True(contour.Stations[i].R_m >= throat.R_m,
                $"Station {i} R={contour.Stations[i].R_m} smaller than throat R={throat.R_m}");
        }
    }

    [Fact]
    public void TotalLength_MatchesSumOfSectionLengths()
    {
        var design = Design();
        var contour = RamjetGeometry.From(design);
        double expected = design.CombustorLength_m
                        * (RamjetGeometry.DiffuserLengthOverCombustor
                         + 1.0
                         + RamjetGeometry.ConvergentLengthOverCombustor
                         + RamjetGeometry.DivergentLengthOverCombustor);
        Assert.Equal(expected, contour.TotalLength_m, 6);
    }

    [Fact]
    public void ExitStationAccessor_MatchesLastStation()
    {
        var contour = RamjetGeometry.From(Design());
        Assert.Equal(contour.Stations[contour.Stations.Length - 1], contour.ExitStation);
    }

    [Fact]
    public void Deterministic_ProducesIdenticalContourAcrossCalls()
    {
        var d = Design();
        var a = RamjetGeometry.From(d);
        var b = RamjetGeometry.From(d);
        Assert.Equal(a.TotalLength_m, b.TotalLength_m, 12);
        Assert.Equal(a.ThroatIndex,   b.ThroatIndex);
        for (int i = 0; i < a.Stations.Length; i++)
        {
            Assert.Equal(a.Stations[i].X_m, b.Stations[i].X_m, 12);
            Assert.Equal(a.Stations[i].R_m, b.Stations[i].R_m, 12);
            Assert.Equal(a.Stations[i].Section, b.Stations[i].Section);
        }
    }
}
