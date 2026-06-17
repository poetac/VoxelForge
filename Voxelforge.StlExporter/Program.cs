// Voxelforge.StlExporter — headless CLI that re-generates the
// chamber voxel geometry at an arbitrary voxel size and writes an STL.
//
// Why a separate process?
//   PicoGK's `Library.Go` sets the voxel size GLOBALLY for the life of
//   the process; it can't be changed mid-session. The interactive app
//   runs at 0.4 mm for responsive design iteration, but users want
//   fine-grained STLs (0.10–0.20 mm) for final print. We solve the
//   "can't change it" problem by doing the fine re-voxelisation in a
//   separate process that uses PicoGK's HEADLESS `new Library(voxel)`
//   constructor — no viewer, no task thread, no GLFW window flash.
//
// Topology dispatch (Sprint 28, 2026-04-23):
//   • ChannelTopology.Aerospike → AerospikeBuilder.Build(ToSpec(cond, design))
//     Honors the full saved design (channel schedule, plug-cooling opt-in,
//     injector pattern) via AerospikeOptimization.ToSpec.
//   • --monolithic → MonolithicEngineBuilder.BuildFromDesign(cond, design, …)
//     Fuses chamber + turbopump + feed manifold + preburner into one STL.
//     Unlike the Benchmarks `--monolithic` CLI (which takes only scalar
//     inputs and re-seeds through AutoSeeder), the exporter honors the
//     full saved design — channel schedule, film fraction, injector pattern,
//     flange specs, port standards all carry through. Dispatches on
//     design.ChannelTopology internally to pick the aerospike-composition core.
//   • Otherwise (bell) → existing ChamberVoxelBuilder.Build via GenerateWith.
//
// Contract:
//   Exit 0 + stdout "Exported …"  ⇒ success
//   Exit 1 + stderr message        ⇒ voxelisation / export failure
//   Exit 2 + stderr message        ⇒ bad design JSON
//   Exit 3 + stderr usage line     ⇒ malformed CLI args
//
// Invoked from the interactive app (Program.cs → HandleExportStlAtVoxel)
// whenever the user asks for a voxel size that differs from the session.

using System.Diagnostics;
using System.Globalization;
using PicoGK;
using Voxelforge.Geometry;
using Voxelforge.IO;
using Voxelforge.Optimization;

namespace Voxelforge.StlExporter;

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

        SavedDesign? saved;
        try { saved = DesignPersistence.Load(opts.DesignPath); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read design JSON '{opts.DesignPath}': {ex.Message}");
            return 2;
        }
        if (saved?.Conditions == null || saved.Design == null)
        {
            Console.Error.WriteLine("Design JSON missing Conditions or Design.");
            return 2;
        }

        try
        {
            // Headless PicoGK — no viewer, no log spam. Disposing unlocks
            // the process-wide "library running" flag so we exit cleanly.
            using var lib = new Library(opts.VoxelSizeMM);
            // PicoGK 2.0: scoped Library is not the global singleton; register
            // it as the ambient so builder methods reach the new Voxels(lib,…) overloads.
            using var _libScope = Voxelforge.Geometry.LibraryScope.Set(lib);

            Voxels voxels;
            string topologyLabel;

            if (opts.Monolithic)
            {
                (voxels, topologyLabel) = BuildMonolithic(saved.Conditions, saved.Design, opts);
            }
            else if (ChannelTopologyDispatcher.IsAerospikeAxisymmetric(saved.Design.ChannelTopology))
            {
                (voxels, topologyLabel) = BuildAerospikeOnly(saved.Conditions, saved.Design, opts);
            }
            else
            {
                (voxels, topologyLabel) = BuildBell(saved.Conditions, saved.Design, opts);
            }

            var export = ChamberVoxelBuilder.ExportStlProfiled(voxels, opts.OutputPath);

            // Emit structured BENCH lines on stdout so the
            // interactive app's RunSubprocessExportAsync can surface the
            // headline numbers in the status bar without grepping a log
            // file. The main app reads stdout via proc.StandardOutput.ReadToEnd().
            Console.WriteLine($"BENCH export_meshing_ms={export.Meshing_ms:F1}");
            Console.WriteLine($"BENCH triangle_count={export.TriangleCount}");
            Console.WriteLine($"BENCH export_stl_write_ms={export.StlWrite_ms:F1}");
            Console.WriteLine($"BENCH stl_bytes={export.StlBytes}");

            Console.WriteLine($"Exported {topologyLabel} STL at {opts.VoxelSizeMM:F3} mm → {opts.OutputPath}");
            Console.WriteLine(export.Message);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Voxel generation or STL write failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static (Voxels, string) BuildBell(
        OperatingConditions cond, RegenChamberDesign design, CliArgs opts)
    {
        // Thread the exporter voxel size through so the
        // BuildProfile stamped on gen.Geometry knows which voxel the
        // grid was built at (used for dense-equivalent voxel count +
        // cross-run scaling analysis).
        var gen = RegenChamberOptimization.GenerateWith(
            cond, design, voxelSize_mm: opts.VoxelSizeMM,
            voxelGenerator: new Voxelforge.Geometry.ChamberVoxelBuilderAdapter());
        if (gen.Geometry.Profile is { } profile)
            profile.EmitBench(Console.Out);
        double buildMs = gen.Geometry.Profile?.Total_ms ?? 0;
        Console.WriteLine($"BENCH total_build_ms={buildMs:F1}");
        return (gen.Geometry.Voxels.AsPicoGK(), "bell");
    }

    private static (Voxels, string) BuildAerospikeOnly(
        OperatingConditions cond, RegenChamberDesign design, CliArgs opts)
    {
        // Closes the silent-correctness gap where GenerateWith's voxel
        // path always produced a bell chamber — the aerospike sidecar
        // goes through BuildPhysicsOnly (no voxels). Jump straight to
        // AerospikeBuilder.Build, which takes an AerospikeSpec derived
        // from the full saved design via AerospikeOptimization.ToSpec.
        var spec = AerospikeOptimization.ToSpec(cond, design);
        var sw = Stopwatch.StartNew();
        var build = AerospikeBuilder.Build(spec, opts.VoxelSizeMM);
        sw.Stop();
        Console.WriteLine($"BENCH total_build_ms={sw.Elapsed.TotalMilliseconds:F1}");
        if (build.Voxels is null)
            throw new InvalidOperationException(
                "AerospikeBuilder returned no voxels (physics-only result).");
        return (build.Voxels!.AsPicoGK(), "aerospike");
    }

    private static (Voxels, string) BuildMonolithic(
        OperatingConditions cond, RegenChamberDesign design, CliArgs opts)
    {
        // Monolithic engine: chamber + turbopump + feed manifold + preburner
        // fused into a single voxel body. Routes through
        // MonolithicEngineBuilder.BuildFromDesign so the full saved design
        // drives the composition — channel schedule, film fraction,
        // injector pattern, flange specs, port standards all carry through
        // (unlike the EngineSpec overloads that re-seed from scalars via
        // AutoSeeder, which is what the Benchmarks --monolithic CLI uses).
        double fillet = opts.BendFilletRadius_mm
            ?? MonolithicEngineBuilder.DefaultBendFilletRadius_mm;

        var sw = Stopwatch.StartNew();
        var result = MonolithicEngineBuilder.BuildFromDesign(
            cond, design, opts.VoxelSizeMM,
            bendFilletRadius_mm:     fillet,
            includePumpMountFlanges: opts.IncludeFlanges,
            includePreburnerBody:    opts.IncludePreburner);
        sw.Stop();
        Console.WriteLine($"BENCH total_build_ms={sw.Elapsed.TotalMilliseconds:F1}");
        Console.WriteLine($"# {result.Description}");
        bool useAerospike = design.ChannelTopology == ChannelTopology.Aerospike;
        return (result.EngineVoxels, useAerospike ? "monolithic-aerospike" : "monolithic-bell");
    }
}

