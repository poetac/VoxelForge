// GitSchemaMigrationTests.cs — Sprint EP.W2.GIT acceptance tests for the
// schema v4 → v5 identity migration. Pins:
//   • Resistojet round-trip across v5 (no Wave-1 regression)
//   • HET round-trip across v5
//   • Arcjet round-trip across v5
//   • PPT round-trip across v5 (no v4 regression)
//   • GIT round-trip across v5 (this PR)
//   • v1 → v5 chained load (Wave-1 designs read forward through four bumps)
//   • v4 → v5 chained load (Wave-2 PPT designs read forward through the new bump)
//   • Unsupported newer schema throws

using System.IO;
using Voxelforge.ElectricPropulsion.IO;

namespace Voxelforge.ElectricPropulsion.Tests.IO;

public sealed class GitSchemaMigrationTests
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

    private static ElectricPropulsionEngineDesign Eo1Ppt() => new(
        Kind:                    ElectricPropulsionEngineKind.PulsedPlasmaThruster,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        CapacitorEnergy_J         = 22.0,
        PulseFrequency_Hz         =  5.0,
        PptElectrodeGap_mm        = 25.0,
        PptPropellantBarLength_mm = 25.0,
        PptElectrodeWidth_mm      = 15.0,
    };

    private static ElectricPropulsionEngineDesign NstarGit() => new(
        Kind:                    ElectricPropulsionEngineKind.GriddedIon,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        BeamVoltage_V               = 1100.0,
        BeamCurrent_A               =    1.76,
        ScreenGridRadius_mm         =  145.0,
        AccelGridGap_mm             =    0.6,
        NeutralizerCathodeCurrent_A =    1.76,
    };

    private static ResistojetConditions GitConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:   2500.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void Resistojet_RoundTrips_AcrossV5()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_ep_v5_resistojet_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(Mr501bResistojet(), Mr501bConditions(), path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.Resistojet, loaded.Design!.Kind);
            Assert.Equal(870.0, loaded.Design.HeaterPower_W, precision: 6);
            // GIT fields default to NaN on a Resistojet round-trip.
            Assert.True(double.IsNaN(loaded.Design.BeamVoltage_V));
            Assert.True(double.IsNaN(loaded.Design.BeamCurrent_A));
            Assert.True(double.IsNaN(loaded.Design.ScreenGridRadius_mm));
            Assert.True(double.IsNaN(loaded.Design.AccelGridGap_mm));
            Assert.True(double.IsNaN(loaded.Design.NeutralizerCathodeCurrent_A));
            Assert.True(double.IsNaN(loaded.Design.GitMassUtilizationOverride));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Het_RoundTrips_AcrossV5()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_ep_v5_het_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(Bpt4000Het(), Mr501bConditions(), path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.HallEffect, loaded.Design!.Kind);
            Assert.Equal(300.0, loaded.Design.DischargeVoltage_V, precision: 6);
            // GIT defaults.
            Assert.True(double.IsNaN(loaded.Design.BeamVoltage_V));
            Assert.True(double.IsNaN(loaded.Design.BeamCurrent_A));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Arcjet_RoundTrips_AcrossV5()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_ep_v5_arcjet_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(Mr509Atos(), Mr501bConditions(), path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.Arcjet, loaded.Design!.Kind);
            Assert.Equal(100.0, loaded.Design.ArcVoltage_V, precision: 6);
            Assert.True(double.IsNaN(loaded.Design.BeamVoltage_V));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Ppt_RoundTrips_AcrossV5()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_ep_v5_ppt_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(Eo1Ppt(), GitConditions(), path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.PulsedPlasmaThruster, loaded.Design!.Kind);
            Assert.Equal(22.0, loaded.Design.CapacitorEnergy_J, precision: 6);
            // GIT defaults preserved on a PPT round-trip.
            Assert.True(double.IsNaN(loaded.Design.BeamVoltage_V));
            Assert.True(double.IsNaN(loaded.Design.NeutralizerCathodeCurrent_A));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Git_RoundTrips_AcrossV5()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_ep_v5_git_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(NstarGit(), GitConditions(), path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.GriddedIon, loaded.Design!.Kind);
            Assert.Equal(1100.0, loaded.Design.BeamVoltage_V,               precision: 6);
            Assert.Equal(   1.76, loaded.Design.BeamCurrent_A,               precision: 6);
            Assert.Equal( 145.0, loaded.Design.ScreenGridRadius_mm,         precision: 6);
            Assert.Equal(   0.6, loaded.Design.AccelGridGap_mm,              precision: 6);
            Assert.Equal(   1.76, loaded.Design.NeutralizerCathodeCurrent_A, precision: 6);
            Assert.True(double.IsNaN(loaded.Design.GitMassUtilizationOverride));
            // PPT / Arcjet / HET fields default — GIT shouldn't carry them.
            Assert.True(double.IsNaN(loaded.Design.CapacitorEnergy_J));
            Assert.True(double.IsNaN(loaded.Design.ArcVoltage_V));
            Assert.True(double.IsNaN(loaded.Design.DischargeVoltage_V));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void V1ResistojetJson_LoadsAsV5_ChainedThroughV2V3V4()
    {
        // v1 → v2 → v3 → v4 → v5 chained migration: a Wave-1 v1 file
        // deserialises with HET, Arcjet, PPT, AND GIT fields all at defaults.
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
        // GIT defaults populated by the chained identity migrations.
        Assert.True(double.IsNaN(loaded.Design.BeamVoltage_V));
        Assert.True(double.IsNaN(loaded.Design.BeamCurrent_A));
        Assert.True(double.IsNaN(loaded.Design.ScreenGridRadius_mm));
        Assert.True(double.IsNaN(loaded.Design.AccelGridGap_mm));
        Assert.True(double.IsNaN(loaded.Design.NeutralizerCathodeCurrent_A));
        Assert.True(double.IsNaN(loaded.Design.GitMassUtilizationOverride));
    }

    [Fact]
    public void V4PptJson_LoadsAsV5_WithDefaultedGitFields()
    {
        // Hand-craft a v4 JSON envelope (PPT-shaped) — pre-Sprint-EP.W2.GIT
        // would have looked exactly like this. Loading must succeed and
        // populate the new GIT fields with NaN defaults.
        const string v4Json = """
            {
                "Schema": "v4",
                "Version": "1.0",
                "AppName": "Voxelforge.ElectricPropulsion",
                "Conditions": {
                    "BusVoltage_V": 28.0,
                    "BusPower_W_avail": 200.0,
                    "AmbientPressure_Pa": 0.0,
                    "Propellant": "N2H4Decomposed",
                    "InletTemperature_K": 300.0,
                    "InletComposition": {
                        "NH3MoleFraction": 0.0,
                        "N2MoleFraction": 0.0,
                        "H2MoleFraction": 1.0,
                        "H2OMoleFraction": 0.0
                    }
                },
                "Design": {
                    "Kind": "PulsedPlasmaThruster",
                    "CapacitorEnergy_J": 22.0,
                    "PulseFrequency_Hz": 5.0,
                    "PptElectrodeGap_mm": 25.0,
                    "PptPropellantBarLength_mm": 25.0,
                    "PptElectrodeWidth_mm": 15.0
                }
            }
            """;

        var loaded = ElectricPropulsionDesignPersistence.ParseJson(v4Json);
        Assert.Equal(ElectricPropulsionEngineKind.PulsedPlasmaThruster, loaded.Design!.Kind);
        Assert.Equal(22.0, loaded.Design.CapacitorEnergy_J, precision: 6);
        // The 6 GIT fields default to NaN on a v4 load.
        Assert.True(double.IsNaN(loaded.Design.BeamVoltage_V));
        Assert.True(double.IsNaN(loaded.Design.BeamCurrent_A));
        Assert.True(double.IsNaN(loaded.Design.ScreenGridRadius_mm));
        Assert.True(double.IsNaN(loaded.Design.AccelGridGap_mm));
        Assert.True(double.IsNaN(loaded.Design.NeutralizerCathodeCurrent_A));
        Assert.True(double.IsNaN(loaded.Design.GitMassUtilizationOverride));
    }

    [Fact]
    public void UnsupportedNewerSchema_Throws()
    {
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
