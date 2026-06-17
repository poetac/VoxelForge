// Sprint render (2026-04-25) — BlenderDiscovery path-enumeration tests.

using System;
using System.Linq;
using Voxelforge.Renderer;
using Xunit;

namespace Voxelforge.Tests;

public class BlenderDiscoveryTests
{
    [Fact]
    public void EnvVarName_IsTheConstantWeAdvertise()
    {
        Assert.Equal("VOXELFORGE_BLENDER_PATH", BlenderDiscovery.EnvVarName);
    }

    [Fact]
    public void SearchedPaths_IncludesPathFallback()
    {
        var paths = BlenderDiscovery.SearchedPaths();
        Assert.Contains(paths, p => p.Contains("$PATH"));
    }

    [Fact]
    public void SearchedPaths_NeverEmpty()
    {
        Assert.NotEmpty(BlenderDiscovery.SearchedPaths());
    }

    [Fact]
    public void Find_ReturnsNullWhenNoBlender_OrAValidExistingPath()
    {
        // We can't reliably control whether Blender is installed on the test
        // host, but we CAN assert the contract: Find() either returns null
        // or returns a path that exists on disk.
        var found = BlenderDiscovery.Find();
        if (found != null)
        {
            Assert.True(System.IO.File.Exists(found),
                $"Find() returned '{found}' but file does not exist");
        }
    }
}
