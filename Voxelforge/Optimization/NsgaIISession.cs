// NsgaIISession.cs — T2.4b: async-task wrapper around NsgaIIOptimizer for the
// main dispatch loop in Program.cs. Mirrors the role of MultiChainSession for
// multi-chain SA, allowing the main loop to poll completion + cancel + finalize
// uniformly across SA and NSGA-II modes.
//
// Progress tracking: wraps the user-supplied IObjective in a CountingObjective
// that increments an atomic counter on every Evaluate() call. This lets the
// session expose live evaluation progress without modifying NsgaIIOptimizer.
// The best-so-far scalar score is also tracked concurrently so the convergence
// panel shows the best feasible score found to date.
//
// Lifecycle:
//   1. Caller constructs NsgaIISession with an IObjective, objectiveExtractor,
//      populationSize, maxGenerations, seed, and optional onCandidateScored.
//   2. Start() spawns the worker task (NsgaIIOptimizer.Run on a ThreadPool thread).
//   3. Main loop polls IsComplete each tick (50 ms), reads Snapshot for UI updates,
//      calls Cancel() on user stop.
//   4. On completion, AwaitResult() returns the NsgaIIOptimizer.Result with the
//      full Pareto front for FinalizeNsgaOpt.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Voxelforge.Optimization;

namespace Voxelforge.AppOptimization;

/// <summary>
/// Async-task wrapper around <see cref="NsgaIIOptimizer"/> for the
/// main dispatch loop. Tracks per-evaluation progress via an
/// <see cref="IObjective"/> wrapper; exposes a thread-safe snapshot
/// for UI polling.
/// </summary>
public sealed class NsgaIISession : IDisposable
{
    // ── Inner: wraps IObjective to count evals + track best score ──────

    private sealed class CountingObjective : IObjective
    {
        private readonly IObjective _inner;
        private long _evaluations;

        private readonly object _bestLock = new();
        private double _bestScore = double.PositiveInfinity;
        private double[]? _bestParams;
        private object? _bestBreakdown;

        public CountingObjective(IObjective inner) => _inner = inner;

        public int DimensionCount => _inner.DimensionCount;
        public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

        public long EvaluationCount => Volatile.Read(ref _evaluations);

        public (double BestScore, double[]? BestParams, object? BestBreakdown) ReadBest()
        {
            lock (_bestLock)
                return (_bestScore,
                        _bestParams is null ? null : (double[])_bestParams.Clone(),
                        _bestBreakdown);
        }

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            var result = _inner.Evaluate(vector, ct);
            Interlocked.Increment(ref _evaluations);

            if (double.IsFinite(result.Score) && result.Score < Volatile.Read(ref _bestScore))
            {
                var paramsCopy = vector.ToArray();
                lock (_bestLock)
                {
                    if (result.Score < _bestScore)
                    {
                        _bestScore = result.Score;
                        _bestParams = paramsCopy;
                        _bestBreakdown = result.EngineSpecificBreakdown;
                    }
                }
            }

            return result;
        }
    }

    // ── Fields ──────────────────────────────────────────────────────────

    private readonly CountingObjective _countingObjective;
    private readonly Func<EvaluationResult, double[]> _objectiveExtractor;
    private readonly Action<double[], double, object?>? _onCandidateScored;
    private readonly int _populationSize;
    private readonly int _maxGenerations;
    private readonly int _seed;
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _stopwatch = new();

    private Task<NsgaIIOptimizer.Result>? _task;

    // ── Public properties ────────────────────────────────────────────────

    public bool IsComplete => _task is not null && _task.IsCompleted;
    public bool IsCancelled => _cts.IsCancellationRequested;

    /// <summary>
    /// Total expected evaluations: initial population + one offspring batch
    /// per generation. Used as the denominator for the progress bar.
    /// </summary>
    public long TotalExpectedEvaluations => (long)_populationSize * (1 + _maxGenerations);

    // ── Constructor ──────────────────────────────────────────────────────

    public NsgaIISession(
        IObjective objective,
        Func<EvaluationResult, double[]> objectiveExtractor,
        int populationSize,
        int maxGenerations,
        int seed = 42,
        Action<double[], double, object?>? onCandidateScored = null)
    {
        if (objective is null) throw new ArgumentNullException(nameof(objective));
        if (objectiveExtractor is null) throw new ArgumentNullException(nameof(objectiveExtractor));

        _countingObjective = new CountingObjective(objective);
        _objectiveExtractor = objectiveExtractor;
        _onCandidateScored = onCandidateScored;
        _populationSize = populationSize;
        _maxGenerations = maxGenerations;
        _seed = seed;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────

    /// <summary>Start the NSGA-II run on a ThreadPool worker.</summary>
    public void Start()
    {
        if (_task is not null) throw new InvalidOperationException("Session already started.");
        _stopwatch.Start();

        // Wrap the objective extractor to also fan feasible candidates to the
        // onCandidateScored callback (mirrors MultiChainSession.OnCandidateScored).
        // The extractor must be pure (called from the background thread).
        Func<EvaluationResult, double[]> wrappedExtractor = eval =>
        {
            var objs = _objectiveExtractor(eval);
            if (_onCandidateScored is not null
                && eval.EngineSpecificBreakdown is not null
                && double.IsFinite(eval.Score))
            {
                _onCandidateScored(Array.Empty<double>(), eval.Score, eval.EngineSpecificBreakdown);
            }
            return objs;
        };

        var optimizer = new NsgaIIOptimizer(
            objective:          _countingObjective,
            objectiveExtractor: wrappedExtractor,
            populationSize:     _populationSize,
            maxGenerations:     _maxGenerations,
            seed:               _seed);

        _task = Task.Run(() => optimizer.Run(_cts.Token));
    }

    /// <summary>
    /// Snapshot of live progress for UI polling. Safe to call from
    /// any thread while <see cref="Start"/> has been called.
    /// </summary>
    public sealed record Snapshot(
        long      EvaluationsCompleted,
        long      TotalExpectedEvaluations,
        double    BestScore,
        double[]? BestParams,
        object?   BestBreakdown,
        long      ElapsedMs);

    public Snapshot ReadSnapshot()
    {
        var (bestScore, bestParams, bestBreakdown) = _countingObjective.ReadBest();
        return new Snapshot(
            EvaluationsCompleted:    _countingObjective.EvaluationCount,
            TotalExpectedEvaluations: TotalExpectedEvaluations,
            BestScore:               bestScore,
            BestParams:              bestParams,
            BestBreakdown:           bestBreakdown,
            ElapsedMs:               _stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Signal the run to stop at the next evaluation boundary.</summary>
    public void Cancel()
    {
        if (!_cts.IsCancellationRequested) _cts.Cancel();
    }

    /// <summary>Block until the run completes. Returns the final NSGA-II result.</summary>
    public NsgaIIOptimizer.Result AwaitResult()
    {
        if (_task is null) throw new InvalidOperationException("Session never started.");
        try { return _task.GetAwaiter().GetResult(); }
        finally { _stopwatch.Stop(); }
    }

    public void Dispose() => _cts.Dispose();
}
