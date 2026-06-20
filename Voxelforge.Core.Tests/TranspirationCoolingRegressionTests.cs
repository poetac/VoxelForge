// TranspirationCoolingRegressionTests.cs — regression guard for the inverted
// transpiration-effectiveness bug (red-team finding #1). Eckert-Livingood
// F(B) = B/(e^B−1) is the Stanton-number REDUCTION ratio (→1 at no bleed,
// →0 at heavy bleed); it must be applied as the temperature effectiveness's
// COMPLEMENT, 1−F(B). Before the fix, more coolant produced LESS cooling and
// a near-zero-bleed wall was reported fully cooled (a non-conservative
// burn-through risk that passed the WALL_TEMP gate). These tests fail on the
// inverted formula and pass on the corrected one.

using Voxelforge.HeatTransfer;

namespace Voxelforge.Core.Tests;

public sealed class TranspirationCoolingRegressionTests
{
    private static double Wall(double bleedMassFlux_kgm2s) =>
        TranspirationCooling.ComputeEffectiveAdiabaticWallTemp(
            T_aw_K: 3000.0,
            T_coolantInlet_K: 150.0,
            h_gas_Wm2K: 20_000.0,
            bleedMassFluxPerArea_kgm2s: bleedMassFlux_kgm2s,
            cpGas_JkgK: 2000.0,
            efficiency: 0.85);

    [Fact]
    public void MoreBleed_ProducesMoreCooling_LowerWallTemp()
    {
        double light = Wall(1e-3);   // B ≈ 1e-4
        double medium = Wall(10.0);  // B ≈ 1
        double heavy = Wall(100.0);  // B ≈ 10
        // Wall temperature must fall monotonically as bleed rises.
        Assert.True(light > medium && medium > heavy,
            $"cooling must rise with bleed: wall(light)={light:F0} wall(medium)={medium:F0} wall(heavy)={heavy:F0}");
    }

    [Fact]
    public void NegligibleBleed_GivesNegligibleCooling_NotFullCooling()
    {
        // With essentially no transpiration flow the wall must stay near the
        // baseline T_aw (3000 K), NOT collapse toward the coolant temperature.
        double wall = Wall(1e-3);
        Assert.True(wall > 2950.0,
            $"near-zero bleed must give near-zero cooling, but wall={wall:F0} K (baseline T_aw=3000 K)");
    }

    [Fact]
    public void HeavyBleed_ApproachesEfficiencyCappedCoolant()
    {
        // At large B the effectiveness saturates; cooling approaches
        // efficiency·(T_aw−T_c) = 0.85·2850 ≈ 2422 K, i.e. wall ≈ 578 K.
        double wall = Wall(100.0);
        Assert.InRange(wall, 450.0, 750.0);
    }
}
