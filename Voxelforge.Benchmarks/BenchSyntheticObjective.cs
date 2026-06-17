// BenchSyntheticObjective.cs — `bench-sa --synthetic <obj>` mode.
//
// Why this exists: every canonical bench-sa preset returns 0 feasible
// candidates (per ADR-018 feasibility audit) so the bench-sa output
// can't discriminate "good SA configuration" from "bad SA configuration"
// — both produce best_total_score = -1 (sentinel for infeasible). For
// validating multi-chain SA, CMA-ES, or any SA-quality experiment we
// need a known-feasible objective with a known minimum.
//
// Three textbook test problems are wired:
//
//   • convex     — sum of squares, minimum at x = 0.5 in each dim, score 0.
//                  Trivially solvable; mostly tests determinism + scaling.
//   • rosenbrock — narrow curved valley, minimum at x = 1 in each dim.
//                  Tests an optimizer's ability to follow a curved
//                  trajectory — distinguishes basic SA from CMA-ES /
//                  multi-chain quickly.
//   • rastrigin  — non-convex with many local minima on a regular grid.
//                  Tests escape from local minima — distinguishes
//                  single-chain (gets stuck) from multi-chain (escapes
//                  via cross-chain migration) cleanly.
//
// All three operate on the same 24-dim "design vector" the production
// SA uses, mapped to [0, 1)^24 by the synthetic objective. That keeps
// the SA setup (bounds, perturbation magnitude, cooling schedule)
// identical to production runs — only the evaluator changes.

