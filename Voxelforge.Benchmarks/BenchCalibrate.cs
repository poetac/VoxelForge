// BenchCalibrate.cs — OOB-1 Sprint 2 (extended): hot-fire CSV → MAP calibration CLI.
//
// Reads a hot-fire CSV (the same format written by MeasuredDataOverlay),
// builds a headless physics runner from a CanonicalDesigns preset, and
// runs CalibrationPosterior.Calibrate to back-solve the five calibration
// knobs {CStarEfficiency, NozzleCfEfficiency, BartzScalingFactor,
//         CoolantHtcScalingFactor, CoolantFrictionScalingFactor}.
//
// CLI:
//   --calibrate <csv-file>
//              [--preset <merlin|rl10|pressure-fed-small|aerospike|pintle>]
//              [--out <result.json>]
//              [--write-back <saved-design.json>]
//              [--verbose]
//
// --write-back: load a saved design JSON (DesignPersistence format), update
//   the five calibration knobs on its OperatingConditions in-place, and
//   save it back. Allows the calibrated values to persist across sessions.
//
// Output: human-readable summary on stdout; optionally full JSON on --out.
//
// The runner calls GenerateWith(..., skipVoxelGeometry: true, skipMfgAnalysis: true)
// — the headless physics path (no PicoGK, ~20-80 ms per call).  With
// maxOuterIterations=4 and 30 golden-section evals per axis the ceiling is
// ~600 runner calls → calibration completes in < 60 s for any preset.

