// NsgaIIIOptimizer.cs — NSGA-III (Non-dominated Sorting Genetic Algorithm
// III) multi-objective optimizer with structured reference directions.
//
// Reference: Deb & Jain (2014), "An Evolutionary Many-Objective Optimization
// Algorithm Using Reference-Point-Based Nondominated Sorting Approach,
// Part I: Solving Problems with Box Constraints", IEEE TEC 18(4).
// https://ieeexplore.ieee.org/document/6600851
//
// NSGA-III upgrades NSGA-II by replacing crowding distance (which degrades
// on ≥ 4 objectives) with a structured set of reference directions on the
// unit simplex (Das-Dennis simplex-lattice) and a niche-preservation operator
// that assigns each new solution to the nearest reference direction while
// minimising the number of solutions already assigned to that direction
// (NicheCount). This achieves better diversity on 3-6 objectives.
//
// Constraint handling: identical to NSGA-II — infeasible candidates are
// dominated by any feasible candidate; within infeasible, smaller total
// constraint violation dominates (Deb 2002 §V).
//
// Number-of-objectives discovery: the objectiveExtractor output length is
// unknown at construction time (IObjective returns a scalar Score). A dummy
// candidate at the lower bounds is evaluated once in the constructor to
// determine p = number of objectives.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Voxelforge.Optimization;

/// <summary>
/// NSGA-III multi-objective optimizer with Das-Dennis reference directions
/// and niche-preservation selection.
/// Drop-in replacement for <see cref="NsgaIIOptimizer"/> on problems with
/// 3–6 objectives where crowding distance degrades.
/// </summary>
[Deterministic]
public sealed class NsgaIIIOptimizer
{
    /// <summary>Single individual in the NSGA-III population.</summary>
    public sealed class Individual
    {
        public double[]          Vector              { get; }
        public EvaluationResult? Evaluation          { get; internal set; }
        public double[]?         Objectives          { get; internal set; }
        public int               Rank                { get; internal set; }
        public double            CrowdingDistance    { get; internal set; }  // unused by III; kept for shape parity
        public double            ConstraintViolation { get; internal set; }

        public Individual(double[] vector) { Vector = vector; }

        public bool IsFeasible => ConstraintViolation <= 0.0;
    }

    /// <summary>Final Pareto front + diagnostics.</summary>
    public sealed record Result(
        IReadOnlyList<Individual> ParetoFront,
        int                       GenerationsCompleted,
        long                      TotalEvaluations,
        long                      ElapsedMilliseconds);

    private readonly IObjective _objective;
    private readonly Func<EvaluationResult, double[]> _objectiveExtractor;
    private readonly int _populationSize;
    private readonly int _maxGenerations;
    private readonly Random _rng;
    private readonly double _crossoverProb;
    private readonly double _mutationProb;
    private readonly double _sbxEta;
    private readonly double _mutEta;
    private readonly int _dim;
    private readonly double[] _lo;
    private readonly double[] _hi;
    private readonly int _numObjectives;
    private readonly List<double[]> _refDirs;

