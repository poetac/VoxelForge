// `--bench-diff` — compare a current bench-sa JSONL against a frozen
// baseline. B1 / BB-6 (2026-04-25): the regression-detection half of
// the bench-regression CI workflow.
//
// Usage:
//   dotnet run --project Voxelforge.Benchmarks -- --bench-diff
//     <baseline.jsonl> <current.jsonl>
//     [--pillar <rocket|airbreathing|electric|marine|all>]
//     [--threshold-percent <float=5.0>]
//     [--summary-only]
//
//   Auto-discover mode (one positional path = current output):
//     dotnet run -- --bench-diff <current.jsonl> --pillar rocket
//
//   Cross-pillar drift report (no positional paths):
//     dotnet run -- --bench-diff --pillar all [--baselines-dir <path>]
//
// Exit codes:
//   0 — all physics scalars within threshold AND all boolean fields match
//   1 — usage / arg-parse error
//   2 — file-load error (one of the JSONL files missing or malformed)
//   3 — at least one (preset, seed) pair missing in the current file
//   4 — at least one physics scalar exceeded threshold OR boolean diverged
//   5 — baseline not found (pillar dir exists but no matching JSONL)
//   6 — no previous baseline found for cross-pillar drift (warning only, exits 0)
//
// Field policy:
//   - Physics scalars (PhysicsFields below) are diff'd as
//     |delta| / max(|baseline|, eps) and compared to threshold.
//   - Boolean fields (BoolFields) must match exactly.
//   - Provenance fields (machine_id, git_sha, timestamp, build_config)
//     are intentionally NOT compared — they vary by run environment.
//   - Timing fields (per_iter_*us, wall_total_ms) are intentionally NOT
//     compared — they vary by machine class.
//   - Cross-pillar fields are compared on the intersection of fields
//     present in both baseline and current records.
//
// Sprint B.2 (BB Wave 1): added --pillar filter, single-arg auto-discovery,
// and --pillar all cross-pillar drift report.

using System.Globalization;
using System.Text.Json;

namespace Voxelforge.Benchmarks;

internal static class BenchDiff
{
    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --bench-diff "
      + "[<baseline.jsonl>] <current.jsonl> "
      + "[--pillar <rocket|airbreathing|electric|marine|all>] "
      + "[--threshold-percent <float=5.0>] [--summary-only] "
      + "[--baselines-dir <path>]";

    private static readonly string[] PhysicsFields =
    {
        "best_total_score",
        "peak_wall_t_k",
        "wall_t_margin_k",
        "coolant_dp_pa",
        "coolant_dp_fraction",
        "coolant_t_out_k",
        "total_heat_load_w",
        "throat_heat_flux_wm2",
        "mass_g",
        "min_safety_factor",
        "fuel_mass_flow_kgs",
        "ox_mass_flow_kgs",
    };

    private static readonly string[] BoolFields =
    {
        "convergence_reached",
        "wall_t_exceeded",
        "infeasible_feature",
        "npsh_feasible",
    };

    // Known pillars with baseline directories. Electric/marine are future.
    private static readonly string[] KnownPillars = ["rocket", "airbreathing"];

