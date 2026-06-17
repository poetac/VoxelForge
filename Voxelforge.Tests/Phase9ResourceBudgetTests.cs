// Phase9ResourceBudgetTests.cs — Pure-data unit tests for the
// Resource Budget machinery. The WinForms-facing
// pieces (cboResourceMode combo, resource gauge label, foreground
// throttle timer) are exercised indirectly by instantiating the
// form during integration runs; here we cover the logic that
// doesn't require a message loop.

using System.Threading;
using System.Threading.Tasks;
using Voxelforge.Analysis;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;
using Voxelforge.UI;

namespace Voxelforge.Tests;

public class Phase9ResourceBudgetTests
{
    // ═════════════════════════════════════════════════════════════════
    //  A3 / A4 — ResourceMode + ResourcePresets resolution
    // ═════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(ResourceMode.Quiet,    16, 32768)]
    [InlineData(ResourceMode.Balanced, 16, 32768)]
    [InlineData(ResourceMode.Maximum,  16, 32768)]
    public void ResourcePresets_Resolve_ProducesSensibleDefaults(ResourceMode mode, int cores, long memMB)
    {
        var r = ResourcePresets.Resolve(mode, cores, memMB);
        Assert.InRange(r.MaxCores, 1, cores);
        Assert.True(r.MemoryBudget_MB >= 0);
    }

    [Fact]
    public void ResourcePresets_Quiet_UsesAQuarterOfCoresAndMemory()
    {
        var r = ResourcePresets.Resolve(ResourceMode.Quiet, 16, 32768);
        Assert.Equal(4, r.MaxCores);
        Assert.Equal(8192, r.MemoryBudget_MB);
        Assert.True(r.DemotePriority);
    }

    [Fact]
    public void ResourcePresets_Maximum_UnlocksEverything()
    {
        var r = ResourcePresets.Resolve(ResourceMode.Maximum, 16, 32768);
        Assert.Equal(16, r.MaxCores);
        Assert.Equal(0, r.MemoryBudget_MB);       // 0 = uncapped
        Assert.False(r.DemotePriority);
    }

    [Fact]
    public void ResourcePresets_Balanced_LeavesHeadroomForForegroundApps()
    {
        var r = ResourcePresets.Resolve(ResourceMode.Balanced, 16, 32768);
        Assert.InRange(r.MaxCores, 8, 14);        // ~75 % of 16
        Assert.Equal(16384, r.MemoryBudget_MB);   // 50 % of 32 GB
        Assert.True(r.DemotePriority);
    }

    [Fact]
    public void ResourcePresets_TinyMachine_DoesNotReturnZeroCores()
    {
        var r = ResourcePresets.Resolve(ResourceMode.Quiet, totalCores: 2, totalMemory_MB: 4096);
        Assert.True(r.MaxCores >= 1);
    }

    [Fact]
    public void ResourceBudget_AutoProbeDefaults_FillsExplicitCapsFromPreset()
    {
        var s = new SessionSettings();  // fresh — MaxParallelism = 0, MemoryBudget_MB = 0
        Assert.Equal(0, s.MaxParallelism);
        Assert.Equal(0, s.MemoryBudget_MB);
        bool changed = ResourceBudgetSettings.AutoProbeDefaults(s);
        Assert.True(changed);
        Assert.True(s.MaxParallelism > 0);
    }

