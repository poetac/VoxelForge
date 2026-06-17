// RegenChamberForm.cs — WinForms control panel for the regen chamber designer.
//
// Single-form layout: left column = inputs, right column = outputs + actions.
// Form runs on an STA WinForms thread; all heavy work (PicoGK, thermal solve,
// optimization) happens on the task thread. Communication is via callbacks
// passed in at construction.

using System.Windows.Forms;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;

namespace Voxelforge.UI;

public sealed class OptSettings
{
    public int MaxIterations { get; init; } = 300;
    public int Seed { get; init; } = 1;
    public int ProfileIndex { get; init; } = 0;
    public bool WarmStart { get; init; } = true;
    public OperatingConditions Conditions { get; init; } = new();
    public RegenChamberDesign BaselineDesign { get; init; } = new();
    public double[]? WarmStartParams { get; init; }

    /// <summary>
    /// TIER A.2 (2026-04-21): parallel evaluation batch size for the SA
    /// step. 1 = legacy serial. When &gt; 1, each StepOpt generates batchSize
    /// perturbations of the SA candidate, evaluates them in parallel via the
    /// physics-only fast path (no voxel build), picks the best and reports
    /// that to SA. Effective (1 + λ) evolution strategy layered on annealing.
    /// Typical speedup: 4–6× on an 8-core laptop at batchSize = 8.
    /// </summary>
    public int ParallelBatchSize { get; init; } = 1;

    /// <summary>
    /// Sprint OPT-1 production wiring (2026-04-25): when true, replaces the
    /// single-chain SA + parallel-batch path with N independent SA chains
    /// running in parallel via <see cref="MultiChainOptimizer"/>. Each chain
    /// has its own RNG, cooling schedule, and restart history; periodic
    /// elite migration cross-pollinates the global best to all chains. Pairs
    /// with Sobol-sequence per-chain initial seeding (T1.2). Strict
    /// determinism: same (Seed, MultiChainCount, MaxIterations) → identical
    /// best result regardless of OS scheduler timing.
    ///
    /// When false, the existing single-chain + ParallelBatchSize path runs
    /// unchanged. Default flipped to true 2026-04-27 (Track A production
    /// promotion): post-#88 multi-chain SA measurably improves feasibility
    /// across the canonical preset set (RL10 EXPANDER 89.8 % → 31 %, merlin
    /// INJ_FACE 100 % → 32 %, pintle PINTLE_BLOCKAGE 99.9 % → 69 % at
    /// 500-iter SA per CLAUDE.md). Single-chain remains available via the
    /// UI toggle for legacy comparison runs.
    /// </summary>
    public bool UseMultiChain { get; init; } = true;

    /// <summary>
    /// Number of parallel SA chains when <see cref="UseMultiChain"/> is true.
    /// 0 = auto-scale to <see cref="MultiChainOptimizer.DefaultChainCount"/>
    /// (typically <c>ProcessorCount − 2</c>, clamped 1-16). Ignored when
    /// <see cref="UseMultiChain"/> is false.
    /// </summary>
    public int MultiChainCount { get; init; } = 0;

    /// <summary>
    /// T2.4b (2026-04-30): when true, replaces both SA paths with
    /// <see cref="NsgaIIOptimizer"/> multi-objective optimization. The
    /// three-axis Pareto front (peak wall T, coolant ΔP, mass) is displayed
    /// in the existing <c>paretoPanel</c>; the convergence panel shows
    /// the best feasible scalar score found so far. Mutually exclusive with
    /// <see cref="UseMultiChain"/> and ParallelBatchSize &gt; 1.
    /// </summary>
    public bool UseNsgaIi { get; init; } = false;

    /// <summary>NSGA-II population size (must be even). Default 100.</summary>
    public int NsgaPopulationSize { get; init; } = 100;

    /// <summary>NSGA-II maximum generations. Default 50.</summary>
    public int NsgaMaxGenerations { get; init; } = 50;
}

/// <summary>
/// 2026-04-22: bundled settings for an automated batch run. Paired with
/// an <see cref="OptSettings"/> when the user clicks "Run Batch &amp; Save".
/// Program.cs stashes these, runs SA normally, and on FinalizeOpt writes
/// the enabled outputs to <see cref="OutputFolder"/> with a shared
/// timestamp prefix (e.g. <c>2026-04-22_14-30-05_design.rcd.json</c>).
/// </summary>
public sealed class BatchRunSettings
{
    public string OutputFolder { get; init; } = "";
    public bool   SaveDesignJson { get; init; } = true;
    public bool   SaveStl { get; init; } = true;
    public bool   SaveReport { get; init; } = true;
    public bool   SaveParetoCsv { get; init; } = false;
    /// <summary>STL voxel size (mm) if SaveStl is true. Matches the session voxel for in-process export.</summary>
    public float  StlVoxelMM { get; init; } = Voxelforge.Program.VoxelSizeMM;
}

public sealed class OptProgress
{
    public bool IsRunning { get; init; }
    public int Iteration { get; init; }
    public int MaxIterations { get; init; }
    public double BestScore { get; init; }
    public double Temperature { get; init; }
    public int RestartCount { get; init; }
    public double[]? BestParams { get; init; }
    public RegenScoreResult? BestBreakdown { get; init; }
}

// Sprint 6 Track B (2026-04-22): marked `partial` so low-risk pure-logic
// slices (control builders, display formatters) can migrate to sibling
// files without touching behaviour. The main file still carries every
// event handler, InitializeComponent, and control-field declaration.
public sealed partial class RegenChamberForm : Form
{
    // ── Visibility registry ──
    // Maps FieldKeys to live row panels. Populated by the Row(label,
    // input, fieldKey) overload as Build*Group() helpers run; consumed
    // by RecomputeFieldVisibility() to drive per-control .Visible
    // updates in response to discriminator changes (cycle / topology /
    // pair / opt-in toggles). Initialized eagerly so any Build*Group()
    // helper that fires during the constructor can register without
    // a null-check.
    private readonly ControlVisibilityRegistry _visibilityRegistry = new();

    // ── Callbacks to main thread ─────────────────────────────────
    private readonly Action<OperatingConditions, RegenChamberDesign> _onParamsChanged;
    // Trailing bool routes the export through the StlExporter subprocess
    // with --monolithic so the produced STL fuses chamber + turbopump +
    // feed manifold + preburner into one body.
    private readonly Action<string, float, bool> _onExportStl;
    // Callback orchestrates: temp-STL export → voxelforge-render subprocess →
    // temp-STL cleanup. See Program.cs for wiring.
    private readonly Action<string, string, string, string, int>? _onRenderImage;
    private readonly Action<string> _onExport3MF;
    private readonly Action<string> _onExportReport;
    private readonly Action<string> _onExportVti;
    private readonly Action<string, OperatingConditions, RegenChamberDesign> _onSaveDesign;
    private readonly Func<string, (OperatingConditions?, RegenChamberDesign?)> _onLoadDesign;
    private readonly Action<OptSettings> _onStartOpt;
    private readonly Action _onStopOpt;
    private readonly Action<OptSettings, BatchRunSettings> _onStartBatch;
    private readonly Func<OptProgress?> _getOptProgress;

    // ── Inputs: conditions ───────────────────────────────────────
    // Sprint 17 / Track H (2026-04-23): `readonly` dropped so the
    // BuildXxxGroup() helpers in RegenChamberForm.ConstructorGroups.cs
    // can assign these fields. Same pattern as the Sprint 15 preburner
    // + aerospike fields — single-assignment-at-construction semantics
    // preserved; missing readonly is a compile-time concession only.
    private NumericUpDown nudThrustN = null!, nudPcPsi = null!, nudMR = null!;
    private NumericUpDown nudBartzFactor = null!;
    private NumericUpDown nudCoolTK = null!, nudCoolPMPa = null!;
    private ComboBox cboMaterial = null!, cboPropellantPair = null!;
    private Label lblPairNote = null!;

    // ── Inputs: geometry ─────────────────────────────────────────
    private NumericUpDown nudContraction = null!, nudExpansion = null!, nudLStar = null!;
    private NumericUpDown nudThetaN = null!, nudThetaE = null!, nudBellFrac = null!;
    private NumericUpDown nudChannelCount = null!;
    private NumericUpDown nudHChamber = null!, nudHThroat = null!, nudHExit = null!;
    private NumericUpDown nudRib = null!, nudWall = null!, nudJacket = null!;
    private NumericUpDown nudSmoothing = null!;
    private CheckBox chkManifolds = null!, chkPorts = null!;
    private NumericUpDown nudPortD = null!, nudManifoldL = null!;
    private NumericUpDown nudChannelFillet = null!;

    // ── Inputs: flanges ──────────────────────────────────────────
    private CheckBox chkInjectorFlange = null!, chkMountFlange = null!;
    private ComboBox cboMountFlangeStd = null!;
    // 2026-04-22: gate for the auto-preview behaviour. Starts ON for
    // backward-compat with earlier sessions; set OFF when driving a
    // batch run or editing many fields before a single Generate.
    private readonly CheckBox chkLivePreview;
    // Toggle next to Export STL that routes the export through
    // MonolithicEngineBuilder.BuildFromDesign in the subprocess.
    private readonly CheckBox chkExportMonolithic = null!;
    private NumericUpDown nudFlangeThk = null!, nudFlangeORFactor = null!, nudPropPortD = null!, nudMountThk = null!;

    // ── Inputs: injector internals (igniter / dome / crossover) ──────
    // Closes the silent-skip gaps tracked by issues #306 / #307 / #308:
    // these fields existed on RegenChamberDesign and were consumed by
    // ChamberVoxelBuilder, but the form had no controls so they were
    // unreachable from the UI. Group is collapsed by default since these
    // are advanced injector / cycle-architecture choices.
    private ComboBox     cboIgniterType         = null!;
    private NumericUpDown nudIgniterRadialFrac  = null!;
    private NumericUpDown nudFuelDomeDepth_mm   = null!;
    private NumericUpDown nudOxDomeDepth_mm     = null!;
    private NumericUpDown nudDomeInletDia_mm    = null!;
    private CheckBox     chkAntiVortexBaffle    = null!;
    private CheckBox     chkCoolantCrossover    = null!;
    private NumericUpDown nudCoolantCrossoverDia_mm = null!;

    // ── Inputs: thread standards ─────────────────────────────────
    private ComboBox cboCoolantPortStd = null!, cboPropPortStd = null!;

    // ── Inputs: STL export voxel size ────────────────────────────
    // Separate from the session voxel size (Program.VoxelSizeMM). When
    // the user sets this smaller than the session value, the export is
    // routed through a subprocess that re-voxelises the geometry at the
    // finer resolution.
    private readonly NumericUpDown nudStlVoxel;

    // ── Tier-2 follow-on inputs ──────────────────────────────────
    // Feed-system stackup controls (ullage opt-in + filter + umbilical).
    // Surfacing these lets the user actually drive the feed-system
    // stackup from the form without editing JSON.
    // `readonly` is dropped so BuildFeedSystemGroup /
    // BuildChannelTopologyGroup / BuildChilldownGroup /
    // BuildStartTransientGroup / BuildEngineCycleGroup in
    // ConstructorGroups.cs can own the assignments.
    private NumericUpDown nudTankUllage_MPa = null!;
    private ComboBox cboFilterStd = null!;
    private NumericUpDown nudFilterContamination = null!;
    private ComboBox cboUmbilicalStd = null!;

    // ChannelTopology dropdown — Axial / Helical / None.
    private ComboBox cboTopology = null!;

    // Chilldown opt-in + 4 params + result readouts.
    private CheckBox chkChilldownEnable = null!;
    private NumericUpDown nudChilldownInitT = null!, nudChilldownHTC = null!,
                          nudChilldownDoneDT = null!, nudChilldownMaxT = null!;
    private Label lblChilldownTime = null!, lblChilldownProp = null!,
                  lblChilldownShock = null!, lblChilldownRegime = null!;

    // Start-transient opt-in + 5 params + chart + readouts.
    // Two per-side valve ramp NumericUpDowns support staged starts.
    private CheckBox chkStartTransient = null!;
    private NumericUpDown nudValveOpen_ms = null!, nudIgniterDelay_ms = null!,
                          nudStartSimDur_ms = null!, nudStartSimDt_ms = null!,
                          nudHardStartFactor = null!;
    private NumericUpDown nudOxValveOpen_ms = null!, nudFuelValveOpen_ms = null!;
    private Label lblStartTimeTo90 = null!, lblStartPeakOvershoot = null!, lblStartUnburned = null!;
    private StartTransientChartPanel pcChartPanel = null!;

    // Engine cycle dropdown + auto-sized pump-pressure overrides + readouts.
    private ComboBox cboEngineCycle = null!;
    private NumericUpDown nudPumpInletP_MPa = null!, nudPumpDischargeP_MPa = null!, nudPumpEff = null!;
    private Label lblPumpFuel = null!, lblPumpOx = null!, lblPumpTotal = null!;

    // Preburner regen cooling opt-in. Mirrors the chilldown /
    // start-transient opt-in pattern: a checkbox plus four
    // NumericUpDown fields for channel count / width / depth / wall
    // thickness. Readouts surface the peak wall T, coolant outlet T,
    // total heat load.
    // `readonly` is dropped so assignments can move out of the inline
    // constructor block into BuildPreburnerCoolingGroup() /
    // BuildAerospikeCoolingGroup() in
    // RegenChamberForm.ConstructorGroups.cs (a partial-method-style
    // pattern; the helpers run during the constructor's execution but
    // the C# compiler doesn't allow readonly assignment from a method
    // even when it is called by the constructor). The fields are still
    // assigned exactly once at form construction; the missing readonly
    // keyword is a compile-time concession only, not a runtime change.
    private CheckBox chkPreburnerCooling = null!;
    private NumericUpDown nudPreburnerChCount = null!, nudPreburnerChWidth_mm = null!,
                          nudPreburnerChDepth_mm = null!, nudPreburnerWallT_mm = null!;
    private Label lblPreburnerWallT = null!, lblPreburnerCoolantOut = null!, lblPreburnerHeatLoad = null!;

    // Aerospike plug-channel regen-cooling opt-in controls. Same
    // field-shape rationale as the preburner controls above. The four
    // NumericUpDowns map to RegenChamberDesign.
    // AerospikePlugChannel{Count,Width_mm,Depth_mm} +
    // AerospikePlugWallThickness_mm; the checkbox maps to
    // IncludeAerospikeRegenCooling. Readouts piggy-back on the existing
    // lblAerospikePlug / lblAerospikeFace surfaces declared below.
    private CheckBox chkAerospikeCooling = null!;
    private NumericUpDown nudAerospikeChCount = null!, nudAerospikeChWidth_mm = null!,
                          nudAerospikeChDepth_mm = null!, nudAerospikeWallT_mm = null!,
                          nudPlugLengthRatio = null!, nudAerospikeContractionRatio = null!;

    // Linear (extruded) aerospike geometry controls.
    // Plug-transverse-width + design-intent aspect ratio.
    // Consumed by AerospikeOptimization.ToSpec only when
    // ChannelTopology = LinearAerospike (silent on every other
    // topology).
    private NumericUpDown nudLinearPlugWidth_mm = null!, nudLinearAspectRatio = null!;

    // Aerospike readouts. Populated
    // from RegenGenerationResult.Aerospike when the design's topology
    // is Aerospike; zeroed / dimmed otherwise. The existing regen-path
    // output rows (lblPeakT, lblMass, …) stay wired so UpdateResults
    // keeps a single code path; aerospike numbers get their own rows.
    private readonly Label lblAerospikePlug, lblAerospikeInjector, lblAerospikeFace;

    // LPBF printability opt-in controls. The
    // checkbox drives RegenChamberDesign.IncludeLpbfPrintabilityAnalysis
    // (opt-in for OVERHANG_ANGLE_EXCEEDED / TRAPPED_POWDER_REGION /
    // DRAIN_PATH_MISSING gates); the dropdown picks the
    // Geometry.LpbfAnalysis.LpbfMaterial profile driving per-alloy
    // overhang thresholds; the axis-override NumericUpDown binds to
    // RegenChamberDesign.LpbfPrintOrientationAxis_deg (-1 = auto).
    private CheckBox chkLpbfPrintability = null!;
    private ComboBox cboLpbfMaterial = null!;
    private NumericUpDown nudLpbfOrientationAxis = null!;
    private Label lblLpbfPrintability = null!;

    // Axial-profile chart: T_wg / T_wc / T_coolant / q vs x.
    private readonly AxialProfileChartPanel axialProfilePanel;

    // Tooltip wiring: single ToolTip instance shared across every form
    // input. Tooltip strings live in `ToolTipText`.
    private readonly System.Windows.Forms.ToolTip _tooltips;

    // "Run all analyses" master switch. When checked, flips on
    // chilldown + start-transient + ullage stackup + GasGenerator cycle
    // in one click and remembers the prior values so unchecking restores
    // them.
    private readonly CheckBox chkRunAllAnalyses;
    private RunAllSnapshot? _preRunAllSnapshot;

