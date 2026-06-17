// DUPLICATED — unify in post-Wave-1 wrap-up.
//
// Lifted verbatim from Voxelforge.Airbreathing.Tests/Helpers/SubprocessRunner.cs
// per the parallel-pillar policy in ADR-026 §2. Rule of three: rocket +
// airbreathing + electric-propulsion all consume sub-process tests; the
// right home is a future Voxelforge.Tests.Common shared project.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Voxelforge.ElectricPropulsion.Tests.Helpers;

/// <summary>
/// Discovers test-built executables and runs them in xUnit-safe
/// subprocess mode.
/// </summary>
public static class SubprocessRunner
{
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

    public static SubprocessResult Run(
        string exePath,
        IEnumerable<string>? args = null,
        string? stdin = null,
        int timeoutMs = 60_000,
        string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("Exe path must be non-empty.", nameof(exePath));
        if (timeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), "Must be positive.");

        if (!File.Exists(exePath))
        {
            return new SubprocessResult(
                ExePath: exePath, ExeExists: false, ExitCode: -1,
                Stdout: string.Empty, Stderr: string.Empty,
                WaitTimedOut: false, Elapsed: TimeSpan.Zero);
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
            try { p.Kill(entireProcessTree: true); } catch { }
        }
        try { p.WaitForExit(); } catch { }
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
