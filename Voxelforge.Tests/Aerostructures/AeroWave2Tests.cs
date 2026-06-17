// AeroWave2Tests.cs — Sprint AS.W2 unit tests for the elliptical-lift
// distribution extension.

using Voxelforge.Aerostructures;
using Xunit;

namespace Voxelforge.Tests.Aerostructures;

public sealed class AeroWave2Tests
{
    [Fact]
    public void DefaultUseEllipticalLift_IsFalse()
    {
        Assert.False(Cessna172Spar().UseEllipticalLift);
    }

    [Fact]
    public void UDL_BaselineBitIdentical_AtDefault()
    {
        // AS.W1 baseline cluster band [200, 350] MPa must still hold.
        var r = WingSparSolver.Solve(Cessna172Spar());
        Assert.InRange(r.MaximumBendingStress_Pa, 200e6, 350e6);
    }

    [Fact]
    public void EllipticalLift_ReducesRootBendingMoment_VsUDL()
    {
        // Elliptical lift distributes inboard → reduces M_max by ~ 25 %.
        var udl       = WingSparSolver.Solve(Cessna172Spar());
        var elliptical = WingSparSolver.Solve(Cessna172Spar()
            with { UseEllipticalLift = true });
        Assert.Equal(0.75, elliptical.MaximumBendingMoment_Nm / udl.MaximumBendingMoment_Nm,
            precision: 6);
    }

    [Fact]
    public void EllipticalLift_ReducesTipDeflection_VsUDL()
    {
        var udl       = WingSparSolver.Solve(Cessna172Spar());
        var elliptical = WingSparSolver.Solve(Cessna172Spar()
            with { UseEllipticalLift = true });
        Assert.Equal(0.65, elliptical.TipDeflection_m / udl.TipDeflection_m,
            precision: 6);
    }

    [Fact]
    public void EllipticalLift_HigherSafetyFactor_VsUDL()
    {
        var udl       = WingSparSolver.Solve(Cessna172Spar());
        var elliptical = WingSparSolver.Solve(Cessna172Spar()
            with { UseEllipticalLift = true });
        Assert.True(elliptical.SafetyFactor > udl.SafetyFactor);
    }

    private static WingSparDesign Cessna172Spar() => new(
        SectionType:          SparSectionType.HollowRectangularBox,
        Material:             SparMaterial.Aluminum7075,
        HalfSpan_m:            5.5,
        OuterHeight_m:         0.20,
        OuterWidth_m:          0.080,
        WallThickness_m:       0.008,
        DistributedLift_Nm:    981.0,
        LoadFactor:            3.8);
}