    // Marquee progress bar — shown in the status bar while
    // a long-running operation is in flight. Toggled heuristically by
    // SetStatus: any status message ending in an ellipsis ("…" or "...")
    // flips the marquee on; any other message flips it off.
    private readonly ProgressBar pbRegen;

    // Settings persistence. Loaded in the constructor, saved in
    // OnFormClosing. See `SessionSettings.cs`.
    private readonly SessionSettings _settings;

    // Design comparison panel. Hidden by default; the
    // user clicks "Compare with…" inside the panel to load a second
    // .rcd.json and see side-by-side deltas.
    private readonly DesignComparePanel comparePanel;
    // Tracks B's design separately from B's RegenGenerationResult so
    // the "Open B as A" button can re-apply B to the main form.
    private RegenChamberDesign? _comparePanelLastBDesign;

    // ── Inputs: film cooling ─────────────────────────────────────
    private CheckBox chkFilmEnable = null!;
    private NumericUpDown nudFilmFrac = null!, nudFilmSlotH = null!, nudFilmInjX = null!, nudFilmInletT = null!;
    private NumericUpDown nudFilmBurnL = null!, nudFilmDecay = null!, nudFilmThroatMix = null!;

    // ── Inputs: injector STL import ──────────────────────────────
    // Dropped `readonly` so the assignments can move into
    // BuildInjectorStlGroup() in ConstructorGroups.cs. Same
    // compile-time concession as the preburner / aerospike cooling
    // field block above.
    private CheckBox chkInjectorSTL = null!, chkInjectorSTLAutoCenter = null!;
    private TextBox txtInjectorSTLPath = null!;
    private Button btnBrowseInjectorSTL = null!;
    private NumericUpDown nudInjectorSTLOffsetX = null!, nudInjectorSTLScale = null!;

    // ── Inputs: proof-test ───────────────────────────────────────
    private NumericUpDown nudProofFactor = null!;
    private Button btnRunProofTest = null!;

    // ── Inputs: tolerance sweep ──────────────────────────────────
    private NumericUpDown nudTolSamples = null!, nudTolWall = null!, nudTolChannel = null!;
    private Button btnRunTolerance = null!;

    // ── Outputs ──────────────────────────────────────────────────
    private readonly Label lblThroatD, lblExitD, lblMassFlow, lblIsp;
    private readonly Label lblChamberL, lblTotalL, lblOD, lblMass, lblCost;
    private readonly Label lblPeakT, lblMargin, lblCoolantOut, lblDP, lblHeatLoad, lblThroatQ;
    private readonly Label lblFilmStatus, lblIspPenalty, lblAxialCoupling, lblConvergence;
    private readonly Label lblSF, lblStress, lblStructConfidence;
    // Hardware-validation overlay UI state.
    private readonly Label lblOverlaySummary, lblOverlayErrors, lblOverlayCalibration;
    private readonly Button btnApplyCalibration;
    private double _pendingCalibratedBartz = 1.0;
    private readonly Label lblStabilityChug, lblStabilityScreech, lblStabilityComposite;
    private readonly Label lblStabilityFreqs;
    private readonly Label lblProofPressure, lblProofSF, lblBurstMargin;
    private readonly Label lblTolPeakT, lblTolSF, lblTolDP, lblTolCoolantT, lblTolSummary;
    private readonly Label lblMinFeature, lblBuildTime;
    private readonly Label lblOverhangSummary, lblMaterialSource;
    private readonly Label lblSTLMessage;
    private readonly TextBox txtWarnings;
    private readonly Label lblStatus;

    private readonly Func<Structure.ProofTestResult?> _runProofTest;
    private readonly Func<Analysis.ToleranceResult?> _runTolerance;

    // ── Optimization controls ────────────────────────────────────
    private readonly NumericUpDown nudIterations, nudSeed;
    private readonly NumericUpDown nudMultiChainCount;
    private readonly ComboBox cboProfile;
    private readonly CheckBox chkWarmStart;
    private readonly CheckBox chkParallelSa;
    private readonly CheckBox chkMultiChainSa;
    private readonly CheckBox chkNsgaIi;
    private readonly NumericUpDown nudNsgaPopulation;
    private readonly NumericUpDown nudNsgaGenerations;
    private readonly ParetoScatterPanel paretoPanel;

    // SA convergence plot + tolerance histogram + status history.
    // Paint-only panels, same pattern as ParetoScatterPanel /
    // AxialProfileChartPanel.
    private readonly OptConvergencePanel optConvergencePanel;
    private readonly ToleranceHistogramPanel tolHistogramPanel;
    private readonly StatusHistoryBuffer _statusHistory;
    private readonly StatusHistoryPanel statusHistoryPanel;

    // Resource Budget — UI surface.
    private readonly System.Windows.Forms.Label    lblResourceGauge;
    // Projected memory + large-thrust indicator.
    private readonly System.Windows.Forms.Label    lblPreflightProjection;
    private readonly System.Windows.Forms.ComboBox cboResourceMode;
    private readonly System.Windows.Forms.NumericUpDown nudMaxParallelism;
    private readonly System.Windows.Forms.NumericUpDown nudMemoryBudget_MB;
    private readonly System.Windows.Forms.CheckBox chkDemotePriority;
    private readonly System.Windows.Forms.CheckBox chkBatteryAwareQuiet;
    private readonly System.Windows.Forms.CheckBox chkAdaptiveForegroundThrottle;
    private readonly System.Windows.Forms.CheckBox chkGcLatencyTuning;
    // When checked, editing any design/op-point input while a solve
    // is in flight posts a cancel request so the stale run unwinds
    // via OperationCanceledException.
    private readonly System.Windows.Forms.CheckBox chkAbortOpOnInputEdit;
    // Opt-in auto-coarsen voxel on memory-gate fail.
    private readonly System.Windows.Forms.CheckBox chkAutoCoarsenVoxel;
    // Fast-preview mode (channels skipped on manual Generate).
    private readonly System.Windows.Forms.CheckBox chkFastPreview;
    // Tiled Generate (axial-tiled voxel build).
    private readonly System.Windows.Forms.CheckBox        chkTileLargeBuilds;
    private readonly System.Windows.Forms.NumericUpDown   nudTileCount;
    // Experimental "Isolate large builds at Fail projection" scaffold.
    // Surfaced to the user and appended as an advisory hint on the
    // pre-flight label. Dispatch routing is still deferred pending
    // the viewer-from-subprocess UX decision.
    private readonly System.Windows.Forms.CheckBox        chkIsolateLargeBuilds;
    // Time budget knobs for tolerance sweep + SA. 0 = no cap.
    private readonly System.Windows.Forms.NumericUpDown nudSweepTimeoutSec;
    private readonly System.Windows.Forms.NumericUpDown nudOptTimeoutSec;
    private readonly ResourceAdaptation _resourceAdaptation;
    private long _lastCpuTimeTicks;
    private System.DateTime _lastCpuSampleAt = System.DateTime.MinValue;
    // 2026-04-22: batch optimization controls — pre-select settings once,
    // then walk away while SA runs and auto-saves the outputs.
    private readonly TextBox txtBatchFolder;
    private readonly CheckBox chkBatchSaveStl, chkBatchSaveReport, chkBatchSaveJson, chkBatchSaveParetoCsv;
    private readonly Button btnRunBatch;
    private readonly Button btnStartOpt, btnStopOpt;
    private Button btnGenerate = null!;   // assigned during the constructor; field so OnPropellantPairChanged can disable it
    private readonly Label lblOptProgress;
    private readonly ProgressBar pbOpt;
    private readonly System.Windows.Forms.Timer _pollTimer;

    // Keep the most-recent Pareto snapshot UI-side so
    // the new "Save Pareto CSV…" button can serialise it without a
    // round-trip to the task thread. Set by ApplyParetoFront when the
    // task thread pushes a new snapshot at FinalizeOpt.
    private IReadOnlyList<Optimization.ParetoPoint>? _lastParetoSnapshot;

    private bool _suppressParamEvents = false;

