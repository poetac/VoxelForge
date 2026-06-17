// Program.Nsga.cs — NSGA-II session methods (T2.4b, 2026-04-30).
//
// Extracted from Program.cs (Sprint 0 / Wave 1, 2026-05-05) as a
// partial-class slice. Behavior is unchanged — the three methods below
// previously lived inline.

using System;
using System.Linq;
using Voxelforge.Optimization;
using Voxelforge.UI;

namespace Voxelforge;

public static partial class Program
{
    private static AppOptimization.NsgaIISession? TryStartNsgaOpt(OptSettings s)
    {
        try
        {
            var profile = RegenChamberOptimization.Profiles[s.ProfileIndex];
            _pareto.Clear();

            var objective = new Optimization.RegenObjective(
                conditions:        s.Conditions,
                baseline:          s.BaselineDesign,
                profile:           profile,
                skipVoxelGeometry: true,
                skipMfgAnalysis:   true);

            // Three-axis objective vector: minimize peak wall T, coolant ΔP, mass.
            double[] ExtractObjectives(Optimization.EvaluationResult eval)
            {
                if (eval.EngineSpecificBreakdown is RegenScoreResult brk)
                    return new[] { brk.PeakWallT_K, brk.CoolantDP_Pa, brk.Mass_g };
                return new[] { double.MaxValue, double.MaxValue, double.MaxValue };
            }

            // Fan every feasible candidate to the shared Pareto front.
            void OnCandidateScored(double[] _, double score, object? breakdown)
            {
                if (breakdown is RegenScoreResult brk && double.IsFinite(score))
                {
                    _pareto.Offer(new Optimization.ParetoPoint(
                        PeakWallT_K:  brk.PeakWallT_K,
                        CoolantDP_Pa: brk.CoolantDP_Pa,
                        Mass_g:       brk.Mass_g,
                        Parameters:   Array.Empty<double>(),
                        Iteration:    0));
                }
            }

            int popSize = s.NsgaPopulationSize;
            if (popSize % 2 != 0) popSize++;   // NSGA-II requires even population

            var session = new AppOptimization.NsgaIISession(
                objective:          objective,
                objectiveExtractor: ExtractObjectives,
                populationSize:     popSize,
                maxGenerations:     s.NsgaMaxGenerations,
                seed:               s.Seed,
                onCandidateScored:  OnCandidateScored);
            session.Start();

            SharedState.WriteOptProgress(new OptProgress
            {
                IsRunning     = true,
                Iteration     = 0,
                MaxIterations = (int)session.TotalExpectedEvaluations,
            });

            SetFormStatus($"NSGA-II started: pop={popSize}, gens={s.NsgaMaxGenerations}, seed={s.Seed}, "
                        + $"profile={profile.Name}");
            return session;
        }
        catch (Exception ex)
        {
            SetFormStatus("NSGA-II start error: " + ex.Message);
            return null;
        }
    }

    private static void PollNsgaProgress(AppOptimization.NsgaIISession session, OptSettings s)
    {
        try
        {
            var snap = session.ReadSnapshot();
            SharedState.WriteOptProgress(new OptProgress
            {
                IsRunning     = true,
                Iteration     = (int)Math.Min(snap.EvaluationsCompleted, int.MaxValue),
                MaxIterations = (int)Math.Min(snap.TotalExpectedEvaluations, int.MaxValue),
                BestScore     = snap.BestScore,
                Temperature   = 0,
                RestartCount  = 0,
                BestParams    = snap.BestParams,
                BestBreakdown = snap.BestBreakdown as RegenScoreResult,
            });
        }
        catch (Exception ex)
        {
            PicoGK.Library.Log($"NSGA-II progress poll error: {ex.Message}");
        }
    }

