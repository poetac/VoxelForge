// ObjectiveWrappers.cs — Sprint EC.W12 follow-on / IObjective composition
// extension.
//
// Three thin wrappers that compose with any IObjective (including the
// existing CostObjective / EngineObjectiveAdapter chain) to add cross-
// cutting behaviour without touching the underlying physics objective:
//
//   1. CachedObjective    — memoize evaluations by vector identity.
//                           Useful when the optimizer revisits the same
//                           vector (CMA-ES on tight clusters, grid
//                           sweeps, hybrid SA / Bayesian restarts).
//   2. TeeObjective       — record every evaluation to a List<>
//                           accessible after the run. Sister to UNIX
//                           tee(1); useful for offline analysis +
//                           Pareto-front reconstruction from full
//                           candidate-set history.
//   3. BoundedObjective   — clip the input vector to [Min, Max] before
//                           dispatching. Defensive layer for optimizers
//                           that violate bounds (e.g. mutation operators
//                           that overshoot, gradient steps that drift).
//
// Composition order:
//   IObjective root  →  ChildObjective inner_wrap  →  Cached or Tee or Bounded
// All wrappers preserve the inner objective's DimensionCount + Variables;
// none of them perturb the physics layer.
//
// Determinism: every wrapper is deterministic when the inner objective
// is. Cached + Tee + Bounded are all pure transforms.
//
// Thread-safety:
//   • BoundedObjective is fully thread-safe (no mutable state).
//   • TeeObjective uses a thread-safe append on its log (lock around
//     the List).
//   • CachedObjective uses ConcurrentDictionary keyed on a structural
//     hash of the vector; the first thread to evaluate wins, others
//     block on a Lazy<EvaluationResult>.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Voxelforge.Optimization.Bayesian;

namespace Voxelforge.Optimization;

/// <summary>
/// Memoizing <see cref="IObjective"/> wrapper. Caches every evaluation
/// keyed on a structural hash of the input vector; concurrent calls
/// with the same vector share the result via <see cref="Lazy{T}"/>
/// double-checked locking.
/// </summary>
/// <remarks>
/// Useful when the optimizer revisits the same vector (CMA-ES on tight
/// clusters, Bayesian-opt acquisition restarts, hybrid SA → CMA-ES
/// handoffs). For SA without restart the cache hit-rate is near zero
/// — wrapping the wrong objective wastes memory + the hash cost.
/// </remarks>
public sealed class CachedObjective : IObjective
{
    private readonly IObjective _inner;
    private readonly ConcurrentDictionary<VectorKey, Lazy<EvaluationResult>> _cache = new();
    private long _hitCount;
    private long _missCount;

    /// <summary>Cumulative hit count since construction.</summary>
    public long HitCount => Interlocked.Read(ref _hitCount);

    /// <summary>Cumulative miss count since construction.</summary>
    public long MissCount => Interlocked.Read(ref _missCount);

    /// <inheritdoc />
    public int DimensionCount => _inner.DimensionCount;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

    /// <summary>Construct a cache around <paramref name="inner"/>.</summary>
    public CachedObjective(IObjective inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        if (vector.Length != _inner.DimensionCount)
            throw new ArgumentException(
                $"Vector length {vector.Length} does not match DimensionCount {_inner.DimensionCount}.",
                nameof(vector));

        // Hot-path optimisation (audit 12-perf §1.1): cache hits used to
        // allocate two double[]s — one inside VectorKey.FromSpan for the
        // persistent key, another via vector.ToArray() for the Lazy
        // closure. Renting the lookup buffer from ArrayPool lets cache
        // hits stay allocation-free; only cache misses commit a single
        // double[] (the persistent VectorKey value) plus the Lazy.
        int len = vector.Length;
        double[] rented = ArrayPool<double>.Shared.Rent(len);
        try
        {
            vector.CopyTo(rented);
            int hash = ComputeHash(rented, len);
            var probe = new VectorKey(rented, hash, len);

            if (_cache.TryGetValue(probe, out var existing))
            {
                Interlocked.Increment(ref _hitCount);
                return existing.Value;
            }

            // Miss: commit a permanent copy as the cache key. The Lazy
            // captures the same permanent array so the deferred inner
            // evaluation sees the exact bytes that defined the key.
            var permanent = vector.ToArray();
            var committedKey = new VectorKey(permanent, hash, len);
            bool isMiss = false;
            var lazy = _cache.GetOrAdd(committedKey, _ =>
            {
                isMiss = true;
                return new Lazy<EvaluationResult>(
                    () => _inner.Evaluate(permanent, ct),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            });

            // ConcurrentDictionary.GetOrAdd may invoke the factory more
            // than once under contention (multiple threads racing the
            // same key); only one Lazy lands in the dictionary. Counts
            // are therefore approximate under heavy contention —
            // accurate within ±1 per contended key. Single-threaded
            // callers see exact counts.
            if (isMiss) Interlocked.Increment(ref _missCount);
            else        Interlocked.Increment(ref _hitCount);

            return lazy.Value;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(rented);
        }
    }

    private static int ComputeHash(double[] values, int length)
    {
        int hash = 17;
        for (int i = 0; i < length; i++)
            hash = unchecked(hash * 31 + values[i].GetHashCode());
        return hash;
    }

    /// <summary>Clear the cache + reset hit/miss counters.</summary>
    public void Reset()
    {
        _cache.Clear();
        Interlocked.Exchange(ref _hitCount, 0);
        Interlocked.Exchange(ref _missCount, 0);
    }

    /// <summary>
    /// Structural hash key over a candidate vector. Equality is bit-
    /// identical FP comparison; small floating-point differences produce
    /// distinct keys (no fuzzy matching). Stores an explicit
    /// <c>Length</c> so a probe key backed by a pooled buffer (whose
    /// allocated length can exceed the logical vector length) compares
    /// correctly against a committed key backed by a tight-fit array.
    /// </summary>
    private readonly struct VectorKey : IEquatable<VectorKey>
    {
        private readonly double[] _values;
        private readonly int _hash;
        private readonly int _length;

        public VectorKey(double[] values, int hash, int length)
        {
            _values = values;
            _hash = hash;
            _length = length;
        }

        public bool Equals(VectorKey other)
        {
            if (_length != other._length) return false;
            for (int i = 0; i < _length; i++)
                if (_values[i] != other._values[i]) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is VectorKey k && Equals(k);
        public override int GetHashCode() => _hash;
    }
}

/// <summary>
/// Record of one evaluation through a <see cref="TeeObjective"/>.
/// </summary>
/// <param name="Vector">Input vector (defensive copy).</param>
/// <param name="Result">Evaluation result.</param>
/// <param name="WallClockUtc">Timestamp at evaluation end.</param>
public sealed record TeeRecord(
    double[] Vector,
    EvaluationResult Result,
    DateTime WallClockUtc);

/// <summary>
/// Tee'ing <see cref="IObjective"/> wrapper. Records every evaluation
/// into an internal append-only log accessible after the run via
/// <see cref="Log"/>. Useful for offline analysis, Pareto-front
/// reconstruction from full candidate-set history, and post-mortem
/// debugging of optimizer trajectories.
/// </summary>
/// <remarks>
/// <para>
/// <b>Explicitly non-deterministic by contract.</b> Every <see cref="TeeRecord"/>
/// stamped into <see cref="Log"/> carries a <see cref="DateTime.UtcNow"/>
/// wall-clock timestamp at evaluation end. Two runs over the same design
/// vector produce identical <see cref="EvaluationResult"/> values but
/// different timestamps. The <c>EvaluationResult</c> itself (Score +
/// Violations + Breakdown) remains deterministic — only the trace metadata
/// is wall-clock-dependent.
/// </para>
/// <para>
/// Do NOT compose <see cref="TeeObjective"/> inside a stack whose Log will
/// be hashed, serialised verbatim, or compared between runs. Use it for
/// post-mortem offline analysis where wall-clock ordering matters and
/// reproducibility is bounded to the Score/Violations only. Analyzer rule
/// VFD012 catches the determinism foot-gun structurally on any other
/// IObjective implementation; <see cref="TeeObjective"/>'s
/// <see cref="Evaluate"/> suppresses it via an explicit
/// <see cref="SuppressMessageAttribute"/> to record this opt-out at the
/// call site.
/// </para>
/// </remarks>
public sealed class TeeObjective : IObjective
{
    private readonly IObjective _inner;
    private readonly List<TeeRecord> _log = new();
    private readonly object _lock = new();

    /// <summary>
    /// Append-only log of all evaluations since construction (or since
    /// the last <see cref="Reset"/> call). Returns a snapshot — safe to
    /// iterate concurrently with further evaluations.
    /// </summary>
    public IReadOnlyList<TeeRecord> Log
    {
        get
        {
            lock (_lock) return _log.ToArray();
        }
    }

    /// <inheritdoc />
    public int DimensionCount => _inner.DimensionCount;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

    /// <summary>Construct a tee around <paramref name="inner"/>.</summary>
    public TeeObjective(IObjective inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Voxelforge.Determinism", "VFD012",
        Justification = "TeeObjective captures wall-clock by contract — see class-level remarks.")]
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        var copy = vector.ToArray();
        var result = _inner.Evaluate(vector, ct);
        var record = new TeeRecord(copy, result, DateTime.UtcNow);
        lock (_lock) _log.Add(record);
        return result;
    }

