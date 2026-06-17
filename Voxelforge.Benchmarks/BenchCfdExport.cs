// BenchCfdExport.cs — BB-3 (2026-04-29): restored `--bench-cfd-export`
// CLI subcommand. Iterates `CfdFieldExport.Write` against a canonical
// LOX/CH4 bell chamber + 80-station thermal solve and emits a
// schema-v1-compliant JSONL record with median/mean/min/max wall-ms
// and a single representative file_bytes value.
//
// Why this exists: pre-Sprint-30 a `bench-cfd-export.jsonl` was
// committed to baselines/ but the CLI flag generating it never
// landed on `main` (see baselines/README.md "phantom" rows). BB-3's
// charter is to make that phantom regenerable. The legacy phantom
// captured `iterations: 50, grid_nx: 96, file_bytes: 21234617,
// median_ms: 15.38` on the original sha (2026-04-23); we re-emit the
// same payload shape augmented with the schema-v1 provenance prefix.
//
// CLI:  --bench-cfd-export [--iterations 50] [--grid-nx 96] [--out PATH]
//
// Output: one JSONL row, plus a stdout BENCH summary block.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.IO;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static class BenchCfdExport
{
    public static int Run(string[] args)
    {
        int iterations = 50;
        int gridNx = 96;
        string? outPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--iterations":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out iterations) || iterations < 1)
                    {
                        Console.Error.WriteLine("--iterations requires a positive integer.");
                        return 3;
                    }
                    break;
                case "--grid-nx":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out gridNx) || gridNx < 16)
                    {
                        Console.Error.WriteLine("--grid-nx requires an integer >= 16.");
                        return 3;
                    }
                    break;
                case "--out":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--out requires a path argument.");
                        return 3;
                    }
                    outPath = args[++i];
                    break;
                default:
                    Console.Error.WriteLine($"Unknown --bench-cfd-export argument: {args[i]}");
                    Console.Error.WriteLine("Usage: --bench-cfd-export [--iterations N] [--grid-nx M] [--out path.jsonl]");
                    return 3;
            }
        }

        // Build solver inputs identical to Phase4PerfBenchmarks.MakeSolverInputs(80)
        // so the bench timing is comparable to the in-process xUnit guard.
        var (contour, channels, solver) = MakeFields(stationCount: 80);

        var grid = new CfdFieldGrid(
            Nx: gridNx, Ny: gridNx, Nz: gridNx,
            TransverseHalfWidth_mm: 1.10 * Math.Max(contour.ChamberRadius_mm, contour.ExitRadius_mm));

        string tempDir = Path.Combine(Path.GetTempPath(), "regen-benchmarks", "cfd-export");
        Directory.CreateDirectory(tempDir);

        // Warm-up — first call pays JIT + table-lookup cost.
        string warmPath = Path.Combine(tempDir, "warmup.vti");
        var warmStats = CfdFieldExport.Write(warmPath, contour, channels, solver,
            outerJacketThickness_mm: 2.5, grid: grid);
        TryDelete(warmPath);

        var samples = new double[iterations];
        long totalBytes = warmStats.FileBytes;
        long lastFileBytes = warmStats.FileBytes;
        for (int i = 0; i < iterations; i++)
        {
            string vtiPath = Path.Combine(tempDir, $"iter_{i:D04}.vti");
            var sw = Stopwatch.StartNew();
            var stats = CfdFieldExport.Write(vtiPath, contour, channels, solver,
                outerJacketThickness_mm: 2.5, grid: grid);
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
            totalBytes += stats.FileBytes;
            lastFileBytes = stats.FileBytes;
            TryDelete(vtiPath);
        }

        Array.Sort(samples);
        double median = samples[iterations / 2];
        double mean = samples.Average();
        double min = samples[0];
        double max = samples[iterations - 1];
        double medianMbps = lastFileBytes / 1_000_000.0 / (median / 1000.0);

        var ci = CultureInfo.InvariantCulture;
        Console.WriteLine($"# bench-cfd-export iterations={iterations} grid={gridNx}^3");
        Console.WriteLine($"# file_bytes={lastFileBytes:N0} (~{lastFileBytes / 1024.0 / 1024.0:F1} MB)");
        Console.WriteLine($"BENCH cfd_export_median_ms = {median.ToString("F2", ci)}");
        Console.WriteLine($"BENCH cfd_export_mean_ms   = {mean.ToString("F2", ci)}");
        Console.WriteLine($"BENCH cfd_export_min_ms    = {min.ToString("F2", ci)}");
        Console.WriteLine($"BENCH cfd_export_max_ms    = {max.ToString("F2", ci)}");
        Console.WriteLine($"BENCH cfd_export_median_mbps = {medianMbps.ToString("F2", ci)}");

        if (outPath != null)
        {
            JsonlSchema.AppendRecord(outPath, "bench-cfd-export", sb =>
            {
                sb.Append("\"iterations\":").Append(iterations).Append(',');
                sb.Append("\"grid_nx\":").Append(gridNx).Append(',');
                sb.Append("\"file_bytes\":").Append(lastFileBytes.ToString(ci)).Append(',');
                sb.Append("\"median_ms\":").Append(median.ToString("F2", ci)).Append(',');
                sb.Append("\"mean_ms\":").Append(mean.ToString("F2", ci)).Append(',');
                sb.Append("\"min_ms\":").Append(min.ToString("F2", ci)).Append(',');
                sb.Append("\"max_ms\":").Append(max.ToString("F2", ci)).Append(',');
                sb.Append("\"median_mbps\":").Append(medianMbps.ToString("F2", ci));
            });
            Console.WriteLine($"# JSONL appended: {outPath}");
        }

        // Best-effort temp cleanup. Don't fail the bench on a stale lock.
        try { Directory.Delete(tempDir, recursive: true); } catch { }
        return 0;
    }

    // Mirrors Phase4PerfBenchmarks.MakeSolverInputs(80) but only returns
    // the fields CfdFieldExport.Write actually needs (contour, channels,
    // solver outputs).
    private static (ChamberContour contour, ChannelSchedule channels, RegenSolverOutputs solver)
        MakeFields(int stationCount)
    {
        var cond = new OperatingConditions { PropellantPair = PropellantPair.LOX_CH4 };
        var design = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = stationCount,
        };
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm:        derived.ThroatRadius_mm,
            contractionRatio:       design.ContractionRatio,
            expansionRatio:         design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            stationCount:           stationCount);
        var material = WallMaterials.All[
            Math.Clamp(cond.WallMaterialIndex, 0, WallMaterials.All.Length - 1)];
        var pairMeta = PropellantPairs.GetMeta(cond.PropellantPair);
        var fluid = CoolantRegistry.Get(pairMeta.CoolantFluidKey);
        var channels = new ChannelSchedule(
            ChannelCount: design.ChannelCount,
            RibThickness_mm: design.RibThickness_mm,
            GasSideWallThickness_mm: design.GasSideWallThickness_mm,
            ChannelHeightAtChamber_mm: design.ChannelHeightChamber_mm,
            ChannelHeightAtThroat_mm: design.ChannelHeightThroat_mm,
            ChannelHeightAtExit_mm: design.ChannelHeightExit_mm);
        var inputs = new RegenSolverInputs(
            Contour: contour, Gas: gas, Wall: material, Channels: channels,
            CoolantMassFlow_kgs: derived.FuelMassFlow_kgs,
            CoolantInletTemp_K: cond.CoolantInletTemp_K,
            CoolantInletPressure_Pa: cond.CoolantInletPressure_Pa,
            CoolantFluid: fluid);
        var solver = RegenCoolingSolver.Solve(inputs);
        return (contour, channels, solver);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
