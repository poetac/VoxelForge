// MpdSchemaMigrationTests.cs — Sprint EP.W2.MPD acceptance tests for the
// schema v5 → v6 identity migration. Pins:
//   • Round-trip across v6 for every prior kind (no regression)
//   • MPD round-trip across v6 (this PR)
//   • v1 → v6 chained load (Wave-1 designs read forward through five bumps)
//   • v5 → v6 chained load (Wave-2 GIT designs read forward through the new bump)
//   • Unsupported newer schema throws

using System.IO;
using Voxelforge.ElectricPropulsion.IO;

namespace Voxelforge.ElectricPropulsion.Tests.IO;

public sealed class MpdSchemaMigrationTests
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

    private static ElectricPropulsionEngineDesign NasaLewisMpd() => new(
        Kind:                    ElectricPropulsionEngineKind.MagnetoPlasmaDynamic,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:    2.0e-4,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        MpdArcCurrent_A     = 4000.0,
        MpdCathodeRadius_mm =   10.0,
        MpdAnodeRadius_mm   =  100.0,
        MpdChamberLength_mm =  150.0,
        MpdCathodeMaterial  = MpdCathodeMaterial.ThoriatedTungsten,
    };

    private static ResistojetConditions MpdConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail: 250000.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void Resistojet_RoundTrips_AcrossV6()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_ep_v6_resistojet_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(Mr501bResistojet(), Mr501bConditions(), path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.Resistojet, loaded.Design!.Kind);
            Assert.Equal(870.0, loaded.Design.HeaterPower_W, precision: 6);
            // MPD fields default to NaN / None on a Resistojet round-trip.
            Assert.True(double.IsNaN(loaded.Design.MpdArcCurrent_A));
            Assert.True(double.IsNaN(loaded.Design.MpdCathodeRadius_mm));
            Assert.True(double.IsNaN(loaded.Design.MpdAnodeRadius_mm));
            Assert.True(double.IsNaN(loaded.Design.MpdChamberLength_mm));
            Assert.Equal(MpdCathodeMaterial.None, loaded.Design.MpdCathodeMaterial);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Git_RoundTrips_AcrossV6()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_ep_v6_git_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(NstarGit(), MpdConditions(), path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.GriddedIon, loaded.Design!.Kind);
            Assert.Equal(1100.0, loaded.Design.BeamVoltage_V, precision: 6);
            // MPD defaults preserved on a GIT round-trip.
            Assert.True(double.IsNaN(loaded.Design.MpdArcCurrent_A));
            Assert.Equal(MpdCathodeMaterial.None, loaded.Design.MpdCathodeMaterial);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Mpd_RoundTrips_AcrossV6()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vxf_ep_v6_mpd_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(NasaLewisMpd(), MpdConditions(), path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.MagnetoPlasmaDynamic, loaded.Design!.Kind);
            Assert.Equal(4000.0, loaded.Design.MpdArcCurrent_A,     precision: 6);
            Assert.Equal( 2.0e-4, loaded.Design.PropellantMassFlow_kgs, precision: 12);
            Assert.Equal(  10.0, loaded.Design.MpdCathodeRadius_mm, precision: 6);
            Assert.Equal( 100.0, loaded.Design.MpdAnodeRadius_mm,   precision: 6);
            Assert.Equal( 150.0, loaded.Design.MpdChamberLength_mm, precision: 6);
            Assert.Equal(MpdCathodeMaterial.ThoriatedTungsten, loaded.Design.MpdCathodeMaterial);
            // GIT / PPT / Arcjet / HET fields default — MPD shouldn't carry them.
            Assert.True(double.IsNaN(loaded.Design.BeamVoltage_V));
            Assert.True(double.IsNaN(loaded.Design.CapacitorEnergy_J));
            Assert.True(double.IsNaN(loaded.Design.ArcVoltage_V));
            Assert.True(double.IsNaN(loaded.Design.DischargeVoltage_V));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void V1ResistojetJson_LoadsAsV6_ChainedThroughV2V3V4V5()
    {
        // v1 → v2 → v3 → v4 → v5 → v6 chained: Wave-1 v1 file deserialises
        // with HET, Arcjet, PPT, GIT, AND MPD fields all at defaults.
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
        // MPD defaults populated by the chained identity migration.
        Assert.True(double.IsNaN(loaded.Design.MpdArcCurrent_A));
        Assert.True(double.IsNaN(loaded.Design.MpdCathodeRadius_mm));
        Assert.True(double.IsNaN(loaded.Design.MpdAnodeRadius_mm));
        Assert.True(double.IsNaN(loaded.Design.MpdChamberLength_mm));
        Assert.Equal(MpdCathodeMaterial.None, loaded.Design.MpdCathodeMaterial);
    }

    [Fact]
    public void V5GitJson_LoadsAsV6_WithDefaultedMpdFields()
    {
        const string v5Json = """
            {
                "Schema": "v5",
                "Version": "1.0",
                "AppName": "Voxelforge.ElectricPropulsion",
                "Conditions": {
                    "BusVoltage_V": 100.0,
                    "BusPower_W_avail": 2500.0,
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
                    "Kind": "GriddedIon",
                    "BeamVoltage_V": 1100.0,
                    "BeamCurrent_A": 1.76,
                    "ScreenGridRadius_mm": 145.0,
                    "AccelGridGap_mm": 0.6,
                    "NeutralizerCathodeCurrent_A": 1.76
                }
            }
            """;

        var loaded = ElectricPropulsionDesignPersistence.ParseJson(v5Json);
        Assert.Equal(ElectricPropulsionEngineKind.GriddedIon, loaded.Design!.Kind);
        Assert.Equal(1100.0, loaded.Design.BeamVoltage_V, precision: 6);
        // The 4 MPD numeric fields + enum default after the v5 → v6 identity migration.
        Assert.True(double.IsNaN(loaded.Design.MpdArcCurrent_A));
        Assert.True(double.IsNaN(loaded.Design.MpdCathodeRadius_mm));
        Assert.True(double.IsNaN(loaded.Design.MpdAnodeRadius_mm));
        Assert.True(double.IsNaN(loaded.Design.MpdChamberLength_mm));
        Assert.Equal(MpdCathodeMaterial.None, loaded.Design.MpdCathodeMaterial);
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