    /// <summary>Clear the log.</summary>
    public void Reset()
    {
        lock (_lock) _log.Clear();
    }
}

/// <summary>
/// Finite-difference gradient diagnostic over an
/// <see cref="IObjective"/>. Not an <see cref="IObjective"/> itself —
/// it's a sibling helper that exposes <see cref="ComputeGradient"/>
/// for callers that need ∇f at a point (line-search probes, gradient-
/// polish post-pass on SA winners, sensitivity analysis).
/// </summary>
/// <remarks>
/// Uses central differences by default
/// (f(x + ε e_i) − f(x − ε e_i)) / (2 ε) for O(ε²) accuracy. Costs 2 ·
/// dim inner-evaluations per gradient. The step size ε defaults to
/// 1e-6 · (Max − Min) per dim — large enough to dominate FP rounding,
/// small enough to keep the truncation error sub-1 %. Override via
/// the constructor when the objective is unusually noisy or steep.
///
/// Forward differences are available via <see cref="ComputeGradientForward"/>
/// when one of the central probes would land outside bounds (rare; the
/// optimizer almost always evaluates well inside the domain).
///
/// Determinism: pure over the wrapped objective. Thread-safety: same
/// as the inner objective.
/// </remarks>
public sealed class GradientProbe
{
    private readonly IObjective _inner;
    private readonly double[] _steps;

    /// <summary>Construct a gradient probe over <paramref name="inner"/>.</summary>
    /// <param name="inner">Inner objective whose gradient to probe.</param>
    /// <param name="relativeStep">
    /// Step fraction per dim: <c>ε_i = relativeStep · (Max_i − Min_i)</c>.
    /// Default 1e-6 — small enough to keep truncation error sub-1 %, large
    /// enough to dominate FP rounding for objectives at the kJ-or-larger
    /// score scale. Tune down for very-noisy objectives (NSGA on highly-
    /// stochastic surrogates) or up for very-smooth ones (analytical
    /// rocket-cycle solvers).
    /// </param>
    public GradientProbe(IObjective inner, double relativeStep = 1.0e-6)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (!(relativeStep > 0))
            throw new ArgumentOutOfRangeException(nameof(relativeStep),
                "relativeStep must be positive.");
        _steps = new double[_inner.DimensionCount];
        for (int i = 0; i < _inner.DimensionCount; i++)
            _steps[i] = relativeStep * (_inner.Variables[i].Max - _inner.Variables[i].Min);
    }

    /// <summary>
    /// Compute the gradient of the inner objective's <see cref="EvaluationResult.Score"/>
    /// at <paramref name="point"/> via central differences. Length of
    /// <paramref name="point"/> must equal the inner objective's
    /// <see cref="IObjective.DimensionCount"/>.
    /// </summary>
    public double[] ComputeGradient(ReadOnlySpan<double> point, CancellationToken ct = default)
    {
        if (point.Length != _inner.DimensionCount)
            throw new ArgumentException(
                $"Point length {point.Length} does not match DimensionCount {_inner.DimensionCount}.",
                nameof(point));

        var gradient = new double[_inner.DimensionCount];
        var probe = point.ToArray();
        for (int i = 0; i < _inner.DimensionCount; i++)
        {
            double original = probe[i];
            double eps = _steps[i];

            probe[i] = original + eps;
            double fPlus = _inner.Evaluate(probe, ct).Score;

            probe[i] = original - eps;
            double fMinus = _inner.Evaluate(probe, ct).Score;

            probe[i] = original;  // restore
            gradient[i] = (fPlus - fMinus) / (2.0 * eps);
        }
        return gradient;
    }

    /// <summary>
    /// Forward-difference variant — cheaper (dim + 1 evaluations vs 2·dim)
    /// but only O(ε) accurate. Use when one of the central probes would
    /// fall outside the design-space bounds, or when call budget is tight.
    /// </summary>
    public double[] ComputeGradientForward(ReadOnlySpan<double> point, CancellationToken ct = default)
    {
        if (point.Length != _inner.DimensionCount)
            throw new ArgumentException(
                $"Point length {point.Length} does not match DimensionCount {_inner.DimensionCount}.",
                nameof(point));

        var probe = point.ToArray();
        double f0 = _inner.Evaluate(probe, ct).Score;

        var gradient = new double[_inner.DimensionCount];
        for (int i = 0; i < _inner.DimensionCount; i++)
        {
            double original = probe[i];
            double eps = _steps[i];

            probe[i] = original + eps;
            double fPlus = _inner.Evaluate(probe, ct).Score;

            probe[i] = original;  // restore
            gradient[i] = (fPlus - f0) / eps;
        }
        return gradient;
    }
}

