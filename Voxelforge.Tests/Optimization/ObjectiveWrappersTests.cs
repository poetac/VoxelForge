// ObjectiveWrappersTests.cs — tests for CachedObjective + TeeObjective
// + BoundedObjective.
//
// Pins:
//   • CachedObjective: cache hit on repeat vector, miss on novel vector;
//     hit/miss counters track correctly; Reset clears.
//   • TeeObjective: every evaluation lands in Log; Log is a defensive
//     snapshot; Reset clears.
//   • BoundedObjective: out-of-bounds vector is clamped before dispatch;
//     in-bounds vector passes through unchanged.
//   • Composition: all three wrappers preserve DimensionCount + Variables.
//   • Null-argument guards.

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Optimization;

public sealed class ObjectiveWrappersTests
{
    /// <summary>
    /// Stateful inner objective whose score is the sum of vector entries
    /// + a call count it advertises. Used to verify caching:
    /// `EvaluationResult.Score` for a fixed vector should be identical
    /// on cached calls, even though the underlying state advanced.
    /// </summary>
    private sealed class CountingObjective : IObjective
    {
        public int CallCount { get; private set; }
        public int DimensionCount { get; } = 2;
        public IReadOnlyList<DesignVariableInfo> Variables { get; } = new[]
        {
            new DesignVariableInfo("x0", 0.0, 1.0),
            new DesignVariableInfo("x1", 0.0, 1.0),
        };
        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            CallCount++;
            return new EvaluationResult(
                Score:                   vector[0] + vector[1],
                Violations:              Array.Empty<FeasibilityViolation>(),
                EngineSpecificBreakdown: null);
        }
    }

    // ── CachedObjective ────────────────────────────────────────────────

    [Fact]
    public void Cached_RepeatVector_HitsCache_AndDoesNotReinvokeInner()
    {
        var inner = new CountingObjective();
        var cached = new CachedObjective(inner);
        var v = new[] { 0.5, 0.5 };

        var r1 = cached.Evaluate(v);
        var r2 = cached.Evaluate(v);
        var r3 = cached.Evaluate(v);

        Assert.Equal(1, inner.CallCount);  // only one inner call
        Assert.Equal(r1.Score, r2.Score);
        Assert.Equal(r2.Score, r3.Score);
        Assert.Equal(2, cached.HitCount);
        Assert.Equal(1, cached.MissCount);
    }

    [Fact]
    public void Cached_NovelVector_Misses()
    {
        var inner = new CountingObjective();
        var cached = new CachedObjective(inner);
        cached.Evaluate(new[] { 0.1, 0.1 });
        cached.Evaluate(new[] { 0.2, 0.2 });
        cached.Evaluate(new[] { 0.3, 0.3 });

        Assert.Equal(3, inner.CallCount);
        Assert.Equal(0, cached.HitCount);
        Assert.Equal(3, cached.MissCount);
    }

    [Fact]
    public void Cached_Reset_ClearsHistory()
    {
        var inner = new CountingObjective();
        var cached = new CachedObjective(inner);
        cached.Evaluate(new[] { 0.5, 0.5 });
        cached.Evaluate(new[] { 0.5, 0.5 });
        cached.Reset();

        Assert.Equal(0, cached.HitCount);
        Assert.Equal(0, cached.MissCount);

        cached.Evaluate(new[] { 0.5, 0.5 });
        Assert.Equal(0, cached.HitCount);
        Assert.Equal(1, cached.MissCount);
        Assert.Equal(2, inner.CallCount);  // 1 pre-reset + 1 post-reset
    }

    [Fact]
    public void Cached_RejectsWrongVectorLength()
    {
        var inner = new CountingObjective();
        var cached = new CachedObjective(inner);
        Assert.Throws<ArgumentException>(() => cached.Evaluate(new[] { 0.5 }));
    }

    [Fact]
    public void Cached_RejectsNullInner()
    {
        Assert.Throws<ArgumentNullException>(() => new CachedObjective(null!));
    }

    // ── TeeObjective ───────────────────────────────────────────────────

    [Fact]
    public void Tee_LogsEveryEvaluation()
    {
        var inner = new CountingObjective();
        var tee = new TeeObjective(inner);
        tee.Evaluate(new[] { 0.1, 0.2 });
        tee.Evaluate(new[] { 0.3, 0.4 });
        tee.Evaluate(new[] { 0.5, 0.6 });

        var log = tee.Log;
        Assert.Equal(3, log.Count);
        Assert.Equal(0.1 + 0.2, log[0].Result.Score, precision: 9);
        Assert.Equal(0.3 + 0.4, log[1].Result.Score, precision: 9);
        Assert.Equal(0.5 + 0.6, log[2].Result.Score, precision: 9);
    }

    [Fact]
    public void Tee_VectorRecord_IsDefensiveCopy()
    {
        var inner = new CountingObjective();
        var tee = new TeeObjective(inner);
        var v = new[] { 0.5, 0.5 };
        tee.Evaluate(v);
        v[0] = 999.0;  // mutate after the call
        Assert.Equal(0.5, tee.Log[0].Vector[0]);
    }

    [Fact]
    public void Tee_Reset_ClearsLog()
    {
        var inner = new CountingObjective();
        var tee = new TeeObjective(inner);
        tee.Evaluate(new[] { 0.1, 0.1 });
        tee.Evaluate(new[] { 0.2, 0.2 });
        tee.Reset();
        Assert.Empty(tee.Log);
    }

    [Fact]
    public void Tee_RejectsNullInner()
    {
        Assert.Throws<ArgumentNullException>(() => new TeeObjective(null!));
    }

    // ── BoundedObjective ───────────────────────────────────────────────

    [Fact]
    public void Bounded_InRangeVector_PassesThroughUnchanged()
    {
        var inner = new CountingObjective();   // bounds [0, 1] per dim
        var bounded = new BoundedObjective(inner);
        var v = new[] { 0.5, 0.7 };
        var r = bounded.Evaluate(v);
        Assert.Equal(1.2, r.Score, precision: 9);
    }

    [Fact]
    public void Bounded_OutOfRangeHigh_ClampsToMax()
    {
        var inner = new CountingObjective();   // bounds [0, 1] per dim
        var bounded = new BoundedObjective(inner);
        var v = new[] { 1.5, 2.0 };
        var r = bounded.Evaluate(v);
        // Clamped to [1, 1]; sum = 2.0.
        Assert.Equal(2.0, r.Score, precision: 9);
    }

    [Fact]
    public void Bounded_OutOfRangeLow_ClampsToMin()
    {
        var inner = new CountingObjective();   // bounds [0, 1] per dim
        var bounded = new BoundedObjective(inner);
        var v = new[] { -0.5, -1.0 };
        var r = bounded.Evaluate(v);
        // Clamped to [0, 0]; sum = 0.
        Assert.Equal(0.0, r.Score, precision: 9);
    }

    [Fact]
    public void Bounded_MixedRange_ClampsPerDimensionIndependently()
    {
        var inner = new CountingObjective();
        var bounded = new BoundedObjective(inner);
        var v = new[] { -0.5, 1.5 };   // one under, one over
        var r = bounded.Evaluate(v);
        // Clamped to [0, 1]; sum = 1.
        Assert.Equal(1.0, r.Score, precision: 9);
    }

    [Fact]
    public void Bounded_RejectsWrongVectorLength()
    {
        var inner = new CountingObjective();
        var bounded = new BoundedObjective(inner);
        Assert.Throws<ArgumentException>(() => bounded.Evaluate(new[] { 0.5 }));
    }

    [Fact]
    public void Bounded_RejectsNullInner()
    {
        Assert.Throws<ArgumentNullException>(() => new BoundedObjective(null!));
    }

    // ── Composition ────────────────────────────────────────────────────

    [Fact]
    public void Composition_AllWrappersPreserveDimensionCountAndVariables()
    {
        var inner = new CountingObjective();
        var bounded = new BoundedObjective(inner);
        var cached  = new CachedObjective(bounded);
        var tee     = new TeeObjective(cached);

        Assert.Equal(2, tee.DimensionCount);
        Assert.Equal(inner.Variables, tee.Variables);
    }

    [Fact]
    public void Composition_Bounded_Cached_Tee_TogetherWork()
    {
        // Compose all three. Same vector twice → tee should log 2
        // records, cached should hit once, bounded should clamp.
        var inner = new CountingObjective();
        var bounded = new BoundedObjective(inner);
        var cached  = new CachedObjective(bounded);
        var tee     = new TeeObjective(cached);

        var oob = new[] { 1.5, 1.5 };       // bounded clamps to [1,1] → score 2
        tee.Evaluate(oob);
        tee.Evaluate(oob);                  // cached hit (same input, same clamped vector)

        // Vector that lives at the upper bound also clamps to [1,1] — but
        // the CachedObjective hashes the ORIGINAL vector before bounded
        // sees it. Two different OOB vectors → two cache misses; identical
        // OOB vectors → one hit. We sent the same vector twice, so:
        Assert.Equal(2, tee.Log.Count);          // tee always logs every call
        Assert.Equal(1, inner.CallCount);        // inner only called once
        Assert.Equal(1, cached.HitCount);
        Assert.Equal(1, cached.MissCount);
    }

    // ── Deterministic invariants ──────────────────────────────────────

    [Fact]
    public void Cached_Determinism_AcrossWrappingLayers()
    {
        // The wrapper layer must not perturb the inner score across runs.
        var inner1 = new CountingObjective();
        var cached1 = new CachedObjective(inner1);
        var inner2 = new CountingObjective();
        var cached2 = new CachedObjective(inner2);

        var v = new[] { 0.42, 0.17 };
        var r1 = cached1.Evaluate(v);
        var r2 = cached2.Evaluate(v);
        Assert.Equal(r1.Score, r2.Score);
    }

    // ── GradientProbe ──────────────────────────────────────────────────

    /// <summary>
    /// Quadratic objective f(x) = x0² + x1² with known analytical
    /// gradient ∇f = (2 x0, 2 x1). Useful for verifying finite-difference
    /// accuracy.
    /// </summary>
    private sealed class QuadraticObjective : IObjective
    {
        public int DimensionCount { get; } = 2;
        public IReadOnlyList<DesignVariableInfo> Variables { get; } = new[]
        {
            new DesignVariableInfo("x0", -1.0, 1.0),
            new DesignVariableInfo("x1", -1.0, 1.0),
        };
        public EvaluationResult Evaluate(ReadOnlySpan<double> v, CancellationToken ct = default)
            => new(
                Score:                   v[0] * v[0] + v[1] * v[1],
                Violations:              Array.Empty<FeasibilityViolation>(),
                EngineSpecificBreakdown: null);
    }

    [Fact]
    public void Gradient_Central_MatchesAnalyticalQuadratic()
    {
        // ∇(x² + y²) at (0.5, 0.3) = (1.0, 0.6).
        var inner = new QuadraticObjective();
        var probe = new GradientProbe(inner);
        var g = probe.ComputeGradient(new[] { 0.5, 0.3 });
        Assert.Equal(1.0, g[0], precision: 5);
        Assert.Equal(0.6, g[1], precision: 5);
    }

    [Fact]
    public void Gradient_ForwardDifference_LessAccurateButStillCorrect()
    {
        var inner = new QuadraticObjective();
        var probe = new GradientProbe(inner, relativeStep: 1e-4);
        var g = probe.ComputeGradientForward(new[] { 0.5, 0.3 });
        // Forward differences are O(ε) — looser tolerance vs central O(ε²).
        Assert.Equal(1.0, g[0], precision: 3);
        Assert.Equal(0.6, g[1], precision: 3);
    }

    [Fact]
    public void Gradient_RejectsNullInner()
    {
        Assert.Throws<ArgumentNullException>(() => new GradientProbe(null!));
    }

    [Fact]
    public void Gradient_RejectsNonPositiveStep()
    {
        var inner = new QuadraticObjective();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GradientProbe(inner, relativeStep: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GradientProbe(inner, relativeStep: -1e-6));
    }

    [Fact]
    public void Gradient_RejectsWrongPointLength()
    {
        var inner = new QuadraticObjective();
        var probe = new GradientProbe(inner);
        Assert.Throws<ArgumentException>(() => probe.ComputeGradient(new[] { 0.5 }));
    }

    // ── SubsamplingObjective ──────────────────────────────────────────

    [Fact]
    public void Subsampling_Median_OfFiveLocallySimilarScores()
    {
        // Quadratic at origin: f(0,0) = 0.
        // SubsamplingObjective steps are range-relative: step = relativeStep ·
        // (Max − Min). The QuadraticObjective has variable range 2.0 on each
        // dim, default relativeStep = 1e-3, so step = 2e-3 and neighbour
        // values land at ±2e-3 → f = (2e-3)² = 4e-6 each.
        // Median of {0, 4e-6, 4e-6, 4e-6, 4e-6} = 4e-6.
        var inner = new QuadraticObjective();
        var sub = new SubsamplingObjective(inner, neighbourCount: 2);
        var r = sub.Evaluate(new[] { 0.0, 0.0 });
        Assert.True(Math.Abs(r.Score - 4e-6) < 1e-8,
            $"Subsampling median should be near 4e-6; got {r.Score:E3}");
    }

    [Fact]
    public void Subsampling_DispatchCountMatchesFormula()
    {
        // 1 + 2 · neighbourCount inner evaluations per call.
        var inner = new CountingObjective();
        var sub = new SubsamplingObjective(inner, neighbourCount: 2);
        sub.Evaluate(new[] { 0.5, 0.5 });
        Assert.Equal(1 + 2 * 2, inner.CallCount);

        var sub3 = new SubsamplingObjective(new CountingObjective(), neighbourCount: 2);
        // Re-test with fresh inner.
        var inner2 = new CountingObjective();
        var sub2 = new SubsamplingObjective(inner2, neighbourCount: 1);
        sub2.Evaluate(new[] { 0.5, 0.5 });
        Assert.Equal(1 + 2 * 1, inner2.CallCount);
    }

    [Fact]
    public void Subsampling_RejectsBadNeighbourCount()
    {
        var inner = new QuadraticObjective();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SubsamplingObjective(inner, neighbourCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SubsamplingObjective(inner, neighbourCount: 9));
    }

    [Fact]
    public void Subsampling_RejectsBadRelativeStep()
    {
        var inner = new QuadraticObjective();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SubsamplingObjective(inner, relativeStep: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SubsamplingObjective(inner, relativeStep: -1e-3));
    }

    [Fact]
    public void Subsampling_RejectsNullInner()
    {
        Assert.Throws<ArgumentNullException>(() => new SubsamplingObjective(null!));
    }

    [Fact]
    public void Subsampling_DimensionCountAndVariables_DelegateToInner()
    {
        var inner = new QuadraticObjective();
        var sub = new SubsamplingObjective(inner);
        Assert.Equal(2, sub.DimensionCount);
        Assert.Same(inner.Variables, sub.Variables);
    }

    [Fact]
    public void Subsampling_NeighbourCountClampedToDimension()
    {
        // neighbourCount > DimensionCount must auto-clamp.
        var inner = new QuadraticObjective();   // dim = 2
        var sub = new SubsamplingObjective(inner, neighbourCount: 5);  // clamped → 2
        var counting = new CountingObjective();
        var clampedSub = new SubsamplingObjective(counting, neighbourCount: 5);
        clampedSub.Evaluate(new[] { 0.5, 0.5 });
        // Should make 1 + 2 · min(5, 2) = 5 calls.
        Assert.Equal(5, counting.CallCount);
    }

    // ── TimeoutObjective ───────────────────────────────────────────────

    private sealed class SlowObjective : IObjective
    {
        private readonly TimeSpan _delay;

        public SlowObjective(TimeSpan delay) { _delay = delay; }

        public int DimensionCount { get; } = 1;
        public IReadOnlyList<DesignVariableInfo> Variables { get; } = new[]
        {
            new DesignVariableInfo("x", 0.0, 1.0),
        };

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            // Poll cancellation in 10 ms slices so timeout can actually fire.
            var deadline = DateTime.UtcNow + _delay;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(10);
            }
            return new EvaluationResult(
                Score:                   vector[0],
                Violations:              Array.Empty<FeasibilityViolation>(),
                EngineSpecificBreakdown: null);
        }
    }

    [Fact]
    public void Timeout_FastEvaluation_PassesThrough()
    {
        var inner = new SlowObjective(TimeSpan.FromMilliseconds(50));
        var timed = new TimeoutObjective(inner, TimeSpan.FromMilliseconds(500));
        var r = timed.Evaluate(new[] { 0.5 });
        Assert.Equal(0.5, r.Score, precision: 9);
        Assert.Empty(r.Violations);
    }

    [Fact]
    public void Timeout_SlowEvaluation_ReturnsInfeasibleWithSyntheticViolation()
    {
        var inner = new SlowObjective(TimeSpan.FromSeconds(2));
        var timed = new TimeoutObjective(inner, TimeSpan.FromMilliseconds(50));
        var r = timed.Evaluate(new[] { 0.5 });
        Assert.True(double.IsPositiveInfinity(r.Score));
        Assert.Contains(r.Violations,
            v => v.ConstraintId == "OBJECTIVE_EVALUATION_TIMEOUT");
    }

    [Fact]
    public void Timeout_HonoursCustomInfeasibleScore()
    {
        var inner = new SlowObjective(TimeSpan.FromSeconds(2));
        var timed = new TimeoutObjective(inner, TimeSpan.FromMilliseconds(50),
            infeasibleScore: 1e9);
        var r = timed.Evaluate(new[] { 0.5 });
        Assert.Equal(1e9, r.Score);
    }

    [Fact]
    public void Timeout_RejectsNullInner()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TimeoutObjective(null!, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Timeout_RejectsNonPositiveTimeout()
    {
        var inner = new QuadraticObjective();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TimeoutObjective(inner, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TimeoutObjective(inner, TimeSpan.FromSeconds(-1)));
    }

    // ── RetryingObjective ──────────────────────────────────────────────

    private sealed class TransientFailureObjective : IObjective
    {
        private readonly int _failuresBeforeSuccess;
        public int CallCount { get; private set; }

        public TransientFailureObjective(int failuresBeforeSuccess)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public int DimensionCount { get; } = 1;
        public IReadOnlyList<DesignVariableInfo> Variables { get; } = new[]
        {
            new DesignVariableInfo("x", 0.0, 1.0),
        };

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            CallCount++;
            if (CallCount <= _failuresBeforeSuccess)
                throw new InvalidOperationException(
                    $"Synthetic transient failure on attempt {CallCount}.");
            return new EvaluationResult(
                Score:                   vector[0],
                Violations:              Array.Empty<FeasibilityViolation>(),
                EngineSpecificBreakdown: null);
        }
    }

    [Fact]
    public void Retry_TransientFailure_RecoversAfterRetries()
    {
        var inner = new TransientFailureObjective(failuresBeforeSuccess: 2);
        var retrying = new RetryingObjective(inner, maxRetries: 3);
        var r = retrying.Evaluate(new[] { 0.5 });
        Assert.Equal(0.5, r.Score, precision: 9);
        Assert.Equal(3, inner.CallCount);   // 2 failures + 1 success
    }

    [Fact]
    public void Retry_ExhaustedAttempts_ThrowsWithInnerException()
    {
        var inner = new TransientFailureObjective(failuresBeforeSuccess: 100);
        var retrying = new RetryingObjective(inner, maxRetries: 2);
        var ex = Assert.Throws<InvalidOperationException>(
            () => retrying.Evaluate(new[] { 0.5 }));
        Assert.Contains("3 attempts", ex.Message);   // 1 initial + 2 retries
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void Retry_ArgumentException_NotRetried()
    {
        // Inner that always throws ArgumentException. Retry should NOT
        // re-invoke; the exception surfaces immediately.
        var inner = new SyntheticInnerThatThrows<ArgumentException>();
        var retrying = new RetryingObjective(inner, maxRetries: 5);
        Assert.Throws<ArgumentException>(() => retrying.Evaluate(new[] { 0.5 }));
        Assert.Equal(1, inner.CallCount);   // no retries
    }

    [Fact]
    public void Retry_RejectsNullInner()
    {
        Assert.Throws<ArgumentNullException>(
            () => new RetryingObjective(null!));
    }

    [Fact]
    public void Retry_RejectsBadMaxRetries()
    {
        var inner = new QuadraticObjective();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RetryingObjective(inner, maxRetries: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RetryingObjective(inner, maxRetries: 11));
    }

    private sealed class SyntheticInnerThatThrows<TException> : IObjective
        where TException : Exception, new()
    {
        public int CallCount { get; private set; }
        public int DimensionCount { get; } = 1;
        public IReadOnlyList<DesignVariableInfo> Variables { get; } = new[]
        {
            new DesignVariableInfo("x", 0.0, 1.0),
        };
        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            CallCount++;
            throw new TException();
        }
    }

    // ── MaximizeAdapter ────────────────────────────────────────────────

    [Fact]
    public void Maximize_NegatesInnerScore_OnFeasible()
    {
        var inner = new QuadraticObjective();   // x0² + x1²
        var maxAdapter = new MaximizeAdapter(inner);
        var r = maxAdapter.Evaluate(new[] { 0.5, 0.3 });
        // Inner score 0.34; wrapped score -0.34.
        Assert.Equal(-0.34, r.Score, precision: 9);
    }

    [Fact]
    public void Maximize_PreservesInfeasibility()
    {
        var alwaysInf = new InlineObjective(
            score: double.PositiveInfinity,
            violations: Array.Empty<FeasibilityViolation>(),
            breakdown: null);
        var maxAdapter = new MaximizeAdapter(alwaysInf);
        var r = maxAdapter.Evaluate(new[] { 0.5 });
        Assert.True(double.IsPositiveInfinity(r.Score),
            "Infeasible inner must remain infeasible after sign-flip — never -∞.");
    }

    [Fact]
    public void Maximize_RoutesNaNInner_ToInfeasibleScore_NotNegativeNaN()
    {
        // Without the audit-hardening fix, NaN inner would negate to -NaN
        // and propagate downstream, silently corrupting any caller doing
        // float comparisons. The fix routes !IsFinite uniformly to the
        // configured infeasibleScore.
        var alwaysNaN = new InlineObjective(
            score: double.NaN,
            violations: Array.Empty<FeasibilityViolation>(),
            breakdown: null);
        var maxAdapter = new MaximizeAdapter(alwaysNaN);
        var r = maxAdapter.Evaluate(new[] { 0.5 });
        Assert.True(double.IsPositiveInfinity(r.Score),
            $"NaN inner must route to InfeasibleScore not negate to -NaN "
          + $"(got {r.Score})");
        Assert.False(double.IsNaN(r.Score), "NaN must NOT leak through.");
    }

    [Fact]
    public void Maximize_HonoursCustomInfeasibleScore()
    {
        var alwaysInf = new InlineObjective(
            score: double.PositiveInfinity,
            violations: Array.Empty<FeasibilityViolation>(),
            breakdown: null);
        var maxAdapter = new MaximizeAdapter(alwaysInf, infeasibleScore: 1e9);
        var r = maxAdapter.Evaluate(new[] { 0.5 });
        Assert.Equal(1e9, r.Score);
    }

    [Fact]
    public void Maximize_RejectsNullInner()
    {
        Assert.Throws<ArgumentNullException>(() => new MaximizeAdapter(null!));
    }

    [Fact]
    public void Maximize_DelegatesVariablesAndDimensionCount()
    {
        var inner = new QuadraticObjective();
        var maxAdapter = new MaximizeAdapter(inner);
        Assert.Equal(2, maxAdapter.DimensionCount);
        Assert.Same(inner.Variables, maxAdapter.Variables);
    }

    // ── CompositeCostObjective ─────────────────────────────────────────

    [Fact]
    public void Composite_SumsExtractors_OnFeasible()
    {
        // Inner returns feasible score 100; breakdown carries
        // three component costs we want summed.
        var inner = new InlineObjective(
            score: 100.0,
            violations: Array.Empty<FeasibilityViolation>(),
            breakdown: (engine: 500.0, tank: 200.0, payload: 100.0));
        var composite = new CompositeCostObjective(inner,
            extractors: new Func<object?, double>[]
            {
                b => (((double engine, double tank, double payload))b!).engine,
                b => (((double engine, double tank, double payload))b!).tank,
                b => (((double engine, double tank, double payload))b!).payload,
            });

        var r = composite.Evaluate(new[] { 0.5 });
        Assert.Equal(800.0, r.Score, precision: 9);     // 500 + 200 + 100
        Assert.Equal(3, composite.ExtractorCount);
    }

    [Fact]
    public void Composite_RoutesInfeasibleThroughInnerViolations()
    {
        var inner = new InlineObjective(
            score: 0.0,
            violations: new[]
            {
                new FeasibilityViolation("X_FAIL", "synthetic", ActualValue: 0, Limit: 1),
            },
            breakdown: null);
        var composite = new CompositeCostObjective(inner,
            extractors: new Func<object?, double>[] { _ => 1.0 });
        var r = composite.Evaluate(new[] { 0.5 });
        Assert.True(double.IsPositiveInfinity(r.Score));
    }

    [Fact]
    public void Composite_RoutesInfeasibleOnInfinityScore()
    {
        var inner = new InlineObjective(
            score: double.PositiveInfinity,
            violations: Array.Empty<FeasibilityViolation>(),
            breakdown: null);
        var composite = new CompositeCostObjective(inner,
            extractors: new Func<object?, double>[] { _ => 1.0 });
        var r = composite.Evaluate(new[] { 0.5 });
        Assert.True(double.IsPositiveInfinity(r.Score));
    }

    [Fact]
    public void Composite_RoutesInfeasibleOnNaNInnerScore()
    {
        // NaN inner score path. Audit-hardening companion to the +∞ test
        // above. Before commit 2c15ad7 the routing used IsPositiveInfinity
        // and NaN would have hit the summing branch where the extractors'
        // doubles would sum cleanly while the route-decision was based on
        // a stale check.
        var inner = new InlineObjective(
            score: double.NaN,
            violations: Array.Empty<FeasibilityViolation>(),
            breakdown: null);
        var composite = new CompositeCostObjective(inner,
            extractors: new Func<object?, double>[] { _ => 1.0 });
        var r = composite.Evaluate(new[] { 0.5 });
        Assert.True(double.IsPositiveInfinity(r.Score),
            $"NaN inner must route to InfeasibleScore (got {r.Score})");
    }

    [Fact]
    public void Composite_RejectsEmptyExtractorList()
    {
        var inner = new QuadraticObjective();
        Assert.Throws<ArgumentException>(
            () => new CompositeCostObjective(inner,
                extractors: Array.Empty<Func<object?, double>>()));
    }

    [Fact]
    public void Composite_RejectsNullExtractor()
    {
        var inner = new QuadraticObjective();
        Assert.Throws<ArgumentException>(
            () => new CompositeCostObjective(inner,
                extractors: new Func<object?, double>?[] { _ => 1.0, null }!));
    }

    [Fact]
    public void Composite_RejectsNullInner()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CompositeCostObjective(null!,
                extractors: new Func<object?, double>[] { _ => 1.0 }));
    }

    [Fact]
    public void Composite_DelegatesVariablesAndDimensionCount()
    {
        var inner = new QuadraticObjective();
        var composite = new CompositeCostObjective(inner,
            extractors: new Func<object?, double>[] { _ => 1.0 });
        Assert.Equal(2, composite.DimensionCount);
        Assert.Same(inner.Variables, composite.Variables);
    }

    // ── NormalizingObjective ───────────────────────────────────────────

    /// <summary>
    /// Inner objective returning preset scores in sequence — useful for
    /// pinning the warmup behaviour of NormalizingObjective deterministically.
    /// </summary>
    private sealed class SequenceObjective : IObjective
    {
        private readonly double[] _scores;
        private int _idx;
        public int DimensionCount { get; } = 1;
        public IReadOnlyList<DesignVariableInfo> Variables { get; } = new[]
        {
            new DesignVariableInfo("x", 0.0, 1.0),
        };
        public SequenceObjective(params double[] scores) { _scores = scores; }
        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            double s = _scores[_idx % _scores.Length];
            _idx++;
            return new EvaluationResult(
                Score:                   s,
                Violations:              Array.Empty<FeasibilityViolation>(),
                EngineSpecificBreakdown: null);
        }
    }

    [Fact]
    public void Normalising_DuringWarmup_PassesThroughRawScore()
    {
        var inner = new SequenceObjective(100, 200, 300, 400, 500);
        var norm = new NormalizingObjective(inner, warmupSamples: 4);

        // First 4 calls (warmup) — raw scores pass through.
        Assert.Equal(100.0, norm.Evaluate(new[] { 0.5 }).Score, precision: 9);
        Assert.Equal(200.0, norm.Evaluate(new[] { 0.5 }).Score, precision: 9);
        Assert.Equal(300.0, norm.Evaluate(new[] { 0.5 }).Score, precision: 9);

        // The fourth call activates normalisation (count reaches warmupSamples).
        // After 4 samples [100, 200, 300, 400] the mean is 250, std is ~129.1.
        // z-score for 400 = (400-250)/129.1 ≈ 1.162.
        var fourth = norm.Evaluate(new[] { 0.5 });
        Assert.InRange(fourth.Score, 1.0, 1.3);
    }

    [Fact]
    public void Normalising_AfterWarmup_EmitsZScores()
    {
        // Build up a known distribution then verify normalisation accuracy.
        // Scores: 10, 20, 30, 40, 50. Mean = 30, std = sqrt(250) ≈ 15.81.
        var inner = new SequenceObjective(10, 20, 30, 40, 50);
        var norm = new NormalizingObjective(inner, warmupSamples: 2);

        for (int i = 0; i < 5; i++) norm.Evaluate(new[] { 0.5 });

        // After 5 evaluations the stats should reflect the full sequence.
        Assert.Equal(5, norm.SampleCount);
        Assert.Equal(30.0, norm.RunningMean, precision: 6);
        Assert.InRange(norm.RunningStdDev, 15.5, 16.0);
    }

    [Fact]
    public void Normalising_InfeasibleInner_RoutesToInfeasibleScore_AndSkipsStats()
    {
        var alwaysInf = new InlineObjective(
            score: double.PositiveInfinity,
            violations: Array.Empty<FeasibilityViolation>(),
            breakdown: null);
        var norm = new NormalizingObjective(alwaysInf, warmupSamples: 4);
        var r = norm.Evaluate(new[] { 0.5 });
        Assert.True(double.IsPositiveInfinity(r.Score));
        // Stats must NOT advance on infeasible calls.
        Assert.Equal(0, norm.SampleCount);
    }

    [Fact]
    public void Normalising_Reset_ClearsStats()
    {
        var inner = new SequenceObjective(10, 20, 30, 40);
        var norm = new NormalizingObjective(inner, warmupSamples: 4);
        norm.Evaluate(new[] { 0.5 });
        norm.Evaluate(new[] { 0.5 });
        Assert.Equal(2, norm.SampleCount);

        norm.Reset();
        Assert.Equal(0, norm.SampleCount);
        Assert.True(double.IsNaN(norm.RunningMean));
        Assert.True(double.IsNaN(norm.RunningStdDev));
    }

    [Fact]
    public void Normalising_RejectsBadWarmup()
    {
        var inner = new QuadraticObjective();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NormalizingObjective(inner, warmupSamples: 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NormalizingObjective(inner, warmupSamples: 0));
    }

    [Fact]
    public void Normalising_RejectsNullInner()
    {
        Assert.Throws<ArgumentNullException>(
            () => new NormalizingObjective(null!));
    }

    [Fact]
    public void Normalising_DelegatesVariablesAndDimensionCount()
    {
        var inner = new QuadraticObjective();
        var norm = new NormalizingObjective(inner);
        Assert.Equal(2, norm.DimensionCount);
        Assert.Same(inner.Variables, norm.Variables);
    }

    [Fact]
    public void Normalising_ConstantInnerScore_DoesNotDivideByZero()
    {
        // Pathological case: every inner score is the same. Std = 0;
        // the wrapper guards via Math.Max(std, 1e-9) so the z-score
        // doesn't NaN.
        var inner = new SequenceObjective(42, 42, 42, 42, 42);
        var norm = new NormalizingObjective(inner, warmupSamples: 2);
        for (int i = 0; i < 5; i++)
        {
            var r = norm.Evaluate(new[] { 0.5 });
            Assert.True(double.IsFinite(r.Score),
                $"Constant inner score should not produce NaN/Inf z-score; got {r.Score}");
        }
    }

    [Fact]
    public void Normalising_NaNInnerScore_RoutesToInfeasible_NoStatsCorruption()
    {
        // Audit fix (2026-05-13): NaN inner scores would otherwise
        // propagate into Welford _mean / _m2 and corrupt every
        // subsequent z-score. The wrapper now routes non-finite scores
        // through InfeasibleScore without updating running stats.
        var nanInner = new InlineObjective(
            score: double.NaN,
            violations: Array.Empty<FeasibilityViolation>(),
            breakdown: null);
        var norm = new NormalizingObjective(nanInner, warmupSamples: 2);
        var r = norm.Evaluate(new[] { 0.5 });
        Assert.True(double.IsPositiveInfinity(r.Score));
        Assert.Equal(0, norm.SampleCount);     // stats stayed clean
    }

    [Fact]
    public void Normalising_NegInfInnerScore_RoutesToInfeasible()
    {
        // -Infinity scores (e.g. a divergent solver path returning -∞)
        // are also non-finite and must be routed to infeasibleScore
        // without polluting the running mean.
        var negInfInner = new InlineObjective(
            score: double.NegativeInfinity,
            violations: Array.Empty<FeasibilityViolation>(),
            breakdown: null);
        var norm = new NormalizingObjective(negInfInner);
        var r = norm.Evaluate(new[] { 0.5 });
        Assert.True(double.IsPositiveInfinity(r.Score));
        Assert.Equal(0, norm.SampleCount);
    }

    // ── SurrogateObjective (B.6 / ADR-032 follow-on) ───────────────────

    private static Voxelforge.Optimization.Bayesian.GaussianProcessSurrogate
        DefaultGp2d() => new Voxelforge.Optimization.Bayesian.GaussianProcessSurrogate(
            bounds:         new[] { (0.0, 1.0), (0.0, 1.0) },
            lengthScale:    0.2,
            signalVariance: 1.0,
            noiseVariance:  1e-6);

    [Fact]
    public void Surrogate_FirstBudgetCalls_HitInner()
    {
        var inner = new CountingObjective();
        var surrogate = new SurrogateObjective(inner, budget: 5, DefaultGp2d());

        for (int i = 0; i < 5; i++)
            surrogate.Evaluate(new[] { 0.1 * i, 0.1 * i });

        Assert.Equal(5, inner.CallCount);
        Assert.Equal(5, surrogate.InnerCallCount);
        Assert.Equal(0, surrogate.SurrogateCallCount);
    }

    [Fact]
    public void Surrogate_PostBudgetCalls_HitSurrogate_NotInner()
    {
        var inner = new CountingObjective();
        var surrogate = new SurrogateObjective(inner, budget: 4, DefaultGp2d());

        // Burn the budget on distinct training points.
        for (int i = 0; i < 4; i++)
            surrogate.Evaluate(new[] { 0.2 + 0.1 * i, 0.2 + 0.1 * i });

        int budgetEndInnerCalls = inner.CallCount;

        // Post-budget calls: inner.CallCount must not increase.
        surrogate.Evaluate(new[] { 0.5, 0.5 });
        surrogate.Evaluate(new[] { 0.6, 0.6 });
        surrogate.Evaluate(new[] { 0.7, 0.7 });

        Assert.Equal(budgetEndInnerCalls, inner.CallCount);  // unchanged
        Assert.Equal(4, surrogate.InnerCallCount);
        Assert.Equal(3, surrogate.SurrogateCallCount);
        Assert.Equal(4, surrogate.TrainingSize);
    }

    [Fact]
    public void Surrogate_SurrogatePredictionAtTrainingPoint_MatchesInnerScore()
    {
        // The GP RBF kernel interpolates training points exactly (noise
        // variance is small, σ_n²=1e-6). Re-querying a training point
        // post-budget should return ≈ the inner score the training
        // saw, to within Cholesky-numerical-floor tolerance.
        var inner = new CountingObjective();
        var surrogate = new SurrogateObjective(inner, budget: 3, DefaultGp2d());

        var v1 = new[] { 0.2, 0.3 };
        var v2 = new[] { 0.5, 0.6 };
        var v3 = new[] { 0.7, 0.8 };

        var s1 = surrogate.Evaluate(v1);
        var s2 = surrogate.Evaluate(v2);
        var s3 = surrogate.Evaluate(v3);

        // Now post-budget; re-query v2 and expect ≈ s2.Score.
        var pred = surrogate.Evaluate(v2);
        Assert.Equal(s2.Score, pred.Score, precision: 3);  // RBF interpolation
        Assert.Equal(1, surrogate.SurrogateCallCount);
    }

    /// <summary>
    /// Always returns +Infinity score with a synthetic violation —
    /// used to verify SurrogateObjective doesn't try to fit a GP on
    /// an infeasible training point.
    /// </summary>
    private sealed class AlwaysInfeasibleObjective : IObjective
    {
        public int DimensionCount { get; } = 1;
        public IReadOnlyList<DesignVariableInfo> Variables { get; } = new[]
        {
            new DesignVariableInfo("x", 0.0, 1.0),
        };
        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
            => new EvaluationResult(
                Score:                   double.PositiveInfinity,
                Violations:              new[] { new FeasibilityViolation("STUB_INFEASIBLE", "synthetic infeasible", 0.0, 0.0) },
                EngineSpecificBreakdown: null);
    }

    [Fact]
    public void Surrogate_InfeasibleBudgetCalls_DoNotPoisonTrainingSet()
    {
        // +Inf scores during the budget phase must not enter the GP
        // training set — Cholesky would blow up. Verify training set
        // only grows on finite-score calls.
        var inner = new AlwaysInfeasibleObjective();
        var gp1d = new Voxelforge.Optimization.Bayesian.GaussianProcessSurrogate(
            bounds:         new[] { (0.0, 1.0) },
            lengthScale:    0.2,
            signalVariance: 1.0,
            noiseVariance:  1e-6);
        var surrogate = new SurrogateObjective(inner, budget: 3, gp1d);

        surrogate.Evaluate(new[] { 0.1 });
        surrogate.Evaluate(new[] { 0.5 });
        surrogate.Evaluate(new[] { 0.9 });

        Assert.Equal(0, surrogate.TrainingSize);
        Assert.Equal(3, surrogate.InnerCallCount);

        // Post-budget with no training data: falls back to inner.
        var r = surrogate.Evaluate(new[] { 0.4 });
        Assert.True(double.IsPositiveInfinity(r.Score));
        Assert.Equal(4, surrogate.InnerCallCount);
        Assert.Equal(0, surrogate.SurrogateCallCount);
    }

    [Fact]
    public void Surrogate_DeterministicRepeat_SameSequenceProducesSameScores()
    {
        // Two SurrogateObjective instances with the same inner + same
        // GP config + same call sequence must produce bit-identical
        // post-budget surrogate predictions. This is the
        // ADR-032 D5 determinism-repeat test.
        var seq = new[]
        {
            new[] { 0.10, 0.20 },
            new[] { 0.30, 0.40 },
            new[] { 0.50, 0.60 },
            new[] { 0.70, 0.80 },  // post-budget query
            new[] { 0.45, 0.55 },  // post-budget query
        };

        double[] RunOnce()
        {
            var inner = new CountingObjective();
            var s = new SurrogateObjective(inner, budget: 3, DefaultGp2d());
            var scores = new double[seq.Length];
            for (int i = 0; i < seq.Length; i++) scores[i] = s.Evaluate(seq[i]).Score;
            return scores;
        }

        var r1 = RunOnce();
        var r2 = RunOnce();
        for (int i = 0; i < r1.Length; i++)
            Assert.Equal(r1[i], r2[i]);
    }

    [Fact]
    public void Surrogate_Reset_RestoresInnerPath()
    {
        var inner = new CountingObjective();
        var surrogate = new SurrogateObjective(inner, budget: 2, DefaultGp2d());

        surrogate.Evaluate(new[] { 0.1, 0.1 });
        surrogate.Evaluate(new[] { 0.2, 0.2 });
        surrogate.Evaluate(new[] { 0.3, 0.3 });  // surrogate path

        Assert.Equal(2, surrogate.InnerCallCount);
        Assert.Equal(1, surrogate.SurrogateCallCount);

        surrogate.Reset();
        Assert.Equal(0, surrogate.InnerCallCount);
        Assert.Equal(0, surrogate.SurrogateCallCount);
        Assert.Equal(0, surrogate.TrainingSize);

        surrogate.Evaluate(new[] { 0.4, 0.4 });  // back on inner
        Assert.Equal(1, surrogate.InnerCallCount);
        Assert.Equal(0, surrogate.SurrogateCallCount);
    }

    [Fact]
    public void Surrogate_RejectsWrongVectorLength()
    {
        var inner = new CountingObjective();
        var surrogate = new SurrogateObjective(inner, budget: 1, DefaultGp2d());
        Assert.Throws<ArgumentException>(() => surrogate.Evaluate(new[] { 0.5 }));
    }

    [Fact]
    public void Surrogate_RejectsNullInner()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SurrogateObjective(null!, budget: 1, DefaultGp2d()));
    }

    [Fact]
    public void Surrogate_RejectsNullSurrogate()
    {
        var inner = new CountingObjective();
        Assert.Throws<ArgumentNullException>(() =>
            new SurrogateObjective(inner, budget: 1, null!));
    }

    [Fact]
    public void Surrogate_RejectsZeroBudget()
    {
        var inner = new CountingObjective();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SurrogateObjective(inner, budget: 0, DefaultGp2d()));
    }

    [Fact]
    public void Surrogate_RejectsDimensionMismatch()
    {
        var inner = new CountingObjective();  // dim 2
        var gp1d = new Voxelforge.Optimization.Bayesian.GaussianProcessSurrogate(
            bounds:         new[] { (0.0, 1.0) },
            lengthScale:    0.2,
            signalVariance: 1.0,
            noiseVariance:  1e-6);
        Assert.Throws<ArgumentException>(() =>
            new SurrogateObjective(inner, budget: 1, gp1d));
    }

    [Fact]
    public void Surrogate_ComposesWithCached_OnInner()
    {
        // SurrogateObjective wrapping CachedObjective wrapping the inner.
        // The cached wrapper deduplicates equal vectors below the
        // surrogate, so a repeated query inside the budget phase still
        // counts as an inner-objective call from the surrogate's
        // perspective (it called its inner — which happens to be a
        // cache) but is served from cache underneath, leaving the
        // CountingObjective hit-count showing only the unique vectors.
        var inner = new CountingObjective();
        var cached = new CachedObjective(inner);
        var surrogate = new SurrogateObjective(cached, budget: 3, DefaultGp2d());

        surrogate.Evaluate(new[] { 0.1, 0.1 });   // miss → inner #1
        surrogate.Evaluate(new[] { 0.1, 0.1 });   // cache hit
        surrogate.Evaluate(new[] { 0.5, 0.5 });   // miss → inner #2 — budget done
        surrogate.Evaluate(new[] { 0.9, 0.9 });   // surrogate path

        Assert.Equal(2, inner.CallCount);
        Assert.Equal(1, cached.HitCount);
        Assert.Equal(2, cached.MissCount);
        Assert.Equal(3, surrogate.InnerCallCount);    // 3 budget-phase dispatches
        Assert.Equal(1, surrogate.SurrogateCallCount);
    }

    // ── AsyncObjective (B.6 / ADR-032 follow-on) ───────────────────────

    private static DesignVariableInfo[] TwoVars() => new[]
    {
        new DesignVariableInfo("x0", 0.0, 1.0),
        new DesignVariableInfo("x1", 0.0, 1.0),
    };

    [Fact]
    public async Task Async_EvaluateAsync_ReturnsAwaitedResult()
    {
        var async = new AsyncObjective(
            dimensionCount: 2,
            variables:      TwoVars(),
            asyncEvaluate:  (mem, ct) =>
            {
                double sum = mem.Span[0] + mem.Span[1];
                return Task.FromResult(new EvaluationResult(
                    Score:                   sum,
                    Violations:              Array.Empty<FeasibilityViolation>(),
                    EngineSpecificBreakdown: null));
            });

        var r = await async.EvaluateAsync(new[] { 0.3, 0.4 }.AsMemory());
        Assert.Equal(0.7, r.Score, precision: 9);
    }

    [Fact]
    public void Async_SyncShim_CallsAsyncPath()
    {
        int callCount = 0;
        var async = new AsyncObjective(
            dimensionCount: 2,
            variables:      TwoVars(),
            asyncEvaluate:  (mem, ct) =>
            {
                Interlocked.Increment(ref callCount);
                return Task.FromResult(new EvaluationResult(
                    Score:                   mem.Span[0] - mem.Span[1],
                    Violations:              Array.Empty<FeasibilityViolation>(),
                    EngineSpecificBreakdown: null));
            });

        var r = async.Evaluate(new[] { 0.8, 0.3 });
        Assert.Equal(0.5, r.Score, precision: 9);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task Async_CancellationToken_PropagatesThroughAwait()
    {
        using var cts = new CancellationTokenSource();
        var async = new AsyncObjective(
            dimensionCount: 2,
            variables:      TwoVars(),
            asyncEvaluate:  async (mem, ct) =>
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                return new EvaluationResult(
                    Score:                   0.0,
                    Violations:              Array.Empty<FeasibilityViolation>(),
                    EngineSpecificBreakdown: null);
            });

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await async.EvaluateAsync(new[] { 0.1, 0.2 }.AsMemory(), cts.Token));
    }

    [Fact]
    public void Async_DeterministicRepeat_SameInputProducesSameScore()
    {
        // The wrapped async function is pure; two evaluations of the
        // same vector must produce identical EvaluationResult.Score.
        var async = new AsyncObjective(
            dimensionCount: 2,
            variables:      TwoVars(),
            asyncEvaluate:  (mem, ct) => Task.FromResult(new EvaluationResult(
                Score:                   mem.Span[0] * mem.Span[1],
                Violations:              Array.Empty<FeasibilityViolation>(),
                EngineSpecificBreakdown: null)));

        var v = new[] { 0.6, 0.7 };
        var r1 = async.Evaluate(v);
        var r2 = async.Evaluate(v);
        Assert.Equal(r1.Score, r2.Score);
    }

    [Fact]
    public void Async_RejectsWrongVectorLength_SyncPath()
    {
        var async = new AsyncObjective(
            dimensionCount: 2,
            variables:      TwoVars(),
            asyncEvaluate:  (mem, ct) => Task.FromResult(new EvaluationResult(
                Score: 0.0, Violations: Array.Empty<FeasibilityViolation>(), EngineSpecificBreakdown: null)));
        Assert.Throws<ArgumentException>(() => async.Evaluate(new[] { 0.5 }));
    }

    [Fact]
    public async Task Async_RejectsWrongVectorLength_AsyncPath()
    {
        var async = new AsyncObjective(
            dimensionCount: 2,
            variables:      TwoVars(),
            asyncEvaluate:  (mem, ct) => Task.FromResult(new EvaluationResult(
                Score: 0.0, Violations: Array.Empty<FeasibilityViolation>(), EngineSpecificBreakdown: null)));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await async.EvaluateAsync(new[] { 0.5 }.AsMemory()));
    }

    [Fact]
    public void Async_RejectsNullAsyncEvaluate()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AsyncObjective(dimensionCount: 2, variables: TwoVars(), asyncEvaluate: null!));
    }

    [Fact]
    public void Async_RejectsNullVariables()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AsyncObjective(
                dimensionCount: 2,
                variables:      null!,
                asyncEvaluate:  (mem, ct) => Task.FromResult(new EvaluationResult(
                    Score: 0.0, Violations: Array.Empty<FeasibilityViolation>(), EngineSpecificBreakdown: null))));
    }

    [Fact]
    public void Async_ComposesWithCached()
    {
        // Async wrapped by CachedObjective at the sync boundary: the
        // optimizer can use Evaluate(span, ct) and the cache deduplicates
        // repeated vectors before the async path runs.
        int callCount = 0;
        var async = new AsyncObjective(
            dimensionCount: 2,
            variables:      TwoVars(),
            asyncEvaluate:  (mem, ct) =>
            {
                Interlocked.Increment(ref callCount);
                return Task.FromResult(new EvaluationResult(
                    Score:                   mem.Span[0] + mem.Span[1],
                    Violations:              Array.Empty<FeasibilityViolation>(),
                    EngineSpecificBreakdown: null));
            });

        var cached = new CachedObjective(async);
        cached.Evaluate(new[] { 0.1, 0.1 });
        cached.Evaluate(new[] { 0.1, 0.1 });
        cached.Evaluate(new[] { 0.2, 0.2 });

        Assert.Equal(2, callCount);  // 2 unique vectors hit the async function
        Assert.Equal(1, cached.HitCount);
        Assert.Equal(2, cached.MissCount);
    }
}
