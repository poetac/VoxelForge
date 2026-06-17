// Su2CfdRunner.cs — Launches SU2_CFD via System.Diagnostics.Process with async
// stdout/stderr capture and convergence detection.
//
// SU2_CFD is located via Su2Locator (SU2_RUN env var → PATH). When SU2 is absent,
// FindSu2Cfd() returns null and the caller receives an InvalidOperationException.
//
// Convergence is assessed by tracking rms[Rho] residuals (log₁₀) in the SU2 stdout
// output. A drop of ≥ 6 orders of magnitude from the first reported residual value
// is considered converged, consistent with CONV_RESIDUAL_MINVAL= -6 in the config.
//
// Deadlock prevention: both stdout and stderr are asynchronously drained via
// BeginOutputReadLine / BeginErrorReadLine with a lock on the StringBuilder to
// prevent the process from blocking when both pipes fill simultaneously.

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Voxelforge.Cfd.Su2;

namespace Voxelforge.Cfd.Runner;

/// <summary>Result of a SU2_CFD subprocess invocation.</summary>
public sealed record Su2RunResult(
    /// <summary>True when SU2 exited with code 0 and residuals dropped ≥ 6 orders.</summary>
    bool Converged,
    /// <summary>Final log₁₀(rms[Rho]) from stdout (NaN when not parseable).</summary>
    double FinalResidualLog10,
    /// <summary>Initial log₁₀(rms[Rho]) from stdout (NaN when not parseable).</summary>
    double InitialResidualLog10,
    /// <summary>InitialResidualLog10 − FinalResidualLog10 (NaN when not parseable).</summary>
    double ResidualDrop,
    /// <summary>Wall-clock time of the SU2 run.</summary>
    TimeSpan WallTime,
    /// <summary>Full captured stdout.</summary>
    string StdOut,
    /// <summary>Full captured stderr.</summary>
    string StdErr,
    /// <summary>SU2_CFD process exit code (non-zero indicates failure).</summary>
    int ExitCode);

/// <summary>
/// Launches SU2_CFD as a subprocess and captures stdout/stderr with convergence detection.
/// </summary>
public static class Su2CfdRunner
{
    // Matches the numeric residual column in SU2 convergence output lines.
    // SU2 v8 prints something like:  "100  5.23e+00  3.14e-03  -4.12 ..."
    // The third column is rms[Rho] in log₁₀. We capture the first signed float
    // after the iteration counter and CFL columns.
    private static readonly Regex ResidualRegex = new(
        @"^\s*\d+[,\s]+[-+]?[\d.eE]+[,\s]+[-+]?[\d.eE]+[,\s]+([-+]?[\d.]+(?:[eE][-+]?\d+)?)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Runs SU2_CFD with the given config and returns a structured result.
    /// </summary>
    /// <param name="configPath">Path to the .cfg file (must exist).</param>
    /// <param name="workDirectory">Working directory for SU2 output files (must exist).</param>
    /// <param name="timeout">Maximum time to allow SU2 to run before killing it.</param>
    /// <param name="su2Executable">
    /// Optional explicit path to SU2_CFD. When null, <see cref="Su2Locator.FindSu2Cfd"/> is used.
    /// </param>
    /// <exception cref="InvalidOperationException">SU2_CFD binary not found.</exception>
    /// <exception cref="FileNotFoundException"><paramref name="configPath"/> does not exist.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="workDirectory"/> does not exist.</exception>
    public static Su2RunResult Run(
        string configPath,
        string workDirectory,
        TimeSpan timeout,
        string? su2Executable = null)
    {
        ArgumentNullException.ThrowIfNull(configPath);
        ArgumentNullException.ThrowIfNull(workDirectory);

        if (!File.Exists(configPath))
            throw new FileNotFoundException("SU2 config file not found.", configPath);

        if (!Directory.Exists(workDirectory))
            throw new DirectoryNotFoundException($"SU2 work directory not found: {workDirectory}");

        string exe = su2Executable
            ?? Su2Locator.FindSu2Cfd()
            ?? throw new InvalidOperationException(
                "SU2_CFD not found. Install SU2 v8 and set the SU2_RUN environment variable " +
                "to the directory containing SU2_CFD.exe (or SU2_CFD on Linux/macOS).");

        var stdoutSb = new StringBuilder();
        var stderrSb = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        // Use ArgumentList instead of Arguments string so the .NET
        // runtime performs per-platform argument escaping internally
        // (audit 01-security L3). configPath may contain spaces or
        // other characters that single-quote-string concatenation
        // would mis-handle if SU2_CFD ever parses argv string-style.
        var psi = new ProcessStartInfo
        {
            FileName               = exe,
            WorkingDirectory       = workDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        psi.ArgumentList.Add(configPath);

        using var proc = new Process { StartInfo = psi };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (stdoutSb) stdoutSb.AppendLine(e.Data);
        };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (stderrSb) stderrSb.AppendLine(e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        bool exited = proc.WaitForExit((int)timeout.TotalMilliseconds);

        if (!exited)
        {
            try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }

        // WaitForExit() with a timeout does not guarantee async pipe draining is complete.
        // Call WaitForExit() a second time (without timeout) after the process exits so the
        // async DataReceived events can flush their last buffered lines.
        proc.WaitForExit();

        stopwatch.Stop();

        string stdout = stdoutSb.ToString();
        string stderr = stderrSb.ToString();
        int exitCode  = exited ? proc.ExitCode : -1;

        // Parse log₁₀ rms[Rho] residuals from stdout
        (double initRes, double finalRes) = ParseResiduals(stdout);

        double drop      = double.IsNaN(initRes) || double.IsNaN(finalRes)
            ? double.NaN
            : initRes - finalRes;

        bool converged   = exitCode == 0
            && !double.IsNaN(drop)
            && drop >= 6.0;

        return new Su2RunResult(
            Converged:            converged,
            FinalResidualLog10:   finalRes,
            InitialResidualLog10: initRes,
            ResidualDrop:         drop,
            WallTime:             stopwatch.Elapsed,
            StdOut:               stdout,
            StdErr:               stderr,
            ExitCode:             exitCode);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static (double init, double final) ParseResiduals(string stdout)
    {
        double init  = double.NaN;
        double final = double.NaN;

        foreach (Match m in ResidualRegex.Matches(stdout))
        {
            if (double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double val))
            {
                if (double.IsNaN(init)) init = val;
                final = val;
            }
        }

        return (init, final);
    }
}
