// Phase8UiFollowOnTests.cs — Tier U2 follow-on tests: SA convergence
// plot, tolerance histogram, status history dropdown. Covers the
// pure-data pieces of each feature;
// the paint-path surface is exercised implicitly by instantiating
// the controls in the form during integration runs.
//
// Intentionally avoids instantiating the full WinForms controls where
// the underlying data surface is testable in isolation — matches the
// Phase7UiInfraTests pattern. StatusHistoryBuffer, OptConvergencePanel,
// and ToleranceHistogramPanel each expose enough internal surface for
// this.

using Voxelforge.UI;

namespace Voxelforge.Tests;

public class Phase8UiFollowOnTests
{
    // ═════════════════════════════════════════════════════════════════
    //  U2.10 — StatusHistory rolling buffer
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void StatusHistory_EvictsOldestWhenCapacityReached()
    {
        var buf = new StatusHistoryBuffer(capacity: 3);
        buf.Add("a"); buf.Add("b"); buf.Add("c"); buf.Add("d");
        var snap = buf.Snapshot();
        Assert.Equal(3, snap.Length);
        // Most-recent-first ordering.
        Assert.Equal("d", snap[0].Message);
        Assert.Equal("c", snap[1].Message);
        Assert.Equal("b", snap[2].Message);
        // "a" evicted.
        Assert.DoesNotContain(snap, e => e.Message == "a");
    }

