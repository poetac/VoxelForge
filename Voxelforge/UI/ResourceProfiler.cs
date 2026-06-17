// ResourceProfiler.cs — Wraps a long-running op with wall-clock,
// CPU-time, and peak working-set
// tracking, emits a structured BENCH line bundle to Library.Log on
// End(), and offers a Format()ed summary for the status bar.
//
// Why:
//   E3 — "Peak 4.2 GB / 87 % CPU / 42 s" at the end of SA + tolerance
//        sweep trains the user's intuition about what each mode costs.
//   E4 — The rest of the codebase already emits BENCH lines
//        (`BuildProfile.EmitBench`, `ExportStlProfiled`, the
//        Benchmarks console app). Extending the pattern to SA and
//        tolerance means one grep over Library.Log captures every
//        heavy op.
//   B6 — SetProcessWorkingSetSize(-1,-1) hint. Called unconditionally
//        after End(). Windows is free to ignore it; at best it
//        returns pages to the OS so a backgrounded IDE/browser has
//        room to page back in.
//
// Concurrency model:
//   One in-flight op at a time. Begin/End are guarded by _lock;
//   RecordWorkingSetSample is lock-free (Interlocked CAS) so the
//   250 ms form tick can feed samples without contending with End().
//   The form's `UpdateResourceGauge` pushes a sample each tick; if
//   no op is in flight, that call is a cheap no-op.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using PicoGK;

namespace Voxelforge.UI;

public readonly struct ResourceOpSummary
{
    public double WallMs               { get; init; }
    public double CpuMs                { get; init; }
    public double CpuPct               { get; init; }
    public long   PeakWorkingSetBytes  { get; init; }
    public long   FinalWorkingSetBytes { get; init; }

    /// <summary>
    /// Human-readable one-liner for the status bar. Picks MB vs GB
    /// and s vs min based on magnitude so long SA runs don't report
    /// "2820 s".
    /// </summary>
    public string Format()
    {
        double peakGB = PeakWorkingSetBytes / (1024.0 * 1024.0 * 1024.0);
        double peakMB = PeakWorkingSetBytes / (1024.0 * 1024.0);
        string memStr = peakGB >= 1.0 ? $"{peakGB:F1} GB" : $"{peakMB:F0} MB";
        double wallSec = WallMs / 1000.0;
        string timeStr = wallSec >= 60
            ? $"{wallSec / 60.0:F1} min"
            : $"{wallSec:F1} s";
        return $"Peak {memStr} / {CpuPct:F0}% CPU / {timeStr}";
    }
}

public static class ResourceProfiler
{
    // Active op name — null when nothing is in flight. Single-op
    // discipline keeps the state minimal; SA and tolerance don't
    // overlap in practice (UI serialises them).
    private static string? _currentOpName;
    private static long _wallStartTicks;
    private static long _cpuStartTicks;
    private static long _opPeakWorkingSetBytes;  // updated via Interlocked
    private static readonly object _lock = new();

    // Last summary by opName. Kept so the UI (form) can read it
    // without a task→UI delegate round-trip — the form's RunTolerance
    // path calls End() indirectly via Program._runTolerance and then
    // reads LastSummary("tol") for the status-bar tail.
    private static readonly ConcurrentDictionary<string, ResourceOpSummary> _lastSummaries = new();

    public static bool    IsOpInFlight    => _currentOpName is not null;
    public static string? CurrentOpName   => _currentOpName;

    /// <summary>Last captured summary for <paramref name="opName"/>, or default.</summary>
    public static ResourceOpSummary LastSummary(string opName)
        => _lastSummaries.TryGetValue(opName, out var s) ? s : default;

