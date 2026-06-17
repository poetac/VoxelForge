// DUPLICATED — unify in Step-1 wrap-up.
//
// Lifted from Voxelforge.Tests/Helpers/SubprocessRunner.cs per
// the parallel-pillar policy. Cross-pillar
// reuse would require a Voxelforge.Tests.Common shared project; deferred
// until the rule-of-three trigger fires (rocket + ramjet + turbojet
// + (eventually) marine all consume sub-process tests).

using System.Diagnostics;
using System.IO;
using System.Text;

namespace Voxelforge.Airbreathing.Tests.Helpers;

/// <summary>
/// Discovers test-built executables and runs them in xUnit-safe
/// subprocess mode. Single helper covers both repo-anchor discovery
/// (walk up to <c>voxelforge.sln</c>) and stdin/stdout piping with a
/// hard timeout.
/// </summary>
public static class SubprocessRunner
{
    /// <summary>
    /// Walk parent directories from <paramref name="baseDir"/> upward
    /// until a directory containing <c>voxelforge.sln</c> is found,
    /// then resolve <paramref name="relativeFromRepoRoot"/> beneath
    /// that directory.
    /// </summary>
    public static string LocateUnderRepo(string relativeFromRepoRoot, string? baseDir = null)
    {
        if (string.IsNullOrWhiteSpace(relativeFromRepoRoot))
            throw new ArgumentException("Relative path must be non-empty.", nameof(relativeFromRepoRoot));

        var dir = new DirectoryInfo(baseDir ?? AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "voxelforge.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException(
                $"Could not locate voxelforge.sln walking up from '{baseDir ?? AppContext.BaseDirectory}'. "
              + "The test must run from a build output under the repo.");

        var parts = relativeFromRepoRoot.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Path.GetFullPath(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray()));
    }

    /// <summary>
    /// Probe an exe path that may not exist yet (e.g. when only a
    /// single project was built). Returns a sentinel result with
    /// <c>ExeExists = false</c> so callers can <c>return</c> cleanly
    /// without failing the test.
    /// </summary>
    public static SubprocessResult ProbeExe(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("Exe path must be non-empty.", nameof(exePath));
        return new SubprocessResult(
            ExePath:      exePath,
            ExeExists:    File.Exists(exePath),
            ExitCode:     -1,
            Stdout:       string.Empty,
            Stderr:       string.Empty,
            WaitTimedOut: false,
            Elapsed:      TimeSpan.Zero);
    }

    /// <summary>
    /// Spawn an exe with redirected I/O and a wait timeout.
    /// </summary>
    public static SubprocessResult Run(
        string exePath,
        IEnumerable<string>? args = null,
        string? stdin = null,
        int timeoutMs = 30_000,
        string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("Exe path must be non-empty.", nameof(exePath));
        if (timeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), "Must be positive.");

        if (!File.Exists(exePath))
        {
            return new SubprocessResult(
                ExePath:      exePath,
                ExeExists:    false,
                ExitCode:     -1,
                Stdout:       string.Empty,
                Stderr:       string.Empty,
                WaitTimedOut: false,
                Elapsed:      TimeSpan.Zero);
        }

        var psi = new ProcessStartInfo
        {
            FileName               = exePath,
            RedirectStandardInput  = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            WorkingDirectory       = workingDirectory ?? Path.GetDirectoryName(exePath)!,
        };
        if (args is not null)
            foreach (var a in args) psi.ArgumentList.Add(a);

        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Process.Start returned null for '{exePath}'");

        if (stdin is not null)
        {
            p.StandardInput.Write(stdin);
            p.StandardInput.Close();
        }

        var stdoutSb = new StringBuilder();
        var stderrSb = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) stdoutSb.AppendLine(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderrSb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        bool exitedInTime = p.WaitForExit(timeoutMs);
        if (!exitedInTime)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* race: process may have just exited */ }
        }
        try { p.WaitForExit(); } catch { /* best-effort drain */ }
        sw.Stop();

        return new SubprocessResult(
            ExePath:      exePath,
            ExeExists:    true,
            ExitCode:     exitedInTime ? p.ExitCode : -1,
            Stdout:       stdoutSb.ToString(),
            Stderr:       stderrSb.ToString(),
            WaitTimedOut: !exitedInTime,
            Elapsed:      sw.Elapsed);
    }
}

/// <summary>
/// Aggregated result of a sub-process run.
/// </summary>
public sealed record SubprocessResult(
    string   ExePath,
    bool     ExeExists,
    int      ExitCode,
    string   Stdout,
    string   Stderr,
    bool     WaitTimedOut,
    TimeSpan Elapsed)
{
    public bool Succeeded => ExeExists && !WaitTimedOut && ExitCode == 0;
}
