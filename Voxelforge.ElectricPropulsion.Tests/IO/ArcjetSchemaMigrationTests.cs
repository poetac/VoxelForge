// ArcjetSchemaMigrationTests.cs — Sprint EP.W2.AJ acceptance tests for the
// schema v2 → v3 identity migration. Pins:
//   • Resistojet round-trip (no Wave-1 regression)
//   • HET round-trip (no Wave-2 HET regression)
//   • v1 → v3 chained load (Wave-1 designs read forward through both bumps)
//   • v2 → v3 chained load (Wave-2 HET designs read forward through the new bump)
//   • Unsupported newer schema throws
//   • Arcjet round-trip (this PR)

using System.IO;
using Voxelforge.ElectricPropulsion.IO;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.IO;

public sealed class ArcjetSchemaMigrationTests
{
    private static ElectricPropulsionEngineDesign Mr501bResistojet() => new(
        Kind:                    ElectricPropulsionEngineKind.Resistojet,
        HeaterPower_W:           870.0,
        PropellantMassFlow_kgs:  1.2e-4,
        NozzleThroatRadius_mm:   0.20,
        NozzleAreaRatio:         100.0,
        HeaterChamberLength_mm:  25.0,
        HeaterChamberRadius_mm:   6.0);

    private static ResistojetConditions Mr501bConditions() => new(
        BusVoltage_V:        28.0,
        BusPower_W_avail:    900.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K:  900.0,
        InletComposition:    PropellantInletComposition.Hydrazine_Shell405);

    private static ElectricPropulsionEngineDesign Bpt4000Het() => new(
        Kind:                    ElectricPropulsionEngineKind.HallEffect,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        DischargeVoltage_V = 300.0,
        DischargeCurrent_A =  15.0,
        MagneticField_T    =   0.02,
        AnodeRadius_mm     =  30.0,
        ChannelLength_mm   =  25.0,
        XenonMassFlow_kgs  =   1.6e-5,
        AnodeMaterial      = AnodeMaterial.Graphite,
        CathodeType        = CathodeType.HollowCathode,
    };

    private static ElectricPropulsionEngineDesign Mr509Atos() => new(
        Kind:                    ElectricPropulsionEngineKind.Arcjet,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  3.9e-5,
        NozzleThroatRadius_mm:   0.5,
        NozzleAreaRatio:         100.0,
        HeaterChamberLength_mm:  12.0,
        HeaterChamberRadius_mm:   4.0)
    {
        ArcVoltage_V             = 100.0,
        ArcCurrent_A             =  18.0,
        ArcGap_mm                =   2.0,
        ArcjetElectrodeMaterial  = ArcjetElectrodeMaterial.Tungsten,
    };