/// <summary>
/// Noise-robust <see cref="IObjective"/> wrapper. Evaluates the inner
/// objective at the central vector plus <c>2 · neighbourCount</c>
/// perturbed neighbours (one positive, one negative per dim sampled),
/// and returns the median score. Per-dim perturbation is a fixed
/// fraction of <c>(Max − Min)</c>.
/// </summary>
/// <remarks>
/// Useful for objectives backed by stochastic solvers (turbulence-
/// model RANS sweeps with random initial conditions, Monte Carlo
/// surrogate samplers) or by physics objectives whose feasibility
/// gates fire on near-boundary cluster bands. Median is more robust
/// than mean to outlier infeasible candidates (the optimizer should
/// see "this design family is feasible" even if one perturbed sample
/// trips a gate).
///
/// Cost: <c>(1 + 2 · neighbourCount)</c> inner evaluations per call.
/// Default neighbourCount=2 ⇒ 5 inner evaluations. Cap at 8 to keep
/// the wrapper from blowing the SA per-iteration budget.
///
/// Determinism: pure over a deterministic inner. The neighbour offsets
/// are themselves deterministic (cycled per-dim in a known pattern,
/// not RNG-driven).
/// </remarks>
public sealed class SubsamplingObjective : IObjective
{
    private readonly IObjective _inner;
    private readonly int _neighbourCount;
    private readonly double _relativeStep;
    private readonly double[] _steps;

    /// <inheritdoc />
    public int DimensionCount => _inner.DimensionCount;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

    /// <summary>
    /// Construct a subsampling wrapper around <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">Inner objective.</param>
    /// <param name="neighbourCount">
    /// Number of neighbouring dims to perturb. Each dim contributes 2
    /// extra evaluations (+ε, -ε). Default 2 → 5 inner evaluations per
    /// call. Must be in [1, 8].
    /// </param>
    /// <param name="relativeStep">
    /// Per-dim perturbation as a fraction of <c>(Max − Min)</c>.
    /// Default 1e-3 — large enough to genuinely probe local
    /// roughness, small enough to stay inside the candidate's
    /// physically-relevant neighbourhood.
    /// </param>
    public SubsamplingObjective(
        IObjective inner,
        int neighbourCount = 2,
        double relativeStep = 1.0e-3)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (neighbourCount < 1 || neighbourCount > 8)
            throw new ArgumentOutOfRangeException(nameof(neighbourCount),
                "neighbourCount must be in [1, 8].");
        if (!(relativeStep > 0))
            throw new ArgumentOutOfRangeException(nameof(relativeStep),
                "relativeStep must be positive.");

        _neighbourCount = Math.Min(neighbourCount, _inner.DimensionCount);
        _relativeStep   = relativeStep;
        _steps = new double[_inner.DimensionCount];
        for (int i = 0; i < _inner.DimensionCount; i++)
            _steps[i] = relativeStep * (_inner.Variables[i].Max - _inner.Variables[i].Min);
    }

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        if (vector.Length != _inner.DimensionCount)
            throw new ArgumentException(
                $"Vector length {vector.Length} does not match DimensionCount {_inner.DimensionCount}.",
                nameof(vector));

        // Hot-path optimisation (audit 12-perf §1.1): the perturbation
        // buffer used to be a fresh double[] per call. Stack-allocate
        // (or pool-rent for unusually large dims) so the wrapper costs
        // zero heap allocations on the hot path. The inner objective
        // signature already accepts ReadOnlySpan, so each probe call
        // dispatches through the span path directly.
        int len = vector.Length;
        Span<double> perturbed = len <= StackallocLimit
            ? stackalloc double[len]
            : default;
        double[]? rented = null;
        if (perturbed.IsEmpty)
        {
            rented = ArrayPool<double>.Shared.Rent(len);
            perturbed = rented.AsSpan(0, len);
        }
        try
        {
            // 1. Central evaluation. Pass the original span straight
            //    through — no copy needed before the perturbation loop
            //    starts mutating.
            var centralResult = _inner.Evaluate(vector, ct);

            // 1a. An infeasible central design keeps the +∞ infeasibility
            //     sentinel. Subsampling robustness is only meaningful around a
            //     *feasible* centre; relabeling an infeasible centre with a
            //     finite neighbour-median would erase the sentinel (per
            //     IObjective: "+∞ … SA never accepts a +Inf candidate") and let
            //     SA accept an infeasible design as a candidate / new best. A
            //     *finite* score carrying advisory Violations is still feasible
            //     ("feasible with warnings") and proceeds to the median below.
            if (!double.IsFinite(centralResult.Score))
                return centralResult;

            // 2. Per-dim ±ε neighbours, cycling through the first
            //    neighbourCount dims deterministically. We pick the dims
            //    with LARGEST range (Max - Min) first to maximise probe
            //    coverage.
            vector.CopyTo(perturbed);
            var dimsToProbe = SelectProbeDims();
            var scores = new List<double>(1 + 2 * _neighbourCount) { centralResult.Score };

            foreach (int dim in dimsToProbe)
            {
                double original = perturbed[dim];
                double eps = _steps[dim];

                perturbed[dim] = original + eps;
                scores.Add(_inner.Evaluate(perturbed, ct).Score);

                perturbed[dim] = original - eps;
                scores.Add(_inner.Evaluate(perturbed, ct).Score);

                perturbed[dim] = original;  // restore
            }

            // 3. Return the median (or central's violations + breakdown).
            scores.Sort();
            double median = scores.Count % 2 == 1
                ? scores[scores.Count / 2]
                : 0.5 * (scores[scores.Count / 2 - 1] + scores[scores.Count / 2]);

            return centralResult with { Score = median };
        }
        finally
        {
            if (rented is not null) ArrayPool<double>.Shared.Return(rented);
        }
    }

    private const int StackallocLimit = 128;

    private int[] SelectProbeDims()
    {
        // Largest-range dims first. Stable sort so ties resolve by index.
        var indexed = new (int Dim, double Range)[_inner.DimensionCount];
        for (int i = 0; i < indexed.Length; i++)
            indexed[i] = (i, _inner.Variables[i].Max - _inner.Variables[i].Min);

        Array.Sort(indexed, (a, b) =>
        {
            int byRange = b.Range.CompareTo(a.Range);  // descending
            return byRange != 0 ? byRange : a.Dim.CompareTo(b.Dim);
        });

        var dims = new int[_neighbourCount];
        for (int i = 0; i < _neighbourCount; i++) dims[i] = indexed[i].Dim;
        return dims;
    }
}

