// Sprint OPT-1 production wiring (2026-04-25): MultiChainSession encapsulates
// a running MultiChainOptimizer.Run on a worker task, exposes thread-safe
// progress snapshots for the main dispatch loop, and fans every feasible
// candidate to the supplied Pareto-offer callback. Mirrors the role of the
// SimulatedAnnealingOptimizer in the single-chain path so the main loop can
// poll completion + cancel + finalize uniformly across both modes.
//
// Lifecycle:
//   1. Caller constructs MultiChainSession with bounds, iters, seed,
//      chainCount, evaluator, paretoOffer, baselineParams.
//   2. Start() spawns the worker task.
//   3. Main loop polls IsComplete each iteration (tens of ms), reads
//      Snapshot for UI updates, calls Cancel() on user stop.
//   4. On completion (or cancellation), Result holds the MultiChain
//      result for FinalizeMultiChainOpt.
//
// Thread-safety: per-iteration progress writes from N chain threads use
// Interlocked counters + a lock-protected best snapshot. Main-loop reads
// take the same lock; reads are cheap (one snapshot copy).

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Voxelforge.Optimization;

namespace Voxelforge.AppOptimization;

/// <summary>
/// Async-task wrapper around <see cref="MultiChainOptimizer"/> for the
/// main dispatch loop. Holds the running <see cref="Task{TResult}"/>,
/// the <see cref="CancellationTokenSource"/>, and a thread-safe
/// progress snapshot updated from inside chain workers.
/// </summary>
public sealed class MultiChainSession : IDisposable
{
    private readonly MultiChainOptimizer _optimizer;
    private readonly Func<double[], (double score, object? breakdown)> _evaluator;
    private readonly Action<double[], double, object?>? _onCandidateScored;
    private readonly double[]? _initialCandidate;
    private readonly CancellationTokenSource _cts = new();

    private Task<MultiChainOptimizer.Result>? _task;

    // Thread-safe progress trackers.
    private long _iterationsCounted;
    private readonly object _bestLock = new();
    private double _bestScore = double.PositiveInfinity;
    private double[]? _bestParams;
    private object? _bestBreakdown;
    private int _bestChainIndex = -1;
    private readonly Stopwatch _stopwatch = new();

    public int ChainCount => _optimizer.ChainCount;
    public int MaxIterations { get; }
    public CancellationToken Token => _cts.Token;
    public bool IsComplete => _task is not null && _task.IsCompleted;
    public bool IsCancelled => _cts.IsCancellationRequested;

    public MultiChainSession(
        (double Min, double Max)[] bounds,
        int maxIterations,
        int baseSeed,
        int chainCount,
        Func<double[], (double score, object? breakdown)> evaluator,
        Action<double[], double, object?>? onCandidateScored = null,
        double[]? initialCandidate = null)
    {
        _optimizer = new MultiChainOptimizer(
            bounds:           bounds,
            maxIterations:    maxIterations,
            baseSeed:         baseSeed,
            chainCount:       chainCount);
        MaxIterations = maxIterations;
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _onCandidateScored = onCandidateScored;
        _initialCandidate = initialCandidate;
    }

