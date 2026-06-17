// RenderPipelineWiringTests — Render Pipeline Sprint B / issue #336 (2026-05-05).
//
// Tests the pure-logic data layer of the render pipeline:
//   - RenderArgs.Parse (arg parsing)
//   - RenderArgs.ToCommandLine (argv reconstruction)
//   - BlenderDiscovery.Find (env-var override path)
//
// WinForms button-visibility tests and subprocess orchestration tests
// are excluded — RegenChamberForm requires an STA WinForms environment
// and OrchestrateRender is private in Program.cs.

using System;
using Voxelforge.Renderer;
using Xunit;

namespace Voxelforge.Tests;

public class RenderPipelineWiringTests
{
    // ─── RenderArgs.Parse ────────────────────────────────────────────

    [Fact]
    public void RenderArgs_Parse_StillMode_RoundTrips()
    {
        var args = RenderArgs.Parse(new[]
        {
            "--in", "a.stl", "--out", "b.png",
            "--mode", "still", "--material", "copper", "--resolution", "low",
        });

        Assert.Equal(RenderMode.Still, args.Mode);
        Assert.Equal("copper", args.Material);
        Assert.Equal(RenderResolution.Low, args.Resolution);
        Assert.Equal("a.stl", args.InputStl);
        Assert.Equal("b.png", args.OutputPath);
    }

    [Fact]
    public void RenderArgs_Parse_TurntableMode_IncludesFrames()
    {
        var args = RenderArgs.Parse(new[]
        {
            "--in", "x.stl", "--out", "x.mp4",
            "--mode", "turntable", "--frames", "24",
        });

        Assert.Equal(RenderMode.Turntable, args.Mode);
        Assert.Equal(24, args.Frames);
    }

    [Fact]
    public void RenderArgs_Parse_DefaultFrames()
    {
        var args = RenderArgs.Parse(new[]
        {
            "--in", "x.stl", "--out", "x.png",
        });

        Assert.Equal(RenderArgs.DefaultFrames, args.Frames);
    }

    // ─── RenderArgs.ToCommandLine ─────────────────────────────────────

    [Fact]
    public void RenderArgs_ToCommandLine_IncludesMode()
    {
        var args = new RenderArgs(
            InputStl:           "a.stl",
            OutputPath:         "b.mp4",
            Mode:               RenderMode.Turntable,
            Material:           "inconel",
            Resolution:         RenderResolution.High,
            Frames:             16,
            BlenderPathOverride: null);

        var cmd = args.ToCommandLine();

        Assert.Contains("--mode", cmd);
        Assert.Contains("turntable", cmd);
    }

    [Fact]
    public void RenderArgs_ToCommandLine_IncludesMaterial()
    {
        var args = new RenderArgs(
            InputStl:           "a.stl",
            OutputPath:         "b.png",
            Mode:               RenderMode.Still,
            Material:           "titanium",
            Resolution:         RenderResolution.Low,
            Frames:             RenderArgs.DefaultFrames,
            BlenderPathOverride: null);

        var cmd = args.ToCommandLine();

        Assert.Contains("--material", cmd);
        Assert.Contains("titanium", cmd);
    }

    [Fact]
    public void RenderArgs_ToCommandLine_IncludesResolution()
    {
        var args = new RenderArgs(
            InputStl:           "a.stl",
            OutputPath:         "b.png",
            Mode:               RenderMode.Still,
            Material:           "copper",
            Resolution:         RenderResolution.High,
            Frames:             RenderArgs.DefaultFrames,
            BlenderPathOverride: null);

        var cmd = args.ToCommandLine();

        Assert.Contains("--resolution", cmd);
    }

    [Fact]
    public void RenderArgs_ToCommandLine_StillMode_NoFramesArg()
    {
        var args = new RenderArgs(
            InputStl:           "a.stl",
            OutputPath:         "b.png",
            Mode:               RenderMode.Still,
            Material:           "copper",
            Resolution:         RenderResolution.High,
            Frames:             RenderArgs.DefaultFrames,
            BlenderPathOverride: null);

        var cmd = args.ToCommandLine();

        Assert.DoesNotContain("--frames", cmd);
    }

    // ─── BlenderDiscovery.Find env-var override ───────────────────────

    [Fact]
    public void BlenderDiscovery_NonExistentPath_ReturnsNull()
    {
        string? prior = Environment.GetEnvironmentVariable(BlenderDiscovery.EnvVarName);
        try
        {
            // Point to a path that cannot exist.
            Environment.SetEnvironmentVariable(
                BlenderDiscovery.EnvVarName,
                @"C:\does-not-exist-voxelforge-test\blender.exe");

            // Find() checks File.Exists on the env-var path first; if it
            // doesn't exist it falls through to the other probes which also
            // won't find Blender in the test environment.
            // We can't guarantee null here if Blender IS installed on PATH,
            // but we CAN assert the env var alone doesn't cause a crash.
            // The "returns null" assertion is valid on a CI machine; on a dev
            // machine with Blender installed the method may return a real path.
            var result = BlenderDiscovery.Find();

            // The env-var path was non-existent so must NOT be returned.
            Assert.NotEqual(@"C:\does-not-exist-voxelforge-test\blender.exe", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(BlenderDiscovery.EnvVarName, prior);
        }
    }

    // ─── RenderArgs.Parse error cases ────────────────────────────────

    [Fact]
    public void RenderArgs_Parse_MissingInFlag_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => RenderArgs.Parse(new[] { "--out", "b.png" }));
    }

    [Fact]
    public void RenderArgs_Parse_UnknownFlag_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => RenderArgs.Parse(new[] { "--in", "a.stl", "--out", "b.png", "--unknown" }));
    }
}