/// <summary>
/// Timeout-enforcing <see cref="IObjective"/> wrapper. Cancels the
/// inner evaluation if it exceeds a wall-clock budget; returns an
/// infeasible-score sentinel for timed-out evaluations.
/// </summary>
/// <remarks>
/// Pairs with subprocess-oracle (`voxelforge-eval`) consumers + the
/// future <c>AsyncObjective</c> wrapper (issue #499). The inner
/// objective MUST observe the cancellation token at natural
/// boundaries for the timeout to be honored; objectives that block
/// indefinitely in pure C# code without a cancellation check will
/// still hang.
///
/// Determinism: timeout enforcement is non-deterministic by design
/// (depends on wall-clock). Two runs of the same vector may behave
/// differently if one barely makes the deadline. Score for a
/// completed-inside-budget call is bit-identical to the unwrapped
/// inner; only the timed-out path differs from a pure inner call.
/// </remarks>
public sealed class TimeoutObjective : IObjective
{
    private readonly IObjective _inner;
    private readonly TimeSpan _timeout;
    private readonly double _infeasibleScore;

    /// <inheritdoc />
    public int DimensionCount => _inner.DimensionCount;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

    /// <summary>Construct a timeout wrapper.</summary>
    /// <param name="inner">Inner objective.</param>
    /// <param name="timeout">Wall-clock budget per Evaluate call.</param>
    /// <param name="infeasibleScore">
    /// Score for timed-out candidates. Defaults to
    /// <see cref="double.PositiveInfinity"/>.
    /// </param>
    public TimeoutObjective(
        IObjective inner,
        TimeSpan timeout,
        double infeasibleScore = double.PositiveInfinity)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout),
                "Timeout must be positive.");
        _timeout = timeout;
        _infeasibleScore = infeasibleScore;
    }

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        try
        {
            return _inner.Evaluate(vector, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout fired (linked cts cancelled itself); not the
            // caller cancelling explicitly. Return an infeasible-score
            // result with a synthetic timeout violation.
            return new EvaluationResult(
                Score: _infeasibleScore,
                Violations: new[]
                {
                    new FeasibilityViolation(
                        "OBJECTIVE_EVALUATION_TIMEOUT",
                        $"Inner objective evaluation exceeded {_timeout.TotalMilliseconds:F0} ms budget.",
                        ActualValue: _timeout.TotalSeconds,
                        Limit:       _timeout.TotalSeconds),
                },
                EngineSpecificBreakdown: null);
        }
    }
}

/// <summary>
/// Retrying <see cref="IObjective"/> wrapper. Re-invokes the inner
/// evaluation up to <see cref="MaxRetries"/> times on transient
/// exceptions; gives up on the final attempt's failure.
/// </summary>
/// <remarks>
/// Pairs with subprocess-oracle consumers where stdout-pipe corruption
/// or transient process-start failures occasionally surface as
/// exceptions from the inner Evaluate. Does NOT retry on
/// <see cref="OperationCanceledException"/> (cancellation is honored
/// immediately) or on <see cref="ArgumentException"/> /
/// <see cref="ArgumentOutOfRangeException"/> (caller bugs, not
/// transient).
///
/// Exponential backoff between attempts: 1 ms, 2 ms, 4 ms, 8 ms, ...
/// capped at 1 s. Total worst-case wall-clock added by N retries is
/// ~2^(N-1) ms (small for typical N=3).
///
/// Determinism: pure relative to the inner. Score on success is
/// bit-identical to a direct inner call (modulo transient retry path).
/// </remarks>
public sealed class RetryingObjective : IObjective
{
    private readonly IObjective _inner;

    /// <summary>Number of retries after the initial attempt fails.</summary>
    public int MaxRetries { get; }

    /// <inheritdoc />
    public int DimensionCount => _inner.DimensionCount;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

    /// <summary>Construct a retrying wrapper.</summary>
    public RetryingObjective(IObjective inner, int maxRetries = 3)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (maxRetries < 1 || maxRetries > 10)
            throw new ArgumentOutOfRangeException(nameof(maxRetries),
                "maxRetries must be in [1, 10].");
        MaxRetries = maxRetries;
    }

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        // Hot-path optimisation (audit 12-perf §1.1): the span stays
        // live across the for-loop body — Thread.Sleep + try/catch are
        // synchronous and never await — so we can pass it straight
        // through to the inner objective on every attempt without
        // materialising a backing double[].
        Exception? lastException = null;
        int backoffMs = 1;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return _inner.Evaluate(vector, ct);
            }
            catch (OperationCanceledException)
            {
                throw;   // Cancellation: don't retry.
            }
            catch (ArgumentException)
            {
                throw;   // Caller-bug: don't retry, surface immediately.
            }
            catch (Exception ex)
            {
                lastException = ex;
                // Sleep + retry only if there's a next attempt; otherwise
                // fall through to the wrapped throw below so the caller
                // sees "Inner objective failed after N attempts" rather
                // than the raw inner exception (the `when (attempt <
                // MaxRetries)` form let the last attempt's exception
                // escape directly, losing the attempt-count summary).
                if (attempt < MaxRetries)
                {
                    Thread.Sleep(backoffMs);
                    backoffMs = Math.Min(backoffMs * 2, 1000);
                }
            }
        }

        // Exhausted retries — throw the last seen exception.
        throw new InvalidOperationException(
            $"Inner objective failed after {MaxRetries + 1} attempts.",
            lastException);
    }
}

