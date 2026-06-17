// Optimizer.cs — Simulated annealing with reheating restarts.
//
// Standalone re-implementation for the regen chamber designer. Pattern-
// matched to proven SA behaviors (auto cooling rate, stagnation restart,
// mixed perturbation, min-perturbation floor) but written fresh here so
// this project has no compile-time dependency on any other project.
//
// Behaviors:
//   • Auto-computed cooling rate hits ~1 % of T₀ at 80 % of budget.
//   • Stagnation restart: if no improvement for budget/5 iterations,
//     jump back to best-ever with 35 % perturbation + reheat to 40 % T₀.
//   • Minimum perturbation floor of 5 % of range (prevents freeze).
//   • Mixed 50/50 all-dimension vs single-dimension perturbation.
//   • Elitist — best-ever always tracked separately from acceptance walk.
//   • Warm-start supported via SetInitialCandidate().

namespace Voxelforge.Optimization;

[Deterministic]
public sealed class SimulatedAnnealingOptimizer
{
    private readonly Random _rng;
    private readonly int _dim;
    private readonly double[] _lo;
    private readonly double[] _hi;
    private readonly int _maxIter;
    private readonly double _T0;
    private readonly double _coolingRate;

    private double[] _current;
    private double _currentScore = double.MaxValue;

    private double[] _best;
    private double _bestScore = double.MaxValue;
    private object? _bestBreakdown;

    private int _itersSinceImprove;
    private readonly int _stagnationThreshold;
    private int _restartCount;

    private int _iter;
    private readonly List<IterationRecord> _history = new();

    private const double MinPerturbFraction = 0.05;
    private const double ReheatFraction = 0.40;
    private const double RestartPerturbFraction = 0.35;
    private const double MinTemperature = 0.01;
    private const int MaxHistorySize = 2000;

    public double[] BestParams => (double[])_best.Clone();
    public double BestScore => _bestScore;
    public object? BestBreakdown => _bestBreakdown;
    public int Iteration => _iter;
    public int MaxIterations => _maxIter;
    public double Temperature { get; private set; }
    public int RestartCount => _restartCount;

    // Early-termination on convergence. When the SA has restarted
    // enough times without the best-score improving, further iterations
    // rarely produce a new best. Budget stops with IsComplete = true
    // so the main loop runs FinalizeOpt. Tunable via
    // MaxRestartsWithoutImprovement (default 3) — three full stagnation
    // cycles is ~60 % of a typical budget on a 300-iter run.
    public int MaxRestartsWithoutImprovement { get; set; } = 3;
    private int _restartsSinceLastBest;

    // Early-termination on persistent infeasibility. If every
    // candidate in a window returns double.PositiveInfinity (the
    // FeasibilityGate infeasible sentinel), no stagnation restart will ever
    // help — the search space has no feasible point for the given conditions.
    // On a too-large thrust (e.g. 50 kN where constraints calibrated for
    // < 10 kN all fail), SA would otherwise burn the full iteration budget
    // before ConvergenceReached trips on zero-new-best restarts. This
    // counter fires the exit as soon as the infeasible streak exceeds
    // MaxConsecutiveInfeasibleBeforeExit (default 60), so a 300-iter run
    // can bail after 1-2 minutes instead of 5-6 hours. Set to 0 to disable.
    public int MaxConsecutiveInfeasibleBeforeExit { get; set; } = 60;
    private int _consecutiveInfeasible;
    private bool _infeasibleExit;

    // Hard memory abort. When a candidate voxel build throws
    // MemoryBudgetExceededException, the design is too large
    // for the user's memory cap and further iterations will fail identically.
    // Caller invokes SignalMemoryAbort() which trips IsComplete so the main
    // dispatch loop unwinds cleanly instead of spinning on the same error.
    private bool _memoryAbort;
    public bool MemoryAbortTripped => _memoryAbort;
    public void SignalMemoryAbort()
    {
        _memoryAbort    = true;
        _infeasibleExit = true;   // piggy-back on the ConvergenceReached path
    }

