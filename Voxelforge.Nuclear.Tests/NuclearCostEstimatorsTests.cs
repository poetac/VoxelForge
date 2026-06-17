// NuclearCostEstimatorsTests.cs — Sprint EC.W10 unit tests for the
// NERVA-class NTR cost estimator.

using Voxelforge.Economics;
using Voxelforge.Nuclear.Economics;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class NuclearCostEstimatorsTests
{
    [Fact]
    public void NrxA6Baseline_HasFiveMillionDollarHardware_Floor()
    {
        var result = NuclearOptimization.GenerateWith(BaselineNrxA6(), Cond());
        var est = NuclearCostEstimators.ForNtrEngine("ntr", result);
        // $5M hardware floor + fuel capex.
        Assert.True(est.CapitalCost_USD >= 5_000_000.0);
        Assert.True(est.Mass_kg > 8000.0);   // 8 t hardware floor + fuel mass
    }

    [Fact]
    public void HeuIsMuchMoreExpensiveThanLeu_PerSameDesign()
    {
        var leu = NuclearCostEstimators.ForNtrEngine("leu",
            NuclearOptimization.GenerateWith(
                BaselineNrxA6() with { EnrichmentTier = UraniumEnrichment.LEU },
                Cond()));
        var heu = NuclearCostEstimators.ForNtrEngine("heu",
            NuclearOptimization.GenerateWith(
                BaselineNrxA6() with { EnrichmentTier = UraniumEnrichment.HEU },
                Cond()));
        // HEU at $250k/kg-fuel vs LEU at $1.8k/kg-fuel → ~ 140x ratio
        // on the fuel component (hardware floor is identical).
        Assert.True(heu.CapitalCost_USD > leu.CapitalCost_USD);
        // Difference should be on the order of millions.
        Assert.True(heu.CapitalCost_USD - leu.CapitalCost_USD > 1_000_000);
    }

    [Fact]
    public void HaleuLandsBetween_LeuAndHeu()
    {
        var leu = NuclearCostEstimators.ForNtrEngine("leu",
            NuclearOptimization.GenerateWith(
                BaselineNrxA6() with { EnrichmentTier = UraniumEnrichment.LEU },
                Cond()));
        var haleu = NuclearCostEstimators.ForNtrEngine("haleu",
            NuclearOptimization.GenerateWith(
                BaselineNrxA6() with { EnrichmentTier = UraniumEnrichment.HALEU },
                Cond()));
        var heu = NuclearCostEstimators.ForNtrEngine("heu",
            NuclearOptimization.GenerateWith(
                BaselineNrxA6() with { EnrichmentTier = UraniumEnrichment.HEU },
                Cond()));
        Assert.True(leu.CapitalCost_USD < haleu.CapitalCost_USD);
        Assert.True(haleu.CapitalCost_USD < heu.CapitalCost_USD);
    }

    [Fact]
    public void Co2_IncludesHardwareFloor()
    {
        var result = NuclearOptimization.GenerateWith(BaselineNrxA6(), Cond());
        var est = NuclearCostEstimators.ForNtrEngine("ntr", result);
        Assert.True(est.EmbodiedCO2_kgCO2eq >= 35_000.0);
    }

    private static NuclearThermalDesign BaselineNrxA6() => new NuclearThermalDesign(
        Kind:                    NuclearKind.NervaSolidCore,
        ReactorThermalPower_MW:   1100.0,
        ReactorCoreLength_mm:     1400.0,
        ReactorCoreDiameter_mm:   1400.0,
        FuelLoadingFraction:      0.65,
        PropellantMassFlow_kgs:   33.0,
        ChamberPressure_bar:      40.0,
        ThroatRadius_mm:          120.0,
        ExpansionRatio:           100.0,
        NozzleLength_mm:          4000.0,
        RegenChannelDepth_mm:     2.0,
        RegenChannelCount:        200,
        NozzleWallThickness_mm:   1.5,
        NozzleChannelWidth_mm:    3.0,
        NozzleManifoldDepth_mm:   5.0);

    private static NuclearThermalConditions Cond() =>
        new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);
}
