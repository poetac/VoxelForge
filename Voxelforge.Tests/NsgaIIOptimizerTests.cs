// NsgaIIOptimizerTests — Issue #161 (T2.4a NSGA-II Pareto algorithm).
//
// Convergence on standard multi-objective benchmarks where the Pareto
// front is known analytically. Pins the constrained-dominance
// behaviour from Deb 2002 §V (infeasible candidates are dominated by
// any feasible candidate; within infeasible, smaller violation
// dominates).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class NsgaIIOptimizerTests
{
    /// <summary>
    /// ZDT1 benchmark from Zitzler-Deb-Thiele 2000. Pareto front is
    /// f1 ∈ [0, 1], f2 = 1 - sqrt(f1) (convex Pareto frontier in
    /// objective space). Decision space is [0, 1]^n with optimum at
    /// x_2..x_n = 0.
    /// </summary>
    private sealed class Zdt1Objective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;
        public Zdt1Objective(int dim)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++) _vars[i] = new DesignVariableInfo($"x{i}", 0.0, 1.0);
        }
        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;
        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            double f1 = vector[0];
            double sum = 0.0;
            for (int i = 1; i < vector.Length; i++) sum += vector[i];
            double g = 1.0 + 9.0 * sum / (vector.Length - 1);
            double f2 = g * (1.0 - Math.Sqrt(f1 / g));
            // Encode (f1, f2) into the EvaluationResult.EngineSpecificBreakdown
            // as a double[] for the extractor to read.
            return new EvaluationResult(
                Score: f1 + f2,   // scalar collapse used only for IObjective contract
                Violations: Array.Empty<FeasibilityViolation>(),
                EngineSpecificBreakdown: new[] { f1, f2 });
        }
    }

    private static double[] Zdt1Extractor(EvaluationResult eval)
    {
        return (double[])eval.EngineSpecificBreakdown!;
    }

    [Fact]
    public void Zdt1_5D_FindsParetoFront()
    {
        // Run NSGA-II on 5D ZDT1. After ~100 generations with 50 pop,
        // the front should approximate the analytical (f1, 1 - sqrt(f1)).
        var obj = new Zdt1Objective(dim: 5);
        var optimizer = new NsgaIIOptimizer(
            objective:           obj,
            objectiveExtractor:  Zdt1Extractor,
            populationSize:      50,
            maxGenerations:      100,
            seed:                42);
        var result = optimizer.Run();

        Assert.NotEmpty(result.ParetoFront);

        // Check at least 5 distinct points on the frontier.
        var distinctF1 = result.ParetoFront
            .Select(ind => ind.Objectives![0])
            .Distinct()
            .Count();
        Assert.True(distinctF1 >= 5,
            $"Pareto front has {distinctF1} distinct f1 values; expected ≥ 5");

        // Frontier-shape check: every front point should approximately
        // satisfy f2 ≤ 1 - sqrt(f1) + ε (analytical optimum).
        // Allow ε = 0.5 — NSGA-II at 100 gens hasn't fully converged.
        foreach (var ind in result.ParetoFront)
        {
            double f1 = ind.Objectives![0];
            double f2 = ind.Objectives[1];
            double f2Theoretical = 1.0 - Math.Sqrt(Math.Max(f1, 0.0));
            Assert.True(f2 <= f2Theoretical + 0.5,
                $"Front point ({f1:F3}, {f2:F3}) far from analytical ({f1:F3}, {f2Theoretical:F3})");
        }
    }

    [Fact]
    public void Determinism_SameSeedProducesSameFront()
    {
        var r1 = new NsgaIIOptimizer(
            new Zdt1Objective(3), Zdt1Extractor, 30, 30, seed: 99).Run();
        var r2 = new NsgaIIOptimizer(
            new Zdt1Objective(3), Zdt1Extractor, 30, 30, seed: 99).Run();

        Assert.Equal(r1.ParetoFront.Count, r2.ParetoFront.Count);
        Assert.Equal(r1.TotalEvaluations,  r2.TotalEvaluations);
        // Sort both by f1 and compare.
        var s1 = r1.ParetoFront.OrderBy(p => p.Objectives![0]).ToList();
        var s2 = r2.ParetoFront.OrderBy(p => p.Objectives![0]).ToList();
        for (int i = 0; i < s1.Count; i++)
        {
            Assert.Equal(s1[i].Objectives, s2[i].Objectives);
        }
    }

    /// <summary>
    /// Constrained problem: minimize (x, y) subject to x + y ≥ 1.
    /// Infeasible: x + y &lt; 1; feasible: x + y ≥ 1. Constraint
    /// violation = max(0, 1 - x - y).
    /// </summary>
    private sealed class ConstrainedObj : IObjective
    {
        private readonly DesignVariableInfo[] _vars =
        {
            new("x", 0.0, 1.0),
            new("y", 0.0, 1.0),
        };
        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;
        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            double x = vector[0], y = vector[1];
            double violation = Math.Max(0.0, 1.0 - x - y);
            if (violation > 0)
            {
                var v = new[]
                {
                    new FeasibilityViolation("SUM_CONSTRAINT",
                        "x + y must be >= 1", ActualValue: x + y, Limit: 1.0),
                };
                return new EvaluationResult(double.PositiveInfinity, v, new[] { x, y });
            }
            return new EvaluationResult(x + y, Array.Empty<FeasibilityViolation>(), new[] { x, y });
        }
    }

    [Fact]
    public void Constrained_OnlyFeasibleInFinalFront()
    {
        // Constrained-dominance: any feasible candidate dominates any
        // infeasible candidate. After a few generations on a 2D problem,
        // every individual on the rank-0 front should be feasible.
        var optimizer = new NsgaIIOptimizer(
            new ConstrainedObj(),
            eval => (double[])eval.EngineSpecificBreakdown!,
            populationSize: 30,
            maxGenerations: 50,
            seed: 7);
        var result = optimizer.Run();

        Assert.NotEmpty(result.ParetoFront);
        foreach (var ind in result.ParetoFront)
        {
            Assert.True(ind.IsFeasible,
                $"Rank-0 front contains infeasible candidate: violation={ind.ConstraintViolation:F3}");
        }
    }

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        var obj = new Zdt1Objective(3);
        Assert.Throws<ArgumentNullException>(() =>
            new NsgaIIOptimizer(null!, Zdt1Extractor, 10, 10));
        Assert.Throws<ArgumentNullException>(() =>
            new NsgaIIOptimizer(obj, null!, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NsgaIIOptimizer(obj, Zdt1Extractor, 1, 10));   // pop < 2
        Assert.Throws<ArgumentException>(() =>
            new NsgaIIOptimizer(obj, Zdt1Extractor, 11, 10));  // pop odd
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NsgaIIOptimizer(obj, Zdt1Extractor, 10, 0));   // gens < 1
    }

    [Fact]
    public void NondominatedSort_RankZeroIsTheActualParetoFront()
    {
        // After running NSGA-II, every rank-0 individual should be
        // non-dominated by every other rank-0 individual (the
        // definition of a Pareto front).
        var optimizer = new NsgaIIOptimizer(
            new Zdt1Objective(4), Zdt1Extractor, 20, 30, seed: 17);
        var result = optimizer.Run();

        var front = result.ParetoFront;
        for (int i = 0; i < front.Count; i++)
        {
            for (int j = 0; j < front.Count; j++)
            {
                if (i == j) continue;
                // Neither i strictly dominates j nor vice versa.
                bool iDominatesJ = StrictPareto(front[i].Objectives!, front[j].Objectives!);
                Assert.False(iDominatesJ,
                    $"Rank-0 individual {i} dominates {j} — should not happen on a true Pareto front");
            }
        }
    }

    private static bool StrictPareto(double[] a, double[] b)
    {
        bool atLeastOneBetter = false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] > b[i]) return false;
            if (a[i] < b[i]) atLeastOneBetter = true;
        }
        return atLeastOneBetter;
    }
}
