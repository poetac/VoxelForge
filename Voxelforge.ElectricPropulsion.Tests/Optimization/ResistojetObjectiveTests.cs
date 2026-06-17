// ResistojetObjectiveTests — Wave-1 acceptance for the 6-dim IObjective
// adapter wrapping the resistojet pipeline. Mirror of HetObjectiveTests
// / ArcjetObjectiveTests. Audit 05-test-gaps.md Section 3 High.

using System;
using Voxelforge.ElectricPropulsion.Optimization;
using Voxelforge.Optimization;

namespace Voxelforge.ElectricPropulsion.Tests.Optimization;

public sealed class ResistojetObjectiveTests
{
    private static ElectricPropulsionEngineDesign ResistojetBaseline() => new(
        Kind:                    ElectricPropulsionEngineKind.Resistojet,
        HeaterPower_W:          1_500.0,
        PropellantMassFlow_kgs: 1.0e-4,
        NozzleThroatRadius_mm:    0.5,
        NozzleAreaRatio:         50.0,
        HeaterChamberLength_mm:  25.0,
        HeaterChamberRadius_mm:   5.0)
    {
        HeaterMaterial          = HeaterMaterial.GrainStabilizedPlatinum,
        ChamberEmissivity       = 0.30,
        ChamberWallThickness_mm = 1.5,
        RadiativelyCooledNozzle = true,
    };

    private static ResistojetConditions VacuumConditions(double busPower_W = 5_000.0) => new(
        BusVoltage_V:        28.0,
        BusPower_W_avail:    busPower_W,
        AmbientPressure_Pa:   0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K:  900.0,
        InletComposition:    PropellantInletComposition.PureH2);

    [Fact]
    public void DefaultBounds_HaveSixDimensions()
    {
        Assert.Equal(6, ResistojetObjective.DefaultBounds.Length);
        Assert.Equal(6, ResistojetObjective.DefaultVariableNames.Length);
    }

    [Fact]
    public void DefaultBounds_NamesMatchVariableNames()
    {
        for (int i = 0; i < ResistojetObjective.DefaultBounds.Length; i++)
            Assert.Equal(ResistojetObjective.DefaultVariableNames[i],
                         ResistojetObjective.DefaultBounds[i].Name);
    }

    [Fact]
    public void DefaultVariableNames_MatchPillarSpecVectorLayout()
    {
        // Pillar spec §2 canonical order — load-bearing for Pack/Unpack.
        Assert.Equal("HeaterPower_W",          ResistojetObjective.DefaultVariableNames[0]);
        Assert.Equal("PropellantMassFlow_kgs", ResistojetObjective.DefaultVariableNames[1]);
        Assert.Equal("NozzleThroatRadius_mm",  ResistojetObjective.DefaultVariableNames[2]);
        Assert.Equal("NozzleAreaRatio",        ResistojetObjective.DefaultVariableNames[3]);
        Assert.Equal("HeaterChamberLength_mm", ResistojetObjective.DefaultVariableNames[4]);
        Assert.Equal("HeaterChamberRadius_mm", ResistojetObjective.DefaultVariableNames[5]);
    }

    [Fact]
    public void DefaultBounds_MatchPillarSpecRanges()
    {
        // Pillar spec §2 documented (Min, Max) per dim.
        Assert.Equal( 200.0,  ResistojetObjective.DefaultBounds[0].Min, precision: 6);
        Assert.Equal(3000.0,  ResistojetObjective.DefaultBounds[0].Max, precision: 6);
        Assert.Equal(1.0e-5,  ResistojetObjective.DefaultBounds[1].Min, precision: 9);
        Assert.Equal(5.0e-4,  ResistojetObjective.DefaultBounds[1].Max, precision: 9);
        Assert.Equal(   0.1,  ResistojetObjective.DefaultBounds[2].Min, precision: 6);
        Assert.Equal(   2.0,  ResistojetObjective.DefaultBounds[2].Max, precision: 6);
        Assert.Equal(  25.0,  ResistojetObjective.DefaultBounds[3].Min, precision: 6);
        Assert.Equal( 150.0,  ResistojetObjective.DefaultBounds[3].Max, precision: 6);
        Assert.Equal(   5.0,  ResistojetObjective.DefaultBounds[4].Min, precision: 6);
        Assert.Equal(  50.0,  ResistojetObjective.DefaultBounds[4].Max, precision: 6);
        Assert.Equal(   2.0,  ResistojetObjective.DefaultBounds[5].Min, precision: 6);
        Assert.Equal(  15.0,  ResistojetObjective.DefaultBounds[5].Max, precision: 6);
    }