    public static int Run(string[] args)
    {
        // Separate positional args from flags so --pillar all can have zero positional args.
        var positional = new List<string>();
        double thresholdPct = 5.0;
        bool summaryOnly = false;
        string pillar = "rocket";   // default — backward compat
        string? baselinesDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pillar":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--pillar missing value"); return 1; }
                    pillar = args[++i].ToLowerInvariant();
                    if (pillar is not ("rocket" or "airbreathing" or "electric" or "marine" or "all"))
                    { Console.Error.WriteLine($"--pillar must be rocket|airbreathing|electric|marine|all, got '{pillar}'"); return 1; }
                    break;
                case "--threshold-percent":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--threshold-percent missing value"); return 1; }
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out thresholdPct)
                        || thresholdPct < 0.0 || thresholdPct > 1000.0)
                    { Console.Error.WriteLine($"--threshold-percent must be 0..1000, got '{args[i]}'"); return 1; }
                    break;
                case "--summary-only":
                    summaryOnly = true;
                    break;
                case "--baselines-dir":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--baselines-dir missing value"); return 1; }
                    baselinesDir = args[++i];
                    break;
                case "-h":
                case "--help":
                    Console.WriteLine(UsageLine);
                    return 0;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"Unknown arg '{args[i]}'");
                        Console.Error.WriteLine(UsageLine);
                        return 1;
                    }
                    positional.Add(args[i]);
                    break;
            }
        }

        // --pillar all: cross-pillar drift report (no positional args needed).
        if (pillar == "all")
            return RunCrossPillar(baselinesDir, thresholdPct, summaryOnly);

        // Standard two-path mode or single-path auto-discovery.
        string baselinePath, currentPath;

        if (positional.Count == 2)
        {
            baselinePath = positional[0];
            currentPath = positional[1];
        }
        else if (positional.Count == 1)
        {
            currentPath = positional[0];
            // Auto-discover latest baseline for this pillar.
            string? discovered = AutoDiscoverBaseline(currentPath, pillar, baselinesDir);
            if (discovered is null)
            {
                Console.Error.WriteLine($"# bench-diff: no baseline found for pillar '{pillar}'.");
                Console.Error.WriteLine($"# Searched in: {BaselineDir(pillar, baselinesDir)}");
                return 5;
            }
            baselinePath = discovered;
            Console.WriteLine($"# bench-diff: auto-discovered baseline: {baselinePath}");
        }
        else
        {
            Console.Error.WriteLine(UsageLine);
            return 1;
        }

        return RunCompare(baselinePath, currentPath, pillar, thresholdPct, summaryOnly);
    }

    private static int RunCompare(string baselinePath, string currentPath,
                                   string pillar, double thresholdPct, bool summaryOnly)
    {
        Dictionary<(string preset, int seed), JsonElement> baseline;
        Dictionary<(string preset, int seed), JsonElement> current;
        try
        {
            baseline = LoadJsonl(baselinePath);
            current = LoadJsonl(currentPath);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"# bench-diff: {ex.Message}");
            return 2;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"# bench-diff: malformed JSONL: {ex.Message}");
            return 2;
        }

        Console.WriteLine($"# bench-diff pillar={pillar}");
        Console.WriteLine($"# bench-diff baseline={baselinePath} ({baseline.Count} records)");
        Console.WriteLine($"# bench-diff current ={currentPath} ({current.Count} records)");
        Console.WriteLine($"# threshold: {thresholdPct:F2}% on {PhysicsFields.Length} physics fields");
        Console.WriteLine($"# boolean parity: {BoolFields.Length} fields");
        Console.WriteLine();

        int missing = 0, physicsDeltas = 0, boolMismatches = 0, recordsCompared = 0;
        var failureLines = new List<string>();

        foreach (var (key, baselineRec) in baseline.OrderBy(kv => kv.Key.preset).ThenBy(kv => kv.Key.seed))
        {
            if (!current.TryGetValue(key, out var currentRec))
            {
                missing++;
                failureLines.Add($"MISSING preset={key.preset} seed={key.seed}");
                continue;
            }
            recordsCompared++;

            foreach (var field in PhysicsFields)
            {
                if (!baselineRec.TryGetProperty(field, out var bVal)) continue;
                if (!currentRec.TryGetProperty(field, out var cVal))
                {
                    failureLines.Add($"MISSING_FIELD preset={key.preset} seed={key.seed} field={field} (in baseline but not current)");
                    physicsDeltas++;
                    continue;
                }
                bool bIsNull = bVal.ValueKind == JsonValueKind.Null;
                bool cIsNull = cVal.ValueKind == JsonValueKind.Null;
                if (bIsNull && cIsNull)
                {
                    // Both sides null — agreement on absence is no signal.
                    continue;
                }
                if (bIsNull || cIsNull)
                {
                    // Asymmetric appearance/disappearance is a regression.
                    failureLines.Add(
                        $"NULL_FIELD preset={key.preset} seed={key.seed} field={field} "
                      + $"baselineKind={bVal.ValueKind} currentKind={cVal.ValueKind}");
                    physicsDeltas++;
                    continue;
                }
                double bv = bVal.GetDouble();
                double cv = cVal.GetDouble();
                double absDelta = Math.Abs(cv - bv);
                double denom = Math.Max(Math.Abs(bv), 1e-12);
                double pct = (absDelta / denom) * 100.0;
                if (pct > thresholdPct)
                {
                    physicsDeltas++;
                    failureLines.Add(
                        $"PHYSICS preset={key.preset} seed={key.seed} field={field} "
                      + $"baseline={bv.ToString("G6", CultureInfo.InvariantCulture)} "
                      + $"current={cv.ToString("G6", CultureInfo.InvariantCulture)} "
                      + $"delta={pct.ToString("F3", CultureInfo.InvariantCulture)}% > {thresholdPct:F2}%");
                }
            }

            foreach (var field in BoolFields)
            {
                if (!baselineRec.TryGetProperty(field, out var bVal)) continue;
                if (!currentRec.TryGetProperty(field, out var cVal))
                {
                    failureLines.Add($"MISSING_FIELD preset={key.preset} seed={key.seed} field={field}");
                    boolMismatches++;
                    continue;
                }
                bool bv = bVal.GetBoolean();
                bool cv = cVal.GetBoolean();
                if (bv != cv)
                {
                    boolMismatches++;
                    failureLines.Add($"BOOLEAN preset={key.preset} seed={key.seed} field={field} baseline={bv} current={cv}");
                }
            }
        }

        if (!summaryOnly)
        {
            foreach (var line in failureLines) Console.WriteLine(line);
            if (failureLines.Count > 0) Console.WriteLine();
        }

        Console.WriteLine($"# Summary: {recordsCompared} records compared, "
                        + $"{missing} missing pairs, "
                        + $"{physicsDeltas} physics deltas > {thresholdPct:F2}%, "
                        + $"{boolMismatches} boolean mismatches");

        if (missing > 0) return 3;
        if (physicsDeltas + boolMismatches > 0) return 4;
        Console.WriteLine("# PASS — all records within threshold and booleans match.");
        return 0;
    }

    // --pillar all: scan all known pillar dirs, compare latest vs previous
    // baseline per preset, and emit a cross-pillar Markdown drift table.
    private static int RunCrossPillar(string? baselinesDir, double thresholdPct, bool summaryOnly)
    {
        string date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Console.WriteLine($"## Cross-pillar bench-diff summary — {date}");
        Console.WriteLine();
        Console.WriteLine($"| Pillar | Preset | Latest baseline | Prev baseline | Score Δ | Status |");
        Console.WriteLine($"|--------|--------|-----------------|---------------|---------|--------|");

        int totalAlerts = 0;

        foreach (string p in KnownPillars)
        {
            string dir = BaselineDir(p, baselinesDir);
            if (!Directory.Exists(dir))
            {
                Console.WriteLine($"| {p} | _(dir not found)_ | — | — | — | SKIP |");
                continue;
            }

            // Gather all JSONL files in this pillar dir.
            var jsonlFiles = Directory.GetFiles(dir, "*.jsonl")
                                      .OrderBy(f => Path.GetFileName(f))
                                      .ToArray();

            if (jsonlFiles.Length == 0)
            {
                Console.WriteLine($"| {p} | _(no baselines)_ | — | — | — | SKIP |");
                continue;
            }

            // Group files by the `preset` field from their JSON content.
            var byPreset = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in jsonlFiles)
            {
                try
                {
                    // Read just the first non-empty line to get preset.
                    string? preset = null;
                    foreach (var line in File.ReadLines(file))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        using var doc = JsonDocument.Parse(line);
                        if (doc.RootElement.TryGetProperty("preset", out var el))
                            preset = el.GetString();
                        break;
                    }
                    if (preset is null) continue;
                    if (!byPreset.TryGetValue(preset, out var list))
                        byPreset[preset] = list = [];
                    list.Add(file);
                }
                catch { /* skip malformed files */ }
            }

            foreach (var (preset, files) in byPreset.OrderBy(kv => kv.Key))
            {
                // Files are already sorted lexicographically (date in name).
                if (files.Count < 2)
                {
                    string onlyName = Path.GetFileName(files[^1]);
                    Console.WriteLine($"| {p} | {preset} | {onlyName} | _(none)_ | — | NO_PREV |");
                    continue;
                }

                string prevPath   = files[^2];
                string latestPath = files[^1];
                string prevName   = Path.GetFileName(prevPath);
                string latestName = Path.GetFileName(latestPath);

                Dictionary<(string, int), JsonElement> prevRec;
                Dictionary<(string, int), JsonElement> latestRec;
                try
                {
                    prevRec   = LoadJsonl(prevPath);
                    latestRec = LoadJsonl(latestPath);
                }
                catch
                {
                    Console.WriteLine($"| {p} | {preset} | {latestName} | {prevName} | — | LOAD_ERR |");
                    totalAlerts++;
                    continue;
                }

                // Diff latest vs previous; compare only the field intersection.
                double maxScoreDeltaPct = 0;
                bool anyFailure = false;
                foreach (var (key, pRec) in prevRec)
                {
                    if (!latestRec.TryGetValue(key, out var lRec)) continue;
                    foreach (var field in PhysicsFields)
                    {
                        if (!pRec.TryGetProperty(field, out var bVal)) continue;
                        if (!lRec.TryGetProperty(field, out var cVal)) continue;
                        if (bVal.ValueKind == JsonValueKind.Null || cVal.ValueKind == JsonValueKind.Null) continue;
                        double bv = bVal.GetDouble();
                        double cv = cVal.GetDouble();
                        double pct = Math.Abs(cv - bv) / Math.Max(Math.Abs(bv), 1e-12) * 100.0;
                        if (pct > thresholdPct) anyFailure = true;
                        if (field == "best_total_score") maxScoreDeltaPct = pct;
                    }
                }

                string scoreDeltaStr = maxScoreDeltaPct == 0 ? "0%" : $"{maxScoreDeltaPct:+0.0;-0.0}%";
                string status = anyFailure ? "DRIFT" : "OK";
                if (anyFailure) totalAlerts++;

                Console.WriteLine($"| {p} | {preset} | {latestName} | {prevName} | {scoreDeltaStr} | {status} |");
            }
        }

        Console.WriteLine();
        if (totalAlerts == 0)
            Console.WriteLine($"# Cross-pillar: all presets within {thresholdPct:F1}% threshold — OK");
        else
            Console.WriteLine($"# Cross-pillar: {totalAlerts} preset(s) with DRIFT or errors — review above.");

        return 0;   // Non-gating: cross-pillar drift is informational.
    }

    // Returns the baseline directory path for the given pillar.
    private static string BaselineDir(string pillar, string? overrideDir)
    {
        if (overrideDir is not null)
            return Path.Combine(overrideDir, pillar);

        // Try repo-root-relative path first (CI runs from repo root).
        string repoRelative = Path.Combine("Voxelforge.Benchmarks", "baselines", pillar);
        if (Directory.Exists(repoRelative)) return repoRelative;

        // Fall back to executable-relative (local dev runs from bin/).
        return Path.Combine(AppContext.BaseDirectory, "baselines", pillar);
    }

    // Auto-discover the latest baseline for the preset found in currentPath,
    // searching in the given pillar's baseline directory.
    private static string? AutoDiscoverBaseline(string currentPath, string pillar, string? baselinesDir)
    {
        // Read preset name from the current JSONL.
        string? preset = null;
        try
        {
            foreach (var line in File.ReadLines(currentPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("preset", out var el))
                    preset = el.GetString();
                break;
            }
        }
        catch { /* fall through — preset stays null */ }

        if (preset is null) return null;

        string dir = BaselineDir(pillar, baselinesDir);
        if (!Directory.Exists(dir)) return null;

        // Glob for all JSONL files whose content has this preset.
        var candidates = Directory.GetFiles(dir, "*.jsonl")
            .Where(f => {
                try
                {
                    foreach (var line in File.ReadLines(f))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        using var doc = JsonDocument.Parse(line);
                        if (doc.RootElement.TryGetProperty("preset", out var el)
                            && string.Equals(el.GetString(), preset, StringComparison.OrdinalIgnoreCase))
                            return true;
                        return false;
                    }
                }
                catch { /* skip */ }
                return false;
            })
            .OrderByDescending(f => Path.GetFileName(f))  // lexicographic — YYYY-MM-DD in name
            .ToArray();

        return candidates.Length > 0 ? candidates[0] : null;
    }

    private static Dictionary<(string preset, int seed), JsonElement> LoadJsonl(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"file not found: {path}", path);
        var dict = new Dictionary<(string, int), JsonElement>();
        // We hold JsonDocument refs alive by parsing once and cloning each
        // root into a new JsonDocument-backed JsonElement; keeping the
        // documents in a local list prevents finalization mid-iteration.
        var docs = new List<JsonDocument>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var doc = JsonDocument.Parse(line);
            docs.Add(doc);
            var root = doc.RootElement;
            if (!root.TryGetProperty("preset", out var presetEl))
                throw new JsonException($"missing 'preset' field in {path}");
            if (!root.TryGetProperty("seed", out var seedEl))
                throw new JsonException($"missing 'seed' field in {path}");
            string preset = presetEl.GetString() ?? "";
            int seed = seedEl.GetInt32();
            // Keep the most recent record per (preset, seed) — bench-sa
            // appends across re-runs on the same day.
            dict[(preset, seed)] = root;
        }
        return dict;
    }
}
