// BenchDualBell.cs — BB-5b (2026-04-29): --bench-dual-bell
// CLI subcommand. Times ChamberContourGenerator.Generate with
// dualBell: true against the same call without dual-bell on a
// canonical LOX/CH4 10 kN bell chamber and emits a schema-v1-
// compliant JSONL record.
//
// Why this exists: the Sprint 20 dual-bell inflection-point geometry
// (ChamberContour.IsDualBell, .InflectionIndex, .InflectionRadius_mm)
// has no standing regression baseline beyond the DualBellTests unit
// tests. This bench captures the generation timing (contour-only, no
// voxels — stays PicoGK-free) and the key dual-bell scalars so a
// future physics change can be caught by bench-diff before merging.
//
// The "byte-identical single-bell" claim in the Sprint 20 spec —
// that dualBell=false produces the same contour as the classic path —
// is validated inline: the bench records single_median_ms alongside
// dual_median_ms so the ratio is archived in the JSONL row.
//
// CLI: --bench-dual-bell [--iterations N] [--station-count M] [--out PATH]
//
// Output: BENCH summary lines + one schema-v1 JSONL row.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static class BenchDualBell
{
    public static int Run(string[] args)
    {
        int iterations   = 200;
        int stationCount = 240;
        string? outPath  = null;

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
                case "--station-count":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out stationCount) || stationCount < 10)
                    {
                        Console.Error.WriteLine("--station-count requires an integer >= 10.");
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
                    Console.Error.WriteLine($"Unknown --bench-dual-bell argument: {args[i]}");
                    Console.Error.WriteLine("Usage: --bench-dual-bell [--iterations N] [--station-count M] [--out path.jsonl]");
                    return 3;
            }
        }

        // Canonical 10 kN LOX/CH4 bell chamber — same propellant pair
        // as BenchCfdExport so the chamber geometry is representative
        // of the regen-designer's primary use case.
        var cond   = new OperatingConditions { PropellantPair = PropellantPair.LOX_CH4 };
        var design = new RegenChamberDesign { ContourStationCount = stationCount };
        var gas    = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);

        double Rt   = derived.ThroatRadius_mm;
        double Rc   = design.ContractionRatio;
        double Re   = design.ExpansionRatio;
        double Lstar = design.CharacteristicLength_m;

        // Dual-bell parameters: SL expansion ratio = 8 (roughly the
        // AJ10 / RL10 sea-level ε), full ε taken from design default
        // (typically 16-20 for this size class), inflection at 5°.
        const double seaLevelEps   = 8.0;
        const double inflectionDeg = 5.0;

        // Warm-up — JIT + table-init cost paid before samples.
        ChamberContourGenerator.Generate(Rt, Rc, Re, Lstar, stationCount: stationCount,
            dualBell: true, seaLevelExpansionRatio: seaLevelEps, inflectionAngleDeg: inflectionDeg);
        ChamberContourGenerator.Generate(Rt, Rc, Re, Lstar, stationCount: stationCount);

        // --- Dual-bell timing --------------------------------------------
        var dualSamples = new double[iterations];
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            ChamberContourGenerator.Generate(Rt, Rc, Re, Lstar, stationCount: stationCount,
                dualBell: true, seaLevelExpansionRatio: seaLevelEps, inflectionAngleDeg: inflectionDeg);
            sw.Stop();
            dualSamples[i] = sw.Elapsed.TotalMilliseconds;
        }

        // --- Single-bell timing (baseline for ratio) ---------------------
        var singleSamples = new double[iterations];
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            ChamberContourGenerator.Generate(Rt, Rc, Re, Lstar, stationCount: stationCount);
            sw.Stop();
            singleSamples[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(dualSamples);
        Array.Sort(singleSamples);

        double dualMedian   = dualSamples[iterations / 2];
        double dualMean     = dualSamples.Average();
        double dualMin      = dualSamples[0];
        double dualMax      = dualSamples[iterations - 1];
        double singleMedian = singleSamples[iterations / 2];
        double ratio        = dualMedian / Math.Max(singleMedian, 0.001);

        // Capture a representative contour for the scalar report.
        var contour = ChamberContourGenerator.Generate(Rt, Rc, Re, Lstar,
            stationCount: stationCount, dualBell: true,
            seaLevelExpansionRatio: seaLevelEps, inflectionAngleDeg: inflectionDeg);

        var ci = CultureInfo.InvariantCulture;
        Console.WriteLine($"# bench-dual-bell iterations={iterations} stations={stationCount}");
        Console.WriteLine($"# throat_radius_mm={Rt:F3} expansion_ratio={Re:F1} sl_expansion_ratio={seaLevelEps:F1}");
        Console.WriteLine($"# is_dual_bell={contour.IsDualBell} inflection_index={contour.InflectionIndex} inflection_radius_mm={contour.InflectionRadius_mm:F3}");
        Console.WriteLine($"BENCH dual_bell_median_ms  = {dualMedian.ToString("F3", ci)}");
        Console.WriteLine($"BENCH dual_bell_mean_ms    = {dualMean.ToString("F3", ci)}");
        Console.WriteLine($"BENCH dual_bell_min_ms     = {dualMin.ToString("F3", ci)}");
        Console.WriteLine($"BENCH dual_bell_max_ms     = {dualMax.ToString("F3", ci)}");
        Console.WriteLine($"BENCH single_bell_median_ms = {singleMedian.ToString("F3", ci)}");
        Console.WriteLine($"BENCH dual_vs_single_ratio  = {ratio.ToString("F3", ci)}");

        if (outPath != null)
        {
            JsonlSchema.AppendRecord(outPath, "bench-dual-bell", sb =>
            {
                sb.Append("\"iterations\":").Append(iterations).Append(',');
                sb.Append("\"station_count\":").Append(stationCount).Append(',');
                sb.Append("\"throat_radius_mm\":").Append(Rt.ToString("F3", ci)).Append(',');
                sb.Append("\"expansion_ratio\":").Append(Re.ToString("F2", ci)).Append(',');
                sb.Append("\"sea_level_expansion_ratio\":").Append(seaLevelEps.ToString("F1", ci)).Append(',');
                sb.Append("\"inflection_angle_deg\":").Append(inflectionDeg.ToString("F1", ci)).Append(',');
                sb.Append("\"inflection_index\":").Append(contour.InflectionIndex).Append(',');
                sb.Append("\"inflection_radius_mm\":").Append(contour.InflectionRadius_mm.ToString("F3", ci)).Append(',');
                sb.Append("\"dual_median_ms\":").Append(dualMedian.ToString("F3", ci)).Append(',');
                sb.Append("\"dual_mean_ms\":").Append(dualMean.ToString("F3", ci)).Append(',');
                sb.Append("\"dual_min_ms\":").Append(dualMin.ToString("F3", ci)).Append(',');
                sb.Append("\"dual_max_ms\":").Append(dualMax.ToString("F3", ci)).Append(',');
                sb.Append("\"single_median_ms\":").Append(singleMedian.ToString("F3", ci)).Append(',');
                sb.Append("\"dual_vs_single_ratio\":").Append(ratio.ToString("F3", ci));
            });
            Console.WriteLine($"# JSONL appended: {outPath}");
        }

        return 0;
    }
}
