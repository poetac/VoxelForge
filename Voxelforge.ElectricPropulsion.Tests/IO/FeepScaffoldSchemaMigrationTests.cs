// FeepScaffoldSchemaMigrationTests.cs — Sprint EP.W5 phase 1 schema chain.
//
// Pins:
//   • CurrentSchemaVersion = v9.
//   • FEEP scaffold design round-trips through v9 cleanly (Kind serialises,
//     all 4 new FEEP fields round-trip including the FeepPropellant enum).
//   • v8 VASIMR-scaffold designs load as v9 with FEEP fields at NaN / None.
//   • v1 Resistojet designs load as v9 through the chained migration.
//   • Calling GenerateWith on a Kind=Feep design now returns a valid
//     ElectricPropulsionResult — EP.W5 phase 2 shipped the Mair-Lozano
//     emitter model. (Was: threw NotImplementedException with EP.W5
//     phase 2 marker; that throw moved into phase-2 history.)

using System.IO;
using Voxelforge.ElectricPropulsion.IO;

namespace Voxelforge.ElectricPropulsion.Tests.IO;

public sealed class FeepScaffoldSchemaMigrationTests
{
    private static readonly ResistojetConditions BaselineConditions = new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:    150.0,                           // FEEP class — sub-Watt to ~100 W
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    private static ElectricPropulsionEngineDesign FeepScaffoldDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.Feep,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        FeepAcceleratingVoltage_V = 9000.0,   // Mair Indium-FEEP cluster anchor
        FeepBeamCurrent_A         = 1e-4,     // 100 μA per emitter
        FeepEmitterTipRadius_mm   = 0.005,    // 5 μm tip
        FeepPropellantMaterial    = FeepPropellant.Indium,
    };

    [Fact]
    public void FeepScaffold_RoundTripsThroughV9Path()
    {
        var design = FeepScaffoldDesign();
        var path = Path.Combine(Path.GetTempPath(),
            $"vxf_ep_v9_feep_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(design, BaselineConditions, path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.Feep, loaded.Design!.Kind);
            Assert.Equal( 9000.0, loaded.Design.FeepAcceleratingVoltage_V, precision: 6);
            Assert.Equal( 1e-4,   loaded.Design.FeepBeamCurrent_A,         precision: 9);
            Assert.Equal( 0.005,  loaded.Design.FeepEmitterTipRadius_mm,   precision: 9);
            Assert.Equal(FeepPropellant.Indium, loaded.Design.FeepPropellantMaterial);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void V8VasimrScaffoldJson_LoadsAsV9_WithDefaultedFeepFields()
    {
        const string v8Json = """
            {
                "Schema": "v8",
                "Version": "1.0",
                "AppName": "Voxelforge.ElectricPropulsion",
                "Conditions": {
                    "BusVoltage_V": 100.0,
                    "BusPower_W_avail": 250000.0,
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
                    "Kind": "Vasimr",
                    "VasimrHeliconRfPower_W": 30000.0,
                    "VasimrIcrhRfPower_W": 170000.0,
                    "VasimrSolenoidField_T": 0.6,
                    "VasimrNozzleExitRadius_mm": 150.0,
                    "VasimrArgonMassFlow_kgs": 1.1e-4
                }
            }
            """;

        var loaded = ElectricPropulsionDesignPersistence.ParseJson(v8Json);
        Assert.Equal(ElectricPropulsionEngineKind.Vasimr, loaded.Design!.Kind);
        Assert.Equal(30000.0, loaded.Design.VasimrHeliconRfPower_W, precision: 6);
        // FEEP fields default to NaN / None after the v8 → v9 identity migration.
        Assert.True(double.IsNaN(loaded.Design.FeepAcceleratingVoltage_V));
        Assert.True(double.IsNaN(loaded.Design.FeepBeamCurrent_A));
        Assert.True(double.IsNaN(loaded.Design.FeepEmitterTipRadius_mm));
        Assert.Equal(FeepPropellant.None, loaded.Design.FeepPropellantMaterial);
    }

    [Fact]
    public void V1ResistojetJson_LoadsAsV9_ChainedThroughAllPriorBumps()
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
        // FEEP fields default to NaN after the v1 → v9 chain.
        Assert.True(double.IsNaN(loaded.Design.FeepAcceleratingVoltage_V));
        Assert.True(double.IsNaN(loaded.Design.FeepBeamCurrent_A));
        Assert.Equal(FeepPropellant.None, loaded.Design.FeepPropellantMaterial);
    }

    [Fact]
    public void Feep_PhysicsDispatch_NowReturnsResult_PostEpW5Phase2()
    {
        // EP.W5 phase 2 (Sprint A.62 / #503) shipped the Mair-Lozano
        // emitter model. The dispatch no longer throws — it returns a
        // valid ElectricPropulsionResult anchored to the IFM Nano
        // cluster. Replaces the prior NotImplementedException test.
        var design = FeepScaffoldDesign();
        var result = ElectricPropulsionOptimization.GenerateWith(design, BaselineConditions);
        Assert.NotNull(result);
        Assert.NotNull(result.PlasmaState);
        Assert.True(result.Thrust_N > 0,
            "FEEP dispatch should produce positive thrust at the scaffold's "
          + "IFM-Nano-class design point (9 kV, 100 μA, 5 μm, Indium).");
        Assert.True(result.IspVacuum_s > 0,
            "FEEP dispatch should produce positive Isp.");
    }

    [Fact]
    public void Feep_FamilyMaskBit_IsRegistered()
    {
        Assert.Equal(1 << 16, (int)Voxelforge.Optimization.EngineFamilyMask.ElectricFeep);
    }

    [Fact]
    public void Feep_EnumValue_IsEight()
    {
        // Schema-stability invariant: Feep stays at value 8 (after
        // Vasimr = 7).
        Assert.Equal(8, (int)ElectricPropulsionEngineKind.Feep);
    }

    [Fact]
    public void FeepPropellant_IndiumAndCesiumRegistered()
    {
        // The two cluster-anchor propellant materials documented in
        // ADR-034 D4 / Sprint EP.W5 phase 1.
        Assert.Equal(1, (int)FeepPropellant.Indium);
        Assert.Equal(2, (int)FeepPropellant.Cesium);
        Assert.Equal(0, (int)FeepPropellant.None);
    }

    [Fact]
    public void CurrentSchemaVersion_IsV10AfterHdltAlsoShipped()
    {
        // The Schema version moves forward with each scaffold; FEEP shipped
        // at v9 but the v10 HDLT bump shipped in the same branch. Test
        // remains valid: v9 designs round-trip cleanly into v10 via the
        // (v9, v10) identity migration.
        Assert.Equal("v10", ElectricPropulsionDesignPersistence.CurrentSchemaVersion);
    }
}