    /// <summary>
    /// IObjective-based ctor (Sprint 0 / Wave 1, 2026-05-05). Wraps
    /// the engine-family-agnostic <see cref="IObjective"/> shape into
    /// the legacy Func evaluator. Bounds are derived from
    /// <c>objective.Variables</c> via <c>DesignVariableInfo.ToBoundsArray</c>.
    /// <para>
    /// The <paramref name="onCandidateScored"/> callback receives the
    /// engine-specific breakdown (i.e. <see cref="EvaluationResult.EngineSpecificBreakdown"/>)
    /// directly so existing rocket-side consumers that downcast to
    /// <c>RegenScoreResult</c> keep working unchanged.
    /// </para>
    /// </summary>
    public MultiChainSession(
        IObjective objective,
        int maxIterations,
        int baseSeed,
        int chainCount,
        Action<double[], double, object?>? onCandidateScored = null,
        double[]? initialCandidate = null)
    {
        if (objective is null) throw new ArgumentNullException(nameof(objective));
        _optimizer = new MultiChainOptimizer(
            objective:        objective,
            maxIterations:    maxIterations,
            baseSeed:         baseSeed,
            chainCount:       chainCount);
        MaxIterations = maxIterations;
        _evaluator = cand =>
        {
            var r = objective.Evaluate(cand, _cts.Token);
            // Pass the engine-specific record through as the breakdown
            // (NOT the wrapping EvaluationResult). Consumers that already
            // expect a RegenScoreResult / AirbreathingResult downcast
            // continue to work without modification.
            return (r.Score, r.EngineSpecificBreakdown);
        };
        _onCandidateScored = onCandidateScored;
        _initialCandidate = initialCandidate;
    }

    /// <summary>
    /// Start the worker task that runs <see cref="MultiChainOptimizer.Run"/>.
    /// </summary>
    public void Start()
    {
        if (_task is not null) throw new InvalidOperationException("Session already started.");
        _stopwatch.Start();
        _task = Task.Run(() => _optimizer.Run(
            evaluator:          _evaluator,
            initialCandidate:   _initialCandidate,
            cancellationToken:  _cts.Token,
            onProgress:         OnProgress));
    }

    private void OnProgress(MultiChainOptimizer.ChainProgress p)
    {
        Interlocked.Increment(ref _iterationsCounted);

        // Update global-best snapshot if this candidate beat it.
        if (!double.IsNaN(p.Score) && p.Score < Volatile.Read(ref _bestScore))
        {
            lock (_bestLock)
            {
                if (p.Score < _bestScore)
                {
                    _bestScore = p.Score;
                    _bestParams = (double[])p.Candidate.Clone();
                    _bestBreakdown = p.Breakdown;
                    _bestChainIndex = p.ChainIndex;
                }
            }
        }

        // Fan every feasible candidate to the Pareto callback.
        if (_onCandidateScored is not null && double.IsFinite(p.Score))
        {
            _onCandidateScored(p.Candidate, p.Score, p.Breakdown);
        }
    }

    /// <summary>
    /// Snapshot of the running session for UI updates. Returns the
    /// global-best params + score so far + total iterations counted
    /// across all chains (sum, not per-chain).
    /// </summary>
    public sealed record Snapshot(
        long      IterationsCounted,
        double    BestScore,
        double[]? BestParams,
        object?   BestBreakdown,
        int       BestChainIndex,
        long      ElapsedMs);

    public Snapshot ReadSnapshot()
    {
        lock (_bestLock)
        {
            return new Snapshot(
                IterationsCounted: Volatile.Read(ref _iterationsCounted),
                BestScore:         _bestScore,
                BestParams:        _bestParams is null ? null : (double[])_bestParams.Clone(),
                BestBreakdown:     _bestBreakdown,
                BestChainIndex:    _bestChainIndex,
                ElapsedMs:         _stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Signal the running optimization to cancel. Returns immediately;
    /// the worker task will observe the cancellation token at the next
    /// iteration boundary. <see cref="IsComplete"/> goes true once all
    /// chains have unwound.
    /// </summary>
    public void Cancel()
    {
        if (!_cts.IsCancellationRequested) _cts.Cancel();
    }

    /// <summary>
    /// Block until the worker task completes (typically after a Cancel()).
    /// Returns the final MultiChain result for FinalizeMultiChainOpt.
    /// Throws if Start() was never called.
    /// </summary>
    public MultiChainOptimizer.Result AwaitResult()
    {
        if (_task is null) throw new InvalidOperationException("Session never started.");
        try { return _task.GetAwaiter().GetResult(); }
        finally { _stopwatch.Stop(); }
    }

    /// <summary>Dispose the cancellation token source.</summary>
    public void Dispose() => _cts.Dispose();
}
