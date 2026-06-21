// NuclearValidateSelfRegressionTests.cs — regression guard for the dead
// ValidateSelf bug (red-team round-3 finding).
//
// NuclearThermalDesign.ValidateSelf() was defined but never called anywhere —
// NuclearOptimization.GenerateWith / NuclearEngine.Evaluate / the objective /
// JSON load all skipped it (the Marine and EP pillars call it in their objective
// unpack; Nuclear was the outlier). So a degenerate-but-constructible design
// (e.g. mDot = 0 from a CLI / deserialized caller) reached the cycle solver,
// divided by zero, and propagated NaN through core-exit-T — which then slipped
// past every hard gate (NaN > limit is false), so the result reported
// IsFeasible = true with NaN performance. GenerateWith now calls ValidateSelf.

using Voxelforge.Nuclear;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class NuclearValidateSelfRegressionTests
{
    private static NuclearThermalDesign ValidDesign() => new(
        Kind:                    NuclearKind.NervaSolidCore,
        ReactorThermalPower_MW:  1100.0,
        ReactorCoreLength_mm:    1400.0,
        ReactorCoreDiameter_mm:  1400.0,
        FuelLoadingFraction:     0.65,
        PropellantMassFlow_kgs:  10.0,
        ChamberPressure_bar:     34.0,
        ThroatRadius_mm:         120.0,
        ExpansionRatio:          100.0,
        NozzleLength_mm:         4000.0,
        RegenChannelDepth_mm:    2.0,
        RegenChannelCount:       200,
        NozzleWallThickness_mm:  1.5,
        NozzleChannelWidth_mm:   3.0,
        NozzleManifoldDepth_mm:  5.0);

    private static NuclearThermalConditions Cond()
        => new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    [Fact]
    public void GenerateWith_ZeroMassFlow_ThrowsRatherThanNaNFeasible()
    {
        var design = ValidDesign() with { PropellantMassFlow_kgs = 0.0 };
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => NuclearOptimization.GenerateWith(design, Cond()));
    }

    [Fact]
    public void GenerateWith_NaNChamberPressure_Throws()
    {
        var design = ValidDesign() with { ChamberPressure_bar = double.NaN };
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => NuclearOptimization.GenerateWith(design, Cond()));
    }

    [Fact]
    public void GenerateWith_ValidDesign_ProducesFiniteFeasibleResult()
    {
        // The guard must not over-reject a valid NERVA-class design.
        var r = NuclearOptimization.GenerateWith(ValidDesign(), Cond());
        Assert.True(double.IsFinite(r.IspVacuum_s) && r.IspVacuum_s > 0.0,
            $"Isp must be finite & positive; got {r.IspVacuum_s}");
        Assert.True(double.IsFinite(r.CoreExitTemp_K),
            $"core-exit T must be finite; got {r.CoreExitTemp_K}");
    }
}