/// <summary>
/// Sign-flipping <see cref="IObjective"/> wrapper. Negates the inner
/// objective's <see cref="EvaluationResult.Score"/> so callers can
/// drive a MAXIMIZE workflow on the existing optimizer portfolio
/// (which uniformly minimises).
/// </summary>
/// <remarks>
/// Avoids the common manual-negation pattern at the EngineObjectiveAdapter
/// callsite (<c>r =&gt; -r.Score</c>). Wrapping with
/// <see cref="MaximizeAdapter"/> reads as <em>"maximise this objective"</em>
/// rather than <em>"minimise its negation"</em>, which is easier to
/// review.
///
/// Feasibility contract preserved: when the inner emits
/// <see cref="EvaluationResult.Violations"/> or
/// <see cref="double.PositiveInfinity"/>, the wrapped output also
/// signals infeasible (routed through
/// <see cref="InfeasibleScore"/>). Never returns a negative-infeasible-
/// score (which would confuse minimization-only optimizers — the
/// infeasible sentinel is always strictly worse than any feasible
/// score).
///
/// Determinism / thread-safety: pure transform; inherits from inner.
/// </remarks>
public sealed class MaximizeAdapter : IObjective
{
    private readonly IObjective _inner;

    /// <summary>
    /// Score returned when the inner objective emits at least one
    /// <see cref="FeasibilityViolation"/> or +∞ score. Defaults to
    /// <see cref="double.PositiveInfinity"/>.
    /// </summary>
    public double InfeasibleScore { get; }

    /// <inheritdoc />
    public int DimensionCount => _inner.DimensionCount;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

    /// <summary>
    /// Wrap <paramref name="inner"/> with a sign-flip on the score.
    /// </summary>
    /// <param name="inner">
    /// Inner objective whose score should be MAXIMIZED by the optimizer
    /// (the wrapper negates it so the optimizer's minimization works).
    /// </param>
    /// <param name="infeasibleScore">
    /// Override for the infeasible-candidate score. Defaults to
    /// <see cref="double.PositiveInfinity"/>.
    /// </param>
    public MaximizeAdapter(
        IObjective inner,
        double infeasibleScore = double.PositiveInfinity)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        InfeasibleScore = infeasibleScore;
    }

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        var innerResult = _inner.Evaluate(vector, ct);

        // Honour the inner feasibility contract first — infeasibles
        // stay infeasible after the wrapper. Audit-hardened (2026-05-13)
        // to also catch NaN: negating NaN produces NaN, which would
        // confuse minimization optimizers.
        if (innerResult.Violations.Count > 0
            || double.IsPositiveInfinity(innerResult.Score)
            || double.IsNaN(innerResult.Score))
        {
            return innerResult with { Score = InfeasibleScore };
        }

        return innerResult with { Score = -innerResult.Score };
    }
}

/// <summary>
/// Composite cost-summing <see cref="IObjective"/> wrapper. Sums N
/// caller-supplied cost extractors over the inner objective's
/// <see cref="EvaluationResult.EngineSpecificBreakdown"/> to produce a
/// total-system cost score.
/// </summary>
/// <remarks>
/// Use case: a NEP cargo-vehicle Pareto sweep wants to minimize the
/// SUM of (engine cost + propellant-tank cost + radiator cost + power-
/// conditioning cost). Each component cost is a separate extractor;
/// the sum is the inner-objective score.
///
/// Equivalent to wrapping the inner with a primary
/// <see cref="CostObjective"/> whose costFn is <c>b => extractor1(b) +
/// extractor2(b) + ...</c>, but keeps the per-component extractors
/// individually inspectable + testable.
///
/// Feasibility contract preserved (infeasible inner → infeasibleScore).
/// Determinism + thread-safety: pure transform; inherits from inner +
/// the supplied extractors.
/// </remarks>
public sealed class CompositeCostObjective : IObjective
{
    private readonly IObjective _inner;
    private readonly Func<object?, double>[] _extractors;
    private readonly double _infeasibleScore;

    /// <inheritdoc />
    public int DimensionCount => _inner.DimensionCount;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

    /// <summary>Number of component cost extractors that get summed.</summary>
    public int ExtractorCount => _extractors.Length;

    /// <summary>
    /// Construct a composite-cost wrapper.
    /// </summary>
    /// <param name="inner">Inner physics objective.</param>
    /// <param name="extractors">
    /// One or more pure functions that each return a per-component cost
    /// scalar from the engine-specific breakdown. The total score is
    /// the SUM of every extractor's output.
    /// </param>
    /// <param name="infeasibleScore">
    /// Optional override for the infeasible-candidate score.
    /// </param>
    public CompositeCostObjective(
        IObjective inner,
        IEnumerable<Func<object?, double>> extractors,
        double infeasibleScore = double.PositiveInfinity)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (extractors is null) throw new ArgumentNullException(nameof(extractors));

        var list = new List<Func<object?, double>>();
        foreach (var ex in extractors)
        {
            if (ex is null)
                throw new ArgumentException(
                    "All extractors must be non-null.", nameof(extractors));
            list.Add(ex);
        }
        if (list.Count == 0)
            throw new ArgumentException(
                "At least one cost extractor must be supplied.", nameof(extractors));

        _extractors = list.ToArray();
        _infeasibleScore = infeasibleScore;
    }

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        var innerResult = _inner.Evaluate(vector, ct);

        // Audit-hardened (2026-05-13): also catch NaN inner scores to
        // prevent the cost-sum from inheriting non-finite contamination.
        if (innerResult.Violations.Count > 0
            || double.IsPositiveInfinity(innerResult.Score)
            || double.IsNaN(innerResult.Score))
        {
            return innerResult with { Score = _infeasibleScore };
        }

        double total = 0.0;
        for (int i = 0; i < _extractors.Length; i++)
            total += _extractors[i](innerResult.EngineSpecificBreakdown);

        return innerResult with { Score = total };
    }
}

