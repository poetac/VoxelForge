// ArcjetObjectiveTests.cs — Pack/Unpack + bus-power clip + Build validation
// for the Wave-2 arcjet IObjective adapter. Sibling to HetObjectiveTests.

using System;
using Voxelforge.ElectricPropulsion.Optimization;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Optimization;

public sealed class ArcjetObjectiveTests
{
    private static ElectricPropulsionEngineDesign ArcjetBaseline() => new(
        Kind:                    ElectricPropulsionEngineKind.Arcjet,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  3.9e-5,
        NozzleThroatRadius_mm:   0.5,
        NozzleAreaRatio:        100.0,
        HeaterChamberLength_mm:  12.0,
        HeaterChamberRadius_mm:   4.0)
    {
        ArcVoltage_V             = 100.0,
        ArcCurrent_A             =  18.0,
        ArcGap_mm                =   2.0,
        ArcjetElectrodeMaterial  = ArcjetElectrodeMaterial.Tungsten,
    };

    private static ResistojetConditions Conditions(double busPower = 2200.0) => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:    busPower,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 900.0,
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void DefaultBounds_HaveSixSlots()
    {
        Assert.Equal(6, ArcjetObjective.DefaultBounds.Length);
        Assert.Equal(6, ArcjetObjective.DefaultVariableNames.Length);
    }

    [Fact]
    public void DefaultBounds_NamesMatchVariableNames()
    {
        for (int i = 0; i < ArcjetObjective.DefaultBounds.Length; i++)
        {
            Assert.Equal(ArcjetObjective.DefaultVariableNames[i],
                         ArcjetObjective.DefaultBounds[i].Name);
        }
    }

    [Fact]
    public void Pack_ProducesSixDimVector_InCanonicalOrder()
    {
        var v = ArcjetObjective.Pack(ArcjetBaseline());
        Assert.Equal(6, v.Length);
        Assert.Equal(18.0,    v[0]);          // ArcCurrent_A
        Assert.Equal(100.0,   v[1]);          // ArcVoltage_V
        Assert.Equal(2.0,     v[2]);          // ArcGap_mm
        Assert.Equal(3.9e-5,  v[3]);          // PropellantMassFlow_kgs
        Assert.Equal(0.5,     v[4]);          // NozzleThroatRadius_mm
        Assert.Equal(100.0,   v[5]);          // NozzleAreaRatio
    }

    [Fact]
    public void Unpack_PackRoundTrips_Identity()
    {
        var baseline = ArcjetBaseline();
        var packed = ArcjetObjective.Pack(baseline);
        var unpacked = ArcjetObjective.Unpack(packed, baseline);
        Assert.Equal(baseline.ArcCurrent_A,           unpacked.ArcCurrent_A);
        Assert.Equal(baseline.ArcVoltage_V,           unpacked.ArcVoltage_V);
        Assert.Equal(baseline.ArcGap_mm,              unpacked.ArcGap_mm);
        Assert.Equal(baseline.PropellantMassFlow_kgs, unpacked.PropellantMassFlow_kgs);
        Assert.Equal(baseline.NozzleThroatRadius_mm,  unpacked.NozzleThroatRadius_mm);
        Assert.Equal(baseline.NozzleAreaRatio,        unpacked.NozzleAreaRatio);
    }

    [Fact]
    public void Unpack_PreservesCategoricalState()
    {
        // Kind, ArcjetElectrodeMaterial, etc. ride from the baseline through
        // Unpack untouched — the SA vector is purely numeric.
        var baseline = ArcjetBaseline();
        var packed = ArcjetObjective.Pack(baseline);
        var unpacked = ArcjetObjective.Unpack(packed, baseline);
        Assert.Equal(ElectricPropulsionEngineKind.Arcjet, unpacked.Kind);
        Assert.Equal(ArcjetElectrodeMaterial.Tungsten, unpacked.ArcjetElectrodeMaterial);
    }

    [Fact]
    public void Unpack_WrongVectorLength_Throws()
    {
        var baseline = ArcjetBaseline();
        Assert.Throws<ArgumentException>(
            () => ArcjetObjective.Unpack(new double[5], baseline));
        Assert.Throws<ArgumentException>(
            () => ArcjetObjective.Unpack(new double[7], baseline));
    }

    [Fact]
    public void Build_NonArcjetBaseline_Throws()
    {
        var resistojet = ArcjetBaseline() with { Kind = ElectricPropulsionEngineKind.Resistojet };
        Assert.Throws<ArgumentException>(
            () => ArcjetObjective.Build(Conditions(), resistojet));
    }

    [Fact]
    public void Build_NullConditions_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ArcjetObjective.Build(null!, ArcjetBaseline()));
    }

    [Fact]
    public void Build_NullBaseline_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ArcjetObjective.Build(Conditions(), null!));
    }

    [Fact]
    public void Build_DefaultBounds_Returns6DimAdapter()
    {
        var obj = ArcjetObjective.Build(Conditions(), ArcjetBaseline());
        Assert.Equal(6, obj.Variables.Count);
    }

    [Fact]
    public void Build_BusPowerClip_ReducesArcCurrentMax()
    {
        // Default I_arc max = 30 A; default V_arc max = 300 V.
        // Bus 3000 W → max I_arc clipped to 3000/300 = 10 A.
        var clipped = ArcjetObjective.Build(Conditions(busPower: 3000.0), ArcjetBaseline());
        var iArcInfo = clipped.Variables[0];
        Assert.Equal("ArcCurrent_A", iArcInfo.Name);
        Assert.True(iArcInfo.Max <= 10.0 + 1e-9,
            $"Expected I_arc max ≤ 10 A under 3 kW bus; got {iArcInfo.Max}.");
    }

    [Fact]
    public void Build_BusPowerSufficient_KeepsDefaultMax()
    {
        // 30 A × 300 V = 9 kW; bus 12 kW > 9 kW → no clip.
        var unclipped = ArcjetObjective.Build(Conditions(busPower: 12000.0), ArcjetBaseline());
        var iArcInfo = unclipped.Variables[0];
        Assert.Equal(30.0, iArcInfo.Max, precision: 6);
    }
}
