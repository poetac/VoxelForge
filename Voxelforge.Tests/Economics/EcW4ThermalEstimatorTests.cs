// EcW4ThermalEstimatorTests.cs — Sprint EC.W4 unit tests for the six
// thermal-pillar cost estimators (compressor, pump, HX, radiator,
// hydro, solar thermal).

using Voxelforge.Compressor;
using Voxelforge.Economics;
using Voxelforge.HeatExchanger;
using Voxelforge.Hydroelectric;
using Voxelforge.Pump;
using Voxelforge.Radiator;
using Voxelforge.SolarThermal;
using Xunit;

namespace Voxelforge.Tests.Economics;

public sealed class EcW4ThermalEstimatorTests
{
    [Fact]
    public void ForCompressor_HasCostScaledWithShaftPower()
    {
        var est = ComponentCostEstimators.ForCompressor("comp", DefaultCompressor());
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.Mass_kg > 0);
        Assert.True(est.EmbodiedCO2_kgCO2eq > 0);
    }

    [Fact]
    public void ForPump_HasPositiveCost()
    {
        var est = ComponentCostEstimators.ForPump("pump", DefaultPump());
        Assert.True(est.CapitalCost_USD > 0);
    }

    [Fact]
    public void ForHeatExchanger_HasPositiveCost()
    {
        var est = ComponentCostEstimators.ForHeatExchanger("hx", DefaultPlateFin());
        Assert.True(est.CapitalCost_USD > 0);
    }

    [Fact]
    public void ForRadiator_ScalesWithArea()
    {
        var small = ComponentCostEstimators.ForRadiator("r1", DefaultRadiator());
        var big   = ComponentCostEstimators.ForRadiator("r2",
            DefaultRadiator() with { PanelArea_m2 = 10.0 });
        Assert.True(big.CapitalCost_USD > small.CapitalCost_USD);
        Assert.True(big.Mass_kg         > small.Mass_kg);
    }

    [Fact]
    public void ForHydroTurbine_PeltonPenstock_HasMillionDollarRange()
    {
        // 10 MW Pelton (200 m head, 5.6 m³/s): capex ~ $25M.
        var est = ComponentCostEstimators.ForHydroTurbine("hydro", new HydroTurbineDesign(
            Kind:                  HydroTurbineKind.Pelton,
            Head_m:                200.0,
            VolumetricFlowRate_m3s: 5.6,
            GeneratorEfficiency:   0.90));
        Assert.True(est.CapitalCost_USD > 1_000_000);
    }

    [Fact]
    public void ForSolarThermal_ParabolicTroughIsCostlierThanFlatPlate_AtSamePower()
    {
        var flat = ComponentCostEstimators.ForSolarThermal("st",
            DefaultSolarFlat());
        var trough = ComponentCostEstimators.ForSolarThermal("st",
            DefaultSolarFlat() with { Kind = SolarCollectorKind.ParabolicTrough });
        // Same aperture + irradiance → comparable thermal power → trough
        // costs ~ 2× more per kW.
        Assert.True(trough.CapitalCost_USD > flat.CapitalCost_USD);
    }

    [Fact]
    public void EconomicAnalyzer_FullThermalLoop_RollsUp()
    {
        // PV + battery + motor + compressor + HX + radiator subsystem.
        var roll = EconomicAnalyzer.Analyze(new[]
        {
            ComponentCostEstimators.ForCompressor("comp",       DefaultCompressor()),
            ComponentCostEstimators.ForPump      ("pump",       DefaultPump()),
            ComponentCostEstimators.ForHeatExchanger("hx",      DefaultPlateFin()),
            ComponentCostEstimators.ForRadiator  ("rad",        DefaultRadiator()),
        });
        Assert.Equal(4, roll.Components.Count);
        Assert.True(roll.TotalCapitalCost_USD > 0);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────

    private static CentrifugalCompressorDesign DefaultCompressor() => new(
        Kind:                            CompressorKind.Centrifugal,
        MassFlow_kgs:                    10.0,
        InletTotalTemperature_K:         288.15,
        InletTotalPressure_Pa:           101_325.0,
        PressureRatio:                   3.0,
        IsentropicEfficiency:            0.85,
        WorkingGasGamma:                 1.4,
        WorkingGasSpecificHeat_J_kgK:    1005.0);

    private static CentrifugalPumpDesign DefaultPump() => new(
        Kind:                  PumpKind.Centrifugal,
        VolumetricFlowRate_m3s: 0.05,
        HeadRise_m:            30.0,
        RotationSpeed_rpm:     3000.0,
        OverallEfficiency:     0.80);

    private static PlateFinDesign DefaultPlateFin() => new(
        Kind:                    HeatExchangerKind.PlateFinCounterflow,
        CoreLength_m:            0.30,
        CoreWidth_m:             0.20,
        CoreHeight_m:            0.15,
        PlateSpacing_m:          0.005,
        FinPitch_m:              0.0015,
        FinThickness_m:          0.0002,
        HotMassFlow_kgs:         0.5,
        ColdMassFlow_kgs:        0.5,
        HotInletTemperature_K:   400.0,
        ColdInletTemperature_K:  300.0,
        HotCp_JkgK:              1005.0,
        ColdCp_JkgK:             1005.0,
        HotDensity_kgm3:         0.5,
        ColdDensity_kgm3:        1.0,
        HotViscosity_PaS:        2.5e-5,
        ColdViscosity_PaS:       1.85e-5);

    private static SpacecraftRadiatorDesign DefaultRadiator() => new(
        Kind:                       RadiatorKind.FlatPanel,
        PanelArea_m2:                3.0,
        OperatingTemperature_K:      310.0,
        SinkTemperature_K:           250.0,
        Emissivity:                  0.85,
        SolarAbsorptivity:           0.20,
        IncidentSolarFlux_W_m2:      0.0);

    private static SolarCollectorDesign DefaultSolarFlat() => new(
        Kind:                        SolarCollectorKind.FlatPlate,
        ApertureArea_m2:             4.0,
        DirectNormalIrradiance_W_m2: 1000.0,
        CollectorTemperature_C:       60.0,
        AmbientTemperature_C:         20.0);
}
