// EcW6AerostructuresAndHybridRocketTests.cs — Sprint EC.W6 unit tests
// for the Aerostructures wing-spar + Hybrid Rocket cost factories.

using Voxelforge.Aerostructures;
using Voxelforge.Economics;
using Voxelforge.Hybrid;
using Xunit;

namespace Voxelforge.Tests.Economics;

public sealed class EcW6AerostructuresAndHybridRocketTests
{
    // ── WingSpar ─────────────────────────────────────────────────────────

    [Fact]
    public void ForWingSpar_Al7075_HasPositiveCost()
    {
        var est = ComponentCostEstimators.ForWingSpar("spar",
            DefaultCessnaSpar());
        Assert.True(est.Mass_kg > 0);
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.EmbodiedCO2_kgCO2eq > 0);
    }

    [Fact]
    public void ForWingSpar_Cfrp_CostsMoreThanAl_PerKg()
    {
        // Same geometry; only material differs.
        var al = ComponentCostEstimators.ForWingSpar("al",
            DefaultCessnaSpar() with { Material = SparMaterial.Aluminum7075 });
        var cf = ComponentCostEstimators.ForWingSpar("cf",
            DefaultCessnaSpar() with { Material = SparMaterial.CarbonFibreComposite });
        // CFRP: $80/kg vs Al: $30/kg, but CFRP density is ~ 1600 vs Al
        // 2810 → CFRP spar is lighter, so capex per spar may still
        // depend on relative mass. Just sanity-check both positive +
        // CFRP capex per kg is higher.
        double alPerKg = al.CapitalCost_USD / al.Mass_kg;
        double cfPerKg = cf.CapitalCost_USD / cf.Mass_kg;
        Assert.True(cfPerKg > alPerKg);
    }

    [Fact]
    public void ForWingSpar_Steel_CheapestPerKg()
    {
        var st = ComponentCostEstimators.ForWingSpar("st",
            DefaultCessnaSpar() with { Material = SparMaterial.Steel4340 });
        var al = ComponentCostEstimators.ForWingSpar("al",
            DefaultCessnaSpar() with { Material = SparMaterial.Aluminum7075 });
        double stPerKg = st.CapitalCost_USD / st.Mass_kg;
        double alPerKg = al.CapitalCost_USD / al.Mass_kg;
        // Steel $8/kg vs Al $30/kg.
        Assert.True(stPerKg < alPerKg);
    }

    // ── HybridRocket ─────────────────────────────────────────────────────

    [Fact]
    public void ForHybridRocket_DefaultDesign_HasPositiveCost()
    {
        var est = ComponentCostEstimators.ForHybridRocket("hr",
            DefaultHybridRocket());
        Assert.True(est.Mass_kg > 0);
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.EmbodiedCO2_kgCO2eq > 0);
    }

    [Fact]
    public void ForHybridRocket_DoubledOxidiserFlow_HigherThrustHigherCost()
    {
        var base_ = ComponentCostEstimators.ForHybridRocket("hr",
            DefaultHybridRocket());
        var hot = ComponentCostEstimators.ForHybridRocket("hr",
            DefaultHybridRocket() with { OxidiserMassFlow_kgs = 4.0 });
        Assert.True(hot.CapitalCost_USD > base_.CapitalCost_USD);
    }

    // ── Cross-pillar rollup ──────────────────────────────────────────────

    [Fact]
    public void EconomicAnalyzer_RollsUpAerostructuresAndHybridRocket()
    {
        var roll = EconomicAnalyzer.Analyze(new[]
        {
            ComponentCostEstimators.ForWingSpar    ("spar", DefaultCessnaSpar()),
            ComponentCostEstimators.ForHybridRocket("hr",   DefaultHybridRocket()),
        });
        Assert.Equal(2, roll.Components.Count);
        Assert.True(roll.TotalCapitalCost_USD > 0);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────

    private static WingSparDesign DefaultCessnaSpar() => new(
        SectionType:        SparSectionType.HollowRectangularBox,
        Material:           SparMaterial.Aluminum7075,
        HalfSpan_m:         5.5,
        OuterHeight_m:      0.20,
        OuterWidth_m:       0.080,
        WallThickness_m:    0.008,
        DistributedLift_Nm: 981.0,
        LoadFactor:         3.8);

    private static HybridRocketDesign DefaultHybridRocket() => new(
        Fuel:                HybridFuel.HTPB,
        GrainLength_m:        1.0,
        InitialPortRadius_m:  0.05,
        OuterGrainRadius_m:   0.20,
        OxidiserMassFlow_kgs: 2.0,
        ChamberPressure_bar:  30.0,
        ExpansionRatio:       12.0);
}
