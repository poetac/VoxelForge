// BenchPostTest.cs — OOB-10 post-test calibration + Markdown report.
//
// Reads a hot-fire CSV, calibrates the five knobs (same as --calibrate),
// and emits a structured Markdown comparison report that maps predicted
// vs measured observables, knob shifts, and fit quality.
//
// CLI:
//   --post-test <measured.csv>
//              [--preset <name>]
//              [--out <report.md>]
//
// Default preset: merlin
// If --out is omitted the Markdown is written to stdout.
//
// The report contains:
//   - Status banner (CONVERGED / NOT CONVERGED)
//   - Calibrated Knobs table
//   - Predicted vs Measured at prior means
//   - Fit Quality metrics
//   - Notes

using System;
using System.IO;
using Voxelforge.Analysis;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static class BenchPostTest
{
    public static int Run(string[] args)
    {
        string? csvPath = null;
        string  preset  = "merlin";
        string? outPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--preset": preset  = args[++i]; break;
                case "--out":    outPath = args[++i]; break;
                default:
                    if (csvPath is null && !args[i].StartsWith("--", StringComparison.Ordinal))
                        csvPath = args[i];
                    else
                    {
                        Console.Error.WriteLine($"[post-test] Unknown argument: {args[i]}");
                        PrintUsage();
                        return 3;
                    }
                    break;
            }
        }

        if (csvPath is null)
        {
            Console.Error.WriteLine("[post-test] Error: no CSV file specified.");
            PrintUsage();
            return 3;
        }

        if (!File.Exists(csvPath))
        {
            Console.Error.WriteLine($"[post-test] Error: CSV file not found: {csvPath}");
            return 4;
        }

        // ── Load preset ───────────────────────────────────────────────────────
        CanonicalDesigns.Preset p;
        try { p = CanonicalDesigns.Get(preset); }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"[post-test] Error: {ex.Message}");
            return 3;
        }

        var cond       = p.Seed.Conditions;
        var seedDesign = p.Seed.Design;

        // ── Parse + summarise CSV ─────────────────────────────────────────────
        var (samples, csvWarnings) = MeasuredDataOverlay.ParseCsv(csvPath);
        foreach (var w in csvWarnings)
            Console.Error.WriteLine($"[post-test] CSV warning: {w}");

        if (samples.Count == 0)
        {
            Console.Error.WriteLine("[post-test] Error: CSV produced zero valid samples.");
            return 4;
        }

        var measured = MeasuredDataOverlay.Summarise(samples);
        Console.Error.WriteLine(
            $"[post-test] {samples.Count} samples from '{Path.GetFileName(csvPath)}'. "
          + $"Preset: {preset}.");

        // ── Build headless physics runner ─────────────────────────────────────
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

        // ── Evaluate prior-mean prediction ────────────────────────────────────
        // Run once at prior means so the report can show pre-calibration error.
        var priorPrediction = Runner(
            cond.CStarEfficiency,
            cond.NozzleCfEfficiency,
            cond.BartzScalingFactor,
            cond.CoolantHtcScalingFactor,
            cond.CoolantFrictionScalingFactor);

        // ── Run calibration ───────────────────────────────────────────────────
        Console.Error.WriteLine("[post-test] Running MAP calibration…");
        var cal = CalibrationPosterior.Calibrate(
            measured, Runner, maxOuterIterations: 4);

        // ── Build Markdown report ─────────────────────────────────────────────
        string report = DoePostTestReport.BuildMarkdown(
            measured, cal, priorPrediction, [..csvWarnings]);

        if (outPath is not null)
        {
            File.WriteAllText(outPath, report, System.Text.Encoding.UTF8);
            Console.Error.WriteLine($"[post-test] Report written to: {outPath}");
        }
        else
        {
            Console.Write(report);
        }

        return 0;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage: --post-test <measured.csv> [--preset <name>] [--out <report.md>]");
        Console.Error.WriteLine(
            $"  Available presets: {string.Join(", ", CanonicalDesigns.AllNames)}");
    }
}
