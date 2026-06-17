// NuclearSchemaV2ToV3MigrationTests.cs — Sprint NU.W3 acceptance tests for
// the nuclear schema v2 → v3 identity migration.
//
// v3 adds bimodal NTR fields to NuclearThermalDesign: BimodalMode enum
// (default Thrust) + 5 init-only Brayton fields (ElectricPowerTarget_kWe,
// BraytonTurbineInletTemp_K, BraytonHePressure_bar, AlternatorRpm,
// BraytonRecuperatorEffectiveness) defaulting to 0.0 / NaN. NervaSolidCore
// designs round-trip into v3 unchanged.

using System.IO;
using Voxelforge.Nuclear;
using Voxelforge.Nuclear.IO;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class NuclearSchemaV2ToV3MigrationTests
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

    private static NuclearThermalDesign Wave3Sp100() => new NuclearThermalDesign(
        Kind:                    NuclearKind.BimodalNtr,
        ReactorThermalPower_MW:  1.5,
        ReactorCoreLength_mm:    500.0,
        ReactorCoreDiameter_mm:  300.0,
        FuelLoadingFraction:     0.65,
        PropellantMassFlow_kgs:  0.5,
        ChamberPressure_bar:     40.0,
        ThroatRadius_mm:         50.0,
        ExpansionRatio:          100.0,
        NozzleLength_mm:         2000.0,
        RegenChannelDepth_mm:    2.0,
        RegenChannelCount:       80,
        NozzleWallThickness_mm:  1.5,
        NozzleChannelWidth_mm:   3.0,
        NozzleManifoldDepth_mm:  5.0) with
    {
        BimodalMode                  = BimodalMode.Hybrid,
        ElectricPowerTarget_kWe      = 100.0,
        BraytonTurbineInletTemp_K    = 1300.0,
        BraytonHePressure_bar        = 120.0,
        AlternatorRpm                = 45_000.0,
    };

    private static NuclearThermalConditions Cond() =>
        new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    [Fact]
    public void NervaSolidCore_RoundTrips_AcrossV3_WithDefaultedBimodalFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_nuclear_v3_nerva_{Path.GetRandomFileName()}.json");
        try
        {
            NuclearDesignPersistence.SaveJson(Wave1NrxA6(), Cond(), path);
            var (loaded, _) = NuclearDesignPersistence.LoadJson(path);
            Assert.Equal(NuclearKind.NervaSolidCore, loaded.Kind);
            Assert.Equal(1100.0, loaded.ReactorThermalPower_MW, precision: 6);
            // Bimodal fields default on a NervaSolidCore round-trip.
            Assert.Equal(BimodalMode.Thrust, loaded.BimodalMode);
            Assert.Equal(0.0, loaded.ElectricPowerTarget_kWe, precision: 12);
            Assert.Equal(0.0, loaded.BraytonTurbineInletTemp_K, precision: 12);
            Assert.Equal(0.0, loaded.BraytonHePressure_bar, precision: 12);
            Assert.Equal(0.0, loaded.AlternatorRpm, precision: 12);
            Assert.True(double.IsNaN(loaded.BraytonRecuperatorEffectiveness));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void BimodalNtr_RoundTrips_AcrossV3_WithBimodalFieldsPreserved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_nuclear_v3_bimodal_{Path.GetRandomFileName()}.json");
        try
        {
            NuclearDesignPersistence.SaveJson(Wave3Sp100(), Cond(), path);
            var (loaded, _) = NuclearDesignPersistence.LoadJson(path);
            Assert.Equal(NuclearKind.BimodalNtr, loaded.Kind);
            Assert.Equal(BimodalMode.Hybrid, loaded.BimodalMode);
            Assert.Equal( 100.0, loaded.ElectricPowerTarget_kWe,    precision: 6);
            Assert.Equal(1300.0, loaded.BraytonTurbineInletTemp_K,  precision: 6);
            Assert.Equal( 120.0, loaded.BraytonHePressure_bar,      precision: 6);
            Assert.Equal(45_000.0, loaded.AlternatorRpm,            precision: 6);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void V1Json_LoadsAsV3_ChainedThroughV2()
    {
        // v1 → v2 → v3 chained migration: Wave-1 v1 file deserialises with
        // fuel-pin fields (Wave-2) AND bimodal fields (Wave-3) all at defaults.
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
        var path = Path.Combine(Path.GetTempPath(), $"vxf_nuclear_v1_v3chain_{Path.GetRandomFileName()}.json");
        try
        {
            File.WriteAllText(path, v1Json);
            var (loaded, _) = NuclearDesignPersistence.LoadJson(path);
            Assert.Equal(NuclearKind.NervaSolidCore, loaded.Kind);
            // Fuel-pin defaults (from v1→v2).
            Assert.True(double.IsNaN(loaded.FuelPinDiameter_mm));
            Assert.Equal(0, loaded.FuelElementCount);
            // Bimodal defaults (from v2→v3).
            Assert.Equal(BimodalMode.Thrust, loaded.BimodalMode);
            Assert.Equal(0.0, loaded.ElectricPowerTarget_kWe);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
