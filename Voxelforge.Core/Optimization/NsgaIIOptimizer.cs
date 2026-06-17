// NsgaIIOptimizer.cs — NSGA-II (Non-dominated Sorting Genetic Algorithm
// II) multi-objective optimizer. Issue #161 (T2.4a from CLAUDE.md
// optimization-infra Tier 2).
//
// Reference: Deb, Pratap, Agarwal, Meyarivan (2002),
// "A Fast and Elitist Multiobjective Genetic Algorithm: NSGA-II",
// IEEE TEC 6(2). https://www.iitk.ac.in/kangal/Deb_NSGA-II.pdf
//
// Why NSGA-II: SA collapses every objective to a scalar via weighted
// sum, forcing users to re-run with different weight vectors per
// objective axis. NSGA-II makes (peak-T, ΔP, mass) trade-offs first-
// class. The constraint-handling extension (Deb 2002 §V): infeasible
// candidates are dominated by ANY feasible candidate; within infeasible,
// candidates with smaller total constraint violation dominate.
//
// Plug-in compatibility: consumes the IObjective interface (single
// scalar Score) plus a user-supplied objectiveExtractor callback that
// converts the EvaluationResult into a vector of objectives. Keeps
// IObjective single-objective; the multi-objective view is per-call.
//
// Algorithm overview (one generation):
//   1. Tournament selection on parent pop P_t (size N) → mating pool
//   2. SBX crossover + polynomial mutation → offspring Q_t (size N)
//   3. Evaluate Q_t via IObjective
//   4. R_t = P_t ∪ Q_t (size 2N)
//   5. Fast non-dominated sort R_t → fronts F_1, F_2, ...
//   6. Build P_{t+1} from front 1, then front 2, ... until size N
//   7. Last partial front: trim by crowding distance (keep highest)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Voxelforge.Optimization;

/// <summary>
/// NSGA-II multi-objective optimizer with constraint handling.
/// Consumes <see cref="IObjective"/> for the engine-family-agnostic
/// optimizer/oracle boundary established in #155, plus an
/// <c>objectiveExtractor</c> callback that converts the
/// <see cref="EvaluationResult"/> into a per-individual objective
/// vector for Pareto sorting.
/// </summary>
[Deterministic]
public sealed class NsgaIIOptimizer
{
    private readonly IObjective _objective;
    private readonly Func<EvaluationResult, double[]> _objectiveExtractor;
    private readonly int _populationSize;
    private readonly int _maxGenerations;
    private readonly Random _rng;
    private readonly double _crossoverProb;
    private readonly double _mutationProb;
    private readonly double _sbxEta;
    private readonly double _mutEta;
    private readonly double _sbxEtaPlus1;
    private readonly double _sbxEtaPlus1Neg;
    private readonly double _sbxEtaPlus1Inv;
    private readonly double _mutEtaPlus1;
    private readonly double _mutEtaPlus1Inv;
    private readonly int _dim;
    private readonly double[] _lo;
    private readonly double[] _hi;

    /// <summary>
    /// Single individual: a candidate vector + its evaluation +
    /// the extracted objective vector + cached non-domination rank +
    /// crowding distance. Mutated only inside the optimizer.
    /// </summary>
    public sealed class Individual
    {
        public double[]          Vector            { get; }
        public EvaluationResult? Evaluation        { get; internal set; }
        public double[]?         Objectives        { get; internal set; }
        public int               Rank              { get; internal set; }
        public double            CrowdingDistance  { get; internal set; }
        public double            ConstraintViolation { get; internal set; }

        public Individual(double[] vector) { Vector = vector; }

        public bool IsFeasible => ConstraintViolation <= 0.0;
    }

    /// <summary>
    /// Final Pareto front + diagnostics.
    /// </summary>
    public sealed record Result(
        IReadOnlyList<Individual> ParetoFront,
        int                       GenerationsCompleted,
        long                      TotalEvaluations,
        long                      ElapsedMilliseconds);

