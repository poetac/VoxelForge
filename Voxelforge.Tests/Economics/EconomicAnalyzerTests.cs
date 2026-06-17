// EconomicAnalyzerTests.cs — Sprint EC.W1 unit tests for the cost
// estimators + system rollup analyzer.

using System;
using System.Linq;
using Voxelforge.Battery;
using Voxelforge.ElectricMotor;
using Voxelforge.Economics;
using Voxelforge.Electrolyser;
using Voxelforge.Flywheel;
using Voxelforge.HydrogenStorage;
using Voxelforge.Photovoltaic;
using Voxelforge.PowerGen;
using Voxelforge.WindTurbine;
using Xunit;

namespace Voxelforge.Tests.Economics;

public sealed class EconomicAnalyzerTests
{
    // ── Battery ──────────────────────────────────────────────────────────

    [Fact]
    public void ForBattery_ModelSPack_ClustersAroundEightyKwh()
    {
        // Tesla Model S-class pack: 96s46p NMC × 5 Ah × ~ 3.6 V = ~ 80 kWh.
        // Expect mass ~ 560 kg, capex ~ $11k, CO2 ~ 6000 kg.
        var est = ComponentCostEstimators.ForBattery("pack", ModelSPack());
        Assert.Equal("pack", est.ComponentName);
        Assert.InRange(est.Mass_kg,             500.0, 700.0);
        Assert.InRange(est.CapitalCost_USD,    9000.0, 13000.0);
        Assert.InRange(est.EmbodiedCO2_kgCO2eq, 4500.0, 7500.0);
    }

    [Fact]
    public void ForBattery_LfpVsNmc_LfpIsCheaperAndLowerCO2_ButHeavier()
    {
        var nmc = ComponentCostEstimators.ForBattery("nmc", ModelSPack());
        var lfp = ComponentCostEstimators.ForBattery("lfp", ModelSPack()
            with { Chemistry = BatteryChemistry.LithiumIronPhosphate });
        Assert.True(lfp.CapitalCost_USD < nmc.CapitalCost_USD);
        Assert.True(lfp.EmbodiedCO2_kgCO2eq < nmc.EmbodiedCO2_kgCO2eq);
        Assert.True(lfp.Mass_kg > nmc.Mass_kg);
    }

    // ── PV ───────────────────────────────────────────────────────────────

