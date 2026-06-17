// SubprocessFrameRenderer — production frame-render seam for OA-1
// (#287). Builds STL via Voxelforge.StlExporter subprocess + renders
// PNG via voxelforge-render subprocess. Locates both executables at
// repo-typical staging paths; returns null on failure so the
// orchestrator can skip the frame and continue.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Voxelforge.Benchmarks;

/// <summary>
/// Production <see cref="ISaFrameRenderer"/>. Each call writes a
/// transient STL + PNG inside the per-frame directory and returns
/// the PNG path on success. Failures (missing exe, non-zero exit)
/// log to stderr and return null — the orchestrator continues with
/// fewer frames rather than aborting the whole compose.
/// </summary>
internal sealed class SubprocessFrameRenderer : ISaFrameRenderer
{
    private readonly string _stlExporterExe;
    private readonly string _rendererExe;
    private readonly double _voxelSize_mm;
    private readonly string _material;
    private readonly string _resolution;

    // Internal accessors for callers in the same assembly that need the exe
    // paths directly (e.g. BenchRenderPreset orbit-GIF invocation).
    internal string StlExporterExe => _stlExporterExe;
    internal string RendererExe    => _rendererExe;
    internal double VoxelSize_mm   => _voxelSize_mm;

    public SubprocessFrameRenderer(
        string stlExporterExe,
        string rendererExe,
        double voxelSize_mm,
        string material,
        string resolution)
    {
        _stlExporterExe = stlExporterExe;
        _rendererExe    = rendererExe;
        _voxelSize_mm   = voxelSize_mm;
        _material       = material;
        _resolution     = resolution;
    }

    /// <summary>
    /// Auto-discover the two production exes. Returns null if either
    /// is missing — caller should fall back to skipping animation.
    /// Mirrors the path-walk in
    /// <c>KioskPipeline.LocateRenderExe</c> but checks both
    /// executables instead of just the renderer.
    /// </summary>
    public static SubprocessFrameRenderer? AutoDiscover(
        double voxelSize_mm = 0.5,
        string material     = "copper",
        string resolution   = "low")
    {
        string baseDir = AppContext.BaseDirectory;
        // baseDir is …/<projectRoot>/bin/<config>/net9.0-windows/.
        // Strip one segment to get …/<projectRoot>/bin/<config>, then
        // GetFileName lifts off the leaf — that's the build config.
        string trimmed = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? configDir = Path.GetDirectoryName(trimmed);
        string  config    = Path.GetFileName(configDir ?? "") ?? "";
        if (string.IsNullOrEmpty(config)) config = "Release";
        // Repo root is four levels up from baseDir
        // (net9.0-windows → <config> → bin → <projectRoot> → repoRoot).
        string repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));

        // The renderer's OutDir lands at <repo>/Voxelforge/bin/<config>/net9.0-windows/
        // (per its csproj — AssemblyName="voxelforge-render"). The
        // StlExporter's OutDir lands at <repo>/Voxelforge/bin/<config>/net9.0-windows/
        // (the AssemblyName/folder asymmetry from Sprint 0 PR-2 — see
        // the long comment on KioskPipeline.LocateRenderExe). The Tests
        // bin folder also has both as a copy convenience.
        var rendererProbes = new System.Collections.Generic.List<string>
        {
            Path.Combine(baseDir, "voxelforge-render.exe"),
            Path.Combine(repoRoot, "Voxelforge", "bin", config, "net9.0-windows", "voxelforge-render.exe"),
            Path.Combine(repoRoot, "Voxelforge", "bin", "Debug",   "net9.0-windows", "voxelforge-render.exe"),
            Path.Combine(repoRoot, "Voxelforge", "bin", "Release", "net9.0-windows", "voxelforge-render.exe"),
        };
        var stlExporterProbes = new System.Collections.Generic.List<string>
        {
            Path.Combine(baseDir, "Voxelforge.StlExporter.exe"),
            Path.Combine(repoRoot, "Voxelforge", "bin", config, "net9.0-windows", "Voxelforge.StlExporter.exe"),
            Path.Combine(repoRoot, "Voxelforge", "bin", "Debug",   "net9.0-windows", "Voxelforge.StlExporter.exe"),
            Path.Combine(repoRoot, "Voxelforge", "bin", "Release", "net9.0-windows", "Voxelforge.StlExporter.exe"),
        };

        string? renderer    = FirstExisting(rendererProbes);
        string? stlExporter = FirstExisting(stlExporterProbes);

        if (renderer is null)
        {
            Console.Error.WriteLine("SaAnimationCapture: voxelforge-render.exe not found. Probed:");
            foreach (var p in rendererProbes) Console.Error.WriteLine($"  - {p}");
            return null;
        }
        if (stlExporter is null)
        {
            Console.Error.WriteLine("SaAnimationCapture: Voxelforge.StlExporter.exe not found. Probed:");
            foreach (var p in stlExporterProbes) Console.Error.WriteLine($"  - {p}");
            return null;
        }

        return new SubprocessFrameRenderer(stlExporter, renderer, voxelSize_mm, material, resolution);
    }

    public string? RenderFrame(SaFrameSnapshot frame, string frameDir)
    {
        Directory.CreateDirectory(frameDir);

        // 1. Write design+conditions JSON for the StlExporter.
        string designPath;
        try
        {
            designPath = SaAnimationCapture.WriteFrameDesignJson(frame, frameDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"SaAnimationCapture: design persist failed for iter {frame.Iteration}: {ex.Message}");
            return null;
        }

        // 2. Build STL.
        string stlPath = Path.Combine(frameDir, $"frame-iter-{frame.Iteration:D5}.stl");
        if (!RunSubprocess(
                _stlExporterExe,
                new[]
                {
                    "--design", designPath,
                    "--voxel",  _voxelSize_mm.ToString("F3", CultureInfo.InvariantCulture),
                    "--out",    stlPath,
                },
                "Voxelforge.StlExporter"))
        {
            return null;
        }

        // 3. Render PNG.
        string pngPath = Path.Combine(frameDir, $"frame-iter-{frame.Iteration:D5}.png");
        if (!RunSubprocess(
                _rendererExe,
                new[]
                {
                    "--in",         stlPath,
                    "--out",        pngPath,
                    "--mode",       "still",
                    "--material",   _material,
                    "--resolution", _resolution,
                },
                "voxelforge-render"))
        {
            return null;
        }

        return File.Exists(pngPath) ? pngPath : null;
    }

    private static bool RunSubprocess(string exe, string[] args, string label)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = exe,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var sbErr = new StringBuilder();
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                Console.Error.WriteLine($"SaAnimationCapture: {label} Process.Start returned null");
                return false;
            }
            // Drain stdout/stderr so the child doesn't block on a full
            // pipe buffer. We only surface stderr on failure.
            proc.OutputDataReceived += (_, _) => { /* discard */ };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) sbErr.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                Console.Error.WriteLine(
                    $"SaAnimationCapture: {label} exit {proc.ExitCode}: " +
                    (sbErr.Length > 400 ? sbErr.ToString(0, 400) + "…" : sbErr.ToString()));
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SaAnimationCapture: {label} threw {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string? FirstExisting(System.Collections.Generic.IEnumerable<string> paths)
    {
        foreach (var p in paths)
            if (File.Exists(p)) return p;
        return null;
    }
}
