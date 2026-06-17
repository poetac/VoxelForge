// Program.Sa.cs — single-chain + multi-chain SA orchestration.
//
// Extracted from Program.cs (Sprint 0 / Wave 1, 2026-05-05) as a
// partial-class slice. Behavior is unchanged. The single-chain
// (1+λ) parallel-batch path lives in TryStartOpt / StepOpt /
// FinalizeOpt; the multi-chain path lives in TryStartMultiChainOpt /
// PollMultiChainProgress / FinalizeMultiChainOpt. Batch-output
// helpers (WriteBatchOutputs, WriteBatchOutputsMultiChain) and the
// shared MakeInfeasibleScore / PerturbParams helpers live here too —
// they are exclusively SA-internal.

using System;
using System.IO;
using PicoGK;
using Voxelforge.Geometry;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.UI;

namespace Voxelforge;

public static partial class Program
{
    // ═════════════════════════════════════════════════════════════════════
    //   Optimization (task thread)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// "Large thrust" threshold above which the solver
    /// auto-arms a 30-minute op time budget if the user has left
    /// <see cref="UI.ResourceBudget.OptTimeoutSeconds"/> at 0 (=no cap).
    /// Prevents a runaway SA on a design that may never find feasible
    /// candidates — e.g. a 50 kN chamber with default voxel at fine
    /// granularity — from burning hours before the user notices.
    /// </summary>
    private const double LargeThrustAutoTimeoutThreshold_N = 10000.0;

    /// <summary>Auto-armed timeout when the user has no explicit value.</summary>
    private const int LargeThrustAutoTimeoutSeconds = 1800;   // 30 min

    private static SimulatedAnnealingOptimizer? TryStartOpt(OptSettings s)
    {
        try
        {
            var profile = RegenChamberOptimization.Profiles[s.ProfileIndex];

            // PHASE 6: reset the Pareto front at the start of every run so
            // the tracked trade-off surface is always about the current run.
            _pareto.Clear();

            // Pre-flight feasibility sanity check. Run ONE
            // physics-only evaluation on the baseline design before SA starts;
            // if it returns +∞ (every gate failed), surface a warning so the
            // user doesn't burn 5 hours hunting feasible points that don't
            // exist at this thrust + chamber-pressure + material combo.
            // Advisory only — we still start SA so a user who knows better
            // (e.g. expects perturbations to escape the infeasible region)
            // can proceed without a blocking dialog.
            try
            {
                var baselineGen = RegenChamberOptimization.GenerateWith(
                    s.Conditions, s.BaselineDesign,
                    skipVoxelGeometry: true, skipMfgAnalysis: true);
                var baselineScore = RegenChamberOptimization.Evaluate(baselineGen, profile);
                if (double.IsPositiveInfinity(baselineScore.TotalScore)
                    || double.IsNaN(baselineScore.TotalScore))
                {
                    SetFormStatus(
                        "Warning: baseline design is infeasible (every candidate may return +∞). "
                      + "SA will exit on the persistent-infeasibility streak (~1-2 min) if no "
                      + "feasible point is found. Consider relaxing thrust, chamber pressure, "
                      + "or wall material before starting.");
                }
            }
            catch (Analysis.MemoryBudgetExceededException) { /* handled downstream */ }
            catch (System.Exception ex)
            {
                Library.Log($"Pre-flight feasibility check skipped: {ex.Message}");
            }

            // Auto-arm a generous opt timeout when thrust
            // is large AND the user has no explicit timeout set. 30 min is
            // plenty of headroom for an 8-way batch on a 100 kN design at
            // 0.4 mm voxel, but much shorter than the 5-hour runaway the
            // original failure report described.
            if (UI.ResourceBudget.OptTimeoutSeconds == 0
                && s.Conditions.Thrust_N > LargeThrustAutoTimeoutThreshold_N)
            {
                TryArmCurrentOpTimeout(System.TimeSpan.FromSeconds(LargeThrustAutoTimeoutSeconds));
                SetFormStatus(
                    $"Large-thrust auto-safeguard: opt time budget set to "
                  + $"{LargeThrustAutoTimeoutSeconds / 60} min. "
                  + $"Override in Resource Budget → Opt timeout.");
            }

            // IEngine Phase 2 (ADR-025): per-candidate evaluation in StepOpt's
            // parallel batch loop routes through this objective rather than
            // calling GenerateWith directly.
            _singleChainObjective = new Optimization.RegenObjective(
                conditions:        s.Conditions,
                baseline:          s.BaselineDesign,
                profile:           profile,
                skipVoxelGeometry: true,
                skipMfgAnalysis:   true);

            var opt = new SimulatedAnnealingOptimizer(
                RegenChamberOptimization.Bounds,
                s.MaxIterations,
                s.Seed);

            if (s.WarmStart && s.WarmStartParams != null)
                opt.SetInitialCandidate(s.WarmStartParams);

            SharedState.WriteOptProgress(new OptProgress
            {
                IsRunning = true,
                Iteration = 0,
                MaxIterations = s.MaxIterations,
            });

            SetFormStatus($"Optimization started: {s.MaxIterations} iters, profile = {profile.Name}");
            return opt;
        }
        catch (Exception ex)
        {
            SetFormStatus("Opt start error: " + ex.Message);
            return null;
        }
    }

