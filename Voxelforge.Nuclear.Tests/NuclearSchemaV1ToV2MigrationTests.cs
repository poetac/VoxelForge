// NuclearSchemaV1ToV2MigrationTests.cs — Sprint NU.W2 acceptance tests for
// the nuclear schema v1 → v2 identity migration.
//
// v2 adds 6 init-only fuel-pin fields to NuclearThermalDesign with NaN/0
// defaults. v1 designs round-trip into v2 unchanged; the per-pin model
// activation guard sits at the optimization layer and is dormant until
// the fields are populated.

using System.IO;
using Voxelforge.Nuclear;
using Voxelforge.Nuclear.IO;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class NuclearSchemaV1ToV2MigrationTests
{
    private static NuclearThermalDesign Wave1NrxA6() => new(
        Kind:                    NuclearKind.NervaSolidCore,
        ReactorThermalPower_MW:  1100.0,
        ReactorCoreLength_mm:    1400.0,
        ReactorCoreDiameter_mm:  1400.0,
        FuelLoadingFraction:     0.65,
        PropellantMassFlow_kgs:  33.0,
        ChamberPressure_bar:     34.0,
        ThroatRadius_mm:         120.0,
        ExpansionRatio:          100.0,
        NozzleLength_mm:         4000.0,
        RegenChannelDepth_mm:    2.0,
        RegenChannelCount:       200,
        NozzleWallThickness_mm:  1.5,
        NozzleChannelWidth_mm:   3.0,
        NozzleManifoldDepth_mm:  5.0);

    private static NuclearThermalDesign Wave2NrxA6WithFuelPins() => Wave1NrxA6() with
    {
        FuelPinDiameter_mm      = 2.5,
        FuelPinPitch_mm         = 3.2,
        FuelPinHexRings         = 2,
        FuelElementCount        = 564,
        FuelPinLength_m         = 1.4,
        FuelPinHotChannelFactor = 1.40,
    };

    private static NuclearThermalConditions Conditions()
        => new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    [Fact]
    public void Wave1_RoundTrips_AcrossV2_WithDefaultedFuelPinFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_nuclear_v2_w1_{Path.GetRandomFileName()}.json");
        try
        {
            NuclearDesignPersistence.SaveJson(Wave1NrxA6(), Conditions(), path);
            var (loaded, _) = NuclearDesignPersistence.LoadJson(path);
            Assert.Equal(NuclearKind.NervaSolidCore, loaded.Kind);
            Assert.Equal(1100.0, loaded.ReactorThermalPower_MW, precision: 6);
            // Fuel-pin fields default to NaN/0 on a Wave-1 round-trip.
            Assert.True(double.IsNaN(loaded.FuelPinDiameter_mm));
            Assert.True(double.IsNaN(loaded.FuelPinPitch_mm));
            Assert.Equal(0, loaded.FuelPinHexRings);
            Assert.Equal(0, loaded.FuelElementCount);
            Assert.True(double.IsNaN(loaded.FuelPinLength_m));
            Assert.True(double.IsNaN(loaded.FuelPinHotChannelFactor));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Wave2_RoundTrips_AcrossV2_WithFuelPinFieldsPreserved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_nuclear_v2_w2_{Path.GetRandomFileName()}.json");
        try
        {
            NuclearDesignPersistence.SaveJson(Wave2NrxA6WithFuelPins(), Conditions(), path);
            var (loaded, _) = NuclearDesignPersistence.LoadJson(path);
            Assert.Equal( 2.5, loaded.FuelPinDiameter_mm,      precision: 6);
            Assert.Equal( 3.2, loaded.FuelPinPitch_mm,         precision: 6);
            Assert.Equal(2,    loaded.FuelPinHexRings);
            Assert.Equal(564,  loaded.FuelElementCount);
            Assert.Equal( 1.4, loaded.FuelPinLength_m,         precision: 6);
            Assert.Equal( 1.40, loaded.FuelPinHotChannelFactor, precision: 6);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void V1Json_LoadsAsV2_WithDefaultedFuelPinFields()
    {
        // Hand-craft a v1 envelope (pre-Sprint-NU.W2 shape).
        const string v1Json = """
            {
              "Schema": "v1",
              "Version": "1.0",
              "CreatedUtc": "2026-01-01T00:00:00.0000000Z",
              "AppName": "Voxelforge.Nuclear",
              "Conditions": {
                "PropellantInletTemp_K": 80.0,
                "TargetDeltaV_ms": 3000.0
              },
              "Design": {
                "Kind": "NervaSolidCore",
                "ReactorThermalPower_MW": 1100.0,
                "ReactorCoreLength_mm": 1400.0,
                "ReactorCoreDiameter_mm": 1400.0,
                "FuelLoadingFraction": 0.65,
                "PropellantMassFlow_kgs": 33.0,
                "ChamberPressure_bar": 34.0,
                "ThroatRadius_mm": 120.0,
                "ExpansionRatio": 100.0,
                "NozzleLength_mm": 4000.0,
                "RegenChannelDepth_mm": 2.0,
                "RegenChannelCount": 200,
                "NozzleWallThickness_mm": 1.5,
                "NozzleChannelWidth_mm": 3.0,
                "NozzleManifoldDepth_mm": 5.0
              }
            }
            """;
        var path = Path.Combine(Path.GetTempPath(), $"vxf_nuclear_v1_chain_{Path.GetRandomFileName()}.json");
        try
        {
            File.WriteAllText(path, v1Json);
            var (loaded, _) = NuclearDesignPersistence.LoadJson(path);
            Assert.Equal(NuclearKind.NervaSolidCore, loaded.Kind);
            Assert.Equal(1100.0, loaded.ReactorThermalPower_MW, precision: 6);
            // The 6 fuel-pin fields default to NaN/0 on a v1 load.
            Assert.True(double.IsNaN(loaded.FuelPinDiameter_mm));
            Assert.Equal(0, loaded.FuelPinHexRings);
            Assert.Equal(0, loaded.FuelElementCount);
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
              "AppName": "Voxelforge.Nuclear"
            }
            """;
        var path = Path.Combine(Path.GetTempPath(), $"vxf_nuclear_v999_{Path.GetRandomFileName()}.json");
        try
        {
            File.WriteAllText(path, newerJson);
            Assert.Throws<UnsupportedNuclearSchemaException>(
                () => NuclearDesignPersistence.LoadJson(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
