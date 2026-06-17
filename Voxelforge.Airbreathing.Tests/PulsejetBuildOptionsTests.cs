// PulsejetBuildOptionsTests — defaults + immutable-record semantics for
// the pulsejet voxel builder's options record. Audit 05-test-gaps.md
// Section 2 Medium.

using Voxelforge.Airbreathing.Geometry;
using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.Tests;

public class PulsejetBuildOptionsTests
{
    [Fact]
    public void Defaults_AreSensibleForPulsejet()
    {
        // Defaults documented inline on the record — thinner wall than
        // the ramjet (1.5 mm vs 2.0 mm) per Foa §11.4 peak pressure
        // excursion.
        var opts = new PulsejetBuildOptions();
        Assert.Equal(1.5, opts.WallThickness_mm,    precision: 6);
        Assert.Equal(0.0, opts.VoxelSize_mm,        precision: 6);  // 0 = auto-resolve
        Assert.Equal(0.15, opts.SmoothenRadius_mm,  precision: 6);
        Assert.Null(opts.LpbfMaterial);
        Assert.Equal(64, opts.LpbfAzimuthalSamples);
        Assert.True(opts.RunLpbfAnalysis);
    }

    [Fact]
    public void RecordEquality_IsValueBased()
    {
        var a = new PulsejetBuildOptions(WallThickness_mm: 1.2);
        var b = new PulsejetBuildOptions(WallThickness_mm: 1.2);
        Assert.Equal(a, b);

        var c = new PulsejetBuildOptions(WallThickness_mm: 2.0);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void WithExpression_UpdatesIndividualFields()
    {
        var baseOpts = new PulsejetBuildOptions();
        var withMat = baseOpts with { LpbfMaterial = LpbfMaterialProfiles.Inconel625 };

        Assert.Null(baseOpts.LpbfMaterial);
        Assert.Same(LpbfMaterialProfiles.Inconel625, withMat.LpbfMaterial);
        Assert.Equal(baseOpts.WallThickness_mm, withMat.WallThickness_mm);
        Assert.Equal(baseOpts.RunLpbfAnalysis,   withMat.RunLpbfAnalysis);
    }

    [Fact]
    public void WithExpressionToggleRunLpbfAnalysis_Independent()
    {
        var on  = new PulsejetBuildOptions(RunLpbfAnalysis: true);
        var off = on with { RunLpbfAnalysis = false };
        Assert.True(on.RunLpbfAnalysis);
        Assert.False(off.RunLpbfAnalysis);
    }
}
