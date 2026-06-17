// DesignVariableInfoBoundsHelperTests.cs — tests for the WithBounds /
// WithMin / WithMax helpers on DesignVariableInfo.

using System;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Optimization;

public sealed class DesignVariableInfoBoundsHelperTests
{
    private static readonly DesignVariableInfo Base = new("x", 0.0, 100.0);

    [Fact]
    public void WithBounds_ReplacesBothEnds()
    {
        var clamped = Base.WithBounds(10.0, 50.0);
        Assert.Equal("x",  clamped.Name);
        Assert.Equal(10.0, clamped.Min);
        Assert.Equal(50.0, clamped.Max);
    }

    [Fact]
    public void WithBounds_PreservesName()
    {
        var v = new DesignVariableInfo("MpdArcCurrent_A", 500.0, 8000.0);
        var clamped = v.WithBounds(500.0, 5000.0);
        Assert.Equal("MpdArcCurrent_A", clamped.Name);
    }

    [Fact]
    public void WithBounds_RejectsInvertedBounds()
    {
        Assert.Throws<ArgumentException>(() => Base.WithBounds(50.0, 10.0));
        Assert.Throws<ArgumentException>(() => Base.WithBounds(50.0, 50.0));
    }

    [Fact]
    public void WithMin_ChangesOnlyMin()
    {
        var clamped = Base.WithMin(10.0);
        Assert.Equal(10.0,  clamped.Min);
        Assert.Equal(100.0, clamped.Max);
    }

    [Fact]
    public void WithMax_ChangesOnlyMax()
    {
        var clamped = Base.WithMax(50.0);
        Assert.Equal(0.0,  clamped.Min);
        Assert.Equal(50.0, clamped.Max);
    }

    [Fact]
    public void WithMin_RejectsAboveExistingMax()
    {
        Assert.Throws<ArgumentException>(() => Base.WithMin(200.0));
        Assert.Throws<ArgumentException>(() => Base.WithMin(100.0));   // equal-to-Max is invalid
    }

    [Fact]
    public void WithMax_RejectsBelowExistingMin()
    {
        Assert.Throws<ArgumentException>(() => Base.WithMax(-10.0));
        Assert.Throws<ArgumentException>(() => Base.WithMax(0.0));     // equal-to-Min is invalid
    }

    [Fact]
    public void WithBounds_ChainableForBindTimeClipping()
    {
        // Realistic use case: bind-time clip narrows arc-current to bus-power-limit.
        var defaults = new DesignVariableInfo("MpdArcCurrent_A", 500.0, 8000.0);
        const double busPowerLimit = 150_000.0;
        const double maxArcVoltage = 50.0;
        double maxJ_busLimited = busPowerLimit / maxArcVoltage;          // 3000 A
        var clipped = defaults.WithMax(Math.Min(defaults.Max, maxJ_busLimited));
        Assert.Equal( 500.0, clipped.Min);
        Assert.Equal(3000.0, clipped.Max);
        Assert.Equal(defaults.Name, clipped.Name);
    }

    [Fact]
    public void WithBounds_IsImmutableSemantics()
    {
        // DesignVariableInfo is a readonly record struct. WithBounds must
        // return a new value; the original is untouched.
        var original = new DesignVariableInfo("x", 0.0, 100.0);
        _ = original.WithBounds(10.0, 50.0);
        Assert.Equal(0.0,   original.Min);
        Assert.Equal(100.0, original.Max);
    }
}
