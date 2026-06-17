using System.Diagnostics;
using System.Globalization;
using System.Text;
using PicoGK;
using Voxelforge.Analysis;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.Injector;
using Voxelforge.Injector.Elements;
using Voxelforge.IO;
using Voxelforge.Manufacturing;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

public static partial class Program
{
    // ════════════════════════════════════════════════════════════════
    //  Meganewton-class envelope + sweep
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Envelope probe — prints <see cref="MegaScaleEnvelope"/>'s
    /// recommendations for every preset thrust class at a user-
    /// selected RAM budget. No voxel build; sub-second runtime.
    ///
    /// Usage:
    ///   dotnet run --project Voxelforge.Benchmarks -- \
    ///       --probe-envelope [--budget-gb 48]
    /// </summary>
    private static int RunEnvelopeProbe(string[] args)
    {
        double budgetGB = 48.0;  // default to 96 GB current-workstation tier, Balanced mode
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--budget-gb" && i + 1 < args.Length)
            {
                if (!double.TryParse(args[++i], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out budgetGB) || budgetGB <= 0)
                {
                    Console.Error.WriteLine("--budget-gb must be a positive number.");
                    return 3;
                }
            }
            else
            {
                Console.Error.WriteLine($"Unknown arg '{args[i]}'.");
                return 3;
            }
        }

        long budgetBytes = (long)(budgetGB * 1024 * 1024 * 1024);
        Console.WriteLine($"# MegaScaleEnvelope probe — budget {budgetGB:F0} GB "
                        + $"({budgetBytes / 1_000_000_000.0:F1} GB / 2^30)");
        Console.WriteLine("# Thrust(kN)  Voxel(mm)  Tiles  Mode        Peak(GB)  Feas  Rationale");
        Console.WriteLine("# ----------  ---------  -----  ----------  --------  ----  ---------");

        foreach (var p in MegaScaleEnvelope.PresetsCurrent)
        {
            var rec = MegaScaleEnvelope.Recommend(p.Thrust_N, budgetBytes);
            double peakGB = rec.ProjectedPeakBytes / (1024.0 * 1024 * 1024);
            string flag = rec.Feasible ? "✓" : "✗";
            Console.WriteLine(
                $"  {rec.Thrust_N / 1000.0,8:F1}  {rec.VoxelSize_mm,8:F2}   "
              + $"{rec.TileCount,3}    {rec.ResourceMode,-10}  {peakGB,6:F2}    {flag}");
        }

