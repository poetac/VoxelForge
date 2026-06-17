// HetSchemaMigrationTests.cs — Sprint EP.W2.HET acceptance tests for the
// schema v1 → v2 identity migration. Pins the Resistojet round-trip
// (no Wave-1 regression) and the v1-load-as-v2 default behaviour.

using System.IO;
using Voxelforge.ElectricPropulsion.IO;

namespace Voxelforge.ElectricPropulsion.Tests.IO;

public sealed class HetSchemaMigrationTests
{
    private static ElectricPropulsionEngineDesign Mr501bResistojet() => new(
        Kind:                    ElectricPropulsionEngineKind.Resistojet,
        HeaterPower_W:           870.0,
        PropellantMassFlow_kgs:  1.2e-4,
        NozzleThroatRadius_mm:   0.20,
        NozzleAreaRatio:         100.0,
        HeaterChamberLength_mm:  25.0,
        HeaterChamberRadius_mm:  6.0);

    private static ResistojetConditions Mr501bConditions() => new(
        BusVoltage_V:        28.0,
        BusPower_W_avail:    900.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K:  900.0,
        InletComposition:    PropellantInletComposition.Hydrazine_Shell405);

    private static ElectricPropulsionEngineDesign Bpt4000HetDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.HallEffect,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        DischargeVoltage_V = 300.0,
        DischargeCurrent_A = 15.0,
        MagneticField_T    = 0.02,
        AnodeRadius_mm     = 30.0,
        ChannelLength_mm   = 25.0,
        XenonMassFlow_kgs  = 1.6e-5,
        AnodeMaterial      = AnodeMaterial.Graphite,
        CathodeType        = CathodeType.HollowCathode,
    };

    [Fact]
    public void CurrentSchemaVersion_IsV10()
    {
        // v3 (Arcjet) → v4 (PPT), v4 → v5 (GIT), v5 → v6 (MPD),
        // v6 → v7 (Applied-Field MPD), v7 → v8 (VASIMR scaffold),
        // v8 → v9 (FEEP scaffold), v9 → v10 (HDLT scaffold).
        // Identity migration chain v1 → v10 means HET-era v2 reads
        // still round-trip cleanly through all subsequent bumps.
        Assert.Equal("v10", ElectricPropulsionDesignPersistence.CurrentSchemaVersion);
    }

    [Fact]
    public void V1Json_LoadsAsV2_WithDefaultedHetFields()
    {
        // Construct a v1 JSON envelope by hand — Resistojet design without
        // any HET fields. Pre-Wave-2 would have looked exactly like this.
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
        Assert.NotNull(loaded.Design);
        Assert.NotNull(loaded.Conditions);
        Assert.Equal(ElectricPropulsionEngineKind.Resistojet, loaded.Design!.Kind);
        // The 8 HET fields default to NaN / None.
        Assert.True(double.IsNaN(loaded.Design.DischargeVoltage_V));
        Assert.True(double.IsNaN(loaded.Design.DischargeCurrent_A));
        Assert.True(double.IsNaN(loaded.Design.MagneticField_T));
        Assert.True(double.IsNaN(loaded.Design.AnodeRadius_mm));
        Assert.True(double.IsNaN(loaded.Design.ChannelLength_mm));
        Assert.True(double.IsNaN(loaded.Design.XenonMassFlow_kgs));
        Assert.Equal(AnodeMaterial.None, loaded.Design.AnodeMaterial);
        Assert.Equal(CathodeType.None,   loaded.Design.CathodeType);
    }

    [Fact]
    public void Resistojet_SaveLoad_RoundTripsByteIdentical()
    {
        var design = Mr501bResistojet();
        var conds  = Mr501bConditions();
        string path = Path.GetTempFileName();
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(design, conds, path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.NotNull(loaded.Design);
            Assert.NotNull(loaded.Conditions);
            Assert.Equal(design.Kind, loaded.Design!.Kind);
            Assert.Equal(design.HeaterPower_W, loaded.Design.HeaterPower_W);
            Assert.Equal(design.NozzleAreaRatio, loaded.Design.NozzleAreaRatio);
            Assert.Equal(conds.Propellant, loaded.Conditions!.Propellant);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Het_SaveLoad_RoundTripsHetFields()
    {
        var design = Bpt4000HetDesign();
        var conds = new ResistojetConditions(
            BusVoltage_V:        300.0,
            BusPower_W_avail:    5000.0,
            AmbientPressure_Pa:  0.0,
            Propellant:          Propellant.Xenon,
            InletTemperature_K:  300.0,
            InletComposition:    PropellantInletComposition.PureH2);

        string path = Path.GetTempFileName();
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(design, conds, path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.NotNull(loaded.Design);
            Assert.Equal(ElectricPropulsionEngineKind.HallEffect, loaded.Design!.Kind);
            Assert.Equal(design.DischargeVoltage_V, loaded.Design.DischargeVoltage_V);
            Assert.Equal(design.DischargeCurrent_A, loaded.Design.DischargeCurrent_A);
            Assert.Equal(design.MagneticField_T,    loaded.Design.MagneticField_T);
            Assert.Equal(design.AnodeRadius_mm,     loaded.Design.AnodeRadius_mm);
            Assert.Equal(design.ChannelLength_mm,   loaded.Design.ChannelLength_mm);
            Assert.Equal(design.XenonMassFlow_kgs,  loaded.Design.XenonMassFlow_kgs);
            Assert.Equal(design.AnodeMaterial,      loaded.Design.AnodeMaterial);
            Assert.Equal(design.CathodeType,        loaded.Design.CathodeType);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void UnsupportedSchema_Throws()
    {
        const string futureJson = """
            {
                "Schema": "v99",
                "Conditions": null,
                "Design": null
            }
            """;
        Assert.Throws<UnsupportedElectricPropulsionSchemaException>(
            () => ElectricPropulsionDesignPersistence.ParseJson(futureJson));
    }
}
