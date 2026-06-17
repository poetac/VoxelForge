// Voxelforge.Airbreathing.StlExporter — headless CLI that builds the
// ramjet voxel geometry at an arbitrary voxel size, optionally runs
// the LPBF printability pass, and writes an STL.
//
// Mirrors the rocket-side Voxelforge.StlExporter (Voxelforge.StlExporter)
// by:
//   • Using PicoGK's headless `new Library(voxelSize_mm)` constructor
//     (no viewer, no GLFW window — runs cleanly in CI / sub-process
//     test contexts).
//   • Emitting structured `BENCH triangle_count=N` + `GATE <id>` lines
//     on stdout so SubprocessRunner-driven tests can parse outcomes.
//
// Sub-process testability is the entire reason this exists as a
// separate exe — CLAUDE.md PicoGK pitfall #8 forbids `new PicoGK.Library`
// inside an xUnit host.
//
// Contract:
//   Exit 0 + `BENCH triangle_count=N` on stdout + STL written  ⇒ success
//   Exit 1 + stderr message                                    ⇒ build / export failure
//   Exit 2 + stderr message                                    ⇒ malformed design JSON
//   Exit 3 + stderr usage line                                 ⇒ malformed CLI args

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PicoGK;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Geometry;
using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.StlExporter;

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

        AirbreathingEngineDesign design;
        try
        {
            string json = File.ReadAllText(opts.DesignPath);
            design = JsonSerializer.Deserialize<AirbreathingEngineDesign>(json, JsonOpts)
                ?? throw new InvalidDataException("Design JSON deserialised to null.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read ramjet design JSON '{opts.DesignPath}': {ex.Message}");
            return 2;
        }

        if (design.Kind != AirbreathingEngineKind.Ramjet
            && design.Kind != AirbreathingEngineKind.Pulsejet
            && design.Kind != AirbreathingEngineKind.Turbofan)
        {
            Console.Error.WriteLine(
                $"This exporter supports Kind=Ramjet, Kind=Pulsejet, or Kind=Turbofan. Got Kind={design.Kind}.");
            return 2;
        }

        try
        {
            // Headless PicoGK: no viewer, runs on the calling thread.
            // Disposing the library unlocks the process-wide flag so we exit cleanly.
            using var lib = new Library((float)opts.VoxelSizeMm);
            // PicoGK 2.0: scoped Library is not the global singleton; register
            // it as the ambient so builder methods reach the new Voxels(lib,…) overloads.
            using var _libScope = Voxelforge.Airbreathing.Geometry.LibraryScope.Set(lib);

            LpbfMaterialProfile? mat = opts.LpbfMaterial;
            var sw = Stopwatch.StartNew();

            int triangleCount;
            string description;
            double solidVolume_mm3, boundingLength_mm, boundingDiameter_mm;
            LpbfPrintabilityResult? printability;

            if (design.Kind == AirbreathingEngineKind.Ramjet)
            {
                var contour = RamjetGeometry.From(design);
                var buildOpts = new RamjetBuildOptions(
                    WallThickness_mm:    opts.WallThicknessMm,
                    VoxelSize_mm:        opts.VoxelSizeMm,
                    SmoothenRadius_mm:   opts.SmoothenRadiusMm,
                    LpbfMaterial:        mat,
                    LpbfAzimuthalSamples: 64,
                    RunLpbfAnalysis:     mat is not null);
                var result = RamjetVoxelBuilder.Build(contour, buildOpts);
                sw.Stop();
                triangleCount       = RamjetStlExport.Save(result.Voxels, opts.OutputPath);
                description         = result.Description;
                solidVolume_mm3     = result.SolidVolume_mm3;
                boundingLength_mm   = result.BoundingLength_mm;
                boundingDiameter_mm = result.BoundingDiameter_mm;
                printability        = result.Printability;
            }
            else if (design.Kind == AirbreathingEngineKind.Pulsejet)
            {
                var contour = PulsejetGeometry.From(design);
                var buildOpts = new PulsejetBuildOptions(
                    WallThickness_mm:    opts.WallThicknessMm,
                    VoxelSize_mm:        opts.VoxelSizeMm,
                    SmoothenRadius_mm:   opts.SmoothenRadiusMm,
                    LpbfMaterial:        mat,
                    LpbfAzimuthalSamples: 64,
                    RunLpbfAnalysis:     mat is not null);
                var result = PulsejetVoxelBuilder.Build(contour, buildOpts);
                sw.Stop();
                triangleCount       = RamjetStlExport.Save(result.Voxels, opts.OutputPath);
                description         = result.Description;
                solidVolume_mm3     = result.SolidVolume_mm3;
                boundingLength_mm   = result.BoundingLength_mm;
                boundingDiameter_mm = result.BoundingDiameter_mm;
                printability        = result.Printability;
            }
            else  // Turbofan (Kind already gated above)
            {
                var contour = TurbofanGeometry.From(design);
                // --wall sets the core wall; per-design BypassDuctWallThickness_mm
                // sets the bypass-duct wall (separate knob, schema v9 → v10).
                var buildOpts = new TurbofanBuildOptions(
                    WallThickness_mm:           opts.WallThicknessMm,
                    BypassDuctWallThickness_mm: design.BypassDuctWallThickness_mm,
                    VoxelSize_mm:               opts.VoxelSizeMm,
                    SmoothenRadius_mm:          opts.SmoothenRadiusMm,
                    LpbfMaterial:               mat,
                    LpbfAzimuthalSamples:       64,
                    RunLpbfAnalysis:            mat is not null);
                var result = TurbofanVoxelBuilder.Build(contour, buildOpts);
                sw.Stop();
                triangleCount       = RamjetStlExport.Save(result.Voxels, opts.OutputPath);
                description         = result.Description;
                solidVolume_mm3     = result.SolidVolume_mm3;
                boundingLength_mm   = result.BoundingLength_mm;
                boundingDiameter_mm = result.BoundingDiameter_mm;
                printability        = result.Printability;
            }

            // BENCH lines (parseable by SubprocessRunner tests).
            Console.WriteLine($"BENCH total_build_ms={sw.Elapsed.TotalMilliseconds:F1}");
            Console.WriteLine($"BENCH triangle_count={triangleCount}");
            Console.WriteLine($"BENCH stl_bytes={new FileInfo(opts.OutputPath).Length}");
            Console.WriteLine($"BENCH solid_volume_mm3={solidVolume_mm3:F1}");
            Console.WriteLine($"BENCH bounding_length_mm={boundingLength_mm:F1}");
            Console.WriteLine($"BENCH bounding_diameter_mm={boundingDiameter_mm:F1}");

            // GATE lines (parseable by feasibility-firing tests). Emit only
            // when LPBF analysis ran and produced violations.
            if (printability is not null)
            {
                var violations = new System.Collections.Generic.List<Voxelforge.Optimization.FeasibilityViolation>();
                AirbreathingFeasibility.EvaluateLpbfGates(printability, violations);
                foreach (var v in violations)
                {
                    Console.WriteLine($"GATE {v.ConstraintId}");
                }
                Console.WriteLine($"BENCH lpbf_violation_count={violations.Count}");
            }

            Console.WriteLine($"# {description}");
            Console.WriteLine($"Exported {design.Kind} STL at {opts.VoxelSizeMm:F3} mm → {opts.OutputPath}");
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
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };
}

