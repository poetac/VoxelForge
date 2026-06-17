// RfgWave2Tests.cs — Sprint RFG.W2 unit tests for the subcooling +
// superheat correction.

using System;
using Voxelforge.Refrigeration;
using Xunit;

namespace Voxelforge.Tests.Refrigeration;

public sealed class RfgWave2Tests
{
    [Fact]
    public void DefaultSubcooling_IsZero()
        => Assert.Equal(0.0, ResidentialAc().SubcoolingDepth_K, precision: 9);

    [Fact]
    public void DefaultSuperheat_IsZero()
        => Assert.Equal(0.0, ResidentialAc().SuperheatDepth_K, precision: 9);

    [Fact]
    public void BaselineBitIdentical_AtZeroSubcoolingAndSuperheat()
    {
        // RFG.W1 baseline COP_cooling ~ 6.57 must still hold at defaults.
        var r = RefrigerationSolver.Solve(ResidentialAc());
        Assert.InRange(r.CoolingCop, 4.0, 8.0);
    }

    [Fact]
    public void Subcooling_BoostsCoolingCop()
    {
        var nominal = RefrigerationSolver.Solve(ResidentialAc());
        var subcooled = RefrigerationSolver.Solve(ResidentialAc()
            with { SubcoolingDepth_K = 10.0 });
        // 10 K subcool → 6 % boost.
        Assert.Equal(1.06, subcooled.CoolingCop / nominal.CoolingCop, precision: 6);
    }

    [Fact]
    public void Superheat_ReducesCoolingCop()
    {
        var nominal = RefrigerationSolver.Solve(ResidentialAc());
        var hot = RefrigerationSolver.Solve(ResidentialAc()
            with { SuperheatDepth_K = 10.0 });
        // 10 K superheat → 2 % drop.
        Assert.Equal(0.98, hot.CoolingCop / nominal.CoolingCop, precision: 6);
    }

    [Fact]
    public void Validate_RejectsNegativeSubcooling()
    {
        Assert.Throws<ArgumentException>(
            () => (ResidentialAc() with { SubcoolingDepth_K = -1.0 }).ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNegativeSuperheat()
    {
        Assert.Throws<ArgumentException>(
            () => (ResidentialAc() with { SuperheatDepth_K = -1.0 }).ValidateSelf());
    }

    private static RefrigerationDesign ResidentialAc() => new(
        Mode:                        RefrigerationMode.Cooling,
        Refrigerant:                 Refrigerant.R410A,
        ColdReservoirTemperature_K:  283.15,
        HotReservoirTemperature_K:   308.15,
        CompressorPowerInput_W:     3500.0);
}
