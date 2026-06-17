// OrbitRig.cs — animated orbit GIF from a turntable render sequence.
//
// Team V Wave 1 (2026-05-05). Produces a 360° animated GIF by:
//   1. Calling BlenderSubprocess.Run() with mode=turntable to render N PNG frames.
//   2. Composing the frame sequence into an infinite-loop GIF via Magick.NET.
//
// Production entry point: OrbitRig.Compose(opts).
// Testable inner: OrbitRig.ComposeGif() (internal, no Blender required in tests).
//
// GIF composition mirrors SaAnimationCapture.cs (PR #316, Benchmarks).

using System;
using System.Collections.Generic;
using System.IO;
using ImageMagick;
using Voxelforge.Renderer.Blender;

namespace Voxelforge.Renderer.Animation;

public static class OrbitRig
{
    public record OrbitRigOptions(
        string  StlPath,
        string  OutputGifPath,
        string  BlenderExe,
        string  RenderScript,
        string  MaterialPath,
        int     Width        = 1280,
        int     Height       = 720,
        int     Samples      = 64,
        string  Engine       = "BLENDER_EEVEE_NEXT",
        int     Frames       = 60,
        int     FrameDelayMs = 33,
        string? HdriPath     = null);

    public record OrbitRigResult(
        bool    Success,
        int     FramesRendered,
        string? GifPath,
        string? ErrorMessage);

    /// <summary>
    /// Renders a turntable sequence and composes it into an animated orbit GIF.
    /// Requires Blender to be installed. Returns a result with Success=false if
    /// Blender is unavailable (non-gating — callers should log and continue).
    /// </summary>
    public static OrbitRigResult Compose(OrbitRigOptions opts)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"vf-orbit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string frameBase = Path.Combine(tempDir, "frame.png");
            var payload = new BlenderSubprocess.RenderPayload(
                InputStl:     opts.StlPath,
                OutputPath:   frameBase,
                MaterialPath: opts.MaterialPath,
                Width:        opts.Width,
                Height:       opts.Height,
                Samples:      opts.Samples,
                Engine:       opts.Engine,
                Mode:         "turntable",
                Frames:       opts.Frames,
                HdriPath:     opts.HdriPath);

            int exitCode = BlenderSubprocess.Run(opts.BlenderExe, opts.RenderScript, payload);
            if (exitCode != 0)
                return new OrbitRigResult(false, 0, null, $"Blender exited with code {exitCode}");

            // Collect rendered frame PNGs: frame_0001.png … frame_NNNN.png
            var framePaths = new List<string>(opts.Frames);
            for (int i = 1; i <= opts.Frames; i++)
            {
                string fp = Path.Combine(tempDir, $"frame_{i:D4}.png");
                if (File.Exists(fp)) framePaths.Add(fp);
            }
            if (framePaths.Count == 0)
                return new OrbitRigResult(false, 0, null, "No frame PNGs found after turntable render");

            string absOut = Path.GetFullPath(opts.OutputGifPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absOut)!);
            ComposeGif(framePaths, absOut, opts.FrameDelayMs);
            return new OrbitRigResult(true, framePaths.Count, absOut, null);
        }
        catch (Exception ex)
        {
            return new OrbitRigResult(false, 0, null, ex.Message);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Composes a list of PNG file paths into an infinite-loop animated GIF.
    /// Internal so tests can call it without Blender installed.
    /// Mirrors SaAnimationCapture.cs lines 277–298 (GIF89a centisecond delays).
    /// </summary>
    internal static void ComposeGif(
        IReadOnlyList<string> framePaths,
        string outputGifPath,
        int frameDelayMs,
        int holdLastFrameMs = 1000)
    {
        string? outDir = Path.GetDirectoryName(Path.GetFullPath(outputGifPath));
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        using var collection = new MagickImageCollection();
        for (int i = 0; i < framePaths.Count; i++)
        {
            var img = new MagickImage(framePaths[i]);
            // GIF89a AnimationDelay is in centiseconds (1/100 s).
            int delayCs = Math.Max(1, frameDelayMs / 10);
            if (i == framePaths.Count - 1)
                delayCs = Math.Max(delayCs, (frameDelayMs + holdLastFrameMs) / 10);
            img.AnimationDelay = (uint)delayCs;
            collection.Add(img);
        }
        collection.Coalesce();
        collection[0].AnimationIterations = 0; // infinite loop
        collection.Write(outputGifPath);
    }
}