    [Fact]
    public void StatusHistory_NullOrEmptyMessageIsSkipped()
    {
        var buf = new StatusHistoryBuffer();
        buf.Add("");
        buf.Add(null!);
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void StatusHistory_Clear_EmptiesAndFiresChanged()
    {
        var buf = new StatusHistoryBuffer();
        buf.Add("x"); buf.Add("y");
        int changes = 0;
        buf.OnChanged += () => changes++;
        buf.Clear();
        Assert.Equal(0, buf.Count);
        Assert.True(changes >= 1);
    }

    [Theory]
    [InlineData("Error: bad input",                StatusSeverity.Error)]
    [InlineData("Warning: truncated",              StatusSeverity.Error)]
    [InlineData("Failed to save",                  StatusSeverity.Error)]
    [InlineData("\u26a0 hard start risk",          StatusSeverity.Error)]
    [InlineData("Export failed with exit 1",       StatusSeverity.Error)]
    [InlineData("Regenerating\u2026",              StatusSeverity.Progress)]
    [InlineData("Running tolerance sweep...",      StatusSeverity.Progress)]
    [InlineData("Ready.",                          StatusSeverity.Info)]
    [InlineData("STL exported at 0.10 mm \u2192 chamber.stl", StatusSeverity.Info)]
    public void StatusHistory_Classify_CategorisesByPrefixOrEllipsis(string msg, StatusSeverity expected)
    {
        Assert.Equal(expected, StatusHistoryBuffer.Classify(msg));
    }

    [Fact]
    public void StatusHistory_FormatRelativeAge_ProducesStableLabels()
    {
        var now = new DateTime(2026, 4, 27, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("just now",   StatusHistoryBuffer.FormatRelativeAge(now.AddSeconds(-1), now));
        Assert.Equal("just now",   StatusHistoryBuffer.FormatRelativeAge(now,                now));
        Assert.Equal("5s ago",     StatusHistoryBuffer.FormatRelativeAge(now.AddSeconds(-5), now));
        Assert.Equal("1m 5s ago",  StatusHistoryBuffer.FormatRelativeAge(now.AddSeconds(-65), now));
        Assert.Equal("2h ago",     StatusHistoryBuffer.FormatRelativeAge(now.AddHours(-2),   now));
    }

    [Fact]
    public void StatusHistory_Add_FiresChangedEvent()
    {
        var buf = new StatusHistoryBuffer();
        int changes = 0;
        buf.OnChanged += () => changes++;
        buf.Add("hello");
        Assert.Equal(1, changes);
    }

    // ═════════════════════════════════════════════════════════════════
    //  U2.6 — OptConvergencePanel: AppendPoint + dedup + reset
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void OptConvergencePanel_AppendPoint_AccumulatesInOrder()
    {
        var p = new OptConvergencePanel();
        p.AppendPoint(1, 5.0);
        p.AppendPoint(2, 4.0);
        p.AppendPoint(3, 3.5);
        var snap = p.SnapshotForTests();
        Assert.Equal(3, snap.Count);
        Assert.Equal((1, 5.0), snap[0]);
        Assert.Equal((3, 3.5), snap[2]);
    }

    [Fact]
    public void OptConvergencePanel_AppendPoint_SkipsNonFinite()
    {
        var p = new OptConvergencePanel();
        p.AppendPoint(1, double.PositiveInfinity);
        p.AppendPoint(2, double.NaN);
        p.AppendPoint(3, 7.0);
        var snap = p.SnapshotForTests();
        Assert.Single(snap);
        Assert.Equal(3, snap[0].Iteration);
    }

    [Fact]
    public void OptConvergencePanel_AppendPoint_DedupsSameIterationToBestScore()
    {
        var p = new OptConvergencePanel();
        p.AppendPoint(5, 8.0);
        p.AppendPoint(5, 6.5);   // same iter, better score → should update in place
        p.AppendPoint(5, 7.0);   // same iter, worse       → ignored
        var snap = p.SnapshotForTests();
        Assert.Single(snap);
        Assert.Equal((5, 6.5), snap[0]);
    }

    [Fact]
    public void OptConvergencePanel_Reset_ClearsAllPoints()
    {
        var p = new OptConvergencePanel();
        p.AppendPoint(1, 1.0);
        p.AppendPoint(2, 0.5);
        p.Reset();
        Assert.Empty(p.SnapshotForTests());
    }

    // ═════════════════════════════════════════════════════════════════
    //  U2.9 — ToleranceHistogramPanel: binning + ToleranceResult plumbing
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void ToleranceHistogram_Bin_DistributesSamplesAcrossBins()
    {
        var samples = new double[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var counts = ToleranceHistogramPanel.Bin(samples, binCount: 5, min: 0, max: 10);
        Assert.Equal(5, counts.Length);
        // Ten evenly-spaced samples in [0,10) over 5 bins = 2 per bin.
        foreach (var c in counts) Assert.Equal(2, c);
    }

    [Fact]
    public void ToleranceHistogram_Bin_ClampsValuesAtOrAboveMaxIntoLastBin()
    {
        var samples = new double[] { 10, 10, 10 };
        var counts = ToleranceHistogramPanel.Bin(samples, binCount: 4, min: 0, max: 10);
        Assert.Equal(3, counts[^1]);
        Assert.Equal(0, counts[0]);
    }

    [Fact]
    public void ToleranceHistogram_Bin_EmptySamplesYieldsZeroCounts()
    {
        var counts = ToleranceHistogramPanel.Bin(Array.Empty<double>(), 10, 0, 1);
        Assert.Equal(10, counts.Length);
        Assert.All(counts, c => Assert.Equal(0, c));
    }

    [Fact]
    public void ToleranceHistogram_Bin_ZeroRangePutsEverythingInFirstBin()
    {
        // min == max is a degenerate input (all samples identical). Should
        // not throw; all samples land in bin 0.
        var counts = ToleranceHistogramPanel.Bin(new[] { 3.14, 3.14 }, 5, 3.14, 3.14);
        Assert.Equal(2, counts[0]);
        Assert.Equal(0, counts[1]);
    }

    [Fact]
    public void ToleranceResult_CarriesPerSampleArrays_ForHistogramPanel()
    {
        // Defensive: the histogram panel depends on ToleranceAnalysis.Run
        // actually populating Samples_*. A future refactor that forgets
        // to hand the arrays through should trip this test.
        var inp = new Voxelforge.Analysis.ToleranceInputs(SampleCount: 24, RandomSeed: 1);
        var cond    = new Voxelforge.Optimization.OperatingConditions
        {
            PropellantPair = Voxelforge.Combustion.PropellantPair.LOX_CH4,
        };
        var design  = new Voxelforge.Optimization.RegenChamberDesign
        {
            IncludeManifolds       = false,
            IncludePorts           = false,
            IncludeInjectorFlange  = false,
            ContourStationCount    = 40,
        };
        var gas = Voxelforge.Combustion.PropellantTables.Lookup(
            cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = Voxelforge.Optimization.RegenChamberOptimization
            .ComputeDerived(cond, gas, design);
        var contour = Voxelforge.Chamber.ChamberContourGenerator.Generate(
            throatRadius_mm:        derived.ThroatRadius_mm,
            contractionRatio:       design.ContractionRatio,
            expansionRatio:         design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            stationCount:           40);

        var r = Voxelforge.Analysis.ToleranceAnalysis.Run(contour, cond, design, inp);

        Assert.NotNull(r.Samples_PeakWallT_K);
        Assert.NotNull(r.Samples_MinSafetyFactor);
        Assert.NotNull(r.Samples_CoolantPressureDrop_Pa);
        Assert.NotNull(r.Samples_CoolantOutletT_K);
        Assert.NotNull(r.Samples_ThroatHeatFlux_Wm2);
        Assert.Equal(r.SampleCount, r.Samples_PeakWallT_K!.Length);
        Assert.Equal(r.SampleCount, r.Samples_MinSafetyFactor!.Length);
        Assert.Equal(r.SampleCount, r.Samples_CoolantPressureDrop_Pa!.Length);
        // The reported quantiles should be inside the [min, max] of the
        // raw arrays (sanity check that arrays are the same draws that
        // produced the quantiles).
        double mn = r.Samples_PeakWallT_K.Min();
        double mx = r.Samples_PeakWallT_K.Max();
        Assert.InRange(r.PeakWallT_K.P50, mn, mx);
        Assert.InRange(r.PeakWallT_K.P10, mn, mx);
        Assert.InRange(r.PeakWallT_K.P90, mn, mx);
    }
}
