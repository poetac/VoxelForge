// PropellantValidationTests.cs — Locks the hard-fail behaviour for
// unimplemented propellant pairs and unknown coolant fluid keys.
// Legacy behaviour silently fell back to LOX/CH4 + methane coolant.

using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class PropellantValidationTests
{
    // ── PropellantValidation.EnsureSupported ────────────────────────

    [Theory]
    [InlineData(PropellantPair.LOX_CH4)]
    [InlineData(PropellantPair.LOX_H2)]
    [InlineData(PropellantPair.LOX_RP1)]
    public void EnsureSupported_PassesForImplementedPairs(PropellantPair pair)
    {
        // Must not throw.
        PropellantValidation.EnsureSupported(pair);
        Assert.True(PropellantValidation.IsSupported(pair));
        Assert.Null(PropellantValidation.Explain(pair));
    }

    [Theory]
    [InlineData(PropellantPair.N2O4_MMH)]
    [InlineData(PropellantPair.H2O2_RP1)]
    public void EnsureSupported_ThrowsForStubPairs(PropellantPair pair)
    {
        var ex = Assert.Throws<UnsupportedPropellantException>(
            () => PropellantValidation.EnsureSupported(pair));
        Assert.Equal(PropellantValidationCode.PairNotImplemented, ex.Code);
        Assert.Equal(pair, ex.Pair);
        Assert.False(PropellantValidation.IsSupported(pair));
        Assert.NotNull(PropellantValidation.Explain(pair));
    }

    // ── CoolantRegistry.Get ─────────────────────────────────────────

    [Theory]
    [InlineData("CH4")]
    [InlineData("H2")]
    [InlineData("RP-1")]
    public void CoolantRegistry_ReturnsFluidForKnownKeys(string key)
    {
        Assert.True(CoolantRegistry.IsKnown(key));
        var fluid = CoolantRegistry.Get(key);
        Assert.NotNull(fluid);
    }

    [Theory]
    [InlineData("MMH")]
    [InlineData("Methanol")]
    [InlineData("")]
    [InlineData("methane")] // case-sensitive by design
    public void CoolantRegistry_ThrowsForUnknownKeys(string key)
    {
        Assert.False(CoolantRegistry.IsKnown(key));
        Assert.Throws<InvalidOperationException>(() => CoolantRegistry.Get(key));
    }

    // ── GenerateWith integration ────────────────────────────────────

    [Fact]
    public void GenerateWith_ThrowsOnStubPair_WithStructuredCode()
    {
        var cond = new OperatingConditions
        {
            Thrust_N = 500,
            ChamberPressure_Pa = 6.9e6,
            MixtureRatio = 1.85,
            PropellantPair = PropellantPair.N2O4_MMH,
        };
        var design = new RegenChamberDesign();

        var ex = Assert.Throws<UnsupportedPropellantException>(
            () => RegenChamberOptimization.GenerateWith(cond, design));
        Assert.Equal(PropellantValidationCode.PairNotImplemented, ex.Code);
        Assert.Equal(PropellantPair.N2O4_MMH, ex.Pair);
    }

    // Happy-path GenerateWith is covered by BaselineDesignRegressionTests; those
    // tests pre-initialise the propellant fields without building voxel geometry,
    // so they exercise the post-validation code path directly. We intentionally
    // do not re-run a voxel build here.
}