    private static void FinalizeNsgaOpt(AppOptimization.NsgaIISession session, OptSettings s)
    {
        try
        {
            var result = session.AwaitResult();

            // Find the feasible Pareto individual with the lowest scalar score
            // as the "best" design to display in the viewer (same convention as SA).
            Optimization.NsgaIIOptimizer.Individual? best = null;
            double bestScore = double.PositiveInfinity;
            foreach (var ind in result.ParetoFront)
            {
                if (ind.IsFeasible && ind.Evaluation is { } ev && ev.Score < bestScore)
                {
                    bestScore = ev.Score;
                    best = ind;
                }
            }

            if (best is null)
            {
                // No feasible individual — display a status note and preserve prior result.
                int feasCount = result.ParetoFront.Count(i => i.IsFeasible);
                SetFormStatus($"NSGA-II done in {result.ElapsedMilliseconds} ms. "
                            + $"No feasible Pareto individual found ({feasCount}/{result.ParetoFront.Count} feasible). "
                            + "Relax constraints or increase MaxGenerations / PopulationSize.");
                SharedState.WriteOptProgress(new OptProgress { IsRunning = false });
                return;
            }

            // Promote Pareto individuals to the shared front with their vectors.
            foreach (var ind in result.ParetoFront)
            {
                if (ind.IsFeasible && ind.Evaluation?.EngineSpecificBreakdown is RegenScoreResult brk)
                {
                    _pareto.Offer(new Optimization.ParetoPoint(
                        PeakWallT_K:  brk.PeakWallT_K,
                        CoolantDP_Pa: brk.CoolantDP_Pa,
                        Mass_g:       brk.Mass_g,
                        Parameters:   (double[])ind.Vector.Clone(),
                        Iteration:    result.GenerationsCompleted));
                }
            }

            // Regenerate the best-scalar-score individual at full fidelity.
            var bestDesign = RegenChamberOptimization.Unpack(best.Vector, s.BaselineDesign);
            var voxelGen = new Voxelforge.Geometry.ChamberVoxelBuilderAdapter();
            var gen = UI.ResourceBudget.AutoCoarsenVoxelToFitBudget
                ? RegenChamberOptimization.GenerateWithAutoCoarsen(s.Conditions, bestDesign, voxelSize_mm: VoxelSizeMM,
                    onVoxelSubstituted: (prev, now, _) =>
                        SetFormStatus($"Best-design voxel auto-coarsened {prev:F2} → {now:F2} mm to fit memory budget; rendering…"),
                    voxelGenerator: voxelGen)
                : RegenChamberOptimization.GenerateWith(s.Conditions, bestDesign, voxelSize_mm: VoxelSizeMM,
                    voxelGenerator: voxelGen);
            var score = RegenChamberOptimization.Evaluate(
                gen, RegenChamberOptimization.Profiles[s.ProfileIndex]);
            _lastResult = gen;
            _lastDesign = bestDesign;
            _lastScore = score;
            _lastResultBestSoFarIter = 0;
            UpdateViewer(gen);
            UpdateFormResults(gen, score);

            var form = _form;
            if (form != null && !form.IsDisposed)
            {
                try { form.BeginInvoke(() => form.ApplyOptResult(best.Vector)); }
                catch { }
            }

            SharedState.WriteOptProgress(new OptProgress
            {
                IsRunning     = false,
                Iteration     = (int)result.TotalEvaluations,
                MaxIterations = (int)session.TotalExpectedEvaluations,
                BestScore     = bestScore,
                BestParams    = (double[])best.Vector.Clone(),
                BestBreakdown = best.Evaluation?.EngineSpecificBreakdown as RegenScoreResult,
            });

            _lastParetoFrontSnapshot = _pareto.Points;
            {
                var pf = _form;
                var snap = _lastParetoFrontSnapshot;
                if (pf != null && !pf.IsDisposed)
                {
                    try { pf.BeginInvoke(() => pf.ApplyParetoFront(snap)); }
                    catch { }
                }
            }

            SetFormStatus($"NSGA-II done in {result.ElapsedMilliseconds} ms. "
                        + $"{result.GenerationsCompleted} gens × {s.NsgaPopulationSize} pop = "
                        + $"{result.TotalEvaluations} evals. "
                        + $"Pareto front: {result.ParetoFront.Count(i => i.IsFeasible)} feasible / "
                        + $"{result.ParetoFront.Count} total. "
                        + $"Best scalar score: {bestScore:F2}.");

            if (_pendingBatchSettings is { } batch)
            {
                try
                {
                    if (batch.SaveParetoCsv && _pareto.Count > 0)
                    {
                        var folder = batch.OutputFolder;
                        string prefix = _batchTimestampPrefix ?? "nsga";
                        string path = System.IO.Path.Combine(folder, $"{prefix}_pareto.csv");
                        Optimization.ParetoFront.SaveToCsv(path, _pareto.Points);
                        PicoGK.Library.Log($"Pareto CSV saved to {path}");
                    }
                }
                catch (Exception ex) { SetFormStatus("Batch Pareto CSV save error: " + ex.Message); }
                finally
                {
                    _pendingBatchSettings = null;
                    _batchTimestampPrefix = null;
                }
            }
        }
        catch (Analysis.MemoryBudgetExceededException mem)
        {
            SetFormStatus(
                $"NSGA-II done but best-design voxel build exceeded the memory budget. "
              + $"Coarsen voxel to ≥ {mem.SuggestedVoxel_mm:F2} mm and re-run Generate to render.");
        }
        catch (OperationCanceledException)
        {
            SetFormStatus("NSGA-II cancelled by user.");
        }
        catch (Exception ex)
        {
            SetFormStatus("NSGA-II finalize error: " + ex.Message);
        }
    }
}