    public NsgaIIOptimizer(
        IObjective objective,
        Func<EvaluationResult, double[]> objectiveExtractor,
        int populationSize,
        int maxGenerations,
        int seed = 42,
        double crossoverProb = 0.9,
        double mutationProb = -1.0,
        double sbxEta = 20.0,
        double mutEta = 20.0)
    {
        if (objective is null) throw new ArgumentNullException(nameof(objective));
        if (objectiveExtractor is null) throw new ArgumentNullException(nameof(objectiveExtractor));
        if (populationSize < 2) throw new ArgumentOutOfRangeException(nameof(populationSize), "populationSize must be ≥ 2");
        if (populationSize % 2 != 0) throw new ArgumentException("populationSize must be even", nameof(populationSize));
        if (maxGenerations < 1) throw new ArgumentOutOfRangeException(nameof(maxGenerations));
        if (crossoverProb < 0 || crossoverProb > 1) throw new ArgumentOutOfRangeException(nameof(crossoverProb));
        if (sbxEta <= 0) throw new ArgumentOutOfRangeException(nameof(sbxEta));
        if (mutEta <= 0) throw new ArgumentOutOfRangeException(nameof(mutEta));

        _objective = objective;
        _objectiveExtractor = objectiveExtractor;
        _populationSize = populationSize;
        _maxGenerations = maxGenerations;
        _rng = new Random(seed);
        _crossoverProb = crossoverProb;
        _sbxEta = sbxEta;
        _mutEta = mutEta;
        _sbxEtaPlus1    = sbxEta + 1.0;
        _sbxEtaPlus1Neg = -(sbxEta + 1.0);
        _sbxEtaPlus1Inv = 1.0 / (sbxEta + 1.0);
        _mutEtaPlus1    = mutEta + 1.0;
        _mutEtaPlus1Inv = 1.0 / (mutEta + 1.0);

        _dim = objective.DimensionCount;
        _mutationProb = mutationProb < 0 ? 1.0 / _dim : mutationProb;

        _lo = new double[_dim];
        _hi = new double[_dim];
        for (int i = 0; i < _dim; i++)
        {
            _lo[i] = objective.Variables[i].Min;
            _hi[i] = objective.Variables[i].Max;
        }
    }

    /// <summary>
    /// Run NSGA-II for <see cref="_maxGenerations"/> generations or
    /// until cancellation. Returns the final non-dominated front.
    /// </summary>
    [Deterministic]
    public Result Run(CancellationToken cancellationToken = default)
    {
        long swStart = Stopwatch.GetTimestamp();
        long evals = 0;

        // Initialize population uniformly at random in bounds.
        var population = new List<Individual>(_populationSize);
        for (int i = 0; i < _populationSize; i++)
        {
            var v = new double[_dim];
            for (int j = 0; j < _dim; j++)
                v[j] = _lo[j] + _rng.NextDouble() * (_hi[j] - _lo[j]);
            population.Add(new Individual(v));
        }
        EvaluateAll(population, cancellationToken, ref evals);
        AssignRanksAndCrowding(population);

        for (int gen = 0; gen < _maxGenerations; gen++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // 1-2. Selection + crossover + mutation → offspring.
            var offspring = MakeOffspring(population);
            EvaluateAll(offspring, cancellationToken, ref evals);

            // 4. Combined population.
            var combined = new List<Individual>(population.Count + offspring.Count);
            combined.AddRange(population);
            combined.AddRange(offspring);

            // 5. Sort + 6-7. Trim to next-gen.
            AssignRanksAndCrowding(combined);
            population = SelectNextGeneration(combined);
        }

        // Extract final Pareto front (rank 0).
        var front = population.Where(p => p.Rank == 0).ToList();

        long swEnd = Stopwatch.GetTimestamp();
        long elapsedMs = (swEnd - swStart) * 1000 / Stopwatch.Frequency;

        return new Result(
            ParetoFront:           front,
            GenerationsCompleted:  _maxGenerations,
            TotalEvaluations:      evals,
            ElapsedMilliseconds:   elapsedMs);
    }

    private void EvaluateAll(List<Individual> pop, CancellationToken ct, ref long evals)
    {
        foreach (var ind in pop)
        {
            if (ind.Evaluation != null) continue;   // already evaluated
            var eval = _objective.Evaluate(ind.Vector, ct);
            ind.Evaluation = eval;
            ind.Objectives = _objectiveExtractor(eval);
            ind.ConstraintViolation = ComputeConstraintViolation(eval);
            evals++;
        }
    }

    private static double ComputeConstraintViolation(EvaluationResult eval)
    {
        // Infinity score → infeasible. Sum the absolute violation
        // magnitudes from FeasibilityViolation entries when present.
        if (double.IsPositiveInfinity(eval.Score))
        {
            // No magnitudes available; flag as 1.0 so any feasible
            // candidate dominates this one. If FeasibilityViolations
            // carries actual / limit scalars, sum the relative gaps.
            if (eval.Violations.Count == 0) return 1.0;
            double sum = 0.0;
            foreach (var v in eval.Violations)
            {
                double denom = Math.Max(Math.Abs(v.Limit), 1e-12);
                double gap = Math.Abs(v.ActualValue - v.Limit) / denom;
                if (!double.IsNaN(gap)) sum += gap;
            }
            return Math.Max(sum, 1.0);  // never zero on +Inf
        }
        // Score is finite — feasible.
        return 0.0;
    }

