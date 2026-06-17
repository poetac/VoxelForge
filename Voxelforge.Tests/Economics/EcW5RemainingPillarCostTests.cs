// EcW5RemainingPillarCostTests.cs — Sprint EC.W5 unit tests for cost
// estimators across the remaining 7 pillars: Stirling, TEG, Tank,
// HeatPipe, Antenna, Chemical Reactor, Refrigeration.

using Voxelforge.Antenna;
using Voxelforge.Chemical;
using Voxelforge.Economics;
using Voxelforge.HeatPipe;
using Voxelforge.Refrigeration;
using Voxelforge.Stirling;
using Voxelforge.Tankage;
using Voxelforge.Thermoelectric;
using Xunit;

namespace Voxelforge.Tests.Economics;

public sealed class EcW5RemainingPillarCostTests
{
    [Fact]
    public void ForStirling_BetaConfig_HasMillionDollarCostPerMW()
    {
        // Designs with ~ 10 kW indicated power → $15k capex band.
        var est = ComponentCostEstimators.ForStirling("stirling",
            DefaultStirling());
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.Mass_kg > 0);
    }

    [Fact]
    public void ForThermoelectric_BiTeStack_HasPositiveCost()
    {
        var est = ComponentCostEstimators.ForThermoelectric("teg",
            DefaultTeg());
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.Mass_kg > 0);
    }

    [Fact]
    public void ForPressureVessel_SteelShell_HasPositiveCost()
    {
        var est = ComponentCostEstimators.ForPressureVessel("tank",
            DefaultTank());
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.Mass_kg > 0);
    }

    [Fact]
    public void ForHeatPipe_ScalesLinearlyWithThroughput()
    {
        var small = ComponentCostEstimators.ForHeatPipe("hp1",
            new HeatPipeDesign(
                Fluid:                  HeatPipeFluid.Water,
                InternalDiameter_m:     0.010,
                Length_m:               0.30,
                HeatThroughput_W:       100.0,
                OperatingTemperature_K: 350.0));
        var large = ComponentCostEstimators.ForHeatPipe("hp2",
            new HeatPipeDesign(
                Fluid:                  HeatPipeFluid.Water,
                InternalDiameter_m:     0.010,
                Length_m:               0.30,
                HeatThroughput_W:       1000.0,
                OperatingTemperature_K: 350.0));
        Assert.Equal(10.0, large.CapitalCost_USD / small.CapitalCost_USD, precision: 6);
        Assert.Equal(10.0, large.Mass_kg         / small.Mass_kg,         precision: 6);
    }

    [Fact]
    public void ForAntenna_OmniMode_GivesFlatScaffoldCost()
    {
        // No dish diameter → scaffold cost (omni / monopole).
        var est = ComponentCostEstimators.ForAntenna("ant", new AntennaLinkDesign(
            TransmitAntennaKind:    AntennaKind.IdealIsotropic,
            ReceiveAntennaKind:     AntennaKind.IdealIsotropic,
            Frequency_Hz:           2.4e9,
            TransmitPower_W:        5.0,
            LinkDistance_m:         1000.0));
        Assert.Equal(500.0, est.CapitalCost_USD, precision: 6);
    }

    [Fact]
    public void ForAntenna_LargeDish_ScalesWithApertureArea()
    {
        var small = ComponentCostEstimators.ForAntenna("a1", new AntennaLinkDesign(
            TransmitAntennaKind:     AntennaKind.ParabolicDish,
            ReceiveAntennaKind:      AntennaKind.IdealIsotropic,
            Frequency_Hz:            2.4e9,
            TransmitPower_W:         50.0,
            LinkDistance_m:          1e6,
            TransmitDishDiameter_m:  1.0));
        var large = ComponentCostEstimators.ForAntenna("a2", new AntennaLinkDesign(
            TransmitAntennaKind:     AntennaKind.ParabolicDish,
            ReceiveAntennaKind:      AntennaKind.IdealIsotropic,
            Frequency_Hz:            2.4e9,
            TransmitPower_W:         50.0,
            LinkDistance_m:          1e6,
            TransmitDishDiameter_m:  2.0));
        // 2× diameter → 4× area → 4× cost.
        Assert.Equal(4.0, large.CapitalCost_USD / small.CapitalCost_USD, precision: 6);
    }

    [Fact]
    public void ForChemicalReactor_ScalesWithVolume()
    {
        var small = ComponentCostEstimators.ForChemicalReactor("c1",
            DefaultReactor() with { ReactorVolume_m3 = 1.0 });
        var big = ComponentCostEstimators.ForChemicalReactor("c2",
            DefaultReactor() with { ReactorVolume_m3 = 10.0 });
        Assert.Equal(10.0, big.CapitalCost_USD / small.CapitalCost_USD, precision: 6);
    }

    [Fact]
    public void ForRefrigeration_HasPositiveCost()
    {
        var est = ComponentCostEstimators.ForRefrigeration("rfg",
            DefaultRefrigeration());
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.Mass_kg > 0);
    }

    [Fact]
    public void FullStackEcW5_Rollup_CoversSevenPillars()
    {
        var roll = EconomicAnalyzer.Analyze(new[]
        {
            ComponentCostEstimators.ForStirling       ("s",   DefaultStirling()),
            ComponentCostEstimators.ForThermoelectric ("teg", DefaultTeg()),
            ComponentCostEstimators.ForPressureVessel ("t",   DefaultTank()),
            ComponentCostEstimators.ForHeatPipe       ("hp",  DefaultHeatPipe()),
            ComponentCostEstimators.ForAntenna        ("ant", DefaultAntennaDish()),
            ComponentCostEstimators.ForChemicalReactor("rx",  DefaultReactor()),
            ComponentCostEstimators.ForRefrigeration  ("rfg", DefaultRefrigeration()),
        });
        Assert.Equal(7, roll.Components.Count);
        Assert.True(roll.TotalCapitalCost_USD > 0);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────

    private static StirlingDesign DefaultStirling() => new(
        Configuration:        StirlingConfiguration.Beta,
        HotSideTemperature_K: 950.0,
        ColdSideTemperature_K: 350.0,
        MeanPressure_Pa:       3.5e6,
        SweptVolume_m3:        0.002,
        OperatingFrequency_Hz: 30.0,
        SecondLawEfficiency:   0.55);

    private static ThermoelectricGeneratorDesign DefaultTeg() => new(
        Material:               ThermoelectricMaterial.BismuthTelluride,
        HotSideTemperature_K:   500.0,
        ColdSideTemperature_K:  300.0,
        HotSideHeatInput_W:     1000.0);

    private static PressureVesselDesign DefaultTank() => new(
        ShellType:              TankShellType.Steel4130,
        InternalRadius_m:        0.50,
        ShellLength_m:           1.50,
        WallThickness_m:         0.005,
        OperatingPressure_Pa:    3.0e6);

    private static HeatPipeDesign DefaultHeatPipe() => new(
        Fluid:                  HeatPipeFluid.Water,
        InternalDiameter_m:     0.012,
        Length_m:               0.30,
        HeatThroughput_W:       500.0,
        OperatingTemperature_K: 350.0);

    private static AntennaLinkDesign DefaultAntennaDish() => new(
        TransmitAntennaKind:    AntennaKind.ParabolicDish,
        ReceiveAntennaKind:     AntennaKind.ParabolicDish,
        Frequency_Hz:           8.4e9,
        TransmitPower_W:        100.0,
        LinkDistance_m:         400_000_000.0,
        TransmitDishDiameter_m: 1.5,
        ReceiveDishDiameter_m:  34.0);

    private static ReactorDesign DefaultReactor() => new(
        Kind:                         ReactorKind.Cstr,
        ReactorVolume_m3:             1.0,
        VolumetricFlowRate_m3s:       0.001,
        InletConcentration_mol_m3:    1000.0,
        OperatingTemperature_K:       350.0,
        ArrheniusPreExponential_per_s: 1e9,
        ActivationEnergy_J_mol:        80_000.0);

    private static RefrigerationDesign DefaultRefrigeration() => new(
        Mode:                       RefrigerationMode.Cooling,
        Refrigerant:                Refrigerant.R134a,
        ColdReservoirTemperature_K: 273.15,
        HotReservoirTemperature_K:  308.15,
        CompressorPowerInput_W:     5000.0);
}