    private static ResistojetConditions ArcjetConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:   2200.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K:  900.0,
        InletComposition:    PropellantInletComposition.PureH2);

    [Fact]
    public void Resistojet_RoundTrips_AcrossV3()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_ep_v3_resistojet_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(Mr501bResistojet(), Mr501bConditions(), path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.NotNull(loaded.Design);
            Assert.NotNull(loaded.Conditions);
            Assert.Equal(ElectricPropulsionEngineKind.Resistojet, loaded.Design!.Kind);
            Assert.Equal(870.0, loaded.Design.HeaterPower_W, precision: 6);
            // Arcjet fields default to NaN / None on round-trip.
            Assert.True(double.IsNaN(loaded.Design.ArcVoltage_V));
            Assert.True(double.IsNaN(loaded.Design.ArcCurrent_A));
            Assert.True(double.IsNaN(loaded.Design.ArcGap_mm));
            Assert.Equal(ArcjetElectrodeMaterial.None, loaded.Design.ArcjetElectrodeMaterial);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Het_RoundTrips_AcrossV3()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_ep_v3_het_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(Bpt4000Het(), Mr501bConditions(), path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.HallEffect, loaded.Design!.Kind);
            Assert.Equal(300.0, loaded.Design.DischargeVoltage_V, precision: 6);
            Assert.Equal(AnodeMaterial.Graphite, loaded.Design.AnodeMaterial);
            // Arcjet fields default to NaN / None — HET shouldn't carry them.
            Assert.True(double.IsNaN(loaded.Design.ArcVoltage_V));
            Assert.Equal(ArcjetElectrodeMaterial.None, loaded.Design.ArcjetElectrodeMaterial);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Arcjet_RoundTrips_AcrossV3()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_ep_v3_arcjet_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(Mr509Atos(), ArcjetConditions(), path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.Arcjet, loaded.Design!.Kind);
            Assert.Equal(100.0, loaded.Design.ArcVoltage_V,           precision: 6);
            Assert.Equal( 18.0, loaded.Design.ArcCurrent_A,           precision: 6);
            Assert.Equal(  2.0, loaded.Design.ArcGap_mm,              precision: 6);
            Assert.Equal(ArcjetElectrodeMaterial.Tungsten, loaded.Design.ArcjetElectrodeMaterial);
            // HET fields default to NaN / None — Arcjet shouldn't carry them.
            Assert.True(double.IsNaN(loaded.Design.DischargeVoltage_V));
            Assert.Equal(AnodeMaterial.None, loaded.Design.AnodeMaterial);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void V2HetJson_LoadsAsV3_WithDefaultedArcjetFields()
    {
        // Hand-craft a v2 JSON envelope (HET-shaped) — pre-Sprint-EP.W2.AJ
        // would have looked exactly like this. Loading must succeed and
        // populate the new arcjet fields with NaN / None defaults.
        const string v2Json = """
            {
                "Schema": "v2",
                "Version": "1.0",
                "AppName": "Voxelforge.ElectricPropulsion",
                "Conditions": {
                    "BusVoltage_V": 28.0,
                    "BusPower_W_avail": 5000.0,
                    "AmbientPressure_Pa": 0.0,
                    "Propellant": "Xenon",
                    "InletTemperature_K": 300.0,
                    "InletComposition": {
                        "NH3MoleFraction": 0.0,
                        "N2MoleFraction": 0.0,
                        "H2MoleFraction": 1.0,
                        "H2OMoleFraction": 0.0
                    }
                },
                "Design": {
                    "Kind": "HallEffect",
                    "DischargeVoltage_V": 300.0,
                    "DischargeCurrent_A": 15.0,
                    "MagneticField_T": 0.02,
                    "AnodeRadius_mm": 30.0,
                    "ChannelLength_mm": 25.0,
                    "XenonMassFlow_kgs": 0.000016,
                    "AnodeMaterial": "Graphite",
                    "CathodeType": "HollowCathode"
                }
            }
            """;

        var loaded = ElectricPropulsionDesignPersistence.ParseJson(v2Json);
        Assert.NotNull(loaded.Design);
        Assert.Equal(ElectricPropulsionEngineKind.HallEffect, loaded.Design!.Kind);
        Assert.Equal(AnodeMaterial.Graphite, loaded.Design.AnodeMaterial);
        // The 5 arcjet fields default to NaN / None on a v2 load.
        Assert.True(double.IsNaN(loaded.Design.ArcVoltage_V));
        Assert.True(double.IsNaN(loaded.Design.ArcCurrent_A));
        Assert.True(double.IsNaN(loaded.Design.ArcGap_mm));
        Assert.True(double.IsNaN(loaded.Design.ArcjetThermalEfficiency));
        Assert.Equal(ArcjetElectrodeMaterial.None, loaded.Design.ArcjetElectrodeMaterial);
    }

    [Fact]
    public void V1ResistojetJson_LoadsAsV3_ChainedThroughV2()
    {
        // v1 → v2 → v3 chained migration: a Wave-1 v1 file deserialises with
        // both HET and Arcjet fields at their defaults.
        const string v1Json = """
            {
                "Schema": "v1",
                "Version": "1.0",
                "AppName": "Voxelforge.ElectricPropulsion",
                "Conditions": {
                    "BusVoltage_V": 28.0,
                    "BusPower_W_avail": 900.0,
                    "AmbientPressure_Pa": 0.0,
                    "Propellant": "N2H4Decomposed",
                    "InletTemperature_K": 900.0,
                    "InletComposition": {
                        "NH3MoleFraction": 0.32,
                        "N2MoleFraction": 0.24,
                        "H2MoleFraction": 0.44,
                        "H2OMoleFraction": 0.0
                    }
                },
                "Design": {
                    "Kind": "Resistojet",
                    "HeaterPower_W": 870.0,
                    "PropellantMassFlow_kgs": 0.00012,
                    "NozzleThroatRadius_mm": 0.20,
                    "NozzleAreaRatio": 100.0,
                    "HeaterChamberLength_mm": 25.0,
                    "HeaterChamberRadius_mm": 6.0
                }
            }
            """;

        var loaded = ElectricPropulsionDesignPersistence.ParseJson(v1Json);
        Assert.Equal(ElectricPropulsionEngineKind.Resistojet, loaded.Design!.Kind);
        Assert.Equal(870.0, loaded.Design.HeaterPower_W, precision: 6);
        // HET defaults (8 fields).
        Assert.True(double.IsNaN(loaded.Design.DischargeVoltage_V));
        Assert.Equal(AnodeMaterial.None, loaded.Design.AnodeMaterial);
        // Arcjet defaults (5 fields).
        Assert.True(double.IsNaN(loaded.Design.ArcVoltage_V));
        Assert.True(double.IsNaN(loaded.Design.ArcCurrent_A));
        Assert.True(double.IsNaN(loaded.Design.ArcGap_mm));
        Assert.True(double.IsNaN(loaded.Design.ArcjetThermalEfficiency));
        Assert.Equal(ArcjetElectrodeMaterial.None, loaded.Design.ArcjetElectrodeMaterial);
    }

    [Fact]
    public void UnsupportedNewerSchema_Throws()
    {
        // Hand-craft a JSON envelope claiming a "v999" schema.
        const string newerJson = """
            {
                "Schema": "v999",
                "Version": "1.0",
                "AppName": "Voxelforge.ElectricPropulsion"
            }
            """;
        Assert.Throws<UnsupportedElectricPropulsionSchemaException>(
            () => ElectricPropulsionDesignPersistence.ParseJson(newerJson));
    }
}
