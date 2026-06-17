// NoyronTierA1Tests.cs — Tier A1 forcing-function suite. Covers:
//   • BuildSubprocessRequest — Validate() contract; BuildArguments()
//     format + culture-invariant decimal; voxel-range clamp.
//   • BuildSubprocess.IsMemoryCapExitCode — STATUS_QUOTA_EXCEEDED
//     recognition; empty-stderr fallback; memoryCap=0 disables.
//   • BuildSubprocess.ParseBenchMs / ParseBenchLong — happy path,
//     missing key, mid-line needle, malformed values.
//   • BuildSubprocess.Run — FileNotFoundException on missing exe.
//   • SessionSettings.IsolateLargeBuildsAtFailProjection — default
//     false + JSON round-trip coexists with other A1 flags.
//
// Does NOT spawn real subprocesses — that would be flaky and slow;
// the real launcher is exercised by the existing StlExporterCliTests.

using System.Text.Json;
using Voxelforge.Geometry;
using Voxelforge.UI;

namespace Voxelforge.Tests;

public class NoyronTierA1Tests
{
    // ══════════════════════ BuildSubprocessRequest ══════════════════════

    [Fact]
    public void Request_Validate_RejectsEmptyFields()
    {
        Assert.Throws<ArgumentException>(() =>
            new BuildSubprocessRequest { DesignJsonPath = "" }.Validate());
        Assert.Throws<ArgumentException>(() =>
            new BuildSubprocessRequest
            {
                DesignJsonPath = "a.json",
                OutStlPath     = "",
            }.Validate());
        Assert.Throws<ArgumentException>(() =>
            new BuildSubprocessRequest
            {
                DesignJsonPath     = "a.json",
                OutStlPath         = "b.stl",
                StlExporterExePath = "",
            }.Validate());
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.0)]
    [InlineData(3.0)]
    public void Request_Validate_RejectsOutOfRangeVoxel(double voxel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BuildSubprocessRequest
            {
                DesignJsonPath     = "a.json",
                OutStlPath         = "b.stl",
                StlExporterExePath = "c.exe",
                VoxelSize_mm       = voxel,
            }.Validate());
    }

    [Fact]
    public void Request_BuildArguments_FormatsInvariantCulture()
    {
        var req = new BuildSubprocessRequest
        {
            DesignJsonPath     = @"C:\temp\design.json",
            OutStlPath         = @"C:\temp\out.stl",
            StlExporterExePath = "exporter.exe",
            VoxelSize_mm       = 0.15,
        };
        string args = req.BuildArguments();
        // Period decimal, not comma — even under a non-US locale.
        Assert.Contains("--voxel 0.1500", args);
        Assert.Contains("\"C:\\temp\\design.json\"", args);
        Assert.Contains("\"C:\\temp\\out.stl\"", args);
    }

    [Fact]
    public void Request_BuildArguments_RoundTripsValidation()
    {
        // Calling BuildArguments() implicitly validates — no separate
        // Validate() call required. Forcing function: the two must
        // stay in sync.
        var req = new BuildSubprocessRequest();
        Assert.Throws<ArgumentException>(() => req.BuildArguments());
    }

    [Fact]
    public void Request_BuildArguments_MonolithicFlagAppended()
    {
        // Sprint 28 (2026-04-23): --monolithic travels through to the
        // subprocess as a trailing flag. Default (false) emits no flag so
        // older exporter builds continue to accept the args verbatim.
        var regen = new BuildSubprocessRequest
        {
            DesignJsonPath     = @"C:\temp\design.json",
            OutStlPath         = @"C:\temp\out.stl",
            StlExporterExePath = "exporter.exe",
            VoxelSize_mm       = 0.25,
        };
        Assert.DoesNotContain("--monolithic", regen.BuildArguments());

        var mono = regen with { Monolithic = true };
        Assert.Contains("--monolithic", mono.BuildArguments());
    }

    // ══════════════════════ IsMemoryCapExitCode ══════════════════════

    [Fact]
    public void IsMemoryCapExitCode_RecognisesStatusQuotaExceeded()
    {
        // STATUS_QUOTA_EXCEEDED = 0xC0000044 — the canonical signal
        // from the Windows Job Object memory-cap killer.
        int statusQuota = unchecked((int)0xC0000044);
        Assert.True(BuildSubprocess.IsMemoryCapExitCode(
            exitCode: statusQuota, stderr: "", memoryCapBytes: 8L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void IsMemoryCapExitCode_FallsBackOnEmptyStderr()
    {
        // Non-zero exit + empty stderr + non-zero cap = probable cap kill.
        Assert.True(BuildSubprocess.IsMemoryCapExitCode(
            exitCode: -1, stderr: "", memoryCapBytes: 1024));
        // But if stderr has a clean error message, don't misattribute.
        Assert.False(BuildSubprocess.IsMemoryCapExitCode(
            exitCode: 2, stderr: "Design JSON missing.", memoryCapBytes: 1024));
    }

    [Fact]
    public void IsMemoryCapExitCode_DisabledWhenCapIsZero()
    {
        Assert.False(BuildSubprocess.IsMemoryCapExitCode(
            exitCode: -1, stderr: "", memoryCapBytes: 0));
    }

    [Fact]
    public void IsMemoryCapExitCode_ZeroExitNeverMisattributed()
    {
        Assert.False(BuildSubprocess.IsMemoryCapExitCode(
            exitCode: 0, stderr: "", memoryCapBytes: 1024));
    }

    // L6: empty-stderr fallback should NOT trip on sub-500ms exits — those
    // are almost certainly startup crashes (DLL load failure, missing
    // runtime), not Job-Object memory kills.

    [Fact]
    public void IsMemoryCapExitCode_StartupCrash_NotMemoryCap()
    {
        // 50ms exit with empty stderr looks like a launcher fault.
        Assert.False(BuildSubprocess.IsMemoryCapExitCode(
            exitCode: -1073741515, stderr: "", memoryCapBytes: 1024,
            elapsedMs: 50));
        // Same exit, sub-threshold elapsed.
        Assert.False(BuildSubprocess.IsMemoryCapExitCode(
            exitCode: -1, stderr: "", memoryCapBytes: 1024,
            elapsedMs: 499));
    }

    [Fact]
    public void IsMemoryCapExitCode_LongRun_StillTreatedAsMemoryCap()
    {
        // Above the 500ms threshold, the heuristic still applies.
        Assert.True(BuildSubprocess.IsMemoryCapExitCode(
            exitCode: -1, stderr: "", memoryCapBytes: 1024,
            elapsedMs: 500));
        Assert.True(BuildSubprocess.IsMemoryCapExitCode(
            exitCode: -1, stderr: "", memoryCapBytes: 1024,
            elapsedMs: 30_000));
    }

    [Fact]
    public void IsMemoryCapExitCode_StatusQuota_AlwaysWinsRegardlessOfElapsed()
    {
        // The canonical NT status code 0xC0000044 always classifies as
        // memory cap, even on sub-threshold elapsed (Job Object can in
        // principle kill very fast on a tight cap).
        int statusQuota = unchecked((int)0xC0000044);
        Assert.True(BuildSubprocess.IsMemoryCapExitCode(
            exitCode: statusQuota, stderr: "", memoryCapBytes: 1024,
            elapsedMs: 1));
    }

    [Fact]
    public void IsMemoryCapExitCode_LegacySignature_StillWorks()
    {
        // Default elapsedMs = -1 preserves pre-L6 behaviour for any
        // caller that doesn't pass it.
        Assert.True(BuildSubprocess.IsMemoryCapExitCode(
            exitCode: -1, stderr: "", memoryCapBytes: 1024));
    }

    // ══════════════════════ ParseBench helpers ══════════════════════

    [Fact]
    public void ParseBenchMs_HappyPath()
    {
        string stdout = "BENCH grid_build_total_ms=1234.5\nBENCH export_meshing_ms=678.1\n";
        Assert.Equal(1234.5, BuildSubprocess.ParseBenchMs(stdout, "grid_build_total_ms"));
        Assert.Equal(678.1,  BuildSubprocess.ParseBenchMs(stdout, "export_meshing_ms"));
    }

    [Fact]
    public void ParseBenchMs_MissingKeyReturnsZero()
    {
        string stdout = "BENCH other_key=42.0\n";
        Assert.Equal(0.0, BuildSubprocess.ParseBenchMs(stdout, "not_present"));
    }

    [Fact]
    public void ParseBenchMs_IgnoresNonBenchMatchesOfKey()
    {
        // Key appears in a non-BENCH context — parser must not trip.
        string stdout = "# grid_build_total_ms: ignored in log\nBENCH grid_build_total_ms=9.5\n";
        Assert.Equal(9.5, BuildSubprocess.ParseBenchMs(stdout, "grid_build_total_ms"));
    }

    [Fact]
    public void ParseBenchLong_HappyPath()
    {
        string stdout = "BENCH triangle_count=2000000\n";
        Assert.Equal(2_000_000L, BuildSubprocess.ParseBenchLong(stdout, "triangle_count"));
    }

    [Fact]
    public void ParseBenchLong_GarbledValueReturnsZero()
    {
        string stdout = "BENCH triangle_count=garbage\n";
        Assert.Equal(0L, BuildSubprocess.ParseBenchLong(stdout, "triangle_count"));
    }

    // ══════════════════════ BuildSubprocess.Run ══════════════════════

    [Fact]
    public void Run_ThrowsWhenExporterExeMissing()
    {
        var req = new BuildSubprocessRequest
        {
            DesignJsonPath     = Path.Combine(Path.GetTempPath(), "dummy.json"),
            OutStlPath         = Path.Combine(Path.GetTempPath(), "dummy.stl"),
            StlExporterExePath = Path.Combine(Path.GetTempPath(),
                $"not-a-real-exe-{Guid.NewGuid():N}.exe"),
            VoxelSize_mm       = 0.5,
        };
        Assert.Throws<FileNotFoundException>(() => BuildSubprocess.Run(req));
    }

    [Fact]
    public void Run_RejectsNullRequest()
    {
        Assert.Throws<ArgumentNullException>(() => BuildSubprocess.Run(null!));
    }

    [Fact]
    public void ExitCodeMemoryCapExceeded_IsPublicConstant()
    {
        // Forcing function: the public constant lets callers route UI
        // messaging based on the cap-breach code without string-
        // matching. If someone renumbers this, they must update UI
        // dispatch too — catching via test.
        Assert.Equal(12, BuildSubprocess.ExitCodeMemoryCapExceeded);
    }

    // ══════════════════════ SessionSettings flag ══════════════════════

    [Fact]
    public void SessionSettings_IsolateFlag_DefaultsFalse()
    {
        var s = new SessionSettings();
        Assert.False(s.IsolateLargeBuildsAtFailProjection);
    }

    [Fact]
    public void SessionSettings_IsolateFlag_RoundTripsThroughJson()
    {
        var original = new SessionSettings
        {
            IsolateLargeBuildsAtFailProjection = true,
            TileLargeBuilds                     = true,
            TileCount                           = 6,
            AutoCoarsenVoxelToFitBudget         = true,
        };

        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<SessionSettings>(json)!;

        Assert.True(restored.IsolateLargeBuildsAtFailProjection);
        Assert.True(restored.TileLargeBuilds);
        Assert.Equal(6, restored.TileCount);
        Assert.True(restored.AutoCoarsenVoxelToFitBudget);
    }
}
