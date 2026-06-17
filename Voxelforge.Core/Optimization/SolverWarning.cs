// SolverWarning.cs — Structured severity grading for solver warnings.
//
// Flat string warnings have accumulated across Thermal, Manufacturing,
// FilmCooling, Film, and the feasibility gate. The UI used to stuff all
// of them into one text box, green on black, with an `[INFEASIBLE]`
// prefix for the hard-gate ones. Users had to scan for severity.
//
// This module adds a structured severity grading — kept alongside the
// legacy string[] Warnings so existing tests and code keep working —
// and lets the UI render a coloured three-column table.
//
// Aggregation is centralised in WarningAggregator.BuildFor so the UI
// and the report exporter see the same structured list regardless of
// which subsystem emitted the underlying text.

namespace Voxelforge.Optimization;

/// <summary>
/// Severity grading for a single <see cref="SolverWarning"/>.
/// Ordered from least to most severe so consumers can sort by integer.
/// </summary>
public enum WarningSeverity
{
    /// <summary>Informational — does not impact design validity.</summary>
    Info = 0,
    /// <summary>Worth inspecting — physics is marginal or partially modelled.</summary>
    Warn = 1,
    /// <summary>Design is infeasible / unprintable / unsafe as specified.</summary>
    Critical = 2,
}

/// <summary>
/// One structured warning. <see cref="Code"/> is a short stable machine key
/// used by tests + external tooling; <see cref="Message"/> is the
/// human-readable description.
/// </summary>
public sealed record SolverWarning(
    WarningSeverity Severity,
    string Code,
    string Message);

public static class WarningAggregator
{
    /// <summary>
    /// Build the structured warning list for a completed score result.
    /// Collects from: feasibility violations (→ Critical), thermal/structural
    /// flags (→ Critical), pseudocritical / feature warnings (→ Warn), and
    /// solver info (→ Info). Everything not otherwise matched comes in as
    /// Warn so no flat warning is ever dropped.
    /// </summary>
    public static SolverWarning[] BuildFor(
        RegenGenerationResult gen, RegenScoreResult score)
    {
        var list = new List<SolverWarning>();

        // ── Hard violations → Critical ───────────────────────────
        foreach (var v in score.FeasibilityViolations)
            list.Add(new SolverWarning(WarningSeverity.Critical, v.ConstraintId, v.Description));

        if (score.YieldExceeded && !HasCode(list, "YIELD_EXCEEDED"))
            list.Add(new SolverWarning(
                WarningSeverity.Critical, "YIELD_EXCEEDED",
                $"Min safety factor {score.MinSafetyFactor:F2} < 1.0."));

        if (score.WallTExceeded && !HasCode(list, "WALL_TEMP"))
            list.Add(new SolverWarning(
                WarningSeverity.Critical, "WALL_TEMP",
                $"Peak wall T {score.PeakWallT_K:F0} K exceeds material limit."));

        // ── Thermal / film / solver text warnings ────────────────
        foreach (var w in gen.Thermal.Warnings)
            list.Add(ClassifyThermal(w));

        // ── Manufacturing ────────────────────────────────────────
        foreach (var w in gen.Manufacturing.Warnings)
            list.Add(ClassifyManufacturing(w));

        // ── Solver diagnostics (convergence) ─────────────────────
        var diag = gen.Thermal.Diagnostics;
        if (diag.MaxWallTempIterationsHit > 0)
            list.Add(new SolverWarning(WarningSeverity.Warn, "WALLT_ITER_HIT",
                $"{diag.MaxWallTempIterationsHit} station(s) hit the 15-iter wall-T cap — values at those stations are suspect."));
        if (diag.PressureClampedCount > 0)
            list.Add(new SolverWarning(WarningSeverity.Warn, "P_CLAMP",
                $"{diag.PressureClampedCount} station(s) hit the 0.1 MPa coolant floor — pressure-drop estimate is a lower bound."));
        if (diag.StationsInPseudocritical > 0)
            list.Add(new SolverWarning(WarningSeverity.Warn, "PSEUDOCRITICAL",
                $"{diag.StationsInPseudocritical} station(s) in pseudocritical region — correlation accuracy degraded."));

        return list.ToArray();
    }

    private static bool HasCode(List<SolverWarning> list, string code)
    {
        foreach (var w in list) if (w.Code == code) return true;
        return false;
    }

    private static SolverWarning ClassifyThermal(string msg)
    {
        if (msg.Contains("exceeds material limit", StringComparison.OrdinalIgnoreCase))
            return new SolverWarning(WarningSeverity.Critical, "WALL_TEMP", msg);
        if (msg.Contains("coking") || msg.Contains("embrittle", StringComparison.OrdinalIgnoreCase))
            return new SolverWarning(WarningSeverity.Critical, "COOLANT_SERVICE_LIMIT", msg);
        if (msg.Contains("service limit", StringComparison.OrdinalIgnoreCase))
            return new SolverWarning(WarningSeverity.Warn, "FLUID_SERVICE_LIMIT", msg);
        if (msg.Contains("pseudocritical", StringComparison.OrdinalIgnoreCase))
            return new SolverWarning(WarningSeverity.Warn, "PSEUDOCRITICAL", msg);
        if (msg.Contains("clamped", StringComparison.OrdinalIgnoreCase)
         || msg.Contains("fell below", StringComparison.OrdinalIgnoreCase))
            return new SolverWarning(WarningSeverity.Warn, "SOLVER_CLAMP", msg);
        if (msg.StartsWith("Coolant = ", StringComparison.Ordinal))
            return new SolverWarning(WarningSeverity.Info, "FLUID_SELECTED", msg);
        return new SolverWarning(WarningSeverity.Warn, "THERMAL", msg);
    }

    private static SolverWarning ClassifyManufacturing(string msg)
    {
        if (msg.Contains("cannot be printed", StringComparison.OrdinalIgnoreCase)
         || msg.Contains("infeasible", StringComparison.OrdinalIgnoreCase))
            return new SolverWarning(WarningSeverity.Critical, "LPBF_INFEASIBLE", msg);
        if (msg.Contains("below", StringComparison.OrdinalIgnoreCase)
         || msg.Contains("exceed", StringComparison.OrdinalIgnoreCase))
            return new SolverWarning(WarningSeverity.Warn, "LPBF_MARGINAL", msg);
        return new SolverWarning(WarningSeverity.Info, "LPBF", msg);
    }
}