    /// <param name="referencePointDivisions">
    /// H in the Das-Dennis simplex-lattice formula. Gives C(H+p-1, p-1)
    /// reference directions. H=12 → 91 dirs for 3 objectives.
    /// Recommended: populationSize ≥ number of reference directions for
    /// good niche coverage (not enforced — smaller populations work but
    /// leave many niches empty).
    /// </param>
    public NsgaIIIOptimizer(
        IObjective objective,
        Func<EvaluationResult, double[]> objectiveExtractor,
        int populationSize,
        int maxGenerations,
        int referencePointDivisions = 12,
        int seed = 42,
        double crossoverProb = 0.9,
        double mutationProb = -1.0,
        double sbxEta = 20.0,
        double mutEta = 20.0)
    {
        if (objective is null)           throw new ArgumentNullException(nameof(objective));
        if (objectiveExtractor is null)  throw new ArgumentNullException(nameof(objectiveExtractor));
        if (populationSize < 2)          throw new ArgumentOutOfRangeException(nameof(populationSize), "populationSize must be ≥ 2");
        if (populationSize % 2 != 0)     throw new ArgumentException("populationSize must be even", nameof(populationSize));
        if (maxGenerations < 1)          throw new ArgumentOutOfRangeException(nameof(maxGenerations));
        if (referencePointDivisions < 1) throw new ArgumentOutOfRangeException(nameof(referencePointDivisions));
        if (crossoverProb < 0 || crossoverProb > 1) throw new ArgumentOutOfRangeException(nameof(crossoverProb));
        if (sbxEta <= 0) throw new ArgumentOutOfRangeException(nameof(sbxEta));
        if (mutEta  <= 0) throw new ArgumentOutOfRangeException(nameof(mutEta));

        _objective          = objective;
        _objectiveExtractor = objectiveExtractor;
        _populationSize     = populationSize;
        _maxGenerations     = maxGenerations;
        _rng            = new Random(seed);
        _crossoverProb  = crossoverProb;
        _sbxEta         = sbxEta;
        _mutEta         = mutEta;

        _dim          = objective.DimensionCount;
        _mutationProb = mutationProb < 0 ? 1.0 / Math.Max(_dim, 1) : mutationProb;

        _lo = new double[_dim];
        _hi = new double[_dim];
        for (int i = 0; i < _dim; i++)
        {
            _lo[i] = objective.Variables[i].Min;
            _hi[i] = objective.Variables[i].Max;
        }

        // Determine p by evaluating a dummy candidate at the lower bounds.
        var dummy = new double[_dim];
        for (int i = 0; i < _dim; i++) dummy[i] = _lo[i];
        var dummyEval = objective.Evaluate(dummy);
        _numObjectives = objectiveExtractor(dummyEval).Length;
        if (_numObjectives < 1)
            throw new InvalidOperationException("objectiveExtractor must return at least 1 objective.");

        _refDirs = GenerateReferenceDirections(_numObjectives, referencePointDivisions);
    }

    // ── Reference direction generation ───────────────────────────────────

    /// <summary>
    /// Das-Dennis simplex-lattice reference directions for <paramref name="p"/>
    /// objectives and <paramref name="H"/> divisions. Returns
    /// C(H+p-1, p-1) points on the unit simplex, each summing to 1.
    /// For p=3, H=12 → 91 points. For p=2, H=10 → 11 points.
    /// </summary>
    internal static List<double[]> GenerateReferenceDirections(int p, int H)
    {
        var result = new List<double[]>();
        var tuple  = new int[p];
        FillTuple(result, tuple, p, H, 0, H);
        return result;
    }

    private static void FillTuple(
        List<double[]> result, int[] tuple, int p, int H, int dim, int remaining)
    {
        if (dim == p - 1)
        {
            tuple[dim] = remaining;
            var point = new double[p];
            for (int i = 0; i < p; i++) point[i] = (double)tuple[i] / H;
            result.Add(point);
            return;
        }
        for (int k = 0; k <= remaining; k++)
        {
            tuple[dim] = k;
            FillTuple(result, tuple, p, H, dim + 1, remaining - k);
        }
    }

    // ── Main loop ─────────────────────────────────────────────────────────

    /// <summary>
    /// Run NSGA-III for up to <c>maxGenerations</c> or until cancellation.
    /// Returns the final non-dominated front (rank 0).
    /// </summary>
    [Deterministic]
    public Result Run(CancellationToken cancellationToken = default)
    {
        long swStart = Stopwatch.GetTimestamp();
        long evals   = 0;

        // Initialise population uniformly at random.
        var population = new List<Individual>(_populationSize);
        for (int i = 0; i < _populationSize; i++)
        {
            var v = new double[_dim];
            for (int j = 0; j < _dim; j++)
                v[j] = _lo[j] + _rng.NextDouble() * (_hi[j] - _lo[j]);
            population.Add(new Individual(v));
        }
        EvaluateAll(population, cancellationToken, ref evals);
        AssignRanks(population);

        int genCompleted = 0;
        for (int gen = 0; gen < _maxGenerations; gen++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var offspring = MakeOffspring(population);
            EvaluateAll(offspring, cancellationToken, ref evals);

            var combined = new List<Individual>(population.Count + offspring.Count);
            combined.AddRange(population);
            combined.AddRange(offspring);

            AssignRanks(combined);
            population = SelectNextGeneration(combined);
            genCompleted++;
        }

        var front = population.Where(p => p.Rank == 0).ToList();

        long swEnd     = Stopwatch.GetTimestamp();
        long elapsedMs = (swEnd - swStart) * 1000 / Stopwatch.Frequency;

        return new Result(
            ParetoFront:          front,
            GenerationsCompleted: genCompleted,
            TotalEvaluations:     evals,
            ElapsedMilliseconds:  elapsedMs);
    }

