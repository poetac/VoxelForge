// BenchSaAirbreathing.cs — `--bench-sa-airbreathing` CLI subcommand.
//
// Captures schema-v1 JSONL fingerprints for the air-breathing pillar's SA
// optimization, parallel to the rocket-side `--bench-sa` (BenchSA.cs).
// Supports three presets:
//
//   mattingly-ramjet    — RamjetObjective at Mattingly M=2 / 12 km flight
//                          conditions (AirbreathingFuel.H2). 6-dim SA vector.
//   j85-turbojet        — TurbojetObjective at GE J85-class sea-level static
//                          conditions (AirbreathingFuel.Jp8). 7-dim SA vector.
//   nasa-gtx-rbcc-ramjet — RbccObjective (Ramjet mode) at NASA GTX-class
//                          M=3.5 / 15 km conditions (AirbreathingFuel.H2).
//                          8-dim SA vector. Sprint A11 (sub-step 1e).
//
// Uses the Func-based MultiChainOptimizer.Run overload (not the IObjective
// overload) so each Evaluate call can be timed individually — same pattern
// as BenchSA.RunOneMultiChain. AirbreathingOptimize.Optimize uses the
// IObjective overload and is the production/test entry point; this file
// is bench-only and trades slightly more boilerplate for timing precision.
//
// CLI:
//   --bench-sa-airbreathing
//     --preset <mattingly-ramjet|j85-turbojet|nasa-gtx-rbcc-ramjet>
//     [--seed   <int=42>]
//     [--iterations <N=500>]
//     [--repeat <N=1>]
//     [--chains <N=auto>]
//     [--out    <path.jsonl>]

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Optimization;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static class BenchSaAirbreathing
{
    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --bench-sa-airbreathing "
      + "--preset <mattingly-ramjet|j85-turbojet|nasa-gtx-rbcc-ramjet> "
      + "[--seed <int=42>] [--iterations <N=500>] [--repeat <N=1>] "
      + "[--chains <N=auto>] [--out <jsonl>]";

    private sealed record PresetDef(
        string Key,
        string KindLabel,
        Func<IObjective> ObjectiveFactory);

    private static readonly PresetDef[] Presets =
    {
        new("mattingly-ramjet", "ramjet", () =>
            RamjetObjective.WithDefaultBounds(
                new FlightConditions(12_000.0, 2.0, AirbreathingFuel.H2))),
        // J85 burns JP-8 per AirbreathingFixtures.J85_SeaLevelStatic.
        // AirbreathingFuel.Jp8 is marked "reserved" in the enum docs but the
        // Sprint A7 solver handles it; if it throws NotImplementedException at
        // runtime, change Jp8 → H2 as a placeholder.
        new("j85-turbojet", "turbojet", () =>
            TurbojetObjective.WithDefaultBounds(
                new FlightConditions(0.0, 0.001, AirbreathingFuel.Jp8))),
        // NASA GTX-class RBCC at M=3.5 / 15 km ramjet-mode cruise.
        // Sprint A11 sub-step 1e (RBCC capstone). 8-dim SA vector.
        new("nasa-gtx-rbcc-ramjet", "rbcc", () =>
            RbccObjective.WithDefaultBounds(
                new FlightConditions(15_000.0, 3.5, AirbreathingFuel.H2),
                RbccOperatingMode.Ramjet)),
    };

    public static int Run(string[] args)
    {
        int    seed        = 42;
        int    iterations  = 500;
        int    repeat      = 1;
        int    chainCount  = 0;   // 0 = auto via MultiChainOptimizer.DefaultChainCount()
        string? presetName = null;
        string? outPath    = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--preset":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--preset missing value"); return 3; }
                    presetName = args[++i];
                    break;
                case "--seed":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--seed missing value"); return 3; }
                    if (!int.TryParse(args[++i], out seed))
                    { Console.Error.WriteLine($"--seed must be int, got '{args[i]}'"); return 3; }
                    break;
                case "--iterations":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--iterations missing value"); return 3; }
                    if (!int.TryParse(args[++i], out iterations) || iterations < 1)
                    { Console.Error.WriteLine($"--iterations must be >= 1, got '{args[i]}'"); return 3; }
                    break;
                case "--repeat":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--repeat missing value"); return 3; }
                    if (!int.TryParse(args[++i], out repeat) || repeat < 1)
                    { Console.Error.WriteLine($"--repeat must be >= 1, got '{args[i]}'"); return 3; }
                    break;
                case "--chains":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--chains missing value"); return 3; }
                    if (!int.TryParse(args[++i], out chainCount) || chainCount < 0)
                    { Console.Error.WriteLine($"--chains must be >= 0, got '{args[i]}'"); return 3; }
                    break;
                case "--out":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--out missing value"); return 3; }
                    outPath = args[++i];
                    break;
                default:
                    Console.Error.WriteLine($"Unknown flag: {args[i]}");
                    Console.Error.WriteLine(UsageLine);
                    return 3;
            }
        }

        if (string.IsNullOrWhiteSpace(presetName))
        {
            Console.Error.WriteLine("--preset is required.");
            Console.Error.WriteLine(UsageLine);
            return 3;
        }

        var preset = Array.Find(Presets, p => p.Key == presetName);
        if (preset is null)
        {
            Console.Error.WriteLine($"Unknown preset '{presetName}'. Valid: {string.Join(", ", Array.ConvertAll(Presets, p => p.Key))}");
            return 3;
        }

        int effectiveChains = chainCount > 0 ? chainCount : MultiChainOptimizer.DefaultChainCount();
        string mode = $"multi-chain (×{effectiveChains})";
        Console.WriteLine($"# bench-sa-airbreathing preset={preset.Key} seed={seed} iters={iterations} repeat={repeat} mode={mode}");
        var machine = MachineInfo.Capture();
        Console.WriteLine(machine.ToHeaderLine());

        // Sprint B.1: airbreathing baselines live in baselines/airbreathing/.
        outPath ??= Path.Combine(AppContext.BaseDirectory, "baselines", "airbreathing",
            $"bench-sa-airbreathing-{preset.Key}-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        Console.WriteLine($"# JSONL: {outPath}");

        for (int rep = 0; rep < repeat; rep++)
        {
            Console.WriteLine();
            Console.WriteLine($"# === repeat {rep + 1}/{repeat} (seed={seed + rep}) ===");
            RunOne(preset, seed + rep, iterations, effectiveChains, outPath);
        }

        Console.WriteLine();
        Console.WriteLine($"BENCH_MEDIAN  bench=bench-sa-airbreathing  preset={preset.Key}  records_appended={repeat}");
        return 0;
    }

    private static void RunOne(
        PresetDef preset,
        int repSeed,
        int iterations,
        int chainCount,
        string outPath)
    {
        var c = CultureInfo.InvariantCulture;

        var objective = preset.ObjectiveFactory();
        var multi = new MultiChainOptimizer(
            objective:     objective,
            maxIterations: iterations,
            baseSeed:      repSeed,
            chainCount:    chainCount);

        var iterTimes_us  = new ConcurrentBag<double>();
        long feasibleCount   = 0;
        long infeasibleCount = 0;
        long swStart = Stopwatch.GetTimestamp();

        (double score, object? bd) TimedEvaluator(double[] cand)
        {
            long t0 = Stopwatch.GetTimestamp();
            var r   = objective.Evaluate(cand);
            iterTimes_us.Add((Stopwatch.GetTimestamp() - t0) * 1_000_000.0 / Stopwatch.Frequency);
            if (double.IsPositiveInfinity(r.Score))
                System.Threading.Interlocked.Increment(ref infeasibleCount);
            else
                System.Threading.Interlocked.Increment(ref feasibleCount);
            return (r.Score, (object?)r);
        }

        var result = multi.Run(TimedEvaluator);
        long swEnd = Stopwatch.GetTimestamp();
        double totalWall_ms = (swEnd - swStart) * 1000.0 / Stopwatch.Frequency;

        // Per-iteration timing percentiles.
        var times = iterTimes_us.ToArray();
        Array.Sort(times);
        double pctl(double p) => times.Length == 0 ? 0
            : times[Math.Min(times.Length - 1, (int)Math.Floor(p * (times.Length - 1)))];
        double mean_us  = times.Length == 0 ? 0 : Sum(times) / times.Length;
        double stdev_us = StdDev(times, mean_us);
        double cv       = mean_us > 0 ? stdev_us / mean_us : 0;
        double p50 = pctl(0.50), p90 = pctl(0.90), p99 = pctl(0.99);

        // Score / Isp fingerprint.
        bool hasFeasible     = double.IsFinite(result.BestScore);
        double bestTotalScore = hasFeasible ? result.BestScore : -1.0;
        // Score = -Isp, so Isp = -Score.
        string bestIspJsonVal = hasFeasible
            ? (-result.BestScore).ToString("F1", c)
            : "null";

        Console.WriteLine($"BENCH preset={preset.Key}");
        Console.WriteLine($"BENCH preset_kind={preset.KindLabel}");
        Console.WriteLine($"BENCH seed={repSeed.ToString(c)}");
        Console.WriteLine($"BENCH chain_count={chainCount.ToString(c)}");
        Console.WriteLine($"BENCH iterations_max={iterations.ToString(c)}");
        Console.WriteLine($"BENCH iterations_completed={result.TotalIterations.ToString(c)}");
        Console.WriteLine($"BENCH feasible_count={feasibleCount.ToString(c)}");
        Console.WriteLine($"BENCH infeasible_count={infeasibleCount.ToString(c)}");
        Console.WriteLine($"BENCH per_iter_p50_us={p50.ToString("F1", c)}");
        Console.WriteLine($"BENCH per_iter_p90_us={p90.ToString("F1", c)}");
        Console.WriteLine($"BENCH per_iter_p99_us={p99.ToString("F1", c)}");
        Console.WriteLine($"BENCH per_iter_mean_us={mean_us.ToString("F1", c)}");
        Console.WriteLine($"BENCH per_iter_stdev_us={stdev_us.ToString("F1", c)}");
        Console.WriteLine($"BENCH per_iter_cv={cv.ToString("F3", c)}");
        Console.WriteLine($"BENCH wall_total_ms={totalWall_ms.ToString("F1", c)}");
        Console.WriteLine($"BENCH best_total_score={bestTotalScore.ToString("F2", c)}");
        Console.WriteLine($"BENCH best_isp_s={bestIspJsonVal}");
        Console.WriteLine($"BENCH winning_chain={result.WinningChain.ToString(c)}");

        JsonlSchema.AppendRecord(outPath, JsonlSchema.BenchNames.BenchSaAirbreathing, sb =>
        {
            sb.Append("\"preset\":\"").Append(preset.Key).Append("\",");
            sb.Append("\"preset_kind\":\"").Append(preset.KindLabel).Append("\",");
            sb.Append("\"seed\":").Append(repSeed.ToString(c)).Append(',');
            sb.Append("\"chain_count\":").Append(chainCount.ToString(c)).Append(',');
            sb.Append("\"iterations_max\":").Append(iterations.ToString(c)).Append(',');
            sb.Append("\"iterations_completed\":").Append(result.TotalIterations.ToString(c)).Append(',');
            sb.Append("\"feasible_count\":").Append(feasibleCount.ToString(c)).Append(',');
            sb.Append("\"infeasible_count\":").Append(infeasibleCount.ToString(c)).Append(',');
            sb.Append("\"per_iter_p50_us\":").Append(p50.ToString("F1", c)).Append(',');
            sb.Append("\"per_iter_p90_us\":").Append(p90.ToString("F1", c)).Append(',');
            sb.Append("\"per_iter_p99_us\":").Append(p99.ToString("F1", c)).Append(',');
            sb.Append("\"per_iter_mean_us\":").Append(mean_us.ToString("F1", c)).Append(',');
            sb.Append("\"per_iter_stdev_us\":").Append(stdev_us.ToString("F1", c)).Append(',');
            sb.Append("\"per_iter_cv\":").Append(cv.ToString("F3", c)).Append(',');
            sb.Append("\"wall_total_ms\":").Append(totalWall_ms.ToString("F1", c)).Append(',');
            sb.Append("\"best_total_score\":").Append(bestTotalScore.ToString("F2", c)).Append(',');
            sb.Append("\"best_isp_s\":").Append(bestIspJsonVal).Append(',');
            sb.Append("\"winning_chain\":").Append(result.WinningChain.ToString(c));
        });

        Console.WriteLine($"# repeat {repSeed} done; {result.TotalIterations} total iters; {totalWall_ms:F1} ms wall; best score {bestTotalScore:F2}");
    }

    private static double Sum(double[] xs)
    {
        double s = 0;
        foreach (var x in xs) s += x;
        return s;
    }

    private static double StdDev(double[] xs, double mean)
    {
        if (xs.Length < 2) return 0;
        double sumSq = 0;
        foreach (var x in xs) sumSq += (x - mean) * (x - mean);
        return Math.Sqrt(sumSq / (xs.Length - 1));
    }
}
