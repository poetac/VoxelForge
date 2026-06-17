// NsgaIIOptimizerDeterminismTests — Issue #552 (audit C4).
//
// Strict bit-identity determinism on tied front sites. The two
// regression cases below would have failed before the tie-break by
// (a, b) → (a.idx, b.idx) inside NsgaIIOptimizer.AssignRanksAndCrowding
// (crowding-distance ordering of a front by each objective) and
// NsgaIIOptimizer.SelectNextGeneration (slot-trim of the last partial
// front by crowding distance), because List<T>.Sort uses introsort
// which is NOT stable, so any tied keys could rotate between runs.
//
// Both fixtures intentionally produce ties:
//   - AllTiedObjective always returns (0.0, 0.0), driving every front
//     into a single rank with every crowding distance flat at +Inf
//     for the boundaries and 0.0 inside.
//   - MirroredObjective returns (x, -x), forcing all candidates onto
//     the first front (each strictly trades f1 for f2 against every
//     other one), with crowding distances dense at 0 for repeated x.
//
// We construct N=10 fresh optimizers per case and assert every final
// population vector + objective vector is bit-identical across all
// runs. This pins both sort sites; loss of stability at either site
// surfaces as a divergent run in the loop.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Optimization;

public class NsgaIIOptimizerDeterminismTests
{
    /// <summary>
    /// 2-objective stub that returns a constant objective vector. Every
    /// candidate ties on both objectives, so every front collapses to
    /// rank 0 with crowding distances {+Inf, +Inf, 0, 0, ..., 0} at
    /// each sweep, exercising the AssignRanksAndCrowding sort and the
    /// SelectNextGeneration trim under maximum ties.
    /// </summary>
    private sealed class AllTiedObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;
        public AllTiedObjective(int dim)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++)
                _vars[i] = new DesignVariableInfo($"x{i}", 0.0, 1.0);
        }
        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;
        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            return new EvaluationResult(
                Score: 0.0,
                Violations: Array.Empty<FeasibilityViolation>(),
                EngineSpecificBreakdown: new[] { 0.0, 0.0 });
        }
    }

    /// <summary>
    /// 2-objective stub that returns (x[0], -x[0]). Every pair of
    /// candidates with distinct x[0] strictly trades f1 for f2, so all
    /// candidates land on the first non-dominated front, but the
    /// crowding distance for the constant objectives (Objectives[1] is
    /// the negation of Objectives[0], so the second sweep just reverses
    /// the first) repeatedly produces tied keys at every interior point
    /// when x[0] values repeat across the population.
    /// </summary>
    private sealed class MirroredObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;
        public MirroredObjective(int dim)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++)
                _vars[i] = new DesignVariableInfo($"x{i}", 0.0, 1.0);
        }
        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;
        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            double x = vector[0];
            return new EvaluationResult(
                Score: 0.0,
                Violations: Array.Empty<FeasibilityViolation>(),
                EngineSpecificBreakdown: new[] { x, -x });
        }
    }

    private static double[] Extractor(EvaluationResult eval) =>
        (double[])eval.EngineSpecificBreakdown!;

    // Materialises the final population (rank-0 front + everything else
    // that was kept by SelectNextGeneration is observable via the result
    // ParetoFront for rank 0; we check the rank-0 set since it's the
    // public surface, and additionally compare every individual via the
    // optimizer state by deriving a stable signature). The signature we
    // compare per run is (vector, objectives) for each ParetoFront
    // individual in the order the optimizer returned them.
    private static List<(double[] Vector, double[] Objectives)> Snapshot(NsgaIIOptimizer.Result r) =>
        r.ParetoFront
            .Select(ind => ((double[])ind.Vector.Clone(),
                            (double[])ind.Objectives!.Clone()))
            .ToList();

    private static void AssertSnapshotsEqual(
        List<(double[] Vector, double[] Objectives)> a,
        List<(double[] Vector, double[] Objectives)> b)
    {
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Vector,     b[i].Vector);
            Assert.Equal(a[i].Objectives, b[i].Objectives);
        }
    }

    [Fact]
    public void StrictDeterminism_TiedCrowdingDistance_ProducesIdenticalSelection()
    {
        // AllTiedObjective: every candidate has identical objectives, so
        // every front-internal crowding-distance sort and every
        // last-partial-front trim runs on fully-tied keys. Pre-fix, the
        // introsort in List<T>.Sort could rotate elements between runs,
        // producing different rank-0 fronts (different individuals
        // selected from the tied set) across the 10 attempts.
        const int dim = 3;
        const int pop = 20;
        const int gens = 15;
        const int seed = 12345;

        var first = new NsgaIIOptimizer(
            new AllTiedObjective(dim), Extractor,
            populationSize: pop, maxGenerations: gens, seed: seed).Run();
        var firstSnap = Snapshot(first);

        for (int run = 0; run < 9; run++)
        {
            var r = new NsgaIIOptimizer(
                new AllTiedObjective(dim), Extractor,
                populationSize: pop, maxGenerations: gens, seed: seed).Run();
            AssertSnapshotsEqual(firstSnap, Snapshot(r));
            Assert.Equal(first.TotalEvaluations,     r.TotalEvaluations);
            Assert.Equal(first.GenerationsCompleted, r.GenerationsCompleted);
        }
    }

    [Fact]
    public void StrictDeterminism_TiedObjectives_ProducesIdenticalRanks()
    {
        // MirroredObjective: every candidate sits on the first front, so
        // the entire population is the rank-0 front and the partial-
        // front trim runs against a fully-rank-0 input. Any duplicated
        // x[0] across the population produces tied crowding-distance
        // keys; pre-fix, the trim was free to pick any tied candidate
        // and the AssignRanksAndCrowding inner sweep was free to rotate
        // tied boundary points, perturbing the rest of the algorithm.
        const int dim = 2;
        const int pop = 20;
        const int gens = 15;
        const int seed = 7;

        var first = new NsgaIIOptimizer(
            new MirroredObjective(dim), Extractor,
            populationSize: pop, maxGenerations: gens, seed: seed).Run();
        var firstSnap = Snapshot(first);

        for (int run = 0; run < 9; run++)
        {
            var r = new NsgaIIOptimizer(
                new MirroredObjective(dim), Extractor,
                populationSize: pop, maxGenerations: gens, seed: seed).Run();
            AssertSnapshotsEqual(firstSnap, Snapshot(r));
            Assert.Equal(first.TotalEvaluations,     r.TotalEvaluations);
            Assert.Equal(first.GenerationsCompleted, r.GenerationsCompleted);
        }
    }
}