    public RegenChamberForm(
        Action<OperatingConditions, RegenChamberDesign> onParamsChanged,
        Action<string, float, bool> onExportStl,
        Action<string> onExport3MF,
        Action<string> onExportReport,
        Action<string> onExportVti,
        Action<string, OperatingConditions, RegenChamberDesign> onSaveDesign,
        Func<string, (OperatingConditions?, RegenChamberDesign?)> onLoadDesign,
        Action<OptSettings> onStartOpt,
        Action<OptSettings, BatchRunSettings> onStartBatch,
        Action onStopOpt,
        Func<OptProgress?> getOptProgress,
        Func<Structure.ProofTestResult?> runProofTest,
        Func<Analysis.ToleranceResult?> runTolerance,
        // Sprint render (2026-04-25): optional. Null → "Render image…" button is hidden.
        // Signature: (outputPath, material, mode, resolution, frames).
        Action<string, string, string, string, int>? onRenderImage = null)
    {
        _onParamsChanged = onParamsChanged;
        _onExportStl = onExportStl;
        _onRenderImage = onRenderImage;
        _onExport3MF = onExport3MF;
        _onExportReport = onExportReport;
        _onExportVti = onExportVti;
        _onSaveDesign = onSaveDesign;
        _onLoadDesign = onLoadDesign;
        _onStartOpt = onStartOpt;
        _onStartBatch = onStartBatch;
        _onStopOpt = onStopOpt;
        _getOptProgress = getOptProgress;
        _runProofTest = runProofTest;
        _runTolerance = runTolerance;

        Text = "Regen Chamber Designer — MVP";
        Font = new System.Drawing.Font("Segoe UI", 9f);
        AutoScaleDimensions = new System.Drawing.SizeF(7f, 15f);
        AutoScaleMode = AutoScaleMode.Font;
        // 2026-04-22 UX pass: form widened 1480 → 1580 and left column
        // widened 700 → 760 to accommodate the wider action buttons and
        // the new 780 px output labels in the right panel.
        Width = 1580; Height = 940;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;

        // Load persisted UI state. Window size + position restored
        // later (after layout) so the form shows up where the user
        // left it. Bad / missing settings → fresh defaults; never
        // throws.
        _settings = SessionSettings.Load();

        // Auto-probe on first run (or whenever the explicit caps are
        // still 0-defaults) so the memory + core caps match the host
        // machine. Persists only when a field actually changed.
        if (ResourceBudgetSettings.AutoProbeDefaults(_settings))
            _settings.Save();
        // Fold the user's budget into the global live snapshot every
        // form construction and whenever the preset or explicit caps
        // change at runtime.
        ResourceBudgetSettings.ApplySettings(_settings);

        if (_settings.WindowWidth > 200 && _settings.WindowHeight > 200)
        {
            Width  = _settings.WindowWidth;
            Height = _settings.WindowHeight;
        }
        if (_settings.WindowX != int.MinValue && _settings.WindowY != int.MinValue)
        {
            // Override the centred default; clamp into the primary
            // screen so a multi-monitor user who unplugged a display
            // doesn't lose the window off-screen.
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            int maxX = screen?.WorkingArea.Width  - 100 ?? 1024;
            int maxY = screen?.WorkingArea.Height - 100 ?? 768;
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(
                System.Math.Clamp(_settings.WindowX, 0, maxX),
                System.Math.Clamp(_settings.WindowY, 0, maxY));
        }

        // ── Layout roots ─────────────────────────────────────────
        var left = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoScroll = true,
            WrapContents = false, Width = 760, Dock = DockStyle.Left
        };
        var right = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoScroll = true,
            WrapContents = false, Dock = DockStyle.Fill
        };
        Controls.Add(right);
        Controls.Add(left);

        // ── Five input groups extracted to
        //    RegenChamberForm.ConstructorGroups.cs. Control fields are
        //    still assigned (inside each helper), just not inline here.
        left.Controls.Add(BuildConditionsGroup());
        left.Controls.Add(BuildNozzleGeometryGroup());
        left.Controls.Add(BuildCoolingChannelsGroup());
        left.Controls.Add(BuildFlangesGroup());
        left.Controls.Add(BuildInjectorInternalsGroup());
        left.Controls.Add(BuildFilmCoolingGroup());

        // UX pass: collapse low-frequency / tuning sections by default
        // — the user can expand them by clicking the header. This keeps
        // the most-edited controls (Operating Point, Nozzle, Cooling
        // Channels, Flanges, Film Cooling, Generate, Mesh Resolution,
        // Optimization, Batch) on screen without scrolling.
        //
        // The 8 groups below are extracted into BuildXxxGroup() helpers
        // in ConstructorGroups.cs. Action Buttons + Mesh resolution +
        // Export & save remain inline because of cross-control
        // dependencies (status-bar refresh, resource-budget adaptive
        // UI) — see ROADMAP.md.

        left.Controls.Add(BuildInjectorStlGroup());
        left.Controls.Add(BuildProofTestGroup());
        left.Controls.Add(BuildToleranceGroup());

        // ─────────────────────────────────────────────────────────
        //  Tier-2 follow-on UI groups
        //  All collapsed by default — first-open UI stays scannable.
        // ─────────────────────────────────────────────────────────

        left.Controls.Add(BuildChannelTopologyGroup());
        left.Controls.Add(BuildFeedSystemGroup());
        left.Controls.Add(BuildChilldownGroup());
        left.Controls.Add(BuildStartTransientGroup());
        left.Controls.Add(BuildEngineCycleGroup());

        // ── Preburner regen cooling and aerospike plug-channel regen
        //    cooling — both groups built by their respective helpers
        //    in RegenChamberForm.ConstructorGroups.cs.
        left.Controls.Add(BuildPreburnerCoolingGroup());
        left.Controls.Add(BuildAerospikeCoolingGroup());
        // Linear-aerospike geometry group.
        left.Controls.Add(BuildLinearAerospikeGroup());

        // ── LPBF printability — opt-in overhang / trapped-powder /
        //    drain-path analysis + per-alloy overhang thresholds.
        left.Controls.Add(BuildLpbfPrintabilityGroup());

        // ── Aerospike readouts (populated when
        // design.ChannelTopology == Aerospike). The group sits among
        // the output-column labels rather than the input column — it
        // is built + added to the right-hand panel elsewhere. Labels
        // only here so InitializeComponent-equivalent stays tidy.
        lblAerospikePlug     = Out("Aerospike plug: —");
        lblAerospikeInjector = Out("Aerospike injector: —");
        lblAerospikeFace     = Out("Aerospike face T: —");

        // ── Action buttons ───────────────────────────────────────
        btnGenerate = new Button { Text = "Generate", Width = 170, Height = 30 };
        btnGenerate.Click += (_, _) => PushParams();
        nudStlVoxel = Num(0.4, 0.05, 2.0, 0.05, 2);
        // Extracted handler bodies into named methods so the
        // keyboard-shortcut dispatcher (OnKeyDown below) can reuse them.
        var btnExportStl = new Button { Text = "Export STL…", Width = 170, Height = 30 };
        btnExportStl.Click += (_, _) => ExportStlViaDialog();
        // --monolithic checkbox. When checked, the
        // exporter fuses chamber + turbopump + feed manifold + preburner
        // into a single STL via MonolithicEngineBuilder.BuildFromDesign.
        // Unchecked (default) preserves the single-body per-topology
        // behaviour (bell via ChamberVoxelBuilder, aerospike via
        // AerospikeBuilder).
        chkExportMonolithic = new CheckBox
        {
            Text = "Monolithic (fused engine)",
            AutoSize = true,
            Checked = false,
        };
        // Tooltip wired inside WireTooltips (_tooltips isn't yet assigned here).
        var btnExport3MF = new Button { Text = "Export 3MF…", Width = 170, Height = 30 };
        btnExport3MF.Click += (_, _) => Export3MFViaDialog();
        var btnExportReport = new Button { Text = "Export Report…", Width = 170, Height = 30 };
        btnExportReport.Click += (_, _) => ExportReportViaDialog();
        var btnExportVti = new Button { Text = "Export CFD Fields…", Width = 170, Height = 30 };
        btnExportVti.Click += (_, _) => ExportVtiViaDialog();
        var btnSave = new Button { Text = "Save Design…", Width = 170, Height = 30 };
        btnSave.Click += (_, _) => SaveDesignViaDialog();
        var btnLoad = new Button { Text = "Load Design…", Width = 170, Height = 30 };
        btnLoad.Click += (_, _) => LoadDesignViaDialog();

        // Recent ▾ — drops a ContextMenuStrip showing the
        // last 10 .rcd.json paths from SessionSettings. Click → load
        // without going through the file dialog. Greys out paths
        // whose file no longer exists. Built on-demand each click so
        // a recently-saved design appears immediately.
        var btnRecent = new Button { Text = "Recent ▾", Width = 100, Height = 30 };
        btnRecent.Click += (_, _) =>
        {
            var menu = new ContextMenuStrip();
            if (_settings.RecentDesigns.Count == 0)
            {
                var item = new ToolStripMenuItem("(no recent designs)") { Enabled = false };
                menu.Items.Add(item);
            }
            else
            {
                foreach (var path in _settings.RecentDesigns)
                {
                    bool exists = System.IO.File.Exists(path);
                    string label = System.IO.Path.GetFileName(path);
                    var item = new ToolStripMenuItem(label)
                    {
                        ToolTipText = path,
                        Enabled = exists,
                    };
                    string capturedPath = path;     // closure-capture loop var
                    item.Click += (_, _) => LoadDesignByPath(capturedPath);
                    menu.Items.Add(item);
                }
                menu.Items.Add(new ToolStripSeparator());
                var clearItem = new ToolStripMenuItem("Clear list");
                clearItem.Click += (_, _) =>
                {
                    // Confirmation guards against accidental loss of the
                    // recent-designs history. The list is recoverable by
                    // re-opening files, but surfacing the prompt matches
                    // the blast-radius discipline applied to batch export
                    // and compare-load operations elsewhere in the form.
                    var res = MessageBox.Show(
                        this,
                        $"Clear all {_settings.RecentDesigns.Count} recent design entries?",
                        "Confirm clear",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2);
                    if (res != DialogResult.Yes) return;
                    _settings.RecentDesigns.Clear();
                    SetStatus("Recent designs cleared.");
                };
                menu.Items.Add(clearItem);
            }
            menu.Show(btnRecent, new System.Drawing.Point(0, btnRecent.Height));
        };

        // "Load Test Data…" — ingests a CSV with
        // measured cold-flow / hot-fire data, overlays predicted vs
        // measured, and surfaces a calibrated Bartz scaling factor the
        // user can apply with one click.
        var btnLoadTestData = new Button { Text = "Load Test Data…", Width = 170, Height = 30 };
        btnLoadTestData.Click += (_, _) => RunMeasuredDataOverlay();

        // 2026-04-22: "Live preview" checkbox — OFF suppresses the auto-
        // regeneration on every parameter edit. A voxel build is ~3–10 s
        // at the default 0.4 mm voxel, so editing five fields with live
        // preview ON costs 30+ s of wasted compute. OFF lets the user
        // stage all changes and click Generate exactly once.
        chkLivePreview = new CheckBox
        {
            Text = "Live preview (regen on every edit)",
            // Persisted across restarts via SessionSettings.
            Checked = _settings.LivePreviewEnabled,
            AutoSize = true,
        };

        // "Run all analyses" master switch. Flips the four
        // opt-in analysis toggles on together (chilldown + start transient
        // + ullage stackup + GasGenerator cycle) so a new user can see the
        // full analysis stack with one click. Unchecking restores the
        // pre-enable values via a saved snapshot.
        chkRunAllAnalyses = new CheckBox
        {
            Text = "Run all analyses (chilldown + start + ullage + GasGenerator)",
            Checked = false,
            AutoSize = true,
        };
        chkRunAllAnalyses.CheckedChanged += (_, _) => OnRunAllAnalysesToggled();

        // 2026-04-22 UX pass: split the single "Actions" group into three
        // smaller groups that are easier to scan. Generate + Live preview
        // lead (most-used controls on top); Mesh Resolution gets its own
        // prominent block so the preview-vs-export voxel distinction is
        // obvious; Export / Save actions come last.

        // ── Generate & preview group ───────────────────────────────
        var actionsGenerate = new FlowLayoutPanel
        { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Width = 660 };
        actionsGenerate.Controls.AddRange(new Control[] { btnGenerate });
        left.Controls.Add(Group("Generate & preview",
            actionsGenerate,
            chkLivePreview,
            chkRunAllAnalyses,
            MakeHelp("Live preview off = edits stage silently. Click Generate to commit. "
                   + "\"Run all analyses\" turns on chilldown, start-transient, ullage-stackup, "
                   + "and GasGenerator cycle in one click; unchecking restores the previous settings. "
                   + "Shortcuts: F5 / Ctrl+G = Generate, Ctrl+S = Save, Ctrl+O = Load, Ctrl+E = Export STL, Esc = Stop, F1 = About.")));

        // ── Mesh resolution group (prominent so users can find it) ─
        // The preview voxel is fixed for the session because PicoGK's
        // Library is a process-global singleton locked at startup
        // (see `Program.VoxelSizeMM`). The STL / 3MF export can still
        // ship at a FINER voxel — requests different from the session
        // voxel route through the headless StlExporter subprocess.
        var lblPreviewVoxel = new Label
        {
            Text = "Preview voxel (this session, fixed): 0.40 mm",
            AutoSize = false, Width = 660, Height = 22,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Italic),
            Padding = new Padding(4, 0, 0, 0),
        };
        left.Controls.Add(Group("Mesh resolution (preview vs. export)",
            lblPreviewVoxel,
            Row("Export voxel (mm) — finer = higher-fidelity STL", nudStlVoxel),
            MakeHelp(
                "Use the Export voxel to ship print-ready STLs at 0.10–0.20 mm while " +
                "keeping the in-app preview fast at 0.40 mm. Any export voxel != session " +
                "voxel re-voxelises in a headless subprocess so your preview stays responsive. " +
                "Range: 0.05–2.0 mm. Expect a few seconds at 0.30 mm, ~30 s at 0.20 mm, " +
                "several minutes at 0.10 mm.")));

        // ── Export & save group ────────────────────────────────────
        var actionsExport = new FlowLayoutPanel
        { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Width = 660 };
        // Sprint render (2026-04-25): "Render image…" button — invokes the
        // voxelforge-render subprocess with PBR copper / inconel / titanium
        // materials, still or turntable mode, low/high/maximum resolution.
        // Hidden when the renderer subprocess isn't wired (Program.cs decides
        // based on Blender discovery at startup).
        var btnRenderImage = new Button { Text = "Render image…", Width = 170, Height = 30 };
        btnRenderImage.Click += (_, _) => RenderImageViaDialog();
        btnRenderImage.Visible = _onRenderImage != null;
        actionsExport.Controls.AddRange(new Control[] { btnExportStl, btnExport3MF, btnExportReport, btnExportVti, btnRenderImage, chkExportMonolithic });
        var actionsSave = new FlowLayoutPanel
        { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Width = 660 };
        actionsSave.Controls.AddRange(new Control[] { btnSave, btnLoad, btnRecent, btnLoadTestData });
        left.Controls.Add(Group("Export & save",
            actionsExport,
            actionsSave));

        // ── Optimization panel ───────────────────────────────────
        nudIterations = Num(300, 50, 2000, 25, 0);
        nudSeed = Num(1, 1, 9999, 1, 0);
        cboProfile = new ComboBox { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var p in RegenChamberOptimization.Profiles) cboProfile.Items.Add(p.Name);
        cboProfile.SelectedIndex = 0;
        chkWarmStart = new CheckBox { Text = "Warm-start from current design", Checked = true, AutoSize = true };
        chkParallelSa = new CheckBox
        {
            Text = "Fast SA (8-way parallel, physics-only eval)",
            Checked = false,
            AutoSize = true,
        };
        chkMultiChainSa = new CheckBox
        {
            Text = "Multi-chain SA (N parallel chains, Sobol seeding)",
            Checked = true,   // Track A (2026-04-27): default-on after benchmark validation
            AutoSize = true,
        };
        // Track A (2026-04-27): explicit chain-count override. 0 = auto-scale
        // to MultiChainOptimizer.DefaultChainCount() (≈ ProcessorCount-2,
        // clamped 1-16). 1 = single-chain-via-multi-chain (handy for
        // determinism comparison). Upper bound 16 matches the library cap.
        nudMultiChainCount = Num(0, 0, 16, 1, 0);
        // Mutual-exclusion: multi-chain mode supersedes the (1+λ) batch path.
        // Keeping both checkboxes interactive is fine — Program.cs picks the
        // multi-chain path when chkMultiChainSa is checked, regardless of
        // chkParallelSa. We still grey out the batch toggle to make the
        // selection unambiguous to the user.
        chkMultiChainSa.CheckedChanged += (_, _) =>
        {
            chkParallelSa.Enabled = !chkMultiChainSa.Checked;
            if (chkMultiChainSa.Checked && chkNsgaIi is not null) chkNsgaIi.Checked = false;
        };

        // T2.4b (2026-04-30): NSGA-II multi-objective mode. Mutually exclusive
        // with both SA paths — when checked, the optimizer runs NsgaIIOptimizer
        // instead of SA. MaxIterations/Seed controls are reused for NSGA-II's
        // MaxGenerations/Seed; NsgaPopulationSize and NsgaMaxGenerations are
        // the NSGA-II-specific knobs.
        chkNsgaIi = new CheckBox
        {
            Text = "NSGA-II multi-objective (Pareto: peak-T, ΔP, mass)",
            Checked = false,
            AutoSize = true,
        };
        nudNsgaPopulation = Num(100, 4, 500, 4, 0);   // must be even; step 4 ensures it
        nudNsgaGenerations = Num(50, 1, 500, 1, 0);
        chkNsgaIi.CheckedChanged += (_, _) =>
        {
            bool nsga = chkNsgaIi.Checked;
            chkMultiChainSa.Enabled = !nsga;
            chkParallelSa.Enabled   = !nsga;
            if (nsga) chkMultiChainSa.Checked = false;
        };

        btnStartOpt = new Button { Text = "Start Optimization", Width = 180, Height = 30 };
        btnStopOpt = new Button { Text = "Stop", Width = 80, Height = 30, Enabled = false };
        btnStartOpt.Click += (_, _) => StartOpt();
        btnStopOpt.Click += (_, _) => { _onStopOpt(); btnStopOpt.Enabled = false; };

        lblOptProgress = new Label { Text = "", AutoSize = false, Width = 640, Height = 22, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
        pbOpt = new ProgressBar { Width = 640, Height = 18 };

        // "Save Pareto CSV…" — exports the current Pareto snapshot
        // to a `iteration,peak_wall_t_k,coolant_dp_pa,mass_g` file via the
        // canonical `ParetoFront.SaveToCsv` (same format as batch mode).
        var btnSaveParetoCsv = new Button { Text = "Save Pareto CSV…", Width = 170, Height = 30 };
        btnSaveParetoCsv.Click += (_, _) =>
        {
            var snap = _lastParetoSnapshot;
            if (snap is null || snap.Count == 0)
            { SetStatus("No Pareto front to export — finish an SA run first."); return; }
            using var dlg = new SaveFileDialog
            {
                Filter = "CSV|*.csv",
                FileName = $"pareto_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv",
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                int n = Optimization.ParetoFront.SaveToCsv(dlg.FileName, snap);
                SetStatus($"Wrote {n} Pareto points → {System.IO.Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex) { SetStatus("Pareto CSV save failed: " + ex.Message); }
        };

        var optBtns = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        optBtns.Controls.AddRange(new Control[] { btnStartOpt, btnStopOpt, btnSaveParetoCsv });

        paretoPanel = new ParetoScatterPanel
        {
            Width = 640, Height = 160,
            BorderStyle = BorderStyle.FixedSingle,
        };
        paretoPanel.PointSelected += pt =>
        {
            // TIER B.8: user clicked a Pareto point → load its params into
            // the form, same as ApplyOptResult does for the SA-best.
            ApplyOptResult(pt.Parameters);
            SetStatus($"Loaded Pareto point: peak T {pt.PeakWallT_K:F0} K, ΔP {pt.CoolantDP_Pa / 1e6:F2} MPa, mass {pt.Mass_g:F0} g (iter {pt.Iteration}).");
        };

        // SA convergence trace. Populated from each
        // PollOptProgress tick with the current best-so-far; reset on
        // StartOpt() so a restart re-draws from iteration 0.
        optConvergencePanel = new OptConvergencePanel
        {
            Width = 640, Height = 100,
            BorderStyle = BorderStyle.FixedSingle,
        };

        left.Controls.Add(Group("Optimization",
            Row("Max iterations", nudIterations),
            Row("Seed", nudSeed),
            Row("Scoring profile", cboProfile),
            chkWarmStart,
            chkParallelSa,
            chkMultiChainSa,
            Row("Multi-chain count (0 = auto)", nudMultiChainCount),
            chkNsgaIi,
            Row("NSGA-II population size", nudNsgaPopulation),
            Row("NSGA-II max generations", nudNsgaGenerations),
            optBtns,
            pbOpt,
            lblOptProgress,
            optConvergencePanel,
            paretoPanel));

        // ── Resource Budget ─────────────────────────────────────
        // Preset combo + explicit caps + behavioural toggles. The
        // preset combo is the headline control — users pick a mood,
        // not a number. The explicit caps are revealed for tweaking
        // under the combo; changing them flips the mode to Custom.
        // Every change fires ApplyResourceBudget() which pushes the
        // updated settings through ResourceBudgetSettings.ApplySettings.
        cboResourceMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 180,
        };
        cboResourceMode.Items.AddRange(new object[] { "Quiet", "Balanced", "Maximum", "Custom" });
        cboResourceMode.SelectedIndex = (int)_settings.ResourceMode;
        cboResourceMode.SelectedIndexChanged += (_, _) => OnResourceModeChanged();

        nudMaxParallelism = Num(_settings.MaxParallelism, 1,
                                System.Environment.ProcessorCount, 1, 0);
        nudMaxParallelism.ValueChanged += (_, _) => OnResourceKnobChanged();

        nudMemoryBudget_MB = Num(_settings.MemoryBudget_MB, 0, 1_048_576, 256, 0);
        nudMemoryBudget_MB.ValueChanged += (_, _) => OnResourceKnobChanged();

        chkDemotePriority = new CheckBox
        {
            Text = "Demote priority during solves",
            Checked = _settings.DemotePriorityDuringSolves, AutoSize = true,
        };
        chkDemotePriority.CheckedChanged += (_, _) => OnResourceKnobChanged();

        chkBatteryAwareQuiet = new CheckBox
        {
            Text = "On battery: auto-flip to Quiet",
            Checked = _settings.BatteryAwareQuiet, AutoSize = true,
        };
        chkBatteryAwareQuiet.CheckedChanged += (_, _) => OnResourceKnobChanged();

        chkAdaptiveForegroundThrottle = new CheckBox
        {
            Text = "Scale down when form loses focus",
            Checked = _settings.AdaptiveForegroundThrottle, AutoSize = true,
        };
        chkAdaptiveForegroundThrottle.CheckedChanged += (_, _) => OnResourceKnobChanged();

        chkGcLatencyTuning = new CheckBox
        {
            Text = "GC latency tuning during heavy work",
            Checked = _settings.GcLatencyTuning, AutoSize = true,
        };
        chkGcLatencyTuning.CheckedChanged += (_, _) => OnResourceKnobChanged();

        // Opt-in "input edit cancels stale solve".
        // When a user nudges a design NUD or a combo while SA / a
        // tolerance sweep is running, the current run's outputs are
        // about to be thrown away anyway — cancel it and free the
        // cores for whatever the user queues next.
        chkAbortOpOnInputEdit = new CheckBox
        {
            Text = "Cancel in-flight solve on input edit",
            Checked = _settings.AbortOpOnInputEdit, AutoSize = true,
        };
        chkAbortOpOnInputEdit.CheckedChanged += (_, _) => OnResourceKnobChanged();

        // Opt-in auto-coarsen of
        // the voxel size whenever the memory-projection gate would block
        // the build. Lets large-thrust designs render at lower fidelity
        // instead of being refused outright. Default off preserves the strict
        // strict behaviour for users who want explicit control.
        chkAutoCoarsenVoxel = new CheckBox
        {
            Text = "Auto-coarsen voxel to fit memory budget",
            Checked = _settings.AutoCoarsenVoxelToFitBudget, AutoSize = true,
        };
        chkAutoCoarsenVoxel.CheckedChanged += (_, _) => OnResourceKnobChanged();

        // "Fast preview" — skip channel voxelisation on
        // manual Generate only. Slashes build time by ~84 % on the default
        // design (baseline median 35.9 s → ~6 s at 0.4 mm voxel) so the
        // user can iterate on geometry / thrust / material without paying
        // the full LPBF-ready flow-path cost. SA / Save / Export paths
        // ignore the flag so a committed design still builds in full.
        chkFastPreview = new CheckBox
        {
            Text = "Fast preview (skip channels on Generate)",
            Checked = _settings.FastPreviewMode, AutoSize = true,
        };
        chkFastPreview.CheckedChanged += (_, _) => OnResourceKnobChanged();

        // "Tile large builds" — Generate dispatches
        // through ChamberAxialTileBuilder.BuildTiled with TileCount axial
        // slices. Peak memory per-tile ≈ 1/N; lets large-thrust designs
        // render at full voxel fidelity on a capped budget where monolithic
        // would be blocked.
        chkTileLargeBuilds = new CheckBox
        {
            Text = "Tile large builds (split Generate into N axial slices)",
            Checked = _settings.TileLargeBuilds, AutoSize = true,
        };
        chkTileLargeBuilds.CheckedChanged += (_, _) => OnResourceKnobChanged();

        nudTileCount = Num(_settings.TileCount, 1, 32, 1, 0);
        nudTileCount.ValueChanged += (_, _) => OnResourceKnobChanged();

        // Surface the IsolateLargeBuildsAtFailProjection scaffold flag
        // in the Resource Budget group. The subprocess-isolation infrastructure
        // (BuildSubprocess.Run + Job Object memory cap + exit-code translation)
        // is ready; this checkbox persists the user's preference through
        // session.json and appends an advisory hint to the pre-flight label
        // when the projection is Fail + this flag is on. Full Generate-via-
        // subprocess dispatch is scheduled for a follow-on — it requires the
        // viewer to render from STL instead of live voxels, which is a
        // UX-policy decision orthogonal to the infrastructure.
        chkIsolateLargeBuilds = new CheckBox
        {
            Text = "Isolate large builds at Fail projection (scaffold)",
            Checked = _settings.IsolateLargeBuildsAtFailProjection, AutoSize = true,
        };
        chkIsolateLargeBuilds.CheckedChanged += (_, _) => OnResourceKnobChanged();

        // Time-budget NUDs. 0 = no cap. Range
        // 0..86400 (1 day) is ample; typical user value 60..300. The
        // increment of 30 s is a coarse grid so a keystroke doesn't
        // wander into meaningless sub-minute differences.
        nudSweepTimeoutSec = Num(_settings.SweepTimeoutSeconds, 0, 86400, 30, 0);
        nudSweepTimeoutSec.ValueChanged += (_, _) => OnResourceKnobChanged();

        nudOptTimeoutSec = Num(_settings.OptTimeoutSeconds, 0, 86400, 60, 0);
        nudOptTimeoutSec.ValueChanged += (_, _) => OnResourceKnobChanged();

        left.Controls.Add(Group("Resource Budget", startCollapsed: true,
            Row("Preset",        cboResourceMode),
            Row("Max parallel cores", nudMaxParallelism),
            Row("Memory cap (MB, 0=preset)", nudMemoryBudget_MB),
            chkDemotePriority,
            chkBatteryAwareQuiet,
            chkAdaptiveForegroundThrottle,
            chkGcLatencyTuning,
            chkAbortOpOnInputEdit,
            chkAutoCoarsenVoxel,
            chkFastPreview,
            chkTileLargeBuilds,
            Row("Tile count (1-32)", nudTileCount),
            chkIsolateLargeBuilds,
            Row("Sweep timeout (s, 0=no cap)", nudSweepTimeoutSec),
            Row("Opt timeout (s, 0=no cap)",   nudOptTimeoutSec)));

        // ── Batch optimization ──────────────────────────────────
        // 2026-04-22: hands-off mode. User sets everything above, picks a
        // folder here, and clicks Run Batch & Save. SA runs with the
        // current settings; on finalize, the outputs (design JSON, STL
        // at session voxel, text report, optional Pareto CSV) are
        // auto-written to the folder with a shared timestamp prefix.
        txtBatchFolder = new TextBox
        {
            Width = 460, Height = 22, ReadOnly = true,
            PlaceholderText = "(choose an output folder before Run Batch…)",
        };
        var btnBrowseBatch = new Button { Text = "Browse…", Width = 90, Height = 28 };
        btnBrowseBatch.Click += (_, _) => BrowseBatchFolder();

        chkBatchSaveJson      = new CheckBox { Text = "Save design .rcd.json",           Checked = true, AutoSize = true };
        chkBatchSaveStl       = new CheckBox { Text = "Save STL (session voxel)",         Checked = true, AutoSize = true };
        chkBatchSaveReport    = new CheckBox { Text = "Save text report",                 Checked = true, AutoSize = true };
        chkBatchSaveParetoCsv = new CheckBox { Text = "Save Pareto CSV",                   Checked = false, AutoSize = true };

        btnRunBatch = new Button { Text = "Run Batch & Save", Width = 180, Height = 30 };
        btnRunBatch.Click += (_, _) => StartBatchRun();

        var batchFolderRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Width = 660 };
        batchFolderRow.Controls.AddRange(new Control[] { txtBatchFolder, btnBrowseBatch });

        left.Controls.Add(Group("Batch optimization (automated run + save)",
            batchFolderRow,
            chkBatchSaveJson,
            chkBatchSaveStl,
            chkBatchSaveReport,
            chkBatchSaveParetoCsv,
            btnRunBatch));

        // ── Outputs on right ─────────────────────────────────────
        lblThroatD = Out("D_throat: —");
        lblExitD = Out("D_exit: —");
        lblMassFlow = Out("ṁ total: —");
        lblIsp = Out("Isp (vac/sl): —");

        right.Controls.Add(Group("Derived",
            lblThroatD, lblExitD, lblMassFlow, lblIsp));

        lblChamberL = Out("L_chamber: —");
        lblTotalL = Out("L_total: —");
        lblOD = Out("OD: —");
        lblMass = Out("Mass: —");
        lblCost = Out("Print cost est.: —");
        right.Controls.Add(Group("Geometry",
            lblChamberL, lblTotalL, lblOD, lblMass, lblCost));

        lblPeakT = Out("Peak wall T: —");
        lblMargin = Out("Wall T margin: —");
        lblCoolantOut = Out("Coolant T out: —");
        lblDP = Out("Coolant ΔP: —");
        lblHeatLoad = Out("Total heat load: —");
        lblThroatQ = Out("Throat heat flux: —");
        lblFilmStatus = Out("Film: disabled");
        lblIspPenalty = Out("Isp penalty: 0 %");
        lblAxialCoupling = Out("Axial conduction RMS: 0 W/m²");
        lblConvergence = Out("Convergence: —");
        // Per-station axial profile chart. Reads from
        // gen.Thermal.Stations and overlays T_wg / T_wc / T_coolant
        // (left axis K) + q (right axis MW/m²) against contour x.
        axialProfilePanel = new AxialProfileChartPanel
        { Width = 780, Height = 220, BorderStyle = BorderStyle.FixedSingle };
        right.Controls.Add(Group("Thermal",
            lblPeakT, lblMargin, lblCoolantOut, lblDP, lblHeatLoad, lblThroatQ,
            lblFilmStatus, lblIspPenalty, lblAxialCoupling, lblConvergence,
            axialProfilePanel));

        lblSF = Out("Min safety factor (hot): —");
        lblStress = Out("Peak stress (VM): —");
        lblStructConfidence = Out("Confidence: —");
        right.Controls.Add(Group("Structure", lblSF, lblStress, lblStructConfidence));

        // ── Combustion stability (traffic-light screening) ──────────
        // Three pills (chug / screech / composite) + a small frequency
        // summary line. Preliminary-design fidelity — screening only.
        lblStabilityChug      = Pill("Chug: —");
        lblStabilityScreech   = Pill("Screech: —");
        lblStabilityComposite = Pill("Composite: —");
        lblStabilityFreqs     = Out("L1 — Hz  |  T1 — Hz  |  T2 — Hz  |  c — m/s");
        var stabilityPills = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Width = 660 };
        stabilityPills.Controls.AddRange(new Control[] { lblStabilityChug, lblStabilityScreech, lblStabilityComposite });
        right.Controls.Add(Group("Combustion Stability (preliminary screening)",
            stabilityPills,
            lblStabilityFreqs));

        lblProofPressure = Out("Proof pressure: —");
        lblProofSF = Out("Proof min SF: —");
        lblBurstMargin = Out("Elastic burst margin: —");
        right.Controls.Add(Group("Proof Test (last run)", startCollapsed: true,
            lblProofPressure, lblProofSF, lblBurstMargin));

        lblTolSummary = Out("Tolerance sweep: not run");
        lblTolPeakT = Out("Peak T  p50/p90/p99: —");
        lblTolSF = Out("Min SF  p10/p50:       —");
        lblTolDP = Out("\u0394P     p50/p90:       —");
        lblTolCoolantT = Out("T_out   p50/p90:       —");
        // Histogram of the per-sample distribution.
        // Fed via SetResult in the render method that already updates
        // the four lbl* above. ComboBox inside the panel picks which
        // of the four traces to render.
        tolHistogramPanel = new ToleranceHistogramPanel
        {
            Width = 760, Height = 160,
            BorderStyle = BorderStyle.FixedSingle,
        };
        right.Controls.Add(Group("Tolerance Sweep (last run)", startCollapsed: true,
            lblTolSummary, lblTolPeakT, lblTolSF, lblTolDP, lblTolCoolantT,
            tolHistogramPanel));

        lblMinFeature = Out("Min feature size: —");
        lblBuildTime = Out("Est. build time: —");
        lblOverhangSummary = Out("Overhang: —");
        lblMaterialSource = Out("Material source: —");
        lblSTLMessage = Out("STL: disabled");
        right.Controls.Add(Group("Manufacturing",
            lblMinFeature, lblBuildTime, lblOverhangSummary, lblMaterialSource, lblSTLMessage));

        // Sprint 10 Track A (2026-04-23) — aerospike readouts. The group
        // stays visible on every design (Axial / TPMS / Aerospike); rows
        // populate with "— (regen path)" when the topology is not
        // aerospike so users see a clear no-op rather than stale numbers.
        right.Controls.Add(Group("Aerospike (Sprint 10 readouts)",
            lblAerospikePlug, lblAerospikeInjector, lblAerospikeFace));

        txtWarnings = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Width = 760,   // 2026-04-22 UX pass: match widened Out() labels
            Height = 150,
            WordWrap = true
        };
        right.Controls.Add(Group("Warnings / Notes", txtWarnings));

        // Design comparison group (collapsed by default).
        comparePanel = new DesignComparePanel();
        comparePanel.LoadAndGenerate = path =>
        {
            // Load the second design via the host's load callback,
            // then generate via the fast path (skipVoxelGeometry: true)
            // so the diff is sub-second. The fast path doesn't touch
            // PicoGK voxels so it's safe on the UI thread.
            try
            {
                var (c, d) = _onLoadDesign(path);
                if (c is null || d is null) return null;
                _comparePanelLastBDesign = d;
                var gen = RegenChamberOptimization.GenerateWith(c, d, skipVoxelGeometry: true);
                comparePanel.SetB(gen, d);
                return gen;
            }
            catch (Exception ex)
            {
                SetStatus("Compare load failed: " + ex.Message);
                return null;
            }
        };
        comparePanel.OpenB = (cond, design) => ApplyDesign(cond, design);
        right.Controls.Add(Group("Design comparison", startCollapsed: true,
            comparePanel));

        // Measured-data overlay results panel. Populated
        // after a CSV is loaded via the "Load Test Data…" action. Stays
        // blank until the first load.
        lblOverlaySummary     = Out("Overlay: (no test data loaded)");
        lblOverlayErrors      = Out("% errors: —");
        lblOverlayCalibration = Out("Calibration: —");
        btnApplyCalibration   = new Button { Text = "Apply calibrated Bartz factor", Width = 260, Height = 28, Enabled = false };
        btnApplyCalibration.Click += (_, _) => ApplyCalibratedBartzFactor();
        right.Controls.Add(Group("Hardware validation overlay", startCollapsed: true,
            lblOverlaySummary, lblOverlayErrors, lblOverlayCalibration, btnApplyCalibration));

        // Status bar is now a Panel hosting the existing
        // text Label plus a small marquee ProgressBar on the right. The
        // marquee is driven by SetStatus via the ellipsis heuristic —
        // any "…" / "..." ending flips it on, any other message flips it
        // off. No change to the existing SetStatus / SetFormStatus API.
        lblStatus = new Label
        {
            Text = "Ready.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Padding = new Padding(6, 0, 0, 0),
        };
        pbRegen = new ProgressBar
        {
            Dock = DockStyle.Right,
            Width = 150,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 0,
            Visible = false,
        };
        // Live resource gauge docked to the left of the
        // marquee. Compact "MEM: 2.1 GB · CPU: 42 %" label updated
        // every poll tick via the existing `_pollTimer`. Gives the
        // user a permanent view of whether the machine is under
        // pressure without opening Task Manager.
        lblResourceGauge = new Label
        {
            Dock = DockStyle.Right,
            // UX fix: 210 → 280 so "MEM: 131072/131072 MB  CPU: 100 %"
            // on a 128 GB workstation doesn't silently truncate. AutoEllipsis
            // added as a belt-and-braces guard against locale-widened digits.
            Width = 280,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = System.Drawing.Color.DimGray,
            Font = new System.Drawing.Font("Segoe UI", 8.25f),
            Text = "MEM: \u2014  CPU: \u2014",
            Padding = new Padding(4, 0, 0, 0),
        };
        // Pre-flight projection indicator — sits to
        // the LEFT of the live resource gauge and shows what the NEXT
        // Generate is projected to consume. Updated on every input-change
        // via OnParamsChanged → UpdatePreflightProjection. Colour-codes
        // dim-gray / dark-orange / firebrick at 0-70 / 70-100 / > 100 %
        // of budget. The live gauge (right) tracks ACTUAL usage during
        // a running op; this label tracks PROJECTED usage for what's
        // about to be clicked.
        lblPreflightProjection = new Label
        {
            Dock = DockStyle.Right,
            Width = 300,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = System.Drawing.Color.DimGray,
            Font = new System.Drawing.Font("Segoe UI", 8.25f),
            Text = "Next Generate: \u2014",
            Padding = new Padding(4, 0, 0, 0),
        };
        var statusBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            BorderStyle = BorderStyle.FixedSingle,
        };
        statusBar.Controls.Add(lblStatus);
        statusBar.Controls.Add(lblResourceGauge);
        statusBar.Controls.Add(lblPreflightProjection);
        statusBar.Controls.Add(pbRegen);
        Controls.Add(statusBar);

        // Rolling buffer of status messages with an
        // expandable list. Docked directly above the status bar so the
        // toggle lives next to the current status line the user is
        // already watching. Collapsed by default; auto-expands on new
        // entries and auto-collapses after 5 s of quiet (unless the
        // user has manually pinned it open).
        _statusHistory = new StatusHistoryBuffer(capacity: 20);
        statusHistoryPanel = new StatusHistoryPanel(_statusHistory)
        {
            Dock = DockStyle.Bottom,
        };
        Controls.Add(statusHistoryPanel);

        // Help → About menu. MenuStrip docks to the top of
        // the form; the existing left/right FlowLayoutPanels (Dock=Left
        // + Dock=Fill) naturally yield 24 px to the menu since docked
        // controls are laid out in reverse add order.
        var menuStrip = new MenuStrip();
        var helpMenu = new ToolStripMenuItem("&Help");
        var aboutItem = new ToolStripMenuItem("&About…", null,
            (_, _) => AboutDialog.Show(this))
        { ShortcutKeys = Keys.F1 };
        helpMenu.DropDownItems.Add(aboutItem);
        menuStrip.Items.Add(helpMenu);
        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);     // added last → docked FIRST (top edge)

        // ── Wire change events ──────────────────────────────────
        WireAllParamEvents();

        // ── Poll timer for opt progress ─────────────────────────
        // Battery awareness + foreground throttle.
        // Lifetime-bound to the form so the timer + event handlers die
        // cleanly on close. ResourceAdaptation is a no-op when neither
        // toggle is enabled in settings.
        _resourceAdaptation = new ResourceAdaptation(_settings, this);

        _pollTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _pollTimer.Tick += (_, _) => PollOptProgress();
        _pollTimer.Start();

        // Prime the pre-flight projection once on form
        // construction so the indicator starts populated rather than
        // waiting for the first MaybePush.
        UpdatePreflightProjection();

        // Hover tooltips on every input. One ToolTip
        // instance + .SetToolTip(control, text) for each field —
        // strings live in `ToolTipText` so this method stays a
        // pure binding step.
        _tooltips = new System.Windows.Forms.ToolTip
        {
            AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 100,
            ShowAlways = true,
        };
        WireTooltips();

        // Form-level keyboard shortcuts. KeyPreview routes
        // every keypress through the form before the focused child, so a
        // single KeyDown handler can dispatch to the same named methods
        // the toolbar buttons use. Mappings live in `ShortcutRouter`.
        KeyPreview = true;
        KeyDown += OnFormKeyDown;

        // Drag-and-drop file load. AllowDrop enables the drop
        // target; DragEnter accepts files the router recognises; DragDrop
        // routes each by extension. Routing lives in `DragDropRouter`.
        AllowDrop = true;
        DragEnter += OnFormDragEnter;
        DragDrop  += OnFormDragDrop;

        // Initial push so user sees geometry immediately
        Load += (_, _) => PushParams();
    }

    // ═══════════════════════════════════════════════════════════════
    //   Form-level input event handlers
    // ═══════════════════════════════════════════════════════════════

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        var action = ShortcutRouter.Resolve(e.KeyData);
        if (action == ShortcutRouter.Action.None) return;

        switch (action)
        {
            case ShortcutRouter.Action.Generate:
                if (btnGenerate.Enabled) PushParams();
                break;
            case ShortcutRouter.Action.SaveDesign:
                SaveDesignViaDialog();
                break;
            case ShortcutRouter.Action.LoadDesign:
                LoadDesignViaDialog();
                break;
            case ShortcutRouter.Action.ExportStl:
                ExportStlViaDialog();
                break;
            case ShortcutRouter.Action.StopOpt:
                StopOptIfRunning();
                break;
            case ShortcutRouter.Action.About:
                AboutDialog.Show(this);
                break;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private static void OnFormDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data is null) { e.Effect = DragDropEffects.None; return; }
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effect = DragDropEffects.None;
            return;
        }
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files is null || files.Length == 0)
        {
            e.Effect = DragDropEffects.None;
            return;
        }
        // Accept if any file matches a known target.
        foreach (var f in files)
            if (DragDropRouter.Resolve(f) != DragDropRouter.Target.None)
            { e.Effect = DragDropEffects.Copy; return; }
        e.Effect = DragDropEffects.None;
    }

    private void OnFormDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data is null) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files is null) return;
        foreach (var f in files)
        {
            switch (DragDropRouter.Resolve(f))
            {
                case DragDropRouter.Target.DesignJson:
                    LoadDesignByPath(f);
                    return;     // one design per drop
                case DragDropRouter.Target.InjectorStl:
                    RouteDroppedInjectorStl(f);
                    return;     // one STL per drop
                case DragDropRouter.Target.MeasuredData:
                    // Fall back to the existing Measured-data overlay.
                    // That helper owns its own file dialog; we inline the
                    // single-file-path version here so drag-drop does NOT
                    // require a second user click.
                    SetStatus($"Drag-drop CSV: opening {System.IO.Path.GetFileName(f)} via Load Test Data flow.");
                    // Non-trivial to split the existing method; fall back
                    // to the dialog for now so the routing still "works".
                    RunMeasuredDataOverlay();
                    return;
            }
        }
    }

    private void RouteDroppedInjectorStl(string path)
    {
        _suppressParamEvents = true;
        try
        {
            txtInjectorSTLPath.Text = path;
            chkInjectorSTL.Checked = true;
        }
        finally { _suppressParamEvents = false; }
        SetStatus($"Injector STL set: {System.IO.Path.GetFileName(path)}");
        MaybePush();
    }

    /// <summary>
    /// Bind hover-help to every input on the form. New
    /// fields should get a tooltip string in <see cref="ToolTipText"/>
    /// and one line here. Skips fields whose meaning is obvious from
    /// the row label (e.g. "Browse…" buttons).
    /// </summary>
    private void WireTooltips()
    {
        // Operating Point group.
        _tooltips.SetToolTip(cboPropellantPair,  ToolTipText.PropellantPair);
        _tooltips.SetToolTip(nudThrustN,         ToolTipText.ThrustN);
        _tooltips.SetToolTip(nudPcPsi,           ToolTipText.ChamberPressurePsi);
        _tooltips.SetToolTip(nudMR,              ToolTipText.MixtureRatio);
        _tooltips.SetToolTip(nudCoolTK,          ToolTipText.CoolantInletTempK);
        _tooltips.SetToolTip(nudCoolPMPa,        ToolTipText.CoolantInletPressureMPa);
        _tooltips.SetToolTip(cboMaterial,        ToolTipText.WallMaterial);
        _tooltips.SetToolTip(nudBartzFactor,     ToolTipText.BartzFactor);

        // Nozzle Geometry group.
        _tooltips.SetToolTip(nudContraction,     ToolTipText.ContractionRatio);
        _tooltips.SetToolTip(nudExpansion,       ToolTipText.ExpansionRatio);
        _tooltips.SetToolTip(nudLStar,           ToolTipText.LStar);
        _tooltips.SetToolTip(nudThetaN,          ToolTipText.ThetaN);
        _tooltips.SetToolTip(nudThetaE,          ToolTipText.ThetaE);
        _tooltips.SetToolTip(nudBellFrac,        ToolTipText.BellLengthFraction);

        // Cooling Channels group.
        _tooltips.SetToolTip(nudChannelCount,    ToolTipText.ChannelCount);
        _tooltips.SetToolTip(nudHChamber,        ToolTipText.ChannelHeightChamber);
        _tooltips.SetToolTip(nudHThroat,         ToolTipText.ChannelHeightThroat);
        _tooltips.SetToolTip(nudHExit,           ToolTipText.ChannelHeightExit);
        _tooltips.SetToolTip(nudRib,             ToolTipText.RibThickness);
        _tooltips.SetToolTip(nudWall,            ToolTipText.WallThickness);
        _tooltips.SetToolTip(nudJacket,          ToolTipText.JacketThickness);
        _tooltips.SetToolTip(nudSmoothing,       ToolTipText.SmoothingRadius);
        _tooltips.SetToolTip(nudManifoldL,       ToolTipText.ManifoldLength);
        _tooltips.SetToolTip(nudPortD,           ToolTipText.CoolantPortDiameter);
        _tooltips.SetToolTip(cboCoolantPortStd,  ToolTipText.CoolantPortThread);
        _tooltips.SetToolTip(nudChannelFillet,   ToolTipText.ChannelManifoldFillet);

        // Flanges group.
        _tooltips.SetToolTip(nudFlangeThk,       ToolTipText.InjectorFlangeThk);
        _tooltips.SetToolTip(nudFlangeORFactor,  ToolTipText.InjectorFlangeORFactor);
        _tooltips.SetToolTip(nudPropPortD,       ToolTipText.PropellantPortDia);
        _tooltips.SetToolTip(cboPropPortStd,     ToolTipText.PropellantPortThread);
        _tooltips.SetToolTip(nudMountThk,        ToolTipText.MountFlangeThk);
        _tooltips.SetToolTip(cboMountFlangeStd,  ToolTipText.MountBoltPattern);

        // Film Cooling group.
        _tooltips.SetToolTip(nudFilmFrac,        ToolTipText.FilmFraction);
        _tooltips.SetToolTip(nudFilmSlotH,       ToolTipText.FilmSlotH);
        _tooltips.SetToolTip(nudFilmInjX,        ToolTipText.FilmInjectionX);
        _tooltips.SetToolTip(nudFilmInletT,      ToolTipText.FilmInletT);
        _tooltips.SetToolTip(nudFilmBurnL,       ToolTipText.FilmBurnoutL);
        _tooltips.SetToolTip(nudFilmDecay,       ToolTipText.FilmDecayCoef);
        _tooltips.SetToolTip(nudFilmThroatMix,   ToolTipText.FilmThroatMix);

        // Optimisation group.
        _tooltips.SetToolTip(nudIterations,      ToolTipText.MaxIterations);
        _tooltips.SetToolTip(nudSeed,            ToolTipText.Seed);
        _tooltips.SetToolTip(cboProfile,         ToolTipText.ScoringProfile);
        _tooltips.SetToolTip(chkWarmStart,       ToolTipText.WarmStart);
        _tooltips.SetToolTip(chkParallelSa,      ToolTipText.ParallelSa);
        _tooltips.SetToolTip(chkMultiChainSa,    ToolTipText.MultiChainSa);
        _tooltips.SetToolTip(chkNsgaIi,          ToolTipText.NsgaIi);

        // Mesh resolution group.
        _tooltips.SetToolTip(nudStlVoxel,        ToolTipText.StlExportVoxel);
        _tooltips.SetToolTip(chkExportMonolithic, ToolTipText.ExportMonolithic);

        // Proof / tolerance.
        _tooltips.SetToolTip(nudProofFactor,     ToolTipText.ProofFactor);
        _tooltips.SetToolTip(nudTolSamples,      ToolTipText.TolSamples);
        _tooltips.SetToolTip(nudTolWall,         ToolTipText.TolWall);
        _tooltips.SetToolTip(nudTolChannel,      ToolTipText.TolChannel);

        // Follow-on groups.
        _tooltips.SetToolTip(nudTankUllage_MPa,  ToolTipText.TankUllageMPa);
        _tooltips.SetToolTip(cboFilterStd,       ToolTipText.FilterPreset);
        _tooltips.SetToolTip(nudFilterContamination, ToolTipText.FilterContamination);
        _tooltips.SetToolTip(cboUmbilicalStd,    ToolTipText.UmbilicalStandard);
        _tooltips.SetToolTip(cboTopology,        ToolTipText.ChannelTopology);

        _tooltips.SetToolTip(chkChilldownEnable, ToolTipText.ChilldownEnable);
        _tooltips.SetToolTip(nudChilldownInitT,  ToolTipText.ChilldownInitT);
        _tooltips.SetToolTip(nudChilldownHTC,    ToolTipText.ChilldownHTC);
        _tooltips.SetToolTip(nudChilldownDoneDT, ToolTipText.ChilldownDoneDT);
        _tooltips.SetToolTip(nudChilldownMaxT,   ToolTipText.ChilldownMaxT);

        _tooltips.SetToolTip(chkStartTransient,  ToolTipText.StartTransientEnable);
        _tooltips.SetToolTip(nudValveOpen_ms,    ToolTipText.ValveOpenMs);
        _tooltips.SetToolTip(nudOxValveOpen_ms,  ToolTipText.OxValveOpenMs);
        _tooltips.SetToolTip(nudFuelValveOpen_ms,ToolTipText.FuelValveOpenMs);
        _tooltips.SetToolTip(nudIgniterDelay_ms, ToolTipText.IgniterDelayMs);
        _tooltips.SetToolTip(nudStartSimDur_ms,  ToolTipText.StartSimDurMs);
        _tooltips.SetToolTip(nudStartSimDt_ms,   ToolTipText.StartSimDtMs);
        _tooltips.SetToolTip(nudHardStartFactor, ToolTipText.HardStartFactor);

        _tooltips.SetToolTip(cboEngineCycle,     ToolTipText.EngineCycle);
        _tooltips.SetToolTip(nudPumpInletP_MPa,  ToolTipText.PumpInletPMPa);
        _tooltips.SetToolTip(nudPumpDischargeP_MPa, ToolTipText.PumpDischargePMPa);
        _tooltips.SetToolTip(nudPumpEff,         ToolTipText.PumpEfficiency);

        // Checkbox tooltips (the main U1.2
        // sweep covered NUDs + combos only). Same SetToolTip pattern.
        _tooltips.SetToolTip(chkManifolds,       ToolTipText.IncludeManifolds);
        _tooltips.SetToolTip(chkPorts,           ToolTipText.IncludeCoolantPorts);
        _tooltips.SetToolTip(chkInjectorFlange,  ToolTipText.IncludeInjectorFlange);
        _tooltips.SetToolTip(chkMountFlange,     ToolTipText.IncludeMountFlange);
        _tooltips.SetToolTip(chkFilmEnable,      ToolTipText.EnableFilmCooling);
        _tooltips.SetToolTip(chkInjectorSTL,     ToolTipText.ImportInjectorSTL);
        _tooltips.SetToolTip(chkInjectorSTLAutoCenter, ToolTipText.AutoCenterInjectorSTL);
        _tooltips.SetToolTip(chkLivePreview,     ToolTipText.LivePreview);
        _tooltips.SetToolTip(chkRunAllAnalyses,  ToolTipText.RunAllAnalyses);
        _tooltips.SetToolTip(chkDemotePriority,  ToolTipText.DemotePriority);
        _tooltips.SetToolTip(chkBatteryAwareQuiet,        ToolTipText.BatteryAwareQuiet);
        _tooltips.SetToolTip(chkAdaptiveForegroundThrottle, ToolTipText.AdaptiveForegroundThrottle);
        _tooltips.SetToolTip(chkGcLatencyTuning, ToolTipText.GcLatencyTuning);
        _tooltips.SetToolTip(chkAbortOpOnInputEdit, ToolTipText.AbortOpOnInputEdit);
        _tooltips.SetToolTip(chkAutoCoarsenVoxel,   ToolTipText.AutoCoarsenVoxel);
        _tooltips.SetToolTip(chkFastPreview,        ToolTipText.FastPreview);
        _tooltips.SetToolTip(chkTileLargeBuilds,    ToolTipText.TileLargeBuilds);
        _tooltips.SetToolTip(nudTileCount,          ToolTipText.TileCount);
        _tooltips.SetToolTip(chkIsolateLargeBuilds, ToolTipText.IsolateLargeBuildsAtFailProjection);

        WireAccessibilityNames();
    }

    /// <summary>
    /// Set <see cref="Control.AccessibleName"/> on the high-traffic input
    /// controls so screen readers announce a human-readable label rather
    /// than the auto-derived field name. Inputs sit in dense
    /// <c>Row(label, control)</c> pairs where the label isn't a
    /// <c>LabelFor</c>-style companion, so assistive tech can't walk
    /// from control to label on its own — this fills the gap.
    /// </summary>
    private void WireAccessibilityNames()
    {
        // Tuple form keeps the list compact and easy to extend. Only the
        // top ~30 user-facing inputs are covered; less-visited advanced
        // fields can be added as the form grows. Tooltips handle the
        // longer explanation; AccessibleName stays short + unit-aware.
        var map = new (Control ctl, string name)[]
        {
            (nudThrustN,           "Thrust in newtons"),
            (nudPcPsi,             "Chamber pressure in psia"),
            (nudMR,                "Mixture ratio"),
            (nudCoolTK,            "Coolant inlet temperature in kelvin"),
            (nudCoolPMPa,          "Coolant inlet pressure in megapascals"),
            (cboPropellantPair,    "Propellant pair"),
            (cboMaterial,          "Wall material"),
            (nudBartzFactor,       "Bartz scaling factor"),
            (nudContraction,       "Contraction ratio"),
            (nudExpansion,         "Expansion ratio"),
            (nudLStar,             "Characteristic length L-star in millimetres"),
            (nudThetaN,            "Bell entrance angle in degrees"),
            (nudThetaE,            "Bell exit angle in degrees"),
            (nudBellFrac,          "Bell length fraction"),
            (nudChannelCount,      "Channel count"),
            (nudHChamber,          "Channel height at chamber in millimetres"),
            (nudHThroat,           "Channel height at throat in millimetres"),
            (nudHExit,             "Channel height at exit in millimetres"),
            (nudRib,               "Rib thickness in millimetres"),
            (nudWall,              "Hot-wall thickness in millimetres"),
            (nudJacket,            "Jacket wall thickness in millimetres"),
            (nudSmoothing,         "Smoothing radius in millimetres"),
            (cboTopology,          "Channel topology"),
            (cboEngineCycle,       "Engine cycle"),
            (chkFilmEnable,        "Enable film cooling"),
            (chkPreburnerCooling,  "Enable preburner regen cooling"),
            (chkAerospikeCooling,  "Enable aerospike plug-channel regen cooling"),
            (chkLivePreview,       "Live preview on parameter edits"),
            (nudProofFactor,       "Proof test factor"),
            (nudIterations,        "Optimisation iterations"),
            (nudSeed,              "Optimisation seed"),
        };
        foreach (var (ctl, name) in map)
        {
            if (ctl != null && string.IsNullOrEmpty(ctl.AccessibleName))
                ctl.AccessibleName = name;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //   Event wiring
    // ═══════════════════════════════════════════════════════════════

    private void WireAllParamEvents()
    {
        NumericUpDown[] nums = {
            nudThrustN, nudPcPsi, nudMR, nudCoolTK, nudCoolPMPa, nudBartzFactor,
            nudContraction, nudExpansion, nudLStar, nudThetaN, nudThetaE, nudBellFrac,
            nudChannelCount, nudHChamber, nudHThroat, nudHExit,
            nudRib, nudWall, nudJacket, nudSmoothing,
            nudManifoldL, nudPortD, nudChannelFillet,
            nudFlangeThk, nudFlangeORFactor, nudPropPortD, nudMountThk,
        };
        foreach (var n in nums) n.ValueChanged += (_, _) => MaybePush();

        NumericUpDown[] nums2 = {
            nudFilmFrac, nudFilmSlotH, nudFilmInjX, nudFilmInletT,
            nudFilmBurnL, nudFilmDecay, nudFilmThroatMix,
            nudInjectorSTLOffsetX, nudInjectorSTLScale,
            // Injector-internals fields (#306 / #307 / #308):
            nudIgniterRadialFrac, nudFuelDomeDepth_mm, nudOxDomeDepth_mm,
            nudDomeInletDia_mm, nudCoolantCrossoverDia_mm,
        };
        foreach (var n in nums2) n.ValueChanged += (_, _) => MaybePush();

        cboMaterial.SelectedIndexChanged += (_, _) => MaybePush();
        cboPropellantPair.SelectedIndexChanged += (_, _) => OnPropellantPairChanged();
        cboCoolantPortStd.SelectedIndexChanged += (_, _) => MaybePush();
        cboPropPortStd.SelectedIndexChanged += (_, _) => MaybePush();
        chkManifolds.CheckedChanged += (_, _) => MaybePush();
        chkPorts.CheckedChanged += (_, _) => MaybePush();
        chkInjectorFlange.CheckedChanged += (_, _) => MaybePush();
        chkMountFlange.CheckedChanged += (_, _) => MaybePush();
        cboMountFlangeStd.SelectedIndexChanged += (_, _) => MaybePush();
        chkFilmEnable.CheckedChanged += (_, _) => MaybePush();
        chkInjectorSTL.CheckedChanged += (_, _) => MaybePush();
        chkInjectorSTLAutoCenter.CheckedChanged += (_, _) => MaybePush();
        txtInjectorSTLPath.Leave += (_, _) => MaybePush();
        // Injector-internals events (#306 / #307 / #308):
        cboIgniterType.SelectedIndexChanged += (_, _) => MaybePush();
        chkAntiVortexBaffle.CheckedChanged  += (_, _) => MaybePush();
        chkCoolantCrossover.CheckedChanged  += (_, _) => MaybePush();

        // Status-bar info on filter preset change — surfaces the
        // clean ΔP + end-of-life multiplier so the user sees what the
        // stackup will actually charge without expanding the Feed System
        // help block. Read-only access to FilterPresets.All; no gate change.
        cboFilterStd.SelectedIndexChanged += (_, _) => OnFilterPresetChanged();

        // Visibility-based dispatch is now the sole pathway. Replaces the
        // legacy UpdateConditionalEnabledStates cascade (Visible-toggle is
        // strictly stronger than Enabled-toggle: removes the row from the
        // form entirely rather than greying it out).
        //
        // Discriminator + opt-in toggle events all funnel into
        // RecomputeFieldVisibility(); the rules table decides which
        // fields show. Previously-greyed control sub-fields (e.g.
        // PreburnerCooling sub-knobs when chkPreburnerCooling is off
        // but cycle has a preburner) now hide via the rule predicate.
        cboEngineCycle.SelectedIndexChanged    += (_, _) => RecomputeFieldVisibility();
        cboTopology.SelectedIndexChanged       += (_, _) => RecomputeFieldVisibility();
        cboPropellantPair.SelectedIndexChanged += (_, _) => RecomputeFieldVisibility();
        chkPreburnerCooling.CheckedChanged     += (_, _) => RecomputeFieldVisibility();
        chkAerospikeCooling.CheckedChanged     += (_, _) => RecomputeFieldVisibility();
        chkChilldownEnable.CheckedChanged      += (_, _) => RecomputeFieldVisibility();
        chkStartTransient.CheckedChanged       += (_, _) => RecomputeFieldVisibility();
        chkLpbfPrintability.CheckedChanged     += (_, _) => RecomputeFieldVisibility();
        chkFilmEnable.CheckedChanged           += (_, _) => RecomputeFieldVisibility();
        chkMountFlange.CheckedChanged          += (_, _) => RecomputeFieldVisibility();
        chkInjectorSTL.CheckedChanged          += (_, _) => RecomputeFieldVisibility();
        RecomputeFieldVisibility();
    }

    /// <summary>
    /// Pulls the form's current categorical state into a Core-side
    /// <see cref="UiVisibilityState"/>. Subsystem toggles default to
    /// <c>false</c> for any control not yet wired (Step 4 fills in the
    /// rest); rules referencing those toggles produce Hidden until the
    /// step lands, which is the intended behaviour.
    /// </summary>
    private UiVisibilityState ReadVisibilityState()
    {
        var cycle = (FeedSystem.EngineCycle)cboEngineCycle.SelectedIndex;
        var topology = (Optimization.ChannelTopology)cboTopology.SelectedIndex;
        var pair = (Combustion.PropellantPair)cboPropellantPair.SelectedIndex;

        return new UiVisibilityState(
            Cycle:                  cycle,
            Topology:               topology,
            Pair:                   pair,
            HasInjectorPattern:     false,                            // No first-class injector-pattern selector on the form yet (carried via JSON load); revisit when the wizard introduces one
            HasDualBell:            false,                            // No dual-bell discriminator on the form today
            ChilldownEnabled:       chkChilldownEnable.Checked,
            StartTransientEnabled:  chkStartTransient.Checked,
            LpbfPrintabilityEnabled:chkLpbfPrintability.Checked,
            PreburnerCoolingEnabled:chkPreburnerCooling.Checked,
            AerospikeCoolingEnabled:chkAerospikeCooling.Checked,
            FilmCoolingEnabled:     chkFilmEnable.Checked,
            MountingFlangeEnabled:  chkMountFlange.Checked,
            InjectorStlEnabled:     chkInjectorSTL.Checked,
            FeedSystemEnabled:      false);                           // Feed-system stackup is opt-in via Tank Ullage > 0 (no boolean checkbox); Step 5 wizard may add one
    }

    /// <summary>
    /// Drives the <see cref="ControlVisibilityRegistry"/> to set per-row .Visible
    /// based on the current discriminator state. Bracketed by
    /// SuspendLayout / ResumeLayout so the layout engine doesn't churn.
    /// </summary>
    /// <remarks>
    /// Honours the existing <c>_suppressParamEvents</c> guard so loading
    /// a saved design (which fires every event sequentially as combos
    /// are populated) doesn't trigger a recompute storm — the trailing
    /// <see cref="ApplyDesign(OperatingConditions, RegenChamberDesign)"/>
    /// call performs the recompute once at the end.
    /// </remarks>
    private void RecomputeFieldVisibility()
    {
        if (_suppressParamEvents) return;
        if (_visibilityRegistry.RegisteredCount == 0) return;  // Step 3 partial wiring — bail until callers register

        var state = ReadVisibilityState();
        SuspendLayout();
        try { _visibilityRegistry.RecomputeAll(state); }
        finally { ResumeLayout(performLayout: true); }
    }

    // UpdateConditionalEnabledStates was retired in favour of
    // RecomputeFieldVisibility() above. Visibility-toggle (.Visible) replaces
    // enabled-toggle (.Enabled) — hidden rows leave the form entirely rather
    // than greying out, removing the visual clutter of inert spinners. The
    // rules table in UiVisibilityRules is the single source of truth for
    // both UI gating and wizard cascade.

    private void OnFilterPresetChanged()
    {
        if (_suppressParamEvents) return;
        int idx = cboFilterStd.SelectedIndex;
        if (idx < 0) return;
        var std = (FeedSystem.FilterStandard)idx;
        var spec = FeedSystem.FilterPresets.SpecFor(std);
        if (std == FeedSystem.FilterStandard.Custom)
        {
            SetStatus("Filter preset: Custom — reads OperatingConditions.FilterDeltaP_Pa "
                + $"as clean ΔP, {spec.DirtyMultiplier:0.0}× at end-of-life.");
        }
        else if (std == FeedSystem.FilterStandard.None)
        {
            SetStatus("Filter preset: (no filter) — stackup charges 0 ΔP.");
        }
        else
        {
            double cleanKPa = spec.NominalCleanDP_Pa / 1000.0;
            SetStatus($"Filter preset: {spec.DisplayName} — {cleanKPa:0} kPa clean, "
                + $"{spec.DirtyMultiplier:0.0}× at end-of-life ({spec.Rating_um:0} µm rating).");
        }
    }

    private void MaybePush()
    {
        if (_suppressParamEvents) return;
        // Abort-on-user-input. An input change while a
        // solve is running means the running outputs are about to be
        // stale; surface a cancel so the task thread unwinds via its
        // existing OperationCanceledException path. Fires before the
        // live-preview gate — cancelling even when no regen follows
        // is the whole point. `ResourceProfiler.IsOpInFlight` covers
        // both SA (profiled as "sa") and tolerance sweep ("tol").
        if (ResourceBudget.AbortOpOnInputEdit && ResourceProfiler.IsOpInFlight)
            SharedState.PostCancelCurrentOp();

        // Refresh the pre-flight projection indicator
        // on every input change. Pure math (no voxels) — one contour
        // generation + bbox projection, typically < 2 ms. Fires
        // regardless of live-preview toggle so the user sees the budget
        // picture while staging edits.
        UpdatePreflightProjection();

        // 2026-04-22: live-preview gate. When OFF, parameter edits are
        // staged silently and the preview only regenerates on an explicit
        // Generate / Run / optimization trigger. Saves a full voxel build
        // per keystroke on fields like Thrust / Mixture Ratio / Channel
        // Count, which each cost 3–10 seconds.
        if (chkLivePreview != null && !chkLivePreview.Checked) return;
        PushParams();
    }

    /// <summary>
    /// Refresh the note label, snap MR to the new pair's band/default, and
    /// guard against the "unavailable" pairs (N2O4/MMH, H2O2/RP-1) whose
    /// tables aren't populated yet. Triggers a single push to regenerate.
    /// </summary>
    private void OnPropellantPairChanged()
    {
        if (cboPropellantPair.SelectedIndex < 0) return;
        var meta = Combustion.PropellantPairs.All[cboPropellantPair.SelectedIndex];

        // Hard-fail on unimplemented pairs. We no longer
        // silently revert to LOX/CH4 — the user's selection is preserved,
        // the banner turns red, and Generate / Optimize are disabled until
        // an implemented pair is chosen.
        string? reason = Combustion.PropellantValidation.Explain(meta.Id);
        bool supported = reason is null;

        if (supported)
        {
            lblPairNote.Text = meta.Note;
            lblPairNote.ForeColor = System.Drawing.SystemColors.ControlText;
        }
        else
        {
            lblPairNote.Text = "GENERATION DISABLED — " + reason;
            lblPairNote.ForeColor = System.Drawing.Color.Firebrick;
        }
        btnGenerate.Enabled = supported;
        btnStartOpt.Enabled = supported;

        _suppressParamEvents = true;
        try
        {
            if (!supported) return;

            // Reset MR to the pair's default if the current value is out of range.
            double mr = (double)nudMR.Value;
            if (mr < meta.MR_Min || mr > meta.MR_Max)
                SetNum(nudMR, meta.MR_Default);
            nudMR.Minimum = (decimal)meta.MR_Min;
            nudMR.Maximum = (decimal)meta.MR_Max;
        }
        finally { _suppressParamEvents = false; }

        PushParams();
    }

    private void PushParams() => _onParamsChanged(ReadConditions(), ReadDesign());

    // ═══════════════════════════════════════════════════════════════
    //   Public API (called from main thread via BeginInvoke)
    // ═══════════════════════════════════════════════════════════════
    //
    // Sprint 6 Track B (2026-04-22) moved the controls ↔ domain I/O
    // methods into the partial-class sibling RegenChamberForm.ParameterIO.cs:
    //   • ReadConditions() → OperatingConditions
    //   • ReadDesign()     → RegenChamberDesign
    //   • ApplyDesign(c, d)
    // Behaviour unchanged; call sites (batch, overlay, persistence, test
    // harnesses) see the same signatures via the shared class identity.

    /// <summary>
    /// Sprint 12 Track E (2026-04-23): the full readout population for a
    /// generation result. Orchestrator only — the real work is in the
    /// per-section helpers in <see cref="RegenChamberForm.ResultsDisplay"/>.
    /// Helpers fire in readout-order (top → bottom of the right-panel
    /// flow), and each owns one physics story. To add a new readout
    /// block: add a <c>PopulateXxxReadouts</c> helper in the partial
    /// file and append it to the list below.
    /// </summary>
    public void UpdateResults(RegenGenerationResult r, RegenScoreResult? score)
    {
        if (IsDisposed) return;

        // Sprint 17 / Track H Companion (P8, 2026-04-23): suspend
        // layout while the 13 Populate* helpers rewrite ~60 labels.
        // Previously the form ran a layout pass after each label text
        // assignment; batching them collapses to a single layout pass
        // at ResumeLayout — noticeable responsiveness win on slower
        // hardware, and a couple of µs of GC pressure saved besides.
        // The label-value short-circuit is still possible as a future
        // refinement (per-label last-value cache) but SuspendLayout
        // alone captures most of the win for almost no risk.
        SuspendLayout();
        try
        {
            PopulateEngineSummary(r);
            PopulateDimensionsAndMass(r);
            PopulateThermalReadouts(r);
            PopulateChartsAndCompare(r);
            PopulateStructuralAndStability(r);
            PopulateManufacturingReadouts(r);
            PopulateDiagnosticsReadouts(r);
            PopulateWarningsPanel(r, score);
            PopulateChilldownReadouts(r);
            PopulateStartTransientReadouts(r);
            PopulateTurbopumpReadouts(r);
            PopulateAerospikeReadouts(r);
            PopulatePreburnerThermalReadouts(r);
            PopulateLpbfPrintabilityReadout(r);
        }
        finally { ResumeLayout(performLayout: true); }
    }

    /// <summary>
    /// Set the status-bar text. Also toggles the marquee
    /// progress bar: any message ending in an ellipsis ("…" or "...")
    /// flips the bar on; any other message flips it off. Keeps the
    /// busy indicator in lockstep with the status line without every
    /// caller having to thread a separate flag through.
    /// </summary>
    public void SetStatus(string msg)
    {
        if (IsDisposed) return;
        lblStatus.Text = msg;
        SetBusyIndicator(IsBusyStatusMessage(msg));
        // Mirror every status message into the
        // rolling history buffer so the expander panel below the status
        // line can surface "what happened 30 s ago" on demand. No-op in
        // the pre-field-init window (e.g. base-class form events).
        _statusHistory?.Add(msg);
    }

    /// <summary>
    /// Infer "work in progress" from the status-text ending.
    /// Matches the project's existing convention that busy messages end
    /// in an ellipsis (e.g. "Regenerating…", "Running tolerance sweep…").
    /// Exposed as internal so the test suite can exercise both forms.
    /// </summary>
    internal static bool IsBusyStatusMessage(string? msg)
    {
        if (string.IsNullOrEmpty(msg)) return false;
        string trimmed = msg.TrimEnd();
        return trimmed.EndsWith('\u2026')
            || trimmed.EndsWith("...",    System.StringComparison.Ordinal);
    }

    private void SetBusyIndicator(bool busy)
    {
        if (pbRegen is null) return;
        if (busy)
        {
            if (!pbRegen.Visible)
            {
                pbRegen.Style = ProgressBarStyle.Marquee;
                pbRegen.MarqueeAnimationSpeed = 30;
                pbRegen.Visible = true;
            }
        }
        else if (pbRegen.Visible)
        {
            pbRegen.MarqueeAnimationSpeed = 0;
            pbRegen.Visible = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //   Action helpers (so keyboard shortcuts + drag-drop
    //   can invoke the same code paths as the toolbar buttons).
    // ═══════════════════════════════════════════════════════════════

    private void ExportStlViaDialog()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "STL|*.stl", FileName = "regen_chamber.stl",
            InitialDirectory = _settings.LastSaveFolder ?? "",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _settings.LastSaveFolder = System.IO.Path.GetDirectoryName(dlg.FileName);
        _onExportStl(dlg.FileName, (float)nudStlVoxel.Value, chkExportMonolithic.Checked);
    }

    // Sprint render (2026-04-25) — opens the modal dialog for material /
    // mode / resolution / frames, then dispatches to _onRenderImage which
    // chains: temp-STL export → voxelforge-render → temp cleanup.
    private void RenderImageViaDialog()
    {
        if (_onRenderImage is null)
        {
            MessageBox.Show(this,
                "Renderer subprocess is not available. Install Blender 4.x and restart " +
                "the app, or set the VOXELFORGE_BLENDER_PATH environment variable.",
                "Render image", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var defaultPath = System.IO.Path.Combine(
            _settings.LastSaveFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "regen_chamber.png");
        using var dlg = new RenderImageDialog(defaultPath);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _settings.LastSaveFolder = System.IO.Path.GetDirectoryName(dlg.OutputPath);
        _onRenderImage(dlg.OutputPath, dlg.Material, dlg.Mode, dlg.Resolution, dlg.Frames);
    }

    private void Export3MFViaDialog()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "3MF|*.3mf", FileName = "regen_chamber.3mf",
            InitialDirectory = _settings.LastSaveFolder ?? "",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _settings.LastSaveFolder = System.IO.Path.GetDirectoryName(dlg.FileName);
        _onExport3MF(dlg.FileName);
    }

    private void ExportReportViaDialog()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Text|*.txt", FileName = "regen_chamber_report.txt",
            InitialDirectory = _settings.LastSaveFolder ?? "",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _settings.LastSaveFolder = System.IO.Path.GetDirectoryName(dlg.FileName);
        _onExportReport(dlg.FileName);
    }

    /// <summary>
    /// UI-side launcher for the CFD-field .vti export. The callback on
    /// the task-thread side inspects
    /// <c>_lastResult.Aerospike</c> and routes to either
    /// <see cref="IO.CfdFieldExport.Write"/> (bell chamber) or
    /// <see cref="IO.CfdFieldExport.WriteAerospike"/> (aerospike plug),
    /// so the operator doesn't need a separate button per topology.
    /// </summary>
    private void ExportVtiViaDialog()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "VTK ImageData|*.vti", FileName = "fields.vti",
            InitialDirectory = _settings.LastSaveFolder ?? "",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _settings.LastSaveFolder = System.IO.Path.GetDirectoryName(dlg.FileName);
        _onExportVti(dlg.FileName);
    }

    private void SaveDesignViaDialog()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Regen design|*.rcd.json", FileName = "chamber.rcd.json",
            InitialDirectory = _settings.LastSaveFolder ?? "",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _settings.LastSaveFolder = System.IO.Path.GetDirectoryName(dlg.FileName);
        _settings.RegisterRecentDesign(dlg.FileName);
        _onSaveDesign(dlg.FileName, ReadConditions(), ReadDesign());
    }

    private void LoadDesignViaDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Regen design|*.rcd.json",
            InitialDirectory = _settings.LastLoadFolder ?? "",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        LoadDesignByPath(dlg.FileName);
    }

    /// <summary>
    /// Shared load-by-path routine used by the Load
    /// Design… button, the keyboard shortcut, the recent-files menu, and
    /// the drag-and-drop handler. Updates SessionSettings (last folder +
    /// recents) and feeds the values into the form on success.
    /// </summary>
    private void LoadDesignByPath(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        if (!System.IO.File.Exists(path))
        {
            SetStatus($"Load failed: file not found — {name}");
            return;
        }
        _settings.LastLoadFolder = System.IO.Path.GetDirectoryName(path);
        _settings.RegisterRecentDesign(path);
        var (c, d) = _onLoadDesign(path);
        if (c != null && d != null)
        {
            ApplyDesign(c, d);
            SetStatus($"Loaded {name}");
        }
        else
        {
            // The host callback (Program.LoadDesignOnUiThread) already
            // writes a detailed "Load error: …" message to the status
            // bar when it catches an exception. Only overwrite if the
            // status bar is blank or still shows an unrelated message.
            if (!lblStatus.Text.StartsWith("Load error", System.StringComparison.Ordinal))
                SetStatus($"Load failed for {name} — file may be corrupt or an older schema.");
        }
    }

    /// <summary>
    /// Stop the running optimization (same as the Stop
    /// button) from the Esc keyboard shortcut. No-op when not running.
    /// </summary>
    private void StopOptIfRunning()
    {
        if (btnStopOpt.Enabled)
        {
            _onStopOpt();
            btnStopOpt.Enabled = false;
            SetStatus("Optimization stopped (Esc).");
        }
    }

    // RunAllSnapshot is declared on the sibling partial-class file
    // RegenChamberForm.RunAllSnapshot.cs (Sprint 6 Track B, 2026-04-22).

    private void OnRunAllAnalysesToggled()
    {
        if (chkRunAllAnalyses.Checked)
        {
            _preRunAllSnapshot = new RunAllSnapshot
            {
                Chilldown       = chkChilldownEnable.Checked,
                StartTrans      = chkStartTransient.Checked,
                TankUllage_MPa  = (double)nudTankUllage_MPa.Value,
                EngineCycle     = cboEngineCycle.SelectedIndex,
            };
            _suppressParamEvents = true;
            try
            {
                chkChilldownEnable.Checked = true;
                chkStartTransient.Checked  = true;
                if ((double)nudTankUllage_MPa.Value <= 0.0)
                {
                    double pcMPa = (double)nudPcPsi.Value * 6894.76 / 1e6;
                    double want  = System.Math.Min(1.5 * pcMPa, (double)nudTankUllage_MPa.Maximum);
                    SetNum(nudTankUllage_MPa, want);
                }
                if (cboEngineCycle.SelectedIndex == 0) // PressureFed → GasGenerator
                    cboEngineCycle.SelectedIndex = 1;
            }
            finally { _suppressParamEvents = false; }
            SetStatus("All analyses enabled (chilldown + start + ullage stackup + GasGenerator).");
            MaybePush();
        }
        else if (_preRunAllSnapshot is { } s)
        {
            _suppressParamEvents = true;
            try
            {
                chkChilldownEnable.Checked = s.Chilldown;
                chkStartTransient.Checked  = s.StartTrans;
                SetNum(nudTankUllage_MPa, s.TankUllage_MPa);
                cboEngineCycle.SelectedIndex = System.Math.Clamp(
                    s.EngineCycle, 0, cboEngineCycle.Items.Count - 1);
            }
            finally { _suppressParamEvents = false; }
            _preRunAllSnapshot = null;
            SetStatus("Restored analysis opt-ins to prior values.");
            MaybePush();
        }
    }

    private void RunToleranceSweep()
    {
        SetStatus("Running tolerance sweep\u2026");
        try
        {
            var r = _runTolerance();
            if (r == null)
            {
                lblTolSummary.Text = "Tolerance sweep: generate a design first";
                return;
            }
            // Stamp the sweep as stale if the user has
            // edited design parameters between the last Generate and this call.
            string currentHash = Optimization.DesignProvenance.Compute(ReadConditions(), ReadDesign());
            string stalePrefix = (r.DesignHash != "" && r.DesignHash != currentHash) ? "STALE \u2014 " : "";
            lblTolSummary.ForeColor = stalePrefix.Length > 0
                ? System.Drawing.Color.Firebrick
                : System.Drawing.SystemColors.ControlText;
            lblTolSummary.Text = stalePrefix + $"Samples: {r.SampleCount}  |  {r.MeanComputeTime_ms:F1} ms/sample  |  "
                               + $"yield fail: {r.YieldExceededCount}  wall-T fail: {r.WallTLimitExceededCount}";
            lblTolPeakT.Text = $"Peak T  p50/p90/p99: {r.PeakWallT_K.P50:F0} / {r.PeakWallT_K.P90:F0} / {r.PeakWallT_K.P99:F0} K";
            lblTolSF.Text = $"Min SF  p10/p50:     {r.MinSafetyFactor.P10:F2} / {r.MinSafetyFactor.P50:F2}";
            lblTolSF.ForeColor = r.MinSafetyFactor.P10 < 1.0 ? System.Drawing.Color.Red
                              : r.MinSafetyFactor.P10 < 1.2 ? System.Drawing.Color.DarkOrange
                              : System.Drawing.Color.DarkGreen;
            lblTolDP.Text = $"\u0394P     p50/p90:     {r.CoolantPressureDrop_Pa.P50/1e6:F2} / {r.CoolantPressureDrop_Pa.P90/1e6:F2} MPa";
            lblTolCoolantT.Text = $"T_out   p50/p90:     {r.CoolantOutletT_K.P50:F0} / {r.CoolantOutletT_K.P90:F0} K";

            // Feed the raw per-sample arrays into the
            // histogram panel. SetResult is a no-op when Samples_* are
            // null (legacy callers) so this is safe.
            tolHistogramPanel.SetResult(r);

            var lines = new List<string>();
            lines.Add($"[Tolerance sweep N={r.SampleCount}:]");
            lines.AddRange(r.Warnings);
            if (lines.Count > 1)
                txtWarnings.Text = string.Join(Environment.NewLine, lines.Concat(new[] { "", txtWarnings.Text }));
            // Append wall/CPU/peak-WS footer so the
            // user sees the cost of the sweep (the profiler End() in
            // Program.RunToleranceFromUI just cached it). Empty tail
            // on a skipped profile; the Format() output is self-
            // describing, so no separator is added when it's blank.
            var res = ResourceProfiler.LastSummary("tol");
            string resTail = res.WallMs > 0 ? $"  |  {res.Format()}" : "";
            SetStatus($"Tolerance sweep done. p99 peak T = {r.PeakWallT_K.P99:F0} K." + resTail);
        }
        catch (System.OperationCanceledException)
        {
            // OCE comes from (a) the Stop button tripping
            // the CTS, (b) a field-edit-driven cancel (D3), or (c) the
            // sweep's own time-budget CancelAfter firing. We can't
            // perfectly distinguish (c) from the others without extra
            // plumbing; we include the budget value so the user sees
            // whether it was likely the cause.
            int budget = ResourceBudget.SweepTimeoutSeconds;
            string tail = budget > 0
                ? $" (time budget: {budget} s \u2014 set to 0 to disable)"
                : "";
            lblTolSummary.Text = "Tolerance sweep cancelled" + tail;
            lblTolSummary.ForeColor = System.Drawing.Color.DarkOrange;
            SetStatus("Tolerance sweep cancelled." + tail);
        }
        catch (Exception ex)
        {
            SetStatus("Tolerance sweep error: " + ex.Message);
        }
    }

    /// <summary>
    /// Open a CSV of measured cold-flow / hot-fire data,
    /// summarise the steady segment, overlay predicted vs measured against
    /// the CURRENT design, and run a Bartz-scaling calibration grid search.
    /// The recommended factor is stashed in <see cref="_pendingCalibratedBartz"/>
    /// so the Apply button can write it to the Operating Point.
    /// </summary>
    /// <summary>2026-04-22: pick the output folder for a batch run.</summary>
    private void BrowseBatchFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Pick an output folder for the batch-run artefacts.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (!string.IsNullOrEmpty(txtBatchFolder.Text) && Directory.Exists(txtBatchFolder.Text))
            dlg.SelectedPath = txtBatchFolder.Text;
        if (dlg.ShowDialog() == DialogResult.OK)
            txtBatchFolder.Text = dlg.SelectedPath;
    }

    /// <summary>
    /// 2026-04-22: kick off a batch run. Validates the output folder, then
    /// hands an (OptSettings, BatchRunSettings) pair to the host for the
    /// task thread to pick up. The same SA pipeline drives the run; the
    /// batch settings only control what Program.cs writes on FinalizeOpt.
    /// </summary>
    private void StartBatchRun()
    {
        string folder = txtBatchFolder.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(folder))
        {
            SetStatus("Batch run: pick an output folder first.");
            return;
        }
        try
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            SetStatus("Batch run: cannot use folder — " + ex.Message);
            return;
        }

        // Any save option has to be on or the run is pointless.
        if (!(chkBatchSaveJson.Checked || chkBatchSaveStl.Checked || chkBatchSaveReport.Checked || chkBatchSaveParetoCsv.Checked))
        {
            SetStatus("Batch run: enable at least one of Save JSON / STL / Report / Pareto CSV.");
            return;
        }

        var baseline = ReadDesign();
        var optSettings = new OptSettings
        {
            MaxIterations   = (int)nudIterations.Value,
            Seed            = (int)nudSeed.Value,
            ProfileIndex    = cboProfile.SelectedIndex,
            WarmStart       = chkWarmStart.Checked,
            Conditions      = ReadConditions(),
            BaselineDesign  = baseline,
            WarmStartParams = chkWarmStart.Checked ? RegenChamberOptimization.Pack(baseline) : null,
            ParallelBatchSize = chkParallelSa.Checked ? 8 : 1,
            UseMultiChain   = chkMultiChainSa.Checked && !chkNsgaIi.Checked,
            MultiChainCount = (int)nudMultiChainCount.Value,
            UseNsgaIi         = chkNsgaIi.Checked,
            NsgaPopulationSize = (int)nudNsgaPopulation.Value,
            NsgaMaxGenerations = (int)nudNsgaGenerations.Value,
        };
        var batchSettings = new BatchRunSettings
        {
            OutputFolder     = folder,
            SaveDesignJson   = chkBatchSaveJson.Checked,
            SaveStl          = chkBatchSaveStl.Checked,
            SaveReport       = chkBatchSaveReport.Checked,
            SaveParetoCsv    = chkBatchSaveParetoCsv.Checked,
            StlVoxelMM       = (float)nudStlVoxel.Value,
        };

        SetStatus($"Batch run started: {optSettings.MaxIterations} iters → {folder}");
        btnStartOpt.Enabled = false;
        btnStopOpt.Enabled = true;
        btnRunBatch.Enabled = false;
        _onStartBatch(optSettings, batchSettings);
    }

    /// <summary>
    /// Parent audit §4: translate the bolt-pattern combobox index back to
    /// the <see cref="Geometry.MountingFlangeStandard"/> enum. Falls back
    /// to <c>Generic8Bolt</c> if the selection is out of range.
    /// </summary>
    private Geometry.MountingFlangeStandard MountFlangeStdFromUI()
    {
        int idx = cboMountFlangeStd.SelectedIndex;
        int max = System.Enum.GetValues<Geometry.MountingFlangeStandard>().Length - 1;
        if (idx < 0 || idx > max) return Geometry.MountingFlangeStandard.Generic8Bolt;
        return (Geometry.MountingFlangeStandard)idx;
    }

    private void RunMeasuredDataOverlay()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "CSV test data|*.csv;*.txt",
            Title = "Load measured test-run data",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            var (samples, parseWarnings) = Analysis.MeasuredDataOverlay.ParseCsv(dlg.FileName);
            if (samples.Count == 0)
            {
                SetStatus("Measured-data overlay: no usable rows in CSV.");
                lblOverlaySummary.Text = "Overlay: CSV has no usable rows (need at least chamber_p_pa, coolant_t_in_k, coolant_t_out_k).";
                lblOverlaySummary.ForeColor = System.Drawing.Color.Firebrick;
                btnApplyCalibration.Enabled = false;
                return;
            }
            var measured = Analysis.MeasuredDataOverlay.Summarise(samples);

            // Runner: re-solve the thermal-only path at a given Bartz factor.
            // Fast — physics-only, no voxel work.
            var baselineCond = ReadConditions();
            var design = ReadDesign();
            (double wallT, double dT, double dP) Runner(double f)
            {
                var c = baselineCond with { BartzScalingFactor = f };
                var gen = RegenChamberOptimization.GenerateWith(c, design, skipVoxelGeometry: true);
                double dTret = gen.Thermal.CoolantOutletT_K - gen.Thermal.CoolantInletT_K;
                return (gen.Thermal.PeakGasSideWallT_K, dTret, gen.Thermal.CoolantPressureDrop_Pa);
            }

            var pred = Runner(baselineCond.BartzScalingFactor);
            var overlay = Analysis.MeasuredDataOverlay.BuildOverlay(
                measured,
                predicted_PeakWallT_K:  pred.wallT,
                predicted_CoolantDT_K:  pred.dT,
                predicted_CoolantDP_Pa: pred.dP,
                calibrationRunner:      Runner);

            lblOverlaySummary.ForeColor = System.Drawing.SystemColors.ControlText;
            lblOverlaySummary.Text = $"Overlay: {measured.SampleCount} samples. Chamber P {measured.ChamberP_Pa / 1e6:F2} MPa, ΔT {measured.CoolantDT_K:F0} K, ΔP {measured.CoolantDP_Pa / 1e6:F2} MPa"
                                    + (double.IsNaN(measured.WallT_K) ? " (no wall-T column)" : $", wall T {measured.WallT_K:F0} K");
            lblOverlayErrors.Text = $"% errors vs measured: wall T {Fmt(overlay.PercentError_PeakWallT)}, ΔT {Fmt(overlay.PercentError_CoolantDT)}, ΔP {Fmt(overlay.PercentError_CoolantDP)}";
            lblOverlayErrors.ForeColor = AnyLarge(overlay)
                ? System.Drawing.Color.DarkOrange
                : System.Drawing.Color.DarkGreen;

            if (overlay.Calibration is { } cal)
            {
                _pendingCalibratedBartz = cal.BartzScalingFactor;
                lblOverlayCalibration.Text = $"Calibration: recommend Bartz factor {cal.BartzScalingFactor:F2} " +
                                             $"(SSR {cal.SumSquaredResidualAt1:F3} → {cal.SumSquaredResidualAtBest:F3}) — {cal.CalibrationNotes}";
                btnApplyCalibration.Enabled = Math.Abs(cal.BartzScalingFactor - baselineCond.BartzScalingFactor) > 0.005;
            }
            else
            {
                lblOverlayCalibration.Text = "Calibration: (no calibration requested)";
                btnApplyCalibration.Enabled = false;
            }

            if (parseWarnings.Count > 0 || overlay.Warnings.Length > 0)
            {
                var lines = new List<string> { "[Measured data overlay:]" };
                lines.AddRange(parseWarnings);
                lines.AddRange(overlay.Warnings);
                txtWarnings.Text = string.Join(Environment.NewLine, lines.Concat(new[] { "", txtWarnings.Text }));
            }
            SetStatus($"Measured-data overlay: {measured.SampleCount} samples; "
                    + (overlay.Calibration is { } c2 ? $"calibrated factor {c2.BartzScalingFactor:F2}." : "no calibration."));
        }
        catch (Exception ex)
        {
            SetStatus("Measured-data overlay error: " + ex.Message);
            lblOverlaySummary.Text = "Overlay: error loading file.";
            lblOverlaySummary.ForeColor = System.Drawing.Color.Firebrick;
        }

        static string Fmt(double pct) => double.IsNaN(pct) ? "—" : $"{pct:+0.0;-0.0;0.0}%";
        static bool AnyLarge(Analysis.MeasuredOverlayResult o)
        {
            double[] vs = { o.PercentError_PeakWallT, o.PercentError_CoolantDT, o.PercentError_CoolantDP };
            foreach (var v in vs) if (!double.IsNaN(v) && Math.Abs(v) > 20) return true;
            return false;
        }
    }

    /// <summary>
    /// Write the most-recently-recommended Bartz factor
    /// from the measured-data overlay into the Operating Point's factor
    /// field. Triggers a Generate push so the viewer picks it up.
    /// </summary>
    private void ApplyCalibratedBartzFactor()
    {
        double f = Math.Clamp(_pendingCalibratedBartz, (double)nudBartzFactor.Minimum, (double)nudBartzFactor.Maximum);
        _suppressParamEvents = true;
        try { nudBartzFactor.Value = (decimal)f; }
        finally { _suppressParamEvents = false; }
        btnApplyCalibration.Enabled = false;
        SetStatus($"Applied calibrated Bartz factor {f:F2} — regenerating.");
        PushParams();
    }

    private void RunProofTest()
    {
        var r = _runProofTest();
        if (r == null)
        {
            lblProofPressure.Text = "Proof pressure: — (generate first)";
            lblProofSF.Text = "Proof min SF: —";
            lblBurstMargin.Text = "Elastic burst margin: —";
            return;
        }
        // Stale if the UI-side design diverged from what
        // the proof was computed against. This typically happens when the
        // user edits parameters after pressing Generate, then opens the
        // Proof-test panel — the proof runs against the cached _lastResult
        // but pulls a potentially different wall thickness from the UI.
        string currentHash = Optimization.DesignProvenance.Compute(ReadConditions(), ReadDesign());
        string stalePrefix = (r.DesignHash != "" && r.DesignHash != currentHash) ? "STALE \u2014 " : "";
        lblProofPressure.Text = stalePrefix + $"Proof pressure: {r.ProofPressure_Pa/1e6:F2} MPa ({r.ProofFactor:F2}\u00d7 MEOP)";
        lblProofPressure.ForeColor = stalePrefix.Length > 0
            ? System.Drawing.Color.Firebrick
            : System.Drawing.SystemColors.ControlText;
        var cs = r.ColdStructure;
        lblProofSF.Text = $"Proof min SF: {cs.MinSafetyFactor:F2}  \u2014  {(r.Passes?"PASS":"FAIL")}";
        lblProofSF.ForeColor = r.Passes ? System.Drawing.Color.DarkGreen : System.Drawing.Color.Red;
        lblBurstMargin.Text = $"Elastic burst P: {r.ElasticBurstPressure_Pa/1e6:F1} MPa ({r.BurstMarginFactor:F2}\u00d7 MEOP)";
        lblBurstMargin.ForeColor = r.BurstMarginFactor >= 2.0 ? System.Drawing.Color.Black : System.Drawing.Color.DarkOrange;
        var lines = new List<string>(r.Warnings);
        if (lines.Count > 0)
            txtWarnings.Text = string.Join(Environment.NewLine,
                new[]{ "[Proof test:]" }.Concat(lines).Concat(new[]{ "", txtWarnings.Text }));
    }

    /// <summary>TIER B.8: push the latest Pareto front into the scatter widget.
    /// Also stash the snapshot so the "Save Pareto CSV…" button can
    /// serialise without a task-thread round trip.</summary>
    public void ApplyParetoFront(IReadOnlyList<Optimization.ParetoPoint>? points)
    {
        _lastParetoSnapshot = points;
        paretoPanel.SetPoints(points);
    }

    public void ApplyOptResult(double[] bestParams)
    {
        var baseline = ReadDesign();
        var d = RegenChamberOptimization.Unpack(bestParams, baseline);
        ApplyDesign(ReadConditions(), d);
    }

    // ═══════════════════════════════════════════════════════════════
    //   Optimization control
    // ═══════════════════════════════════════════════════════════════

    private void StartOpt()
    {
        var baseline = ReadDesign();
        var settings = new OptSettings
        {
            MaxIterations = (int)nudIterations.Value,
            Seed = (int)nudSeed.Value,
            ProfileIndex = cboProfile.SelectedIndex,
            WarmStart = chkWarmStart.Checked,
            Conditions = ReadConditions(),
            BaselineDesign = baseline,
            WarmStartParams = chkWarmStart.Checked ? RegenChamberOptimization.Pack(baseline) : null,
            ParallelBatchSize = chkParallelSa.Checked ? 8 : 1,
            UseMultiChain = chkMultiChainSa.Checked && !chkNsgaIi.Checked,
            MultiChainCount = (int)nudMultiChainCount.Value,
            UseNsgaIi = chkNsgaIi.Checked,
            NsgaPopulationSize = (int)nudNsgaPopulation.Value,
            NsgaMaxGenerations = (int)nudNsgaGenerations.Value,
        };
        // Wipe the convergence trace before a new
        // run so iteration 0 starts at the left edge of the plot.
        optConvergencePanel.Reset();
        _onStartOpt(settings);
        btnStartOpt.Enabled = false;
        btnStopOpt.Enabled = true;
    }

    // Resource Budget — UI wiring.
    // ResourceMode combo changed: flip the mode, re-resolve defaults,
    // re-apply. Leave the explicit caps as-is so a user who picked
    // Balanced then nudged cores down keeps their override.
    private void OnResourceModeChanged()
    {
        if (cboResourceMode.SelectedIndex < 0) return;
        _settings.ResourceMode = (ResourceMode)cboResourceMode.SelectedIndex;

        // When the user explicitly picks a preset (not Custom),
        // rewrite the explicit caps so the NumericUpDowns reflect
        // the preset's defaults. Custom skips this to respect the
        // user's prior explicit values.
        if (_settings.ResourceMode != ResourceMode.Custom)
        {
            var r = ResourcePresets.Resolve(_settings.ResourceMode,
                        ResourceBudget.TotalCores, ResourceBudget.TotalMemory_MB);
            _settings.MaxParallelism    = r.MaxCores;
            _settings.MemoryBudget_MB   = r.MemoryBudget_MB;
            nudMaxParallelism.Value     = System.Math.Clamp(r.MaxCores, 1, System.Environment.ProcessorCount);
            nudMemoryBudget_MB.Value    = System.Math.Max(0, System.Math.Min(1_048_576, r.MemoryBudget_MB));
            chkDemotePriority.Checked   = r.DemotePriority;
        }
        ApplyResourceBudget();
    }

    // Individual knob changed: flip mode to Custom (unless already),
    // persist values, re-apply.
    private void OnResourceKnobChanged()
    {
        _settings.MaxParallelism               = (int)nudMaxParallelism.Value;
        _settings.MemoryBudget_MB              = (int)nudMemoryBudget_MB.Value;
        _settings.DemotePriorityDuringSolves   = chkDemotePriority.Checked;
        _settings.BatteryAwareQuiet            = chkBatteryAwareQuiet.Checked;
        _settings.AdaptiveForegroundThrottle   = chkAdaptiveForegroundThrottle.Checked;
        _settings.GcLatencyTuning              = chkGcLatencyTuning.Checked;
        _settings.SweepTimeoutSeconds          = (int)nudSweepTimeoutSec.Value;
        _settings.OptTimeoutSeconds            = (int)nudOptTimeoutSec.Value;
        _settings.AbortOpOnInputEdit           = chkAbortOpOnInputEdit.Checked;
        _settings.AutoCoarsenVoxelToFitBudget  = chkAutoCoarsenVoxel.Checked;
        _settings.FastPreviewMode              = chkFastPreview.Checked;
        _settings.TileLargeBuilds              = chkTileLargeBuilds.Checked;
        _settings.TileCount                    = (int)nudTileCount.Value;
        _settings.IsolateLargeBuildsAtFailProjection = chkIsolateLargeBuilds.Checked;

        // Only switch to Custom when the user manually diverged from the
        // preset's resolved values. Prevents flipping to Custom when the
        // preset-change handler seeds the explicit caps.
        if (_settings.ResourceMode != ResourceMode.Custom)
        {
            var r = ResourcePresets.Resolve(_settings.ResourceMode,
                        ResourceBudget.TotalCores, ResourceBudget.TotalMemory_MB);
            bool diverged = _settings.MaxParallelism   != r.MaxCores
                         || _settings.MemoryBudget_MB  != r.MemoryBudget_MB
                         || _settings.DemotePriorityDuringSolves != r.DemotePriority;
            if (diverged)
            {
                _settings.ResourceMode = ResourceMode.Custom;
                cboResourceMode.SelectedIndex = (int)ResourceMode.Custom;
            }
        }
        ApplyResourceBudget();
    }

    private void ApplyResourceBudget()
    {
        ResourceBudgetSettings.ApplySettings(_settings);
        _settings.Save();
    }

    // Live resource gauge. Reads current process
    // working set + CPU time delta against wall-clock since the
    // last tick; updates the status-bar label. Called from the
    // existing 250 ms poll timer.
    private void UpdateResourceGauge()
    {
        if (lblResourceGauge is null || IsDisposed) return;
        try
        {
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            long wsBytes = p.WorkingSet64;
            // Feed the 250 ms working-set sample into
            // the profiler's peak tracker. Cheap no-op when no op is
            // in flight; when one is, this gives the post-op summary
            // a true peak rather than a start-of-op snapshot.
            ResourceProfiler.RecordWorkingSetSample(wsBytes);
            double memMb = wsBytes / (1024.0 * 1024.0);

            double cpuPct = 0;
            long cpuNow = p.TotalProcessorTime.Ticks;
            var wallNow = System.DateTime.UtcNow;
            if (_lastCpuSampleAt != System.DateTime.MinValue)
            {
                long cpuDelta = cpuNow - _lastCpuTimeTicks;
                double wallDeltaSec = (wallNow - _lastCpuSampleAt).TotalSeconds;
                if (wallDeltaSec > 0)
                {
                    // Normalise by logical processor count so 100 % == all
                    // cores saturated. A 4-core box pinned on 1 core
                    // reads ~25 % rather than 100 %.
                    double cpuSec = cpuDelta / (double)System.TimeSpan.TicksPerSecond;
                    cpuPct = 100.0 * cpuSec / (wallDeltaSec * System.Environment.ProcessorCount);
                    cpuPct = System.Math.Clamp(cpuPct, 0, 100);
                }
            }
            _lastCpuTimeTicks = cpuNow;
            _lastCpuSampleAt  = wallNow;

            // Colour the label when the user is near or over the
            // memory budget: dim-gray normal, dark-orange > 70 %,
            // firebrick > 100 %.
            System.Drawing.Color fg = System.Drawing.Color.DimGray;
            int budgetMB = ResourceBudget.MemoryBudget_MB;
            if (budgetMB > 0)
            {
                double frac = memMb / budgetMB;
                if (frac > 1.0) fg = System.Drawing.Color.Firebrick;
                else if (frac > 0.70) fg = System.Drawing.Color.DarkOrange;
            }
            lblResourceGauge.ForeColor = fg;
            lblResourceGauge.Text = budgetMB > 0
                ? $"MEM: {memMb:F0}/{budgetMB} MB  CPU: {cpuPct:F0} %"
                : $"MEM: {memMb:F0} MB  CPU: {cpuPct:F0} %";
        }
        catch (System.Exception ex)
        {
            lblResourceGauge.Text = "MEM/CPU: —";
            System.Diagnostics.Debug.WriteLine($"ResourceGauge: {ex.Message}");
        }
    }

    /// <summary>
    /// Refresh the pre-flight memory-projection
    /// indicator in the status bar. Called from <see cref="MaybePush"/>
    /// on every parameter change. Pure math — one cheap
    /// <see cref="Voxelforge.Analysis.MemoryProjectionGate.ProjectPreflight"/>
    /// call against the current form inputs. Colour-codes dim-gray
    /// (safe), dark-orange (> 70 %), firebrick (> 100 %). Also appends
    /// a "(large thrust — set Auto-coarsen or Tile)" hint when thrust
    /// exceeds 10 kN so the user sees the recommended mitigations
    /// without hunting through settings.
    /// </summary>
    private void UpdatePreflightProjection()
    {
        if (lblPreflightProjection is null || IsDisposed) return;
        try
        {
            var cond   = ReadConditions();
            var design = ReadDesign();
            long budget = ResourceBudget.MemoryBudget_MB > 0
                ? (long)ResourceBudget.MemoryBudget_MB * 1024L * 1024L
                : 0L;

            var p = Voxelforge.Analysis.MemoryProjectionGate.ProjectPreflight(
                cond, design, Voxelforge.Program.VoxelSizeMM, budget);

            System.Drawing.Color fg = System.Drawing.Color.DimGray;
            if (p.Level == Voxelforge.Analysis.MemoryProjectionLevel.Fail)
                fg = System.Drawing.Color.Firebrick;
            else if (p.Level == Voxelforge.Analysis.MemoryProjectionLevel.Warning)
                fg = System.Drawing.Color.DarkOrange;

            string memStr = p.ProjectedBytes > 0
                ? $"{p.ProjectedBytes / 1_048_576:N0} MB"
                : "\u2014";
            string budgetStr = budget > 0
                ? $"/ {budget / 1_048_576:N0} MB"
                : "";
            string thrustTag = cond.Thrust_N > 10000
                ? "  (large thrust)"
                : "";

            // Auto-activate hint: when projection
            // would be Fail-level AND the user has not opted into either
            // mitigation path (auto-coarsen / tile-large-builds), append
            // a prescriptive "→ tile or coarsen" pointer so the fix is
            // obvious without digging into the Resource Budget group.
            // When the user opted into isolate-
            // large-builds, append "(isolate: scaffold)" so they know the
            // infrastructure exists but dispatch is still in-process.
            string hint = "";
            if (p.Level == Voxelforge.Analysis.MemoryProjectionLevel.Fail
                && !ResourceBudget.AutoCoarsenVoxelToFitBudget
                && !ResourceBudget.TileLargeBuilds)
            {
                hint = "  → enable Tile / Auto-coarsen";
            }
            if (p.Level == Voxelforge.Analysis.MemoryProjectionLevel.Fail
                && ResourceBudget.IsolateLargeBuildsAtFailProjection)
            {
                hint += "  [isolate: scaffold]";
            }

            lblPreflightProjection.ForeColor = fg;
            lblPreflightProjection.Text = $"Next Generate: {memStr}{budgetStr}{thrustTag}{hint}";
        }
        catch (System.Exception ex)
        {
            // Don't let a stale / transient input state crash the form.
            lblPreflightProjection.Text = "Next Generate: \u2014";
            System.Diagnostics.Debug.WriteLine($"PreflightProjection: {ex.Message}");
        }
    }

    private void PollOptProgress()
    {
        UpdateResourceGauge();   // Refresh every 250 ms tick.
        var p = _getOptProgress();
        if (p == null) return;

        int pct = p.MaxIterations > 0 ? Math.Clamp(p.Iteration * 100 / p.MaxIterations, 0, 100) : 0;
        pbOpt.Value = pct;
        lblOptProgress.Text = p.IsRunning
            ? $"Iter {p.Iteration}/{p.MaxIterations}  T={p.Temperature:F1}  best={p.BestScore:F2}  restarts={p.RestartCount}"
            : p.Iteration > 0 ? $"Stopped. Best={p.BestScore:F2}  @ iter {p.Iteration}  restarts={p.RestartCount}" : "";

        btnStopOpt.Enabled = p.IsRunning;
        btnStartOpt.Enabled = !p.IsRunning;
        btnRunBatch.Enabled = !p.IsRunning;    // 2026-04-22: batch run gated on same state

        // Stream the best-so-far into the convergence
        // trace. AppendPoint deduplicates same-iteration updates and
        // skips non-finite placeholders, so feeding every poll is safe.
        if (p.Iteration > 0)
            optConvergencePanel.AppendPoint(p.Iteration, p.BestScore, p.RestartCount);
    }

    // ═══════════════════════════════════════════════════════════════
    //   UI builders
    // ═══════════════════════════════════════════════════════════════
    //
    // Sprint 6 Track B (2026-04-22) moved the control-factory methods
    // (Num / SetNum / Out / Pill / ApplyStabilityPill / Row / Group /
    // Group-collapsible / MakeHelp) into the partial-class sibling
    // RegenChamberForm.Builders.cs. They remain `private static` and
    // every call site in this file keeps working unchanged.

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>
    /// Capture window geometry + UI toggles into
    /// <see cref="SessionSettings"/> and write to disk on close.
    /// Best-effort — failures log but never block close.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try
        {
            _settings.WindowWidth  = this.WindowState == FormWindowState.Normal ? this.Width  : this.RestoreBounds.Width;
            _settings.WindowHeight = this.WindowState == FormWindowState.Normal ? this.Height : this.RestoreBounds.Height;
            _settings.WindowX      = this.WindowState == FormWindowState.Normal ? this.Location.X : this.RestoreBounds.X;
            _settings.WindowY      = this.WindowState == FormWindowState.Normal ? this.Location.Y : this.RestoreBounds.Y;
            _settings.LivePreviewEnabled = chkLivePreview?.Checked ?? true;
            _settings.Save();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SessionSettings persist failed: {ex.Message}");
        }
        base.OnFormClosing(e);
    }
}
