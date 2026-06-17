// Voxelforge.Marine.StlExporter — headless CLI that builds the AUV hull
// voxel geometry at an arbitrary voxel size and writes an STL.
//
// Mirrors Voxelforge.Airbreathing.StlExporter by:
//   • Using PicoGK's headless `new Library(voxelSize_mm)` constructor
//     (no viewer — runs cleanly in CI / sub-process test contexts).
//   • Emitting structured `BENCH triangle_count=N` lines on stdout so
//     SubprocessRunner-driven tests can parse outcomes.
//
// Sub-process testability is the reason this exists as a separate exe.
//
// Contract:
//   Exit 0 + `BENCH triangle_count=N` on stdout + STL written ⇒ success
//   Exit 1 + stderr message                                   ⇒ build / export failure
//   Exit 2 + stderr message                                   ⇒ malformed design JSON
//   Exit 3 + stderr usage line                                ⇒ malformed CLI args

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PicoGK;
using Voxelforge.Marine;
using Voxelforge.Marine.Geometry;

namespace Voxelforge.Marine.StlExporter;

public static class Program
{
    public static int Main(string[] args)
    {
        CliArgs opts;
        try { opts = CliArgs.Parse(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(CliArgs.UsageLine);
            return 3;
        }

        MarineDesign design;
        try
        {
            string json = File.ReadAllText(opts.DesignPath);
            design = JsonSerializer.Deserialize<MarineDesign>(json, JsonOpts)
                ?? throw new InvalidDataException("Design JSON deserialised to null.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read marine design JSON '{opts.DesignPath}': {ex.Message}");
            return 2;
        }

        try
        {
            design.ValidateSelf();
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Design validation failed: {ex.Message}");
            return 2;
        }

        try
        {
            using var lib = new Library((float)opts.VoxelSizeMm);
            using var _libScope = Voxelforge.Marine.Geometry.LibraryScope.Set(lib);

            var buildOpts = new MarineHullBuildOptions(
                WallThickness_mm:    opts.WallThicknessMm,
                VoxelSize_mm:        opts.VoxelSizeMm,
                SmoothenRadius_mm:   opts.SmoothenRadiusMm);

            var sw = Stopwatch.StartNew();
            var result = MarineHullVoxelBuilder.Build(design, buildOpts);
            sw.Stop();

            int triangleCount = MarineStlExport.Save(result.Shell, opts.OutputPath);

            Console.WriteLine($"BENCH total_build_ms={sw.Elapsed.TotalMilliseconds:F1}");
            Console.WriteLine($"BENCH triangle_count={triangleCount}");
            Console.WriteLine($"BENCH stl_bytes={new FileInfo(opts.OutputPath).Length}");
            Console.WriteLine($"BENCH shell_volume_mm3={result.ShellVolume_mm3:F1}");
            Console.WriteLine($"BENCH hull_length_mm={result.HullLength_mm:F1}");
            Console.WriteLine($"BENCH hull_diameter_mm={result.HullDiameter_mm:F1}");
            Console.WriteLine($"BENCH estimated_mass_g={result.EstimatedMass_g:F1}");
            Console.WriteLine($"# {result.Description}");
            Console.WriteLine($"Exported marine hull STL at {opts.VoxelSizeMm:F3} mm → {opts.OutputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Voxel generation or STL write failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };
}

public sealed record CliArgs(
    string DesignPath,
    double VoxelSizeMm,
    string OutputPath,
    double WallThicknessMm   = 4.0,
    double SmoothenRadiusMm  = 0.0)
{
    public const string UsageLine =
        "Usage: Voxelforge.Marine.StlExporter --design <marine-design.json> "
      + "--voxel <mm> --out <stl-path> [--wall <mm>] [--smoothen <mm>]";

    public static CliArgs Parse(string[] args)
    {
        string? design = null;
        double? voxel  = null;
        string? output = null;
        double wall    = 4.0;
        double smoothen = 0.0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--design":
                    if (i + 1 >= args.Length) throw new ArgumentException("--design missing value");
                    design = args[++i];
                    break;
                case "--voxel":
                    if (i + 1 >= args.Length) throw new ArgumentException("--voxel missing value");
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        throw new ArgumentException($"--voxel must be a float, got '{args[i]}'");
                    voxel = v;
                    break;
                case "--out":
                    if (i + 1 >= args.Length) throw new ArgumentException("--out missing value");
                    output = args[++i];
                    break;
                case "--wall":
                    if (i + 1 >= args.Length) throw new ArgumentException("--wall missing value");
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                        throw new ArgumentException($"--wall must be a float, got '{args[i]}'");
                    wall = w;
                    break;
                case "--smoothen":
                    if (i + 1 >= args.Length) throw new ArgumentException("--smoothen missing value");
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                        throw new ArgumentException($"--smoothen must be a float, got '{args[i]}'");
                    smoothen = s;
                    break;
                case "-h": case "--help":
                    throw new ArgumentException(UsageLine);
                default:
                    throw new ArgumentException($"Unknown arg '{args[i]}'");
            }
        }
        if (design is null || voxel is null || output is null)
            throw new ArgumentException("Missing required arg (--design, --voxel, --out).");
        if (voxel < 0.05 || voxel > 2.0)
            throw new ArgumentOutOfRangeException(nameof(voxel),
                $"voxel size {voxel.Value:F3} mm out of supported range 0.05-2.0 mm");
        if (wall <= 0)
            throw new ArgumentOutOfRangeException(nameof(wall),
                $"wall thickness {wall:F3} mm must be positive");
        return new CliArgs(design, voxel.Value, output, wall, smoothen);
    }
}
