// MarineSchemaV1ToV2MigrationTests.cs — schema migration tests for marine v1 → v2.
//
// v2 adds HullFamily to MarineDesign. Old v1 files should migrate transparently,
// defaulting to HullFamily.Myring.

using System.IO;
using System.Text.Json;
using Voxelforge.Marine;
using Voxelforge.Marine.IO;
using Xunit;

namespace Voxelforge.Marine.Tests;

public sealed class MarineSchemaV1ToV2MigrationTests
{
    // Minimal valid v1 JSON envelope (HullFamily field absent).
    private static string MakeV1Json() => """
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

    [Fact]
    public void LoadJson_V1File_MigratesHullFamilyToMyring()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, MakeV1Json());
            var (design, _) = MarineDesignPersistence.LoadJson(tmp);
            Assert.Equal(HullFamily.Myring, design.HullFamily);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void LoadJson_V1File_PreservesAllOtherFields()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, MakeV1Json());
            var (design, cond) = MarineDesignPersistence.LoadJson(tmp);
            Assert.Equal(1.595,             design.Length_m,    precision: 6);
            Assert.Equal(0.190,             design.Diameter_m,  precision: 6);
            Assert.Equal(MarineKind.AuvMidBody, design.Kind);
            Assert.Equal(1.5,  cond.CruiseSpeed_ms, precision: 6);
            Assert.Equal(100.0, cond.MaxDepth_m,    precision: 6);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void SaveLoadRoundTrip_V2_PreservesHullFamily()
    {
        var design = new MarineDesign(
            Kind: MarineKind.AuvMidBody,
            Length_m: 1.595, Diameter_m: 0.190,
            NoseFairingFraction: 0.18, TailFairingFraction: 0.22,
            WallThickness_m: 0.005, MaterialIndex: 1, DepthRating_m: 100.0,
            HullFamily: HullFamily.CylindricalHemi);
        var cond = new MarineConditions(CruiseSpeed_ms: 1.5, MaxDepth_m: 100.0);

        var tmp = Path.GetTempFileName();
        try
        {
            MarineDesignPersistence.SaveJson(design, cond, tmp);
            var (loaded, _) = MarineDesignPersistence.LoadJson(tmp);
            Assert.Equal(HullFamily.CylindricalHemi, loaded.HullFamily);
        }
        finally { File.Delete(tmp); }
    }
}