    // ── Evaluation ────────────────────────────────────────────────────────

    private void EvaluateAll(List<Individual> pop, CancellationToken ct, ref long evals)
    {
        foreach (var ind in pop)
        {
            if (ind.Evaluation != null) continue;
            var eval                = _objective.Evaluate(ind.Vector, ct);
            ind.Evaluation          = eval;
            ind.Objectives          = _objectiveExtractor(eval);
            ind.ConstraintViolation = ComputeConstraintViolation(eval);
            evals++;
        }
    }

    private static double ComputeConstraintViolation(EvaluationResult eval)
    {
        if (!double.IsPositiveInfinity(eval.Score)) return 0.0;
        if (eval.Violations.Count == 0) return 1.0;
        double sum = 0.0;
        foreach (var v in eval.Violations)
        {
            double denom = Math.Max(Math.Abs(v.Limit), 1e-12);
            double gap   = Math.Abs(v.ActualValue - v.Limit) / denom;
            if (!double.IsNaN(gap)) sum += gap;
        }
        return Math.Max(sum, 1.0);
    }

    // ── Non-dominated sort (ranks only — no crowding distance) ────────────

    private static void AssignRanks(List<Individual> pop)
    {
        int n = pop.Count;
        var dominated      = new List<int>[n];
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

        int fi = 0;
        while (fronts[fi].Count > 0)
        {
            var nextFront = new List<int>();
            foreach (int p in fronts[fi])
                foreach (int q in dominated[p])
                {
                    if (--dominationCount[q] == 0)
                    {
                        pop[q].Rank = fi + 1;
                        nextFront.Add(q);
                    }
                }
            fi++;
            fronts.Add(nextFront);
        }
    }

    private static bool Dominates(Individual a, Individual b)
    {
        if ( a.IsFeasible && !b.IsFeasible) return true;
        if (!a.IsFeasible &&  b.IsFeasible) return false;
        if (!a.IsFeasible && !b.IsFeasible)
            return a.ConstraintViolation < b.ConstraintViolation;

        bool atLeastOneBetter = false;
        for (int i = 0; i < a.Objectives!.Length; i++)
        {
            if (a.Objectives[i] > b.Objectives![i]) return false;
            if (a.Objectives[i] < b.Objectives[i]) atLeastOneBetter = true;
        }
        return atLeastOneBetter;
    }

    // ── NSGA-III selection (niche-preservation) ───────────────────────────

