// BenchRuntimeAudit.cs — Sprint B.3 (BB Wave 1) bench-runtime-audit.
//
// Reads all dated schema-v1 JSONL files in a pillar's baselines directory,
// groups records by (preset, seed), and reports per_iter_p50_us drift from
// the oldest to the most recent baseline. Flags any preset where the p50
// drift exceeds 50 % as DRIFT_ALERT.
//
// CLI:
//   --bench-runtime-audit
//     --pillar <rocket|airbreathing>
//     [--baselines-dir <path>]
//     [--out <report.md>]
//     [--drift-threshold-pct <float=50.0>]
//
// Exit codes:
//   0 — no presets with DRIFT_ALERT
//   1 — usage / arg-parse error
//   2 — baselines dir not found or no JSONL files
//   6 — one or more presets exceed the drift threshold (informational; non-gating)

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Voxelforge.Benchmarks;

internal static class BenchRuntimeAudit
{
    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --bench-runtime-audit "
      + "--pillar <rocket|airbreathing> "
      + "[--baselines-dir <path>] [--out <report.md>] "
      + "[--drift-threshold-pct <float=50.0>]";

    public static int Run(string[] args)
    {
        string? pillar = null;
        string? baselinesDir = null;
        string? outPath = null;
        double driftThresholdPct = 50.0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pillar":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--pillar missing value"); return 1; }
                    pillar = args[++i].ToLowerInvariant();
                    break;
                case "--baselines-dir":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--baselines-dir missing value"); return 1; }
                    baselinesDir = args[++i];
                    break;
                case "--out":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--out missing value"); return 1; }
                    outPath = args[++i];
                    break;
                case "--drift-threshold-pct":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--drift-threshold-pct missing value"); return 1; }
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out driftThresholdPct)
                        || driftThresholdPct < 0.0)
                    { Console.Error.WriteLine($"--drift-threshold-pct must be ≥ 0, got '{args[i]}'"); return 1; }
                    break;
                case "-h":
                case "--help":
                    Console.WriteLine(UsageLine);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown arg '{args[i]}'");
                    Console.Error.WriteLine(UsageLine);
                    return 1;
            }
        }

        if (pillar is null)
        {
            Console.Error.WriteLine("Missing required --pillar.");
            Console.Error.WriteLine(UsageLine);
            return 1;
        }

        string dir = ResolveDir(pillar, baselinesDir);
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"# bench-runtime-audit: directory not found: {dir}");
            return 2;
        }

        var jsonlFiles = Directory.GetFiles(dir, "*.jsonl")
                                  .OrderBy(f => Path.GetFileName(f))   // lexicographic = chronological (YYYY-MM-DD)
                                  .ToArray();
        if (jsonlFiles.Length == 0)
        {
            Console.Error.WriteLine($"# bench-runtime-audit: no JSONL files found in {dir}");
            return 2;
        }

        // Collect per-preset timing series.
        // Key: preset name. Value: list of (filename_date, p50_us, wall_ms) ordered chronologically.
        var series = new Dictionary<string, List<(string Date, double P50_us, double Wall_ms)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in jsonlFiles)
        {
            string fname = Path.GetFileName(file);
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("preset", out var presetEl)) continue;
                    string preset = presetEl.GetString() ?? "";
                    if (string.IsNullOrEmpty(preset)) continue;

                    double p50 = root.TryGetProperty("per_iter_p50_us", out var p50El)
                        ? p50El.GetDouble() : double.NaN;
                    double wall = root.TryGetProperty("wall_total_ms", out var wallEl)
                        ? wallEl.GetDouble() : double.NaN;

                    if (!series.TryGetValue(preset, out var list))
                        series[preset] = list = [];

                    // Deduplicate by filename — use the filename as the date key
                    // so multiple records in the same JSONL don't inflate the series.
                    if (!list.Any(e => e.Date == fname))
                        list.Add((fname, p50, wall));
                }
                catch { /* skip malformed lines */ }
            }
        }

        if (series.Count == 0)
        {
            Console.Error.WriteLine("# bench-runtime-audit: no preset records found.");
            return 2;
        }

        // Build the report.
        string date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.AppendLine($"## Bench-runtime audit — {pillar} — {date}");
        sb.AppendLine();
        sb.AppendLine($"Drift threshold: {driftThresholdPct:F0}%  |  Source: `{dir}`");
        sb.AppendLine();
        sb.AppendLine("| Preset | Baselines | Oldest p50 (µs) | Latest p50 (µs) | Drift | Wall-clock (ms) | Status |");
        sb.AppendLine("|--------|-----------|-----------------|-----------------|-------|-----------------|--------|");

        int alertCount = 0;

        foreach (var (preset, entries) in series.OrderBy(kv => kv.Key))
        {
            if (entries.Count == 0) continue;

            var oldest = entries[0];
            var latest = entries[^1];

            string oldestP50Str = double.IsNaN(oldest.P50_us) ? "N/A" : $"{oldest.P50_us:F1}";
            string latestP50Str = double.IsNaN(latest.P50_us) ? "N/A" : $"{latest.P50_us:F1}";
            string wallStr      = double.IsNaN(latest.Wall_ms) ? "N/A" : $"{latest.Wall_ms:F1}";

            string driftStr;
            string status;

            if (double.IsNaN(oldest.P50_us) || double.IsNaN(latest.P50_us) || oldest.P50_us < 1e-6)
            {
                driftStr = "N/A";
                status   = "NO_DATA";
            }
            else
            {
                double driftPct = (latest.P50_us - oldest.P50_us) / oldest.P50_us * 100.0;
                driftStr = $"{driftPct:+0.0;-0.0}%";

                if (Math.Abs(driftPct) > driftThresholdPct)
                {
                    status = "DRIFT_ALERT";
                    alertCount++;
                }
                else
                {
                    status = "OK";
                }
            }

            sb.AppendLine(
                $"| {preset} | {entries.Count} | {oldestP50Str} | {latestP50Str} | {driftStr} | {wallStr} | {status} |");
        }

        sb.AppendLine();
        if (alertCount == 0)
            sb.AppendLine($"**No presets exceeded the {driftThresholdPct:F0}% drift threshold.**");
        else
            sb.AppendLine($"**{alertCount} preset(s) exceeded the {driftThresholdPct:F0}% drift threshold — review DRIFT_ALERT rows.**");

        string report = sb.ToString();

        // Always print to stdout.
        Console.Write(report);

        // Optionally write to a .md file.
        if (outPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".");
            File.WriteAllText(outPath, report);
            Console.WriteLine($"# bench-runtime-audit: report written to {outPath}");
        }

        // Exit 6 if any alert — informational, not gating.
        return alertCount > 0 ? 6 : 0;
    }

    private static string ResolveDir(string pillar, string? baselinesDir)
    {
        if (baselinesDir is not null)
            return Path.Combine(baselinesDir, pillar);

        string repoRelative = Path.Combine("Voxelforge.Benchmarks", "baselines", pillar);
        if (Directory.Exists(repoRelative)) return repoRelative;

        return Path.Combine(AppContext.BaseDirectory, "baselines", pillar);
    }
}
