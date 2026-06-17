// BuildSubprocess.cs — Reusable launcher for the out-of-process voxel
// build + STL export path. Wraps Process.Start of
// `Voxelforge.StlExporter.exe` with a Windows Job Object
// memory cap + optional priority demote + structured exit-code
// translation (STATUS_QUOTA_EXCEEDED on memory cap breach → exit code
// 12 with a clean user-actionable message).
//
// Why this exists
// ───────────────
// Program.RunSubprocessExportAsync used to inline this logic. Lifting
// it to a library-level helper:
//   1. Future UI wiring can route the main-thread Generate() through
//      a subprocess when the memory pre-flight projects Fail — closes
//      the last hard-crash vector where actual allocation exceeds the
//      pre-flight estimate (PicoGK's OpenVDB tree build can overshoot
//      the cube-root projection by 2-3× for pathological channel
//      topologies). Pre-flight is conservative on average but not
//      correct on every input.
//   2. Tests can exercise argument generation + exit-code translation
//      without spawning a real subprocess, catching regressions in
//      the memory-cap handling logic.
//   3. The StlExporter.exe process already does the FULL build-to-STL
//      pipeline (runs RegenChamberOptimization.GenerateWith and writes
//      STL) — so "build subprocess" and "STL export subprocess" are
//      the same binary with the same CLI. A1 is really "package the
//      existing pattern for reuse," not "write a new subprocess."
//
// Non-goals
// ─────────
// This module does NOT wire the subprocess into Program.RegenerateForManualMode
// yet. That's a separate UI-dispatch sprint because swapping the in-
// process Voxels handle for an STL-only subprocess result changes the
// viewer-update path (today the viewer renders live Voxels; after UI
// wiring it would render a Mesh loaded from the subprocess-produced
// STL). The SessionSettings.IsolateLargeBuildsAtFailProjection flag
// introduced alongside this module is a scaffold for that future work.

using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Voxelforge.Windows;

namespace Voxelforge.Geometry;

/// <summary>
/// Input bundle for <see cref="BuildSubprocess.Run"/>. All paths must
/// exist / be writable respectively before the call — validation
/// happens in the ctor so callers get immediate feedback.
/// </summary>
public sealed record BuildSubprocessRequest
{
    public string DesignJsonPath     { get; init; } = "";
    public string OutStlPath         { get; init; } = "";
    public string StlExporterExePath { get; init; } = "";
    public double VoxelSize_mm       { get; init; } = 0.4;
    public ulong  MemoryCapBytes     { get; init; } = 0;
    public bool   DemotePriority     { get; init; } = false;
    public int    TimeoutMs          { get; init; } = 0;  // 0 = no timeout
    // Sprint 28 (2026-04-23) — route through MonolithicEngineBuilder.BuildFromDesign
    // so the produced STL fuses chamber + turbopump + feed manifold + preburner
    // into one voxel body. False (default) retains the per-topology single-body
    // behaviour (bell via ChamberVoxelBuilder, aerospike via AerospikeBuilder).
    public bool   Monolithic         { get; init; } = false;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DesignJsonPath))
            throw new ArgumentException("DesignJsonPath required", nameof(DesignJsonPath));
        if (string.IsNullOrWhiteSpace(OutStlPath))
            throw new ArgumentException("OutStlPath required", nameof(OutStlPath));
        if (string.IsNullOrWhiteSpace(StlExporterExePath))
            throw new ArgumentException("StlExporterExePath required", nameof(StlExporterExePath));
        if (VoxelSize_mm < 0.05 || VoxelSize_mm > 2.0)
            throw new ArgumentOutOfRangeException(nameof(VoxelSize_mm),
                $"Voxel size {VoxelSize_mm:F3} mm out of supported range 0.05–2.0 mm.");
    }

    /// <summary>
    /// Build the command-line argument string the subprocess expects.
    /// Exposed public + static for testability + diagnostic logging —
    /// the exit-code translation can be unit-tested without a real
    /// process, and operators can paste this string into a shell to
    /// reproduce a subprocess invocation. The actual launch path uses
    /// <see cref="BuildArgumentList"/> instead, which avoids the
    /// double-quote-escaping ambiguity of a single argument string.
    /// </summary>
    public string BuildArguments()
    {
        Validate();
        string args = $"--design \"{DesignJsonPath}\" "
                    + $"--voxel {VoxelSize_mm.ToString("F4", CultureInfo.InvariantCulture)} "
                    + $"--out \"{OutStlPath}\"";
        if (Monolithic) args += " --monolithic";
        return args;
    }

    /// <summary>
    /// Build the argument list (one element per token) the subprocess
    /// expects. Used by <see cref="BuildSubprocess.Run"/> via
    /// <see cref="ProcessStartInfo.ArgumentList"/>, which performs
    /// per-platform argument escaping internally — avoids the audit
    /// 01-security L3 concern about embedded quote/whitespace in path
    /// values when the .NET runtime concatenates a single Arguments
    /// string before handing it to the OS.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<string> BuildArgumentList()
    {
        Validate();
        var list = new System.Collections.Generic.List<string>(7)
        {
            "--design", DesignJsonPath,
            "--voxel", VoxelSize_mm.ToString("F4", CultureInfo.InvariantCulture),
            "--out", OutStlPath,
        };
        if (Monolithic) list.Add("--monolithic");
        return list;
    }
}

