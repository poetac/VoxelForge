// HetObjectiveTests.cs — Sprint EP.W2.HET acceptance tests for the 6-dim
// IObjective adapter wrapping the HET pipeline.

using Voxelforge.ElectricPropulsion.Optimization;

namespace Voxelforge.ElectricPropulsion.Tests.Optimization;

public sealed class HetObjectiveTests
{
    private static ElectricPropulsionEngineDesign HetBaseline() => new(
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

    private static ResistojetConditions VacuumConditions() => new(
        BusVoltage_V:        300.0,
        BusPower_W_avail:    5000.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.Xenon,
        InletTemperature_K:  300.0,
        InletComposition:    PropellantInletComposition.PureH2);

    [Fact]
    public void Pack_Unpack_RoundTrip_PreservesVector()
    {
        var baseline = HetBaseline();
        double[] vec = HetObjective.Pack(baseline);
        var rebuilt = HetObjective.Unpack(vec, baseline);
        double[] vec2 = HetObjective.Pack(rebuilt);
        Assert.Equal(vec.Length, vec2.Length);
        for (int i = 0; i < vec.Length; i++)
            Assert.Equal(vec[i], vec2[i]);
    }

    [Fact]
    public void Unpack_PreservesCategoricalFields()
    {
        var baseline = HetBaseline() with
        {
            AnodeMaterial = AnodeMaterial.BoronNitride,
            CathodeType   = CathodeType.FilamentCathode,
        };
        double[] vec = HetObjective.Pack(baseline);
        var rebuilt = HetObjective.Unpack(vec, baseline);
        Assert.Equal(AnodeMaterial.BoronNitride, rebuilt.AnodeMaterial);
        Assert.Equal(CathodeType.FilamentCathode, rebuilt.CathodeType);
        Assert.Equal(ElectricPropulsionEngineKind.HallEffect, rebuilt.Kind);
    }

    [Fact]
    public void Build_BusPowerClip_AppliesToDischargeCurrent()
    {
        // BusPower 1500 W with V_d_max = 400 V → I_d_max = 3.75 A. Default
        // upper bound is 25 A, so the clip moves it down.
        var conds = VacuumConditions() with { BusPower_W_avail = 1500.0 };
        var obj = HetObjective.Build(conds, HetBaseline());
        Assert.Equal(6, obj.DimensionCount);
        var idVar = obj.Variables[1];
        Assert.Equal("DischargeCurrent_A", idVar.Name);
        Assert.True(idVar.Max <= 4.0,
            $"DischargeCurrent_A upper bound should be clipped near 3.75 A; got {idVar.Max}.");
    }

    [Fact]
    public void Build_OnResistojetBaseline_Throws()
    {
        var resistojet = HetBaseline() with { Kind = ElectricPropulsionEngineKind.Resistojet };
        Assert.Throws<System.ArgumentException>(
            () => HetObjective.Build(VacuumConditions(), resistojet));
    }
}