using System.Diagnostics;
using System.Globalization;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static class BenchSyntheticObjective
{
    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --bench-sa --synthetic "
      + "<convex|rosenbrock|rastrigin> "
      + "[--seed <int=42>] [--iterations <N=500>] [--repeat <N=3>] "
      + "[--multi-chain] [--chains <N=auto>] [--out <jsonl>]";

    private static double EvalConvex(double[] x)
    {
        double s = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double d = x[i] - 0.5;
            s += d * d;
        }
        return s;
    }

    private static double EvalRosenbrock(double[] x)
    {
        // Standard Rosenbrock: minimum at x = 1 in every dim, value 0.
        // Map [0, 1] → [-2, 2] so the global min is in the interior.
        double s = 0;
        for (int i = 0; i + 1 < x.Length; i++)
        {
            double xi  = -2.0 + 4.0 * x[i];
            double xi1 = -2.0 + 4.0 * x[i + 1];
            double a = xi1 - xi * xi;
            double b = 1.0 - xi;
            s += 100.0 * a * a + b * b;
        }
        return s;
    }

    private static double EvalRastrigin(double[] x)
    {
        // Standard Rastrigin: minimum at x = 0 in every dim, value 0.
        // Map [0, 1] → [-5.12, 5.12] (textbook range).
        double s = 10.0 * x.Length;
        for (int i = 0; i < x.Length; i++)
        {
            double xi = -5.12 + 10.24 * x[i];
            s += xi * xi - 10.0 * System.Math.Cos(2.0 * System.Math.PI * xi);
        }
        return s;
    }

    public static int Run(string[] args)
    {
        // Args layout (after the leading "--synthetic <obj>" already
        // consumed by the caller in BenchSA.Run): same flags as bench-sa
        // but the evaluator is fixed.
        string obj = "";
        int seed = 42;
        int iterations = 500;
        int repeat = 3;
        string? outPath = null;
        bool useMultiChain = false;
        int chainCount = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--synthetic":
                    if (i + 1 >= args.Length) { System.Console.Error.WriteLine("--synthetic missing value"); return 3; }
                    obj = args[++i].ToLowerInvariant();
                    break;
                case "--seed":
                    if (i + 1 >= args.Length) { System.Console.Error.WriteLine("--seed missing value"); return 3; }
                    if (!int.TryParse(args[++i], out seed))
                    { System.Console.Error.WriteLine($"--seed must be int"); return 3; }
                    break;
                case "--iterations":
                    if (i + 1 >= args.Length) { System.Console.Error.WriteLine("--iterations missing value"); return 3; }
                    if (!int.TryParse(args[++i], out iterations) || iterations < 1)
                    { System.Console.Error.WriteLine($"--iterations must be ≥ 1"); return 3; }
                    break;
                case "--repeat":
                    if (i + 1 >= args.Length) { System.Console.Error.WriteLine("--repeat missing value"); return 3; }
                    if (!int.TryParse(args[++i], out repeat) || repeat < 1)
                    { System.Console.Error.WriteLine($"--repeat must be ≥ 1"); return 3; }
                    break;
                case "--out":
                    if (i + 1 >= args.Length) { System.Console.Error.WriteLine("--out missing value"); return 3; }
                    outPath = args[++i];
                    break;
                case "--multi-chain":
                    useMultiChain = true;
                    break;
                case "--chains":
                    if (i + 1 >= args.Length) { System.Console.Error.WriteLine("--chains missing value"); return 3; }
                    if (!int.TryParse(args[++i], out chainCount) || chainCount < 0)
                    { System.Console.Error.WriteLine($"--chains must be ≥ 0"); return 3; }
                    useMultiChain = true;
                    break;
                case "-h": case "--help":
                    System.Console.WriteLine(UsageLine);
                    return 0;
                default:
                    System.Console.Error.WriteLine($"Unknown arg '{args[i]}'");
                    System.Console.Error.WriteLine(UsageLine);
                    return 3;
            }
        }

        System.Func<double[], double> eval = obj switch
        {
            "convex"     => EvalConvex,
            "rosenbrock" => EvalRosenbrock,
            "rastrigin"  => EvalRastrigin,
            _ => throw new System.ArgumentException($"Unknown synthetic objective '{obj}'. "
                                                  + "Valid: convex, rosenbrock, rastrigin."),
        };

        // Use the production 24-dim Bounds so the SA setup matches.
        var bounds = RegenChamberOptimization.Bounds;
        // For synthetic, normalise to [0, 1] — the evaluator handles its own scaling.
        var unitBounds = new (double Min, double Max)[bounds.Length];
        for (int i = 0; i < bounds.Length; i++) unitBounds[i] = (0.0, 1.0);

        int effectiveChains = useMultiChain
            ? (chainCount > 0 ? chainCount : MultiChainOptimizer.DefaultChainCount())
            : 1;
        string mode = useMultiChain ? $"multi-chain (×{effectiveChains})" : "single-chain";

        outPath ??= System.IO.Path.Combine(System.AppContext.BaseDirectory, "baselines",
            $"bench-sa-synthetic-{obj}-{System.DateTime.UtcNow:yyyy-MM-dd}.jsonl");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath)!);
        System.Console.WriteLine($"# bench-sa --synthetic {obj} seed={seed} iters={iterations} repeat={repeat} mode={mode}");
        var machine = MachineInfo.Capture();
        System.Console.WriteLine(machine.ToHeaderLine());
        System.Console.WriteLine($"# JSONL: {outPath}");

        var c = CultureInfo.InvariantCulture;
        for (int rep = 0; rep < repeat; rep++)
        {
            int repSeed = seed + rep;
            System.Console.WriteLine();
            System.Console.WriteLine($"# === repeat {rep + 1}/{repeat} (seed={repSeed}) ===");

            long swStart = Stopwatch.GetTimestamp();
            double bestScore;
            int firstFeasibleIter = 0;   // synthetic objective is always finite
            int totalEvals;

            if (useMultiChain)
            {
                var multi = new MultiChainOptimizer(
                    bounds:        unitBounds,
                    maxIterations: iterations,
                    baseSeed:      repSeed,
                    chainCount:    effectiveChains);
                multi.PerChainMaxConsecutiveInfeasibleBeforeExit = 0;
                var result = multi.Run((cand) => (eval(cand), (object?)null));
                bestScore = result.BestScore;
                totalEvals = result.TotalIterations;
            }
            else
            {
                var opt = new SimulatedAnnealingOptimizer(unitBounds, iterations, repSeed);
                opt.MaxConsecutiveInfeasibleBeforeExit = 0;
                while (!opt.IsComplete)
                {
                    var cand = opt.NextCandidate();
                    double score = eval(cand);
                    opt.ReportScore(cand, score, null);
                }
                bestScore = opt.BestScore;
                totalEvals = opt.Iteration;
            }

            long swEnd = Stopwatch.GetTimestamp();
            double wall_ms = (swEnd - swStart) * 1000.0 / Stopwatch.Frequency;

            System.Console.WriteLine($"BENCH preset=synthetic-{obj}");
            System.Console.WriteLine($"BENCH seed={repSeed.ToString(c)}");
            System.Console.WriteLine($"BENCH mode={(useMultiChain ? "multi-chain" : "single-chain")}");
            if (useMultiChain) System.Console.WriteLine($"BENCH chains={effectiveChains.ToString(c)}");
            System.Console.WriteLine($"BENCH iterations_completed={totalEvals.ToString(c)}");
            System.Console.WriteLine($"BENCH iterations_max={(iterations * effectiveChains).ToString(c)}");
            System.Console.WriteLine($"BENCH first_feasible_iter={firstFeasibleIter.ToString(c)}");
            System.Console.WriteLine($"BENCH wall_total_ms={wall_ms.ToString("F1", c)}");
            System.Console.WriteLine($"BENCH best_total_score={bestScore.ToString("F6", c)}");

            JsonlSchema.AppendRecord(outPath, JsonlSchema.BenchNames.BenchSa, sb =>
            {
                sb.Append("\"preset\":\"synthetic-").Append(obj).Append("\",");
                sb.Append("\"seed\":").Append(repSeed.ToString(c)).Append(',');
                sb.Append("\"mode\":\"").Append(useMultiChain ? "multi-chain" : "single-chain").Append("\",");
                if (useMultiChain) sb.Append("\"chains\":").Append(effectiveChains.ToString(c)).Append(',');
                sb.Append("\"iterations_completed\":").Append(totalEvals.ToString(c)).Append(',');
                sb.Append("\"iterations_max\":").Append((iterations * effectiveChains).ToString(c)).Append(',');
                sb.Append("\"first_feasible_iter\":").Append(firstFeasibleIter.ToString(c)).Append(',');
                sb.Append("\"wall_total_ms\":").Append(wall_ms.ToString("F1", c)).Append(',');
                sb.Append("\"best_total_score\":").Append(bestScore.ToString("F6", c));
            });
        }

        System.Console.WriteLine();
        System.Console.WriteLine($"BENCH_MEDIAN  bench=bench-sa-synthetic  preset={obj}  records_appended={repeat}");
        return 0;
    }
}