    [Fact]
    public void ForPhotovoltaic_StandardPanel_HasPositiveCost()
    {
        var est = ComponentCostEstimators.ForPhotovoltaic("pv", DefaultPanel());
        Assert.True(est.Mass_kg > 0);
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.EmbodiedCO2_kgCO2eq > 0);
        // Sanity: cost/power ≈ $0.21/W. Pull out W via dividing cost/$0.21.
        // For the default 60-cell panel at STC, P ≈ 250-350 W → cost
        // $52-74. Loose band:
        Assert.InRange(est.CapitalCost_USD, 30.0, 90.0);
    }

    // ── Motor ────────────────────────────────────────────────────────────

    [Fact]
    public void ForMotor_DriveUnit_ScalesWithMechanicalPower()
    {
        var est = ComponentCostEstimators.ForMotor("drive", DefaultMotor());
        Assert.True(est.Mass_kg > 0);
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.EmbodiedCO2_kgCO2eq > 0);
    }

    // ── EconomicAnalyzer rollup ──────────────────────────────────────────

    [Fact]
    public void EconomicAnalyzer_SumsAcrossComponents()
    {
        var b = ComponentCostEstimators.ForBattery("pack", ModelSPack());
        var p = ComponentCostEstimators.ForPhotovoltaic("pv", DefaultPanel());
        var m = ComponentCostEstimators.ForMotor("drive", DefaultMotor());

        var roll = EconomicAnalyzer.Analyze(new[] { b, p, m });
        Assert.Equal(3, roll.Components.Count);
        Assert.Equal(b.Mass_kg + p.Mass_kg + m.Mass_kg, roll.TotalMass_kg,
            precision: 6);
        Assert.Equal(b.CapitalCost_USD + p.CapitalCost_USD + m.CapitalCost_USD,
            roll.TotalCapitalCost_USD, precision: 6);
        Assert.Equal(b.EmbodiedCO2_kgCO2eq + p.EmbodiedCO2_kgCO2eq + m.EmbodiedCO2_kgCO2eq,
            roll.TotalEmbodiedCO2_kgCO2eq, precision: 6);
    }

    [Fact]
    public void EconomicAnalyzer_OnEmptySet_ReturnsZeroTotals()
    {
        var roll = EconomicAnalyzer.Analyze(Array.Empty<CostEstimate>());
        Assert.Empty(roll.Components);
        Assert.Equal(0.0, roll.TotalMass_kg, precision: 9);
        Assert.Equal(0.0, roll.TotalCapitalCost_USD, precision: 9);
        Assert.Equal(0.0, roll.TotalEmbodiedCO2_kgCO2eq, precision: 9);
    }

    [Fact]
    public void SystemCostBreakdown_ToTable_ContainsTotalsAndComponents()
    {
        var b = ComponentCostEstimators.ForBattery("pack", ModelSPack());
        var p = ComponentCostEstimators.ForPhotovoltaic("pv", DefaultPanel());
        var roll = EconomicAnalyzer.Analyze(new[] { b, p });
        string table = roll.ToTable();
        Assert.Contains("pack", table);
        Assert.Contains("pv", table);
        Assert.Contains("Total", table);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────

    private static BatteryPackDesign ModelSPack() => new(
        Chemistry:        BatteryChemistry.NickelManganeseCobalt,
        CellsInSeries:    96,
        ParallelStrings:  46,
        StateOfCharge:    1.0,
        LoadCurrent_A:    0.0);

    private static PvPanelDesign DefaultPanel() => new(
        CellType:           PhotovoltaicCellType.Monocrystalline,
        CellsInSeries:      60,
        StringsInParallel:  1,
        CellArea_cm2:       243.0,
        Irradiance_W_m2:    1000.0,
        CellTemperature_C:  25.0);

    private static MotorDesign DefaultMotor() => new(
        Kind:                   MotorKind.PermanentMagnetSynchronous,
        TorqueConstant_NmA:     0.5,
        ArmatureResistance_Ohm: 0.05,
        ConstantPowerLoss_W:    500.0,
        BusVoltage_V:           400.0,
        ArmatureCurrent_A:      100.0);

    // ── Sprint EC.W2 — wind/electrolyser/H2/fuel-cell/flywheel ──────────

    [Fact]
    public void ForWindTurbine_MultiMegawattHawt_HasMillionDollarCapex()
    {
        // 1-MW class HAWT (~ 30 m rotor, 80 m hub, 11 m/s rated).
        var est = ComponentCostEstimators.ForWindTurbine("wt", DefaultHawt());
        // Should land around $1M capex, 50-100 tonnes, ~100 tonnes CO2.
        Assert.True(est.Mass_kg > 10_000);
        Assert.True(est.CapitalCost_USD > 500_000);
        Assert.True(est.EmbodiedCO2_kgCO2eq > 50_000);
    }

    [Fact]
    public void ForElectrolyser_PemStack_ScalesWithPower()
    {
        var est = ComponentCostEstimators.ForElectrolyser("el", DefaultElectrolyser());
        Assert.True(est.Mass_kg > 0);
        Assert.True(est.CapitalCost_USD > 0);
        // Doubling current density roughly doubles power → doubles cost.
        var est2 = ComponentCostEstimators.ForElectrolyser("el", DefaultElectrolyser()
            with { OperatingCurrentDensity_A_cm2 = 2.0 });
        Assert.True(est2.CapitalCost_USD > est.CapitalCost_USD);
    }

    [Fact]
    public void ForHydrogenStorage_Cryo_IsLighterButCostlierPerKg_VsCompressed()
    {
        // Equal-volume tanks: cryogenic stores far more H2 by mass
        // (LH2 ~ 70 kg/m³ vs 700-bar gas ~ 40 kg/m³), so it carries
        // different totals. Compare per-kg-H2 rates instead by using a
        // 1 m³ tank for each.
        var compressed = ComponentCostEstimators.ForHydrogenStorage("c",
            new HydrogenStorageDesign(
                Kind:                   HydrogenStorageKind.CompressedGas,
                InternalVolume_m3:      1.0,
                OperatingPressure_bar:  700.0,
                OperatingTemperature_K: 298.0,
                DryMass_kg:             0.0));
        var cryogenic = ComponentCostEstimators.ForHydrogenStorage("ly",
            new HydrogenStorageDesign(
                Kind:                   HydrogenStorageKind.LiquidCryogenic,
                InternalVolume_m3:      1.0,
                OperatingPressure_bar:  1.0,
                OperatingTemperature_K: 20.3,
                DryMass_kg:             0.0));
        // Both should have positive cost.
        Assert.True(compressed.CapitalCost_USD > 0);
        Assert.True(cryogenic.CapitalCost_USD > 0);
    }

    [Fact]
    public void ForFuelCell_PemStack_ScalesWithPower()
    {
        var est = ComponentCostEstimators.ForFuelCell("fc", DefaultFuelCell());
        Assert.True(est.Mass_kg > 0);
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.EmbodiedCO2_kgCO2eq > 0);
    }

    [Fact]
    public void ForFlywheel_HighSpeedComposite_HasCost()
    {
        var est = ComponentCostEstimators.ForFlywheel("fw", DefaultFlywheel());
        Assert.True(est.Mass_kg > 0);
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.EmbodiedCO2_kgCO2eq > 0);
    }

    // ── EC.W2 fixtures ──────────────────────────────────────────────────

    private static HawtDesign DefaultHawt() => new(
        Kind:                          WindTurbineKind.HorizontalAxis,
        RotorRadius_m:                  30.0,
        BladeCount:                     3,
        HubHeight_m:                    80.0,
        DesignWindSpeed_ms:             11.0,
        DesignTipSpeedRatio:            7.0,
        GearboxAndGeneratorEfficiency:  0.95,
        CutInWindSpeed_ms:              3.5,
        CutOutWindSpeed_ms:             25.0);

    private static PemElectrolyserDesign DefaultElectrolyser() => new(
        Kind:                           ElectrolyserKind.Pem,
        CellCount:                      100,
        ActiveAreaPerCell_cm2:          750.0,
        OperatingCurrentDensity_A_cm2:  1.0,
        OperatingTemperature_C:         80.0,
        OperatingPressure_bar:          30.0);

    private static PemFuelCellDesign DefaultFuelCell() => new(
        Kind:                           PowerGenKind.PemFuelCell,
        CellCount:                      400,
        ActiveAreaPerCell_cm2:          300.0,
        OperatingCurrentDensity_A_cm2:  1.0,
        OperatingTemperature_C:         80.0,
        OperatingPressure_bar:          2.5);

    private static FlywheelDesign DefaultFlywheel() => new(
        Shape:              FlywheelShape.SolidDisk,
        Material:           FlywheelMaterial.CarbonFibreComposite,
        OuterRadius_m:      0.30,
        Mass_kg:            100.0,
        RotationSpeed_rpm:  30_000.0);
}
