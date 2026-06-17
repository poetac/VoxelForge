// OrbitRigTests.cs — unit tests for OrbitRig.ComposeGif().
//
// Team V Wave 1 (2026-05-05). Tests target the internal ComposeGif() method
// directly, bypassing the Blender subprocess entirely. PNG fixtures are created
// with System.Drawing.Bitmap (available on net9.0-windows + UseWindowsForms)
// so no extra NuGet deps are needed in the test project.
//
// Pattern matches SaAnimationCapture stub-renderer tests from PR #316.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Voxelforge.Renderer.Animation;
using Xunit;

namespace Voxelforge.Tests.RendererTests;

public sealed class OrbitRigTests : IDisposable
{
    private readonly string _tempDir;

    public OrbitRigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vf-orbit-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void ComposeGif_WritesGifFromPngFrames()
    {
        var frames = CreateFrames(3, Color.Red, Color.Green, Color.Blue);
        string gifPath = Path.Combine(_tempDir, "orbit.gif");

        OrbitRig.ComposeGif(frames, gifPath, frameDelayMs: 100);

        Assert.True(File.Exists(gifPath));
        Assert.True(new FileInfo(gifPath).Length > 0);
    }

    [Fact]
    public void ComposeGif_OutputHasGif89aSignature()
    {
        var frames = CreateFrames(2, Color.Cyan, Color.Magenta);
        string gifPath = Path.Combine(_tempDir, "sig.gif");

        OrbitRig.ComposeGif(frames, gifPath, frameDelayMs: 50);

        byte[] bytes = File.ReadAllBytes(gifPath);
        // GIF89a header: G I F 8 9 a
        Assert.True(bytes.Length >= 6, "GIF file is unexpectedly short");
        Assert.Equal((byte)'G', bytes[0]);
        Assert.Equal((byte)'I', bytes[1]);
        Assert.Equal((byte)'F', bytes[2]);
        Assert.Equal((byte)'8', bytes[3]);
        Assert.Equal((byte)'9', bytes[4]);
        Assert.Equal((byte)'a', bytes[5]);
    }

    [Fact]
    public void ComposeGif_60Frames_ProducesReasonableFileSize()
    {
        // 60 solid-colour frames should produce a GIF that's clearly > a single-frame file.
        var colors = new Color[60];
        for (int i = 0; i < 60; i++)
            colors[i] = Color.FromArgb(i * 4, (i * 7) % 256, (i * 13) % 256);
        var frames = CreateFrames(60, colors);
        string gifPath = Path.Combine(_tempDir, "sixty.gif");

        OrbitRig.ComposeGif(frames, gifPath, frameDelayMs: 33, holdLastFrameMs: 1000);

        Assert.True(File.Exists(gifPath));
        long size = new FileInfo(gifPath).Length;
        Assert.True(size > 100, $"GIF is suspiciously small: {size} bytes");
    }

    [Fact]
    public void ComposeGif_OutputDirectoryIsCreatedIfMissing()
    {
        var frames = CreateFrames(2, Color.Yellow, Color.Blue);
        string nestedDir = Path.Combine(_tempDir, "nested", "subdir");
        string gifPath   = Path.Combine(nestedDir, "orbit.gif");

        OrbitRig.ComposeGif(frames, gifPath, frameDelayMs: 100);

        Assert.True(File.Exists(gifPath));
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private List<string> CreateFrames(int count, params Color[] colors)
    {
        var paths = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            Color color = colors.Length > 0 ? colors[i % colors.Length] : Color.Gray;
            string p = Path.Combine(_tempDir, $"frame_{i:D4}.png");
            CreatePng(p, color);
            paths.Add(p);
        }
        return paths;
    }

    private static void CreatePng(string path, Color color, int width = 16, int height = 16)
    {
        using var bmp = new Bitmap(width, height);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(color);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
