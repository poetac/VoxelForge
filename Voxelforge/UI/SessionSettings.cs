// SessionSettings.cs — Per-user UI state that survives a restart.
//
// Persisted state:
//   • Window size + location (last seen at form-close)
//   • Live-preview checkbox
//   • Last-used save folder + last-used load folder (so the
//     SaveFileDialog / OpenFileDialog open in the right place)
//   • Recent .rcd.json paths (last 10) for the U1.4 recent-files menu
//
// Storage location:
//   %LocalAppData%/Voxelforge/session.json
//
// Format: JSON via System.Text.Json. Round-trip stable; Load() never
// throws — if the file is missing or corrupt it returns a fresh
// instance with default values. Save() best-effort; failures log
// to Debug but do not propagate.
//
// Adding a new persisted field: add a property + default + version
// check the schema doesn't drift (no schema versioning today; add
// when the persisted set grows beyond the U1 scope).

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Voxelforge.UI;

public sealed class SessionSettings
{
    /// <summary>Last-seen form width [px]. 0 = use default.</summary>
    public int WindowWidth { get; set; } = 0;
    /// <summary>Last-seen form height [px]. 0 = use default.</summary>
    public int WindowHeight { get; set; } = 0;
    /// <summary>Last-seen form X. int.MinValue = use default (centred).</summary>
    public int WindowX { get; set; } = int.MinValue;
    /// <summary>Last-seen form Y. int.MinValue = use default (centred).</summary>
    public int WindowY { get; set; } = int.MinValue;

    /// <summary>Live-preview checkbox state. Default: ON.</summary>
    public bool LivePreviewEnabled { get; set; } = true;

    /// <summary>Last directory used for Save Design… / Save Pareto CSV…</summary>
    public string? LastSaveFolder { get; set; }
    /// <summary>Last directory used for Load Design… / Load Test Data…</summary>
    public string? LastLoadFolder { get; set; }

    /// <summary>
    /// Most-recent .rcd.json paths, newest-first, max length
    /// <see cref="MaxRecentFiles"/>. Drives the U1.4 recent-files menu.
    /// </summary>
    public System.Collections.Generic.List<string> RecentDesigns { get; set; } = new();

    [JsonIgnore]
    public const int MaxRecentFiles = 10;

    // ─── Resource Budget ──────────────────────────────────────────
    // Resource-budget fields: user tells the app how much of the
    // machine it's allowed to use. ResourceMode is the headline
    // preset; the explicit caps below it are honoured when Mode ==
    // Custom (or when a preset's resolved value is overridden).
    // A value of 0 means "use the preset default" so upgrading legacy
    // saved session files round-trips without breakage.

    /// <summary>
    /// Headline resource mode. Quiet / Balanced / Maximum / Custom.
    /// Default Balanced matches a workstation with headroom left for
    /// the user's other apps. Quiet recommended on laptops / battery.
    /// Maximum unlocks all cores + no memory cap for dedicated runs.
    /// </summary>
    public ResourceMode ResourceMode { get; set; } = ResourceMode.Balanced;

    /// <summary>
    /// Max parallel degree for `Parallel.For` paths (tolerance sweep,
    /// SA batch). 0 = resolve from ResourceMode. Clamped to
    /// [1, Environment.ProcessorCount] at use.
    /// </summary>
    public int MaxParallelism { get; set; } = 0;

    /// <summary>
    /// Memory budget cap in MB. Preflight projection gate fails when
    /// a build projects above this. 0 = resolve from ResourceMode.
    /// Applied both to the main process (warning) and to the
    /// StlExporter subprocess (hard Job Object cap).
    /// </summary>
    public int MemoryBudget_MB { get; set; } = 0;

    /// <summary>
    /// When true, main process is demoted to
    /// <see cref="System.Diagnostics.ProcessPriorityClass.BelowNormal"/>
    /// while heavy solves run; restored to Normal when idle. The
    /// StlExporter subprocess inherits the same demotion.
    /// </summary>
    public bool DemotePriorityDuringSolves { get; set; } = true;

    /// <summary>
    /// Auto-flip to Quiet when `SystemInformation.PowerStatus` reports
    /// battery. Resumes prior mode when AC returns.
    /// </summary>
    public bool BatteryAwareQuiet { get; set; } = true;

    /// <summary>
    /// When the main form loses foreground focus, scale the resource
    /// budget down to the Quiet preset. Restores on regain.
    /// </summary>
    public bool AdaptiveForegroundThrottle { get; set; } = false;

