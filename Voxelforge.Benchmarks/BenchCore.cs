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
    /// <summary>
    /// Exercise the axial-tiled voxel build.
    /// Produces a plan + per-tile build + welded single-STL output.
    /// Emits BENCH lines + a human-readable summary so the user can
    /// compare peak memory + wall-clock against the monolithic path.
    /// </summary>
    private static int RunTiled(
        OperatingConditions cond, RegenChamberDesign design,
        BenchArgs           cli,  string             stlOutPath)
    {
        // Replicate the RegenChamberOptimization.GenerateWith internals
        // just enough to produce a ChamberBuildOptions we can hand to
        // the tile builder. Keep it minimal — no physics solves here,
        // we're benchmarking geometry only.
        // Shared helper — keep benchmark + form-side dispatch on
        // the same ChamberBuildOptions factory so any future field add
        // can't drift between them.
        var opts = RegenChamberOptimization.ComposeChamberBuildOptions(cond, design);

        var plan = ChamberAxialTileBuilder.PlanTiles(
            opts.Contour,
            targetTileCount:            cli.Tiles,
            injectorFlangeThickness_mm: opts.IncludeInjectorFlange ? opts.InjectorFlangeThickness_mm : 0.0,
            mountFlangeThickness_mm:    opts.IncludeMountingFlange  ? opts.MountingFlangeThickness_mm : 0.0,
            gimbalAftExtension_mm:      0.0);

        Console.WriteLine($"# Plan: {plan.Rationale}");
        Console.WriteLine($"# Covering [{plan.ChamberXMin_mm:F1}, {plan.ChamberXMax_mm:F1}] mm → {plan.Count} tile(s).");

        long t0 = Stopwatch.GetTimestamp();
        var summary = ChamberAxialTileBuilder.BuildTiled(opts, plan, stlOutPath);
        long t1 = Stopwatch.GetTimestamp();
        double totalMs = (t1 - t0) / (double)Stopwatch.Frequency * 1000.0;

        Console.WriteLine("# ── Per-tile breakdown ────────────────────────");
        for (int i = 0; i < summary.TileResults.Count; i++)
        {
            var r = summary.TileResults[i];
            Console.WriteLine(
                $"#   tile {i}: x∈[{r.Tile.XMin_mm:F1}, {r.Tile.XMax_mm:F1}] mm, "
              + $"{r.BuildWallMs:F1} ms, "
              + $"{r.SubtractedChannelCount} channels, "
              + $"inletMan={r.IncludedInletManifold}, outletMan={r.IncludedOutletManifold}, "
              + $"inletPort={r.IncludedInletPort}, outletPort={r.IncludedOutletPort}, "
              + $"injFlange={r.IncludedInjectorFlange}, mountFlange={r.IncludedMountingFlange}");
        }

        Console.WriteLine("# ── Tiled BENCH ───────────────────────────────");
        Console.WriteLine($"BENCH tiled_plan_count={summary.Plan.Count}");
        Console.WriteLine($"BENCH tiled_build_ms={summary.PerTileBuild_ms:F1}");
        Console.WriteLine($"BENCH tiled_weld_ms={summary.Weld_ms:F1}");
        Console.WriteLine($"BENCH tiled_total_ms={totalMs:F1}");
        Console.WriteLine($"BENCH tiled_input_triangles={summary.WeldResult.InputTriangleCount}");
        Console.WriteLine($"BENCH tiled_output_triangles={summary.WeldResult.OutputTriangleCount}");
        Console.WriteLine($"BENCH tiled_dropped_triangles={summary.WeldResult.DroppedTriangleCount}");
        Console.WriteLine($"BENCH tiled_output_bytes={summary.WeldResult.OutputBytes}");
        Console.WriteLine($"# Output STL: {summary.OutputStlPath}");
        return 0;
    }

    // BuildOptionsForBenchmark retired in favour of the shared
    // public helper RegenChamberOptimization.ComposeChamberBuildOptions
    // — same result from a single factory, so Program.cs dispatch and
    // this Benchmarks harness can't drift apart if a ChamberBuildOptions
    // field is added later.

    private static RunRecord RunOne(
        OperatingConditions cond, RegenChamberDesign design, float voxelMM, string? stlPath)
    {
        long t0 = Stopwatch.GetTimestamp();
        var gen = RegenChamberOptimization.GenerateWith(cond, design, voxelSize_mm: voxelMM);
        long t1 = Stopwatch.GetTimestamp();

        double meshingMs = 0, writeMs = 0;
        int triangles = 0;
        long stlBytes = 0;
        if (stlPath != null && gen.Geometry.Voxels != null)
        {
            var export = ChamberVoxelBuilder.ExportStlProfiled(gen.Geometry.Voxels.AsPicoGK(),stlPath);
            meshingMs = export.Meshing_ms;
            writeMs   = export.StlWrite_ms;
            triangles = export.TriangleCount;
            stlBytes  = export.StlBytes;
        }

        double generateWithMs = (t1 - t0) / (double)Stopwatch.Frequency * 1000.0;
        return new RunRecord(
            VoxelMM:         voxelMM,
            Profile:         gen.Geometry.Profile,
            GenerateWithMs:  generateWithMs,
            MeshingMs:       meshingMs,
            StlWriteMs:      writeMs,
            TriangleCount:   triangles,
            StlBytes:        stlBytes,
            Description:     gen.Geometry.Description);
    }

    private static void EmitMedianSummary(List<RunRecord> runs, TextWriter sink)
    {
        if (runs.Count == 0) return;
        double Median(Func<RunRecord, double> f)
        {
            var vs = runs.Select(f).OrderBy(v => v).ToArray();
            return vs.Length % 2 == 1 ? vs[vs.Length / 2] : 0.5 * (vs[vs.Length / 2 - 1] + vs[vs.Length / 2]);
        }
        int MedianInt(Func<RunRecord, int> f)
        {
            var vs = runs.Select(f).OrderBy(v => v).ToArray();
            return vs[vs.Length / 2];
        }
        sink.WriteLine($"BENCH_MEDIAN voxel_size_mm={runs[0].VoxelMM:F3}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_total_ms={Median(r => r.Profile?.Total_ms ?? 0):F1}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_shell_ms={Median(r => r.Profile?.Shell_ms ?? 0):F1}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_channels_ms={Median(r => r.Profile?.Channels_ms ?? 0):F1}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_channel_voxelise_ms={Median(r => r.Profile?.ChannelVoxelise_ms ?? 0):F1}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_channel_boolsub_ms={Median(r => r.Profile?.ChannelBoolSubtract_ms ?? 0):F1}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_channel_count={(runs[0].Profile?.ChannelCount ?? 0)}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_manifolds_ms={Median(r => r.Profile?.Manifolds_ms ?? 0):F1}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_radial_ports_ms={Median(r => r.Profile?.RadialPorts_ms ?? 0):F1}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_smoothen_ms={Median(r => r.Profile?.Smoothen_ms ?? 0):F1}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_injflange_ms={Median(r => r.Profile?.InjectorFlange_ms ?? 0):F1}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_mountflange_ms={Median(r => r.Profile?.MountingFlange_ms ?? 0):F1}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_injbores_ms={Median(r => r.Profile?.InjectorBores_ms ?? 0):F1}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_late_ms={Median(r => r.Profile?.LateFeatures_ms ?? 0):F1}");
        sink.WriteLine($"BENCH_MEDIAN grid_build_final_ms={Median(r => r.Profile?.FinalMeasurements_ms ?? 0):F1}");
        if (runs.Any(r => r.TriangleCount > 0))
        {
            sink.WriteLine($"BENCH_MEDIAN export_meshing_ms={Median(r => r.MeshingMs):F1}");
            sink.WriteLine($"BENCH_MEDIAN export_stl_write_ms={Median(r => r.StlWriteMs):F1}");
            sink.WriteLine($"BENCH_MEDIAN triangle_count={MedianInt(r => r.TriangleCount)}");
        }
        sink.WriteLine($"BENCH_MEDIAN dense_voxels={(runs[0].Profile?.DenseEquivalentVoxels ?? 0)}");
    }
}
