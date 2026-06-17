// Program.cs — entry point for voxelforge-render subprocess.
//
// Sprint render (2026-04-25) — Visual elegance / Noyron-parity track.
// Team V Wave 1 (2026-05-05): refactored to use BlenderSubprocess;
// added --hdri-path flag; turntable+.gif routes to OrbitRig.
//
// Architecture: pure orchestrator. Parses CLI args, finds blender.exe,
// validates inputs, then invokes Blender headless with the bundled
// templates/render.py via BlenderSubprocess.Run(). Animated-GIF orbit
// renders delegate to OrbitRig.Compose().
//
// Exit codes:
//   0 — success
//   2 — argument error (missing/invalid flags)
//   3 — Blender not found (auto-discovery failed; see SearchedPaths in stderr)
//   4 — input STL not found
//   5 — material spec not found
//   6 — render.py template not found
//   7 — Blender subprocess failed (forwards Blender's exit code if non-zero)

using System;
using System.IO;
using Voxelforge.Renderer;
using Voxelforge.Renderer.Animation;
using Voxelforge.Renderer.Blender;

try
{
    var renderArgs = RenderArgs.Parse(args);

    // Validate input STL.
    if (!File.Exists(renderArgs.InputStl))
    {
        Console.Error.WriteLine($"voxelforge-render: input STL not found: {renderArgs.InputStl}");
        return 4;
    }

    // Locate render.py + materials directory next to this exe.
    string baseDir    = AppContext.BaseDirectory;
    string renderScript = Path.Combine(baseDir, "templates", "render.py");
    string materialPath = Path.Combine(baseDir, "materials", $"{renderArgs.Material}.json");

    if (!File.Exists(renderScript))
    {
        Console.Error.WriteLine($"voxelforge-render: render.py template not found at {renderScript}");
        Console.Error.WriteLine("This usually means the build dropped templates/ — rebuild Voxelforge.Renderer.");
        return 6;
    }
    if (!File.Exists(materialPath))
    {
        Console.Error.WriteLine($"voxelforge-render: material '{renderArgs.Material}' not found at {materialPath}");
        Console.Error.WriteLine($"Available materials: {string.Join(", ", AvailableMaterials(baseDir))}");
        return 5;
    }

    // Resolve HDRi path: explicit --hdri-path override, else skip (render.py falls back).
    string? hdriPath = renderArgs.HdriPath;
    if (hdriPath is not null && !File.Exists(hdriPath))
    {
        Console.Error.WriteLine($"voxelforge-render: HDRi file not found: {hdriPath} (continuing with grey-blue fallback)");
        hdriPath = null;
    }

    // Locate Blender.
    string? blender = renderArgs.BlenderPathOverride ?? BlenderDiscovery.Find();
    if (string.IsNullOrEmpty(blender) || !File.Exists(blender))
    {
        Console.Error.WriteLine("voxelforge-render: blender.exe not found.");
        Console.Error.WriteLine("Searched (in order):");
        foreach (var p in BlenderDiscovery.SearchedPaths())
            Console.Error.WriteLine($"  - {p}");
        Console.Error.WriteLine($"\nFix: set $env:{BlenderDiscovery.EnvVarName} or pass --blender-path");
        return 3;
    }

    // Ensure output dir exists.
    var outDir = Path.GetDirectoryName(Path.GetFullPath(renderArgs.OutputPath));
    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        Directory.CreateDirectory(outDir);

    var (width, height, samples, engine) = ResolutionPresets.Spec(renderArgs.Resolution);

    Console.Error.WriteLine($"voxelforge-render: blender = {blender}");
    Console.Error.WriteLine($"voxelforge-render: mode={renderArgs.Mode} material={renderArgs.Material} resolution={renderArgs.Resolution} ({width}x{height}, {samples} samples, {engine})");
    if (renderArgs.Mode == RenderMode.Turntable)
        Console.Error.WriteLine($"voxelforge-render: turntable frames={renderArgs.Frames}");

    // Animated orbit GIF: turntable mode where output path ends in .gif.
    bool isGifOutput = renderArgs.OutputPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    if (renderArgs.Mode == RenderMode.Turntable && isGifOutput)
    {
        Console.Error.WriteLine($"voxelforge-render: composing orbit GIF → {renderArgs.OutputPath}");
        var opts = new OrbitRig.OrbitRigOptions(
            StlPath:       Path.GetFullPath(renderArgs.InputStl),
            OutputGifPath: Path.GetFullPath(renderArgs.OutputPath),
            BlenderExe:    blender,
            RenderScript:  renderScript,
            MaterialPath:  Path.GetFullPath(materialPath),
            Width:         width,
            Height:        height,
            Samples:       samples,
            Engine:        engine,
            Frames:        renderArgs.Frames,
            FrameDelayMs:  33,
            HdriPath:      hdriPath);
        var result = OrbitRig.Compose(opts);
        if (!result.Success)
        {
            Console.Error.WriteLine($"voxelforge-render: orbit GIF failed: {result.ErrorMessage}");
            return 7;
        }
        Console.Error.WriteLine($"voxelforge-render: wrote orbit GIF ({result.FramesRendered} frames) → {result.GifPath}");
        return 0;
    }

    // Standard still or turntable (raw PNGs).
    var payload = new BlenderSubprocess.RenderPayload(
        InputStl:     Path.GetFullPath(renderArgs.InputStl),
        OutputPath:   Path.GetFullPath(renderArgs.OutputPath),
        MaterialPath: Path.GetFullPath(materialPath),
        Width:        width,
        Height:       height,
        Samples:      samples,
        Engine:       engine,
        Mode:         RenderEnumNames.ToCommandLine(renderArgs.Mode),
        Frames:       renderArgs.Frames,
        HdriPath:     hdriPath);

    int exitCode = BlenderSubprocess.Run(blender, renderScript, payload);
    if (exitCode != 0)
    {
        Console.Error.WriteLine($"voxelforge-render: Blender exited with code {exitCode}");
        return 7;
    }

    Console.Error.WriteLine($"voxelforge-render: wrote {renderArgs.OutputPath}");
    return 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"voxelforge-render: {ex.Message}");
    return 2;
}

static string[] AvailableMaterials(string baseDir)
{
    var materialsDir = Path.Combine(baseDir, "materials");
    if (!Directory.Exists(materialsDir)) return Array.Empty<string>();
    var files = Directory.GetFiles(materialsDir, "*.json");
    var slugs = new string[files.Length];
    for (int i = 0; i < files.Length; i++)
        slugs[i] = Path.GetFileNameWithoutExtension(files[i]);
    return slugs;
}