    private List<Individual> SelectNextGeneration(List<Individual> combined)
    {
        int N = _populationSize;

        // Build index map for O(1) lookup.
        var indexMap = new Dictionary<Individual, int>(combined.Count, ReferenceEqualityComparer.Instance);
        for (int i = 0; i < combined.Count; i++) indexMap[combined[i]] = i;

        // Fill P_{t+1} from front 0 upward until the next front would overflow.
        var byRank = combined
            .Select((ind, idx) => (ind, idx))
            .GroupBy(t => t.ind.Rank)
            .OrderBy(g => g.Key)
            .ToList();

        var nextIndices    = new List<int>(N);
        List<int>? lastFrontIndices = null;

        foreach (var rankGroup in byRank)
        {
            var frontIdx = rankGroup.Select(t => t.idx).ToList();
            if (nextIndices.Count + frontIdx.Count <= N)
            {
                nextIndices.AddRange(frontIdx);
                if (nextIndices.Count == N) { lastFrontIndices = null; break; }
            }
            else
            {
                lastFrontIndices = frontIdx;
                break;
            }
        }

        if (lastFrontIndices == null)
        {
            return nextIndices.Select(i => combined[i]).ToList();
        }

        int K = N - nextIndices.Count;

        // Normalise objectives for ALL combined solutions (ideal point
        // from R_t per Deb & Jain 2014 §IV-B).
        double[][] normObjs = ComputeNormalizedObjectives(combined);

        // Associate each solution in next ∪ lastFront to nearest reference direction.
        // NicheCount counts only the already-accepted solutions (nextIndices).
        var nicheCount = new int[_refDirs.Count];
        var assocAll   = new int[combined.Count];
        for (int i = 0; i < combined.Count; i++) assocAll[i] = -1;

        foreach (int idx in nextIndices)
        {
            int j      = NearestRefDir(normObjs[idx]);
            assocAll[idx] = j;
            nicheCount[j]++;
        }
        foreach (int idx in lastFrontIndices)
        {
            assocAll[idx] = NearestRefDir(normObjs[idx]);
        }

        // Build per-ref-dir candidate lists from lastFront (array avoids
        // Dictionary iteration-order non-determinism — indices are sorted
        // because combined is built from population then offspring, and
        // lastFrontIndices comes from an ordered GroupBy).
        var refCands = new List<int>[_refDirs.Count];
        for (int j = 0; j < _refDirs.Count; j++) refCands[j] = new List<int>();
        foreach (int idx in lastFrontIndices) refCands[assocAll[idx]].Add(idx);

        // Track which lastFront members are still available.
        var available = new bool[combined.Count];
        foreach (int idx in lastFrontIndices) available[idx] = true;

        var selectedFromLast = new List<int>(K);

        for (int pick = 0; pick < K; pick++)
        {
            // Find j* = direction with min NicheCount that still has available candidates.
            int jStar    = -1;
            int minNiche = int.MaxValue;
            for (int j = 0; j < _refDirs.Count; j++)
            {
                // Fast check: any candidate still available for direction j?
                bool hasCand = false;
                foreach (int idx in refCands[j])
                {
                    if (available[idx]) { hasCand = true; break; }
                }
                if (!hasCand) continue;
                if (nicheCount[j] < minNiche)
                {
                    minNiche = nicheCount[j];
                    jStar    = j;
                }
            }
            if (jStar == -1) break;

            // Choose from j*'s available candidates.
            int chosen = -1;
            if (minNiche == 0)
            {
                // Pick candidate with minimum perpendicular distance to j*.
                double bestDist = double.PositiveInfinity;
                foreach (int idx in refCands[jStar])
                {
                    if (!available[idx]) continue;
                    double d = PerpDistance(normObjs[idx], _refDirs[jStar]);
                    if (d < bestDist) { bestDist = d; chosen = idx; }
                }
            }
            else
            {
                // Pick first available (deterministic — list was built in stable order).
                foreach (int idx in refCands[jStar])
                {
                    if (available[idx]) { chosen = idx; break; }
                }
            }

            if (chosen == -1) break;
            selectedFromLast.Add(chosen);
            available[chosen] = false;
            nicheCount[jStar]++;
        }

        var result = new List<Individual>(N);
        foreach (int i in nextIndices)    result.Add(combined[i]);
        foreach (int i in selectedFromLast) result.Add(combined[i]);
        return result;
    }

    // ── Objective normalisation ───────────────────────────────────────────

    private double[][] ComputeNormalizedObjectives(List<Individual> combined)
    {
        int n = combined.Count;
        int m = _numObjectives;

        // Collect objectives for all solutions (guard null for cancelled eval).
        var allObjs = new double[n][];
        for (int i = 0; i < n; i++)
            allObjs[i] = combined[i].Objectives ?? new double[m];

        // Ideal point: min per objective (feasible-preferred; fallback to all).
        bool anyFeasible = combined.Any(ind => ind.IsFeasible);
        var zStar = new double[m];
        for (int objM = 0; objM < m; objM++) zStar[objM] = double.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            if (anyFeasible && !combined[i].IsFeasible) continue;
            for (int objM = 0; objM < m; objM++)
                zStar[objM] = Math.Min(zStar[objM], allObjs[i][objM]);
        }
        for (int objM = 0; objM < m; objM++)
            if (double.IsPositiveInfinity(zStar[objM])) zStar[objM] = 0.0;

