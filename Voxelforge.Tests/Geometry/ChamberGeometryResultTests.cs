// ChamberGeometryResultTests.cs — Issue #556 PR-1 (2026-05-16).
//
// Per audit 05-test-gaps.md § 1.2: pure-data record carrying the chamber
// builder's scalar outputs. Covered transitively by ChamberAnalyticalBuilder
// tests, but the record itself had no direct ctor / equality assertions.

using Voxelforge.Geometry;

namespace Voxelforge.Tests;

public class ChamberGeometryResultTests
{
    private static ChamberGeometryResult Sample(
        double mass_g = 250.0,
        BuildProfile? profile = null) =>
        new(
            Voxels:                  null!,
            SolidVolume_mm3:         28_000.0,
            InnerSurfaceArea_mm2:    18_500.0,
            OuterJacketThickness_mm: 2.0,
            TotalMass_g:             mass_g,
            PrintedCost_USD:         52.5,
            BoundingLength_mm:       180.0,
            BoundingDiameter_mm:     45.0,
            Description:             "test stub",
            InjectorSTLMessage:      "",
            Profile:                 profile);

    [Fact]
    public void Ctor_StoresAllFieldsVerbatim()
    {
        var r = Sample();
        Assert.Equal(28_000.0, r.SolidVolume_mm3, precision: 6);
        Assert.Equal(180.0, r.BoundingLength_mm, precision: 6);
        Assert.Equal(45.0, r.BoundingDiameter_mm, precision: 6);
        Assert.Equal(52.5, r.PrintedCost_USD, precision: 6);
        Assert.Equal("test stub", r.Description);
        Assert.Null(r.Profile);
    }

    [Fact]
    public void Ctor_DefaultsInjectorStlMessageToEmpty()
    {
        var r = new ChamberGeometryResult(
            Voxels:                  null!,
            SolidVolume_mm3:         1.0,
            InnerSurfaceArea_mm2:    1.0,
            OuterJacketThickness_mm: 1.0,
            TotalMass_g:             1.0,
            PrintedCost_USD:         1.0,
            BoundingLength_mm:       1.0,
            BoundingDiameter_mm:     1.0,
            Description:             "d");
        Assert.Equal(string.Empty, r.InjectorSTLMessage);
        Assert.Null(r.Profile);
    }

    [Fact]
    public void RecordEquality_DistinguishesByMass()
    {
        var a = Sample(mass_g: 100.0);
        var b = Sample(mass_g: 200.0);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WithExpression_KeepsUnchangedFields()
    {
        var a = Sample();
        var b = a with { TotalMass_g = 999.0 };

        Assert.Equal(999.0, b.TotalMass_g, precision: 6);
        Assert.Equal(a.SolidVolume_mm3, b.SolidVolume_mm3, precision: 6);
        Assert.Equal(a.Description, b.Description);
    }

    [Fact]
    public void Profile_RoundTripsThroughCtor()
    {
        var profile = new BuildProfile(
            Shell_ms: 1, Channels_ms: 2, ChannelVoxelise_ms: 3,
            ChannelBoolSubtract_ms: 4, ChannelCount: 5, Manifolds_ms: 6,
            RadialPorts_ms: 7, Smoothen_ms: 8, InjectorFlange_ms: 9,
            MountingFlange_ms: 10, InjectorBores_ms: 11, LateFeatures_ms: 12,
            FinalMeasurements_ms: 13, Total_ms: 14, VoxelSize_mm: 0.4,
            BBoxLx_mm: 100f, BBoxLy_mm: 50f, BBoxLz_mm: 50f,
            DenseEquivalentVoxels: 1_000_000);

        var r = Sample(profile: profile);
        Assert.NotNull(r.Profile);
        Assert.Equal(5, r.Profile!.ChannelCount);
    }
}