/// <summary>
/// Parsed CLI arguments for the STL exporter. Kept as its own record so
/// the parser is unit-testable without spinning up PicoGK.
/// </summary>
public sealed record CliArgs(
    string DesignPath,
    float VoxelSizeMM,
    string OutputPath,
    bool Monolithic = false,
    double? BendFilletRadius_mm = null,
    bool IncludeFlanges = true,
    bool IncludePreburner = true)
{
    public const string UsageLine =
        "Usage: Voxelforge.StlExporter --design <path.rcd.json> --voxel <mm> --out <path.stl> "
        + "[--monolithic [--fillet <mm>] [--no-flanges] [--no-preburner]]";

    public static CliArgs Parse(string[] args)
    {
        string? design = null;
        float? voxel = null;
        string? output = null;
        bool monolithic = false;
        double? bendFillet = null;
        bool includeFlanges = true;
        bool includePreburner = true;

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
                    if (!float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        throw new ArgumentException($"--voxel must be a float, got '{args[i]}'");
                    voxel = v;
                    break;
                case "--out":
                    if (i + 1 >= args.Length) throw new ArgumentException("--out missing value");
                    output = args[++i];
                    break;
                case "--monolithic":
                    monolithic = true;
                    break;
                case "--fillet":
                    if (i + 1 >= args.Length) throw new ArgumentException("--fillet missing value");
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                        throw new ArgumentException($"--fillet must be a float, got '{args[i]}'");
                    bendFillet = f;
                    break;
                case "--no-flanges":
                    includeFlanges = false;
                    break;
                case "--no-preburner":
                    includePreburner = false;
                    break;
                case "-h":
                case "--help":
                    throw new ArgumentException(UsageLine);
                default:
                    throw new ArgumentException($"Unknown arg '{args[i]}'");
            }
        }
        if (design == null || voxel == null || output == null)
            throw new ArgumentException("Missing required arg.");
        if (voxel < 0.05f || voxel > 2f)
            throw new ArgumentOutOfRangeException(nameof(voxel),
                $"voxel size {voxel.Value:F3} mm out of supported range 0.05–2.0 mm");
        if (!monolithic && (bendFillet.HasValue || !includeFlanges || !includePreburner))
            throw new ArgumentException(
                "--fillet, --no-flanges, --no-preburner require --monolithic.");
        return new CliArgs(design, voxel.Value, output, monolithic, bendFillet, includeFlanges, includePreburner);
    }
}
