// BenchSa.SingleChain.cs — single-chain SA runner for --bench-sa.
// Extracted from BenchSa.cs (BB-1 Wave 1 decomposition).
// RunOne drives a single SimulatedAnnealingOptimizer instance and
// emits a schema-v1 JSONL record when complete.

using System.Diagnostics;
using System.Globalization;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static partial class BenchSA
{
    private static void RunOne(CanonicalDesigns.Preset preset,
                               RegenGenerationResult seedGen, RegenScoreResult seedScore,
                               double[] baselineParams,
                               int repSeed, int iterations, bool disableInfeasibleExit, string outPath,
                               SaAnimationCapture? saAnim = null)
    {
        var c = CultureInfo.InvariantCulture;
        var opt = new SimulatedAnnealingOptimizer(
            RegenChamberOptimization.Bounds,
            iterations,
            repSeed);
        opt.SetInitialCandidate(baselineParams);
        if (disableInfeasibleExit) opt.MaxConsecutiveInfeasibleBeforeExit = 0;

        var iterTimes_us = new List<double>(iterations);
        long swStart = Stopwatch.GetTimestamp();
        int feasibleCount = 0;
        int infeasibleCount = 0;
        int firstFeasibleIter = -1;

        // Hold onto the score for the best-FEASIBLE-so-far. If SA finds
        // a feasible candidate (rare under pre-cascade gate calibration)
        // we cache it here; otherwise the seed score IS the fingerprint
        // (pre-cascade reference value, even if TotalScore is +∞).
        RegenScoreResult? bestFeasibleScore = null;

        while (!opt.IsComplete)
        {
            long t0 = Stopwatch.GetTimestamp();
            var cand = opt.NextCandidate();
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
            catch (Exception ex)
            {
                Console.WriteLine($"# iter {opt.Iteration}: GenerateWith threw ({ex.GetType().Name}: {ex.Message}); marking infeasible");
                s = MakeInfeasibleScore();
            }

            RecordViolations(s);
            DumpTrace(cand, s, g);
            bool newBest = opt.ReportScore(cand, s.TotalScore, s);
            if (newBest && double.IsFinite(s.TotalScore)) bestFeasibleScore = s;

            // OA-1 (#287): offer to the animation capture. OfferFrame
            // gates internally on score-finite + better-than-best, so
            // calling unconditionally is fine. d is the SA-Unpacked
            // RegenChamberDesign for this candidate; immutable record,
            // safe to retain by reference. Same for preset.Seed.Conditions.
            saAnim?.OfferFrame(opt.Iteration, s.TotalScore, preset.Seed.Conditions, d);

            if (double.IsPositiveInfinity(s.TotalScore)) infeasibleCount++;
            else
            {
                feasibleCount++;
                if (firstFeasibleIter < 0) firstFeasibleIter = opt.Iteration;
            }

            long t1 = Stopwatch.GetTimestamp();
            iterTimes_us.Add((t1 - t0) * 1_000_000.0 / Stopwatch.Frequency);
        }

        long swEnd = Stopwatch.GetTimestamp();
        double totalWall_ms = (swEnd - swStart) * 1000.0 / Stopwatch.Frequency;

        // Fingerprint source: the SEED design's preflight score IS the
        // pre-cascade reference value. Even when TotalScore is +∞
        // (gates fire), the underlying physics scalars are real values
        // captured at AutoSeeder defaults. SA may improve on it (rare
        // under Sprint-29 gate calibration); if so, prefer the best
        // feasible candidate's score.
        var bestScore = bestFeasibleScore ?? seedScore;
        var bestGen   = bestFeasibleScore != null ? null : (RegenGenerationResult?)seedGen;

        // Per-iteration timing percentiles (microseconds).
        iterTimes_us.Sort();
        double pctl(double p) => iterTimes_us.Count == 0 ? 0
            : iterTimes_us[Math.Min(iterTimes_us.Count - 1,
                (int)Math.Floor(p * (iterTimes_us.Count - 1)))];
        double mean_us  = iterTimes_us.Count == 0 ? 0 : iterTimes_us.Average();
        double stdev_us = StdDev(iterTimes_us, mean_us);
        double cv       = mean_us > 0 ? stdev_us / mean_us : 0;
        double p50      = pctl(0.50), p90 = pctl(0.90), p99 = pctl(0.99);

        // Fingerprint scalars (NaN-safe — sentinel -1 if best was infeasible).
        double bestTotal = double.IsFinite(opt.BestScore) ? opt.BestScore : -1;
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
        // Mass flows and NPSH from the GenerationResult.
        double fuelMassFlow_kgs = bestGen?.Derived.FuelMassFlow_kgs ?? double.NaN;
        double oxMassFlow_kgs   = bestGen?.Derived.OxidizerMassFlow_kgs ?? double.NaN;
        bool   npshFeasible     = bestGen?.Turbopump?.NPSHFeasible ?? true; // pressure-fed: true (no pump)

        // BENCH stdout block — grep-friendly, mirrors RunRecord.EmitBench style.
        Console.WriteLine($"BENCH preset={preset.Name}");
        Console.WriteLine($"BENCH seed={repSeed.ToString(c)}");
        Console.WriteLine($"BENCH mode=single-chain");
        Console.WriteLine($"BENCH first_feasible_iter={firstFeasibleIter.ToString(c)}");
        Console.WriteLine($"BENCH iterations_completed={opt.Iteration.ToString(c)}");
        Console.WriteLine($"BENCH iterations_max={iterations.ToString(c)}");
        Console.WriteLine($"BENCH restarts={opt.RestartCount.ToString(c)}");
        Console.WriteLine($"BENCH convergence_reached={opt.ConvergenceReached.ToString().ToLowerInvariant()}");
        Console.WriteLine($"BENCH infeasible_exit={opt.InfeasibleExitTripped.ToString().ToLowerInvariant()}");
        Console.WriteLine($"BENCH feasible_count={feasibleCount.ToString(c)}");
        Console.WriteLine($"BENCH infeasible_count={infeasibleCount.ToString(c)}");
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

        // JSONL record via the schema-v1 emitter.
        JsonlSchema.AppendRecord(outPath, JsonlSchema.BenchNames.BenchSa, sb =>
        {
            sb.Append("\"preset\":\"").Append(preset.Name).Append("\",");
            sb.Append("\"seed\":").Append(repSeed.ToString(c)).Append(',');
            sb.Append("\"iterations_max\":").Append(iterations.ToString(c)).Append(',');
            sb.Append("\"iterations_completed\":").Append(opt.Iteration.ToString(c)).Append(',');
            sb.Append("\"restarts\":").Append(opt.RestartCount.ToString(c)).Append(',');
            sb.Append("\"convergence_reached\":").Append(opt.ConvergenceReached ? "true" : "false").Append(',');
            sb.Append("\"infeasible_exit\":").Append(opt.InfeasibleExitTripped ? "true" : "false").Append(',');
            sb.Append("\"feasible_count\":").Append(feasibleCount.ToString(c)).Append(',');
            sb.Append("\"infeasible_count\":").Append(infeasibleCount.ToString(c)).Append(',');
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

        Console.WriteLine($"# repeat {repSeed - (repSeed % 1)} done; {opt.Iteration} iters; {totalWall_ms:F1} ms wall; best score {bestTotal:F2}");
    }
}
