// TurbofanBuildOptionsTests — defaults + immutable-record semantics for
// the turbofan voxel builder's options record. Audit 05-test-gaps.md
// Section 2 Medium.

using Voxelforge.Airbreathing.Geometry;
using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.Tests;

public class TurbofanBuildOptionsTests
{
    [Fact]
    public void Defaults_AreSensibleForTurbofan()
    {
        var opts = new TurbofanBuildOptions();
        Assert.Equal(2.0, opts.WallThickness_mm,           precision: 6);
        Assert.Equal(2.0, opts.BypassDuctWallThickness_mm, precision: 6);
        Assert.Equal(0.0, opts.VoxelSize_mm,               precision: 6);  // 0 = auto-resolve
        Assert.Equal(0.15, opts.SmoothenRadius_mm,         precision: 6);
        Assert.Null(opts.LpbfMaterial);
        Assert.Equal(64, opts.LpbfAzimuthalSamples);
        Assert.True(opts.RunLpbfAnalysis);
    }

    [Fact]
    public void RecordEquality_IsValueBased()
    {
        var a = new TurbofanBuildOptions(BypassDuctWallThickness_mm: 1.5);
        var b = new TurbofanBuildOptions(BypassDuctWallThickness_mm: 1.5);
        Assert.Equal(a, b);

        var c = new TurbofanBuildOptions(BypassDuctWallThickness_mm: 2.5);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void WithExpression_UpdatesIndividualFields()
    {
        var baseOpts = new TurbofanBuildOptions();
        var withMat = baseOpts with { LpbfMaterial = LpbfMaterialProfiles.Inconel625 };

        Assert.Null(baseOpts.LpbfMaterial);
        Assert.Same(LpbfMaterialProfiles.Inconel625, withMat.LpbfMaterial);
        Assert.Equal(baseOpts.WallThickness_mm,           withMat.WallThickness_mm);
        Assert.Equal(baseOpts.BypassDuctWallThickness_mm, withMat.BypassDuctWallThickness_mm);
    }

    [Fact]
    public void BypassWall_CanRunThinnerThanCoreWall()
    {
        // Cold-stream bypass duct sees lower pressures than the hot-stream
        // core shell, so a typical low-bypass turbofan can run a thinner
        // bypass shell than core.
        var opts = new TurbofanBuildOptions(
            WallThickness_mm:           3.0,
            BypassDuctWallThickness_mm: 1.5);
        Assert.True(opts.BypassDuctWallThickness_mm < opts.WallThickness_mm);
    }
}
