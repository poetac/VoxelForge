// ResourceBudget.cs (Core half) — Single source of truth for "what
// are the current active resource limits" across the app.
//
// Sprint A-3 Phase 2 / ADR-021 (2026-04-30): split into a partial
// class so the Core-resident orchestrator (RegenChamberOptimization,
// moving to Core in Phase 2) can read the live caps without
// referencing the App project. This file holds the storage fields,
// read-only properties, and primitive-arg `Apply` / `ResolveDefaults`
// methods. The App-side partial
// (`Voxelforge/UI/ResourceBudget.Settings.cs`) holds the
// `ApplySettings(SessionSettings)` and `AutoProbeDefaults(SessionSettings)`
// extensions because `SessionSettings` is App-side WinForms-tied.
//
// Why a global: the Parallel.For callers in ToleranceAnalysis + SA
// batch run on the task thread, the StlExporter subprocess launch
// runs on the threadpool, and the UI reads the live values to render
// the preset combo + resource gauge. Threading a SessionSettings
// reference into every callsite would mean touching ~10 files every
// time the user nudges the mode; a process-wide static read with
// volatile snapshot fields keeps the wiring minimal.

using System;
using System.Threading;

namespace Voxelforge.UI;

public static class ResourceBudget
{
    // Live snapshot. Volatile so a Parallel.For or SA batch reading
    // MaxParallelism always sees the latest value without a lock.
    private static int  _maxParallelism    = Math.Max(1, Environment.ProcessorCount - 2);
    private static int  _memoryBudgetMB    = 0;     // 0 = no cap
    private static bool _demotePriority    = true;
    private static bool _gcLatencyTuning;
    private static int  _sweepTimeoutSec;           // 0 = no cap
    private static int  _optTimeoutSec;             // 0 = no cap
    private static bool _abortOpOnInputEdit;        // Cancel-on-edit
    private static bool _autoCoarsenVoxel;          // Auto-coarsen voxel on memory-gate fail
    private static bool _fastPreviewMode;           // Channels-skipped fast preview
    private static bool _tileLargeBuilds;           // Tiled-Generate dispatch
    private static int  _tileCount = 4;             // Tile count
    private static bool _isolateLargeBuilds;        // Isolate-large-builds subprocess scaffold
    private static ResourceMode _mode      = ResourceMode.Balanced;
    private static int _totalCoresCache    = Environment.ProcessorCount;
    private static long _totalMemoryMBCache;        // lazy-filled on first probe

    /// <summary>Current active cap on Parallel.For / SA batch workers. Always ≥ 1.</summary>
    public static int MaxParallelism => Volatile.Read(ref _maxParallelism);

    /// <summary>Current active memory cap in MB. 0 means no cap.</summary>
    public static int MemoryBudget_MB => Volatile.Read(ref _memoryBudgetMB);

    /// <summary>
    /// Convert the memory cap to bytes for Win32 APIs (Job Object,
    /// <see cref="System.Diagnostics.Process.MaxWorkingSet"/>). 0 when no cap.
    /// </summary>
    public static ulong MemoryBudget_Bytes
        => MemoryBudget_MB > 0 ? (ulong)MemoryBudget_MB * 1024UL * 1024UL : 0UL;

    /// <summary>Whether heavy solves should run at BelowNormal priority.</summary>
    public static bool DemotePriority => Volatile.Read(ref _demotePriority) ? true : false;

    /// <summary>Flip GCSettings.LatencyMode during heavy work.</summary>
    public static bool GcLatencyTuning => Volatile.Read(ref _gcLatencyTuning) ? true : false;

    /// <summary>
    /// Time budget for the Monte-Carlo tolerance sweep in seconds.
    /// 0 = no cap (token-based plumbing ready but not armed).
    /// </summary>
    public static int SweepTimeoutSeconds => Volatile.Read(ref _sweepTimeoutSec);

    /// <summary>
    /// Time budget for simulated annealing in seconds. 0 = no cap.
    /// </summary>
    public static int OptTimeoutSeconds => Volatile.Read(ref _optTimeoutSec);

    /// <summary>
    /// When true, a form-side input edit during an in-flight solve
    /// posts a cancel request. Default <c>false</c>.
    /// </summary>
    public static bool AbortOpOnInputEdit => Volatile.Read(ref _abortOpOnInputEdit);

    /// <summary>
    /// When true, a Generate / FinalizeOpt voxel build that would be
    /// blocked by <see cref="Analysis.MemoryProjectionGate.EnsureFits"/>
    /// is transparently retried at the coarser voxel size the gate
    /// suggests, up to 3 levels, with a status-bar announcement.
    /// </summary>
    public static bool AutoCoarsenVoxelToFitBudget => Volatile.Read(ref _autoCoarsenVoxel);

    /// <summary>
    /// When true, manual Generate clicks render a channels-skipped
    /// "fast preview" (~10× faster than full build).
    /// </summary>
    public static bool FastPreviewMode => Volatile.Read(ref _fastPreviewMode);

    /// <summary>
    /// When true, manual Generate clicks route through tiled-build
    /// dispatch instead of the monolithic voxel pipeline.
    /// </summary>
    public static bool TileLargeBuilds => Volatile.Read(ref _tileLargeBuilds);