    private static void AssignRanksAndCrowding(List<Individual> pop)
    {
        // Fast non-dominated sort (Deb 2002 §III.A).
        int n = pop.Count;
        var dominated = new List<int>[n];
        var dominationCount = new int[n];
        for (int i = 0; i < n; i++) dominated[i] = new List<int>();

        var fronts = new List<List<int>> { new List<int>() };

        for (int p = 0; p < n; p++)
        {
            for (int q = 0; q < n; q++)
            {
                if (p == q) continue;
                if (Dominates(pop[p], pop[q]))
                    dominated[p].Add(q);
                else if (Dominates(pop[q], pop[p]))
                    dominationCount[p]++;
            }
            if (dominationCount[p] == 0)
            {
                pop[p].Rank = 0;
                fronts[0].Add(p);
            }
        }

        int frontIdx = 0;
        while (fronts[frontIdx].Count > 0)
        {
            var nextFront = new List<int>();
            foreach (int p in fronts[frontIdx])
            {
                foreach (int q in dominated[p])
                {
                    dominationCount[q]--;
                    if (dominationCount[q] == 0)
                    {
                        pop[q].Rank = frontIdx + 1;
                        nextFront.Add(q);
                    }
                }
            }
            frontIdx++;
            fronts.Add(nextFront);
        }

        // Crowding distance per front (Deb 2002 §III.B).
        foreach (var front in fronts)
        {
            if (front.Count == 0) continue;
            foreach (int idx in front) pop[idx].CrowdingDistance = 0.0;
            int numObj = pop[front[0]].Objectives!.Length;
            for (int m = 0; m < numObj; m++)
            {
                // Stable tie-break by original population index. Without
                // this, ties in pop[a].Objectives[m] leave the relative
                // order of (a, b) up to the underlying List<T>.Sort, which
                // is introsort and NOT stable — different runs at the
                // same seed can rotate tied elements, perturbing the
                // PositiveInfinity boundary assignment and downstream
                // crowding-distance accumulation.
                front.Sort((a, b) =>
                {
                    int c = pop[a].Objectives![m].CompareTo(pop[b].Objectives![m]);
                    return c != 0 ? c : a.CompareTo(b);
                });
                pop[front[0]].CrowdingDistance = double.PositiveInfinity;
                pop[front[^1]].CrowdingDistance = double.PositiveInfinity;
                double objMin = pop[front[0]].Objectives![m];
                double objMax = pop[front[^1]].Objectives![m];
                double range = objMax - objMin;
                if (range < 1e-12) continue;
                for (int i = 1; i < front.Count - 1; i++)
                {
                    pop[front[i]].CrowdingDistance +=
                        (pop[front[i + 1]].Objectives![m] - pop[front[i - 1]].Objectives![m]) / range;
                }
            }
        }
    }

    /// <summary>
    /// Constrained Pareto dominance (Deb 2002 §V).
    /// </summary>
    private static bool Dominates(Individual a, Individual b)
    {
        // Constraint dominance.
        if (a.IsFeasible && !b.IsFeasible) return true;
        if (!a.IsFeasible && b.IsFeasible) return false;
        if (!a.IsFeasible && !b.IsFeasible)
            return a.ConstraintViolation < b.ConstraintViolation;

        // Both feasible: standard Pareto dominance.
        bool atLeastOneBetter = false;
        for (int i = 0; i < a.Objectives!.Length; i++)
        {
            if (a.Objectives[i] > b.Objectives![i]) return false;
            if (a.Objectives[i] < b.Objectives[i]) atLeastOneBetter = true;
        }
        return atLeastOneBetter;
    }

    private List<Individual> SelectNextGeneration(List<Individual> combined)
    {
        // Bucket-sort by rank (dense small ints) — replaces
        // combined.GroupBy(p => p.Rank).OrderBy(g => g.Key).ToList(), which
        // allocates a GroupBy iterator + OrderBy iterator + outer list +
        // one inner list per rank per generation.
        int maxRank = 0;
        for (int i = 0; i < combined.Count; i++)
        {
            int r = combined[i].Rank;
            if (r > maxRank) maxRank = r;
        }
        var buckets = new List<Individual>[maxRank + 1];
        for (int i = 0; i < combined.Count; i++)
        {
            int r = combined[i].Rank;
            (buckets[r] ??= new List<Individual>()).Add(combined[i]);
        }

        var next = new List<Individual>(_populationSize);
        for (int r = 0; r <= maxRank; r++)
        {
            var indv = buckets[r];
            if (indv is null) continue;
            if (next.Count + indv.Count <= _populationSize)
            {
                next.AddRange(indv);
                if (next.Count == _populationSize) break;
            }
            else
            {
                int slots = _populationSize - next.Count;
                // Project to (individual, originalIndex) for a deterministic tie-break.
                // Direct List<Individual>.Sort with a CrowdingDistance-only comparer is
                // non-deterministic when two individuals share a crowding distance
                // (common at 0.0 and +∞).
                var withIdx = indv.Select((ind, i) => (ind, idx: i)).ToList();
                withIdx.Sort((a, b) =>
                {
                    int c = b.ind.CrowdingDistance.CompareTo(a.ind.CrowdingDistance);
                    return c != 0 ? c : a.idx.CompareTo(b.idx);
                });
                for (int i = 0; i < slots; i++) next.Add(withIdx[i].ind);
                break;
            }
        }
        return next;
    }