/// <summary>
/// Running z-score normalising <see cref="IObjective"/> wrapper.
/// Tracks the running mean + variance of inner-objective scores via
/// Welford's online algorithm; after a warmup window emits
/// <c>(score - μ) / σ</c> so multi-objective optimizers see
/// comparably-scaled scores.
/// </summary>
/// <remarks>
/// Use case: NSGA-II Pareto sweep where the raw inner scores have
/// wildly different magnitudes (thrust in N vs cost in USD vs
/// embodied-CO₂ in kg). The crowding-distance calculation would
/// otherwise be dominated by whichever objective has the largest
/// numerical range. Normalising to z-scores levels the playing field.
///
/// Two-phase output:
///   • Warmup phase (first <see cref="WarmupSamples"/> calls): the
///     wrapper passes the raw inner score through unchanged while it
///     builds up the running statistics. Optimizer trajectory is
///     unaffected during this phase.
///   • Normalised phase (after warmup): emits
///     <c>(score - RunningMean) / max(RunningStdDev, 1e-9)</c>.
///
/// Feasibility contract preserved: when the inner emits Violations or
/// +∞, the wrapper routes through <see cref="InfeasibleScore"/> WITHOUT
/// updating the running statistics (infeasible scores would otherwise
/// bias the mean / variance toward +∞).
///
/// Thread-safety: Welford's update uses a lock for atomicity (the
/// running stats are mutable state, unlike every other wrapper in this
/// file). The locked critical section is microseconds; not a hot-path
/// concern unless the inner objective is similarly cheap.
///
/// Determinism caveat: the score depends on the ORDER of evaluations
/// (running mean accumulates differently if vectors are sampled in a
/// different order). SA's strict-determinism contract holds because
/// SA evaluates deterministically; NSGA-II's stochastic mutation
/// however means re-running with the same seed produces the same
/// normalisation trajectory but differs from a deterministic baseline.
/// Document this in the call site if it matters.
/// </remarks>
public sealed class NormalizingObjective : IObjective
{
    private readonly IObjective _inner;
    private readonly int _warmupSamples;
    private readonly double _infeasibleScore;
    private readonly object _statsLock = new();

    // Welford running stats — count + mean + M2 (sum of squared deltas).
    private long _count;
    private double _mean;
    private double _m2;

    /// <inheritdoc />
    public int DimensionCount => _inner.DimensionCount;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

    /// <summary>
    /// Number of inner evaluations required before normalisation
    /// activates. Calls below this threshold return raw inner scores.
    /// </summary>
    public int WarmupSamples { get; }

    /// <summary>
    /// Running count of feasible evaluations consumed (for diagnostic).
    /// Infeasible evaluations are NOT counted.
    /// </summary>
    public long SampleCount
    {
        get { lock (_statsLock) return _count; }
    }

    /// <summary>Running mean of feasible scores. NaN before any sample.</summary>
    public double RunningMean
    {
        get { lock (_statsLock) return _count > 0 ? _mean : double.NaN; }
    }

    /// <summary>
    /// Running standard deviation of feasible scores (sample variance,
    /// N-1 denominator). NaN with fewer than 2 samples.
    /// </summary>
    public double RunningStdDev
    {
        get
        {
            lock (_statsLock)
            {
                if (_count < 2) return double.NaN;
                return Math.Sqrt(_m2 / (_count - 1));
            }
        }
    }

    /// <summary>Score returned for inner-infeasible candidates.</summary>
    public double InfeasibleScore { get; }

    /// <summary>Construct a z-score normaliser.</summary>
    /// <param name="inner">Inner objective.</param>
    /// <param name="warmupSamples">
    /// Number of inner calls used to build up statistics before
    /// normalisation activates. Default 16 — small enough to converge
    /// quickly, large enough to give a stable mean. Must be ≥ 2.
    /// </param>
    /// <param name="infeasibleScore">
    /// Score for infeasible candidates. Defaults to
    /// <see cref="double.PositiveInfinity"/>.
    /// </param>
    public NormalizingObjective(
        IObjective inner,
        int warmupSamples = 16,
        double infeasibleScore = double.PositiveInfinity)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (warmupSamples < 2)
            throw new ArgumentOutOfRangeException(nameof(warmupSamples),
                "warmupSamples must be ≥ 2.");
        WarmupSamples = warmupSamples;
        _warmupSamples = warmupSamples;
        InfeasibleScore = infeasibleScore;
        _infeasibleScore = infeasibleScore;
    }

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        var innerResult = _inner.Evaluate(vector, ct);

        // Infeasible OR non-finite score: route + do NOT update stats.
        // NaN scores would otherwise propagate into _mean / _m2 and
        // corrupt every subsequent z-score (audit fix 2026-05-13);
        // ±Infinity is similarly poisonous to a running mean.
        if (innerResult.Violations.Count > 0
            || !double.IsFinite(innerResult.Score))
        {
            return innerResult with { Score = _infeasibleScore };
        }

        double raw = innerResult.Score;
        double normalized;
        lock (_statsLock)
        {
            // Welford online update.
            _count++;
            double delta = raw - _mean;
            _mean += delta / _count;
            double delta2 = raw - _mean;
            _m2 += delta * delta2;

            if (_count >= _warmupSamples && _count >= 2)
            {
                double std = Math.Sqrt(_m2 / (_count - 1));
                normalized = (raw - _mean) / Math.Max(std, 1e-9);
            }
            else
            {
                normalized = raw;  // warmup pass-through
            }
        }
        return innerResult with { Score = normalized };
    }

    /// <summary>Reset running statistics.</summary>
    public void Reset()
    {
        lock (_statsLock)
        {
            _count = 0;
            _mean = 0;
            _m2 = 0;
        }
    }
}

/// <summary>
/// Defensive bounds-clipping <see cref="IObjective"/> wrapper. Clamps
/// each dimension of the input vector to <c>[Min, Max]</c> from the
/// inner objective's <see cref="DesignVariableInfo"/> before dispatching.
/// </summary>
/// <remarks>
/// Useful for optimizers whose mutation / crossover operators don't
/// natively respect bounds (NSGA-II SBX overshoots ~3 % of the time at
/// default eta; CMA-ES sampling can produce out-of-bounds candidates
/// after large covariance updates). The wrapper preserves any
/// out-of-bounds candidate's structure but pins each dim into legal
/// range — the inner objective then sees only legal inputs.
/// </remarks>
public sealed class BoundedObjective : IObjective
{
    private readonly IObjective _inner;
    private readonly double[] _lo;
    private readonly double[] _hi;

    /// <inheritdoc />
    public int DimensionCount => _inner.DimensionCount;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