    /// <summary>
    /// Requested tile count. Clamped to [1, 32] in <see cref="Apply"/>.
    /// </summary>
    public static int TileCount => Volatile.Read(ref _tileCount);

    /// <summary>
    /// Mirrors the App-side
    /// <c>SessionSettings.IsolateLargeBuildsAtFailProjection</c>.
    /// </summary>
    public static bool IsolateLargeBuildsAtFailProjection => Volatile.Read(ref _isolateLargeBuilds);

    /// <summary>Currently active mode (Quiet / Balanced / Maximum / Custom).</summary>
    public static ResourceMode CurrentMode => _mode;

    /// <summary>Cached machine-total core count (snapshotted at first probe).</summary>
    public static int TotalCores => _totalCoresCache;

    /// <summary>Cached machine-total RAM in MB (snapshotted at first probe).</summary>
    public static long TotalMemory_MB
    {
        get
        {
            long cached = Volatile.Read(ref _totalMemoryMBCache);
            if (cached > 0) return cached;
            long probed = ProbeTotalMemoryMB();
            Volatile.Write(ref _totalMemoryMBCache, probed);
            return probed;
        }
    }

    /// <summary>
    /// Sprint A-3 Phase 2 / ADR-021: apply primitive snapshot values
    /// without referencing SessionSettings. The App-side
    /// <c>ApplySettings(SessionSettings)</c> partial-class extension
    /// routes through here.
    /// </summary>
    public static void Apply(
        ResourceMode mode,
        int maxParallelism, int memoryBudget_MB,
        bool demotePriority, bool gcLatencyTuning,
        int sweepTimeoutSec, int optTimeoutSec,
        bool abortOpOnInputEdit, bool autoCoarsenVoxel,
        bool fastPreviewMode, bool tileLargeBuilds, int tileCount,
        bool isolateLargeBuilds)
    {
        _mode = mode;
        var resolved = ResourcePresets.Resolve(mode, TotalCores, TotalMemory_MB);

        int cores = maxParallelism > 0 ? maxParallelism : resolved.MaxCores;
        cores = Math.Clamp(cores, 1, TotalCores);

        int mem = memoryBudget_MB > 0 ? memoryBudget_MB : resolved.MemoryBudget_MB;

        Volatile.Write(ref _maxParallelism, cores);
        Volatile.Write(ref _memoryBudgetMB, mem);
        Volatile.Write(ref _demotePriority, demotePriority && resolved.DemotePriority);
        Volatile.Write(ref _gcLatencyTuning, gcLatencyTuning);
        Volatile.Write(ref _sweepTimeoutSec, Math.Max(0, sweepTimeoutSec));
        Volatile.Write(ref _optTimeoutSec, Math.Max(0, optTimeoutSec));
        Volatile.Write(ref _abortOpOnInputEdit, abortOpOnInputEdit);
        Volatile.Write(ref _autoCoarsenVoxel, autoCoarsenVoxel);
        Volatile.Write(ref _fastPreviewMode, fastPreviewMode);
        Volatile.Write(ref _tileLargeBuilds, tileLargeBuilds);
        Volatile.Write(ref _tileCount, Math.Clamp(tileCount, 1, 32));
        Volatile.Write(ref _isolateLargeBuilds, isolateLargeBuilds);
    }

    /// <summary>
    /// Sprint A-3 Phase 2 / ADR-021: pure resolution helper for
    /// auto-probing default cap values. The App-side
    /// <c>AutoProbeDefaults(SessionSettings)</c> partial-class
    /// extension folds this back into the user's settings.
    /// </summary>
    public static ResourcePresets.Resolved ResolveDefaults(ResourceMode mode)
        => ResourcePresets.Resolve(mode, TotalCores, TotalMemory_MB);

    /// <summary>
    /// Probe total physical memory in MB. Falls back to 8192 (8 GB)
    /// when the system probe fails — a conservative, workable default.
    /// </summary>
    private static long ProbeTotalMemoryMB()
    {
        try
        {
            var memStatus = new NativeMethods.MEMORYSTATUSEX();
            memStatus.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(memStatus);
            if (NativeMethods.GlobalMemoryStatusEx(ref memStatus))
                return (long)(memStatus.ullTotalPhys / (1024 * 1024));
        }
        catch
        {
            // fall through
        }
        // GCMemoryInfo gives us a conservative fallback based on what
        // the GC thinks is available — at least as good as hard-coding.
        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0)
                return info.TotalAvailableMemoryBytes / (1024 * 1024);
        }
        catch { }
        return 8192;
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll",
            CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        internal struct MEMORYSTATUSEX
        {
            public uint   dwLength;
            public uint   dwMemoryLoad;
            public ulong  ullTotalPhys;
            public ulong  ullAvailPhys;
            public ulong  ullTotalPageFile;
            public ulong  ullAvailPageFile;
            public ulong  ullTotalVirtual;
            public ulong  ullAvailVirtual;
            public ulong  ullAvailExtendedVirtual;
        }
    }
}
