// MarineCostEstimatorsTests.cs — Sprint EC.W10 unit tests for the
// AUV cost estimator.

using Voxelforge.Economics;
using Voxelforge.Marine.Economics;
using Xunit;

namespace Voxelforge.Marine.Tests;

public sealed class MarineCostEstimatorsTests
{
    [Fact]
    public void Remus100_Ti_AuvBaseline_HasPositiveCost()
    {
        var result = MarineOptimization.GenerateWith(DefaultRemus100(),
            DefaultConditions());
        var est = MarineCostEstimators.ForAuvDisplacement("auv", result);
        Assert.True(est.Mass_kg > 0);
        Assert.True(est.CapitalCost_USD > 50_000.0);  // integration overhead floor
        Assert.True(est.EmbodiedCO2_kgCO2eq > 0);
    }

    [Fact]
    public void TitaniumCostsMore_ThanAluminum_AtSameGeometry()
    {
        var ti = MarineCostEstimators.ForAuvDisplacement("ti",
            MarineOptimization.GenerateWith(
                DefaultRemus100() with { MaterialIndex = 0 },
                DefaultConditions()));
        var al = MarineCostEstimators.ForAuvDisplacement("al",
            MarineOptimization.GenerateWith(
                DefaultRemus100() with { MaterialIndex = 1 },
                DefaultConditions()));
        // Ti-6Al-4V $200/kg vs Al-6061 $40/kg → capex significantly
        // higher even after integration overhead.
        Assert.True(ti.CapitalCost_USD > al.CapitalCost_USD);
    }

    [Fact]
    public void Capex_IncludesIntegrationFloor()
    {
        var result = MarineOptimization.GenerateWith(DefaultRemus100(),
            DefaultConditions());
        var est = MarineCostEstimators.ForAuvDisplacement("auv", result);
        // Integration floor is $50k. Even a hypothetical zero-mass
        // hull would carry it. Just check it's above the floor.
        Assert.True(est.CapitalCost_USD >= 50_000.0);
    }

    private static MarineDesign DefaultRemus100() => new(
        Kind:                MarineKind.AuvMidBody,
        Length_m:            1.6,
        Diameter_m:          0.19,
        NoseFairingFraction: 0.15,
        TailFairingFraction: 0.20,
        WallThickness_m:     0.004,
        MaterialIndex:       0,
        DepthRating_m:       100.0);

    private static MarineConditions DefaultConditions() => new(
        CruiseSpeed_ms: 1.5,
        MaxDepth_m:     100.0);
}
