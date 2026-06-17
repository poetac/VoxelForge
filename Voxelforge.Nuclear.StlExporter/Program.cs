// Voxelforge.Nuclear.StlExporter — headless CLI that builds the NTR nozzle
// + stub core voxel geometry at an arbitrary voxel size and writes an STL.
//
// Mirrors Voxelforge.ElectricPropulsion.StlExporter / Voxelforge.StlExporter:
//   • Headless `new Library(voxelSize_mm)` constructor (no viewer).
//   • `BENCH triangle_count=N` + scalar lines on stdout for parsing.
//   • Exit codes — 0 success / 1 build failure / 2 malformed JSON / 3 bad CLI.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PicoGK;
using Voxelforge.Nuclear;
using Voxelforge.Nuclear.Geometry;

namespace Voxelforge.Nuclear.StlExporter;

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

        NuclearThermalDesign design;
        try
        {
            string json = File.ReadAllText(opts.DesignPath);
            design = JsonSerializer.Deserialize<NuclearThermalDesign>(json, JsonOpts)
                ?? throw new InvalidDataException("Design JSON deserialised to null.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read NTR design JSON '{opts.DesignPath}': {ex.Message}");
            return 2;
        }

        try
        {
            using var lib      = new Library((float)opts.VoxelSizeMm);
            using var _libScope = LibraryScope.Set(lib);

            var buildOpts = new NtrBuildOptions(
                VoxelSize_mm:      opts.VoxelSizeMm,
                SmoothenRadius_mm: opts.SmoothenRadiusMm,
                EnableStubCore:    true);

            var sw     = Stopwatch.StartNew();
            var result = NtrChamberVoxelBuilder.Build(design, buildOpts);
            sw.Stop();

            int triangleCount = NtrStlExport.Save(result.Voxels, opts.OutputPath);

            Console.WriteLine($"BENCH total_build_ms={sw.Elapsed.TotalMilliseconds:F1}");
            Console.WriteLine($"BENCH triangle_count={triangleCount}");
            Console.WriteLine($"BENCH stl_bytes={new FileInfo(opts.OutputPath).Length}");
            Console.WriteLine($"BENCH solid_volume_mm3={result.SolidVolume_mm3:F1}");
            Console.WriteLine($"BENCH nozzle_length_mm={result.NozzleLength_mm:F1}");
            Console.WriteLine($"BENCH bounding_diameter_mm={result.BoundingDiameter_mm:F1}");
            Console.WriteLine($"BENCH throat_radius_mm={result.ThroatRadius_mm:F2}");
            Console.WriteLine($"BENCH exit_radius_mm={result.ExitRadius_mm:F2}");
            Console.WriteLine($"BENCH expansion_ratio={result.ExpansionRatio:F2}");
            Console.WriteLine($"BENCH mass_g={result.TotalMass_g:F2}");

            Console.WriteLine($"# {result.Description}");
            Console.WriteLine($"Exported NTR STL at {opts.VoxelSizeMm:F3} mm → {opts.OutputPath}");
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
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
        NumberHandling              = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };
}

public sealed record CliArgs(
    string DesignPath,
    double VoxelSizeMm,
    string OutputPath,
    double SmoothenRadiusMm = 0.10)
{
    public const string UsageLine =
        "Usage: Voxelforge.Nuclear.StlExporter --design <ntr-design.json> "
      + "--voxel <mm> --out <stl-path> [--smoothen <mm>]";

    public static CliArgs Parse(string[] args)
    {
        string? design  = null;
        double? voxel   = null;
        string? output  = null;
        double  smoothen = 0.10;

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
                case "--smoothen":
                    if (i + 1 >= args.Length) throw new ArgumentException("--smoothen missing value");
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                        throw new ArgumentException($"--smoothen must be a float, got '{args[i]}'");
                    smoothen = s;
                    break;
                case "-h":
                case "--help":
                    throw new ArgumentException(UsageLine);
                default:
                    throw new ArgumentException($"Unknown arg '{args[i]}'");
            }
        }

        if (design is null || voxel is null || output is null)
            throw new ArgumentException("Missing required arg (--design, --voxel, --out).");
        if (voxel < 0.1 || voxel > 5.0)
            throw new ArgumentOutOfRangeException(nameof(voxel),
                $"Voxel size {voxel.Value:F3} mm out of supported range 0.1-5.0 mm for NTR nozzle.");
        return new CliArgs(design, voxel.Value, output, smoothen);
    }
}
