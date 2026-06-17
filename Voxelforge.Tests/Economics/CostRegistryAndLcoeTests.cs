// CostRegistryAndLcoeTests.cs — Sprint EC.W3 unit tests for the
// CostRegistry side-helper and the LcoeCalculator.

using System;
using Voxelforge.Battery;
using Voxelforge.Economics;
using Voxelforge.Photovoltaic;
using Voxelforge.WindTurbine;
using Xunit;

namespace Voxelforge.Tests.Economics;

public sealed class CostRegistryAndLcoeTests
{
    // ── CostRegistry ─────────────────────────────────────────────────────

    [Fact]
    public void CostRegistry_BuildsBreakdownFromFactories()
    {
        var pack = ModelSPack();
        var panel = DefaultPanel();
        var costs = new CostRegistry();
        costs.Register("pack",
            () => ComponentCostEstimators.ForBattery("pack", pack));
        costs.Register("pv",
            () => ComponentCostEstimators.ForPhotovoltaic("pv", panel));
        var r = costs.BuildBreakdown();
        Assert.Equal(2, r.Components.Count);
        Assert.True(r.TotalCapitalCost_USD > 0);
    }

    [Fact]
    public void CostRegistry_RejectsDuplicateRegistration()
    {
        var costs = new CostRegistry();
        costs.Register("pack",
            () => ComponentCostEstimators.ForBattery("pack", ModelSPack()));
        Assert.Throws<InvalidOperationException>(
            () => costs.Register("pack",
                () => ComponentCostEstimators.ForBattery("pack", ModelSPack())));
    }

    [Fact]
    public void CostRegistry_ReinvocationPicksUpFactoryClosures()
    {
        // The factory captures a mutable design — change the design and
        // BuildBreakdown re-invokes, picking up the new cost.
        var design = ModelSPack();
        var costs = new CostRegistry();
        costs.Register("pack",
            () => ComponentCostEstimators.ForBattery("pack", design));
        double cost1 = costs.BuildBreakdown().TotalCapitalCost_USD;
        // Re-register with a doubled parallel-string count → roughly
        // doubled capex.
        var costs2 = new CostRegistry();
        var design2 = design with { ParallelStrings = design.ParallelStrings * 2 };
        costs2.Register("pack",
            () => ComponentCostEstimators.ForBattery("pack", design2));
        double cost2 = costs2.BuildBreakdown().TotalCapitalCost_USD;
        Assert.InRange(cost2 / cost1, 1.8, 2.2);
    }

    // ── LCOE ─────────────────────────────────────────────────────────────

    [Fact]
    public void Lcoe_PvAtDefaultDiscount_LandsInCommercialBand()
    {
        // 1 MW PV farm: capex ~ $1M, opex ~ $10k/yr, annual energy
        // 1 MW · 8760 · CF=0.20 = 1.752 GWh.
        double lcoe = LcoeCalculator.Compute(
            capex_USD:                   1_000_000.0,
            annualOpex_USD:              10_000.0,
            annualEnergyProduction_kWh:  1_752_000.0,
            discountRate:                0.07,
            lifetimeYears:               25);
        // IEA 2024 utility PV LCOE: $0.04-0.08/kWh band.
        Assert.InRange(lcoe, 0.03, 0.10);
    }

    [Fact]
    public void Lcoe_ZeroDiscountRate_LimitMatchesSimpleAmortisation()
    {
        // At r=0, LCOE = (capex/n + opex) / energy.
        double lcoe = LcoeCalculator.Compute(
            capex_USD:                  100_000.0,
            annualOpex_USD:             0.0,
            annualEnergyProduction_kWh: 10_000.0,
            discountRate:               0.0,
            lifetimeYears:              10);
        // 100k/10/10k = 1.0.
        Assert.Equal(1.0, lcoe, precision: 6);
    }

    [Fact]
    public void Lcoe_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LcoeCalculator.Compute(-1.0, 0, 1000, 0.07, 20));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LcoeCalculator.Compute(1000, 0, 0, 0.07, 20));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LcoeCalculator.Compute(1000, 0, 1000, 1.0, 20));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LcoeCalculator.Compute(1000, 0, 1000, 0.07, 0));
    }

    [Fact]
    public void Lcoe_OverloadAcceptsBreakdownDirectly()
    {
        var breakdown = new SystemCostBreakdown(
            Components:                Array.Empty<CostEstimate>(),
            TotalMass_kg:              0,
            TotalCapitalCost_USD:      1_000_000,
            TotalEmbodiedCO2_kgCO2eq:  0);
        double lcoe = LcoeCalculator.Compute(
            breakdown:                  breakdown,
            annualOpex_USD:             10_000,
            annualEnergyProduction_kWh: 1_500_000);
        Assert.True(lcoe > 0);
    }

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
}