    /// <summary>
    /// Flip `GCSettings.LatencyMode` between Interactive (idle) and
    /// SustainedLowLatency (heavy work) to smooth UI paint.
    /// </summary>
    public bool GcLatencyTuning { get; set; } = false;

    /// <summary>
    /// Time budget for the Monte-Carlo tolerance sweep in seconds.
    /// 0 = no cap. When &gt; 0, the op's
    /// <see cref="System.Threading.CancellationTokenSource"/> gets a
    /// <c>CancelAfter</c> call so a runaway sweep unwinds cleanly.
    /// Default 0 preserves legacy behaviour; typical user value is
    /// 60–180.
    /// </summary>
    public int SweepTimeoutSeconds { get; set; } = 0;

    /// <summary>
    /// Time budget for simulated annealing runs in seconds. 0 = no
    /// cap. Wall-clock (not per-iteration); the SA dispatch loop
    /// simply sees the CTS fire and unwinds via
    /// <see cref="FinalizeOpt"/>. Default 0 preserves legacy
    /// behaviour.
    /// </summary>
    public int OptTimeoutSeconds { get; set; } = 0;

    /// <summary>
    /// When true, editing a design / operating-point input while a
    /// solve (SA or tolerance sweep) is in flight fires
    /// <c>SharedState.PostCancelCurrentOp</c>, unwinding the now-stale
    /// run via <see cref="System.OperationCanceledException"/>. Default
    /// <c>false</c> preserves legacy behaviour for users who prefer to
    /// let a background solve finish even after an input change.
    /// </summary>
    public bool AbortOpOnInputEdit { get; set; } = false;

    /// <summary>
    /// When true, a Generate / FinalizeOpt voxel build that would
    /// exceed the memory budget is automatically retried at the
    /// coarser voxel size computed by
    /// <see cref="Analysis.MemoryProjectionGate.SuggestCoarserVoxel"/>,
    /// instead of being blocked outright. Retries up to 3 levels, each
    /// time re-asking the gate for a fresh suggestion so a pathological
    /// chamber doesn't loop forever. The status bar announces the
    /// substitution ("Voxel auto-coarsened 0.40 → 0.85 mm to fit
    /// 16 GB budget") so the user knows the geometry was rendered at
    /// lower fidelity than requested. Default <c>false</c> preserves
    /// strict block-on-Fail behaviour for users who prefer explicit
    /// control; opting in is a single checkbox in the Resource Budget
    /// group.
    /// </summary>
    public bool AutoCoarsenVoxelToFitBudget { get; set; } = false;

    /// <summary>
    /// When true, manual Generate clicks force
    /// <see cref="Voxelforge.Optimization.ChannelTopology.None"/>
    /// for the preview build, skipping the full channel-voxelise pass
    /// (~84 % of build time at 0.4 mm voxel on a representative
    /// reference chamber). The user's actual design is preserved —
    /// this flag only changes what
    /// <see cref="Program.RegenerateForManualMode"/> renders. SA /
    /// FinalizeOpt / batch runs ignore it entirely so the "committed"
    /// path still produces a full flow-path model.
    /// Default <c>false</c> preserves legacy behaviour.
    /// </summary>
    public bool FastPreviewMode { get; set; } = false;

    /// <summary>
    /// When true, manual Generate clicks dispatch the voxel build
    /// through
    /// <see cref="Voxelforge.Geometry.ChamberAxialTileBuilder.BuildTiled"/>
    /// instead of the monolithic path. Peak memory per tile ≈ 1/N of
    /// the full grid, which lets a 50 kN design at 0.4 mm voxel fit
    /// inside a 16 GB budget that monolithic would refuse.
    /// Opt-in — default <c>false</c> preserves legacy behaviour.
    /// Paired with <see cref="TileCount"/> for the N splits.
    /// SA / Save / FinalizeOpt paths ignore this flag so committed
    /// designs always produce a monolithic STL (tiled mode is for
    /// interactive exploration of large-thrust designs).
    /// </summary>
    public bool TileLargeBuilds { get; set; } = false;

    /// <summary>
    /// Target tile count for tiled Generate. Clamped to [1, 32] by
    /// <see cref="ResourceBudget.ApplySettings"/>; the planner
    /// (<see cref="Geometry.ChamberAxialTileBuilder.PlanTiles"/>) may
    /// collapse to fewer if the per-tile core length would fall below
    /// <c>MinTileLength_mm</c>. Default 4 matches the Benchmarks
    /// harness default on the 2 224 N reference chamber.
    /// </summary>
    public int TileCount { get; set; } = 4;

