// Program.cs — Entry point for the Regen Chamber Designer.
//
// CI infrastructure note (2026-05-16): the self-hosted runner was
// restored after a multi-day outage; this comment-only touch is a
// no-op edit so the dotnet pipeline runs against this PR (ci.yml's
// paths-ignore skips PRs that touch only markdown / docs).
//
// Three-thread architecture (pattern-matched to HX, written fresh here):
//   Main thread — Library.Go(), PicoGK viewer event loop (GLFW).
//   Task thread — Run(), PicoGK voxel ops, thermal solve, optimizer stepping.
//   STA thread — RegenChamberForm (WinForms UI).
//
// Cross-thread communication flows through SharedState: UI thread posts
// one-shot requests (param changes, exports, save/load, opt start/stop);
// task thread polls, consumes, and processes. Updates back to the UI use
// form.BeginInvoke. Every PicoGK voxel operation runs on the task thread.

using System.Windows.Forms;
using PicoGK;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Geometry;
using Voxelforge.Geometry;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.UI;

namespace Voxelforge;

public static partial class Program
{
    // ── Configuration ────────────────────────────────────────────────────
    // 0.4 mm gives ~2 voxels across the thinnest 0.8 mm walls.
    // Drop to 0.3 mm if you need higher fidelity on 0.5 mm features.
    // Surfaced from `private` → `internal const` so the form's
    // pre-flight projection indicator can target the same voxel the main
    // Generate path will use. A future sprint can expose this as a UI
    // knob; for now the static default keeps callers in sync.
    internal const float VoxelSizeMM = 0.4f;
    private const int ViewerGroupChamber = 1;

    // ── Task-thread state ────────────────────────────────────────────────
    // Thread-ownership contract for the `_last*` fields below
    // (formalising what was previously only an implicit convention):
    //
    //   • WRITER: task thread only. `Run()`, `RunGenerateRequest`,
    //     `HandleRunBatchGeneration`, `Optimizer` finaliser, and the opt
    //     best-adopt path all run on the single task thread, so there is
    //     exactly one writer at any instant.
    //   • READER: UI thread (WinForms callbacks) reads these when the user
    //     presses "Export STL" / "Save Design" / "Copy Summary" / "Export
    //     Report" / etc. Readers dereference and act on a *snapshot* —
    //     none of them mutate.
    //   • SAFETY: C# reference assignments are atomic for reference-typed
    //     fields (ECMA-335 I.12.6.6) and the `int` counter is 4 bytes
    //     aligned, so a reader cannot observe a torn write. The explicit
    //     `GC.Collect()` in the Generate path emits a full memory barrier
    //     that also publishes any preceding writes; between generations,
    //     a stale read by the UI is acceptable (the user gets the prior
    //     result, not corruption).
    //
    //   These fields deliberately have no lock — the single-writer /
    //   snapshot-reader pattern has been verified across ~1 year of use.
    //   Adding a lock here would serialize every UI click against SA
    //   stepping and is NOT wanted.
    private static RegenChamberForm? _form;
    private static volatile bool _formReady = false;
    // Air-breathing mode — set before the STA thread creates its form.
    // Controlled by --airbreathing CLI flag in UiThreadMain.
    private static volatile bool _airbreathingMode = false;
    // Controlled by --electric CLI flag in UiThreadMain.
    private static volatile bool _electricMode = false;
    // Controlled by --marine CLI flag in UiThreadMain.
    private static volatile bool _marineMode = false;
    // Controlled by --avalonia-electric CLI flag in UiThreadMain. (ADR-027 Phase 1)
    private static volatile bool _avaloniaElectricMode = false;
    private static Voxelforge.Avalonia.ElectricPropulsionWindow? _avaloniaEpWindow;
    private static volatile bool _avaloniaEpWindowReady = false;
    private static UI.ElectricPropulsionForm? _epForm;
    private static volatile bool _epFormReady = false;
    private static UI.MarineForm? _marineForm;
    private static volatile bool _marineFormReady = false;
    private static UI.AirbreathingForm? _abForm;
    private static volatile bool _abFormReady = false;
    private static RegenGenerationResult? _lastResult;   // task-thread-write, UI-thread-read
    private static RegenChamberDesign? _lastDesign;      // task-thread-write, UI-thread-read
    private static RegenScoreResult? _lastScore;         // task-thread-write, UI-thread-read
    // Best-so-far provenance. 0 = final (manual regen or
    // post-FinalizeOpt). > 0 = active SA run; value is the opt iteration at
    // which this best was adopted. Export paths stamp a "BEST-SO-FAR (iter N)"
    // banner when > 0 so exports during a running optimization are never
    // mistaken for a converged result. Same task-thread-write / UI-thread-read
    // contract as the `_last*` references above.
    private static int _lastResultBestSoFarIter = 0;     // task-thread-write, UI-thread-read
    // PHASE 6 (2026-04-20): Pareto front over (peak T, coolant ΔP, mass).
    // Populated during every SA step (not just best-updating ones) so the
    // trade-off surface reflects the whole search, not just the weighted
    // winner. Consumers: status bar summary, optional report section.
    private static readonly Optimization.ParetoFront _pareto = new();
    // PHASE 6: snapshot of the Pareto front at the end of the most recent
    // optimization run; read by HandleExportReport so a report written after
    // the run ends still shows the trade-off surface.
    private static IReadOnlyList<Optimization.ParetoPoint>? _lastParetoFrontSnapshot;
    // 2026-04-22: non-null during a batch run. FinalizeOpt reads this and
    // auto-writes the enabled outputs to OutputFolder, then clears it.
    private static BatchRunSettings? _pendingBatchSettings;
    private static string? _batchTimestampPrefix;
    // IEngine Phase 2 (ADR-025): IObjective for the single-chain SA hot path.
    // Created in TryStartOpt, consumed by StepOpt's parallel batch loop.
    // Null between optimization runs.
    private static Optimization.RegenObjective? _singleChainObjective;