/// <summary>
/// Parsed CLI arguments. Mirrors the rocket exporter's CliArgs record
/// so test discovery / arg patterns are interchangeable.
/// </summary>
public sealed record CliArgs(
    string DesignPath,
    double VoxelSizeMm,
    string OutputPath,
    double WallThicknessMm = 2.0,
    double SmoothenRadiusMm = 0.15,
    LpbfMaterialProfile? LpbfMaterial = null)
{
    public const string UsageLine =
        "Usage: Voxelforge.Airbreathing.StlExporter --design <ramjet-design.json> "
      + "--voxel <mm> --out <stl-path> [--wall <mm>] [--smoothen <mm>] [--lpbf <material>]";

    /// <summary>
    /// Parse the CLI flags. Throws <see cref="ArgumentException"/> on
    /// any malformed / missing required arg.
    /// </summary>
    public static CliArgs Parse(string[] args)
    {
        string? design = null;
        double? voxel = null;
        string? output = null;
        double wall = 2.0;
        double smoothen = 0.15;
        LpbfMaterialProfile? lpbfMat = null;

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
                case "--lpbf":
                    if (i + 1 >= args.Length) throw new ArgumentException("--lpbf missing value");
                    lpbfMat = ParseLpbfMaterial(args[++i]);
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
        if (voxel < 0.05 || voxel > 2.0)
            throw new ArgumentOutOfRangeException(nameof(voxel),
                $"voxel size {voxel.Value:F3} mm out of supported range 0.05-2.0 mm");
        if (wall <= 0)
            throw new ArgumentOutOfRangeException(nameof(wall),
                $"wall thickness {wall:F3} mm must be positive");
        return new CliArgs(design, voxel.Value, output, wall, smoothen, lpbfMat);
    }

    private static LpbfMaterialProfile ParseLpbfMaterial(string token)
        => token.ToLowerInvariant() switch
        {
            "grcop42" or "grcop-42"           => LpbfMaterialProfiles.GRCop42,
            "cucrzr"                          => LpbfMaterialProfiles.CuCrZr,
            "inconel625" or "in625"           => LpbfMaterialProfiles.Inconel625,
            "inconel718" or "in718"           => LpbfMaterialProfiles.Inconel718,
            "stainless316l" or "316l" or "ss" => LpbfMaterialProfiles.Stainless316L,
            _ => throw new ArgumentException(
                $"Unknown --lpbf material '{token}'. "
              + $"Choices: grcop42, cucrzr, inconel625, inconel718, stainless316l."),
        };
}
