// PptObjectiveTests.cs — Sprint EP.W2.PPT IObjective adapter tests.
// Mirror of ArcjetObjectiveTests / HetObjectiveTests.

using System;
using Voxelforge.ElectricPropulsion.Optimization;

namespace Voxelforge.ElectricPropulsion.Tests.Optimization;

public sealed class PptObjectiveTests
{
    private static ElectricPropulsionEngineDesign Eo1BaselinePpt() => new(
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
        PptIspCalibration         = double.NaN,
    };

    private static ResistojetConditions VacuumConditions(double busPower_W = 200.0) => new(
        BusVoltage_V:        28.0,
        BusPower_W_avail:    busPower_W,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void DefaultBounds_HaveSixDimensions()
    {
        Assert.Equal(6, PptObjective.DefaultBounds.Length);
        Assert.Equal(6, PptObjective.DefaultVariableNames.Length);
    }

    [Fact]
    public void DefaultBounds_NamesMatchVariableNames()
    {
        for (int i = 0; i < PptObjective.DefaultBounds.Length; i++)
            Assert.Equal(PptObjective.DefaultVariableNames[i], PptObjective.DefaultBounds[i].Name);
    }

    [Fact]
    public void Pack_ReturnsSixDimensionalVector()
    {
        var design = Eo1BaselinePpt();
        var v = PptObjective.Pack(design);
        Assert.Equal(6, v.Length);
        Assert.Equal(22.0, v[0]);
        Assert.Equal( 5.0, v[1]);
        Assert.Equal(25.0, v[2]);
        Assert.Equal(25.0, v[3]);
        Assert.Equal(15.0, v[4]);
        Assert.True(double.IsNaN(v[5]));
    }

    [Fact]
    public void Unpack_RoundTripsThroughPack()
    {
        var design = Eo1BaselinePpt() with { PptIspCalibration = 870.0 };
        var v = PptObjective.Pack(design);
        var design2 = PptObjective.Unpack(v, design);
        Assert.Equal(design.CapacitorEnergy_J,         design2.CapacitorEnergy_J);
        Assert.Equal(design.PulseFrequency_Hz,         design2.PulseFrequency_Hz);
        Assert.Equal(design.PptElectrodeGap_mm,        design2.PptElectrodeGap_mm);
        Assert.Equal(design.PptPropellantBarLength_mm, design2.PptPropellantBarLength_mm);
        Assert.Equal(design.PptElectrodeWidth_mm,      design2.PptElectrodeWidth_mm);
        Assert.Equal(design.PptIspCalibration,         design2.PptIspCalibration);
        Assert.Equal(design.Kind,                      design2.Kind);
    }

    [Fact]
    public void Unpack_PreservesCategoricalState()
    {
        var design = Eo1BaselinePpt();
        var v = new[] { 10.0, 1.0, 20.0, 20.0, 12.0, 800.0 };
        var design2 = PptObjective.Unpack(v, design);
        Assert.Equal(ElectricPropulsionEngineKind.PulsedPlasmaThruster, design2.Kind);
    }

    [Fact]
    public void Unpack_WrongVectorLength_Throws()
    {
        var design = Eo1BaselinePpt();
        Assert.Throws<ArgumentException>(() => PptObjective.Unpack(new[] { 1.0, 2.0 }, design));
    }

    [Fact]
    public void Build_OnNonPptKind_Throws()
    {
        var resistojet = Eo1BaselinePpt() with { Kind = ElectricPropulsionEngineKind.Resistojet };
        Assert.Throws<ArgumentException>(() => PptObjective.Build(VacuumConditions(), resistojet));
    }

    [Fact]
    public void Build_BindTimeBusPowerClip_LowersCapacitorEnergyMax()
    {
        // BusPower_W_avail = 50 W ⇒ E_cap_max_clipped = 50 / 10 (max f) = 5 J
        // (default E_cap max is 50 J). The first dim's Max should fall to 5.
        var lowBus = VacuumConditions(busPower_W: 50.0);
        var obj = PptObjective.Build(lowBus, Eo1BaselinePpt());
        Assert.Equal("CapacitorEnergy_J", obj.Variables[0].Name);
        Assert.True(obj.Variables[0].Max <= 5.0 + 1e-9,
            $"Expected E_cap_max ≤ 5 J after bind-time clip; got {obj.Variables[0].Max}.");
    }

    [Fact]
    public void Build_NoBudget_UsesDefaultBounds()
    {
        // BusPower_W_avail = 200 W ⇒ E_cap_max_clipped = 200 / 10 = 20 J,
        // which is below the default 50 J; the clip lowers the ceiling.
        var obj = PptObjective.Build(VacuumConditions(busPower_W: 200.0), Eo1BaselinePpt());
        Assert.True(obj.Variables[0].Max <= 50.0);
        Assert.Equal(0.5, obj.Variables[0].Min);
    }

    [Fact]
    public void Pack_NullDesign_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PptObjective.Pack(null!));
    }
}