        Console.WriteLine();
        Console.WriteLine("# Apply with:  dotnet run --project Voxelforge.Benchmarks -- \\");
        Console.WriteLine("#     --autonomous --propellant LOX_CH4 --thrust <N> --pc 7e6 --eps 15 \\");
        Console.WriteLine("#     --voxel <mm> (per table above), then use UI's TileLargeBuilds.");
        return 0;
    }

    /// <summary>
    /// Meganewton sweep — for each envelope preset at the user's
    /// budget, runs <see cref="RegenChamberOptimization.GenerateWith"/>
    /// in tiled mode + writes a baseline JSONL row with timings +
    /// triangle counts + memory profile. Intended to be run overnight
    /// on the current 96 GB workstation tier; produces
    /// `Benchmarks/baselines/mega-sweep-YYYYMMDD.jsonl` by default.
    ///
    /// NOTE: this method LAUNCHES real PicoGK voxel builds at each
    /// thrust class, which can take 10-60 minutes per sweep point —
    /// shorter on 96 GB hardware than the original 64 GB anchor.
    /// Start with <c>--probe-envelope</c> to preview what will run
    /// before committing a sweep.
    ///
    /// Usage:
    ///   dotnet run --project Voxelforge.Benchmarks -- \
    ///       --mega-sweep [--budget-gb 48] [--out mega-sweep.jsonl] \
    ///       [--min-thrust 10000] [--max-thrust 200000]
    /// </summary>
    private static int RunMegaSweep(string[] args)
    {
        double budgetGB   = 48.0;  // default to 96 GB current-workstation tier, Balanced mode
        string? outPath   = null;
        double minThrust  = MegaScaleEnvelope.MinThrust_N;
        double maxThrust  = MegaScaleEnvelope.MaxThrust_N;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--budget-gb":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--budget-gb missing value"); return 3; }
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out budgetGB))
                    {
                        Console.Error.WriteLine("--budget-gb must be a positive number.");
                        return 3;
                    }
                    break;
                case "--out":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--out missing value"); return 3; }
                    outPath = args[++i];
                    break;
                case "--min-thrust":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--min-thrust missing value"); return 3; }
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out minThrust))
                    {
                        Console.Error.WriteLine("--min-thrust must be a number in N."); return 3;
                    }
                    break;
                case "--max-thrust":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--max-thrust missing value"); return 3; }
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out maxThrust))
                    {
                        Console.Error.WriteLine("--max-thrust must be a number in N."); return 3;
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unknown arg '{args[i]}'.");
                    return 3;
            }
        }

        long budgetBytes = (long)(budgetGB * 1024 * 1024 * 1024);
        outPath ??= Path.Combine(AppContext.BaseDirectory, "baselines",
            $"mega-sweep-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        var sweep = MegaScaleEnvelope.BuildSweep(budgetBytes)
            .Where(p => p.Thrust_N >= minThrust && p.Thrust_N <= maxThrust)
            .ToArray();

        if (sweep.Length == 0)
        {
            Console.Error.WriteLine(
                $"No sweep points fit budget {budgetGB:F0} GB in thrust range "
              + $"[{minThrust}, {maxThrust}]. Probe envelope: --probe-envelope.");
            return 4;
        }

        Console.WriteLine($"# Meganewton sweep — {sweep.Length} points, budget {budgetGB:F0} GB, "
                        + $"thrust [{minThrust:F0}, {maxThrust:F0}] N");
        Console.WriteLine($"# Output: {outPath}");

        for (int i = 0; i < sweep.Length; i++)
        {
            var pt = sweep[i];
            Console.WriteLine();
            Console.WriteLine($"# [{i + 1}/{sweep.Length}] Thrust {pt.Thrust_N / 1000.0:F1} kN at "
                            + $"voxel {pt.VoxelSize_mm:F2} mm × {pt.TileCount} tiles ({pt.ResourceMode})");

            try
            {
                // Use the AutoSeeder defaults so the sweep is representative.
                var seed = AutoSeeder.Seed(new EngineSpec(
                    PropellantPair:     PropellantPair.LOX_CH4,
                    Thrust_N:           pt.Thrust_N,
                    ChamberPressure_Pa: 7e6,
                    ExpansionRatio:     15.0));
                PropellantTables.UseEquilibrium = seed.UseEquilibriumRecommended;

                long t0 = Stopwatch.GetTimestamp();
                long peakBefore = Environment.WorkingSet;

                using var lib = new Library((float)pt.VoxelSize_mm);

                // Tiled build per envelope recommendation. Uses the same
                // ComposeChamberBuildOptions + PlanTiles + BuildTiled path
                // the UI and Benchmarks RunTiled already exercise.
                var opts = RegenChamberOptimization.ComposeChamberBuildOptions(
                    seed.Conditions, seed.Design);
                var plan = ChamberAxialTileBuilder.PlanTiles(
                    opts.Contour,
                    targetTileCount:            pt.TileCount,
                    injectorFlangeThickness_mm: opts.IncludeInjectorFlange
                                                ? opts.InjectorFlangeThickness_mm : 0.0,
                    mountFlangeThickness_mm:    opts.IncludeMountingFlange
                                                ? opts.MountingFlangeThickness_mm : 0.0,
                    gimbalAftExtension_mm:      0.0);

                string tileOut = Path.Combine(Path.GetTempPath(),
                    $"mega-{pt.Thrust_N:F0}N.stl");
                var summary = ChamberAxialTileBuilder.BuildTiled(opts, plan, tileOut);

                long t1 = Stopwatch.GetTimestamp();
                double totalSec = (t1 - t0) / (double)Stopwatch.Frequency;
                long peakAfter  = Environment.WorkingSet;
                long peakDelta  = Math.Max(0, peakAfter - peakBefore);

                // Emit JSONL row.
                var c = CultureInfo.InvariantCulture;
                using var sink = new StreamWriter(outPath, append: true);
                sink.WriteLine(
                    "{"
                  + $"\"timestamp\":\"{DateTime.UtcNow:O}\","
                  + $"\"thrust_n\":{pt.Thrust_N.ToString("F0", c)},"
                  + $"\"voxel_mm\":{pt.VoxelSize_mm.ToString("F3", c)},"
                  + $"\"tile_count\":{summary.Plan.Count},"
                  + $"\"total_ms\":{(totalSec * 1000.0).ToString("F0", c)},"
                  + $"\"per_tile_build_ms\":{summary.PerTileBuild_ms.ToString("F0", c)},"
                  + $"\"weld_ms\":{summary.Weld_ms.ToString("F0", c)},"
                  + $"\"peak_working_set_bytes\":{peakAfter},"
                  + $"\"working_set_delta_bytes\":{peakDelta},"
                  + $"\"output_triangles\":{summary.WeldResult.OutputTriangleCount},"
                  + $"\"output_bytes\":{summary.WeldResult.OutputBytes}"
                  + "}");

                Console.WriteLine($"#   done: {totalSec:F1} s, {summary.WeldResult.OutputTriangleCount:N0} tris, "
                                + $"peak WS {peakAfter / (1024.0 * 1024 * 1024):F2} GB");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"#   FAILED: {ex.Message}");
                using var sink = new StreamWriter(outPath, append: true);
                sink.WriteLine(
                    "{"
                  + $"\"timestamp\":\"{DateTime.UtcNow:O}\","
                  + $"\"thrust_n\":{pt.Thrust_N:F0},"
                  + $"\"failed\":true,"
                  + $"\"error\":\"{ex.Message.Replace("\"", "\\\"").Replace("\n", " ")}\""
                  + "}");
                // Continue with remaining sweep points — a 200 kN OOM
                // doesn't prevent the 500 kN point from being captured.
            }
        }

        Console.WriteLine();
        Console.WriteLine($"# Sweep complete. Baseline written to {outPath}.");
        return 0;
    }
}
