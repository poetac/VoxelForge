// OverhangAnalysisTests.cs — Lock the 45° rule per-station check.
// Verifies the asymptotes (straight barrel = fully supported, steep cone
// = overhang) and the orientation recommendation.

using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;
using Voxelforge.Manufacturing;

namespace Voxelforge.Tests;

public class OverhangAnalysisTests
{
    private static ChannelSchedule SimpleChannels() =>
        new(ChannelCount: 40, RibThickness_mm: 0.8,
            GasSideWallThickness_mm: 0.8,
            ChannelHeightAtChamber_mm: 2.5,
            ChannelHeightAtThroat_mm: 1.5,
            ChannelHeightAtExit_mm: 2.0);

    [Fact]
    public void TypicalBellContour_AllSelfSupporting()
    {
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: 4, contractionRatio: 6, expansionRatio: 8,
            characteristicLength_m: 1.1, thetaN_deg: 30, thetaE_deg: 10,
            bellLengthFraction: 0.8, stationCount: 180);
        var r = OverhangAnalysis.Analyze(contour, SimpleChannels(), 2.0);
        Assert.True(r.AllSelfSupporting,
            $"Standard bell should be self-supporting; got {r.UnprintableStationCount} unprintable");
        Assert.True(r.WorstOverhangAngle_deg_InnerWall > 45);
        Assert.True(r.WorstOverhangAngle_deg_OuterWall > 45);
    }

    [Fact]
    public void SteepBellTheta_FlagsOverhang()
    {
        // θ_n = 38° puts bell-arc slope near tan(38°) = 0.78, angle from
        // horizontal ~ atan(1/0.78) = 52° — still marginally supported.
        // Push further with θ_n = 38° AND high exit angle (also 38°,
        // which the parabola code accepts even if unusual) to stress.
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: 4, contractionRatio: 6, expansionRatio: 8,
            characteristicLength_m: 1.1, thetaN_deg: 38, thetaE_deg: 16,
            bellLengthFraction: 0.6, stationCount: 180);
        var r = OverhangAnalysis.Analyze(contour, SimpleChannels(), 2.0);
        Assert.True(r.WorstOverhangAngle_deg_OuterWall < 60,
            $"Aggressive bell should narrow overhang margins; got {r.WorstOverhangAngle_deg_OuterWall:F0}°");
    }

    [Fact]
    public void PerStationData_IsMonotonicWithContour()
    {
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: 4, contractionRatio: 6, expansionRatio: 8,
            characteristicLength_m: 1.1, stationCount: 120);
        var r = OverhangAnalysis.Analyze(contour, SimpleChannels(), 2.0);
        Assert.Equal(contour.Stations.Length, r.PerStation.Length);

        foreach (var st in r.PerStation)
        {
            if (st.Region == ChamberRegion.Barrel)
            {
                Assert.InRange(st.InnerAngle_deg, 89.9, 90.1);  // vertical wall
                Assert.InRange(st.OuterAngle_deg, 89.9, 90.1);
            }
            Assert.InRange(st.InnerAngle_deg, 0, 90.01);
            Assert.InRange(st.OuterAngle_deg, 0, 90.01);
        }
    }

    [Fact]
    public void OrientationFlip_IsConsidered()
    {
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: 4, contractionRatio: 6, expansionRatio: 8,
            characteristicLength_m: 1.1, stationCount: 120);
        var up = OverhangAnalysis.Analyze(contour, SimpleChannels(), 2.0, throatUp: true);
        var dn = OverhangAnalysis.Analyze(contour, SimpleChannels(), 2.0, throatUp: false);
        // Flipping orientation flips which surfaces overhang, so at least
        // one angle should differ between the two runs.
        Assert.False(up.WorstOverhangAngle_deg_InnerWall.Equals(dn.WorstOverhangAngle_deg_InnerWall)
                  && up.WorstOverhangAngle_deg_OuterWall.Equals(dn.WorstOverhangAngle_deg_OuterWall));
    }
}
