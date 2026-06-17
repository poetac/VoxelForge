// ChamberAnalyticalBuilderTests.cs — Issue #556 PR-1 (2026-05-16).
//
// Per audit 05-test-gaps.md § 1.2: ChamberAnalyticalBuilder is the
// pure-math (no PicoGK) chamber mass / cost / bounding-box estimator
// used by the bench-SA and voxelforge-eval paths. It is exercised
// transitively via the regen-chamber pipeline but never directly tested.
// This regression guard fixes that.

using Voxelforge.Chamber;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Tests;

public class ChamberAnalyticalBuilderTests
{
    private static ChamberContour DefaultContour() =>
        ChamberContourGenerator.Generate(
            throatRadius_mm:        4.0,
            contractionRatio:       6.0,
            expansionRatio:         8.0,
            characteristicLength_m: 1.1,
            thetaN_deg:             30.0,
            thetaE_deg:             10.0,
            bellLengthFraction:     0.8,
            stationCount:           120);

    private static ChannelSchedule DefaultChannels() => new(
        ChannelCount:               60,
        RibThickness_mm:            0.8,
        GasSideWallThickness_mm:    1.0,
        ChannelHeightAtChamber_mm:  2.0,
        ChannelHeightAtThroat_mm:   1.2,
        ChannelHeightAtExit_mm:     2.0);

    private static ChamberBuildOptions DefaultOptions() => new(
        Contour:                 DefaultContour(),
        Channels:                DefaultChannels(),
        OuterJacketThickness_mm: 2.0,
        MaterialForMass:         WallMaterials.CuCrZr);

    [Fact]
    public void BuildAnalytical_PopulatesPositiveScalarOutputs()
    {
        var r = ChamberAnalyticalBuilder.BuildAnalytical(DefaultOptions());

        // Voxels is intentionally null on the physics-only path (the
        // record reserves that field for the PicoGK-backed builder).
        Assert.Null(r.Voxels);

        Assert.True(r.SolidVolume_mm3 > 0, $"SolidVolume_mm3 = {r.SolidVolume_mm3}");
        Assert.True(r.TotalMass_g > 0, $"TotalMass_g = {r.TotalMass_g}");
        Assert.True(r.PrintedCost_USD > 0, $"PrintedCost_USD = {r.PrintedCost_USD}");
        Assert.True(r.InnerSurfaceArea_mm2 > 0);
        Assert.True(r.BoundingLength_mm > 0);
        Assert.True(r.BoundingDiameter_mm > 0);
    }

    [Fact]
    public void BuildAnalytical_BoundingLengthMatchesContourTotalLength()
    {
        var opt = DefaultOptions();
        var r = ChamberAnalyticalBuilder.BuildAnalytical(opt);
        Assert.Equal(opt.Contour.TotalLength_mm, r.BoundingLength_mm, precision: 6);
    }

    [Fact]
    public void BuildAnalytical_BoundingDiameterIncludesWallChannelJacket()
    {
        var opt = DefaultOptions();
        var r = ChamberAnalyticalBuilder.BuildAnalytical(opt);

        // Bounding diameter = 2·(R_exit + t_wall + h_ch_exit + t_jacket).
        double expected = 2.0 * (opt.Contour.Stations[^1].R_mm
                              + opt.Channels.GasSideWallThickness_mm
                              + opt.Channels.ChannelHeightAtExit_mm
                              + opt.OuterJacketThickness_mm);
        Assert.Equal(expected, r.BoundingDiameter_mm, precision: 4);
    }

    [Fact]
    public void BuildAnalytical_DescriptionContainsSentinelStubTag()
    {
        // The analytical path stamps "Physics-only stub" so downstream
        // report code can distinguish from the voxel-built description.
        var r = ChamberAnalyticalBuilder.BuildAnalytical(DefaultOptions());
        Assert.Contains("Physics-only stub", r.Description);
    }

    [Fact]
    public void BuildAnalytical_SkipChannelGeneration_ShrinksOuterEnvelope()
    {
        // Ablative-only builds (SkipChannelGeneration=true) zero the
        // channel-height contribution to the outer envelope. The
        // bounding diameter must drop by 2·channelHeightAtExit relative
        // to the channel-bearing build.
        var withCh    = DefaultOptions();
        var withoutCh = withCh with { SkipChannelGeneration = true };

        var a = ChamberAnalyticalBuilder.BuildAnalytical(withCh);
        var b = ChamberAnalyticalBuilder.BuildAnalytical(withoutCh);

        double delta = a.BoundingDiameter_mm - b.BoundingDiameter_mm;
        Assert.Equal(2.0 * withCh.Channels.ChannelHeightAtExit_mm, delta, precision: 4);
    }

    [Fact]
    public void BuildAnalytical_MaterialDefaultsToCuCrZrWhenNull()
    {
        // MaterialForMass null → CuCrZr default. Mass should equal the
        // explicit-CuCrZr build to within 6 sig figs.
        var withNull = DefaultOptions() with { MaterialForMass = null };
        var withCu   = DefaultOptions() with { MaterialForMass = WallMaterials.CuCrZr };

        var a = ChamberAnalyticalBuilder.BuildAnalytical(withNull);
        var b = ChamberAnalyticalBuilder.BuildAnalytical(withCu);

        Assert.Equal(a.TotalMass_g, b.TotalMass_g, precision: 6);
        Assert.Equal(a.PrintedCost_USD, b.PrintedCost_USD, precision: 6);
    }

    [Fact]
    public void BuildAnalytical_DenserMaterial_ProducesHigherMass()
    {
        var copper  = DefaultOptions() with { MaterialForMass = WallMaterials.CuCrZr };
        var inconel = DefaultOptions() with { MaterialForMass = WallMaterials.Inconel625 };

        var mCu  = ChamberAnalyticalBuilder.BuildAnalytical(copper).TotalMass_g;
        var mIn  = ChamberAnalyticalBuilder.BuildAnalytical(inconel).TotalMass_g;

        // The volumes are identical (geometry is material-independent on
        // the analytical path); only the density factor differs. CuCrZr
        // (~8900 kg/m³) is slightly denser than Inconel 625 (~8440),
        // so the Cu mass must exceed the Inconel mass.
        Assert.True(mCu > mIn,
            $"CuCrZr mass {mCu:F1} g should exceed Inconel-625 mass {mIn:F1} g " +
            "(same geometry, higher density).");
    }
}
