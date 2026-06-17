// tech-debt T12 (2026-04-28): centralized subprocess launching for
// xUnit-safe out-of-process tests (xUnit + PicoGK pitfall #8).
//
// All test-suite subprocess paths share the same shape:
//   1. Locate the exe under the repo's voxelforge.sln anchor.
//   2. Spawn with redirected stdin/stdout/stderr, no shell, no window.
//   3. Wait with a hard timeout; kill on overshoot.
//   4. Surface a structured SubprocessResult with diagnostic flags.
//
// Centralizing this here means individual tests don't have to reinvent
// the discovery walk or the "exited / timed out / never spawned" logic.

using System.Diagnostics;
using System.IO;
using System.Text;

namespace Voxelforge.Tests.Helpers;

/// <summary>
/// Discovers test-built executables and runs them in xUnit-safe
/// subprocess mode. Helpers cover both the "find under the repo's
/// voxelforge.sln anchor" pattern (BenchSADeterminismTests) and the
/// "find under the main app's bin dir" pattern (VoxelforgeEvalSubprocessTests).
/// </summary>
public static class SubprocessRunner
{
    /// <summary>
    /// Walk parent directories from <paramref name="baseDir"/> upward
    /// until a directory containing <c>voxelforge.sln</c> is found,
    /// then resolve <paramref name="relativeFromRepoRoot"/> beneath
    /// that directory.
    /// </summary>
    /// <param name="relativeFromRepoRoot">
    /// Path components relative to the repo root, e.g.
    /// <c>"Voxelforge.Benchmarks/bin/Release/net9.0-windows/Voxelforge.Benchmarks.exe"</c>.
    /// Forward or back slashes both work.
    /// </param>
    /// <param name="baseDir">Optional starting directory (defaults to <see cref="AppContext.BaseDirectory"/>).</param>
    /// <returns>The absolute path to the resolved exe. Existence is NOT checked here — call <see cref="Run"/> or <see cref="ProbeExe"/>.</returns>
    /// <exception cref="InvalidOperationException">If no ancestor contains <c>voxelforge.sln</c>.</exception>
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

        // Normalize separators so callers can pass either flavour.
        var parts = relativeFromRepoRoot.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Path.GetFullPath(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray()));
    }

    /// <summary>
    /// Probe an exe path that may not exist yet (e.g. when only a
    /// single project was built). Returns a sentinel result with
    /// <c>ExeExists = false</c> so callers can `return` cleanly without
    /// failing the test.
    /// </summary>
    public static SubprocessResult ProbeExe(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("Exe path must be non-empty.", nameof(exePath));
        return new SubprocessResult(
            ExePath: exePath,
            ExeExists: File.Exists(exePath),
            ExitCode: -1,
            Stdout: string.Empty,
            Stderr: string.Empty,
            WaitTimedOut: false,
            Elapsed: TimeSpan.Zero);
    }

    /// <summary>
    /// Spawn an exe with redirected I/O and a wait timeout. Returns a
    /// <see cref="SubprocessResult"/> regardless of success — callers
    /// inspect <c>Succeeded</c> / <c>WaitTimedOut</c> / <c>ExeExists</c>
    /// to determine outcome.
    /// </summary>
    /// <param name="exePath">Absolute path to the exe.</param>
    /// <param name="args">Command-line arguments (passed via <c>ArgumentList</c> to avoid shell quoting issues).</param>
    /// <param name="stdin">Optional input piped to the exe's stdin. <c>null</c> = no input.</param>
    /// <param name="timeoutMs">Hard timeout in milliseconds. Process is killed on overshoot.</param>
    /// <param name="workingDirectory">Optional working dir (defaults to the exe's own directory).</param>
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
                ExePath: exePath,
                ExeExists: false,
                ExitCode: -1,
                Stdout: string.Empty,
                Stderr: string.Empty,
                WaitTimedOut: false,
                Elapsed: TimeSpan.Zero);
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(exePath)!,
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
        // Per Process.WaitForExit docs: when stdout/stderr are redirected
        // to async handlers, the no-arg WaitForExit() must be called AFTER
        // the timed overload to guarantee the async readers have drained.
        // Without this, JsonDocument.Parse on result.Stdout can race the
        // last buffered line and see a truncated-mid-token document.
        try { p.WaitForExit(); } catch { /* best-effort */ }
        sw.Stop();

        return new SubprocessResult(
            ExePath: exePath,
            ExeExists: true,
            ExitCode: exitedInTime ? p.ExitCode : -1,
            Stdout: stdoutSb.ToString(),
            Stderr: stderrSb.ToString(),
            WaitTimedOut: !exitedInTime,
            Elapsed: sw.Elapsed);
    }
}
