// InletRecoveryTests.cs — Sprint A4 acceptance for the inlet-recovery
// model.

using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class InletRecoveryTests
{
    [Fact]
    public void SubsonicMach_ReturnsSubsonicConstant()
    {
        Assert.Equal(InletRecovery.SubsonicRecoveryFactor, InletRecovery.Pi_d(0.0), 6);
        Assert.Equal(InletRecovery.SubsonicRecoveryFactor, InletRecovery.Pi_d(0.5), 6);
        Assert.Equal(InletRecovery.SubsonicRecoveryFactor, InletRecovery.Pi_d(0.99), 6);
    }

    [Fact]
    public void Mach1_DropsFromSubsonicConstantToMechanicalEfficiency()
    {
        // At M = 1: piDMax = 1 − 0 = 1, π_d = mech_eff × 1 = 0.95.
        Assert.Equal(InletRecovery.SupersonicMechanicalEfficiency, InletRecovery.Pi_d(1.0), 6);
    }

    [Fact]
    public void Mach2_MatchesMilStdReferenceValue()
    {
        // π_d(M=2) = 0.95 × (1 − 0.075 × 1^1.35) = 0.95 × 0.925 = 0.87875
        Assert.Equal(0.87875, InletRecovery.Pi_d(2.0), 5);
    }

    [Fact]
    public void Mach3_RecoveryIsLowerThanMach2()
    {
        // The MIL-STD curve is monotone-decreasing in supersonic regime.
        Assert.True(InletRecovery.Pi_d(3.0) < InletRecovery.Pi_d(2.0));
        Assert.True(InletRecovery.Pi_d(4.0) < InletRecovery.Pi_d(3.0));
    }

    [Fact]
    public void MachAboveCurveDomain_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => InletRecovery.Pi_d(5.5));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => InletRecovery.Pi_d(-0.1));
    }
}