    private List<Individual> MakeOffspring(List<Individual> parents)
    {
        var offspring = new List<Individual>(_populationSize);
        for (int i = 0; i < _populationSize; i += 2)
        {
            var p1 = TournamentSelect(parents);
            var p2 = TournamentSelect(parents);
            (var c1v, var c2v) = SbxCrossover(p1.Vector, p2.Vector);
            PolynomialMutate(c1v);
            PolynomialMutate(c2v);
            offspring.Add(new Individual(c1v));
            offspring.Add(new Individual(c2v));
        }
        return offspring;
    }

    private Individual TournamentSelect(List<Individual> pop)
    {
        var a = pop[_rng.Next(pop.Count)];
        var b = pop[_rng.Next(pop.Count)];
        // Crowded comparison operator: better rank wins; on tie,
        // greater crowding distance wins.
        if (a.Rank < b.Rank) return a;
        if (b.Rank < a.Rank) return b;
        return a.CrowdingDistance > b.CrowdingDistance ? a : b;
    }

    private (double[] c1, double[] c2) SbxCrossover(double[] p1, double[] p2)
    {
        var c1 = new double[_dim];
        var c2 = new double[_dim];
        for (int i = 0; i < _dim; i++)
        {
            if (_rng.NextDouble() <= _crossoverProb && Math.Abs(p1[i] - p2[i]) > 1e-14)
            {
                double y1 = Math.Min(p1[i], p2[i]);
                double y2 = Math.Max(p1[i], p2[i]);
                double yLow = _lo[i];
                double yHigh = _hi[i];
                double rand = _rng.NextDouble();
                double beta = 1.0 + 2.0 * (y1 - yLow) / Math.Max(y2 - y1, 1e-30);
                double alpha = 2.0 - Math.Pow(beta, _sbxEtaPlus1Neg);
                double betaQ;
                if (rand <= 1.0 / alpha)
                    betaQ = Math.Pow(rand * alpha, _sbxEtaPlus1Inv);
                else
                    betaQ = Math.Pow(1.0 / (2.0 - rand * alpha), _sbxEtaPlus1Inv);
                double child1 = 0.5 * ((y1 + y2) - betaQ * (y2 - y1));
                beta = 1.0 + 2.0 * (yHigh - y2) / Math.Max(y2 - y1, 1e-30);
                alpha = 2.0 - Math.Pow(beta, _sbxEtaPlus1Neg);
                if (rand <= 1.0 / alpha)
                    betaQ = Math.Pow(rand * alpha, _sbxEtaPlus1Inv);
                else
                    betaQ = Math.Pow(1.0 / (2.0 - rand * alpha), _sbxEtaPlus1Inv);
                double child2 = 0.5 * ((y1 + y2) + betaQ * (y2 - y1));
                child1 = Math.Clamp(child1, yLow, yHigh);
                child2 = Math.Clamp(child2, yLow, yHigh);
                if (_rng.NextDouble() < 0.5) { c1[i] = child2; c2[i] = child1; }
                else                          { c1[i] = child1; c2[i] = child2; }
            }
            else
            {
                c1[i] = p1[i];
                c2[i] = p2[i];
            }
        }
        return (c1, c2);
    }

    private void PolynomialMutate(double[] x)
    {
        for (int i = 0; i < _dim; i++)
        {
            if (_rng.NextDouble() > _mutationProb) continue;
            double y = x[i];
            double yLow = _lo[i];
            double yHigh = _hi[i];
            double delta1 = (y - yLow) / Math.Max(yHigh - yLow, 1e-30);
            double delta2 = (yHigh - y) / Math.Max(yHigh - yLow, 1e-30);
            double rand = _rng.NextDouble();
            double deltaQ;
            if (rand < 0.5)
            {
                double xy = 1.0 - delta1;
                double val = 2.0 * rand + (1.0 - 2.0 * rand) * Math.Pow(xy, _mutEtaPlus1);
                deltaQ = Math.Pow(val, _mutEtaPlus1Inv) - 1.0;
            }
            else
            {
                double xy = 1.0 - delta2;
                double val = 2.0 * (1.0 - rand) + 2.0 * (rand - 0.5) * Math.Pow(xy, _mutEtaPlus1);
                deltaQ = 1.0 - Math.Pow(val, _mutEtaPlus1Inv);
            }
            x[i] = Math.Clamp(y + deltaQ * (yHigh - yLow), yLow, yHigh);
        }
    }
}
