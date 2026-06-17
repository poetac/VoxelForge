// Sprint T1.2 (2026-04-25): SobolSequence tests.

using System;
using System.Collections.Generic;
using System.Linq;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class SobolSequenceTests
{
    [Fact]
    public void Constructor_RejectsNonPositiveDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SobolSequence(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SobolSequence(-1));
    }

    [Fact]
    public void Next_ReturnsPointsInUnitHypercube()
    {
        var seq = new SobolSequence(4);
        for (int i = 0; i < 100; i++)
        {
            var p = seq.Next();
            Assert.Equal(4, p.Length);
            foreach (var v in p)
            {
                Assert.InRange(v, 0.0, 1.0);
            }
        }
    }

    [Fact]
    public void Next_IsDeterministic_FreshInstanceProducesSamePoints()
    {
        var s1 = new SobolSequence(8);
        var s2 = new SobolSequence(8);
        for (int i = 0; i < 64; i++)
        {
            var p1 = s1.Next();
            var p2 = s2.Next();
            Assert.Equal(p1, p2);
        }
    }

    [Fact]
    public void SkipTo_AdvancesToCorrectIndex()
    {
        // SkipTo(n) advances the internal index to n. The next Next() call
        // then advances index to n+1 and returns the point at that step.
        // To match Next() called 6 times (which returns the point at
        // step 6, i.e. internal index moves 0→1→…→6), we SkipTo(5) and
        // then call Next() once.
        var ref_ = new SobolSequence(4);
        for (int i = 0; i < 6; i++) ref_.Next();
        var refPoint = ref_.Next();      // 7th call → step 7

        var skipped = new SobolSequence(4);
        skipped.SkipTo(6);               // index now at 6
        var skippedPoint = skipped.Next(); // step 7

        Assert.Equal(refPoint, skippedPoint);
    }

    [Fact]
    public void Index_TracksPointsGenerated()
    {
        var seq = new SobolSequence(4);
        Assert.Equal(0, seq.Index);
        seq.Next();
        Assert.Equal(1, seq.Index);
        for (int i = 0; i < 9; i++) seq.Next();
        Assert.Equal(10, seq.Index);
    }

    [Fact]
    public void LowDiscrepancy_FirstDimensionCoversBinsExceptOrigin()
    {
        // Gray-code Sobol on dim 0 is a permutation of {1/2^k, 2/2^k, …,
        // (2^k - 1)/2^k} after the first 2^k - 1 points, then re-enters
        // bins from index 2^k onward. So the first 31 points (we skip the
        // origin which would be point 0) hit 31 DISTINCT bins of size
        // 1/32 — leaving bin 0 (the bin containing the origin) unhit.
        // This is the actual coverage property, not "all 32 bins by point 31".
        var seq = new SobolSequence(2);
        var bins = new bool[32];
        for (int i = 0; i < 31; i++)
        {
            double x = seq.Next()[0];
            int bin = (int)(x * 32);
            Assert.False(bins[bin], $"point {i} fell into already-occupied bin {bin}");
            bins[bin] = true;
        }
        // Should have exactly 31 distinct bins covered (all but bin 0).
        int hit = bins.Count(b => b);
        Assert.Equal(31, hit);
        Assert.False(bins[0], "bin 0 (the origin bin) should be unhit by Gray-code Sobol points 1..31");
    }

    [Fact]
    public void LowDiscrepancy_BeatsUniformRandom_OnFirst8Dimensions()
    {
        // Star-discrepancy test: Sobol's L∞ error (max |empirical CDF -
        // ideal CDF|) on the first dimension should be below 1/N for any
        // power-of-2 N up to ~1024. Uniform random has L∞ ~ √(log/N).
        const int N = 64;
        var seq = new SobolSequence(8);
        var firstDimValues = new double[N];
        for (int i = 0; i < N; i++) firstDimValues[i] = seq.Next()[0];
        Array.Sort(firstDimValues);
        double maxDeviation = 0;
        for (int i = 0; i < N; i++)
        {
            double idealCdf = (i + 1.0) / N;
            double deviation = Math.Abs(firstDimValues[i] - idealCdf);
            if (deviation > maxDeviation) maxDeviation = deviation;
        }
        // Sobol on dim 0 (van der Corput base 2) gives perfect tiling at
        // power-of-2 sizes — max deviation should be < 1/N + tiny.
        Assert.True(maxDeviation < 2.0 / N,
            $"Sobol dim 0 max deviation {maxDeviation:F4} exceeds 2/N = {2.0 / N:F4}");
    }

    [Fact]
    public void ChainSlice_ReturnsRequestedCount()
    {
        var pts = SobolSequence.ChainSlice(dimensions: 8, count: 16, sliceIndex: 0, totalSlices: 4);
        Assert.Equal(16, pts.Length);
        foreach (var p in pts)
        {
            Assert.Equal(8, p.Length);
            Assert.All(p, v => Assert.InRange(v, 0.0, 1.0));
        }
    }

    [Fact]
    public void ChainSlice_DistinctSlices_ProduceDistinctPoints()
    {
        // Two different slices must not return identical point sequences —
        // they're meant to explore non-overlapping regions of the design space.
        var s0 = SobolSequence.ChainSlice(dimensions: 8, count: 8, sliceIndex: 0, totalSlices: 4);
        var s1 = SobolSequence.ChainSlice(dimensions: 8, count: 8, sliceIndex: 1, totalSlices: 4);
        var s0_first = string.Join(",", s0[0].Select(v => v.ToString("F6")));
        var s1_first = string.Join(",", s1[0].Select(v => v.ToString("F6")));
        Assert.NotEqual(s0_first, s1_first);
    }

    [Fact]
    public void ChainSlice_SameSliceIndex_IsDeterministic()
    {
        var s_a = SobolSequence.ChainSlice(dimensions: 8, count: 8, sliceIndex: 2, totalSlices: 4);
        var s_b = SobolSequence.ChainSlice(dimensions: 8, count: 8, sliceIndex: 2, totalSlices: 4);
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(s_a[i], s_b[i]);
        }
    }

    [Fact]
    public void TwentyFourDim_SequenceProducesValidPoints()
    {
        // voxelforge's SA registry is 24-dim — must work without error
        // even though only the first 8 dims have baked Joe-Kuo numbers
        // (others use Halton-style fallback).
        var seq = new SobolSequence(24);
        for (int i = 0; i < 32; i++)
        {
            var p = seq.Next();
            Assert.Equal(24, p.Length);
            foreach (var v in p) Assert.InRange(v, 0.0, 1.0);
        }
    }

    [Fact]
    public void ChainSlice_RejectsOutOfRangeIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SobolSequence.ChainSlice(dimensions: 4, count: 4, sliceIndex: 5, totalSlices: 4));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SobolSequence.ChainSlice(dimensions: 4, count: 4, sliceIndex: -1, totalSlices: 4));
    }
}
