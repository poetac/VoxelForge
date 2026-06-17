// MarineHullBuildOptionsTests.cs — ctor + record-equality tests for the
// PicoGK-free options record consumed by the marine voxel pipeline.
// Per audit 05-test-gaps.md §4 the record was previously unreferenced.

using Voxelforge.Marine.Geometry;
using Xunit;

namespace Voxelforge.Marine.Tests.Geometry;

public sealed class MarineHullBuildOptionsTests
{
    // ── Ctor / defaults ──────────────────────────────────────────────────

    [Fact]
    public void Ctor_RequiredWallThickness_OptionalsDefaultToAuto()
    {
        // VoxelSize_mm and SmoothenRadius_mm default to 0 — sentinel
        // values meaning "auto" and "skip-smoothing" respectively.
        var opt = new MarineHullBuildOptions(WallThickness_mm: 4.0);
        Assert.Equal(4.0, opt.WallThickness_mm);
        Assert.Equal(0.0, opt.VoxelSize_mm);
        Assert.Equal(0.0, opt.SmoothenRadius_mm);
    }

    [Fact]
    public void Ctor_AllFieldsExplicit_RoundTrip()
    {
        var opt = new MarineHullBuildOptions(
            WallThickness_mm:  5.0,
            VoxelSize_mm:      0.25,
            SmoothenRadius_mm: 0.10);
        Assert.Equal(5.0,  opt.WallThickness_mm);
        Assert.Equal(0.25, opt.VoxelSize_mm);
        Assert.Equal(0.10, opt.SmoothenRadius_mm);
    }

    // ── MaxAutoVoxelSize_mm constant ────────────────────────────────────

    [Fact]
    public void MaxAutoVoxelSize_mm_IsPositive_AndAtPrintableCeiling()
    {
        // AUV hulls at 0.5–5 m length print well at ≤ 0.4 mm voxel — the
        // class doc-string pins this value. Catch unintended drift.
        Assert.Equal(0.4, MarineHullBuildOptions.MaxAutoVoxelSize_mm);
    }

    // ── Record equality + with-expression ───────────────────────────────

    [Fact]
    public void RecordEquality_StructurallyComparesByValue()
    {
        var a = new MarineHullBuildOptions(4.0, 0.3, 0.05);
        var b = new MarineHullBuildOptions(4.0, 0.3, 0.05);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void RecordEquality_DifferentWallThickness_NotEqual()
    {
        var a = new MarineHullBuildOptions(4.0);
        var b = new MarineHullBuildOptions(5.0);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WithExpression_OverridesVoxelSizeOnly()
    {
        var baseOpt = new MarineHullBuildOptions(WallThickness_mm: 4.0);
        var custom  = baseOpt with { VoxelSize_mm = 0.15 };
        // wall preserved, voxel updated, smoothing still at sentinel 0.
        Assert.Equal(4.0,  custom.WallThickness_mm);
        Assert.Equal(0.15, custom.VoxelSize_mm);
        Assert.Equal(0.0,  custom.SmoothenRadius_mm);
    }
}