/// <summary>Structured outcome of a <see cref="BuildSubprocess.Run"/> call.</summary>
public sealed record BuildSubprocessResult
{
    /// <summary>Raw OS exit code from the subprocess.</summary>
    public int     ExitCode        { get; init; }

    /// <summary>Subprocess stdout (complete).</summary>
    public string  Stdout          { get; init; } = "";

    /// <summary>Subprocess stderr (complete).</summary>
    public string  Stderr          { get; init; } = "";

    /// <summary>Total wall-clock time from launch to exit.</summary>
    public double  WallClockMs     { get; init; }

    /// <summary>
    /// True when the subprocess was killed by the Windows Job Object
    /// for exceeding the memory cap. Caller should surface a message
    /// like "coarsen voxel or raise the Resource Budget cap."
    /// </summary>
    public bool    MemoryCapExceeded { get; init; }

    /// <summary>True on success (exit 0 + STL file written).</summary>
    public bool    Success         { get; init; }

    /// <summary>Parsed BENCH grid_build_total_ms (0 if absent).</summary>
    public double  GridBuildMs     { get; init; }

    /// <summary>Parsed BENCH export_meshing_ms (0 if absent).</summary>
    public double  MeshingMs       { get; init; }

    /// <summary>Parsed BENCH export_stl_write_ms (0 if absent).</summary>
    public double  WriteMs         { get; init; }

    /// <summary>Parsed BENCH triangle_count (0 if absent).</summary>
    public long    TriangleCount   { get; init; }

    /// <summary>
    /// True when a Job Object was successfully attached to the
    /// subprocess. False when memory-cap was requested but OS API
    /// failure prevented binding; the subprocess ran anyway, but
    /// without memory protection.
    /// </summary>
    public bool    JobObjectAttached { get; init; }
}

/// <summary>
/// Library-level launcher for the voxel-build subprocess. Synchronous;
/// blocks until the child exits. For non-blocking use, wrap the call
/// in <c>Task.Run</c> (the existing
/// <c>Program.RunSubprocessExportAsync</c> in the main app does this).
/// </summary>
public static class BuildSubprocess
{
    /// <summary>
    /// Exit code mapped from Windows STATUS_QUOTA_EXCEEDED (0xC0000044)
    /// when the Job Object kills the subprocess for exceeding the
    /// memory cap. Visible to callers so they can route UI messaging.
    /// </summary>
    public const int ExitCodeMemoryCapExceeded = 12;

    /// <summary>
    /// Launch the subprocess synchronously and return a structured
    /// result. Throws <see cref="System.IO.FileNotFoundException"/>
    /// if the exporter exe is missing; all other runtime errors are
    /// surfaced via <see cref="BuildSubprocessResult.Success"/>.
    /// </summary>
    public static BuildSubprocessResult Run(BuildSubprocessRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        request.Validate();
        if (!File.Exists(request.StlExporterExePath))
            throw new FileNotFoundException(
                $"Subprocess exe not found: {request.StlExporterExePath}",
                request.StlExporterExePath);

        JobObject? job = null;
        bool jobAttached = false;
        if (request.MemoryCapBytes > 0)
        {
            try { job = new JobObject(request.MemoryCapBytes); }
            catch { /* log + continue without cap — better to run than refuse */ }
        }

        long t0 = Stopwatch.GetTimestamp();
        try
        {
            var psi = new ProcessStartInfo(request.StlExporterExePath)
            {
                UseShellExecute        = false,
                RedirectStandardError  = true,
                RedirectStandardOutput = true,
                CreateNoWindow         = true,
            };
            foreach (var arg in request.BuildArgumentList())
                psi.ArgumentList.Add(arg);
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return new BuildSubprocessResult
                {
                    ExitCode    = -1,
                    Stderr      = "Process.Start returned null.",
                    Success     = false,
                    WallClockMs = 0,
                };
            }

            if (job is not null)
            {
                try { jobAttached = job.AssignProcess(proc); }
                catch { jobAttached = false; }
            }

            if (request.DemotePriority)
            {
                try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; }
                catch { /* non-fatal */ }
            }

            // Drain stdout/stderr on background tasks BEFORE WaitForExit.
            // Subprocess deadlock pattern: child writes > 64 KB to a pipe
            // (OS buffer limit), blocks on write; parent blocks on
            // WaitForExit; neither makes progress. Canonical fix is to
            // keep both streams draining concurrently so buffers empty as
            // they fill. Near-memory-cap exports emit long diagnostics on
            // stderr that can plausibly exceed 64 KB, so this is a real
            // failure mode for the monolithic-engine subprocess path.
            var stdoutTask = Task.Run(() => proc.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => proc.StandardError.ReadToEnd());

            bool completed = request.TimeoutMs > 0
                ? proc.WaitForExit(request.TimeoutMs)
                : TrueAfterWait(proc);

