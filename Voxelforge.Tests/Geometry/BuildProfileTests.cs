// BuildProfileTests.cs — Issue #556 PR-1 (2026-05-16).
//
// Per audit 05-test-gaps.md § 1.2: BuildProfile is a Core record consumed
// by the chamber-voxel builder for per-stage wall-clock timings. The record
// itself was never named in test code; coverage was incidental via
// ChamberGeometryResult.Profile.

using System.IO;
using Voxelforge.Geometry;

namespace Voxelforge.Tests;

public class BuildProfileTests
{
    private static BuildProfile Sample() => new(
        Shell_ms:               12.0,
        Channels_ms:            34.0,
        ChannelVoxelise_ms:     18.0,
        ChannelBoolSubtract_ms: 11.0,
        ChannelCount:           80,
        Manifolds_ms:           6.5,
        RadialPorts_ms:         3.2,
        Smoothen_ms:            22.0,
        InjectorFlange_ms:      4.0,
        MountingFlange_ms:      2.0,
        InjectorBores_ms:       1.5,
        LateFeatures_ms:        7.0,
        FinalMeasurements_ms:   3.5,
        Total_ms:              125.0,
        VoxelSize_mm:           0.40,
        BBoxLx_mm:            120.0f,
        BBoxLy_mm:             80.0f,
        BBoxLz_mm:             80.0f,
        DenseEquivalentVoxels: 12_000_000L);

    [Fact]
    public void Ctor_StoresAllFieldsVerbatim()
    {
        var p = Sample();

        Assert.Equal(12.0, p.Shell_ms, precision: 6);
        Assert.Equal(34.0, p.Channels_ms, precision: 6);
        Assert.Equal(80, p.ChannelCount);
        Assert.Equal(0.40, p.VoxelSize_mm, precision: 6);
        Assert.Equal(120.0f, p.BBoxLx_mm);
        Assert.Equal(12_000_000L, p.DenseEquivalentVoxels);
    }

    [Fact]
    public void RecordEquality_HoldsOnIdenticalFieldValues()
    {
        var a = Sample();
        var b = Sample();
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void WithExpression_OnlyChangesTargetedField()
    {
        var a = Sample();
        var b = a with { ChannelCount = 100 };

        Assert.Equal(100, b.ChannelCount);
        Assert.Equal(a.Total_ms, b.Total_ms, precision: 6);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EmitBench_WritesPrefixedKeyValueLinesForEachField()
    {
        var p = Sample();
        var sw = new StringWriter();
        p.EmitBench(sw, prefix: "grid_build_");

        string output = sw.ToString();
        // Spot-check a handful of expected emissions — one per known key.
        Assert.Contains("BENCH grid_build_shell_ms=12.0", output);
        Assert.Contains("BENCH grid_build_channels_ms=34.0", output);
        Assert.Contains("BENCH grid_build_channel_count=80", output);
        Assert.Contains("BENCH grid_build_total_ms=125.0", output);
        Assert.Contains("BENCH voxel_size_mm=0.400", output);
        Assert.Contains("BENCH bbox_lx_mm=120.0", output);
        Assert.Contains("BENCH dense_voxels=12000000", output);
    }

    [Fact]
    public void EmitBench_CustomPrefixIsApplied()
    {
        var p = Sample();
        var sw = new StringWriter();
        p.EmitBench(sw, prefix: "phase2_");

        Assert.Contains("BENCH phase2_shell_ms=", sw.ToString());
        Assert.DoesNotContain("BENCH grid_build_shell_ms=", sw.ToString());
    }
}
