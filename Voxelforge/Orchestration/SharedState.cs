// SharedState.cs — cross-thread one-shot message bus.
//
// Extracted from Program.cs (Sprint 0 / Wave 1, 2026-05-05) as part of
// the dispatcher decomposition. The behavior is unchanged — every public
// method has the same signature and lock semantics as before.
//
// Every field is protected by the single _lock. UI thread "Post" methods
// set flags + data; the task thread's "TryTake" methods consume them
// atomically. WriteOptProgress/ReadOptProgress is a rolling snapshot (not
// one-shot) since the UI polls it on a 250 ms timer.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Geometry;
using Voxelforge.Optimization;
using Voxelforge.UI;

namespace Voxelforge;

internal static class SharedState
{
    private static readonly object _lock = new();

    // Param change
    private static bool _hasParamChange;
    private static OperatingConditions? _pendingCond;
    private static RegenChamberDesign? _pendingDesign;

    // Export STL
    private static bool _hasExportStl;
    private static string? _pendingStlPath;
    private static float _pendingStlVoxelMM;
    private static bool _pendingStlMonolithic;

    // Export 3MF (PHASE 7)
    private static bool _hasExport3MF;
    private static string? _pending3MFPath;

    // Export report
    private static bool _hasExportReport;
    private static string? _pendingReportPath;

    // Export VTK ImageData (.vti) CFD fields.
    // Task-thread inspects _lastResult to pick bell vs aerospike.
    private static bool _hasExportVti;
    private static string? _pendingVtiPath;

    // Start batch (2026-04-22)
    private static bool _hasStartBatch;
    private static OptSettings? _pendingBatchOpt;
    private static BatchRunSettings? _pendingBatchRun;

    // Save design
    private static bool _hasSaveDesign;
    private static string? _pendingSavePath;
    private static OperatingConditions? _pendingSaveCond;
    private static RegenChamberDesign? _pendingSaveDesign;

    // Optimization control
    private static bool _hasStartOpt;
    private static OptSettings? _pendingOptSettings;
    private static bool _stopOptRequested;

    // Rolling progress snapshot (not one-shot)
    private static OptProgress? _optProgress;

    // ── Producers ──────────────────────────────────────────────────────

    public static void PostParamChange(OperatingConditions c, RegenChamberDesign d)
    {
        lock (_lock) { _hasParamChange = true; _pendingCond = c; _pendingDesign = d; }
    }

    public static void PostExportStl(string path, float voxelMM, bool monolithic)
    {
        lock (_lock)
        {
            _hasExportStl = true;
            _pendingStlPath = path;
            _pendingStlVoxelMM = voxelMM;
            _pendingStlMonolithic = monolithic;
        }
    }

    public static void PostExport3MF(string path)
    {
        lock (_lock) { _hasExport3MF = true; _pending3MFPath = path; }
    }

    public static void PostExportReport(string path)
    {
        lock (_lock) { _hasExportReport = true; _pendingReportPath = path; }
    }

    public static void PostExportVti(string path)
    {
        lock (_lock) { _hasExportVti = true; _pendingVtiPath = path; }
    }

    public static void PostSaveDesign(string path, OperatingConditions c, RegenChamberDesign d)
    {
        lock (_lock)
        {
            _hasSaveDesign = true; _pendingSavePath = path;
            _pendingSaveCond = c; _pendingSaveDesign = d;
        }
    }

    public static void PostStartOpt(OptSettings settings)
    {
        lock (_lock) { _hasStartOpt = true; _pendingOptSettings = settings; }
    }

    public static void PostStartBatch(OptSettings settings, BatchRunSettings batch)
    {
        lock (_lock)
        {
            _hasStartBatch = true;
            _pendingBatchOpt = settings;
            _pendingBatchRun = batch;
        }
    }

    public static void PostStopOpt()
    {
        lock (_lock) { _stopOptRequested = true; }
    }

    // Distinct from StopOpt — a "cancel current op"
    // request from the UI (e.g. user edited an input during a
    // running tolerance sweep, or switched away from the app with
    // AdaptiveForegroundThrottle on). The task thread's main loop
    // polls this and calls `CancelCurrentOp()` which trips the
    // CTS shared by Parallel.For / ToleranceAnalysis.Run / SA.
    private static bool _cancelCurrentOpRequested;
    public static void PostCancelCurrentOp()
    {
        lock (_lock) { _cancelCurrentOpRequested = true; }
    }
    public static bool TryTakeCancelCurrentOp()
    {
        lock (_lock)
        {
            if (!_cancelCurrentOpRequested) return false;
            _cancelCurrentOpRequested = false;
            return true;
        }
    }

    // ── Consumers (task thread) ────────────────────────────────────────

    public static bool TryTakeParamChange(out OperatingConditions? c, out RegenChamberDesign? d)
    {
        lock (_lock)
        {
            if (!_hasParamChange) { c = null; d = null; return false; }
            c = _pendingCond; d = _pendingDesign;
            _hasParamChange = false; _pendingCond = null; _pendingDesign = null;
            return true;
        }
    }

    public static bool TryTakeExportStl(out string? path, out float voxelMM, out bool monolithic)
    {
        lock (_lock)
        {
            if (!_hasExportStl) { path = null; voxelMM = 0; monolithic = false; return false; }
            path = _pendingStlPath;
            voxelMM = _pendingStlVoxelMM;
            monolithic = _pendingStlMonolithic;
            _hasExportStl = false; _pendingStlPath = null; _pendingStlVoxelMM = 0;
            _pendingStlMonolithic = false;
            return true;
        }
    }

