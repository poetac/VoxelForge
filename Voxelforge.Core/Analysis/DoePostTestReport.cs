// DoePostTestReport.cs — OOB-10 post-test Markdown comparison report.
//
// Builds a human-readable Markdown artifact summarising a hot-fire campaign:
//   1. Status banner (CONVERGED / NOT CONVERGED)
//   2. Calibrated Knobs table (Knob | Prior | MAP | Δ | Interpretation)
//   3. Predicted vs Measured (at prior means vs measured values)
//   4. Fit Quality (SSR improvement ratio)
//   5. Notes (calibration diagnostics + CSV parse warnings)
//
// The caller is responsible for computing `priorPrediction` by evaluating
// the physics runner at the five prior means before calling Calibrate.
// This keeps DoePostTestReport free of any Func<> or runner dependency.

using System;
using System.Globalization;
using System.Text;

namespace Voxelforge.Analysis;

public static class DoePostTestReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Builds a Markdown post-test comparison report.
    /// </summary>
    /// <param name="measured">Averaged observables from the hot-fire CSV.</param>
    /// <param name="cal">MAP calibration result from CalibrationPosterior.Calibrate.</param>
    /// <param name="priorPrediction">Physics prediction evaluated at the five prior means,
    ///   so the report can show pre-calibration model error.</param>
    /// <param name="parseWarnings">Warnings emitted by MeasuredDataOverlay.ParseCsv.</param>
    public static string BuildMarkdown(
        MeasuredSummary            measured,
        MultiKnobCalibrationResult cal,
        CalibrationObservables     priorPrediction,
        string[]                   parseWarnings)
    {
        double improvement = cal.SsrAtPrior > 0
            ? (cal.SsrAtPrior - cal.SsrAtMap) / cal.SsrAtPrior
            : 0.0;
        bool converged = improvement >= 0.05 || cal.SsrAtMap < 1e-4;

        var sb = new StringBuilder();

        sb.AppendLine($"# Post-Test Calibration Report");
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        // ── Status banner ─────────────────────────────────────────────────────
        string status = converged ? "**✓ CONVERGED**" : "**⚠ NOT CONVERGED**";
        sb.AppendLine($"## Status");
        sb.AppendLine($"> {status} — SSR improvement {improvement:P1} ({cal.SsrAtPrior:G4} → {cal.SsrAtMap:G4})");
        sb.AppendLine();

        // ── Calibrated knobs ──────────────────────────────────────────────────
        sb.AppendLine("## Calibrated Knobs");
        sb.AppendLine();
        sb.AppendLine("| Knob | Prior | MAP | Δ | Interpretation |");
        sb.AppendLine("|------|------:|----:|--:|----------------|");
        AppendKnobRow(sb, cal.CStarEfficiency);
        AppendKnobRow(sb, cal.NozzleCfEfficiency);
        AppendKnobRow(sb, cal.BartzScalingFactor);
        AppendKnobRow(sb, cal.CoolantHtcScalingFactor);
        AppendKnobRow(sb, cal.CoolantFrictionScalingFactor);
        sb.AppendLine();

        // ── Predicted vs measured ─────────────────────────────────────────────
        sb.AppendLine("## Predicted vs Measured (at prior means)");
        sb.AppendLine();
        sb.AppendLine("| Observable | Predicted | Measured | Residual |");
        sb.AppendLine("|------------|----------:|---------:|---------:|");
        AppendObservable(sb, "Total mass flow (kg/s)", priorPrediction.TotalMassFlow_kgs,     measured.TotalMassFlow_kgs, "F4");
        AppendObservable(sb, "Peak wall T (K)",        priorPrediction.PeakWallT_K,           measured.WallT_K,           "F1");
        AppendObservable(sb, "Coolant ΔT (K)",         priorPrediction.CoolantDT_K,           measured.CoolantDT_K,       "F1");
        AppendObservable(sb, "Coolant ΔP (kPa)",       priorPrediction.CoolantDP_Pa / 1e3,    measured.CoolantDP_Pa / 1e3, "F2");
        sb.AppendLine();

        // ── Fit quality ───────────────────────────────────────────────────────
        sb.AppendLine("## Fit Quality");
        sb.AppendLine();
        sb.AppendLine($"- SSR at prior means: `{cal.SsrAtPrior:G6}`");
        sb.AppendLine($"- SSR at MAP:         `{cal.SsrAtMap:G6}`");
        sb.AppendLine($"- Improvement:        `{improvement:P1}`");
        sb.AppendLine($"- Iterations used:    `{cal.IterationsUsed}`");
        sb.AppendLine($"- Samples averaged:   `{measured.SampleCount}`");
        sb.AppendLine($"- Chamber pressure:   `{measured.ChamberP_Pa / 1e6:F3} MPa`");
        sb.AppendLine();

        // ── Notes ─────────────────────────────────────────────────────────────
        bool hasNotes = (cal.Notes?.Length > 0) || (parseWarnings?.Length > 0);
        if (hasNotes)
        {
            sb.AppendLine("## Notes");
            sb.AppendLine();
            if (cal.Notes is not null)
                foreach (var note in cal.Notes)
                    sb.AppendLine($"- {note}");
            if (parseWarnings is not null)
                foreach (var w in parseWarnings)
                    sb.AppendLine($"- CSV warning: {w}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendKnobRow(StringBuilder sb, KnobEstimate k)
    {
        double delta = k.MapValue - k.PriorMean;
        string deltaStr = delta >= 0
            ? $"+{delta.ToString("F4", Inv)}"
            : delta.ToString("F4", Inv);
        sb.AppendLine($"| {k.Name} | {k.PriorMean.ToString("F4", Inv)} | {k.MapValue.ToString("F4", Inv)} | {deltaStr} | {k.Interpretation} |");
    }

    private static void AppendObservable(StringBuilder sb, string name,
        double predicted, double measured, string fmt)
    {
        string predStr = FormatVal(predicted, fmt);
        string measStr = FormatVal(measured,  fmt);
        string residStr = (!double.IsNaN(predicted) && !double.IsNaN(measured))
            ? FormatVal(predicted - measured, fmt)
            : "n/a";
        sb.AppendLine($"| {name} | {predStr} | {measStr} | {residStr} |");
    }

    private static string FormatVal(double v, string fmt)
        => double.IsNaN(v) ? "n/a" : v.ToString(fmt, Inv);
}