using System;
using System.IO;
using System.Text.Json;
using Voxelforge.Analysis;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static class BenchCalibrate
{
    // Cache the JsonSerializerOptions instance to satisfy CA1869.
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = null,
    };

    public static int Run(string[] args)
    {
        string? csvPath       = null;
        string  preset        = "merlin";
        string? outPath       = null;
        string? writeBackPath = null;
        bool    verbose       = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--preset":      preset        = args[++i]; break;
                case "--out":         outPath       = args[++i]; break;
                case "--write-back":  writeBackPath = args[++i]; break;
                case "--verbose":     verbose       = true;       break;
                default:
                    if (csvPath is null && !args[i].StartsWith("--", StringComparison.Ordinal))
                        csvPath = args[i];
                    else
                    {
                        Console.Error.WriteLine($"[calibrate] Unknown argument: {args[i]}");
                        PrintUsage();
                        return 1;
                    }
                    break;
            }
        }

        if (csvPath is null)
        {
            Console.Error.WriteLine("[calibrate] Error: no CSV file specified.");
            PrintUsage();
            return 1;
        }

        if (!File.Exists(csvPath))
        {
            Console.Error.WriteLine($"[calibrate] Error: CSV file not found: {csvPath}");
            return 1;
        }

        // ── Load preset ───────────────────────────────────────────────────────
        CanonicalDesigns.Preset p;
        try
        {
            p = CanonicalDesigns.Get(preset);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"[calibrate] Error: {ex.Message}");
            return 1;
        }

        var cond       = p.Seed.Conditions;
        var seedDesign = p.Seed.Design;

        // ── Parse + summarise CSV ─────────────────────────────────────────────
        var (samples, csvWarnings) = MeasuredDataOverlay.ParseCsv(csvPath);
        foreach (var w in csvWarnings)
            Console.Error.WriteLine($"[calibrate] CSV warning: {w}");

        if (samples.Count == 0)
        {
            Console.Error.WriteLine("[calibrate] Error: CSV produced zero valid samples.");
            return 1;
        }

        var measured = MeasuredDataOverlay.Summarise(samples);

        Console.Error.WriteLine(
            $"[calibrate] {samples.Count} samples from '{Path.GetFileName(csvPath)}'. "
          + $"Preset: {preset}.");
        Console.Error.WriteLine(
            $"[calibrate] Measured summary — "
          + $"TotalMassFlow: {Fmt(measured.TotalMassFlow_kgs, "kg/s")}  "
          + $"WallT: {Fmt(measured.WallT_K, "K")}  "
          + $"CoolantDT: {Fmt(measured.CoolantDT_K, "K")}  "
          + $"CoolantDP: {Fmt(measured.CoolantDP_Pa / 1e6, "MPa")}");

        // ── Build headless physics runner ─────────────────────────────────────
        // All five calibration knobs live on OperatingConditions (hardware-
        // specific calibration, not geometry). Each call overrides them on a
        // with-expression clone and runs the headless thermal solve.
        // skipVoxelGeometry + skipMfgAnalysis = true keeps each call in the
        // ~20-80 ms physics-only path.
        CalibrationObservables Runner(double cstar, double cf, double bartz,
                                      double htcSF, double frictionSF)
        {
            var c = cond with
            {
                CStarEfficiency              = cstar,
                NozzleCfEfficiency           = cf,
                BartzScalingFactor           = bartz,
                CoolantHtcScalingFactor      = htcSF,
                CoolantFrictionScalingFactor = frictionSF,
            };

            var r = RegenChamberOptimization.GenerateWith(
                c, seedDesign,
                skipVoxelGeometry: true,
                skipMfgAnalysis:   true);

            // If no thermal march ran (e.g. ChannelTopology.None), return all-NaN
            // so the calibration treats this preset as thermally unobservable.
            if (r.Thermal.Stations.Length == 0)
                return new CalibrationObservables(
                    TotalMassFlow_kgs: r.Derived.TotalMassFlow_kgs,
                    PeakWallT_K:       double.NaN,
                    CoolantDT_K:       double.NaN,
                    CoolantDP_Pa:      double.NaN);

            double dT = r.Thermal.CoolantOutletT_K - r.Thermal.CoolantInletT_K;
            return new CalibrationObservables(
                TotalMassFlow_kgs: r.Derived.TotalMassFlow_kgs,
                PeakWallT_K:       r.Thermal.PeakGasSideWallT_K,
                CoolantDT_K:       dT > 0 ? dT : double.NaN,
                CoolantDP_Pa:      r.Thermal.CoolantPressureDrop_Pa > 0
                                       ? r.Thermal.CoolantPressureDrop_Pa
                                       : double.NaN);
        }

        // ── Run calibration ───────────────────────────────────────────────────
        Console.Error.WriteLine("[calibrate] Starting coordinate-descent MAP calibration…");
        var result = CalibrationPosterior.Calibrate(measured, Runner, verbose: verbose);

        // ── Print human-readable summary ──────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("=== Calibration result ===");
        Console.WriteLine($"  Preset        : {preset}  ({p.Description[..Math.Min(60, p.Description.Length)]}…)");
        Console.WriteLine($"  CSV samples   : {samples.Count}");
        Console.WriteLine($"  SSR at prior  : {result.SsrAtPrior:G4}");
        Console.WriteLine($"  SSR at MAP    : {result.SsrAtMap:G4}");
        Console.WriteLine($"  Iterations    : {result.IterationsUsed}");
        Console.WriteLine();
        Console.WriteLine($"  Knob                  MAP      Prior mean ± σ       Curvature");
        Console.WriteLine($"  ─────────────────────────────────────────────────────────────");
        PrintKnob(result.CStarEfficiency);
        PrintKnob(result.NozzleCfEfficiency);
        PrintKnob(result.BartzScalingFactor);
        PrintKnob(result.CoolantHtcScalingFactor);
        PrintKnob(result.CoolantFrictionScalingFactor);
        Console.WriteLine();
        Console.WriteLine("  Notes:");
        foreach (var note in result.Notes)
            Console.WriteLine($"    • {note}");
        Console.WriteLine();

        // ── Optionally write JSON result ──────────────────────────────────────
        if (outPath is not null)
        {
            var json = JsonSerializer.Serialize(result, s_jsonOptions);
            File.WriteAllText(outPath, json);
            Console.WriteLine($"  Full JSON result written to: {outPath}");
        }

        // ── Optionally write calibrated knobs back into a saved design ────────
        if (writeBackPath is not null)
        {
            if (!File.Exists(writeBackPath))
            {
                Console.Error.WriteLine(
                    $"[calibrate] Error: --write-back file not found: {writeBackPath}");
                return 1;
            }

            var saved = Voxelforge.IO.DesignPersistence.Load(writeBackPath);
            if (saved?.Conditions is null || saved.Design is null)
            {
                Console.Error.WriteLine(
                    $"[calibrate] Error: could not load valid OperatingConditions + "
                  + $"RegenChamberDesign from '{writeBackPath}'.");
                return 1;
            }

            var updatedCond = saved.Conditions with
            {
                CStarEfficiency              = result.CStarEfficiency.MapValue,
                NozzleCfEfficiency           = result.NozzleCfEfficiency.MapValue,
                BartzScalingFactor           = result.BartzScalingFactor.MapValue,
                CoolantHtcScalingFactor      = result.CoolantHtcScalingFactor.MapValue,
                CoolantFrictionScalingFactor = result.CoolantFrictionScalingFactor.MapValue,
            };
            Voxelforge.IO.DesignPersistence.Save(
                writeBackPath, updatedCond, saved.Design, r: null);
            Console.WriteLine();
            Console.WriteLine($"  Calibrated knobs written back to: {writeBackPath}");
            Console.WriteLine($"    CStarEfficiency              {saved.Conditions.CStarEfficiency:F4}  →  {updatedCond.CStarEfficiency:F4}");
            Console.WriteLine($"    NozzleCfEfficiency           {saved.Conditions.NozzleCfEfficiency:F4}  →  {updatedCond.NozzleCfEfficiency:F4}");
            Console.WriteLine($"    BartzScalingFactor           {saved.Conditions.BartzScalingFactor:F4}  →  {updatedCond.BartzScalingFactor:F4}");
            Console.WriteLine($"    CoolantHtcScalingFactor      {saved.Conditions.CoolantHtcScalingFactor:F4}  →  {updatedCond.CoolantHtcScalingFactor:F4}");
            Console.WriteLine($"    CoolantFrictionScalingFactor {saved.Conditions.CoolantFrictionScalingFactor:F4}  →  {updatedCond.CoolantFrictionScalingFactor:F4}");
        }
        else if (outPath is null)
        {
            // Neither --out nor --write-back: suggest how to persist the result.
            Console.WriteLine();
            Console.WriteLine("  To persist calibrated values:");
            Console.WriteLine($"    --out <result.json>             save full calibration report");
            Console.WriteLine($"    --write-back <saved-design.3df> update knobs in a saved design");
        }

        Console.WriteLine();
        return 0;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage: --calibrate <csv-file> [--preset <name>] [--out <json>] [--write-back <design>] [--verbose]");
        Console.Error.WriteLine(
            $"  Available presets: {string.Join(", ", CanonicalDesigns.AllNames)}");
        Console.Error.WriteLine(
            "  CSV must contain headers matching MeasuredDataOverlay.ParseCsv format.");
        Console.Error.WriteLine(
            "  Columns that enable calibration:");
        Console.Error.WriteLine(
            "    total_mass_flow_kgs        → CStarEfficiency + NozzleCfEfficiency");
        Console.Error.WriteLine(
            "    wall_t_k                   → BartzScalingFactor (direct)");
        Console.Error.WriteLine(
            "    coolant_t_out_k/in_k / ΔT → BartzScalingFactor + CoolantHtcScalingFactor");
        Console.Error.WriteLine(
            "    coolant_dp_pa              → CoolantFrictionScalingFactor");
    }

    private static void PrintKnob(KnobEstimate k)
    {
        Console.WriteLine(
            $"  {k.Name,-22} {k.MapValue:F4}   "
          + $"{k.PriorMean:F4} ± {k.PriorSigma:F4}   {k.SsrCurvature:F2}");
    }

    private static string Fmt(double v, string unit)
        => double.IsNaN(v) ? "n/a" : $"{v:F4} {unit}";
}