    /// <summary>
    /// When true AND the pre-flight memory projection reports Fail,
    /// route the Generate() dispatch through
    /// <see cref="Geometry.BuildSubprocess"/> so a native OpenVDB OOM
    /// cannot crash the main-app UI. The subprocess runs under a
    /// Windows Job Object with the user's memory cap; on breach the
    /// kill is clean (exit code 12) instead of freezing the machine.
    ///
    /// Default false — the existing guardrails
    /// (<see cref="AutoCoarsenVoxelToFitBudget"/> + <see cref="TileLargeBuilds"/>)
    /// handle the common large-thrust path without losing the live
    /// voxel viewer. Power users enable this flag when the pre-flight
    /// projection keeps under-predicting and they need a hard OS-level
    /// crash guarantee. **Wiring into <c>RegenerateForManualMode</c>
    /// is a follow-on sprint** — today the flag round-trips through
    /// JSON but isn't consumed by the dispatch.
    /// </summary>
    public bool IsolateLargeBuildsAtFailProjection { get; set; } = false;

    // ─── Setup Wizard (UI overhaul Sprint 2) ──────────────────────
    // Persisted state for the SetupWizardForm. WizardVersion = 0 means
    // "user has never been through the wizard"; the current shipped
    // version is 1, so first-launch users get the wizard once. Users
    // can opt out via the "Skip wizard next time" checkbox on Page 3,
    // which sets SkipWizardOnLaunch = true. Resetting via the Help
    // menu sets WizardVersion back to 0.

    /// <summary>
    /// Highest wizard version this user has completed. 0 = first
    /// launch, no wizard ever shown. Bumps to the current shipped
    /// version when the user clicks Finish (or "Start fresh" without
    /// cancelling). The current version is the one a future schema
    /// migration will compare against if wizard contents change.
    /// </summary>
    public int WizardVersion { get; set; } = 0;

    /// <summary>
    /// When true, the wizard is suppressed on launch even when
    /// <see cref="WizardVersion"/> would otherwise trigger it.
    /// Toggled by the "Skip wizard next time" checkbox on Page 3,
    /// or "Help → Reset to wizard…" to clear the suppression and
    /// reset <see cref="WizardVersion"/> to 0.
    /// </summary>
    public bool SkipWizardOnLaunch { get; set; } = false;

    /// <summary>
    /// JSON-serialised <see cref="Optimization.OperatingConditions"/>
    /// + <see cref="Optimization.RegenChamberDesign"/> pair the
    /// wizard produced last time it ran. Used to round-trip the
    /// wizard's seed across launches so a user clicking "skip" still
    /// gets their last preset back. <c>null</c> on first launch.
    /// </summary>
    public string? LastSeedDesignJson { get; set; }

    /// <summary>
    /// Friendly name of the canonical preset the wizard last loaded
    /// (e.g., "merlin", "rl10"). Used to highlight that preset card
    /// on the next wizard run; null when the user picked "start
    /// from scratch" instead.
    /// </summary>
    public string? LastPresetName { get; set; }

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Standard storage path:
    /// <c>%LocalAppData%/Voxelforge/session.json</c>.
    /// </summary>
    public static string DefaultPath()
    {
        string baseDir = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "Voxelforge", "session.json");
    }

    /// <summary>
    /// Load from <see cref="DefaultPath"/> or the supplied path.
    /// Never throws — bad file / missing file returns a fresh defaults
    /// instance. Caller can detect "first run" by comparing the
    /// returned WindowWidth / Height against 0.
    /// </summary>
    public static SessionSettings Load(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            if (!File.Exists(path)) return new SessionSettings();
            string json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<SessionSettings>(json, Opts);
            return settings ?? new SessionSettings();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"SessionSettings.Load: ignoring bad file at {path}: {ex.Message}");
            return new SessionSettings();
        }
    }

    /// <summary>
    /// Save to <see cref="DefaultPath"/> or the supplied path.
    /// Best-effort: returns true on success, false on any failure.
    /// Creates the parent directory if missing.
    /// </summary>
    public bool Save(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Opts));
            return true;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"SessionSettings.Save: failed at {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Bump <paramref name="path"/> to the front of the recent-files
    /// list, dedup, cap at <see cref="MaxRecentFiles"/>. Best-effort;
    /// silently no-ops on null/empty.
    /// </summary>
    public void RegisterRecentDesign(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        // Case-insensitive dedup to match Windows file system behaviour.
        RecentDesigns.RemoveAll(p =>
            string.Equals(p, path, System.StringComparison.OrdinalIgnoreCase));
        RecentDesigns.Insert(0, path);
        while (RecentDesigns.Count > MaxRecentFiles)
            RecentDesigns.RemoveAt(RecentDesigns.Count - 1);
    }
}
