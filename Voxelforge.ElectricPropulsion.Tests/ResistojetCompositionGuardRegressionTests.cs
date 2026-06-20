// ResistojetCompositionGuardRegressionTests.cs — regression guard for the
// resistojet degenerate-composition NaN bug (red-team finding). The mixture
// property helpers divide by MixtureMW = Σ xᵢ·MWᵢ, which is 0 when every inlet
// mole fraction is 0, producing NaN γ/cp that propagate to NaN thrust/Isp and
// NaN-vs-limit gate comparisons that silently never fire. RunResistojetPipeline
// now calls the (previously dead) PropellantInletComposition.ValidateOrThrow at
// entry. This test fails on the old code (no throw, NaN result) and passes now.

using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests;

public sealed class ResistojetCompositionGuardRegressionTests
{
    private static ElectricPropulsionEngineDesign Design() => new(
        Kind:                    ElectricPropulsionEngineKind.Resistojet,
        HeaterPower_W:           870.0,
        PropellantMassFlow_kgs:  1.2e-4,
        NozzleThroatRadius_mm:   0.20,
        NozzleAreaRatio:         100.0,
        HeaterChamberLength_mm:  25.0,
        HeaterChamberRadius_mm:  6.0);

    private static ResistojetConditions Cond(PropellantInletComposition comp) => new(
        BusVoltage_V:        28.0,
        BusPower_W_avail:    900.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K:  900.0,
        InletComposition:    comp);

    [Fact]
    public void DegenerateComposition_ThrowsClearly_RatherThanNaN()
    {
        // All mole fractions zero → MixtureMW = 0 → 0/0 in the mixture helpers.
        var degenerate = new PropellantInletComposition(0.0, 0.0, 0.0, 0.0);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ElectricPropulsionOptimization.GenerateWith(Design(), Cond(degenerate)));
    }

    [Fact]
    public void NonNormalisedComposition_ThrowsClearly()
    {
        // Fractions that don't sum to 1.0 are physically invalid.
        var bad = new PropellantInletComposition(0.5, 0.0, 0.0, 0.0);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ElectricPropulsionOptimization.GenerateWith(Design(), Cond(bad)));
    }

    [Fact]
    public void ValidComposition_StillProducesFiniteThrust()
    {
        var r = ElectricPropulsionOptimization.GenerateWith(
            Design(), Cond(PropellantInletComposition.Hydrazine_Shell405));
        Assert.True(double.IsFinite(r.Thrust_N) && r.Thrust_N > 0.0, $"thrust = {r.Thrust_N}");
    }
}