    /// <summary>
    /// Start an op. Call <see cref="End"/> in a finally to guarantee
    /// the in-flight flag clears even on an exception path.
    /// </summary>
    public static void Begin(string opName)
    {
        lock (_lock)
        {
            long wsAtStart = 0;
            try
            {
                using var p = Process.GetCurrentProcess();
                _cpuStartTicks = p.TotalProcessorTime.Ticks;
                wsAtStart      = p.WorkingSet64;
            }
            catch
            {
                _cpuStartTicks = 0;
            }
            _wallStartTicks = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref _opPeakWorkingSetBytes, wsAtStart);
            // Re-arm the watchdog for each new op so a
            // previously-tripped flag doesn't block the next run.
            Interlocked.Exchange(ref _watchdogTripped,          0);
            Interlocked.Exchange(ref _lastWatchdogTripWsBytes,  0);
            _currentOpName  = opName;
        }
    }

    /// <summary>
    /// Fed from the form's 250 ms poll timer. Lock-free max-update so
    /// the UI thread never blocks on the task-thread End(). Cheap
    /// no-op when no op is in flight.
    /// </summary>
    public static void RecordWorkingSetSample(long wsBytes)
    {
        if (_currentOpName is null) return;
        long cur;
        do
        {
            cur = Interlocked.Read(ref _opPeakWorkingSetBytes);
            if (wsBytes <= cur) return;
        }
        while (Interlocked.CompareExchange(ref _opPeakWorkingSetBytes, wsBytes, cur) != cur);

        // Working-set watchdog. When the in-flight op's
        // working set crosses WatchdogFraction of the configured memory
        // budget AND the user has opted in to the watchdog (on by default
        // in presets that cap memory), post a cancel request on the
        // shared cancellation channel. The main dispatch loop picks it
        // up on the next scheduling cycle, unwinds the op via
        // OperationCanceledException, and surfaces a status-bar message.
        // This is hard protection against the soft gate missing a
        // fast-growing allocation (e.g. if the projection underestimated
        // sparsity for this particular design).
        if (!_watchdogEnabled) return;
        long budget = ResourceBudget.MemoryBudget_MB > 0
            ? (long)ResourceBudget.MemoryBudget_MB * 1024L * 1024L
            : 0L;
        if (budget <= 0) return;
        long threshold = (long)(budget * WatchdogFraction);
        if (wsBytes < threshold) return;

        // Only trip ONCE per op — re-entry is prevented by an Interlocked
        // flag so a 250 ms poll that keeps sampling over threshold does
        // not spam the cancel channel.
        if (Interlocked.CompareExchange(ref _watchdogTripped, 1, 0) != 0) return;

        // Assign diagnostics BEFORE the side-effects below: PicoGK.Library.Log
        // throws in any test-host context that hasn't initialised a Library,
        // and we need the tripped-bytes diagnostic to survive that for the
        // ResourceProfiler.LastWatchdogTripWsBytes reader.
        Interlocked.Exchange(ref _lastWatchdogTripWsBytes, wsBytes);

        try { SharedState.PostCancelCurrentOp(); }
        catch { /* can't post cancel → let natural GC pressure handle it */ }

        try
        {
            PicoGK.Library.Log(
                $"ResourceProfiler watchdog: working set {wsBytes / 1_048_576:N0} MB "
              + $"≥ {WatchdogFraction * 100:F0} % of {ResourceBudget.MemoryBudget_MB:N0} MB budget — "
              + $"posting cancel for op '{_currentOpName}'.");
        }
        catch { /* no Library init in headless tests — silently skip logging */ }
    }

    /// <summary>
    /// Watchdog trip fraction. When the in-flight op's
    /// working set crosses this fraction of the configured budget, the
    /// watchdog posts a cancel request. 0.95 gives a ~300 MB breathing
    /// room on a 6 GB budget for the unwind to complete before actually
    /// hitting the cap.
    /// </summary>
    public const double WatchdogFraction = 0.95;

    private static volatile bool _watchdogEnabled = true;
    private static int           _watchdogTripped;          // Interlocked 0/1
    private static long          _lastWatchdogTripWsBytes;  // diagnostic

    /// <summary>
    /// Enable or disable the watchdog at runtime. Default on. Tests flip
    /// it off so they can feed samples above threshold without triggering
    /// the SharedState cancel channel in a shared-process environment.
    /// </summary>
    public static void SetWatchdogEnabled(bool enabled) => _watchdogEnabled = enabled;

    /// <summary>True when the watchdog has tripped for the current op.</summary>
    public static bool WatchdogTripped => Interlocked.CompareExchange(ref _watchdogTripped, 0, 0) != 0;

    /// <summary>
    /// Working-set bytes recorded at the moment the watchdog tripped,
    /// or 0 when the watchdog is still armed. Surfaced by the form's
    /// status-bar "watchdog stopped op at N MB" message.
    /// </summary>
    public static long LastWatchdogTripWsBytes => Interlocked.Read(ref _lastWatchdogTripWsBytes);

    /// <summary>
    /// End the op. Returns a summary record; emits BENCH lines to
    /// Library.Log when <paramref name="emitBench"/> is true. Always
    /// fires a SetProcessWorkingSetSize(-1,-1) trim hint (B6).
    /// Safe to call with a mismatched name — no-ops and returns
    /// default rather than clobbering an unrelated active op.
    /// </summary>
    public static ResourceOpSummary End(string opName, bool emitBench = true)
    {
        lock (_lock)
        {
            if (_currentOpName != opName) return default;

            double wallMs = (Stopwatch.GetTimestamp() - _wallStartTicks)
                          * 1000.0 / Stopwatch.Frequency;
            long cpuTicks = 0;
            long curWs    = 0;
            long peakWs   = Interlocked.Read(ref _opPeakWorkingSetBytes);
            try
            {
                using var p = Process.GetCurrentProcess();
                cpuTicks = Math.Max(0, p.TotalProcessorTime.Ticks - _cpuStartTicks);
                curWs    = p.WorkingSet64;
                if (curWs > peakWs) peakWs = curWs;
            }
            catch { }

            double cpuMs   = cpuTicks * 1000.0 / TimeSpan.TicksPerSecond;
            double wallSec = wallMs / 1000.0;
            int    cores   = Math.Max(1, Environment.ProcessorCount);
            double cpuPct  = wallSec > 0
                ? 100.0 * (cpuMs / 1000.0) / (wallSec * cores)
                : 0;
            cpuPct = Math.Clamp(cpuPct, 0, 100);

            var summary = new ResourceOpSummary
            {
                WallMs               = wallMs,
                CpuMs                = cpuMs,
                CpuPct               = cpuPct,
                PeakWorkingSetBytes  = peakWs,
                FinalWorkingSetBytes = curWs,
            };

            if (emitBench)
            {
                try
                {
                    // One key per line — matches the BuildProfile.EmitBench
                    // pattern so the same BENCH-parsing grep picks these up.
                    Library.Log($"BENCH {opName}_wall_ms={wallMs:F1}");
                    Library.Log($"BENCH {opName}_cpu_ms={cpuMs:F1}");
                    Library.Log($"BENCH {opName}_cpu_pct={cpuPct:F1}");
                    Library.Log($"BENCH {opName}_peak_ws_bytes={peakWs}");
                }
                catch { /* never let logging kill an op */ }
            }

            _currentOpName = null;
            _lastSummaries[opName] = summary;

            // B6 — working-set trim hint. Asking Windows to return pages
            // back to the OS after a heavy op is cheap and advisory; at
            // worst the OS ignores us.
            TrimWorkingSet();

            return summary;
        }
    }

    /// <summary>
    /// Hint Windows to trim the process working set.
    /// Advisory — OS is free to ignore. Swallows all exceptions;
    /// logging failures would trade one "noisy op" for another.
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            SetProcessWorkingSetSize(p.Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch { /* advisory only */ }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(
        IntPtr hProcess, IntPtr dwMin, IntPtr dwMax);
}
