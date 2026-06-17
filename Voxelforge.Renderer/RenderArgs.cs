// RenderArgs.cs — parsed CLI args for the renderer subprocess.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Voxelforge.Renderer;

internal enum RenderMode
{
    Still,
    Turntable,
}

internal enum RenderResolution
{
    Low,        // 1280x720 / Eevee / 64 samples — ~5 s/frame
    High,       // 1920x1080 / Cycles / 256 samples — ~30 s/frame
    Maximum,    // 3840x2160 / Cycles / 1024 samples — ~3-5 min/frame
}

internal static class RenderEnumNames
{
    public static string ToCommandLine(RenderMode m) => m switch
    {
        RenderMode.Still     => "still",
        RenderMode.Turntable => "turntable",
        _ => throw new ArgumentOutOfRangeException(nameof(m), $"Unhandled RenderMode: {m}"),
    };

    public static string ToCommandLine(RenderResolution r) => r switch
    {
        RenderResolution.Low     => "low",
        RenderResolution.High    => "high",
        RenderResolution.Maximum => "maximum",
        _ => throw new ArgumentOutOfRangeException(nameof(r), $"Unhandled RenderResolution: {r}"),
    };
}

/// <summary>
/// Resolution → concrete render parameters. Exposed for both the C# CLI
/// validator and the Blender Python script (passed through as JSON).
/// </summary>
internal static class ResolutionPresets
{
    public static (int Width, int Height, int Samples, string Engine) Spec(RenderResolution res) => res switch
    {
        RenderResolution.Low     => (1280, 720,  64,   "BLENDER_EEVEE_NEXT"),
        RenderResolution.High    => (1920, 1080, 256,  "CYCLES"),
        RenderResolution.Maximum => (3840, 2160, 1024, "CYCLES"),
        _ => throw new ArgumentOutOfRangeException(nameof(res)),
    };
}

