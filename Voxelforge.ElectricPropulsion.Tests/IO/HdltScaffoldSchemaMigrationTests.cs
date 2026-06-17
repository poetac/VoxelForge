// HdltScaffoldSchemaMigrationTests.cs — Sprint EP.W6 phase 1 schema chain.

using System.IO;
using Voxelforge.ElectricPropulsion.IO;

namespace Voxelforge.ElectricPropulsion.Tests.IO;

public sealed class HdltScaffoldSchemaMigrationTests
{
    private static readonly ResistojetConditions BaselineConditions = new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:   5000.0,             // HDLT 100 W – 5 kW class
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    private static ElectricPropulsionEngineDesign HdltScaffoldDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.Hdlt,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        HdltHeliconRfPower_W            = 500.0,    // ANU Charles-Boswell baseline
        HdltMagneticFieldGradient_TpM   =  10.0,
        HdltChannelLength_mm            = 250.0,
        HdltArgonMassFlow_kgs           = 1.0e-5,   // 10 mg/s
    };

    [Fact]
    public void HdltScaffold_RoundTripsThroughV10Path()
    {
        var design = HdltScaffoldDesign();
        var path = Path.Combine(Path.GetTempPath(),
            $"vxf_ep_v10_hdlt_{Path.GetRandomFileName()}.json");
        try
        {
            ElectricPropulsionDesignPersistence.SaveJson(design, BaselineConditions, path);
            var loaded = ElectricPropulsionDesignPersistence.LoadJson(path);
            Assert.Equal(ElectricPropulsionEngineKind.Hdlt, loaded.Design!.Kind);
            Assert.Equal( 500.0, loaded.Design.HdltHeliconRfPower_W,          precision: 6);
            Assert.Equal(  10.0, loaded.Design.HdltMagneticFieldGradient_TpM, precision: 6);
            Assert.Equal( 250.0, loaded.Design.HdltChannelLength_mm,          precision: 6);
            Assert.Equal( 1.0e-5, loaded.Design.HdltArgonMassFlow_kgs,        precision: 9);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void V9FeepScaffoldJson_LoadsAsV10_WithDefaultedHdltFields()
    {
        const string v9Json = """
            {
                "Schema": "v9",
                "Version": "1.0",
                "AppName": "Voxelforge.ElectricPropulsion",
                "Conditions": {
                    "BusVoltage_V": 100.0,
                    "BusPower_W_avail": 150.0,
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
                    "Kind": "Feep",
                    "FeepAcceleratingVoltage_V": 9000.0,
                    "FeepBeamCurrent_A": 0.0001,
                    "FeepEmitterTipRadius_mm": 0.005,
                    "FeepPropellantMaterial": "Indium"
                }
            }
            """;

        var loaded = ElectricPropulsionDesignPersistence.ParseJson(v9Json);
        Assert.Equal(ElectricPropulsionEngineKind.Feep, loaded.Design!.Kind);
        Assert.Equal(9000.0, loaded.Design.FeepAcceleratingVoltage_V, precision: 6);
        // HDLT fields default to NaN after the v9 → v10 identity migration.
        Assert.True(double.IsNaN(loaded.Design.HdltHeliconRfPower_W));
        Assert.True(double.IsNaN(loaded.Design.HdltMagneticFieldGradient_TpM));
        Assert.True(double.IsNaN(loaded.Design.HdltChannelLength_mm));
        Assert.True(double.IsNaN(loaded.Design.HdltArgonMassFlow_kgs));
    }

    [Fact]
    public void V1ResistojetJson_LoadsAsV10_ChainedThroughAllPriorBumps()
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
        // HDLT (last bump) fields all NaN after the v1 → v10 chain.
        Assert.True(double.IsNaN(loaded.Design.HdltHeliconRfPower_W));
        Assert.True(double.IsNaN(loaded.Design.HdltMagneticFieldGradient_TpM));
        Assert.True(double.IsNaN(loaded.Design.HdltChannelLength_mm));
        Assert.True(double.IsNaN(loaded.Design.HdltArgonMassFlow_kgs));
    }

    [Fact]
    public void Hdlt_PhysicsDispatch_NowReturnsResult_PostEpW6Phase2()
    {
        // EP.W6 phase 2 (Sprint A.63 / #504) shipped the Helicon-DL
        // model. The dispatch no longer throws — it returns a valid
        // ElectricPropulsionResult anchored to the ANU baseline.
        // Replaces the prior NotImplementedException test.
        var design = HdltScaffoldDesign();
        var result = ElectricPropulsionOptimization.GenerateWith(design, BaselineConditions);
        Assert.NotNull(result);
        Assert.NotNull(result.PlasmaState);
        Assert.True(result.Thrust_N > 0,
            "HDLT dispatch should produce positive thrust at the scaffold's "
          + "ANU-class design point (500 W, 10 T/m, 250 mm, Argon).");
        Assert.True(result.IspVacuum_s > 0,
            "HDLT dispatch should produce positive Isp.");
    }

    [Fact]
    public void Hdlt_FamilyMaskBit_IsRegistered()
    {
        Assert.Equal(1 << 17, (int)Voxelforge.Optimization.EngineFamilyMask.ElectricHdlt);
    }

    [Fact]
    public void Hdlt_EnumValue_IsNine()
    {
        // Schema-stability invariant: Hdlt stays at value 9 (after Feep = 8).
        Assert.Equal(9, (int)ElectricPropulsionEngineKind.Hdlt);
    }

    [Fact]
    public void CurrentSchemaVersion_IsV10()
    {
        Assert.Equal("v10", ElectricPropulsionDesignPersistence.CurrentSchemaVersion);
    }
}