    [Fact]
    public void ResourceBudget_AutoProbeDefaults_RespectsUserOverrides()
    {
        var s = new SessionSettings
        {
            MaxParallelism = 3,             // user has explicitly picked 3 cores
            MemoryBudget_MB = 2048,         // and 2 GB
        };
        bool changed = ResourceBudgetSettings.AutoProbeDefaults(s);
        Assert.False(changed);
        Assert.Equal(3, s.MaxParallelism);
        Assert.Equal(2048, s.MemoryBudget_MB);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Time-budget plumbing
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionSettings_TimeoutFields_DefaultToZero()
    {
        // 0 = no cap; preserves legacy behaviour for users upgrading.
        var s = new SessionSettings();
        Assert.Equal(0, s.SweepTimeoutSeconds);
        Assert.Equal(0, s.OptTimeoutSeconds);
    }

    [Fact]
    public void SessionSettings_TimeoutFields_RoundTripThroughJson()
    {
        using var tmp = TestTempFile.WithUniqueName("rcd-session", "json");
        var s = new SessionSettings
        {
            SweepTimeoutSeconds = 60,
            OptTimeoutSeconds   = 900,
        };
        Assert.True(s.Save(tmp.Path));

        var loaded = SessionSettings.Load(tmp.Path);
        Assert.Equal(60, loaded.SweepTimeoutSeconds);
        Assert.Equal(900, loaded.OptTimeoutSeconds);
    }

    [Fact]
    public void ResourceBudget_ApplySettings_PropagatesTimeouts()
    {
        var s = new SessionSettings
        {
            SweepTimeoutSeconds = 120,
            OptTimeoutSeconds   = 600,
        };
        ResourceBudgetSettings.ApplySettings(s);
        Assert.Equal(120, ResourceBudget.SweepTimeoutSeconds);
        Assert.Equal(600, ResourceBudget.OptTimeoutSeconds);
    }

    [Fact]
    public void ResourceBudget_ApplySettings_ClampsNegativeTimeoutsToZero()
    {
        // A hand-edited session.json must not be able to arm a
        // negative CancelAfter — the clamp lives in ApplySettings.
        var s = new SessionSettings
        {
            SweepTimeoutSeconds = -5,
            OptTimeoutSeconds   = -10,
        };
        ResourceBudgetSettings.ApplySettings(s);
        Assert.Equal(0, ResourceBudget.SweepTimeoutSeconds);
        Assert.Equal(0, ResourceBudget.OptTimeoutSeconds);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Abort-on-user-input
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionSettings_AbortOpOnInputEdit_DefaultsFalse()
    {
        // Default false preserves legacy behaviour; the user opts in
        // explicitly when they want field edits to cancel stale solves.
        var s = new SessionSettings();
        Assert.False(s.AbortOpOnInputEdit);
    }

    [Fact]
    public void SessionSettings_AbortOpOnInputEdit_RoundTripsThroughJson()
    {
        using var tmp = TestTempFile.WithUniqueName("rcd-session", "json");
        var s = new SessionSettings { AbortOpOnInputEdit = true };
        Assert.True(s.Save(tmp.Path));
        var loaded = SessionSettings.Load(tmp.Path);
        Assert.True(loaded.AbortOpOnInputEdit);
    }

    [Fact]
    public void ResourceBudget_ApplySettings_PropagatesAbortOpOnInputEdit()
    {
        var on  = new SessionSettings { AbortOpOnInputEdit = true };
        var off = new SessionSettings { AbortOpOnInputEdit = false };

        ResourceBudgetSettings.ApplySettings(on);
        Assert.True(ResourceBudget.AbortOpOnInputEdit);

        // Applying a fresh settings must flip the global back — the
        // form only ever writes one snapshot at a time, so the global
        // shouldn't sticky-latch at true.
        ResourceBudgetSettings.ApplySettings(off);
        Assert.False(ResourceBudget.AbortOpOnInputEdit);
    }

    [Fact]
    public void SharedState_CancelCurrentOp_RoundTripsThroughPostAndTryTake()
    {
        // Drain any pending cancel request so this test is independent
        // of run order — TryTake returns false when nothing is queued.
        while (SharedState.TryTakeCancelCurrentOp()) { /* drain */ }
        Assert.False(SharedState.TryTakeCancelCurrentOp());

        SharedState.PostCancelCurrentOp();
        Assert.True(SharedState.TryTakeCancelCurrentOp());

        // One post → one take. The flag clears on read; a second take
        // returns false so the task-thread loop doesn't cancel twice.
        Assert.False(SharedState.TryTakeCancelCurrentOp());
    }

    [Fact]
    public void CancellationTokenSource_CancelAfter_FiresWithinBudget()
    {
        // Contract check: the underlying CancelAfter mechanism we rely
        // on for D2 does trip the token before the budget expires.
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        var start = System.DateTime.UtcNow;
        // Generous wait — Windows GitHub runners can starve the timer thread
        // pool under load and miss a 2 s deadline (observed 2026-04-25).
        Assert.True(cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(15)));
        Assert.True(cts.IsCancellationRequested);
        // Timer fires after the delay; sub-ms precision isn't the point.
        Assert.True((System.DateTime.UtcNow - start).TotalMilliseconds < 15000);
    }

    // ═════════════════════════════════════════════════════════════════
    //  B1 — MemoryProjectionGate projection + thresholds
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void MemoryProjectionGate_SmallChamber_PassesUnderGenerousBudget()
    {
        // 200 mm × 100 mm × 100 mm bbox at 0.4 mm voxel = 125M dense
        // voxels. × 0.50 sparsity × 4 bytes = 250 MB projected.
        // 8 GB budget → Pass.
        var p = MemoryProjectionGate.Project(
            boundingLx_mm: 200, boundingLy_mm: 100, boundingLz_mm: 100,
            voxelSize_mm: 0.4,
            budgetBytes: 8L * 1024 * 1024 * 1024);
        Assert.Equal(MemoryProjectionLevel.Pass, p.Level);
        Assert.True(p.FractionOfBudget < 0.10);
    }

    [Fact]
    public void MemoryProjectionGate_FineVoxel_FailsTightBudget()
    {
        // 200 × 100 × 100 at 0.10 mm = 2e9 dense voxels × 0.5 × 4 B
        // = ~3.7 GB projected. With a 1 GB budget → Fail.
        var p = MemoryProjectionGate.Project(
            boundingLx_mm: 200, boundingLy_mm: 100, boundingLz_mm: 100,
            voxelSize_mm: 0.10,
            budgetBytes: 1L * 1024 * 1024 * 1024);
        Assert.Equal(MemoryProjectionLevel.Fail, p.Level);
        Assert.True(p.FractionOfBudget > 1.0);
        Assert.Contains("Coarsen voxel size", p.Message);
    }

    [Fact]
    public void MemoryProjectionGate_NearBudget_WarnsButPasses()
    {
        // 200 × 100 × 100 at 0.10 mm = 2e9 dense voxels × 0.5 × 4 B
        // ≈ 3.72 GB. With a 4.5 GB budget → frac ~0.83 → Warning.
        long budget = (long)(4.5 * 1024 * 1024 * 1024);
        var p = MemoryProjectionGate.Project(
            boundingLx_mm: 200, boundingLy_mm: 100, boundingLz_mm: 100,
            voxelSize_mm: 0.10,
            budgetBytes: budget);
        Assert.Equal(MemoryProjectionLevel.Warning, p.Level);
        Assert.InRange(p.FractionOfBudget, 0.70, 1.00);
    }

    [Fact]
    public void MemoryProjectionGate_ZeroBudget_SkipsProjection()
    {
        var p = MemoryProjectionGate.Project(
            boundingLx_mm: 200, boundingLy_mm: 100, boundingLz_mm: 100,
            voxelSize_mm: 0.4,
            budgetBytes: 0);
        Assert.Equal(MemoryProjectionLevel.Pass, p.Level);
        Assert.Contains("skipped", p.Message);
    }

    [Fact]
    public void MemoryProjectionGate_ZeroVoxelSize_SkipsProjection()
    {
        var p = MemoryProjectionGate.Project(100, 100, 100, 0, 1_000_000_000);
        Assert.Equal(MemoryProjectionLevel.Pass, p.Level);
        Assert.Contains("Voxel size", p.Message);
    }

    // ═════════════════════════════════════════════════════════════════
    //  D1 — CancellationToken plumbing through ToleranceAnalysis
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void ToleranceAnalysis_CancellationToken_AbortsBeforeCompletion()
    {
        var cond = new OperatingConditions
        {
            PropellantPair = Voxelforge.Combustion.PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false,
            IncludePorts = false,
            IncludeInjectorFlange = false,
            ContourStationCount = 40,
        };
        var gas = Voxelforge.Combustion.PropellantTables.Lookup(
            cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        var contour = Voxelforge.Chamber.ChamberContourGenerator.Generate(
            throatRadius_mm:        derived.ThroatRadius_mm,
            contractionRatio:       design.ContractionRatio,
            expansionRatio:         design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            stationCount:           40);

        var cts = new CancellationTokenSource();
        cts.Cancel();   // pre-cancel → Parallel.For aborts immediately on first dispatch

        var inputs = new ToleranceInputs(SampleCount: 200, RandomSeed: 1);
        Assert.Throws<System.OperationCanceledException>(() =>
            ToleranceAnalysis.Run(contour, cond, design, inputs, cts.Token));
    }

    // ═════════════════════════════════════════════════════════════════
    //  F2 — SA early termination on convergence
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Optimizer_ConvergenceReached_TripsAfterThreeRestartsWithoutBest()
    {
        // Single-dim problem with a minimum at 5. Start away from it,
        // but feed the optimiser a constant score so it never finds a
        // new best — after enough iterations it should restart + stop.
        var bounds = new (double Min, double Max)[] { (0, 10) };
        var opt = new SimulatedAnnealingOptimizer(bounds, maxIterations: 200, seed: 7);
        opt.MaxRestartsWithoutImprovement = 2;  // aggressive for test speed

        // First candidate: random — accept as initial best.
        var cand0 = opt.NextCandidate();
        opt.ReportScore(cand0, score: 100.0, breakdown: null);
        Assert.False(opt.ConvergenceReached);

        // Feed the SAME score forever. The SA's stagnation counter
        // will hit threshold and trigger a restart; after two more
        // such cycles with no improvement, ConvergenceReached flips.
        for (int i = 0; i < 200; i++)
        {
            var cand = opt.NextCandidate();
            opt.ReportScore(cand, score: 100.0, breakdown: null);
            if (opt.IsComplete) break;
        }
        Assert.True(opt.ConvergenceReached);
        Assert.True(opt.IsComplete);
    }

    [Fact]
    public void Optimizer_NewBestResetsConvergenceCounter()
    {
        var bounds = new (double Min, double Max)[] { (0, 10) };
        var opt = new SimulatedAnnealingOptimizer(bounds, maxIterations: 500, seed: 7);
        opt.MaxRestartsWithoutImprovement = 3;

        // Prime: score 100.
        opt.ReportScore(opt.NextCandidate(), 100.0, null);

        // Flatline for a while, then drop to 50: that's a new best,
        // counter should reset, ConvergenceReached stays false.
        for (int i = 0; i < 30; i++)
            opt.ReportScore(opt.NextCandidate(), 100.0, null);
        opt.ReportScore(opt.NextCandidate(), 50.0, null);

        Assert.False(opt.ConvergenceReached);
    }

    // ═════════════════════════════════════════════════════════════════
    //  ResourceProfiler
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void ResourceProfiler_Begin_SetsInFlight_End_Clears()
    {
        ResourceProfiler.Begin("probe-flight");
        Assert.True(ResourceProfiler.IsOpInFlight);
        Assert.Equal("probe-flight", ResourceProfiler.CurrentOpName);
        var s = ResourceProfiler.End("probe-flight", emitBench: false);
        Assert.False(ResourceProfiler.IsOpInFlight);
        Assert.Null(ResourceProfiler.CurrentOpName);
        Assert.True(s.WallMs >= 0);
        Assert.True(s.PeakWorkingSetBytes > 0);
    }

    [Fact]
    public void ResourceProfiler_RecordWorkingSetSample_UpdatesPeakMonotonically()
    {
        ResourceProfiler.Begin("probe-peak");
        try
        {
            ResourceProfiler.RecordWorkingSetSample(1_000_000);
            ResourceProfiler.RecordWorkingSetSample(5_000_000);
            ResourceProfiler.RecordWorkingSetSample(2_000_000);   // stale; peak stays at 5M
            ResourceProfiler.RecordWorkingSetSample(7_500_000);
        }
        finally
        {
            var s = ResourceProfiler.End("probe-peak", emitBench: false);
            // End() may pick up the real process WS — just assert that
            // the peak is at least the largest sample we pushed.
            Assert.True(s.PeakWorkingSetBytes >= 7_500_000);
        }
    }

    [Fact]
    public void ResourceProfiler_End_CachesLastSummary()
    {
        // Tech-debt T17 (2026-04-28): the previous version of this test
        // used `Thread.Sleep(5)` to ensure wall-clock time > 0 ms before
        // assertion. That's fragile on slow CI (5 ms can round to 0 on
        // some Stopwatch resolutions). The cache-equality assertions are
        // the load-bearing claim; whether `cached.WallMs` is 0 or 5 ms
        // doesn't change what this test is meant to pin (the End() →
        // LastSummary() round-trip preserves all fields). So we drop the
        // sleep + the fragile WallMs > 0 assertion entirely. If wall-time
        // tracking ever regresses, ResourceProfiler_End_TracksWallTime
        // (a sibling test that does the heavy work to take measurable
        // time) catches it.
        ResourceProfiler.Begin("probe-cache");
        var ret = ResourceProfiler.End("probe-cache", emitBench: false);
        var cached = ResourceProfiler.LastSummary("probe-cache");
        Assert.Equal(ret.WallMs, cached.WallMs);
        Assert.Equal(ret.PeakWorkingSetBytes, cached.PeakWorkingSetBytes);
        Assert.True(cached.WallMs >= 0);
    }

    [Fact]
    public void ResourceProfiler_End_WithWrongOpName_IsNoOpAndDoesNotClobber()
    {
        ResourceProfiler.Begin("probe-active");
        try
        {
            var stale = ResourceProfiler.End("probe-other", emitBench: false);
            Assert.Equal(0, stale.WallMs);
            Assert.True(ResourceProfiler.IsOpInFlight);
            Assert.Equal("probe-active", ResourceProfiler.CurrentOpName);
        }
        finally
        {
            ResourceProfiler.End("probe-active", emitBench: false);
        }
    }

    [Fact]
    public void ResourceProfiler_RecordWorkingSetSample_NoOpWhenIdle()
    {
        // Any leftover op from another test: clear it.
        if (ResourceProfiler.IsOpInFlight)
            ResourceProfiler.End(ResourceProfiler.CurrentOpName!, emitBench: false);

        // No op in flight — RecordWorkingSetSample must not throw and
        // must not trigger a lingering "in flight" state.
        ResourceProfiler.RecordWorkingSetSample(999_999_999);
        Assert.False(ResourceProfiler.IsOpInFlight);
    }

    [Fact]
    public void ResourceOpSummary_Format_UsesGBForLargePeaksAndMinForLongRuns()
    {
        var big = new ResourceOpSummary
        {
            WallMs              = 120_000,          // 2 min
            CpuMs               = 60_000,
            CpuPct              = 75,
            PeakWorkingSetBytes = 3L * 1024 * 1024 * 1024,   // 3 GB
        };
        string s = big.Format();
        Assert.Contains("GB", s);
        Assert.Contains("min", s);
        Assert.Contains("75%", s);

        var tiny = new ResourceOpSummary
        {
            WallMs              = 500,               // 0.5 s
            CpuMs               = 50,
            CpuPct              = 10,
            PeakWorkingSetBytes = 400L * 1024 * 1024,  // 400 MB
        };
        string t = tiny.Format();
        Assert.Contains("MB", t);
        Assert.Contains(" s", t);
        Assert.DoesNotContain("GB", t);
        Assert.DoesNotContain("min", t);
    }

    // ═════════════════════════════════════════════════════════════════
    //  MemoryProjectionGate.EnsureFits + SuggestCoarserVoxel
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void MemoryProjectionGate_EnsureFits_PassesUnderGenerousBudget()
    {
        // Baseline chamber at 0.4 mm voxel projects ~20 MB — well under
        // 8 GB budget; EnsureFits should return a Pass-level projection.
        var p = MemoryProjectionGate.EnsureFits(
            boundingLx_mm: 200.0, boundingLy_mm: 60.0, boundingLz_mm: 60.0,
            voxelSize_mm: 0.4, budgetBytes: 8L * 1024 * 1024 * 1024);
        Assert.Equal(MemoryProjectionLevel.Pass, p.Level);
    }

    [Fact]
    public void MemoryProjectionGate_EnsureFits_ThrowsOnFail()
    {
        // 500 × 200 × 200 mm bbox at 0.1 mm voxel → ~40 GB projected
        // (dense 2 × 10¹⁰ voxels × sparsity 0.5 × 4 B). Under an 8 GB
        // budget this is a Fail-level projection, which EnsureFits
        // converts to a MemoryBudgetExceededException.
        var ex = Assert.Throws<MemoryBudgetExceededException>(() =>
            MemoryProjectionGate.EnsureFits(
                boundingLx_mm: 500.0, boundingLy_mm: 200.0, boundingLz_mm: 200.0,
                voxelSize_mm: 0.1, budgetBytes: 8L * 1024 * 1024 * 1024));
        Assert.True(ex.SuggestedVoxel_mm > 0.1,
            "Suggested voxel must be coarser than the requested voxel.");
        Assert.Contains("voxel", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MemoryProjectionGate_EnsureFits_SkipsWhenBudgetZero()
    {
        // Maximum resource mode leaves MemoryBudget_MB = 0 → no gate.
        // EnsureFits must not throw regardless of projection size.
        var p = MemoryProjectionGate.EnsureFits(
            boundingLx_mm: 10000.0, boundingLy_mm: 10000.0, boundingLz_mm: 10000.0,
            voxelSize_mm: 0.1, budgetBytes: 0);
        Assert.Equal(MemoryProjectionLevel.Pass, p.Level);
    }

    [Fact]
    public void MemoryProjectionGate_SuggestCoarserVoxel_CubeRootScaling()
    {
        // 500 × 200 × 200 mm bbox at 0.1 mm voxel, 8 GB budget.
        // Projected = (5000 × 2000 × 2000) × 0.5 × 4 B = 4 × 10¹⁰ B ≈ 40 GB.
        // Ratio vs (8 GB × 0.80 target) = 40 / 6.87 ≈ 5.82.
        // Suggested = 0.1 × 5.82^(1/3) ≈ 0.180 mm, ceil to 0.01 mm → 0.18 mm.
        double suggested = MemoryProjectionGate.SuggestCoarserVoxel(
            boundingLx_mm: 500.0, boundingLy_mm: 200.0, boundingLz_mm: 200.0,
            voxelSize_mm: 0.1, budgetBytes: 8L * 1024 * 1024 * 1024);
        Assert.InRange(suggested, 0.17, 0.22);
    }

    [Fact]
    public void MemoryProjectionGate_SuggestCoarserVoxel_ReturnsNaNOnDegenerate()
    {
        // Zero budget → can't back-solve a safe voxel.
        double s1 = MemoryProjectionGate.SuggestCoarserVoxel(100, 100, 100, 0.4, 0);
        Assert.True(double.IsNaN(s1));
        // Zero voxel → can't back-solve either.
        double s2 = MemoryProjectionGate.SuggestCoarserVoxel(100, 100, 100, 0,
            budgetBytes: 1L * 1024 * 1024 * 1024);
        Assert.True(double.IsNaN(s2));
    }

    [Fact]
    public void MemoryBudgetExceededException_CarriesSuggestedVoxelAndFields()
    {
        // Same Fail-level inputs as EnsureFits_ThrowsOnFail; inspect the
        // exception payload so the UI catch can render an actionable status.
        var ex = Assert.Throws<MemoryBudgetExceededException>(() =>
            MemoryProjectionGate.EnsureFits(
                boundingLx_mm: 500.0, boundingLy_mm: 200.0, boundingLz_mm: 200.0,
                voxelSize_mm: 0.1, budgetBytes: 8L * 1024 * 1024 * 1024));
        Assert.Equal(0.1, ex.RequestedVoxel_mm);
        Assert.True(ex.BudgetBytes > 0);
        Assert.True(ex.ProjectedBytes > ex.BudgetBytes);
        Assert.True(ex.FractionOfBudget > 1.0);
    }

    // ═════════════════════════════════════════════════════════════════
    //  SA infeasible-streak early exit
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Optimizer_InfeasibleStreakExit_TripsAfterThreshold()
    {
        // Every candidate returns +∞. After 60 consecutive infeasible
        // iterations (default MaxConsecutiveInfeasibleBeforeExit), both
        // InfeasibleExitTripped and ConvergenceReached flip to true —
        // the main dispatch loop then sees IsComplete and runs
        // FinalizeOpt, rather than burning a full 300-iter / 5-hour run.
        var bounds = new (double Min, double Max)[] { (0, 10) };
        var opt = new SimulatedAnnealingOptimizer(bounds, maxIterations: 300, seed: 7);
        opt.MaxConsecutiveInfeasibleBeforeExit = 60;

        // Prime: score +∞ so the very first candidate counts as infeasible.
        opt.ReportScore(opt.NextCandidate(), double.PositiveInfinity, null);

        // Feed +∞ until the exit flag flips.
        for (int i = 0; i < 200 && !opt.InfeasibleExitTripped; i++)
            opt.ReportScore(opt.NextCandidate(), double.PositiveInfinity, null);

        Assert.True(opt.InfeasibleExitTripped);
        Assert.True(opt.ConvergenceReached);
        Assert.True(opt.IsComplete);
        Assert.True(opt.Iteration < 120,
            $"Should exit well before 200 iters; got {opt.Iteration}.");
    }

    [Fact]
    public void Optimizer_InfeasibleStreak_ResetsOnFeasibleScore()
    {
        // Streak counter must reset when a finite score lands — otherwise
        // a late-arriving feasible candidate would be wasted.
        var bounds = new (double Min, double Max)[] { (0, 10) };
        var opt = new SimulatedAnnealingOptimizer(bounds, maxIterations: 500, seed: 7);
        opt.MaxConsecutiveInfeasibleBeforeExit = 10;

        opt.ReportScore(opt.NextCandidate(), 100.0, null);
        // 5 infeasible in a row (under threshold)
        for (int i = 0; i < 5; i++)
            opt.ReportScore(opt.NextCandidate(), double.PositiveInfinity, null);
        // One feasible resets the counter
        opt.ReportScore(opt.NextCandidate(), 100.0, null);
        // Now 9 more infeasible — still under threshold because it reset
        for (int i = 0; i < 9; i++)
            opt.ReportScore(opt.NextCandidate(), double.PositiveInfinity, null);

        Assert.False(opt.InfeasibleExitTripped);
    }

    [Fact]
    public void Optimizer_InfeasibleStreak_NaNScoreCountsAsInfeasible()
    {
        // Propellant-table edge cases and divide-by-zero can emit NaN.
        // Treat NaN the same as +∞ so SA doesn't spin forever on a
        // numerically broken search space.
        var bounds = new (double Min, double Max)[] { (0, 10) };
        var opt = new SimulatedAnnealingOptimizer(bounds, maxIterations: 500, seed: 7);
        opt.MaxConsecutiveInfeasibleBeforeExit = 15;

        opt.ReportScore(opt.NextCandidate(), double.NaN, null);
        for (int i = 0; i < 20 && !opt.InfeasibleExitTripped; i++)
            opt.ReportScore(opt.NextCandidate(), double.NaN, null);

        Assert.True(opt.InfeasibleExitTripped);
    }

    [Fact]
    public void Optimizer_InfeasibleStreak_DisabledWhenThresholdIsZero()
    {
        // Opt-out: setting MaxConsecutiveInfeasibleBeforeExit = 0 keeps
        // Legacy behaviour (stagnation-restart convergence only).
        var bounds = new (double Min, double Max)[] { (0, 10) };
        var opt = new SimulatedAnnealingOptimizer(bounds, maxIterations: 500, seed: 7);
        opt.MaxConsecutiveInfeasibleBeforeExit = 0;
        opt.MaxRestartsWithoutImprovement = 99;   // keep convergence disabled too

        opt.ReportScore(opt.NextCandidate(), double.PositiveInfinity, null);
        for (int i = 0; i < 200; i++)
            opt.ReportScore(opt.NextCandidate(), double.PositiveInfinity, null);

        Assert.False(opt.InfeasibleExitTripped);
    }

    // ═════════════════════════════════════════════════════════════════
    //  SA memory-abort signal (SignalMemoryAbort)
    // ═════════════════════════════════════════════════════════════════

    // ═════════════════════════════════════════════════════════════════
    //  AutoCoarsenVoxelToFitBudget (setting + snapshot)
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionSettings_AutoCoarsenVoxelToFitBudget_DefaultsFalse()
    {
        // Opt-in: strict block-on-Fail behaviour must be preserved
        // for users who haven't flipped the checkbox.
        var s = new SessionSettings();
        Assert.False(s.AutoCoarsenVoxelToFitBudget);
    }

    [Fact]
    public void SessionSettings_AutoCoarsenVoxelToFitBudget_RoundTripsThroughJson()
    {
        using var tmp = TestTempFile.Create();
        var outSet = new SessionSettings { AutoCoarsenVoxelToFitBudget = true };
        outSet.Save(tmp.Path);
        var inSet = SessionSettings.Load(tmp.Path);
        Assert.True(inSet.AutoCoarsenVoxelToFitBudget);
    }

    [Fact]
    public void ResourceBudget_ApplySettings_PropagatesAutoCoarsenVoxel()
    {
        // Bidirectional: on → global-on, off → global-off. Catches any
        // sticky-latch regression in the volatile snapshot machinery.
        var on  = new SessionSettings { AutoCoarsenVoxelToFitBudget = true };
        ResourceBudgetSettings.ApplySettings(on);
        Assert.True(ResourceBudget.AutoCoarsenVoxelToFitBudget);

        var off = new SessionSettings { AutoCoarsenVoxelToFitBudget = false };
        ResourceBudgetSettings.ApplySettings(off);
        Assert.False(ResourceBudget.AutoCoarsenVoxelToFitBudget);
    }

    // ═════════════════════════════════════════════════════════════════
    //  FastPreviewMode (channels-skipped Generate preview)
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionSettings_FastPreviewMode_DefaultsFalse()
    {
        var s = new SessionSettings();
        Assert.False(s.FastPreviewMode);
    }

    [Fact]
    public void SessionSettings_FastPreviewMode_RoundTripsThroughJson()
    {
        using var tmp = TestTempFile.Create();
        var outSet = new SessionSettings { FastPreviewMode = true };
        outSet.Save(tmp.Path);
        var inSet = SessionSettings.Load(tmp.Path);
        Assert.True(inSet.FastPreviewMode);
    }

    [Fact]
    public void ResourceBudget_ApplySettings_PropagatesFastPreviewMode()
    {
        var on = new SessionSettings { FastPreviewMode = true };
        ResourceBudgetSettings.ApplySettings(on);
        Assert.True(ResourceBudget.FastPreviewMode);

        var off = new SessionSettings { FastPreviewMode = false };
        ResourceBudgetSettings.ApplySettings(off);
        Assert.False(ResourceBudget.FastPreviewMode);
    }

    // ═════════════════════════════════════════════════════════════════
    //  ResourceProfiler working-set watchdog
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void ResourceProfiler_Watchdog_DoesNotTripBelowThreshold()
    {
        // Set a 1 GB budget and feed samples well under threshold; the
        // watchdog must stay armed (not tripped).
        var saved = new SessionSettings
        {
            MemoryBudget_MB = 1024,   // 1 GB budget
            ResourceMode    = ResourceMode.Custom,
            MaxParallelism  = 1,
        };
        ResourceBudgetSettings.ApplySettings(saved);
        ResourceProfiler.SetWatchdogEnabled(false);   // decouple from SharedState for the test
        ResourceProfiler.Begin("wd-test-1");
        try
        {
            ResourceProfiler.RecordWorkingSetSample(100L * 1024 * 1024);   // 100 MB — well under 95 % × 1 GB
            Assert.False(ResourceProfiler.WatchdogTripped);
        }
        finally
        {
            ResourceProfiler.End("wd-test-1", emitBench: false);
            ResourceProfiler.SetWatchdogEnabled(true);
        }
    }

    [Fact]
    public void ResourceProfiler_Watchdog_TripsOnceAboveThreshold()
    {
        // 1 GB budget, sample at 99 % of budget triggers the watchdog.
        // When disabled (our test config), it should NOT post a cancel
        // request (decouples us from SharedState singletons) but the
        // Trip flag must still work correctly once re-enabled.
        ResourceBudgetSettings.ApplySettings(new SessionSettings
        {
            MemoryBudget_MB = 1024,
            ResourceMode    = ResourceMode.Custom,
            MaxParallelism  = 1,
        });

        // Enable the watchdog to verify it trips the flag; we intercept
        // the cancel channel by reading TryTakeCancelCurrentOp after.
        ResourceProfiler.SetWatchdogEnabled(true);
        while (SharedState.TryTakeCancelCurrentOp()) { /* drain */ }

        ResourceProfiler.Begin("wd-test-2");
        try
        {
            long aboveThreshold = (long)(1024L * 1024L * 1024L * 0.99);
            ResourceProfiler.RecordWorkingSetSample(aboveThreshold);
            Assert.True(ResourceProfiler.WatchdogTripped);
            Assert.True(ResourceProfiler.LastWatchdogTripWsBytes >= (long)(1024L * 1024L * 1024L * 0.95));
            // The cancel flag must have been posted exactly once.
            Assert.True(SharedState.TryTakeCancelCurrentOp(),
                "Watchdog must post one cancel request on first threshold crossing.");
            // Feeding more samples above threshold must NOT re-post cancel
            // (Interlocked 0→1 latch).
            ResourceProfiler.RecordWorkingSetSample(aboveThreshold + 1000);
            Assert.False(SharedState.TryTakeCancelCurrentOp(),
                "Second sample above threshold must NOT re-post cancel.");
        }
        finally
        {
            ResourceProfiler.End("wd-test-2", emitBench: false);
        }
    }

    [Fact]
    public void ResourceProfiler_Watchdog_ReArmsOnNewOp()
    {
        // Trip in op 1, then Begin op 2 → flag resets.
        ResourceBudgetSettings.ApplySettings(new SessionSettings
        {
            MemoryBudget_MB = 1024, ResourceMode = ResourceMode.Custom, MaxParallelism = 1,
        });
        ResourceProfiler.SetWatchdogEnabled(true);
        while (SharedState.TryTakeCancelCurrentOp()) { }

        ResourceProfiler.Begin("wd-reset-1");
        ResourceProfiler.RecordWorkingSetSample((long)(1024L * 1024L * 1024L * 0.98));
        Assert.True(ResourceProfiler.WatchdogTripped);
        ResourceProfiler.End("wd-reset-1", emitBench: false);
        while (SharedState.TryTakeCancelCurrentOp()) { }

        ResourceProfiler.Begin("wd-reset-2");
        try
        {
            Assert.False(ResourceProfiler.WatchdogTripped);
            Assert.Equal(0L, ResourceProfiler.LastWatchdogTripWsBytes);
        }
        finally
        {
            ResourceProfiler.End("wd-reset-2", emitBench: false);
        }
    }

    [Fact]
    public void ResourceProfiler_Watchdog_NoOpWhenBudgetIsZero()
    {
        // Maximum resource mode → MemoryBudget_MB = 0 → watchdog is a
        // no-op regardless of sample size.
        ResourceBudgetSettings.ApplySettings(new SessionSettings
        {
            MemoryBudget_MB = 0, ResourceMode = ResourceMode.Maximum, MaxParallelism = 1,
        });
        ResourceProfiler.SetWatchdogEnabled(true);
        while (SharedState.TryTakeCancelCurrentOp()) { }

        ResourceProfiler.Begin("wd-zero-budget");
        try
        {
            // Arbitrary large sample; with zero budget there's nothing
            // to compare against.
            ResourceProfiler.RecordWorkingSetSample(100L * 1024L * 1024L * 1024L);   // 100 GB
            Assert.False(ResourceProfiler.WatchdogTripped);
            Assert.False(SharedState.TryTakeCancelCurrentOp());
        }
        finally
        {
            ResourceProfiler.End("wd-zero-budget", emitBench: false);
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  MemoryProjectionGate.ProjectPreflight
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectPreflight_DefaultDesign_ReturnsPass()
    {
        // Default 2224 N / 0.4 mm voxel / 8 GB budget should project ~20 MB.
        var proj = MemoryProjectionGate.ProjectPreflight(
            new Voxelforge.Optimization.OperatingConditions(),
            new Voxelforge.Optimization.RegenChamberDesign(),
            voxelSize_mm: 0.4,
            budgetBytes:  8L * 1024 * 1024 * 1024);
        Assert.Equal(MemoryProjectionLevel.Pass, proj.Level);
        Assert.True(proj.ProjectedBytes > 0);
        Assert.True(proj.FractionOfBudget < 0.1);
    }

    [Fact]
    public void ProjectPreflight_FineVoxelLargeThrust_FailsTightBudget()
    {
        // 50 kN chamber at 0.1 mm voxel under a 4 GB budget → Fail level,
        // matching the crash scenario the gate was designed around.
        var proj = MemoryProjectionGate.ProjectPreflight(
            new Voxelforge.Optimization.OperatingConditions { Thrust_N = 50000 },
            new Voxelforge.Optimization.RegenChamberDesign(),
            voxelSize_mm: 0.1,
            budgetBytes:  4L * 1024 * 1024 * 1024);
        Assert.Equal(MemoryProjectionLevel.Fail, proj.Level);
    }

    [Fact]
    public void ProjectPreflight_ZeroVoxel_ReturnsPassWithHint()
    {
        var proj = MemoryProjectionGate.ProjectPreflight(
            new Voxelforge.Optimization.OperatingConditions(),
            new Voxelforge.Optimization.RegenChamberDesign(),
            voxelSize_mm: 0.0,
            budgetBytes:  8L * 1024 * 1024 * 1024);
        Assert.Equal(MemoryProjectionLevel.Pass, proj.Level);
        Assert.Contains("No voxel size", proj.Message);
    }

    // ═════════════════════════════════════════════════════════════════
    //  TileLargeBuilds + TileCount settings
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionSettings_TileLargeBuilds_DefaultsFalse()
    {
        var s = new SessionSettings();
        Assert.False(s.TileLargeBuilds);
    }

    [Fact]
    public void SessionSettings_TileCount_DefaultsTo4()
    {
        var s = new SessionSettings();
        Assert.Equal(4, s.TileCount);
    }

    [Fact]
    public void SessionSettings_TileFields_RoundTripThroughJson()
    {
        using var tmp = TestTempFile.Create();
        var outSet = new SessionSettings { TileLargeBuilds = true, TileCount = 7 };
        outSet.Save(tmp.Path);
        var inSet = SessionSettings.Load(tmp.Path);
        Assert.True(inSet.TileLargeBuilds);
        Assert.Equal(7, inSet.TileCount);
    }

    [Fact]
    public void ResourceBudget_ApplySettings_PropagatesTileFlag()
    {
        var on  = new SessionSettings { TileLargeBuilds = true, TileCount = 6 };
        ResourceBudgetSettings.ApplySettings(on);
        Assert.True(ResourceBudget.TileLargeBuilds);
        Assert.Equal(6, ResourceBudget.TileCount);

        var off = new SessionSettings { TileLargeBuilds = false, TileCount = 4 };
        ResourceBudgetSettings.ApplySettings(off);
        Assert.False(ResourceBudget.TileLargeBuilds);
        Assert.Equal(4, ResourceBudget.TileCount);
    }

    [Fact]
    public void ResourceBudget_ApplySettings_ClampsTileCountToValidRange()
    {
        // < 1 → clamped up to 1; > 32 → clamped down to 32. Defence
        // against hand-edited session.json with nonsensical values.
        ResourceBudgetSettings.ApplySettings(new SessionSettings { TileLargeBuilds = true, TileCount = -5 });
        Assert.Equal(1, ResourceBudget.TileCount);

        ResourceBudgetSettings.ApplySettings(new SessionSettings { TileLargeBuilds = true, TileCount = 9999 });
        Assert.Equal(32, ResourceBudget.TileCount);
    }

    [Fact]
    public void Optimizer_SignalMemoryAbort_FlipsMemoryAbortAndIsComplete()
    {
        var bounds = new (double Min, double Max)[] { (0, 10) };
        var opt = new SimulatedAnnealingOptimizer(bounds, maxIterations: 500, seed: 7);
        opt.ReportScore(opt.NextCandidate(), 100.0, null);
        Assert.False(opt.MemoryAbortTripped);
        Assert.False(opt.IsComplete);

        opt.SignalMemoryAbort();

        Assert.True(opt.MemoryAbortTripped);
        Assert.True(opt.ConvergenceReached,
            "SignalMemoryAbort should route through the ConvergenceReached flag.");
        Assert.True(opt.IsComplete);
    }

    // ═════════════════════════════════════════════════════════════════
    //  IsolateLargeBuildsAtFailProjection scaffold
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionSettings_IsolateLargeBuildsAtFailProjection_DefaultsFalse()
    {
        var s = new SessionSettings();
        Assert.False(s.IsolateLargeBuildsAtFailProjection);
    }

    [Fact]
    public void SessionSettings_IsolateLargeBuildsAtFailProjection_RoundTripsThroughJson()
    {
        using var tmp = TestTempFile.Create();
        var outSet = new SessionSettings { IsolateLargeBuildsAtFailProjection = true };
        outSet.Save(tmp.Path);
        var inSet = SessionSettings.Load(tmp.Path);
        Assert.True(inSet.IsolateLargeBuildsAtFailProjection);
    }

    [Fact]
    public void ResourceBudget_ApplySettings_PropagatesIsolateLargeBuilds()
    {
        var on  = new SessionSettings { IsolateLargeBuildsAtFailProjection = true };
        ResourceBudgetSettings.ApplySettings(on);
        Assert.True(ResourceBudget.IsolateLargeBuildsAtFailProjection);

        var off = new SessionSettings { IsolateLargeBuildsAtFailProjection = false };
        ResourceBudgetSettings.ApplySettings(off);
        Assert.False(ResourceBudget.IsolateLargeBuildsAtFailProjection);
    }
}
