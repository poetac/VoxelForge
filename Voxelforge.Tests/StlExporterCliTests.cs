// StlExporterCliTests.cs — Lock the argument-parser contract for the
// headless STL exporter. Parsing is pure (no PicoGK), so we test it in
// the same xUnit harness as every other pure-math module.

using Voxelforge.StlExporter;

namespace Voxelforge.Tests;

public class StlExporterCliTests
{
    [Fact]
    public void ValidArgs_Parse()
    {
        var a = CliArgs.Parse(new[] { "--design", "a.json", "--voxel", "0.25", "--out", "b.stl" });
        Assert.Equal("a.json", a.DesignPath);
        Assert.Equal(0.25f, a.VoxelSizeMM);
        Assert.Equal("b.stl", a.OutputPath);
    }

    [Fact]
    public void ArgOrder_DoesNotMatter()
    {
        var a = CliArgs.Parse(new[] { "--voxel", "0.4", "--out", "b.stl", "--design", "a.json" });
        Assert.Equal("a.json", a.DesignPath);
        Assert.Equal(0.4f, a.VoxelSizeMM);
        Assert.Equal("b.stl", a.OutputPath);
    }

    [Fact]
    public void MissingArg_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CliArgs.Parse(new[] { "--voxel", "0.4", "--out", "b.stl" }));
        Assert.Throws<ArgumentException>(() =>
            CliArgs.Parse(new[] { "--design", "a.json", "--out", "b.stl" }));
    }

    [Fact]
    public void MissingValueAfterFlag_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CliArgs.Parse(new[] { "--design", "a.json", "--voxel" }));
    }

    [Fact]
    public void InvalidVoxelNumber_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CliArgs.Parse(new[] { "--design", "a.json", "--voxel", "not-a-number", "--out", "b.stl" }));
    }

    [Fact]
    public void OutOfRangeVoxel_Throws()
    {
        // Floor: 0.05 mm; ceiling: 2.0 mm. Catches both accidental zero and
        // accidental meters-as-millimetres inputs.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CliArgs.Parse(new[] { "--design", "a.json", "--voxel", "0.001", "--out", "b.stl" }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CliArgs.Parse(new[] { "--design", "a.json", "--voxel", "50", "--out", "b.stl" }));
    }

    [Fact]
    public void UnknownArg_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CliArgs.Parse(new[] { "--design", "a.json", "--voxel", "0.4", "--out", "b.stl", "--bogus" }));
    }

    [Fact]
    public void VoxelParsesWithInvariantCulture()
    {
        // Regression: on de-DE systems "0.25" → 25 unless culture is forced.
        // Parser uses InvariantCulture; this test will catch if someone
        // drops that later.
        var a = CliArgs.Parse(new[] { "--design", "a.json", "--voxel", "0.125", "--out", "b.stl" });
        Assert.Equal(0.125f, a.VoxelSizeMM);
    }

    // ── Sprint 28 (2026-04-23): --monolithic and companion flags. ──────

    [Fact]
    public void Default_MonolithicFlagsAreOff()
    {
        var a = CliArgs.Parse(new[] { "--design", "a.json", "--voxel", "0.25", "--out", "b.stl" });
        Assert.False(a.Monolithic);
        Assert.Null(a.BendFilletRadius_mm);
        Assert.True(a.IncludeFlanges);
        Assert.True(a.IncludePreburner);
    }

    [Fact]
    public void MonolithicFlag_Parses()
    {
        var a = CliArgs.Parse(new[] { "--design", "a.json", "--voxel", "0.25", "--out", "b.stl", "--monolithic" });
        Assert.True(a.Monolithic);
    }

    [Fact]
    public void MonolithicCompanions_Parse()
    {
        var a = CliArgs.Parse(new[]
        {
            "--design", "a.json", "--voxel", "0.25", "--out", "b.stl",
            "--monolithic",
            "--fillet", "1.5",
            "--no-flanges",
            "--no-preburner",
        });
        Assert.True(a.Monolithic);
        Assert.Equal(1.5, a.BendFilletRadius_mm);
        Assert.False(a.IncludeFlanges);
        Assert.False(a.IncludePreburner);
    }

    [Fact]
    public void MonolithicCompanions_WithoutMonolithic_Throws()
    {
        // Guards against confusing silent-ignore if a user passes --fillet
        // without --monolithic. These knobs only make sense on the fused path.
        Assert.Throws<ArgumentException>(() => CliArgs.Parse(new[]
        {
            "--design", "a.json", "--voxel", "0.25", "--out", "b.stl",
            "--fillet", "1.5",
        }));
        Assert.Throws<ArgumentException>(() => CliArgs.Parse(new[]
        {
            "--design", "a.json", "--voxel", "0.25", "--out", "b.stl",
            "--no-flanges",
        }));
        Assert.Throws<ArgumentException>(() => CliArgs.Parse(new[]
        {
            "--design", "a.json", "--voxel", "0.25", "--out", "b.stl",
            "--no-preburner",
        }));
    }

    [Fact]
    public void FilletMissingValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => CliArgs.Parse(new[]
        {
            "--design", "a.json", "--voxel", "0.25", "--out", "b.stl",
            "--monolithic", "--fillet",
        }));
    }

    [Fact]
    public void FilletInvalidNumber_Throws()
    {
        Assert.Throws<ArgumentException>(() => CliArgs.Parse(new[]
        {
            "--design", "a.json", "--voxel", "0.25", "--out", "b.stl",
            "--monolithic", "--fillet", "not-a-number",
        }));
    }
}
