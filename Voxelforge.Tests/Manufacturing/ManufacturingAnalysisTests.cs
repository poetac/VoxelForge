// ManufacturingAnalysisTests.cs — Issue #556 PR-1 (2026-05-16).
//
// Per audit 05-test-gaps.md § 1.7: ManufacturingAnalysis static class +
// ManufacturingReport record were both entirely untested. The class is
// the pre-print LPBF manufacturability check that drives the build-time
// estimate, layer count, and feature-size warnings. ManufacturingReport
// is the read-out record consumed by the UI + report exporter.

using Voxelforge.Chamber;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Manufacturing;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class ManufacturingAnalysisTests
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

    // 24 channels with 0.8 mm ribs around a 4 mm throat keeps the
    // narrowest channel width above the 0.30 mm LPBF floor. 60-channel
    // baselines used elsewhere assume a larger throat radius.
    private static ChannelSchedule DefaultChannels() => new(
        ChannelCount:              24,
        RibThickness_mm:           0.8,
        GasSideWallThickness_mm:   1.0,
        ChannelHeightAtChamber_mm: 2.0,
        ChannelHeightAtThroat_mm:  1.2,
        ChannelHeightAtExit_mm:    2.0);

    private static ChamberGeometryResult DefaultGeom() =>
        ChamberAnalyticalBuilder.BuildAnalytical(new ChamberBuildOptions(
            Contour:                 DefaultContour(),
            Channels:                DefaultChannels(),
            OuterJacketThickness_mm: 2.0,
            MaterialForMass:         WallMaterials.CuCrZr));

    [Fact]
    public void Analyze_PopulatesScalarReportFields()
    {
        var r = ManufacturingAnalysis.Analyze(
            DefaultContour(), DefaultChannels(), DefaultGeom(), WallMaterials.CuCrZr);

        Assert.True(r.EstimatedLayers > 0,
            $"EstimatedLayers={r.EstimatedLayers} should be positive.");
        Assert.True(r.EstimatedBuildHours > 0);
        Assert.True(r.EstimatedBuildCost_USD > 0);
        Assert.True(r.MinFeatureSize_mm > 0);
        Assert.True(r.BuildHeight_mm > 0);
        Assert.True(r.BuildDiameter_mm > 0);
        Assert.NotNull(r.Overhang);
        Assert.NotNull(r.Material.Name);
        Assert.NotNull(r.BuildOrientationRecommendation);
        Assert.NotEmpty(r.Recommendations);
    }

    [Fact]
    public void Analyze_LayerCountFollowsBuildHeightOver30Microns()
    {
        // EstimatedLayers = ceil(BuildHeight / 0.030 mm). Verify the
        // layer count is roughly consistent with the build-height it
        // reports.
        var r = ManufacturingAnalysis.Analyze(
            DefaultContour(), DefaultChannels(), DefaultGeom(), WallMaterials.CuCrZr);

        int expected = (int)System.Math.Ceiling(r.BuildHeight_mm / 0.030);
        Assert.Equal(expected, r.EstimatedLayers);
    }

    [Fact]
    public void Analyze_FeatureSizeOK_TrueForBaselineGeometry()
    {
        // Baseline 0.8 mm rib + 1.0 mm wall stay above the 0.30 mm
        // LPBF floor.
        var r = ManufacturingAnalysis.Analyze(
            DefaultContour(), DefaultChannels(), DefaultGeom(), WallMaterials.CuCrZr);
        Assert.True(r.FeatureSizeOK);
    }

    [Fact]
    public void Analyze_SubLpbfRib_TripsFeatureSizeWarning()
    {
        // Rib at 0.10 mm (below 0.30 mm LPBF floor) must mark
        // FeatureSizeOK = false and emit a warning mentioning the
        // shortfall.
        var channels = DefaultChannels() with { RibThickness_mm = 0.10 };
        var r = ManufacturingAnalysis.Analyze(
            DefaultContour(), channels, DefaultGeom(), WallMaterials.CuCrZr);
        Assert.False(r.FeatureSizeOK);
        Assert.Contains(r.Warnings, w => w.Contains("below LPBF"));
    }

    [Fact]
    public void Analyze_VeryThinGasSideWall_AddsPorosityWarning()
    {
        // 0.30 mm gas-side wall is below the 0.50 mm porosity threshold.
        var channels = DefaultChannels() with { GasSideWallThickness_mm = 0.30 };
        var r = ManufacturingAnalysis.Analyze(
            DefaultContour(), channels, DefaultGeom(), WallMaterials.CuCrZr);
        Assert.Contains(r.Warnings, w => w.Contains("porosity"));
    }

    [Fact]
    public void Analyze_VeryThinRib_AddsCollapseWarning()
    {
        // 0.35 mm rib is between LPBF floor (0.30) and collapse
        // threshold (0.50). Must add the "may collapse" warning even
        // though feature-size check passes.
        var channels = DefaultChannels() with { RibThickness_mm = 0.35 };
        var r = ManufacturingAnalysis.Analyze(
            DefaultContour(), channels, DefaultGeom(), WallMaterials.CuCrZr);
        Assert.Contains(r.Warnings, w => w.Contains("collapse"));
    }

    [Fact]
    public void Analyze_LargeBuildHeight_AddsLargeFormatWarning()
    {
        // Force a tall build via a tall L* — easiest is to feed in a
        // synthetic ChamberGeometryResult with BoundingLength > 400 mm.
        var bigGeom = new ChamberGeometryResult(
            Voxels:                  null!,
            SolidVolume_mm3:         100_000.0,
            InnerSurfaceArea_mm2:    50_000.0,
            OuterJacketThickness_mm: 2.0,
            TotalMass_g:             1000.0,
            PrintedCost_USD:         200.0,
            BoundingLength_mm:       500.0,
            BoundingDiameter_mm:     80.0,
            Description:             "synthetic",
            InjectorSTLMessage:      "");

        var r = ManufacturingAnalysis.Analyze(
            DefaultContour(), DefaultChannels(), bigGeom, WallMaterials.CuCrZr);

        Assert.Contains(r.Warnings, w => w.Contains("400") || w.Contains("envelope"));
    }

    [Fact]
    public void Analyze_LargeBuildDiameter_AddsLargeFormatWarning()
    {
        var bigGeom = new ChamberGeometryResult(
            Voxels:                  null!,
            SolidVolume_mm3:         100_000.0,
            InnerSurfaceArea_mm2:    50_000.0,
            OuterJacketThickness_mm: 2.0,
            TotalMass_g:             1000.0,
            PrintedCost_USD:         200.0,
            BoundingLength_mm:       100.0,
            BoundingDiameter_mm:     400.0,
            Description:             "synthetic",
            InjectorSTLMessage:      "");

        var r = ManufacturingAnalysis.Analyze(
            DefaultContour(), DefaultChannels(), bigGeom, WallMaterials.CuCrZr);

        Assert.Contains(r.Warnings,
            w => w.Contains("build-plate") || w.Contains("diameter"));
    }

    [Fact]
    public void Analyze_CopperAlloy_AddsCopperSpecificRecommendation()
    {
        // CuCrZr Name contains "Cu" so the copper-rec branch fires.
        var r = ManufacturingAnalysis.Analyze(
            DefaultContour(), DefaultChannels(), DefaultGeom(), WallMaterials.CuCrZr);
        Assert.Contains(r.Recommendations,
            rec => rec.Contains("green-laser") || rec.Contains("copper"));
    }

    [Fact]
    public void Analyze_InconelDoesNotTriggerCopperRecommendation()
    {
        // Sanity-check the opposite branch: Inconel must NOT carry the
        // copper-specific recommendation. (Inconel does still pick up
        // generic recommendations.)
        var inconelGeom = ChamberAnalyticalBuilder.BuildAnalytical(new ChamberBuildOptions(
            Contour:                 DefaultContour(),
            Channels:                DefaultChannels(),
            OuterJacketThickness_mm: 2.0,
            MaterialForMass:         WallMaterials.Inconel625));

        var r = ManufacturingAnalysis.Analyze(
            DefaultContour(), DefaultChannels(), inconelGeom, WallMaterials.Inconel625);
        Assert.DoesNotContain(r.Recommendations, rec => rec.Contains("green-laser"));
    }

    [Fact]
    public void Analyze_ReportRoundTrips_MaterialReference()
    {
        var r = ManufacturingAnalysis.Analyze(
            DefaultContour(), DefaultChannels(), DefaultGeom(), WallMaterials.CuCrZr);
        Assert.Equal(WallMaterials.CuCrZr.Name, r.Material.Name);
    }

    [Fact]
    public void Analyze_DesignOverload_WithPlainPort_DoesNotAddPortWarning()
    {
        // Plain port = unthreaded → port-proportionality check returns
        // early. No port-related warning regardless of chamber OD.
        var design = new RegenChamberDesign
        {
            CoolantPortStandard    = PortStandard.Plain,
            PropellantPortStandard = PortStandard.Plain,
        };
        var r = ManufacturingAnalysis.Analyze(
            DefaultContour(), DefaultChannels(), DefaultGeom(),
            WallMaterials.CuCrZr, design);
        Assert.DoesNotContain(r.Warnings, w => w.Contains("port"));
    }

    [Fact]
    public void ManufacturingReport_RecordEquality_HoldsByValue()
    {
        // Two runs over identical inputs must yield equivalent reports.
        // Records' default equality kicks in across all fields.
        var a = ManufacturingAnalysis.Analyze(
            DefaultContour(), DefaultChannels(), DefaultGeom(), WallMaterials.CuCrZr);
        var b = ManufacturingAnalysis.Analyze(
            DefaultContour(), DefaultChannels(), DefaultGeom(), WallMaterials.CuCrZr);

        // Some sub-records carry arrays (Warnings, Recommendations,
        // Overhang.PerStation) where default record-equality compares by
        // reference, not by value. So this checks the salient scalars
        // rather than full record equality.
        Assert.Equal(a.EstimatedLayers, b.EstimatedLayers);
        Assert.Equal(a.EstimatedBuildHours, b.EstimatedBuildHours, precision: 6);
        Assert.Equal(a.MinFeatureSize_mm, b.MinFeatureSize_mm, precision: 6);
        Assert.Equal(a.FeatureSizeOK, b.FeatureSizeOK);
    }
}