    // In-flight flag for the subprocess STL export
    // path. Subprocess exports at non-session voxel sizes can take 10-60 s
    // (or longer at < 0.10 mm). The export Task.Run runs on the threadpool
    // so the task thread keeps cycling SA / regen / load requests during
    // the wait. A second export request while one is in-flight gets a
    // clean reject status instead of queueing or blocking. Interlocked
    // CompareExchange protects against a request landing on the threadpool
    // exactly when the prior export releases the flag.
    private static int _subprocessExportInFlight = 0;

    // Cancellation for in-flight long ops (SA batch,
    // tolerance sweep, optimization loop). Lives on the task thread;
    // Stop button cancels, next op constructs a fresh CTS.
    private static System.Threading.CancellationTokenSource? _currentOpCts;
    private static readonly object _currentOpCtsLock = new();

    // Track priority so we can restore to Normal when
    // heavy work ends. _priorityDemoted reflects the current state.
    private static bool _priorityDemoted;
    private static readonly object _priorityLock = new();

    [STAThread]
    public static void Main()
    {
        Console.WriteLine($"Regen Chamber Designer — MVP");
        Console.WriteLine($"Voxel size: {VoxelSizeMM} mm");
        Console.WriteLine($"Booting PicoGK…");
        Library.Go(VoxelSizeMM, Run);
    }

    // ═════════════════════════════════════════════════════════════════════
    //   Task thread
    // ═════════════════════════════════════════════════════════════════════

