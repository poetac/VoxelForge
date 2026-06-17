// RamjetBuildOptionsTests — defaults + immutable-record semantics for
// the ramjet voxel builder's options record.

using Voxelforge.Airbreathing.Geometry;
using Voxelforge.Geometry.LpbfAnalysis;
using Xunit;

namespace Voxelforge.Airbreathing.Tests;

public class RamjetBuildOptionsTests
{
    [Fact]
    public void Defaults_AreSensibleForSmallRamjet()
    {
        var opts = new RamjetBuildOptions();

        Assert.Equal(2.0, opts.WallThickness_mm);
        Assert.Equal(0.0, opts.VoxelSize_mm);     // 0 = auto-resolve
        Assert.Equal(0.15, opts.SmoothenRadius_mm);
        Assert.Null(opts.LpbfMaterial);
        Assert.Equal(64, opts.LpbfAzimuthalSamples);
        Assert.True(opts.RunLpbfAnalysis);
    }

    [Fact]
    public void RecordEquality_IsValueBased()
    {
        var a = new RamjetBuildOptions(WallThickness_mm: 1.5);
        var b = new RamjetBuildOptions(WallThickness_mm: 1.5);
        Assert.Equal(a, b);

        var c = new RamjetBuildOptions(WallThickness_mm: 2.0);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void WithExpression_UpdatesIndividualFields()
    {
        var baseOpts = new RamjetBuildOptions();
        var withMat = baseOpts with { LpbfMaterial = LpbfMaterialProfiles.Inconel625 };

        Assert.Null(baseOpts.LpbfMaterial);
        Assert.Same(LpbfMaterialProfiles.Inconel625, withMat.LpbfMaterial);
        Assert.Equal(baseOpts.WallThickness_mm, withMat.WallThickness_mm);  // other fields unchanged
    }
}
