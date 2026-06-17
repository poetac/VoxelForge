// ElectricPropulsionCostEstimatorsTests.cs — Sprint EC.W9 unit tests
// for the kind-aware EP cost estimator.

using Voxelforge.ElectricPropulsion;
using Voxelforge.ElectricPropulsion.Economics;
using Voxelforge.ElectricPropulsion.Engines;
using Voxelforge.ElectricPropulsion.IO;
using Voxelforge.Economics;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests;

public sealed class ElectricPropulsionCostEstimatorsTests
{
    [Fact]
    public void Resistojet_HasCostMassAndCO2()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(
            DesignSeed(), ConditionsSeed());
        var est = ElectricPropulsionCostEstimators
            .ForElectricPropulsionThruster("resistojet", result);
        Assert.True(est.Mass_kg > 0);
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.EmbodiedCO2_kgCO2eq > 0);
    }

    [Fact]
    public void Co2_TracksMassAt16kgCO2_per_kg()
    {
        // EC.W9 invariant: CO2 = mass × 16.
        var result = ElectricPropulsionOptimization.GenerateWith(
            DesignSeed(), ConditionsSeed());
        var est = ElectricPropulsionCostEstimators
            .ForElectricPropulsionThruster("res", result);
        Assert.Equal(est.Mass_kg * 16.0, est.EmbodiedCO2_kgCO2eq, precision: 4);
    }

    [Fact]
    public void Rollup_OfResistojetIntoBreakdown_HasSingleComponent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(
            DesignSeed(), ConditionsSeed());
        var est = ElectricPropulsionCostEstimators
            .ForElectricPropulsionThruster("res", result);
        var roll = EconomicAnalyzer.Analyze(new[] { est });
        Assert.Single(roll.Components);
        Assert.Equal(est.CapitalCost_USD, roll.TotalCapitalCost_USD, precision: 4);
    }

    private static ElectricPropulsionEngineDesign DesignSeed() => new(
        Kind:                   ElectricPropulsionEngineKind.Resistojet,
        HeaterPower_W:          870.0,
        PropellantMassFlow_kgs: 1.2e-4,
        NozzleThroatRadius_mm:  0.20,
        NozzleAreaRatio:        100.0,
        HeaterChamberLength_mm: 25.0,
        HeaterChamberRadius_mm: 6.0);

    private static ResistojetConditions ConditionsSeed() => new(
        BusVoltage_V:       28.0,
        BusPower_W_avail:   900.0,
        AmbientPressure_Pa: 0.0,
        Propellant:         Propellant.N2H4Decomposed,
        InletTemperature_K: 900.0,
        InletComposition:   PropellantInletComposition.Hydrazine_Shell405);
}