            if (!completed)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                // Give the drain tasks a bounded window to observe EOF
                // after the kill; bail out even if they're stuck so a
                // broken child cannot hang the parent indefinitely.
                try { Task.WaitAll(new[] { stdoutTask, stderrTask }, 2000); } catch { }
                long t1k = Stopwatch.GetTimestamp();
                return new BuildSubprocessResult
                {
                    ExitCode          = -2,
                    Stderr            = $"Subprocess exceeded {request.TimeoutMs} ms timeout.",
                    Success           = false,
                    WallClockMs       = (t1k - t0) / (double)Stopwatch.Frequency * 1000.0,
                    JobObjectAttached = jobAttached,
                };
            }

            // WaitForExit returned true → child closed its pipes → the
            // drain tasks will observe EOF and complete. GetResult blocks
            // the tiny window until they do; guarded against rare
            // Dispose-race IOException on the stream readers.
            string stdout = "";
            string stderr = "";
            try { stdout = stdoutTask.GetAwaiter().GetResult(); } catch { }
            try { stderr = stderrTask.GetAwaiter().GetResult(); } catch { }
            int    exit   = proc.ExitCode;
            long   t1     = Stopwatch.GetTimestamp();
            double wallMs = (t1 - t0) / (double)Stopwatch.Frequency * 1000.0;

            bool capped = IsMemoryCapExitCode(exit, stderr, request.MemoryCapBytes,
                                              elapsedMs: wallMs);
            if (capped) exit = ExitCodeMemoryCapExceeded;

            return new BuildSubprocessResult
            {
                ExitCode          = exit,
                Stdout            = stdout,
                Stderr            = stderr,
                WallClockMs       = wallMs,
                MemoryCapExceeded = capped,
                Success           = !capped && exit == 0 && File.Exists(request.OutStlPath),
                GridBuildMs       = ParseBenchMs(stdout, "grid_build_total_ms"),
                MeshingMs         = ParseBenchMs(stdout, "export_meshing_ms"),
                WriteMs           = ParseBenchMs(stdout, "export_stl_write_ms"),
                TriangleCount     = ParseBenchLong(stdout, "triangle_count"),
                JobObjectAttached = jobAttached,
            };
        }
        finally
        {
            try { job?.Dispose(); } catch { }
        }
    }

    private static bool TrueAfterWait(Process p) { p.WaitForExit(); return true; }

    /// <summary>
    /// Heuristic: a non-zero exit code with empty stderr and a
    /// non-zero memory cap almost always means the Job Object killed
    /// the process for STATUS_QUOTA_EXCEEDED. The exit code is the
    /// NT status cast to int (0xC0000044 = -1073741756). Exposed
    /// internal so tests can drive it directly.
    ///
    /// L6 (post-Phase-6 logical-error audit): the empty-stderr fallback
    /// also fires on early startup crashes (DLL load failure, missing
    /// runtime) that exit sub-second before the child can write stderr.
    /// Misdiagnosing those as memory-cap misdirects debugging. When
    /// <paramref name="elapsedMs"/> is &gt;= 0 and below
    /// <see cref="StartupCrashThresholdMs"/>, the empty-stderr fallback
    /// is suppressed. <paramref name="elapsedMs"/> &lt; 0 (default)
    /// preserves legacy behaviour for existing test fixtures.
    /// </summary>
    internal const int StartupCrashThresholdMs = 500;

    internal static bool IsMemoryCapExitCode(int exitCode, string stderr, ulong memoryCapBytes,
                                             double elapsedMs = -1)
    {
        if (memoryCapBytes == 0) return false;
        if (exitCode == 0)       return false;
        // STATUS_QUOTA_EXCEEDED is the canonical signal.
        if (unchecked((uint)exitCode) == 0xC0000044u) return true;
        // Fallback: the subprocess was killed without getting a chance
        // to write stderr. Treat as cap breach UNLESS the wall-clock is
        // below the startup-crash threshold, which strongly suggests a
        // launcher-level failure rather than a memory-driven kill.
        if (elapsedMs >= 0 && elapsedMs < StartupCrashThresholdMs) return false;
        return string.IsNullOrWhiteSpace(stderr);
    }

    /// <summary>
    /// Pull a single "BENCH key=<float>" line out of a stdout blob.
    /// Returns 0 when absent or non-numeric. Exposed internal for tests.
    /// </summary>
    internal static double ParseBenchMs(string stdout, string key)
    {
        string needle = $"BENCH {key}=";
        foreach (var line in stdout.Split('\n'))
        {
            int i = line.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) continue;
            int start = i + needle.Length;
            int end = start;
            while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '.' || line[end] == '-'))
                end++;
            if (end > start && double.TryParse(line.AsSpan(start, end - start),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
        }
        return 0;
    }

    internal static long ParseBenchLong(string stdout, string key)
    {
        string needle = $"BENCH {key}=";
        foreach (var line in stdout.Split('\n'))
        {
            int i = line.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) continue;
            int start = i + needle.Length;
            int end = start;
            while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '-'))
                end++;
            if (end > start && long.TryParse(line.AsSpan(start, end - start),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
                return v;
        }
        return 0;
    }
}