    /// <summary>Construct a bounds-clipping wrapper.</summary>
    public BoundedObjective(IObjective inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _lo = new double[_inner.DimensionCount];
        _hi = new double[_inner.DimensionCount];
        for (int i = 0; i < _inner.DimensionCount; i++)
        {
            _lo[i] = _inner.Variables[i].Min;
            _hi[i] = _inner.Variables[i].Max;
        }
    }

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        if (vector.Length != _inner.DimensionCount)
            throw new ArgumentException(
                $"Vector length {vector.Length} does not match DimensionCount {_inner.DimensionCount}.",
                nameof(vector));

        // Hot-path optimisation (audit 12-perf §1.1): clamp into a
        // stack-allocated buffer instead of heap-allocating a fresh
        // double[] per Evaluate. SA vectors top out around 31 dims (the
        // rocket SA registry width); the stackalloc cap of 128 covers
        // every IObjective dimensioned to fit on the stack, and falls
        // back to ArrayPool for anything larger.
        int len = vector.Length;
        Span<double> clamped = len <= StackallocLimit
            ? stackalloc double[len]
            : default;
        double[]? rented = null;
        if (clamped.IsEmpty)
        {
            rented = ArrayPool<double>.Shared.Rent(len);
            clamped = rented.AsSpan(0, len);
        }
        try
        {
            for (int i = 0; i < len; i++)
                clamped[i] = Math.Clamp(vector[i], _lo[i], _hi[i]);
            return _inner.Evaluate(clamped, ct);
        }
        finally
        {
            if (rented is not null) ArrayPool<double>.Shared.Return(rented);
        }
    }

    private const int StackallocLimit = 128;
}

/// <summary>
/// Surrogate-replacement <see cref="IObjective"/> wrapper. The first
/// <see cref="Budget"/> evaluations are dispatched to the inner
/// objective and used as training data for a Bayesian GP; subsequent
/// evaluations are served from the GP posterior mean. The optimizer
/// sees a single <see cref="IObjective"/> interface and never knows
/// the swap happened.
/// </summary>
/// <remarks>
/// Use when the inner objective is expensive (CFD-validated rocket
/// cycle; multi-second voxel build) and the optimizer wants many more
/// evaluations than the compute budget admits — SA on 10 k iterations
/// over a 100-call physics budget, CMA-ES dense neighbourhood
/// exploration, hybrid SA → CMA-ES handoff. ADR-032 follow-on.
///
/// Infeasible scores (<see cref="double.PositiveInfinity"/>) are
/// returned verbatim during the budget phase but are NOT fed into the
/// GP — Cholesky decomposition cannot tolerate +Inf entries, and the
/// GP is only meaningful on the feasible region. Budget calls count
/// toward the budget regardless of feasibility, so a region that is
/// always infeasible exhausts the budget and the wrapper then falls
/// back to the inner objective until a finite-score training point
/// arrives.
///
/// Determinism: given the same call sequence + same inner objective,
/// the same vector at the same call index returns the same score.
/// The GP fit is lazy (on first surrogate call) and re-fits when new
/// finite training points arrive.
///
/// Thread-safety: training-set mutation and the lazy fit are guarded
/// by an internal lock; the GP <see cref="GaussianProcessSurrogate.Predict"/>
/// call is invoked under the lock so the wrapper never reads a half-
/// updated Cholesky factor.
/// </remarks>
public sealed class SurrogateObjective : IObjective
{
    private readonly IObjective _inner;
    private readonly GaussianProcessSurrogate _surrogate;
    private readonly int _budget;
    private readonly object _lock = new();
    private readonly List<double[]> _trainX = new();
    private readonly List<double> _trainY = new();
    private int _innerCallsConsumed;
    private bool _fitDirty;
    private long _innerCallCount;
    private long _surrogateCallCount;

    /// <summary>Budget (inner-objective call count before the surrogate takes over).</summary>
    public int Budget => _budget;

    /// <summary>Cumulative count of inner-objective evaluations.</summary>
    public long InnerCallCount => Interlocked.Read(ref _innerCallCount);

    /// <summary>Cumulative count of surrogate evaluations.</summary>
    public long SurrogateCallCount => Interlocked.Read(ref _surrogateCallCount);

    /// <summary>Current size of the surrogate training set (finite scores only).</summary>
    public int TrainingSize
    {
        get { lock (_lock) return _trainY.Count; }
    }

    /// <inheritdoc />
    public int DimensionCount => _inner.DimensionCount;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

    /// <summary>
    /// Construct a surrogate-replacement wrapper.
    /// </summary>
    /// <param name="inner">Expensive objective being approximated.</param>
    /// <param name="budget">Number of inner-objective calls to make
    /// before switching to surrogate predictions. Must be ≥ 1.</param>
    /// <param name="surrogate">GP surrogate. Its
    /// <see cref="GaussianProcessSurrogate.DimensionCount"/> must equal
    /// <paramref name="inner"/>'s <see cref="IObjective.DimensionCount"/>;
    /// its bounds should match <see cref="IObjective.Variables"/>'
    /// <c>(Min, Max)</c> ranges (this is not enforced — the caller is
    /// responsible for matching).</param>
    public SurrogateObjective(IObjective inner, int budget, GaussianProcessSurrogate surrogate)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _surrogate = surrogate ?? throw new ArgumentNullException(nameof(surrogate));
        if (budget < 1)
            throw new ArgumentOutOfRangeException(nameof(budget),
                $"Budget must be ≥ 1; got {budget}.");
        if (surrogate.DimensionCount != inner.DimensionCount)
            throw new ArgumentException(
                $"Surrogate DimensionCount ({surrogate.DimensionCount}) must equal inner "
              + $"DimensionCount ({inner.DimensionCount}).",
                nameof(surrogate));
        _budget = budget;
    }

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        if (vector.Length != _inner.DimensionCount)
            throw new ArgumentException(
                $"Vector length {vector.Length} does not match DimensionCount {_inner.DimensionCount}.",
                nameof(vector));

        bool useInner;
        lock (_lock)
        {
            useInner = _innerCallsConsumed < _budget;
        }

        if (useInner)
        {
            // Pass the span straight through to the inner objective.
            // Only allocate a permanent copy if the score is finite —
            // infeasible budget calls don't enter the training set, so
            // the array is wasted in that path.
            var r = _inner.Evaluate(vector, ct);
            Interlocked.Increment(ref _innerCallCount);
            if (double.IsFinite(r.Score))
            {
                var copy = vector.ToArray();
                lock (_lock)
                {
                    _innerCallsConsumed++;
                    _trainX.Add(copy);
                    _trainY.Add(r.Score);
                    _fitDirty = true;
                }
            }
            else
            {
                lock (_lock) _innerCallsConsumed++;
            }
            return r;
        }

        // Surrogate path. The GP Predict API takes a double[], so we
        // materialise here — but only on this branch, not on every
        // call. Fit lazily under the lock so concurrent first-surrogate-
        // call threads cooperate. The lock yields a nullable mean —
        // null means "no training points yet, fall back to the inner
        // objective without a second budget tick".
        double? surrogateMean = null;
        double[]? vArr = null;
        lock (_lock)
        {
            if (_trainY.Count > 0)
            {
                if (_fitDirty)
                {
                    _surrogate.Fit(_trainX.ToArray(), _trainY.ToArray());
                    _fitDirty = false;
                }
                vArr = vector.ToArray();
                var (m, _) = _surrogate.Predict(vArr);
                surrogateMean = m;
            }
        }

        if (surrogateMean.HasValue)
        {
            Interlocked.Increment(ref _surrogateCallCount);
            return new EvaluationResult(
                Score:                   surrogateMean.Value,
                Violations:              Array.Empty<FeasibilityViolation>(),
                EngineSpecificBreakdown: null);
        }

        // Budget exhausted with no finite training points — every
        // budget call was infeasible. Fall back to the inner objective
        // and feed any finite score we land on into the training set
        // for subsequent calls.
        var fallback = _inner.Evaluate(vector, ct);
        Interlocked.Increment(ref _innerCallCount);
        if (double.IsFinite(fallback.Score))
        {
            var copy = vArr ?? vector.ToArray();
            lock (_lock)
            {
                _trainX.Add(copy);
                _trainY.Add(fallback.Score);
                _fitDirty = true;
            }
        }
        return fallback;
    }

    /// <summary>
    /// Clear the training set and the counters; the next call to
    /// <see cref="Evaluate"/> will dispatch to the inner objective.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _trainX.Clear();
            _trainY.Clear();
            _innerCallsConsumed = 0;
            _fitDirty = false;
        }
        Interlocked.Exchange(ref _innerCallCount, 0);
        Interlocked.Exchange(ref _surrogateCallCount, 0);
    }
}