    public bool IsComplete => _iter >= _maxIter
                           || ConvergenceReached;

    /// <summary>
    /// True when the SA has concluded that further iterations are
    /// unlikely to produce a new best. Exposed read-only so the UI
    /// can report "stopped at iter 180 / 300 (converged)" rather
    /// than "stopped at iter 300". Also true when the
    /// persistent-infeasibility exit fires so downstream callers can
    /// treat it as a normal convergence without further ceremony.
    /// </summary>
    public bool ConvergenceReached =>
        (_restartsSinceLastBest >= MaxRestartsWithoutImprovement
         && _iter > 0
         && _iter >= Math.Max(30, _maxIter / 10))
        || _infeasibleExit;

    /// <summary>
    /// True when <see cref="ConvergenceReached"/> was tripped
    /// specifically by the persistent-infeasibility exit (not by
    /// iteration cap or stagnation-restart convergence). The UI
    /// surfaces a more useful status message in this case: "Every
    /// candidate was infeasible — try a smaller thrust, larger voxel,
    /// or relaxed thresholds."
    /// </summary>
    public bool InfeasibleExitTripped => _infeasibleExit;

    public IReadOnlyList<IterationRecord> History => _history;

    public SimulatedAnnealingOptimizer(
        (double Min, double Max)[] bounds,
        int maxIterations,
        int seed,
        double initialTemperature = 800.0)
    {
        _dim = bounds.Length;
        _lo = bounds.Select(b => b.Min).ToArray();
        _hi = bounds.Select(b => b.Max).ToArray();
        _maxIter = maxIterations;
        _T0 = initialTemperature;
        Temperature = _T0;
        _rng = new Random(seed);

        int coolingSteps = Math.Max((int)(maxIterations * 0.8), 10);
        _coolingRate = Math.Pow(0.01, 1.0 / coolingSteps);

        _stagnationThreshold = Math.Max(maxIterations / 5, 15);

        _current = RandomCandidate();
        _best = (double[])_current.Clone();
    }

    public void SetInitialCandidate(double[] candidate)
    {
        if (_iter > 0) return;
        for (int i = 0; i < _dim; i++)
            _current[i] = Math.Clamp(candidate[i], _lo[i], _hi[i]);
        _best = (double[])_current.Clone();
    }

    /// <summary>
    /// Sprint T1.1 (2026-04-25): Replace this chain's <c>_current</c> walk
    /// state with the given (params, score). Used by
    /// <see cref="MultiChainOptimizer"/> to migrate elites from neighbouring
    /// chains. Does NOT touch <c>_best</c> — the chain retains its own
    /// historical best independent of received elites. Caller must clone
    /// the params array if it intends to keep modifying it.
    /// </summary>
    public void MigrateFrom(double[] migratedParams, double migratedScore)
    {
        if (migratedParams.Length != _dim)
            throw new System.ArgumentException(
                $"migratedParams length {migratedParams.Length} ≠ optimizer dim {_dim}", nameof(migratedParams));
        for (int i = 0; i < _dim; i++)
            _current[i] = System.Math.Clamp(migratedParams[i], _lo[i], _hi[i]);
        _currentScore = migratedScore;
        // If the migrated candidate beats the local best, update _best too.
        if (migratedScore < _bestScore)
        {
            _best = (double[])_current.Clone();
            _bestScore = migratedScore;
            // _bestBreakdown stays null for migrated elites; original
            // breakdown lives on the donor chain. UI consumers can recover
            // it from the donor's BestBreakdown if they need it.
        }
    }

    public double[] NextCandidate()
    {
        if (_iter == 0) return (double[])_current.Clone();
        return Perturb(_current);
    }

