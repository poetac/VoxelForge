// BenchSa.MultiChain.cs — multi-chain SA runner for --bench-sa.
// Extracted from BenchSa.cs (BB-1 Wave 1 decomposition).
// RunOneMultiChain drives MultiChainOptimizer with N parallel chains
// + Sobol seeding + barrier-enforced elite migration, and emits the
// same schema-v1 JSONL fields as the single-chain path so baselines
// are diff-comparable across modes.

using System.Diagnostics;
using System.Globalization;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static partial class BenchSA
{
    // Multi-chain variant of RunOne: runs MultiChainOptimizer.Run with N parallel
    // chains + Sobol seeding + barrier-enforced elite migration. Records the same
    // fingerprint scalars as single-chain so the JSONL diff against single-chain
    // baselines quantifies the search-quality + wall-clock difference.
    private static void RunOneMultiChain(CanonicalDesigns.Preset preset,
                                          RegenGenerationResult seedGen, RegenScoreResult seedScore,
                                          double[] baselineParams,
                                          int repSeed, int iterations, int chainCount,
                                          bool disableInfeasibleExit, string outPath,
                                          SaAnimationCapture? saAnim = null)
    {
        var c = CultureInfo.InvariantCulture;
        var multi = new MultiChainOptimizer(
            bounds:          RegenChamberOptimization.Bounds,
            maxIterations:   iterations,
            baseSeed:        repSeed,
            chainCount:      chainCount);
        if (disableInfeasibleExit) multi.PerChainMaxConsecutiveInfeasibleBeforeExit = 0;

        var iterTimes_us = new System.Collections.Concurrent.ConcurrentBag<double>();
        long swStart = Stopwatch.GetTimestamp();
        int feasibleCount = 0;
        int infeasibleCount = 0;
        var feasLock = new object();
        RegenScoreResult? bestFeasibleScore = null;
        int firstFeasibleIter = -1;     // iteration count (across all chains) at first feasible

        // Per-candidate wall-clock measurement; concurrent via ConcurrentBag.
        long iterCounter = 0;
        long firstFeasibleIterAtomic = -1;

        (double, object?) Evaluator(double[] cand)
        {
            long t0 = Stopwatch.GetTimestamp();
            var d = RegenChamberOptimization.Unpack(cand, preset.Seed.Design);
            RegenScoreResult s;
            RegenGenerationResult? g = null;
            try
            {
                g = RegenChamberOptimization.GenerateWith(
                    preset.Seed.Conditions, d,
                    skipVoxelGeometry: true, skipMfgAnalysis: true);
                // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
                s = RegenChamberOptimization.Evaluate(g, RegenChamberOptimization.Profiles[0]);
            }
            catch
            {
                s = MakeInfeasibleScore();
            }
            long t1 = Stopwatch.GetTimestamp();
            iterTimes_us.Add((t1 - t0) * 1_000_000.0 / Stopwatch.Frequency);
            RecordViolations(s);
            DumpTrace(cand, s, g);
            long iter = System.Threading.Interlocked.Increment(ref iterCounter);
            if (double.IsPositiveInfinity(s.TotalScore))
                System.Threading.Interlocked.Increment(ref infeasibleCount);
            else
            {
                System.Threading.Interlocked.Increment(ref feasibleCount);
                // First-feasible across all chains (won by whichever chain hits it first
                // by iteration count — deterministic insofar as iter counter increments
                // are atomic but not bound to a particular chain's arrival order).
                System.Threading.Interlocked.CompareExchange(ref firstFeasibleIterAtomic, iter, -1);
                lock (feasLock)
                {
                    if (bestFeasibleScore == null || s.TotalScore < bestFeasibleScore.TotalScore)
                        bestFeasibleScore = s;
                }
                // OA-1 (#287): offer to the animation capture from inside the
                // chain worker. SaAnimationCapture.OfferFrame is internally
                // lock-serialised across chains so concurrent calls are safe.
                // The captured iter is the across-chain monotonic counter
                // (iterCounter), not the per-chain iteration — that's the
                // ordering the post-SA compose walks anyway.
                saAnim?.OfferFrame((int)iter, s.TotalScore, preset.Seed.Conditions, d);
            }
            return (s.TotalScore, (object?)s);
        }

        var result = multi.Run(Evaluator, initialCandidate: baselineParams);

        long swEnd = Stopwatch.GetTimestamp();
        double totalWall_ms = (swEnd - swStart) * 1000.0 / Stopwatch.Frequency;
        firstFeasibleIter = (int)System.Threading.Interlocked.Read(ref firstFeasibleIterAtomic);

        var bestScore = bestFeasibleScore ?? seedScore;
        var bestGen   = bestFeasibleScore != null ? null : (RegenGenerationResult?)seedGen;

        var iterArr = iterTimes_us.ToArray();
        Array.Sort(iterArr);
        double pctl(double p) => iterArr.Length == 0 ? 0
            : iterArr[Math.Min(iterArr.Length - 1, (int)Math.Floor(p * (iterArr.Length - 1)))];
        double mean_us  = iterArr.Length == 0 ? 0 : iterArr.Average();
        double stdev_us = StdDev(iterArr, mean_us);
        double cv       = mean_us > 0 ? stdev_us / mean_us : 0;
        double p50      = pctl(0.50), p90 = pctl(0.90), p99 = pctl(0.99);

        double bestTotal = double.IsFinite(result.BestScore) ? result.BestScore : -1;
        double peakWallT_K        = bestScore.PeakWallT_K;
        double wallTMargin_K      = bestScore.WallTMargin_K;
        double coolantDP_Pa       = bestScore.CoolantDP_Pa;
        double coolantDP_fraction = bestScore.CoolantDP_Fraction;
        double coolantTOut_K      = bestScore.CoolantTOut_K;
        double totalHeatLoad_W    = bestScore.TotalHeatLoad_W;
        double throatHeatFlux_Wm2 = bestScore.ThroatHeatFlux_Wm2;
        double mass_g             = bestScore.Mass_g;
        double minSafetyFactor    = bestScore.MinSafetyFactor;
        bool   wallTExceeded      = bestScore.WallTExceeded;
        bool   infeasibleFeature  = bestScore.InfeasibleFeature;
        double fuelMassFlow_kgs = bestGen?.Derived.FuelMassFlow_kgs ?? double.NaN;
        double oxMassFlow_kgs   = bestGen?.Derived.OxidizerMassFlow_kgs ?? double.NaN;
        bool   npshFeasible     = bestGen?.Turbopump?.NPSHFeasible ?? true;

        // BENCH stdout block — annotated with mode + chain count for diff-friendly grep.
        Console.WriteLine($"BENCH preset={preset.Name}");
        Console.WriteLine($"BENCH seed={repSeed.ToString(c)}");
        Console.WriteLine($"BENCH mode=multi-chain");
        Console.WriteLine($"BENCH chains={chainCount.ToString(c)}");
        Console.WriteLine($"BENCH iterations_completed={result.TotalIterations.ToString(c)}");
        Console.WriteLine($"BENCH iterations_max={(iterations * chainCount).ToString(c)}");
        Console.WriteLine($"BENCH winning_chain={result.WinningChain.ToString(c)}");
        Console.WriteLine($"BENCH feasible_count={feasibleCount.ToString(c)}");
        Console.WriteLine($"BENCH infeasible_count={infeasibleCount.ToString(c)}");
        Console.WriteLine($"BENCH first_feasible_iter={firstFeasibleIter.ToString(c)}");
        Console.WriteLine($"BENCH per_iter_p50_us={p50.ToString("F1", c)}");
        Console.WriteLine($"BENCH per_iter_p90_us={p90.ToString("F1", c)}");
        Console.WriteLine($"BENCH per_iter_p99_us={p99.ToString("F1", c)}");
        Console.WriteLine($"BENCH per_iter_mean_us={mean_us.ToString("F1", c)}");
        Console.WriteLine($"BENCH per_iter_stdev_us={stdev_us.ToString("F1", c)}");
        Console.WriteLine($"BENCH per_iter_cv={cv.ToString("F3", c)}");
        Console.WriteLine($"BENCH wall_total_ms={totalWall_ms.ToString("F1", c)}");
        Console.WriteLine($"BENCH best_total_score={bestTotal.ToString("F2", c)}");
        Console.WriteLine($"BENCH golden_peak_wall_t_k={peakWallT_K.ToString("F1", c)}");
        Console.WriteLine($"BENCH golden_coolant_dp_pa={coolantDP_Pa.ToString("F0", c)}");
        Console.WriteLine($"BENCH golden_coolant_t_out_k={coolantTOut_K.ToString("F1", c)}");
        Console.WriteLine($"BENCH golden_throat_heat_flux_wm2={throatHeatFlux_Wm2.ToString("F0", c)}");
        Console.WriteLine($"BENCH golden_mass_g={mass_g.ToString("F1", c)}");
        Console.WriteLine($"BENCH golden_min_safety_factor={minSafetyFactor.ToString("F3", c)}");
        Console.WriteLine($"BENCH golden_fuel_mass_flow_kgs={Fmt(fuelMassFlow_kgs)}");
        Console.WriteLine($"BENCH golden_ox_mass_flow_kgs={Fmt(oxMassFlow_kgs)}");
        Console.WriteLine($"BENCH golden_npsh_feasible={(npshFeasible ? 1 : 0)}");

        // JSONL record (mode + chain count fields added for downstream diff).
        JsonlSchema.AppendRecord(outPath, JsonlSchema.BenchNames.BenchSa, sb =>
        {
            sb.Append("\"preset\":\"").Append(preset.Name).Append("\",");
            sb.Append("\"seed\":").Append(repSeed.ToString(c)).Append(',');
            sb.Append("\"mode\":\"multi-chain\",");
            sb.Append("\"chains\":").Append(chainCount.ToString(c)).Append(',');
            sb.Append("\"winning_chain\":").Append(result.WinningChain.ToString(c)).Append(',');
            sb.Append("\"iterations_max\":").Append((iterations * chainCount).ToString(c)).Append(',');
            sb.Append("\"iterations_completed\":").Append(result.TotalIterations.ToString(c)).Append(',');
            sb.Append("\"feasible_count\":").Append(feasibleCount.ToString(c)).Append(',');
            sb.Append("\"infeasible_count\":").Append(infeasibleCount.ToString(c)).Append(',');
            sb.Append("\"first_feasible_iter\":").Append(firstFeasibleIter.ToString(c)).Append(',');
            sb.Append("\"per_iter_p50_us\":").Append(p50.ToString("F1", c)).Append(',');
            sb.Append("\"per_iter_p90_us\":").Append(p90.ToString("F1", c)).Append(',');
            sb.Append("\"per_iter_p99_us\":").Append(p99.ToString("F1", c)).Append(',');
            sb.Append("\"per_iter_mean_us\":").Append(mean_us.ToString("F1", c)).Append(',');
            sb.Append("\"per_iter_stdev_us\":").Append(stdev_us.ToString("F1", c)).Append(',');
            sb.Append("\"per_iter_cv\":").Append(cv.ToString("F3", c)).Append(',');
            sb.Append("\"wall_total_ms\":").Append(totalWall_ms.ToString("F1", c)).Append(',');
            sb.Append("\"best_total_score\":").Append(bestTotal.ToString("F2", c)).Append(',');
            sb.Append("\"peak_wall_t_k\":").Append(peakWallT_K.ToString("F1", c)).Append(',');
            sb.Append("\"wall_t_margin_k\":").Append(wallTMargin_K.ToString("F1", c)).Append(',');
            sb.Append("\"coolant_dp_pa\":").Append(coolantDP_Pa.ToString("F0", c)).Append(',');
            sb.Append("\"coolant_dp_fraction\":").Append(coolantDP_fraction.ToString("F4", c)).Append(',');
            sb.Append("\"coolant_t_out_k\":").Append(coolantTOut_K.ToString("F1", c)).Append(',');
            sb.Append("\"total_heat_load_w\":").Append(totalHeatLoad_W.ToString("F0", c)).Append(',');
            sb.Append("\"throat_heat_flux_wm2\":").Append(throatHeatFlux_Wm2.ToString("F0", c)).Append(',');
            sb.Append("\"mass_g\":").Append(mass_g.ToString("F1", c)).Append(',');
            sb.Append("\"min_safety_factor\":").Append(minSafetyFactor.ToString("F3", c)).Append(',');
            sb.Append("\"wall_t_exceeded\":").Append(wallTExceeded ? "true" : "false").Append(',');
            sb.Append("\"infeasible_feature\":").Append(infeasibleFeature ? "true" : "false").Append(',');
            sb.Append("\"fuel_mass_flow_kgs\":").Append(Fmt(fuelMassFlow_kgs)).Append(',');
            sb.Append("\"ox_mass_flow_kgs\":").Append(Fmt(oxMassFlow_kgs)).Append(',');
            sb.Append("\"npsh_feasible\":").Append(npshFeasible ? "true" : "false");
        });

        Console.WriteLine($"# multi-chain repeat done; {result.TotalIterations} iters across {chainCount} chains; "
                        + $"{totalWall_ms:F1} ms wall; best score {bestTotal:F2} (chain {result.WinningChain}); "
                        + $"first feasible at iter {firstFeasibleIter}");
    }
}
