// BenchRenderPreset.cs — site-render helper (2026-05-01).
// Renders a still PNG and/or animated orbit GIF of a canonical design
// preset seed without running SA. Reuses SubprocessFrameRenderer for the
// still path; routes the orbit GIF through voxelforge-render's built-in
// turntable+.gif mode (which delegates to OrbitRig internally).
//
// Team V Wave 1 (2026-05-05): loads per-preset JSON config for default
// material/resolution; adds --out-gif for orbit GIF output.
//
// Usage:
//   dotnet run --project Voxelforge.Benchmarks -- --render-preset <name>
//       --out <path.png> [--out-gif <path.gif>]
//       [--voxel <mm=0.6>] [--material <copper>] [--resolution <low|high|maximum=low>]

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Voxelforge.Benchmarks;

internal static class BenchRenderPreset
{
    private const string UsageLine =
        "Usage: --render-preset <merlin|rl10|aerospike|pintle|pressure-fed-small> " +
        "--out <path.png> [--out-gif <path.gif>] [--voxel <mm=0.6>] " +
        "[--material <copper>] [--resolution <low|high|maximum>]";

    public static int Run(string[] args)
    {
        string? presetName = null;
        string? outPath    = null;
        string? outGifPath = null;
        double  voxelMm    = 0.6;
        string? material   = null;   // null = load from preset JSON, then fall back to "copper"
        string? resolution = null;   // null = load from preset JSON, then fall back to "low"

        // First positional arg is the preset name (Program.cs strips "--render-preset").
        if (args.Length > 0 && !args[0].StartsWith('-'))
            presetName = args[0];

        for (int i = (presetName is null ? 0 : 1); i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out":        outPath    = args[++i]; break;
                case "--out-gif":    outGifPath = args[++i]; break;
                case "--voxel":      voxelMm    = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--material":   material   = args[++i]; break;
                case "--resolution": resolution = args[++i]; break;
                default:
                    Console.Error.WriteLine($"Unknown arg: {args[i]}");
                    Console.Error.WriteLine(UsageLine);
                    return 1;
            }
        }

        if (presetName is null || (outPath is null && outGifPath is null))
        {
            Console.Error.WriteLine(UsageLine);
            return 1;
        }

        CanonicalDesigns.Preset preset;
        try { preset = CanonicalDesigns.Get(presetName); }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        // Load per-preset JSON for default material + resolution.
        LoadPresetDefaults(presetName, ref material, ref resolution);
        material   ??= "copper";
        resolution ??= "low";