    private static void Run()
    {
        Library.Log("Task thread started.");

        // Viewer style (warm copper — CuCrZr default). If ColorFloat hex
        // constructor is missing, fall back silently.
        try { Library.oViewer().SetGroupMaterial(ViewerGroupChamber, new ColorFloat(0.75f, 0.50f, 0.32f), 0.90f, 0.30f); }
        catch (Exception ex) { Library.Log($"Viewer material init failed: {ex.Message}"); }

        // Launch WinForms UI on its own STA thread.
        var uiThread = new Thread(UiThreadMain)
        {
            IsBackground = true,
            Name = "RegenUI",
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        // Wait for whichever form (rocket / air-breathing / electric / marine / avalonia-ep) is expected.
        while (!_formReady && !_abFormReady && !_epFormReady && !_marineFormReady && !_avaloniaEpWindowReady && Library.bContinueTask(true))
            Thread.Sleep(30);
        if (_airbreathingMode)
        {
            if (!_abFormReady || _abForm == null) { Library.Log("Airbreathing form never initialized — exiting."); return; }
        }
        else if (_avaloniaElectricMode)
        {
            if (!_avaloniaEpWindowReady || _avaloniaEpWindow == null) { Library.Log("Avalonia electric-propulsion window never initialized — exiting."); return; }
        }
        else if (_electricMode)
        {
            if (!_epFormReady || _epForm == null) { Library.Log("Electric-propulsion form never initialized — exiting."); return; }
        }
        else if (_marineMode)
        {
            if (!_marineFormReady || _marineForm == null) { Library.Log("Marine form never initialized — exiting."); return; }
        }
        else
        {
            if (!_formReady || _form == null) { Library.Log("Form never initialized — exiting."); return; }
        }

        // Main dispatch loop.
        SimulatedAnnealingOptimizer? opt = null;
        // When OptSettings.UseMultiChain is true, a MultiChainSession runs N
        // parallel SA chains on a worker task instead of the single-chain
        // ask-tell loop. Exactly one of (opt, multi, nsga) is
        // set while optRunning is true; all three are null otherwise.
        AppOptimization.MultiChainSession? multi = null;
        AppOptimization.NsgaIISession? nsga = null;
        OptSettings? activeOptSettings = null;
        bool optRunning = false;

        while (Library.bContinueTask(true))
        {
            // ── Manual-mode parameter change (rocket) ──
            // Ignored while an optimization is running — the optimizer owns
            // the design space during its run.
            if (!_airbreathingMode && !optRunning && SharedState.TryTakeParamChange(out var cond, out var design))
            {
                RegenerateForManualMode(cond!, design!);
            }

            // ── Air-breathing manual-mode parameter change ──
            if (_airbreathingMode && SharedState.TryTakeAirbreathingParamChange(
                    out var abCond, out var abDesign, out var abOpts))
            {
                RegenerateAirbreathingForManualMode(abCond!, abDesign!, abOpts!);
            }

            // ── Electric-propulsion manual-mode parameter change ──
            if (_electricMode && SharedState.TryTakeElectricPropulsionParamChange(
                    out var epCond, out var epDesign))
            {
                RegenerateElectricPropulsionForManualMode(epCond!, epDesign!);
            }

            // ── Avalonia electric-propulsion manual-mode parameter change ──
            if (_avaloniaElectricMode && SharedState.TryTakeElectricPropulsionParamChange(
                    out var avEpCond, out var avEpDesign))
            {
                RegenerateForAvaloniaElectricMode(avEpCond!, avEpDesign!);
            }

            // ── Marine manual-mode parameter change ──
            if (_marineMode && SharedState.TryTakeMarineParamChange(
                    out var marineCond, out var marineDesign))
            {
                RegenerateMarineForManualMode(marineCond!, marineDesign!);
            }

            // ── File operations (always safe to process) ──
            if (SharedState.TryTakeExportStl(out var stlPath, out var stlVoxelMM, out var stlMonolithic))
                HandleExportStl(stlPath!, stlVoxelMM, stlMonolithic);

            if (SharedState.TryTakeExport3MF(out var threeMfPath))
                HandleExport3MF(threeMfPath!);   // PHASE 7

            if (SharedState.TryTakeExportReport(out var reportPath))
                HandleExportReport(reportPath!);

            if (SharedState.TryTakeExportVti(out var vtiPath))
                HandleExportVti(vtiPath!);

            if (SharedState.TryTakeSaveDesign(out var savePath, out var saveCond, out var saveDesign))
                HandleSaveDesign(savePath!, saveCond!, saveDesign!);

            // ── Start batch optimization (2026-04-22) ──
            // A batch-start message also carries the output folder + save
            // toggles. Stash the batch settings here so FinalizeOpt sees
            // them and auto-saves.
            if (!optRunning && SharedState.TryTakeStartBatch(out var batchOpt, out var batchRun))
            {
                _pendingBatchSettings = batchRun;
                _batchTimestampPrefix = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                activeOptSettings = batchOpt;
                if (batchOpt!.UseNsgaIi)
                {
                    nsga = TryStartNsgaOpt(batchOpt!);
                    optRunning = nsga != null;
                }
                else if (batchOpt!.UseMultiChain)
                {
                    multi = TryStartMultiChainOpt(batchOpt!);
                    optRunning = multi != null;
                }
                else
                {
                    opt = TryStartOpt(batchOpt!);
                    optRunning = opt != null;
                }
                if (optRunning)
                {
                    BeginOp();
                    int optSec = Voxelforge.UI.ResourceBudget.OptTimeoutSeconds;
                    if (optSec > 0) TryArmCurrentOpTimeout(TimeSpan.FromSeconds(optSec));
                }
                if (!optRunning) { _pendingBatchSettings = null; _batchTimestampPrefix = null; }
            }

            // ── Start optimization ──
            if (!optRunning && SharedState.TryTakeStartOpt(out activeOptSettings))
            {
                if (activeOptSettings!.UseNsgaIi)
                {
                    nsga = TryStartNsgaOpt(activeOptSettings!);
                    optRunning = nsga != null;
                }
                else if (activeOptSettings!.UseMultiChain)
                {
                    multi = TryStartMultiChainOpt(activeOptSettings!);
                    optRunning = multi != null;
                }
                else
                {
                    opt = TryStartOpt(activeOptSettings!);
                    optRunning = opt != null;
                }
                if (optRunning)
                {
                    BeginOp();
                    int optSec = Voxelforge.UI.ResourceBudget.OptTimeoutSeconds;
                    if (optSec > 0) TryArmCurrentOpTimeout(TimeSpan.FromSeconds(optSec));
                }
            }

            // ── Honour a UI-side cancel request ──
            // The form posts this when the user edits an input during
            // an in-flight op (or the A5/C5 throttles trip). Cancels
            // the CTS; any running Parallel.For / ToleranceAnalysis.Run
            // unwinds via OperationCanceledException.
            if (SharedState.TryTakeCancelCurrentOp())
                CancelCurrentOp();

            // ── Step optimization ──
            if (optRunning && opt != null && activeOptSettings != null)
            {
                // Stop request now cancels the CTS as well
                // as setting the flag, so an in-flight Parallel.For unwinds
                // immediately instead of finishing its current batch.
                bool stopRequested = SharedState.TryTakeStopOpt();
                if (stopRequested) CancelCurrentOp();
                if (stopRequested || opt.IsComplete)
                {
                    FinalizeOpt(opt, activeOptSettings);
                    EndOp();                   // Restore priority
                    opt = null;
                    activeOptSettings = null;
                    optRunning = false;
                }
                else
                {
                    StepOpt(opt, activeOptSettings);
                }
            }
            else if (optRunning && multi != null && activeOptSettings != null)
            {
                // Multi-chain mode: the worker task drives all chains in parallel.
                // Main loop just polls progress for the UI and watches for
                // cancel / completion.
                bool stopRequested = SharedState.TryTakeStopOpt();
                if (stopRequested)
                {
                    CancelCurrentOp();
                    multi.Cancel();
                }
                if (multi.IsComplete)
                {
                    FinalizeMultiChainOpt(multi, activeOptSettings);
                    EndOp();
                    multi.Dispose();
                    multi = null;
                    activeOptSettings = null;
                    optRunning = false;
                }
                else
                {
                    PollMultiChainProgress(multi, activeOptSettings);
                    Thread.Sleep(50);   // throttle UI polls; chains are doing the real work
                }
            }
            else if (optRunning && nsga != null && activeOptSettings != null)
            {
                // NSGA-II mode: the worker task drives the entire evolution.
                // Main loop polls evaluation-count progress for the UI.
                bool stopRequested = SharedState.TryTakeStopOpt();
                if (stopRequested)
                {
                    CancelCurrentOp();
                    nsga.Cancel();
                }
                if (nsga.IsComplete)
                {
                    FinalizeNsgaOpt(nsga, activeOptSettings);
                    EndOp();
                    nsga.Dispose();
                    nsga = null;
                    activeOptSettings = null;
                    optRunning = false;
                }
                else
                {
                    PollNsgaProgress(nsga, activeOptSettings);
                    Thread.Sleep(50);
                }
            }
            else
            {
                // Idle — don't burn CPU when nothing is happening.
                Thread.Sleep(20);
            }
        }

        Library.Log("Task loop exiting.");
    }

    // ═════════════════════════════════════════════════════════════════════
    //   UI thread
    // ═════════════════════════════════════════════════════════════════════

    private static void UiThreadMain()
    {
        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // --airbreathing flag routes to the air-breathing viewer instead of
        // the rocket engine form.  Use Environment.GetCommandLineArgs() so
        // both direct-exe launches and `dotnet run -- --airbreathing` work.
        var cliArgs = Environment.GetCommandLineArgs();
        if (cliArgs.Any(a => a.Equals("--airbreathing", StringComparison.OrdinalIgnoreCase)))
        {
            // Optional --engine-kind <Name> pre-selects the engine-kind ComboBox.
            // Default = Ramjet (Wave-1 Phase-0 behaviour).
            var initialKind = Airbreathing.AirbreathingEngineKind.Ramjet;
            int kindIdx = Array.FindIndex(cliArgs,
                a => a.Equals("--engine-kind", StringComparison.OrdinalIgnoreCase));
            if (kindIdx >= 0 && kindIdx + 1 < cliArgs.Length
                && Enum.TryParse<Airbreathing.AirbreathingEngineKind>(
                       cliArgs[kindIdx + 1], ignoreCase: true, out var parsedKind))
                initialKind = parsedKind;

            _airbreathingMode = true;
            var abForm = new UI.AirbreathingForm(
                onGenerate:  (fc, design, opts)
                    => SharedState.PostAirbreathingParamChange(fc, design, opts),
                initialKind: initialKind);

            abForm.FormClosed += (_, _) =>
            {
                try { Library.bContinueTask(false); } catch { /* shutting down */ }
            };
            _ = abForm.Handle;
            _abForm      = abForm;
            _abFormReady = true;
            Application.Run(abForm);
            return;
        }

        // --avalonia-electric: Avalonia Phase-1 electric-propulsion viewer (ADR-027).
        // Starts Avalonia on a dedicated MTA background thread; this STA thread returns
        // immediately after the window is confirmed open. The existing --electric WinForms
        // path is untouched — both flags are independent.
        if (cliArgs.Any(a => a.Equals("--avalonia-electric", StringComparison.OrdinalIgnoreCase)))
        {
            _avaloniaElectricMode = true;
            var win = Voxelforge.Avalonia.AvaloniaElectricRunner.Launch(
                onGenerate: (cond, design)
                    => SharedState.PostElectricPropulsionParamChange(cond, design),
                onClosed: () =>
                {
                    try { Library.bContinueTask(false); } catch { /* shutting down */ }
                });
            _avaloniaEpWindow      = win;
            _avaloniaEpWindowReady = true;
            return;   // Avalonia owns its thread; STA thread is done
        }

        // --electric flag routes to the electric-propulsion (Wave-1 resistojet) form.
        if (cliArgs.Any(a => a.Equals("--electric", StringComparison.OrdinalIgnoreCase)))
        {
            _electricMode = true;
            var epForm = new UI.ElectricPropulsionForm(
                onGenerate: (cond, design)
                    => SharedState.PostElectricPropulsionParamChange(cond, design));

            epForm.FormClosed += (_, _) =>
            {
                try { Library.bContinueTask(false); } catch { /* shutting down */ }
            };
            _ = epForm.Handle;
            _epForm      = epForm;
            _epFormReady = true;
            Application.Run(epForm);
            return;
        }

        // --marine flag routes to the marine (AUV displacement-hull) form.
        if (cliArgs.Any(a => a.Equals("--marine", StringComparison.OrdinalIgnoreCase)))
        {
            _marineMode = true;
            var marineForm = new UI.MarineForm(
                onGenerate: (cond, design)
                    => SharedState.PostMarineParamChange(cond, design));

            marineForm.FormClosed += (_, _) =>
            {
                try { Library.bContinueTask(false); } catch { /* shutting down */ }
            };
            _ = marineForm.Handle;
            _marineForm      = marineForm;
            _marineFormReady = true;
            Application.Run(marineForm);
            return;
        }

        // The renderer button shows up only when voxelforge-render.exe is found
        // next to the App. The orchestration callback chains: temp-STL export
        // via StlExporter → voxelforge-render → cleanup. Both are subprocesses
        // (no PicoGK on UI thread) so a
        // worker-thread Task.Run is sufficient — no SharedState plumbing needed.
        var renderExe = Path.Combine(AppContext.BaseDirectory, "voxelforge-render.exe");
        Action<string, string, string, string, int>? renderCallback = File.Exists(renderExe)
            ? (output, material, mode, resolution, frames) =>
                System.Threading.Tasks.Task.Run(() =>
                    OrchestrateRender(renderExe, output, material, mode, resolution, frames))
            : null;

        // Setup-wizard gate.
        // SetupWizardForm.ShouldShow consults SessionSettings.WizardVersion
        // (shows when WizardVersion < CurrentWizardVersion), so it DOES fire on
        // a first launch. The wizard's exit feeds into the form via the returned
        // WizardResult's seed JSON, which the form picks up from SessionSettings
        // on its own Load() call.
        {
            var preFormSettings = SessionSettings.Load();
            if (SetupWizardForm.ShouldShow(preFormSettings))
            {
                var wizardResult = SetupWizardForm.RunModal(preFormSettings);
                if (wizardResult is null)
                {
                    // User cancelled — exit without launching the form.
                    Library.bContinueTask(false);
                    return;
                }

                // Round-trip the wizard's seed through SessionSettings so the
                // form's Load() pass picks it up. Fires whenever ShouldShow
                // returned true (first launch / bumped WizardVersion).
                preFormSettings.LastSeedDesignJson  = SerializeWizardSeed(
                    wizardResult.Conditions, wizardResult.Design);
                preFormSettings.LastPresetName      = wizardResult.PresetName;
                preFormSettings.WizardVersion       = SetupWizardForm.CurrentWizardVersion;
                preFormSettings.SkipWizardOnLaunch  = wizardResult.SkipNextLaunch;
                preFormSettings.Save();
            }
        }

        var form = new RegenChamberForm(
            onParamsChanged: (c, d) => SharedState.PostParamChange(c, d),
            onExportStl:     (path, voxelMM, monolithic) => SharedState.PostExportStl(path, voxelMM, monolithic),
            onExport3MF:     (path) => SharedState.PostExport3MF(path),
            onExportReport:  (path) => SharedState.PostExportReport(path),
            onExportVti:     (path) => SharedState.PostExportVti(path),
            onSaveDesign:    (path, c, d) => SharedState.PostSaveDesign(path, c, d),
            onLoadDesign:    LoadDesignOnUiThread,
            onStartOpt:      (settings) => SharedState.PostStartOpt(settings),
            onStartBatch:    (settings, batch) => SharedState.PostStartBatch(settings, batch),
            onStopOpt:       ()         => SharedState.PostStopOpt(),
            getOptProgress:  ()         => SharedState.ReadOptProgress(),
            runProofTest:    RunProofTestFromUI,
            runTolerance:    RunToleranceFromUI,
            onRenderImage:   renderCallback);

        form.FormClosed += (_, _) =>
        {
            // Signal the task thread to exit.
            try { Library.bContinueTask(false); } catch { /* shutting down */ }
        };

        // Force handle creation before BeginInvoke can ever be called against it.
        _ = form.Handle;

        _form = form;
        _formReady = true;

        Application.Run(form);
    }

    private static (OperatingConditions?, RegenChamberDesign?) LoadDesignOnUiThread(string path)
    {
        try
        {
            var sd = DesignPersistence.Load(path);
            if (sd?.Conditions != null && sd.Design != null)
                return (sd.Conditions, sd.Design);
            return (null, null);
        }
        catch (System.IO.FileNotFoundException)
        {
            SetFormStatus("Load error: file not found.");
            return (null, null);
        }
        catch (System.IO.DirectoryNotFoundException)
        {
            SetFormStatus("Load error: folder not found.");
            return (null, null);
        }
        catch (System.UnauthorizedAccessException)
        {
            SetFormStatus("Load error: permission denied.");
            return (null, null);
        }
        catch (System.Text.Json.JsonException ex)
        {
            SetFormStatus($"Load error: not a valid .rcd.json ({ex.Message}).");
            return (null, null);
        }
        catch (Exception ex)
        {
            SetFormStatus("Load error: " + ex.Message);
            return (null, null);
        }
    }

    // ── Air-breathing handlers extracted to Program.Airbreathing.cs ───────
    //   (Sprint 0 / Wave 1, 2026-05-05). RegenerateAirbreathingForManualMode,
    //   UpdateViewerAirbreathing, UpdateAbFormResults, SetAbFormStatus all
    //   live in the partial class file alongside the AirbreathingForm
    //   wiring at line 360 in Program.cs (kept here because it shares
    //   UiThreadMain's GLFW + STA setup).

    /// <summary>
    /// Called from the UI thread. Blocks briefly while the task-thread state
    /// is read, but since the proof-test analysis is pure math (no voxel ops),
    /// it's safe to run on the calling thread directly.
    /// </summary>
    /// <summary>
    /// Serialise the wizard's chosen seed pair to a string suitable for
    /// round-tripping through
    /// <see cref="UI.SessionSettings.LastSeedDesignJson"/>. Uses the
    /// same shape as <see cref="IO.DesignPersistence.Save"/> minus the
    /// file-write — a small JSON envelope with a schema-version stamp
    /// + the conditions + design. The form picks this up in its
    /// <c>RegenChamberForm</c> constructor (Step 6 wires that handoff).
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions s_wizardSeedJsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static string SerializeWizardSeed(OperatingConditions c, RegenChamberDesign d)
    {
        var envelope = new
        {
            SchemaVersion = "wizard-v1",
            Conditions    = c,
            Design        = d,
        };
        return System.Text.Json.JsonSerializer.Serialize(envelope, s_wizardSeedJsonOpts);
    }

    private static Structure.ProofTestResult? RunProofTestFromUI()
    {
        if (_lastResult == null || _lastDesign == null) return null;
        try
        {
            return RegenChamberOptimization.EvaluateProofTest(_lastResult, _lastDesign);
        }
        catch (Exception ex)
        {
            Library.Log($"Proof-test error: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Called from the UI. Pure math (re-runs the thermal + structural
    /// passes many times); no voxel ops, so safe from the UI thread.
    /// </summary>
    private static Analysis.ToleranceResult? RunToleranceFromUI()
    {
        if (_lastResult == null || _lastDesign == null) return null;
        // Profile the sweep for BENCH emission +
        // the status-bar "Peak … MB / xx% CPU / s" tail. Must End()
        // regardless of success so the in-flight flag clears cleanly.
        Voxelforge.UI.ResourceProfiler.Begin("tol");
        // Allocate a fresh CTS dedicated to this sweep
        // and arm the optional time budget. We don't reuse BeginOp here
        // because the sweep is UI-thread synchronous (no surrounding SA
        // dispatch loop) — a fresh CTS keeps a stale token from a prior
        // Stop button from tripping this run.
        lock (_currentOpCtsLock)
        {
            _currentOpCts?.Dispose();
            _currentOpCts = new System.Threading.CancellationTokenSource();
        }
        int timeoutSec = Voxelforge.UI.ResourceBudget.SweepTimeoutSeconds;
        if (timeoutSec > 0) TryArmCurrentOpTimeout(TimeSpan.FromSeconds(timeoutSec));
        try
        {
            var inputs = new Analysis.ToleranceInputs(
                SampleCount: _lastDesign.ToleranceSampleCount,
                WallThicknessTolerance_mm: _lastDesign.WallThicknessTolerance_mm,
                ChannelHeightTolerance_mm: _lastDesign.ChannelHeightTolerance_mm,
                RibThicknessTolerance_mm: _lastDesign.RibThicknessTolerance_mm,
                JacketThicknessTolerance_mm: _lastDesign.JacketThicknessTolerance_mm,
                RandomSeed: 1);
            // Pass the current op token so the Stop
            // button (or an input-change cancel) can unwind the sweep
            // partway through.
            return RegenChamberOptimization.EvaluateTolerance(
                _lastResult, _lastDesign, inputs, CurrentOpToken());
        }
        catch (OperationCanceledException)
        {
            // Propagate so the form can distinguish
            // cancel / timeout from "no design yet" (null return).
            throw;
        }
        catch (Exception ex)
        {
            Library.Log($"Tolerance sweep error: {ex}");
            return null;
        }
        finally
        {
            // Profiler's End() stashes the summary in its per-opName
            // LastSummary table; the form reads it from there for the
            // status-bar tail. Also emits BENCH tol_* lines.
            Voxelforge.UI.ResourceProfiler.End("tol");
        }
    }

    /// <summary>
    /// Current cancellation token for any in-flight
    /// long op (SA, tolerance sweep). Callers pass this to Parallel.
    /// For + ToleranceAnalysis + RegenCoolingSolver. Stop button /
    /// user-input cancels via <see cref="CancelCurrentOp"/>.
    /// </summary>
    private static System.Threading.CancellationToken CurrentOpToken()
    {
        lock (_currentOpCtsLock)
        {
            _currentOpCts ??= new System.Threading.CancellationTokenSource();
            return _currentOpCts.Token;
        }
    }

    /// <summary>
    /// Start a new long op: dispose any stale CTS, allocate a fresh
    /// one, demote process priority if the user's budget calls for
    /// it. Call <see cref="EndOp"/> in a `finally` to restore.
    /// </summary>
    private static System.Runtime.GCLatencyMode _priorGcLatencyMode;
    private static bool _gcLatencyTuned;

    private static void BeginOp()
    {
        lock (_currentOpCtsLock)
        {
            _currentOpCts?.Dispose();
            _currentOpCts = new System.Threading.CancellationTokenSource();
        }
        DemoteIfConfigured();
        // If the user enabled GC latency tuning, flip
        // to SustainedLowLatency so gen-2 collections don't land on a
        // heavy voxel op. Restore prior mode in EndOp().
        if (Voxelforge.UI.ResourceBudget.GcLatencyTuning && !_gcLatencyTuned)
        {
            try
            {
                _priorGcLatencyMode = System.Runtime.GCSettings.LatencyMode;
                System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
                _gcLatencyTuned = true;
            }
            catch (Exception ex) { Library.Log($"GC latency tune failed: {ex.Message}"); }
        }
        // Start wall/CPU/peak-WS profiler. EndOp()
        // pulls the formatted summary for FinalizeOpt's status line.
        Voxelforge.UI.ResourceProfiler.Begin("sa");
    }

    private static void EndOp()
    {
        // Stop the profiler (also emits BENCH
        // lines + fires a working-set trim hint). FinalizeOpt already
        // harvested the summary for its status line; this End() call
        // is a defensive no-op that guarantees the in-flight flag
        // clears on error paths where FinalizeOpt threw.
        Voxelforge.UI.ResourceProfiler.End("sa");

        RestorePriority();
        if (_gcLatencyTuned)
        {
            try { System.Runtime.GCSettings.LatencyMode = _priorGcLatencyMode; }
            catch (Exception ex) { Library.Log($"GC latency restore failed: {ex.Message}"); }
            _gcLatencyTuned = false;
        }
    }

    /// <summary>Cancel any in-flight long op. Safe to call repeatedly.</summary>
    private static void CancelCurrentOp()
    {
        lock (_currentOpCtsLock)
        {
            try { _currentOpCts?.Cancel(); } catch { /* already disposed */ }
        }
    }

    /// <summary>
    /// Schedule a deadline on the current op's
    /// cancellation source. When the timeout elapses the CTS fires,
    /// unwinding Parallel.For / ToleranceAnalysis the same way a Stop
    /// would. No-op when the timeout is zero/negative or when no op
    /// is currently in flight. Safe to call repeatedly — subsequent
    /// calls extend or shorten the existing deadline per
    /// <see cref="System.Threading.CancellationTokenSource.CancelAfter(TimeSpan)"/>.
    /// </summary>
    private static void TryArmCurrentOpTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) return;
        lock (_currentOpCtsLock)
        {
            if (_currentOpCts is null) return;
            try { _currentOpCts.CancelAfter(timeout); }
            catch { /* already disposed — op finished before arming */ }
        }
    }

    /// <summary>
    /// Demote the current process to BelowNormal if
    /// the user has opted into priority demotion. No-op if already
    /// demoted or if the resolved budget says DemotePriority=false.
    /// </summary>
    private static void DemoteIfConfigured()
    {
        if (!Voxelforge.UI.ResourceBudget.DemotePriority) return;
        lock (_priorityLock)
        {
            if (_priorityDemoted) return;
            try
            {
                using var p = System.Diagnostics.Process.GetCurrentProcess();
                p.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
                _priorityDemoted = true;
            }
            catch (Exception ex)
            {
                // Insufficient rights (unusual) — log and carry on at
                // default priority. Do NOT crash the task thread.
                Library.Log($"Priority demote failed: {ex.Message}");
            }
        }
    }

    private static void RestorePriority()
    {
        lock (_priorityLock)
        {
            if (!_priorityDemoted) return;
            try
            {
                using var p = System.Diagnostics.Process.GetCurrentProcess();
                p.PriorityClass = System.Diagnostics.ProcessPriorityClass.Normal;
                _priorityDemoted = false;
            }
            catch (Exception ex)
            {
                Library.Log($"Priority restore failed: {ex.Message}");
            }
        }
    }



    // ═════════════════════════════════════════════════════════════════════
    //   View / form helpers (task thread)
    // ═════════════════════════════════════════════════════════════════════

    private static void UpdateViewer(RegenGenerationResult gen)
    {
        // TIER A.2 polish: physics-only results intentionally carry
        // Voxels = null (analytical mass only). Skip the viewer update
        // rather than NRE the task thread.
        if (gen.Geometry.Voxels is null) return;
        try
        {
            var viewer = Library.oViewer();
            viewer.RemoveAllObjects();
            viewer.Add(gen.Geometry.Voxels.AsPicoGK(), ViewerGroupChamber);
        }
        catch (Exception ex) { Library.Log($"Viewer update failed: {ex.Message}"); }
    }

    private static void UpdateFormResults(RegenGenerationResult gen, RegenScoreResult? score)
    {
        var form = _form;
        if (form == null || form.IsDisposed) return;
        try { form.BeginInvoke(() => form.UpdateResults(gen, score)); }
        catch { /* form closing */ }
    }

    private static void SetFormStatus(string msg)
    {
        var form = _form;
        if (form == null || form.IsDisposed) return;
        try { form.BeginInvoke(() => form.SetStatus(msg)); }
        catch { /* form closing */ }
    }
}

// SharedState — extracted to Voxelforge/Orchestration/SharedState.cs
// (Sprint 0 / Wave 1, 2026-05-05). The class shape, namespace, and
// public methods are unchanged.

