// tech-debt T12 (2026-04-28): structured result for subprocess-spawning tests.
//
// The legacy pattern was a tuple `(int exitCode, string stdout, string stderr)`
// plus brittle inline assertions. Failures fell into one of three categories
// that the old shape couldn't distinguish:
//
//   • exe not found       → 30 s WaitForExit timeout with no signal
//   • semantic failure    → exitCode != 0 with stderr preserved
//   • parse failure       → stdout that didn't match the expected schema
//
// `SubprocessResult` carries enough context (`ExeExists`, `WaitTimedOut`,
// optional `ParseError`) that a test failure message can name the actual
// fault instead of a generic "did not exit" string.

using System.Diagnostics;

namespace Voxelforge.Tests.Helpers;

/// <summary>
/// Result of running an exe via <see cref="SubprocessRunner.Run"/>.
/// Carries the exit code, stdout/stderr, plus diagnostic flags that
/// distinguish "exe missing" from "exe ran but failed" from "exe hung."
/// </summary>
/// <param name="ExePath">The exe path that was launched (or the expected path if <see cref="ExeExists"/> is false).</param>
/// <param name="ExeExists">Whether the exe was present on disk before the spawn attempt.</param>
/// <param name="ExitCode">The process exit code; meaningful only when <see cref="WaitTimedOut"/> is false.</param>
/// <param name="Stdout">Captured standard output. Always populated, may be empty.</param>
/// <param name="Stderr">Captured standard error. Always populated, may be empty.</param>
/// <param name="WaitTimedOut">True iff the process did not exit before the timeout fired (the runner kills it in that case).</param>
/// <param name="Elapsed">Wall-clock duration from spawn to wait-completion.</param>
public sealed record SubprocessResult(
    string ExePath,
    bool ExeExists,
    int ExitCode,
    string Stdout,
    string Stderr,
    bool WaitTimedOut,
    TimeSpan Elapsed)
{
    /// <summary>True iff the exe ran to completion with exit code 0.</summary>
    public bool Succeeded => ExeExists && !WaitTimedOut && ExitCode == 0;

    /// <summary>
    /// Human-readable failure description suitable for an Assert message.
    /// Distinguishes the three failure modes (exe-not-found, timeout,
    /// non-zero exit) and surfaces stdout/stderr where available.
    /// </summary>
    public string DescribeFailure()
    {
        if (!ExeExists)
            return $"Exe not found at '{ExePath}'. Did the project build?";
        if (WaitTimedOut)
            return $"Exe '{ExePath}' did not exit within the timeout ({Elapsed.TotalSeconds:F1} s).\n"
                 + $"stdout (partial):\n{Truncate(Stdout)}\n"
                 + $"stderr (partial):\n{Truncate(Stderr)}";
        return $"Exe '{ExePath}' exited with code {ExitCode} (wall {Elapsed.TotalSeconds:F2} s).\n"
             + $"stdout:\n{Truncate(Stdout)}\n"
             + $"stderr:\n{Truncate(Stderr)}";
    }

    private static string Truncate(string s, int max = 4000)
        => s.Length <= max
            ? s
            : string.Concat(s.AsSpan(0, max), $"\n…(truncated, {s.Length - max} more chars)");
}
