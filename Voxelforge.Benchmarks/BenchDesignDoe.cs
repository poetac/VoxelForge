// BenchDesignDoe.cs — OOB-10 DOE campaign designer.
//
// Sobol-samples the SA design space, pre-screens for feasibility, runs
// the headless physics oracle, and writes a CSV of predicted observables
// that the test engineer uses to choose what to build and fire.
//
// CLI:
//   --design-doe [--preset <name>] [--n <count>] [--out <path.csv>]
//
// Default preset: merlin   Default n: 10
// Columns: design_id, predicted_pc_mpa, predicted_massflow_kgs, predicted_peak_twg_k
//          (design_json is last for readability; contains full OperatingConditions + Design)
//
// The search cap is 50×n Sobol candidates, so n=10 caps at 500 physics evals.
// If the feasible count is below n, the tool exits 0 and emits what it found.
// `BENCH doe_feasible_rows=N` is always the last stdout line for test parsing.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Voxelforge.Analysis;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static class BenchDesignDoe
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = false };

    public static int Run(string[] args)
    {
        string preset = "merlin";
        int    n      = 10;
        string? outPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--preset": preset  = args[++i]; break;
                case "--n":
                    if (!int.TryParse(args[++i], out n) || n < 1)
                    {
                        Console.Error.WriteLine("[design-doe] Error: --n must be a positive integer.");
                        return 3;
                    }
                    break;
                case "--out": outPath = args[++i]; break;
                default:
                    Console.Error.WriteLine($"[design-doe] Unknown argument: {args[i]}");
                    return 3;
            }
        }

        // ── Load preset ───────────────────────────────────────────────────────
        CanonicalDesigns.Preset p;
        try { p = CanonicalDesigns.Get(preset); }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"[design-doe] Error: {ex.Message}");
            return 4;
        }

        var cond       = p.Seed.Conditions;
        var seedDesign = p.Seed.Design;

        // ── Open output ───────────────────────────────────────────────────────
        var csvBuf = new StringBuilder();
        csvBuf.AppendLine(
            "design_id,predicted_pc_mpa,predicted_massflow_kgs,predicted_peak_twg_k,design_json");

        // ── Sobol DOE loop ────────────────────────────────────────────────────
        var   bounds   = RegenChamberOptimization.Bounds;
        var   sobol    = new SobolSequence(bounds.Length);
        sobol.SkipTo(1);                       // skip the all-zeros origin

        int feasibleCount = 0;
        int maxAttempts   = n * 50;

        Console.Error.WriteLine(
            $"[design-doe] Preset: {preset}  n={n}  max_attempts={maxAttempts}");

        for (int attempt = 0; attempt < maxAttempts && feasibleCount < n; attempt++)
        {
            double[] normalized = sobol.Next();

            // Map [0,1)^D → raw design vector in physical bounds
            var raw = new double[bounds.Length];
            for (int d = 0; d < bounds.Length; d++)
                raw[d] = bounds[d].Min + normalized[d] * (bounds[d].Max - bounds[d].Min);

            var design = RegenChamberOptimization.Unpack(raw, seedDesign);

            // Fast pre-screen before the thermal solve
            if (FeasibilityGate.PreScreen(cond, design) is not null)
                continue;

            RegenGenerationResult r;
            try
            {
                r = RegenChamberOptimization.GenerateWith(
                    cond, design,
                    skipVoxelGeometry: true,
                    skipMfgAnalysis:   true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[design-doe] GenerateWith exception (attempt {attempt}): {ex.Message}");
                continue;
            }

            // Drop designs that produced an infeasible generation result
            if (!FeasibilityGate.Evaluate(r).IsFeasible)
                continue;

            feasibleCount++;
            double pcMpa  = r.Conditions.ChamberPressure_Pa / 1e6;
            double mDot   = r.Derived.TotalMassFlow_kgs;
            double peakTw = r.Thermal.Stations.Length > 0
                ? r.Thermal.PeakGasSideWallT_K
                : double.NaN;

            // Embed the design as compact JSON (OperatingConditions serialised
            // implicitly via DesignPersistence's JSON conventions).
            string designJson = JsonSerializer.Serialize(
                new { Conditions = cond, Design = design }, s_jsonOptions)
                .Replace(",", ";");  // keep CSV columns intact (no commas inside)

            csvBuf.AppendLine(string.Join(",",
                feasibleCount.ToString(Inv),
                pcMpa .ToString("F4", Inv),
                mDot  .ToString("F6", Inv),
                double.IsNaN(peakTw)
                    ? ""
                    : peakTw.ToString("F1", Inv),
                $"\"{designJson}\""));
        }

        // ── Write CSV ─────────────────────────────────────────────────────────
        string csv = csvBuf.ToString();
        if (outPath is not null)
            File.WriteAllText(outPath, csv, Encoding.UTF8);
        else
            Console.Write(csv);

        Console.WriteLine($"BENCH doe_feasible_rows={feasibleCount}");
        Console.Error.WriteLine(
            $"[design-doe] Done. {feasibleCount} feasible rows (cap={n}).");
        if (outPath is not null)
            Console.Error.WriteLine($"[design-doe] CSV written to: {outPath}");

        return 0;
    }
}