    private static void StepOpt(SimulatedAnnealingOptimizer opt, OptSettings s)
    {
        try
        {
            var cand = opt.NextCandidate();

            // TIER A.2: parallel-SA fast path. Generate `batch-1` extra
            // candidate perturbations around `cand`, evaluate all of them in
            // parallel using the physics-only path (no voxel build), and
            // feed the best back to SA. Effective (1 + λ) evolution
            // strategy layered on top of annealing.
            int batchSize = Math.Max(1, s.ParallelBatchSize);
            double[][] batch;
            if (batchSize > 1)
            {
                batch = new double[batchSize][];
                batch[0] = cand;
                var rng = new Random(opt.Iteration * 7919 + s.Seed);
                for (int k = 1; k < batchSize; k++)
                    batch[k] = PerturbParams(cand, rng, 0.05);
            }
            else
            {
                batch = new[] { cand };
            }

            var batchGens   = new RegenGenerationResult[batch.Length];
            var batchScores = new RegenScoreResult[batch.Length];
            bool parallel = batchSize > 1;
            if (parallel)
            {
                // Honour user's resource budget +
                // cancellation token. MaxDegreeOfParallelism limits how
                // many SA candidates race in parallel; ResourceBudget
                // resolves it from the active preset. The CTS lives
                // in _currentOpCts, cancelled by the Stop button.
                var parallelOpts = new System.Threading.Tasks.ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1,
                        Math.Min(batch.Length,
                                 Voxelforge.UI.ResourceBudget.MaxParallelism)),
                    CancellationToken = CurrentOpToken(),
                };
                // Per-worker try/catch: a single pathological perturbation
                // (contour clamp, zero-width channel, propellant-table edge)
                // must NOT kill the whole batch. Mark that slot with +∞ so
                // SA rejects it and we keep the other N-1 candidates.
                try
                {
                    System.Threading.Tasks.Parallel.For(0, batch.Length, parallelOpts, i =>
                    {
                        try
                        {
                            // IEngine Phase 2 (ADR-025): route through IObjective.
                            // RegenObjective.Evaluate folds the T1.5 pre-screen
                            // short-circuit and the GenerateWith + Evaluate pair into
                            // a single call — no per-slot Unpack + FeasibilityGate
                            // + GenerateWith needed here.
                            var evalResult = _singleChainObjective!.Evaluate(
                                batch[i], parallelOpts.CancellationToken);
                            batchScores[i] = evalResult.EngineSpecificBreakdown as RegenScoreResult
                                ?? MakeInfeasibleScore(
                                    preScreenViolation: evalResult.Violations.Count > 0
                                        ? evalResult.Violations[0] : null);
                        }
                        catch (Exception ex)
                        {
                            Library.Log($"Batch eval slot {i} failed: {ex.Message}");
                            batchScores[i] = MakeInfeasibleScore(exceptionReason: ex.Message);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    // Stop button pressed mid-batch — unwind quietly, Step
                    // returns and the outer main loop observes the Stop flag.
                    Library.Log("SA batch cancelled by user.");
                    return;
                }
            }
            else
            {
                var d = RegenChamberOptimization.Unpack(batch[0], s.BaselineDesign);
                batchGens[0]   = RegenChamberOptimization.GenerateWith(
                    s.Conditions, d, voxelSize_mm: VoxelSizeMM,
                    voxelGenerator: new Voxelforge.Geometry.ChamberVoxelBuilderAdapter());
                batchScores[0] = RegenChamberOptimization.Evaluate(
                    batchGens[0], RegenChamberOptimization.Profiles[s.ProfileIndex]);
            }

            // Pick best-of-batch by total score (lower = better).
            int bestIdx = 0;
            for (int i = 1; i < batch.Length; i++)
                if (batchScores[i].TotalScore < batchScores[bestIdx].TotalScore) bestIdx = i;

            var bestCand  = batch[bestIdx];
            var bestScore = batchScores[bestIdx];
            // In the parallel path batchGens is never populated; bestGen is re-built
            // from the physics-only score if the candidate is a new best (see below).
            // In the non-parallel path batchGens[0] carries the full voxel geometry.
            var bestGen   = parallel ? null : batchGens[bestIdx];

            bool newBest = opt.ReportScore(bestCand, bestScore.TotalScore, bestScore);

            // PHASE 6: offer every feasible candidate in the batch to the
            // Pareto front (not just the per-batch winner — the front should
            // reflect the entire search).
            for (int i = 0; i < batch.Length; i++)
            {
                if (double.IsFinite(batchScores[i].TotalScore))
                {
                    _pareto.Offer(new Optimization.ParetoPoint(
                        PeakWallT_K:  batchScores[i].PeakWallT_K,
                        CoolantDP_Pa: batchScores[i].CoolantDP_Pa,
                        Mass_g:       batchScores[i].Mass_g,
                        Parameters:   (double[])batch[i].Clone(),
                        Iteration:    opt.Iteration));
                }
            }

            if (newBest)
            {
                // TIER A.2: when in parallel mode the best candidate was
                // scored from a physics-only pass (no Voxels). Re-run the
                // full pipeline now — only on the winner — so the viewer
                // and downstream exports have a proper geometry.
                var bestDesign = RegenChamberOptimization.Unpack(bestCand, s.BaselineDesign);
                if (parallel)
                {
                    bestGen   = RegenChamberOptimization.GenerateWith(
                        s.Conditions, bestDesign, voxelSize_mm: VoxelSizeMM,
                        voxelGenerator: new Voxelforge.Geometry.ChamberVoxelBuilderAdapter());
                    bestScore = RegenChamberOptimization.Evaluate(
                        bestGen, RegenChamberOptimization.Profiles[s.ProfileIndex]);
                }
                _lastResult = bestGen;
                _lastDesign = bestDesign;
                _lastScore  = bestScore;
                _lastResultBestSoFarIter = opt.Iteration;
                UpdateViewer(bestGen!);
                UpdateFormResults(bestGen!, bestScore);
            }

            SharedState.WriteOptProgress(new OptProgress
            {
                IsRunning = true,
                Iteration = opt.Iteration,
                MaxIterations = opt.MaxIterations,
                BestScore = opt.BestScore,
                Temperature = opt.Temperature,
                RestartCount = opt.RestartCount,
                BestParams = opt.BestParams,
                BestBreakdown = opt.BestBreakdown as RegenScoreResult,
            });
        }
        catch (Voxelforge.Analysis.MemoryBudgetExceededException mem)
        {
            // A candidate voxel build exceeded
            // the memory budget — further iterations would OOM identically.
            // Signal the optimizer so IsComplete trips on the next check;
            // the main dispatch loop observes it and runs FinalizeOpt
            // without re-entering StepOpt. Surface the suggested voxel
            // size the gate computed, so the user has an actionable next step.
            opt.SignalMemoryAbort();
            Library.Log($"SA memory abort: {mem.Message}");
            SetFormStatus(
                $"Optimization stopped — voxel build exceeds memory budget. "
              + $"Coarsen voxel to ≥ {mem.SuggestedVoxel_mm:F2} mm "
              + $"(requested {mem.RequestedVoxel_mm:F2} mm) or raise the Resource Budget cap.");
            return;
        }
        catch (Exception ex)
        {
            // Don't kill the optimizer on transient eval failure.
            Library.Log($"Opt step error (continuing): {ex.Message}");
        }
    }

    private static void WriteBatchOutputs(
        BatchRunSettings batch, SimulatedAnnealingOptimizer opt, ScoringProfile profile)
    {
        if (_lastResult == null || _lastDesign == null) return;
        string folder = batch.OutputFolder;
        string prefix = _batchTimestampPrefix ?? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        int written = 0;

        // 1. Design JSON (conditions + full design + last-result summary).
        if (batch.SaveDesignJson)
        {
            try
            {
                string path = Path.Combine(folder, $"{prefix}_design.rcd.json");
                DesignPersistence.Save(path, _lastResult.Conditions, _lastDesign, _lastResult);
                written++;
            }
            catch (Exception ex) { Library.Log($"Batch save JSON failed: {ex.Message}"); }
        }

        // 2. STL at session voxel (same-voxel in-process path — fast).
        if (batch.SaveStl)
        {
            try
            {
                string path = Path.Combine(folder, $"{prefix}_chamber.stl");
                ChamberVoxelBuilder.ExportStl(_lastResult.Geometry.Voxels.AsPicoGK(), path);
                written++;
            }
            catch (Exception ex) { Library.Log($"Batch save STL failed: {ex.Message}"); }
        }

        // 3. Text report (includes Pareto front when non-empty).
        if (batch.SaveReport)
        {
            try
            {
                string path = Path.Combine(folder, $"{prefix}_report.txt");
                ReportExport.SaveToFile(_lastResult, path, bestSoFarIteration: 0, _pareto.Points);
                written++;
            }
            catch (Exception ex) { Library.Log($"Batch save report failed: {ex.Message}"); }
        }

        // 4. Pareto front as CSV for scripting / external plotting.
        if (batch.SaveParetoCsv && _pareto.Count > 0)
        {
            try
            {
                string path = Path.Combine(folder, $"{prefix}_pareto.csv");
                Optimization.ParetoFront.SaveToCsv(path, _pareto.Points);
                written++;
            }
            catch (Exception ex) { Library.Log($"Batch save Pareto CSV failed: {ex.Message}"); }
        }

        // 5. Always write a short summary so the user can tell the run
        //    actually happened even if the optional artefacts failed.
        try
        {
            string summaryPath = Path.Combine(folder, $"{prefix}_summary.txt");
            var sb = new System.Text.StringBuilder(1024);
            sb.AppendLine("Voxelforge — batch run summary");
            sb.AppendLine($"Timestamp:      {DateTime.Now:u}");
            sb.AppendLine($"Iterations ran: {opt.Iteration} / {opt.MaxIterations}");
            sb.AppendLine($"Restarts:       {opt.RestartCount}");
            sb.AppendLine($"Best score:     {opt.BestScore:F4}");
            sb.AppendLine($"Profile:        {profile.Name}");
            sb.AppendLine($"Propellant:     {_lastResult.Conditions.PropellantPair}");
            sb.AppendLine($"Thrust (N):     {_lastResult.Conditions.Thrust_N:F0}");
            sb.AppendLine($"Pc (MPa):       {_lastResult.Conditions.ChamberPressure_Pa / 1e6:F2}");
            sb.AppendLine($"Design hash:    {_lastResult.DesignHash}");
            sb.AppendLine($"Pareto points:  {_pareto.Count}");
            sb.AppendLine($"Files written:  {written + 1}");   // +1 for this summary
            File.WriteAllText(summaryPath, sb.ToString());
            written++;
        }
        catch (Exception ex) { Library.Log($"Batch summary failed: {ex.Message}"); }

        SetFormStatus($"Batch run complete — {written} file(s) in {folder} (prefix {prefix}).");
    }

    // Z1.5 hot-fix (2026-04-28): callers can now distinguish three
    // infeasibility origins via the warning text + populated violation list:
    //   • pre-screen reject  → preScreenViolation set (cheap-gate diagnostic)
    //   • exception throw    → exceptionReason set (Library.Log already logs)
    //   • generic / legacy   → both null → preserves the original
    //                          "[INFEASIBLE] batch eval threw" string for
    //                          back-compat with anything pattern-matching it.
    private static RegenScoreResult MakeInfeasibleScore(
        FeasibilityViolation? preScreenViolation = null,
        string? exceptionReason = null)
    {
        string warning;
        FeasibilityViolation[] violations;
        if (preScreenViolation is not null)
        {
            warning =
                $"[INFEASIBLE] pre-screen reject: {preScreenViolation.ConstraintId} — "
                + preScreenViolation.Description;
            violations = new[] { preScreenViolation };
        }
        else if (exceptionReason is not null)
        {
            warning = $"[INFEASIBLE] eval threw: {exceptionReason}";
            violations = Array.Empty<FeasibilityViolation>();
        }
        else
        {
            warning = "[INFEASIBLE] batch eval threw — slot skipped.";
            violations = Array.Empty<FeasibilityViolation>();
        }
        return new(
            TotalScore: double.PositiveInfinity,
            PeakWallT_K: 0, WallTMargin_K: 0,
            CoolantDP_Pa: 0, CoolantDP_Fraction: 0,
            CoolantTOut_K: 0, TotalHeatLoad_W: 0, ThroatHeatFlux_Wm2: 0,
            Mass_g: 0, Cost_USD: 0,
            MinFeatureSize_mm: 0, MinSafetyFactor: 0,
            WallTExceeded: true, YieldExceeded: true, InfeasibleFeature: true,
            Warnings: new[] { warning },
            FeasibilityViolations: violations);
    }

    private static double[] PerturbParams(double[] seed, Random rng, double stdevFraction)
    {
        var bounds = RegenChamberOptimization.Bounds;
        var p = new double[seed.Length];
        for (int i = 0; i < seed.Length; i++)
        {
            double range = bounds[i].Max - bounds[i].Min;
            // Box-Muller Gaussian.
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            double z = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            double val = seed[i] + z * stdevFraction * range;
            p[i] = Math.Clamp(val, bounds[i].Min, bounds[i].Max);
        }
        return p;
    }

    private static void FinalizeOpt(SimulatedAnnealingOptimizer opt, OptSettings s)
    {
        try
        {
            var profile = RegenChamberOptimization.Profiles[s.ProfileIndex];

            // Regenerate best design at full fidelity for display + apply.
            var bestDesign = RegenChamberOptimization.Unpack(opt.BestParams, s.BaselineDesign);
            var voxelGen2 = new Voxelforge.Geometry.ChamberVoxelBuilderAdapter();
            var gen = Voxelforge.UI.ResourceBudget.AutoCoarsenVoxelToFitBudget
                ? RegenChamberOptimization.GenerateWithAutoCoarsen(s.Conditions, bestDesign, voxelSize_mm: VoxelSizeMM,
                    onVoxelSubstituted: (prev, now, _) =>
                        SetFormStatus($"Best-design voxel auto-coarsened {prev:F2} → {now:F2} mm to fit memory budget; rendering…"),
                    voxelGenerator: voxelGen2)
                : RegenChamberOptimization.GenerateWith(s.Conditions, bestDesign, voxelSize_mm: VoxelSizeMM,
                    voxelGenerator: voxelGen2);
            var score = RegenChamberOptimization.Evaluate(gen, profile);
            _lastResult = gen;
            _lastDesign = bestDesign;
            _lastScore = score;
            _lastResultBestSoFarIter = 0;
            UpdateViewer(gen);
            UpdateFormResults(gen, score);

            var form = _form;
            if (form != null && !form.IsDisposed)
            {
                try { form.BeginInvoke(() => form.ApplyOptResult(opt.BestParams)); }
                catch { /* form closing */ }
            }

            SharedState.WriteOptProgress(new OptProgress
            {
                IsRunning = false,
                Iteration = opt.Iteration,
                MaxIterations = opt.MaxIterations,
                BestScore = opt.BestScore,
                Temperature = opt.Temperature,
                RestartCount = opt.RestartCount,
                BestParams = opt.BestParams,
                BestBreakdown = opt.BestBreakdown as RegenScoreResult,
            });

            _lastParetoFrontSnapshot = _pareto.Points;

            {
                var paretoForm = _form;
                var snapshot = _lastParetoFrontSnapshot;
                if (paretoForm != null && !paretoForm.IsDisposed)
                {
                    try { paretoForm.BeginInvoke(() => paretoForm.ApplyParetoFront(snapshot)); }
                    catch { /* form closing */ }
                }
            }

            var summary = Voxelforge.UI.ResourceProfiler.End("sa");
            string tail = summary.WallMs > 0 ? $"  |  {summary.Format()}" : "";

            SetFormStatus($"Optimization done. Best = {opt.BestScore:F2} @ iter {opt.Iteration}, "
                        + $"restarts = {opt.RestartCount}, Pareto front = {_pareto.Count} points." + tail);

            if (_pendingBatchSettings is { } batch)
            {
                try { WriteBatchOutputs(batch, opt, profile); }
                catch (Exception ex) { SetFormStatus("Batch save error: " + ex.Message); }
                finally
                {
                    _pendingBatchSettings = null;
                    _batchTimestampPrefix = null;
                }
            }
        }
        catch (Voxelforge.Analysis.MemoryBudgetExceededException mem)
        {
            SetFormStatus(
                $"Optimization done but best-design voxel build exceeded the memory budget. "
              + $"Coarsen voxel to ≥ {mem.SuggestedVoxel_mm:F2} mm and re-run Generate to render.");
        }
        catch (Exception ex) { SetFormStatus("Opt finalize error: " + ex.Message); }
    }

    // ═════════════════════════════════════════════════════════════════════
    //   Multi-chain SA orchestration (Sprint OPT-1 production wiring)
    // ═════════════════════════════════════════════════════════════════════

    private static AppOptimization.MultiChainSession? TryStartMultiChainOpt(OptSettings s)
    {
        try
        {
            var profile = RegenChamberOptimization.Profiles[s.ProfileIndex];
            _pareto.Clear();

            // Pre-flight feasibility check, same as TryStartOpt.
            try
            {
                var baselineGen = RegenChamberOptimization.GenerateWith(
                    s.Conditions, s.BaselineDesign,
                    skipVoxelGeometry: true, skipMfgAnalysis: true);
                var baselineScore = RegenChamberOptimization.Evaluate(baselineGen, profile);
                if (double.IsPositiveInfinity(baselineScore.TotalScore)
                    || double.IsNaN(baselineScore.TotalScore))
                {
                    SetFormStatus(
                        "Warning: baseline design is infeasible. Multi-chain SA will exit on the "
                      + "persistent-infeasibility streak (~1-2 min) if no feasible point is found.");
                }
            }
            catch (Analysis.MemoryBudgetExceededException) { }
            catch (Exception ex) { Library.Log($"Multi-chain pre-flight skipped: {ex.Message}"); }

            if (UI.ResourceBudget.OptTimeoutSeconds == 0
                && s.Conditions.Thrust_N > LargeThrustAutoTimeoutThreshold_N)
            {
                TryArmCurrentOpTimeout(TimeSpan.FromSeconds(LargeThrustAutoTimeoutSeconds));
                SetFormStatus(
                    $"Large-thrust auto-safeguard: opt time budget set to "
                  + $"{LargeThrustAutoTimeoutSeconds / 60} min.");
            }

            // Sprint 0 / Wave 1 (2026-05-05): per-candidate evaluation routes
            // through RegenObjective via MultiChainSession's IObjective ctor.
            // RegenObjective performs the pre-screen short-circuit + uses the
            // physics-only fast path (skipVoxelGeometry + skipMfgAnalysis).
            var settings = s;
            var objective = new Optimization.RegenObjective(
                conditions:        settings.Conditions,
                baseline:          settings.BaselineDesign,
                profile:           profile,
                skipVoxelGeometry: true,
                skipMfgAnalysis:   true);

            // Pareto offer for every feasible candidate. _pareto.Offer is
            // thread-safe (its underlying ConcurrentBag accepts concurrent adds).
            // The MultiChainSession IObjective ctor unwraps EvaluationResult
            // into the engine-specific breakdown (RegenScoreResult here)
            // before invoking this callback.
            void OnCandidateScored(double[] cand, double score, object? breakdown)
            {
                if (breakdown is RegenScoreResult brk && double.IsFinite(score))
                {
                    _pareto.Offer(new Optimization.ParetoPoint(
                        PeakWallT_K:  brk.PeakWallT_K,
                        CoolantDP_Pa: brk.CoolantDP_Pa,
                        Mass_g:       brk.Mass_g,
                        Parameters:   (double[])cand.Clone(),
                        Iteration:    0));
                }
            }

            int chainCount = s.MultiChainCount > 0
                ? s.MultiChainCount
                : MultiChainOptimizer.DefaultChainCount();

            var session = new AppOptimization.MultiChainSession(
                objective:           objective,
                maxIterations:       s.MaxIterations,
                baseSeed:            s.Seed,
                chainCount:          chainCount,
                onCandidateScored:   OnCandidateScored,
                initialCandidate:    s.WarmStart ? s.WarmStartParams : null);
            session.Start();

            SharedState.WriteOptProgress(new OptProgress
            {
                IsRunning = true,
                Iteration = 0,
                MaxIterations = s.MaxIterations * chainCount,
            });

            SetFormStatus($"Multi-chain SA started: {chainCount} chains × {s.MaxIterations} iters, "
                        + $"profile = {profile.Name}");
            return session;
        }
        catch (Exception ex)
        {
            SetFormStatus("Multi-chain opt start error: " + ex.Message);
            return null;
        }
    }

    private static void PollMultiChainProgress(AppOptimization.MultiChainSession session, OptSettings s)
    {
        try
        {
            var snap = session.ReadSnapshot();
            SharedState.WriteOptProgress(new OptProgress
            {
                IsRunning      = true,
                Iteration      = (int)Math.Min(snap.IterationsCounted, int.MaxValue),
                MaxIterations  = s.MaxIterations * session.ChainCount,
                BestScore      = snap.BestScore,
                Temperature    = 0,
                RestartCount   = 0,
                BestParams     = snap.BestParams,
                BestBreakdown  = snap.BestBreakdown as RegenScoreResult,
            });
        }
        catch (Exception ex)
        {
            Library.Log($"Multi-chain progress poll error: {ex.Message}");
        }
    }

    private static void FinalizeMultiChainOpt(AppOptimization.MultiChainSession session, OptSettings s)
    {
        try
        {
            var profile = RegenChamberOptimization.Profiles[s.ProfileIndex];
            var result = session.AwaitResult();

            // Regenerate the global-best design at full fidelity for the viewer.
            var bestDesign = RegenChamberOptimization.Unpack(result.BestParams, s.BaselineDesign);
            var voxelGen3 = new Voxelforge.Geometry.ChamberVoxelBuilderAdapter();
            var gen = UI.ResourceBudget.AutoCoarsenVoxelToFitBudget
                ? RegenChamberOptimization.GenerateWithAutoCoarsen(s.Conditions, bestDesign, voxelSize_mm: VoxelSizeMM,
                    onVoxelSubstituted: (prev, now, _) =>
                        SetFormStatus($"Best-design voxel auto-coarsened {prev:F2} → {now:F2} mm to fit memory budget; rendering…"),
                    voxelGenerator: voxelGen3)
                : RegenChamberOptimization.GenerateWith(s.Conditions, bestDesign, voxelSize_mm: VoxelSizeMM,
                    voxelGenerator: voxelGen3);
            var score = RegenChamberOptimization.Evaluate(gen, profile);
            _lastResult = gen;
            _lastDesign = bestDesign;
            _lastScore = score;
            _lastResultBestSoFarIter = 0;
            UpdateViewer(gen);
            UpdateFormResults(gen, score);

            var form = _form;
            if (form != null && !form.IsDisposed)
            {
                try { form.BeginInvoke(() => form.ApplyOptResult(result.BestParams)); }
                catch { }
            }

            SharedState.WriteOptProgress(new OptProgress
            {
                IsRunning     = false,
                Iteration     = result.TotalIterations,
                MaxIterations = s.MaxIterations * session.ChainCount,
                BestScore     = result.BestScore,
                Temperature   = 0,
                RestartCount  = 0,
                BestParams    = result.BestParams,
                BestBreakdown = result.BestBreakdown as RegenScoreResult,
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

            var summary = UI.ResourceProfiler.End("sa");
            string tail = summary.WallMs > 0 ? $"  |  {summary.Format()}" : "";
            int totalRestarts = 0;
            int infeasibleExits = 0;
            foreach (var c in result.Chains)
            {
                totalRestarts += c.RestartCount;
                if (c.InfeasibleExitTripped) infeasibleExits++;
            }
            string exitNote = infeasibleExits > 0
                ? $", {infeasibleExits}/{result.ChainCount} chains exited on infeasibility streak"
                : "";
            SetFormStatus($"Multi-chain SA done. Best = {result.BestScore:F2} (chain {result.WinningChain}) "
                        + $"after {result.TotalIterations} total iters across {result.ChainCount} chains in "
                        + $"{result.ElapsedMilliseconds} ms ({totalRestarts} restarts{exitNote}). "
                        + $"Pareto = {_pareto.Count}." + tail);

            if (_pendingBatchSettings is { } batch)
            {
                try { WriteBatchOutputsMultiChain(batch, result, s); }
                catch (Exception ex) { SetFormStatus("Batch save error: " + ex.Message); }
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
                $"Multi-chain SA done but best-design voxel build exceeded the memory budget. "
              + $"Coarsen voxel to ≥ {mem.SuggestedVoxel_mm:F2} mm and re-run Generate to render.");
        }
        catch (OperationCanceledException)
        {
            SetFormStatus("Multi-chain SA cancelled by user.");
        }
        catch (Exception ex)
        {
            SetFormStatus("Multi-chain finalize error: " + ex.Message);
        }
    }

    // Mirror of WriteBatchOutputs for multi-chain results.
    private static void WriteBatchOutputsMultiChain(
        BatchRunSettings batch, MultiChainOptimizer.Result result, OptSettings s)
    {
        // Synthesize a single-chain-shaped wrapper around the multi-chain
        // result so the existing WriteBatchOutputs logic can run unchanged.
        // We construct a fresh SimulatedAnnealingOptimizer with the result's
        // best params loaded and never advance it — used purely as a data
        // carrier for the batch writer.
        var stub = new SimulatedAnnealingOptimizer(
            RegenChamberOptimization.Bounds, s.MaxIterations, s.Seed);
        stub.SetInitialCandidate(result.BestParams);
        stub.ReportScore(result.BestParams, result.BestScore, result.BestBreakdown);
        WriteBatchOutputs(batch, stub, RegenChamberOptimization.Profiles[s.ProfileIndex]);
    }
}