    public static bool TryTakeExport3MF(out string? path)
    {
        lock (_lock)
        {
            if (!_hasExport3MF) { path = null; return false; }
            path = _pending3MFPath; _hasExport3MF = false; _pending3MFPath = null;
            return true;
        }
    }

    public static bool TryTakeExportReport(out string? path)
    {
        lock (_lock)
        {
            if (!_hasExportReport) { path = null; return false; }
            path = _pendingReportPath; _hasExportReport = false; _pendingReportPath = null;
            return true;
        }
    }

    public static bool TryTakeExportVti(out string? path)
    {
        lock (_lock)
        {
            if (!_hasExportVti) { path = null; return false; }
            path = _pendingVtiPath; _hasExportVti = false; _pendingVtiPath = null;
            return true;
        }
    }

    public static bool TryTakeSaveDesign(out string? path, out OperatingConditions? c, out RegenChamberDesign? d)
    {
        lock (_lock)
        {
            if (!_hasSaveDesign) { path = null; c = null; d = null; return false; }
            path = _pendingSavePath; c = _pendingSaveCond; d = _pendingSaveDesign;
            _hasSaveDesign = false; _pendingSavePath = null; _pendingSaveCond = null; _pendingSaveDesign = null;
            return true;
        }
    }

    public static bool TryTakeStartOpt(out OptSettings? settings)
    {
        lock (_lock)
        {
            if (!_hasStartOpt) { settings = null; return false; }
            settings = _pendingOptSettings; _hasStartOpt = false; _pendingOptSettings = null;
            return true;
        }
    }

    public static bool TryTakeStartBatch(out OptSettings? settings, out BatchRunSettings? batch)
    {
        lock (_lock)
        {
            if (!_hasStartBatch) { settings = null; batch = null; return false; }
            settings = _pendingBatchOpt; batch = _pendingBatchRun;
            _hasStartBatch = false; _pendingBatchOpt = null; _pendingBatchRun = null;
            return true;
        }
    }

    public static bool TryTakeStopOpt()
    {
        lock (_lock)
        {
            if (!_stopOptRequested) return false;
            _stopOptRequested = false; return true;
        }
    }

    public static void WriteOptProgress(OptProgress p)
    {
        lock (_lock) { _optProgress = p; }
    }

    public static OptProgress? ReadOptProgress()
    {
        lock (_lock) { return _optProgress; }
    }

    // ── Air-breathing param change ─────────────────────────────────────

    private static bool _hasAbParamChange;
    private static FlightConditions? _pendingAbCond;
    private static AirbreathingEngineDesign? _pendingAbDesign;
    private static RamjetBuildOptions? _pendingAbOpts;

    public static void PostAirbreathingParamChange(
        FlightConditions c, AirbreathingEngineDesign d, RamjetBuildOptions o)
    {
        lock (_lock)
        {
            _hasAbParamChange = true;
            _pendingAbCond    = c;
            _pendingAbDesign  = d;
            _pendingAbOpts    = o;
        }
    }

    public static bool TryTakeAirbreathingParamChange(
        out FlightConditions? c, out AirbreathingEngineDesign? d, out RamjetBuildOptions? o)
    {
        lock (_lock)
        {
            if (!_hasAbParamChange) { c = null; d = null; o = null; return false; }
            c = _pendingAbCond; d = _pendingAbDesign; o = _pendingAbOpts;
            _hasAbParamChange = false;
            _pendingAbCond    = null;
            _pendingAbDesign  = null;
            _pendingAbOpts    = null;
            return true;
        }
    }

    // ── Electric-propulsion param change ──────────────────────────────────

    private static bool _hasEpParamChange;
    private static Voxelforge.ElectricPropulsion.ResistojetConditions? _pendingEpCond;
    private static Voxelforge.ElectricPropulsion.ElectricPropulsionEngineDesign? _pendingEpDesign;

    public static void PostElectricPropulsionParamChange(
        Voxelforge.ElectricPropulsion.ResistojetConditions c,
        Voxelforge.ElectricPropulsion.ElectricPropulsionEngineDesign d)
    {
        lock (_lock)
        {
            _hasEpParamChange = true;
            _pendingEpCond    = c;
            _pendingEpDesign  = d;
        }
    }

    public static bool TryTakeElectricPropulsionParamChange(
        out Voxelforge.ElectricPropulsion.ResistojetConditions? c,
        out Voxelforge.ElectricPropulsion.ElectricPropulsionEngineDesign? d)
    {
        lock (_lock)
        {
            if (!_hasEpParamChange) { c = null; d = null; return false; }
            c = _pendingEpCond; d = _pendingEpDesign;
            _hasEpParamChange = false;
            _pendingEpCond    = null;
            _pendingEpDesign  = null;
            return true;
        }
    }

    // ── Marine param change ───────────────────────────────────────────────

    private static bool _hasMarineParamChange;
    private static Voxelforge.Marine.MarineConditions? _pendingMarineCond;
    private static Voxelforge.Marine.MarineDesign? _pendingMarineDesign;

    public static void PostMarineParamChange(
        Voxelforge.Marine.MarineConditions c, Voxelforge.Marine.MarineDesign d)
    {
        lock (_lock)
        {
            _hasMarineParamChange = true;
            _pendingMarineCond    = c;
            _pendingMarineDesign  = d;
        }
    }

    public static bool TryTakeMarineParamChange(
        out Voxelforge.Marine.MarineConditions? c, out Voxelforge.Marine.MarineDesign? d)
    {
        lock (_lock)
        {
            if (!_hasMarineParamChange) { c = null; d = null; return false; }
            c = _pendingMarineCond; d = _pendingMarineDesign;
            _hasMarineParamChange = false;
            _pendingMarineCond    = null;
            _pendingMarineDesign  = null;
            return true;
        }
    }
}
