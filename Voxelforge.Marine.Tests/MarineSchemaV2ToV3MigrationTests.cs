// MarineSchemaV2ToV3MigrationTests.cs — schema migration tests for marine v2 → v3.
//
// v3 adds the 5 planing-specific init-only fields to MarineDesign
// (BeamMidship_m, DeadriseAngle_deg, MassDisplacement_kg, FreeboardHeight_m,
// LongitudinalCgFraction). Identity migration: old AUV designs default the
// new fields to NaN.

using System.IO;
using Voxelforge.Marine;
using Voxelforge.Marine.IO;
using Xunit;

namespace Voxelforge.Marine.Tests;

public sealed class MarineSchemaV2ToV3MigrationTests
{
    private static MarineDesign Remus100AuvV2() => new(
        Kind:                MarineKind.AuvMidBody,
        Length_m:            1.595,
        Diameter_m:          0.190,
        NoseFairingFraction: 0.18,
        TailFairingFraction: 0.22,
        WallThickness_m:     0.005,
        MaterialIndex:       1,
        DepthRating_m:       100.0,
        HullFamily:          HullFamily.Myring);

    private static MarineConditions Remus100Conditions() => new(
        CruiseSpeed_ms: 1.5,
        MaxDepth_m:     100.0);

    private static MarineDesign PlaningYachtV3() => new(
        Kind:                MarineKind.SurfaceHull,
        Length_m:           11.0,
        Diameter_m:          1.0,
        NoseFairingFraction: 0.25,
        TailFairingFraction: 0.25,
        WallThickness_m:     0.005,
        MaterialIndex:       0,
        DepthRating_m:       1.0,
        HullFamily:          HullFamily.Planing)
    {
        BeamMidship_m          = 3.0,
        DeadriseAngle_deg      = 18.0,
        MassDisplacement_kg    = 5000.0,
        FreeboardHeight_m      = 0.6,
        LongitudinalCgFraction = 0.50,
    };

    private static MarineConditions PlaningConditions() => new(
        CruiseSpeed_ms: 12.86,
        MaxDepth_m:      0.0);

    [Fact]
    public void Auv_RoundTrips_AcrossV3()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_marine_v3_auv_{Path.GetRandomFileName()}.json");
        try
        {
            MarineDesignPersistence.SaveJson(Remus100AuvV2(), Remus100Conditions(), path);
            var (loaded, _) = MarineDesignPersistence.LoadJson(path);
            Assert.Equal(MarineKind.AuvMidBody, loaded.Kind);
            Assert.Equal(HullFamily.Myring, loaded.HullFamily);
            // Planing fields default to NaN on an AUV round-trip.
            Assert.True(double.IsNaN(loaded.BeamMidship_m));
            Assert.True(double.IsNaN(loaded.DeadriseAngle_deg));
            Assert.True(double.IsNaN(loaded.MassDisplacement_kg));
            Assert.True(double.IsNaN(loaded.FreeboardHeight_m));
            Assert.True(double.IsNaN(loaded.LongitudinalCgFraction));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Planing_RoundTrips_AcrossV3()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_marine_v3_planing_{Path.GetRandomFileName()}.json");
        try
        {
            MarineDesignPersistence.SaveJson(PlaningYachtV3(), PlaningConditions(), path);
            var (loaded, _) = MarineDesignPersistence.LoadJson(path);
            Assert.Equal(MarineKind.SurfaceHull, loaded.Kind);
            Assert.Equal(HullFamily.Planing, loaded.HullFamily);
            Assert.Equal( 3.0, loaded.BeamMidship_m,          precision: 6);
            Assert.Equal(18.0, loaded.DeadriseAngle_deg,      precision: 6);
            Assert.Equal(5000.0, loaded.MassDisplacement_kg,  precision: 6);
            Assert.Equal( 0.6, loaded.FreeboardHeight_m,      precision: 6);
            Assert.Equal( 0.50, loaded.LongitudinalCgFraction, precision: 6);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void V1AuvJson_LoadsAsV3_ChainedThroughV2()
    {
        // v1 → v2 → v3 chained migration: a Wave-1 v1 file deserialises with
        // HullFamily defaulted to Myring AND planing fields all NaN.
        const string v1Json = """
            {
              "Schema": "v1",
              "Version": "1.0",
              "CreatedUtc": "2026-01-01T00:00:00.0000000Z",
              "AppName": "Voxelforge.Marine",
              "Conditions": {
                "CruiseSpeed_ms": 1.5,
                "MaxDepth_m": 100.0,
                "WaterTemperature_K": 277.15,
                "Salinity_ppt": 35.0
              },
              "Design": {
                "Kind": "AuvMidBody",
                "Length_m": 1.595,
                "Diameter_m": 0.190,
                "NoseFairingFraction": 0.18,
                "TailFairingFraction": 0.22,
                "WallThickness_m": 0.005,
                "MaterialIndex": 1,
                "DepthRating_m": 100.0
              }
            }
            """;
        var path = Path.Combine(Path.GetTempPath(), $"vxf_marine_v1_chain_{Path.GetRandomFileName()}.json");
        try
        {
            File.WriteAllText(path, v1Json);
            var (loaded, _) = MarineDesignPersistence.LoadJson(path);
            Assert.Equal(MarineKind.AuvMidBody, loaded.Kind);
            Assert.Equal(HullFamily.Myring, loaded.HullFamily);
            Assert.True(double.IsNaN(loaded.BeamMidship_m));
            Assert.True(double.IsNaN(loaded.DeadriseAngle_deg));
            Assert.True(double.IsNaN(loaded.LongitudinalCgFraction));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void UnsupportedNewerSchema_Throws()
    {
        const string newerJson = """
            {
              "Schema": "v999",
              "Version": "1.0",
              "AppName": "Voxelforge.Marine"
            }
            """;
        var path = Path.Combine(Path.GetTempPath(), $"vxf_marine_v999_{Path.GetRandomFileName()}.json");
        try
        {
            File.WriteAllText(path, newerJson);
            Assert.Throws<UnsupportedMarineSchemaException>(
                () => MarineDesignPersistence.LoadJson(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
