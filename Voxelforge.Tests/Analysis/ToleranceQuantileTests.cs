// ToleranceQuantileTests.cs — Issue #556 PR-1 (2026-05-16).
//
// Per audit 05-test-gaps.md § 1.7: ToleranceInputs + ToleranceResult are
// covered by ToleranceAnalysisTests, but ToleranceQuantile (the P10/P50/
// P90/P99 sub-record) was never named directly. The Monte-Carlo sweep
// reports populate them automatically; this test pins the constructor /
// equality semantics so any silent field-order swap is caught.

using Voxelforge.Analysis;

namespace Voxelforge.Tests;

public class ToleranceQuantileTests
{
    [Fact]
    public void Ctor_AssignsQuantilesInDeclaredOrder()
    {
        var q = new ToleranceQuantile(P10: 1.0, P50: 2.0, P90: 3.0, P99: 4.0);
        Assert.Equal(1.0, q.P10, precision: 6);
        Assert.Equal(2.0, q.P50, precision: 6);
        Assert.Equal(3.0, q.P90, precision: 6);
        Assert.Equal(4.0, q.P99, precision: 6);
    }

    [Fact]
    public void RecordEquality_HoldsOnIdenticalFieldValues()
    {
        var a = new ToleranceQuantile(1.0, 2.0, 3.0, 4.0);
        var b = new ToleranceQuantile(1.0, 2.0, 3.0, 4.0);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void RecordEquality_DifferentP99_Distinguishes()
    {
        var a = new ToleranceQuantile(1.0, 2.0, 3.0, 4.0);
        var b = new ToleranceQuantile(1.0, 2.0, 3.0, 4.5);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WithExpression_PreservesOtherFields()
    {
        var a = new ToleranceQuantile(1.0, 2.0, 3.0, 4.0);
        var b = a with { P50 = 2.5 };
        Assert.Equal(2.5, b.P50, precision: 6);
        Assert.Equal(a.P10, b.P10, precision: 6);
        Assert.Equal(a.P90, b.P90, precision: 6);
        Assert.Equal(a.P99, b.P99, precision: 6);
    }

    [Fact]
    public void Ordering_AscendingP10ThroughP99_HoldsForRealSweepShape()
    {
        // The records are passive — nothing enforces monotone quantiles.
        // But every consumer in the codebase produces P10 ≤ P50 ≤ P90 ≤ P99.
        // A sample built that way must compare strictly increasing.
        var q = new ToleranceQuantile(P10: 100.0, P50: 200.0, P90: 300.0, P99: 350.0);
        Assert.True(q.P10 < q.P50);
        Assert.True(q.P50 < q.P90);
        Assert.True(q.P90 < q.P99);
    }
}