    public bool ReportScore(double[] candidate, double score, object? breakdown)
    {
        bool accepted;
        bool newBest = false;

        if (_iter == 0)
        {
            _current = (double[])candidate.Clone();
            _currentScore = score;
            _best = (double[])candidate.Clone();
            _bestScore = score;
            _bestBreakdown = breakdown;
            accepted = true;
            newBest = true;
        }
        else
        {
            double delta = score - _currentScore;
            if (delta <= 0)
                accepted = true;
            else
            {
                double p = Math.Exp(-delta / Math.Max(Temperature, MinTemperature));
                accepted = _rng.NextDouble() < p;
            }
            if (accepted)
            {
                _current = (double[])candidate.Clone();
                _currentScore = score;
            }
            if (score < _bestScore)
            {
                _best = (double[])candidate.Clone();
                _bestScore = score;
                _bestBreakdown = breakdown;
                newBest = true;
            }
        }

        if (newBest)
        {
            _itersSinceImprove = 0;
            // Any new best resets the convergence counter. Three
            // restarts without a new best → stop.
            _restartsSinceLastBest = 0;
        }
        else
        {
            _itersSinceImprove++;
            if (_itersSinceImprove >= _stagnationThreshold)
            {
                _current = PerturbFrom(_best, RestartPerturbFraction);
                _currentScore = _bestScore;
                Temperature = _T0 * ReheatFraction;
                _itersSinceImprove = 0;
                _restartCount++;
                _restartsSinceLastBest++;
            }
        }

        // Track consecutive-infeasible streak. Trip the
        // InfeasibleExitTripped / ConvergenceReached flags once the
        // streak exceeds MaxConsecutiveInfeasibleBeforeExit so the
        // main dispatch loop stops feeding candidates through the
        // physics pipeline for a search space with no feasible point.
        if (double.IsPositiveInfinity(score) || double.IsNaN(score))
        {
            _consecutiveInfeasible++;
            if (MaxConsecutiveInfeasibleBeforeExit > 0
                && _consecutiveInfeasible >= MaxConsecutiveInfeasibleBeforeExit)
            {
                _infeasibleExit = true;
            }
        }
        else
        {
            _consecutiveInfeasible = 0;
        }

        if (_history.Count >= MaxHistorySize) _history.RemoveAt(0);
        _history.Add(new IterationRecord(
            Iteration: _iter,
            Score: score,
            Accepted: accepted,
            IsBest: newBest,
            Temperature: Temperature,
            Parameters: (double[])candidate.Clone()));

        _iter++;
        Temperature *= _coolingRate;

        return newBest;
    }

    private double[] RandomCandidate()
    {
        var p = new double[_dim];
        for (int i = 0; i < _dim; i++)
            p[i] = _lo[i] + _rng.NextDouble() * (_hi[i] - _lo[i]);
        return p;
    }

    private double[] Perturb(double[] current)
    {
        var p = (double[])current.Clone();
        double ratio = Temperature / _T0;
        double scale = Math.Max(ratio * 0.3, MinPerturbFraction);

        bool allDims = _rng.NextDouble() < 0.5;
        if (allDims)
        {
            for (int i = 0; i < _dim; i++) p[i] = PerturbOne(p[i], i, scale);
        }
        else
        {
            int k = _rng.Next(_dim);
            p[k] = PerturbOne(p[k], k, scale);
        }
        return p;
    }

    private double[] PerturbFrom(double[] origin, double scale)
    {
        var p = (double[])origin.Clone();
        for (int i = 0; i < _dim; i++) p[i] = PerturbOne(p[i], i, scale);
        return p;
    }

    private double PerturbOne(double v, int dim, double scale)
    {
        double range = _hi[dim] - _lo[dim];
        double delta = (_rng.NextDouble() * 2.0 - 1.0) * scale * range;
        return Math.Clamp(v + delta, _lo[dim], _hi[dim]);
    }
}

public sealed record IterationRecord(
    int Iteration,
    double Score,
    bool Accepted,
    bool IsBest,
    double Temperature,
    double[] Parameters);