        var renderer = SubprocessFrameRenderer.AutoDiscover(voxelMm, material, resolution);
        if (renderer is null)
        {
            Console.Error.WriteLine(
                "Could not locate Voxelforge.StlExporter.exe or voxelforge-render.exe. " +
                "Build both in Release first: dotnet build voxelforge.sln -c Release");
            return 2;
        }

        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"vf-render-{presetName}-{Guid.NewGuid():N}");
        string frameDir = Path.Combine(tempDir, "frame");

        var frame = new SaFrameSnapshot(
            Iteration:  0,
            Score:      0.0,
            Conditions: preset.Seed.Conditions,
            Design:     preset.Seed.Design);

        try
        {
            // --- Still PNG ---
            // RenderFrame also builds the STL we can re-use for the orbit GIF.
            string? pngPath = null;
            if (outPath is not null)
            {
                Console.WriteLine($"Rendering {presetName} → {outPath} (voxel={voxelMm} mm, {material}/{resolution}) …");
                pngPath = renderer.RenderFrame(frame, frameDir);
                if (pngPath is null)
                {
                    Console.Error.WriteLine("Render failed — see stderr above for subprocess details.");
                    return 3;
                }
                string absOut = Path.GetFullPath(outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absOut)!);
                File.Copy(pngPath, absOut, overwrite: true);
                Console.WriteLine($"OK  {absOut}");
            }

            // --- Orbit GIF ---
            if (outGifPath is not null)
            {
                // Locate the STL: RenderFrame puts it at frame-iter-00000.stl in frameDir.
                // If only --out-gif was requested (no --out), we need to build the STL first.
                string stlPath = Path.Combine(frameDir, "frame-iter-00000.stl");
                if (!File.Exists(stlPath))
                {
                    // Build STL only (no full render needed).
                    if (!BuildStlOnly(frame, renderer, frameDir, out stlPath))
                    {
                        Console.Error.WriteLine("STL build for orbit GIF failed — see stderr.");
                        return 3;
                    }
                }

                return RenderOrbitGif(renderer.RendererExe, stlPath, outGifPath, material);
            }

            return 0;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // Load material + resolution from the preset JSON bundled alongside voxelforge-render.exe.
    // Best-effort: leaves the ref params null if the config cannot be found/parsed.
    private static void LoadPresetDefaults(string presetName, ref string? material, ref string? resolution)
    {
        // Only load if caller didn't supply explicit overrides.
        if (material is not null && resolution is not null) return;

        string? path = FindPresetConfigPath($"rocket-{presetName}");
        if (path is null) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            material   ??= root.TryGetProperty("material",   out var m) ? m.GetString() : null;
            resolution ??= root.TryGetProperty("resolution", out var r) ? r.GetString() : null;
        }
        catch { /* config load is best-effort */ }
    }

    private static string? FindPresetConfigPath(string configName)
    {
        string baseDir  = AppContext.BaseDirectory;
        string trimmed  = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string buildCfg = Path.GetFileName(Path.GetDirectoryName(trimmed) ?? "") is { Length: > 0 } s ? s : "Release";
        string repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));

        var candidates = new[]
        {
            Path.Combine(baseDir, "Presets", $"{configName}.json"),
            Path.Combine(repoRoot, "Voxelforge", "bin", buildCfg, "net9.0-windows", "Presets", $"{configName}.json"),
            Path.Combine(repoRoot, "Voxelforge", "bin", "Debug",   "net9.0-windows", "Presets", $"{configName}.json"),
            Path.Combine(repoRoot, "Voxelforge", "bin", "Release", "net9.0-windows", "Presets", $"{configName}.json"),
        };
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    // Build just the STL (no PNG render) for the orbit-GIF path when --out wasn't requested.
    private static bool BuildStlOnly(SaFrameSnapshot frame, SubprocessFrameRenderer renderer, string frameDir, out string stlPath)
    {
        Directory.CreateDirectory(frameDir);
        stlPath = Path.Combine(frameDir, "frame-iter-00000.stl");

        string designPath;
        try { designPath = SaAnimationCapture.WriteFrameDesignJson(frame, frameDir); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BenchRenderPreset: design persist failed: {ex.Message}");
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName              = renderer.StlExporterExe,
            UseShellExecute       = false,
            RedirectStandardError = true,
            CreateNoWindow        = true,
        };
        psi.ArgumentList.Add("--design"); psi.ArgumentList.Add(designPath);
        psi.ArgumentList.Add("--voxel");  psi.ArgumentList.Add(renderer.VoxelSize_mm.ToString("F3", CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--out");    psi.ArgumentList.Add(stlPath);

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            proc.BeginErrorReadLine();
            proc.WaitForExit();
            return proc.ExitCode == 0 && File.Exists(stlPath);
        }
        catch { return false; }
    }

    // Invoke voxelforge-render with --mode turntable --out <path>.gif.
    // The renderer's built-in turntable+GIF routing calls OrbitRig internally.
    private static int RenderOrbitGif(string rendererExe, string stlPath, string outGifPath, string material)
    {
        string absGif = Path.GetFullPath(outGifPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absGif)!);

        Console.WriteLine($"Rendering orbit GIF → {absGif} (60 frames, {material}/low) …");

        var psi = new ProcessStartInfo
        {
            FileName              = rendererExe,
            UseShellExecute       = false,
            RedirectStandardError = true,
            CreateNoWindow        = true,
        };
        psi.ArgumentList.Add("--in");         psi.ArgumentList.Add(stlPath);
        psi.ArgumentList.Add("--out");        psi.ArgumentList.Add(absGif);
        psi.ArgumentList.Add("--mode");       psi.ArgumentList.Add("turntable");
        psi.ArgumentList.Add("--frames");     psi.ArgumentList.Add("60");
        psi.ArgumentList.Add("--material");   psi.ArgumentList.Add(material);
        psi.ArgumentList.Add("--resolution"); psi.ArgumentList.Add("low");

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                Console.Error.WriteLine("voxelforge-render start failed.");
                return 3;
            }
            proc.BeginErrorReadLine();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                Console.Error.WriteLine($"voxelforge-render orbit GIF exited {proc.ExitCode}.");
                return 3;
            }
            Console.WriteLine($"OK  {absGif}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"voxelforge-render orbit GIF threw: {ex.Message}");
            return 3;
        }
    }
}