        // Translate: f' = f - z*.
        var translated = new double[n][];
        for (int i = 0; i < n; i++)
        {
            translated[i] = new double[m];
            for (int objM = 0; objM < m; objM++)
                translated[i][objM] = allObjs[i][objM] - zStar[objM];
        }

        // Extreme points via ASF: for axis m, find solution minimising ASF(f', w_m).
        var extremeRows = new double[m][];
        for (int objM = 0; objM < m; objM++)
        {
            double minAsf = double.PositiveInfinity;
            int    bestI  = 0;
            for (int i = 0; i < n; i++)
            {
                double asf = ComputeAsf(translated[i], objM, m);
                if (asf < minAsf) { minAsf = asf; bestI = i; }
            }
            extremeRows[objM] = translated[bestI];
        }

        // Intercepts via Gaussian elimination on the m×m system.
        double[]? intercepts = SolveIntercepts(extremeRows, m);
        if (intercepts == null!)
        {
            // Singular — fallback: max(f'_m) + 1e-10.
            intercepts = new double[m];
            for (int objM = 0; objM < m; objM++)
            {
                double mx = 0.0;
                for (int i = 0; i < n; i++)
                    mx = Math.Max(mx, translated[i][objM]);
                intercepts[objM] = mx + 1e-10;
            }
        }

