// AppliedFieldMpdSchemaMigrationTests.cs — Sprint EP.W3.AF schema chain.
//
// Schema v6 → v7 identity migration. Pins:
//   • CurrentSchemaVersion (moved to v10 after subsequent scaffold bumps)
//   • Round-trip across v7 for every prior kind (no regression)
//   • Applied-field MPD round-trip across v7 (this PR)
//   • v1 → v7 chained load (Wave-1 designs read forward through six bumps)
//   • v6 → v7 chained load (Wave-2 self-field MPD designs read forward
//     through the new bump and remain self-field-only — B = NaN means
//     "applied field disabled" in the solver).

using System.IO;
using Voxelforge.ElectricPropulsion.IO;

namespace Voxelforge.ElectricPropulsion.Tests.IO;

public sealed class AppliedFieldMpdSchemaMigrationTests
{
    private static readonly ResistojetConditions BaselineConditions = new(
        BusVoltage_V:        100.0,
        BusPower_W_avail: 120000.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    private static ElectricPropulsionEngineDesign AppliedFieldMpdDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.MagnetoPlasmaDynamic,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:    4.0e-5,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        MpdArcCurrent_A                 = 1500.0,
        MpdCathodeRadius_mm             =    6.0,
        MpdAnodeRadius_mm               =   50.0,
        MpdChamberLength_mm             =  100.0,
        MpdCathodeMaterial              = MpdCathodeMaterial.ThoriatedTungsten,
        MpdAppliedFieldStrength_T       = 0.15,
        MpdAppliedFieldCouplingOverride = 0.10,
    };

    [Fact]
    public void AppliedFieldMpd_RoundTripsThroughV7Path()
    {
        var design = AppliedFieldMpdDesign();
        var path = Path.Combine(Path.GetTempPath(), $"vxf_ep_v7_af_mpd_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(design, BaselineConditions, path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.MagnetoPlasmaDynamic, loaded.Design!.Kind);
            Assert.Equal(1500.0, loaded.Design.MpdArcCurrent_A, precision: 6);
            Assert.Equal(0.15, loaded.Design.MpdAppliedFieldStrength_T, precision: 6);
            Assert.Equal(0.10, loaded.Design.MpdAppliedFieldCouplingOverride, precision: 6);
            Assert.Equal(MpdCathodeMaterial.ThoriatedTungsten, loaded.Design.MpdCathodeMaterial);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void V6SelfFieldMpdJson_LoadsAsV7_WithDefaultedAppliedFieldFields()
    {
        // Wave-2 self-field MPD JSON (schema v6) without the new Wave-3 AF
        // fields. The identity migration should leave the design solving
        // identically to its v6 path — AppliedField fields default to NaN,
        // which the solver treats as "applied field disabled."
        const string v6Json = """
            {
                "Schema": "v6",
                "Version": "1.0",
                "AppName": "Voxelforge.ElectricPropulsion",
                "Conditions": {
                    "BusVoltage_V": 100.0,
                    "BusPower_W_avail": 300000.0,
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
                    "Kind": "MagnetoPlasmaDynamic",
                    "PropellantMassFlow_kgs": 0.0002,
                    "MpdArcCurrent_A": 4000.0,
                    "MpdCathodeRadius_mm": 10.0,
                    "MpdAnodeRadius_mm": 100.0,
                    "MpdChamberLength_mm": 150.0,
                    "MpdCathodeMaterial": "ThoriatedTungsten"
                }
            }
            """;

        var loaded = ElectricPropulsionDesignPersistence.ParseJson(v6Json);
        Assert.Equal(ElectricPropulsionEngineKind.MagnetoPlasmaDynamic, loaded.Design!.Kind);
        Assert.Equal(4000.0, loaded.Design.MpdArcCurrent_A, precision: 6);
        // The 2 applied-field fields default to NaN after the v6 → v7
        // identity migration.
        Assert.True(double.IsNaN(loaded.Design.MpdAppliedFieldStrength_T));
        Assert.True(double.IsNaN(loaded.Design.MpdAppliedFieldCouplingOverride));
    }

    [Fact]
    public void V1ResistojetJson_LoadsAsV7_ChainedThroughAllPriorBumps()
    {
        // v1 → v7 chained: Wave-1 v1 file deserialises with HET, Arcjet,
        // PPT, GIT, MPD, AND applied-field MPD fields all at defaults.
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
        // Applied-field MPD defaults populated by the chained identity migration.
        Assert.True(double.IsNaN(loaded.Design.MpdAppliedFieldStrength_T));
        Assert.True(double.IsNaN(loaded.Design.MpdAppliedFieldCouplingOverride));
    }

    [Fact]
    public void CurrentSchemaVersion_IsV10AfterSubsequentScaffoldsAlsoShipped()
    {
        // The Schema version moves forward with each scaffold; Applied-Field
        // MPD shipped at v7 but subsequent v8 (VASIMR), v9 (FEEP), and v10
        // (HDLT) scaffolds shipped after. Test remains valid: v7 AF-MPD
        // designs round-trip cleanly into v10 via the (v7, v8, v9, v10)
        // identity migrations — exercised by AppliedFieldMpd_RoundTripsThroughV7Path
        // and V6SelfFieldMpdJson_LoadsAsV7_WithDefaultedAppliedFieldFields above.
        Assert.Equal("v10", ElectricPropulsionDesignPersistence.CurrentSchemaVersion);
    }
}