internal sealed record RenderArgs(
    string         InputStl,
    string         OutputPath,
    RenderMode     Mode,
    string         Material,           // material slug; resolved against materials/<slug>.json
    RenderResolution Resolution,
    int            Frames,             // turntable only; ignored for still
    string?        BlenderPathOverride, // null = auto-discover
    string?        HdriPath = null     // null = use bundled studio.exr fallback in render.py
)
{
    public const int DefaultFrames = 16;

    /// <summary>
    /// Parse argv into <see cref="RenderArgs"/>. Throws <see cref="ArgumentException"/>
    /// with a user-readable message on any error.
    /// </summary>
    public static RenderArgs Parse(string[] args)
    {
        string? input = null, output = null, material = "copper", blender = null, hdriPath = null;
        var mode = RenderMode.Still;
        var resolution = RenderResolution.High;
        int frames = DefaultFrames;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--in":
                    input = RequireValue(args, ref i, "--in");
                    break;
                case "--out":
                    output = RequireValue(args, ref i, "--out");
                    break;
                case "--mode":
                    mode = ParseMode(RequireValue(args, ref i, "--mode"));
                    break;
                case "--material":
                    material = ValidateMaterialSlug(RequireValue(args, ref i, "--material"));
                    break;
                case "--resolution":
                    resolution = ParseResolution(RequireValue(args, ref i, "--resolution"));
                    break;
                case "--frames":
                    if (!int.TryParse(RequireValue(args, ref i, "--frames"), out frames) || frames < 1)
                        throw new ArgumentException("--frames must be a positive integer");
                    break;
                case "--blender-path":
                    blender = RequireValue(args, ref i, "--blender-path");
                    break;
                case "--hdri-path":
                    hdriPath = RequireValue(args, ref i, "--hdri-path");
                    break;
                case "--help" or "-h":
                    throw new ArgumentException(UsageLine);
                default:
                    throw new ArgumentException($"unknown argument: {args[i]}\n\n{UsageLine}");
            }
        }

        if (string.IsNullOrEmpty(input))  throw new ArgumentException("--in is required");
        if (string.IsNullOrEmpty(output)) throw new ArgumentException("--out is required");

        return new RenderArgs(
            InputStl:            input,
            OutputPath:          output,
            Mode:                mode,
            Material:            material,
            Resolution:          resolution,
            Frames:              frames,
            BlenderPathOverride: blender,
            HdriPath:            hdriPath);
    }

    /// <summary>Produces the argv list to pass to the voxelforge-render subprocess.</summary>
    public IReadOnlyList<string> ToCommandLine()
    {
        var args = new List<string> {
            "--in",         InputStl,
            "--out",        OutputPath,
            "--mode",       RenderEnumNames.ToCommandLine(Mode),
            "--material",   Material,
            "--resolution", RenderEnumNames.ToCommandLine(Resolution),
        };
        if (Mode == RenderMode.Turntable)
        {
            args.Add("--frames");
            args.Add(Frames.ToString());
        }
        if (BlenderPathOverride is not null)
        {
            args.Add("--blender-path");
            args.Add(BlenderPathOverride);
        }
        if (HdriPath is not null)
        {
            args.Add("--hdri-path");
            args.Add(HdriPath);
        }
        return args;
    }

    public const string UsageLine =
        "Usage: voxelforge-render --in <stl-path> --out <png-or-gif-path>\n" +
        "                         [--mode still|turntable]   (default: still)\n" +
        "                         [--material copper|inconel|titanium|stainless]   (default: copper)\n" +
        "                         [--resolution low|high|maximum]   (default: high)\n" +
        "                         [--frames N]   (turntable only; default: 16)\n" +
        "                         [--hdri-path <abs-path-to.exr>]   (else bundled studio.exr fallback)\n" +
        "                         [--blender-path <path-to-blender.exe>]   (else auto-discover)\n" +
        "Note: --mode turntable --out <path>.gif produces an animated GIF (Magick.NET).";

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{flag} requires a value");
        return args[++i];
    }

    /// <summary>
    /// Slug-validates the <c>--material</c> CLI value. Accepts only
    /// <c>[a-zA-Z0-9_-]+</c>; rejects any path-separator, drive-letter,
    /// parent-directory token, or other shape that could escape the
    /// <c>materials/&lt;slug&gt;.json</c> lookup root.
    ///
    /// Audit 01-security.md M1: the renderer concatenates this value into
    /// a file path via <c>Path.Combine(baseDir, "materials", $"{material}.json")</c>.
    /// <c>Path.Combine</c> does not normalise <c>..</c> segments, so without
    /// this guard a caller could pass <c>--material ../../../Users/x/secrets</c>
    /// and have the renderer read arbitrary <c>.json</c> files on disk.
    /// Validating at the parse site means every downstream consumer
    /// (Program.cs, OrbitRig, BlenderSubprocess) can assume the slug is safe.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="material"/>
    /// is empty or does not match <c>^[a-zA-Z0-9_-]+$</c>.</exception>
    internal static string ValidateMaterialSlug(string material)
    {
        if (string.IsNullOrEmpty(material))
            throw new ArgumentException("--material must not be empty", nameof(material));
        if (!MaterialSlugRegex.IsMatch(material))
            throw new ArgumentException(
                $"--material must match [a-zA-Z0-9_-]+ (got '{material}'); " +
                "path separators, drive letters, '..' segments, and whitespace are rejected.",
                nameof(material));
        return material;
    }

    private static readonly Regex MaterialSlugRegex =
        new(@"^[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static RenderMode ParseMode(string s) => s.ToLowerInvariant() switch
    {
        "still" => RenderMode.Still,
        "turntable" => RenderMode.Turntable,
        _ => throw new ArgumentException($"--mode must be 'still' or 'turntable', got '{s}'"),
    };

    private static RenderResolution ParseResolution(string s) => s.ToLowerInvariant() switch
    {
        "low" => RenderResolution.Low,
        "high" => RenderResolution.High,
        "maximum" or "max" => RenderResolution.Maximum,
        _ => throw new ArgumentException($"--resolution must be 'low', 'high', or 'maximum', got '{s}'"),
    };
}