        // Normalise: f'' = f' / a.
        var normObjs = new double[n][];
        for (int i = 0; i < n; i++)
        {
            normObjs[i] = new double[m];
            for (int objM = 0; objM < m; objM++)
            {
                double denom = Math.Max(Math.Abs(intercepts[objM]), 1e-10);
                normObjs[i][objM] = translated[i][objM] / denom;
            }
        }
        return normObjs;
    }

    private static double ComputeAsf(double[] f, int axisM, int m)
    {
        double max = double.NegativeInfinity;
        for (int i = 0; i < m; i++)
        {
            double w   = (i == axisM) ? 1.0 : 1e-6;
            double val = f[i] / w;
            if (val > max) max = val;
        }
        return max;
    }

    private static double[]? SolveIntercepts(double[][] extreme, int m)
    {
        // Solve A·x = 1 (rhs all-ones) where A[k,j] = extreme[k][j],
        // giving x_j = 1/a_j (intercept a_j = 1/x_j).
        // Gaussian elimination with partial pivoting.
        var mat = new double[m, m + 1];
        for (int k = 0; k < m; k++)
        {
            for (int j = 0; j < m; j++) mat[k, j] = extreme[k][j];
            mat[k, m] = 1.0;
        }

        for (int col = 0; col < m; col++)
        {
            // Partial pivot.
            int    pivotRow = col;
            double maxAbs   = Math.Abs(mat[col, col]);
            for (int row = col + 1; row < m; row++)
            {
                double abs = Math.Abs(mat[row, col]);
                if (abs > maxAbs) { maxAbs = abs; pivotRow = row; }
            }
            if (maxAbs < 1e-14) return null;   // singular

            if (pivotRow != col)
                for (int j = 0; j <= m; j++)
                    (mat[col, j], mat[pivotRow, j]) = (mat[pivotRow, j], mat[col, j]);

            for (int row = col + 1; row < m; row++)
            {
                double f = mat[row, col] / mat[col, col];
                for (int j = col; j <= m; j++)
                    mat[row, j] -= f * mat[col, j];
            }
        }

        var x = new double[m];
        for (int row = m - 1; row >= 0; row--)
        {
            x[row] = mat[row, m];
            for (int j = row + 1; j < m; j++) x[row] -= mat[row, j] * x[j];
            if (Math.Abs(mat[row, row]) < 1e-14) return null;
            x[row] /= mat[row, row];
        }

        // Convert x_j = 1/a_j → a_j = 1/x_j.
        var intercepts = new double[m];
        for (int i = 0; i < m; i++)
            intercepts[i] = Math.Abs(x[i]) < 1e-14 ? 1e-10 : 1.0 / x[i];
        return intercepts;
    }

    // ── Association helpers ───────────────────────────────────────────────

    private int NearestRefDir(double[] normObj)
    {
        int    best     = 0;
        double bestDist = double.PositiveInfinity;
        for (int j = 0; j < _refDirs.Count; j++)
        {
            double d = PerpDistance(normObj, _refDirs[j]);
            if (d < bestDist) { bestDist = d; best = j; }
        }
        return best;
    }

    private static double PerpDistance(double[] d, double[] w)
    {
        // d_perp = ||d - (d·w / w·w) * w||
        double dotDW = 0.0, dotWW = 0.0;
        for (int i = 0; i < d.Length; i++) { dotDW += d[i] * w[i]; dotWW += w[i] * w[i]; }
        double proj  = dotWW > 1e-14 ? dotDW / dotWW : 0.0;
        double sumSq = 0.0;
        for (int i = 0; i < d.Length; i++) { double diff = d[i] - proj * w[i]; sumSq += diff * diff; }
        return Math.Sqrt(sumSq);
    }

    // ── Offspring generation (identical to NSGA-II) ───────────────────────

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
        if (a.Rank < b.Rank) return a;
        if (b.Rank < a.Rank) return b;
        return _rng.Next(2) == 0 ? a : b;
    }

    private (double[] c1, double[] c2) SbxCrossover(double[] p1, double[] p2)
    {
        var c1 = new double[_dim];
        var c2 = new double[_dim];
        for (int i = 0; i < _dim; i++)
        {
            if (_rng.NextDouble() <= _crossoverProb && Math.Abs(p1[i] - p2[i]) > 1e-14)
            {
                double y1    = Math.Min(p1[i], p2[i]);
                double y2    = Math.Max(p1[i], p2[i]);
                double yLow  = _lo[i];
                double yHigh = _hi[i];
                double rand  = _rng.NextDouble();
                double beta  = 1.0 + 2.0 * (y1 - yLow) / Math.Max(y2 - y1, 1e-30);
                double alpha = 2.0 - Math.Pow(beta, -(_sbxEta + 1.0));
                double betaQ;
                if (rand <= 1.0 / alpha)
                    betaQ = Math.Pow(rand * alpha, 1.0 / (_sbxEta + 1.0));
                else
                    betaQ = Math.Pow(1.0 / (2.0 - rand * alpha), 1.0 / (_sbxEta + 1.0));
                double child1 = 0.5 * ((y1 + y2) - betaQ * (y2 - y1));
                beta  = 1.0 + 2.0 * (yHigh - y2) / Math.Max(y2 - y1, 1e-30);
                alpha = 2.0 - Math.Pow(beta, -(_sbxEta + 1.0));
                if (rand <= 1.0 / alpha)
                    betaQ = Math.Pow(rand * alpha, 1.0 / (_sbxEta + 1.0));
                else
                    betaQ = Math.Pow(1.0 / (2.0 - rand * alpha), 1.0 / (_sbxEta + 1.0));
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
            double y      = x[i];
            double yLow   = _lo[i];
            double yHigh  = _hi[i];
            double delta1 = (y - yLow)   / Math.Max(yHigh - yLow, 1e-30);
            double delta2 = (yHigh - y)  / Math.Max(yHigh - yLow, 1e-30);
            double rand   = _rng.NextDouble();
            double mutPow = 1.0 / (_mutEta + 1.0);
            double deltaQ;
            if (rand < 0.5)
            {
                double xy  = 1.0 - delta1;
                double val = 2.0 * rand + (1.0 - 2.0 * rand) * Math.Pow(xy, _mutEta + 1.0);
                deltaQ = Math.Pow(val, mutPow) - 1.0;
            }
            else
            {
                double xy  = 1.0 - delta2;
                double val = 2.0 * (1.0 - rand) + 2.0 * (rand - 0.5) * Math.Pow(xy, _mutEta + 1.0);
                deltaQ = 1.0 - Math.Pow(val, mutPow);
            }
            x[i] = Math.Clamp(y + deltaQ * (yHigh - yLow), yLow, yHigh);
        }
    }
}