    [Fact]
    public void Pack_ReturnsSixDimensionalVector_InCanonicalOrder()
    {
        var v = ResistojetObjective.Pack(ResistojetBaseline());
        Assert.Equal(6, v.Length);
        Assert.Equal(1_500.0, v[0]);
        Assert.Equal(1.0e-4,  v[1]);
        Assert.Equal(    0.5, v[2]);
        Assert.Equal(   50.0, v[3]);
        Assert.Equal(   25.0, v[4]);
        Assert.Equal(    5.0, v[5]);
    }

    [Fact]
    public void Pack_NullDesign_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ResistojetObjective.Pack(null!));
    }

    [Fact]
    public void Unpack_RoundTripsThroughPack()
    {
        var baseline = ResistojetBaseline();
        var v = ResistojetObjective.Pack(baseline);
        var rebuilt = ResistojetObjective.Unpack(v, baseline);
        var v2 = ResistojetObjective.Pack(rebuilt);
        Assert.Equal(v.Length, v2.Length);
        for (int i = 0; i < v.Length; i++)
            Assert.Equal(v[i], v2[i], precision: 9);
    }

    [Fact]
    public void Unpack_PreservesCategoricalState()
    {
        // Per the EngineObjectiveAdapter contract: categorical fields
        // (HeaterMaterial / ChamberEmissivity / wall / RadiativelyCooledNozzle)
        // ride from the baseline through Unpack untouched.
        var baseline = ResistojetBaseline() with
        {
            HeaterMaterial          = HeaterMaterial.TungstenRhenium,
            ChamberEmissivity       = 0.55,
            ChamberWallThickness_mm = 2.0,
            RadiativelyCooledNozzle = false,
        };
        var v = ResistojetObjective.Pack(baseline);
        var rebuilt = ResistojetObjective.Unpack(v, baseline);

        Assert.Equal(HeaterMaterial.TungstenRhenium,         rebuilt.HeaterMaterial);
        Assert.Equal(0.55,                                   rebuilt.ChamberEmissivity, precision: 6);
        Assert.Equal(2.0,                                    rebuilt.ChamberWallThickness_mm, precision: 6);
        Assert.False(rebuilt.RadiativelyCooledNozzle);
        Assert.Equal(ElectricPropulsionEngineKind.Resistojet, rebuilt.Kind);
    }

    [Fact]
    public void Unpack_NullVector_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ResistojetObjective.Unpack(null!, ResistojetBaseline()));
    }

    [Fact]
    public void Unpack_NullBaseline_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ResistojetObjective.Unpack(new double[6], null!));
    }

    [Fact]
    public void Unpack_WrongVectorLength_Throws()
    {
        var baseline = ResistojetBaseline();
        Assert.Throws<ArgumentException>(
            () => ResistojetObjective.Unpack(new double[5], baseline));
        Assert.Throws<ArgumentException>(
            () => ResistojetObjective.Unpack(new double[7], baseline));
    }

    [Fact]
    public void Build_DefaultBounds_Returns6DimAdapter()
    {
        var obj = ResistojetObjective.Build(VacuumConditions(), ResistojetBaseline());
        Assert.Equal(6, obj.Variables.Count);
        Assert.Equal(6, obj.DimensionCount);
    }

    [Fact]
    public void Build_NullConditions_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ResistojetObjective.Build(null!, ResistojetBaseline()));
    }

    [Fact]
    public void Build_NullBaseline_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ResistojetObjective.Build(VacuumConditions(), null!));
    }

    [Fact]
    public void Build_VariablesMatchPillarSpec()
    {
        // First call into Build (without a custom variables array) must apply
        // the bind-time bus-power clip to dim 0 and leave the rest at the
        // documented defaults.
        var conds = VacuumConditions(busPower_W: 5_000.0);  // > default 3000 cap
        var obj = ResistojetObjective.Build(conds, ResistojetBaseline());
        Assert.Equal("HeaterPower_W",          obj.Variables[0].Name);
        Assert.Equal("PropellantMassFlow_kgs", obj.Variables[1].Name);
        Assert.Equal("NozzleThroatRadius_mm",  obj.Variables[2].Name);
        Assert.Equal("NozzleAreaRatio",        obj.Variables[3].Name);
        Assert.Equal("HeaterChamberLength_mm", obj.Variables[4].Name);
        Assert.Equal("HeaterChamberRadius_mm", obj.Variables[5].Name);
    }

    [Fact]
    public void Build_HighBusPower_HeaterPowerUpperBoundIsDefaultMax()
    {
        // Bus 5 kW > default 3 kW cap → no clip; upper bound stays at 3000.
        var obj = ResistojetObjective.Build(VacuumConditions(busPower_W: 5_000.0),
                                            ResistojetBaseline());
        Assert.Equal(3000.0, obj.Variables[0].Max, precision: 3);
    }

    [Fact]
    public void Build_LowBusPower_HeaterPowerUpperBoundClippedToBusPower()
    {
        // Bus 1.5 kW < default 3 kW cap → upper bound clipped to bus power.
        var obj = ResistojetObjective.Build(VacuumConditions(busPower_W: 1_500.0),
                                            ResistojetBaseline());
        Assert.Equal("HeaterPower_W", obj.Variables[0].Name);
        Assert.Equal(1_500.0, obj.Variables[0].Max, precision: 3);
    }

    [Fact]
    public void Build_VeryLowBusPower_LowerBoundDropsToTrackMax()
    {
        // Bus 100 W well below default min 200 W. The Build factory's
        // clip must keep Min <= Max to avoid an invalid bounds tuple.
        var obj = ResistojetObjective.Build(VacuumConditions(busPower_W: 100.0),
                                            ResistojetBaseline());
        Assert.True(obj.Variables[0].Min <= obj.Variables[0].Max,
            $"HeaterPower bounds invalid: Min={obj.Variables[0].Min} > Max={obj.Variables[0].Max}");
        Assert.Equal(100.0, obj.Variables[0].Max, precision: 3);
    }

    [Fact]
    public void Build_CustomVariables_BypassesBusPowerClip()
    {
        // When the caller supplies an explicit variables list the bus-power
        // clip does not fire.
        var custom = new[]
        {
            new DesignVariableInfo("HeaterPower_W",            500.0,  800.0),
            new DesignVariableInfo("PropellantMassFlow_kgs",   1e-5,   3e-4),
            new DesignVariableInfo("NozzleThroatRadius_mm",    0.2,    1.5),
            new DesignVariableInfo("NozzleAreaRatio",         30.0,  120.0),
            new DesignVariableInfo("HeaterChamberLength_mm",  10.0,   40.0),
            new DesignVariableInfo("HeaterChamberRadius_mm",   3.0,   10.0),
        };
        var obj = ResistojetObjective.Build(
            VacuumConditions(busPower_W: 50.0), ResistojetBaseline(), custom);
        Assert.Equal(500.0, obj.Variables[0].Min, precision: 3);
        Assert.Equal(800.0, obj.Variables[0].Max, precision: 3);
    }

    [Fact]
    public void Build_WrongVectorCount_Throws()
    {
        var custom = new[]
        {
            new DesignVariableInfo("HeaterPower_W",            500.0,  800.0),
            new DesignVariableInfo("PropellantMassFlow_kgs",   1e-5,   3e-4),
        };
        Assert.Throws<ArgumentException>(
            () => ResistojetObjective.Build(VacuumConditions(), ResistojetBaseline(), custom));
    }
}
