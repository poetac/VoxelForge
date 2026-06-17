// IsolatorRecoveryTests.cs — Sprint A10 unit tests for the pseudo-
// shock-train pressure recovery model (Mattingly §17.4).

using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class IsolatorRecoveryTests
{
    [Fact]
    public void Pi_iso_AtMach1_IsOne()
    {
        // No excess Mach → no pseudo-shock loss.
        double pi = IsolatorRecovery.Pi_iso(1.0);
        Assert.Equal(1.0, pi, precision: 9);
    }

    [Fact]
    public void Pi_iso_AtMach2_IsExpected()
    {
        // 1 − 0.015 × (4 − 1) = 1 − 0.045 = 0.955
        double expected = 0.955;
        double actual   = IsolatorRecovery.Pi_iso(2.0);
        Assert.Equal(expected, actual, precision: 6);
    }

    [Fact]
    public void Pi_iso_AtMach3_IsExpected()
    {
        // 1 − 0.015 × (9 − 1) = 1 − 0.120 = 0.880
        double expected = 0.880;
        double actual   = IsolatorRecovery.Pi_iso(3.0);
        Assert.Equal(expected, actual, precision: 6);
    }

    [Fact]
    public void Pi_iso_HighMach_ClampsToFloor()
    {
        // At large M the formula goes negative; clamp must kick in.
        // M = 10 → 1 − 0.015×99 = 1 − 1.485 → clamped to floor.
        double pi = IsolatorRecovery.Pi_iso(10.0);
        Assert.Equal(IsolatorRecovery.IsolatorRecoveryFloor, pi);
    }

    [Fact]
    public void Pi_iso_AtMach5_AtOrAboveFloor()
    {
        double pi = IsolatorRecovery.Pi_iso(5.0);
        Assert.True(pi >= IsolatorRecovery.IsolatorRecoveryFloor,
            $"Expected π_iso ≥ floor {IsolatorRecovery.IsolatorRecoveryFloor} at M=5, got {pi}");
    }

    [Fact]
    public void Pi_iso_SubsonicMach_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => IsolatorRecovery.Pi_iso(0.8));
    }

    [Fact]
    public void Pi_iso_Deterministic()
    {
        double a = IsolatorRecovery.Pi_iso(2.5);
        double b = IsolatorRecovery.Pi_iso(2.5);
        Assert.Equal(a, b);
    }
}
