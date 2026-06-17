// EcW7RocketEngineCostTests.cs — Sprint EC.W7 unit tests for the
// regen-cooled rocket-engine cost factory.

using Voxelforge.Combustion;
using Voxelforge.Economics;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Economics;

public sealed class EcW7RocketEngineCostTests
{
    [Fact]
    public void ForRegenRocketEngine_DefaultDesign_HasPositiveCost()
    {
        var gen = GenerateSmallEngine();
        var est = ComponentCostEstimators.ForRegenRocketEngine("engine", gen);
        Assert.True(est.Mass_kg > 0);
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.EmbodiedCO2_kgCO2eq > 0);
    }

    [Fact]
    public void ForRegenRocketEngine_CapexIsManufacturingPrintCostPlusOverhead()
    {
        // EC.W7 invariant: capex = print × 1.30 (30 % overhead).
        var gen = GenerateSmallEngine();
        var est = ComponentCostEstimators.ForRegenRocketEngine("engine", gen);
        double printCost = gen.Manufacturing.EstimatedBuildCost_USD;
        Assert.Equal(printCost * 1.30, est.CapitalCost_USD, precision: 4);
    }

    [Fact]
    public void ForRegenRocketEngine_MassIsChamberGeometryMass()
    {
        var gen = GenerateSmallEngine();
        var est = ComponentCostEstimators.ForRegenRocketEngine("engine", gen);
        Assert.Equal(gen.Geometry.TotalMass_g / 1000.0, est.Mass_kg, precision: 6);
    }

    [Fact]
    public void ForRegenRocketEngine_CO2_Is22kgPerKgSuperalloy()
    {
        var gen = GenerateSmallEngine();
        var est = ComponentCostEstimators.ForRegenRocketEngine("engine", gen);
        Assert.Equal(est.Mass_kg * 22.0, est.EmbodiedCO2_kgCO2eq, precision: 4);
    }

    [Fact]
    public void EconomicAnalyzer_RocketEngineRolledIntoStack()
    {
        // A rocket + battery in the same rollup. Capex should = engine
        // + pack.
        var gen = GenerateSmallEngine();
        var roll = EconomicAnalyzer.Analyze(new[]
        {
            ComponentCostEstimators.ForRegenRocketEngine("engine", gen),
            ComponentCostEstimators.ForBattery("pack", ModelSPack()),
        });
        Assert.Equal(2, roll.Components.Count);
        Assert.True(roll.TotalCapitalCost_USD > 0);
    }

    private static RegenGenerationResult GenerateSmallEngine()
    {
        var cond = new OperatingConditions
        {
            PropellantPair = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 40,
        };
        return RegenChamberOptimization.GenerateWith(cond, design,
            skipVoxelGeometry: true);
    }

    private static Voxelforge.Battery.BatteryPackDesign ModelSPack() => new(
        Chemistry:        Voxelforge.Battery.BatteryChemistry.NickelManganeseCobalt,
        CellsInSeries:    96,
        ParallelStrings:  46,
        StateOfCharge:    1.0,
        LoadCurrent_A:    0.0);
}
