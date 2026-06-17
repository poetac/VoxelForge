// Su2CfdRunnerTests.cs — direct unit tests for the SU2_CFD subprocess
// wrapper. Per audit 05-test-gaps.md §6 the runner was previously
// exercised only end-to-end via CfdCalibrationRunner + the
// Smoke-tagged CfdSmokeTests (which require an SU2 binary).
//
// These tests are CI-safe: they cover the failure-mode contract
// (FileNotFound / DirectoryNotFound / null-arg) plus the Su2RunResult
// record's ctor and equality semantics. None of them invoke SU2 itself.

using System;
using System.IO;
using Voxelforge.Cfd.Runner;
using Xunit;

namespace Voxelforge.Cfd.Tests.Runner;

public sealed class Su2CfdRunnerTests
{
    // ── Argument validation ────────────────────────────────────────────

    [Fact]
    public void Run_NullConfigPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Su2CfdRunner.Run(
                configPath:    null!,
                workDirectory: Path.GetTempPath(),
                timeout:       TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Run_NullWorkDirectory_ThrowsArgumentNullException()
    {
        // Use a real file so the configPath existence check passes,
        // proving null-workDirectory is what surfaces.
        string tmpCfg = Path.GetTempFileName();
        try
        {
            Assert.Throws<ArgumentNullException>(() =>
                Su2CfdRunner.Run(
                    configPath:    tmpCfg,
                    workDirectory: null!,
                    timeout:       TimeSpan.FromSeconds(1)));
        }
        finally
        {
            File.Delete(tmpCfg);
        }
    }

    // ── File / directory contract ──────────────────────────────────────

    [Fact]
    public void Run_NonExistentConfigPath_ThrowsFileNotFoundException()
    {
        string missingCfg = Path.Combine(Path.GetTempPath(),
            "vf_test_missing_" + Guid.NewGuid().ToString("N") + ".cfg");
        Assert.Throws<FileNotFoundException>(() =>
            Su2CfdRunner.Run(
                configPath:    missingCfg,
                workDirectory: Path.GetTempPath(),
                timeout:       TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Run_NonExistentWorkDirectory_ThrowsDirectoryNotFoundException()
    {
        string tmpCfg = Path.GetTempFileName();
        try
        {
            string missingDir = Path.Combine(Path.GetTempPath(),
                "vf_test_missing_dir_" + Guid.NewGuid().ToString("N"));
            Assert.Throws<DirectoryNotFoundException>(() =>
                Su2CfdRunner.Run(
                    configPath:    tmpCfg,
                    workDirectory: missingDir,
                    timeout:       TimeSpan.FromSeconds(1)));
        }
        finally
        {
            File.Delete(tmpCfg);
        }
    }

    // ── SU2 binary missing path ────────────────────────────────────────

    [Fact]
    public void Run_ExplicitSu2Path_NonExistentBinary_StillReportsDeterministically()
    {
        // When a user supplies an explicit `su2Executable` path that does
        // not exist on disk, the runner's pre-flight argument validation
        // passes (we don't `File.Exists` the exe — Process.Start would).
        // Process.Start surfaces this as a System.ComponentModel.Win32Exception
        // on Windows / Linux. The runner must NOT silently swallow it.
        // The relevant contract here is "throws something"; capture the
        // exception and assert it's not null + not OOM/etc.
        string tmpCfg = Path.GetTempFileName();
        string tmpDir = Path.GetTempPath();
        try
        {
            string bogusExe = Path.Combine(Path.GetTempPath(),
                "vf_test_bogus_su2_" + Guid.NewGuid().ToString("N") + ".exe");
            var ex = Record.Exception(() =>
                Su2CfdRunner.Run(
                    configPath:    tmpCfg,
                    workDirectory: tmpDir,
                    timeout:       TimeSpan.FromSeconds(1),
                    su2Executable: bogusExe));
            Assert.NotNull(ex);
            // Specifically should not be OutOfMemoryException / NRE.
            Assert.IsNotType<OutOfMemoryException>(ex);
            Assert.IsNotType<NullReferenceException>(ex);
        }
        finally
        {
            File.Delete(tmpCfg);
        }
    }

    // ── Su2RunResult record ────────────────────────────────────────────

    [Fact]
    public void Su2RunResult_Ctor_StoresAllFields()
    {
        var wall = TimeSpan.FromSeconds(42);
        var r = new Su2RunResult(
            Converged:            true,
            FinalResidualLog10:   -8.5,
            InitialResidualLog10: -1.2,
            ResidualDrop:         7.3,
            WallTime:             wall,
            StdOut:               "stdout-text",
            StdErr:               "stderr-text",
            ExitCode:             0);
        Assert.True(r.Converged);
        Assert.Equal(-8.5,         r.FinalResidualLog10);
        Assert.Equal(-1.2,         r.InitialResidualLog10);
        Assert.Equal( 7.3,         r.ResidualDrop);
        Assert.Equal(wall,         r.WallTime);
        Assert.Equal("stdout-text", r.StdOut);
        Assert.Equal("stderr-text", r.StdErr);
        Assert.Equal(0,            r.ExitCode);
    }

    [Fact]
    public void Su2RunResult_RecordEquality_ComparesByValue()
    {
        var a = new Su2RunResult(true, -8, -1, 7, TimeSpan.FromSeconds(1), "o", "e", 0);
        var b = new Su2RunResult(true, -8, -1, 7, TimeSpan.FromSeconds(1), "o", "e", 0);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Su2RunResult_RecordEquality_DifferentExitCode_NotEqual()
    {
        var a = new Su2RunResult(true, -8, -1, 7, TimeSpan.FromSeconds(1), "o", "e", 0);
        var b = new Su2RunResult(true, -8, -1, 7, TimeSpan.FromSeconds(1), "o", "e", 1);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Su2RunResult_WithExpression_OverridesConverged()
    {
        var baseR = new Su2RunResult(true, -8, -1, 7, TimeSpan.Zero, "", "", 0);
        var diverged = baseR with { Converged = false, ExitCode = 1, ResidualDrop = 2.0 };
        Assert.False(diverged.Converged);
        Assert.Equal(1,   diverged.ExitCode);
        Assert.Equal(2.0, diverged.ResidualDrop);
        // Other fields propagate.
        Assert.Equal(-8.0, diverged.FinalResidualLog10);
    }
}