/// <summary>
/// Async-execution <see cref="IObjective"/> wrapper for off-process /
/// off-machine physics oracles (e.g. the <c>voxelforge-eval</c>
/// subprocess oracle from ADR-016; future cloud-backed CFD runners).
/// Exposes an explicit <see cref="EvaluateAsync"/> path; the sync
/// <see cref="IObjective.Evaluate"/> shim blocks on it.
/// </summary>
/// <remarks>
/// Use when the inner physics evaluation is naturally async — process
/// start-up latency dominates inner CPU cost, so an N-chain parallel
/// optimizer wants to pipeline multiple subprocesses concurrently
/// rather than serialise them through the sync <see cref="IObjective"/>
/// contract. ADR-032 follow-on; ADR-016 canonical consumer.
///
/// Cancellation: <see cref="EvaluateAsync"/> forwards the token
/// directly to the wrapped async function so cancellation propagates
/// at the async-function's natural <c>await</c> boundaries. The sync
/// shim blocks on <c>GetAwaiter().GetResult()</c>; on cancellation the
/// inner <see cref="OperationCanceledException"/> surfaces unwrapped
/// (consistent with the existing <see cref="IObjective.Evaluate"/>
/// contract — implementations propagate cancellation as exceptions).
///
/// Determinism: if the wrapped async function is deterministic, so is
/// this wrapper. The sync shim's <c>GetAwaiter().GetResult()</c> path
/// does not deadlock under <c>SynchronizationContext.Current</c>-free
/// callers (the optimizer's threads have no sync context). Avoid
/// calling <see cref="Evaluate"/> from a UI / ASP.NET thread that
/// pins a sync context — call <see cref="EvaluateAsync"/> directly
/// from such callers instead.
/// </remarks>
public sealed class AsyncObjective : IObjective
{
    private readonly int _dimensionCount;
    private readonly IReadOnlyList<DesignVariableInfo> _variables;
    private readonly Func<ReadOnlyMemory<double>, CancellationToken, Task<EvaluationResult>> _asyncEvaluate;

    /// <inheritdoc />
    public int DimensionCount => _dimensionCount;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _variables;

    /// <summary>
    /// Construct an async-execution wrapper.
    /// </summary>
    /// <param name="dimensionCount">Dimension count of the search vector.</param>
    /// <param name="variables">Per-dimension descriptors. Length must
    /// equal <paramref name="dimensionCount"/>.</param>
    /// <param name="asyncEvaluate">Async evaluation function. Must
    /// honour the <see cref="CancellationToken"/> at its natural
    /// <c>await</c> boundaries.</param>
    public AsyncObjective(
        int dimensionCount,
        IReadOnlyList<DesignVariableInfo> variables,
        Func<ReadOnlyMemory<double>, CancellationToken, Task<EvaluationResult>> asyncEvaluate)
    {
        if (dimensionCount < 1)
            throw new ArgumentOutOfRangeException(nameof(dimensionCount),
                $"DimensionCount must be ≥ 1; got {dimensionCount}.");
        _variables = variables ?? throw new ArgumentNullException(nameof(variables));
        if (variables.Count != dimensionCount)
            throw new ArgumentException(
                $"variables.Count ({variables.Count}) != dimensionCount ({dimensionCount}).",
                nameof(variables));
        _asyncEvaluate = asyncEvaluate ?? throw new ArgumentNullException(nameof(asyncEvaluate));
        _dimensionCount = dimensionCount;
    }

    /// <summary>
    /// Async evaluation entry point. Forwards <paramref name="vector"/>
    /// + <paramref name="ct"/> to the wrapped async function.
    /// </summary>
    public Task<EvaluationResult> EvaluateAsync(
        ReadOnlyMemory<double> vector,
        CancellationToken ct = default)
    {
        if (vector.Length != _dimensionCount)
            throw new ArgumentException(
                $"Vector length {vector.Length} does not match DimensionCount {_dimensionCount}.",
                nameof(vector));
        return _asyncEvaluate(vector, ct);
    }

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        if (vector.Length != _dimensionCount)
            throw new ArgumentException(
                $"Vector length {vector.Length} does not match DimensionCount {_dimensionCount}.",
                nameof(vector));
        var mem = vector.ToArray().AsMemory();
        return _asyncEvaluate(mem, ct).GetAwaiter().GetResult();
    }
}
