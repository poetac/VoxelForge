// VasimrScaffoldSchemaMigrationTests.cs — Sprint EP.W4 phase 1 schema chain.
//
// Pins:
//   • CurrentSchemaVersion (moved to v10 after subsequent scaffold bumps).
//   • VASIMR scaffold design round-trips through v8 cleanly (Kind serialises,
//     all 5 new VASIMR fields round-trip).
//   • v7 self-field MPD designs load as v8 with VASIMR fields at default NaN.
//   • v1 Resistojet designs load as v8 with VASIMR fields at default NaN.
//   • Calling GenerateWith on a Kind=Vasimr design still throws (the schema
//     scaffold doesn't activate the physics dispatch).

using System.IO;
using Voxelforge.ElectricPropulsion.IO;

namespace Voxelforge.ElectricPropulsion.Tests.IO;

public sealed class VasimrScaffoldSchemaMigrationTests
{
    private static readonly ResistojetConditions BaselineConditions = new(
        BusVoltage_V:        100.0,
        BusPower_W_avail: 250000.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    private static ElectricPropulsionEngineDesign VasimrScaffoldDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.Vasimr,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        VasimrHeliconRfPower_W     = 30000.0,    // 30 kW (VX-200 anchor)
        VasimrIcrhRfPower_W        = 170000.0,   // 170 kW
        VasimrSolenoidField_T      = 0.6,
        VasimrNozzleExitRadius_mm  = 150.0,
        VasimrArgonMassFlow_kgs    = 1.1e-4,     // 110 mg/s
    };

    [Fact]
    public void VasimrScaffold_RoundTripsThroughV8Path()
    {
        var design = VasimrScaffoldDesign();
        var path = Path.Combine(Path.GetTempPath(),
            $"vxf_ep_v8_vasimr_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(design, BaselineConditions, path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.Vasimr, loaded.Design!.Kind);
            Assert.Equal( 30000.0, loaded.Design.VasimrHeliconRfPower_W,    precision: 6);
            Assert.Equal(170000.0, loaded.Design.VasimrIcrhRfPower_W,       precision: 6);
            Assert.Equal(     0.6, loaded.Design.VasimrSolenoidField_T,     precision: 6);
            Assert.Equal(   150.0, loaded.Design.VasimrNozzleExitRadius_mm, precision: 6);
            Assert.Equal(  1.1e-4, loaded.Design.VasimrArgonMassFlow_kgs,   precision: 9);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void V7SelfFieldMpdJson_LoadsAsV8_WithDefaultedVasimrFields()
    {
        const string v7Json = """
            {
                "Schema": "v7",
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
                    "MpdAppliedFieldStrength_T": 0.15,
                    "MpdCathodeMaterial": "ThoriatedTungsten"
                }
            }
            """;

        var loaded = ElectricPropulsionDesignPersistence.ParseJson(v7Json);
        Assert.Equal(ElectricPropulsionEngineKind.MagnetoPlasmaDynamic, loaded.Design!.Kind);
        Assert.Equal(0.15, loaded.Design.MpdAppliedFieldStrength_T, precision: 6);
        // VASIMR fields default to NaN after the v7 → v8 identity migration.
        Assert.True(double.IsNaN(loaded.Design.VasimrHeliconRfPower_W));
        Assert.True(double.IsNaN(loaded.Design.VasimrIcrhRfPower_W));
        Assert.True(double.IsNaN(loaded.Design.VasimrSolenoidField_T));
        Assert.True(double.IsNaN(loaded.Design.VasimrNozzleExitRadius_mm));
        Assert.True(double.IsNaN(loaded.Design.VasimrArgonMassFlow_kgs));
    }

    [Fact]
    public void V1ResistojetJson_LoadsAsV8_ChainedThroughAllPriorBumps()
    {
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
        // Both AF-MPD AND VASIMR fields default to NaN after the chained
        // v1 → v8 migration.
        Assert.True(double.IsNaN(loaded.Design.MpdAppliedFieldStrength_T));
        Assert.True(double.IsNaN(loaded.Design.VasimrHeliconRfPower_W));
    }

    [Fact]
    public void Vasimr_PhysicsDispatch_NowReturnsResult_PostEpW4Phase2()
    {
        // EP.W4 phase 2 (Sprint A.64 / #498) shipped the helicon + ICRH
        // + magnetic-nozzle physics. The dispatch no longer throws —
        // it returns a valid ElectricPropulsionResult anchored to the
        // VX-200i baseline. Replaces the prior NotImplementedException
        // test.
        var design = VasimrScaffoldDesign();
        var result = ElectricPropulsionOptimization.GenerateWith(design, BaselineConditions);
        Assert.NotNull(result);
        Assert.NotNull(result.PlasmaState);
        Assert.True(result.Thrust_N > 0,
            "VASIMR dispatch should produce positive thrust at the scaffold's "
          + "VX-200i-class design point (30+170 kW, 2 T, 100 mm, Argon).");
        Assert.True(result.IspVacuum_s > 0,
            "VASIMR dispatch should produce positive Isp.");
    }

    [Fact]
    public void CurrentSchemaVersion_IsV10AfterSubsequentScaffoldsAlsoShipped()
    {
        // The Schema version moves forward with each scaffold; VASIMR
        // shipped at v8 but subsequent v9 (FEEP) and v10 (HDLT) scaffolds
        // shipped after. Test remains valid: v8 VASIMR designs round-trip
        // cleanly into v10 via the (v8, v9, v10) identity migrations —
        // exercised by VasimrScaffold_RoundTripsThroughV8Path and
        // V7SelfFieldMpdJson_LoadsAsV8_WithDefaultedVasimrFields above.
        Assert.Equal("v10", ElectricPropulsionDesignPersistence.CurrentSchemaVersion);
    }
}
