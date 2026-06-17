// NsgaIIIOptimizerTests — NSGA-III multi-objective optimizer validation.
//
// Convergence on ZDT1 (known analytic Pareto front), reference-direction
// geometry verification, and niche-preservation correctness checks.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class NsgaIIIOptimizerTests
{
    // ── ZDT1 benchmark (same as NsgaIIOptimizerTests) ───────────────────

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
            double g  = 1.0 + 9.0 * sum / (vector.Length - 1);
            double f2 = g * (1.0 - Math.Sqrt(f1 / g));
            return new EvaluationResult(
                Score: f1 + f2,
                Violations: Array.Empty<FeasibilityViolation>(),
                EngineSpecificBreakdown: new[] { f1, f2 });
        }
    }

    private static double[] Zdt1Extractor(EvaluationResult eval) =>
        (double[])eval.EngineSpecificBreakdown!;

    // ── Main convergence test ────────────────────────────────────────────

    [Fact]
    public void Zdt1_2D_FindsParetoFront()
    {
        var obj = new Zdt1Objective(dim: 30);
        var optimizer = new NsgaIIIOptimizer(
            objective:           obj,
            objectiveExtractor:  Zdt1Extractor,
            populationSize:      100,
            maxGenerations:      150,
            seed:                42);
        var result = optimizer.Run();

        Assert.NotEmpty(result.ParetoFront);

        // At least 10 distinct f1 values on the Pareto front.
        int distinctF1 = result.ParetoFront
            .Select(ind => Math.Round(ind.Objectives![0], 2))
            .Distinct()
            .Count();
        Assert.True(distinctF1 >= 10,
            $"Expected ≥ 10 distinct f1 values; got {distinctF1}");

        // Quality: every front point should have f1 + f2 reasonably close to
        // the analytic optimum. The ZDT1 Pareto front satisfies
        // f2 = 1 - sqrt(f1), so f1 + f2 = f1 + 1 - sqrt(f1) ≤ 1.25 max.
        // Allow ε = 0.6 for partial convergence at 150 gens.
        foreach (var ind in result.ParetoFront)
        {
            double f1 = ind.Objectives![0];
            double f2 = ind.Objectives[1];
            Assert.True(f1 + f2 < 1.6,
                $"Front point f1={f1:F3} f2={f2:F3}, sum={f1+f2:F3} exceeds 1.6");
        }
    }

    // ── Reference direction geometry ─────────────────────────────────────

    [Fact]
    public void ReferencePoints_3Obj_H12_Has91Points()
    {
        // C(12+3-1, 3-1) = C(14, 2) = 91.
        var dirs = NsgaIIIOptimizer.GenerateReferenceDirections(3, 12);
        Assert.Equal(91, dirs.Count);
    }

    [Fact]
    public void ReferencePoints_2Obj_H10_Has11Points()
    {
        // C(10+2-1, 2-1) = C(11, 1) = 11.
        var dirs = NsgaIIIOptimizer.GenerateReferenceDirections(2, 10);
        Assert.Equal(11, dirs.Count);
    }

    [Fact]
    public void ReferencePoints_SumToOne()
    {
        // All reference direction coordinates should sum to 1.0 (unit simplex).
        var dirs = NsgaIIIOptimizer.GenerateReferenceDirections(3, 12);
        foreach (var d in dirs)
        {
            double sum = d.Sum();
            Assert.True(Math.Abs(sum - 1.0) < 1e-10,
                $"Reference direction {string.Join(",", d.Select(x => x.ToString("F6")))} sums to {sum:F10}, expected 1.0");
        }
    }

    [Fact]
    public void ReferencePoints_4Obj_H4()
    {
        // C(4+4-1, 4-1) = C(7,3) = 35.
        var dirs = NsgaIIIOptimizer.GenerateReferenceDirections(4, 4);
        Assert.Equal(35, dirs.Count);
        foreach (var d in dirs)
            Assert.True(Math.Abs(d.Sum() - 1.0) < 1e-10);
    }

    // ── Determinism ───────────────────────────────────────────────────────

    [Fact]
    public void Determinism_SameSeedProducesBitIdenticalFront()
    {
        var r1 = new NsgaIIIOptimizer(
            new Zdt1Objective(5), Zdt1Extractor, 30, 20, seed: 77).Run();
        var r2 = new NsgaIIIOptimizer(
            new Zdt1Objective(5), Zdt1Extractor, 30, 20, seed: 77).Run();

        Assert.Equal(r1.ParetoFront.Count, r2.ParetoFront.Count);
        Assert.Equal(r1.TotalEvaluations, r2.TotalEvaluations);
        var s1 = r1.ParetoFront.OrderBy(p => p.Objectives![0]).ToList();
        var s2 = r2.ParetoFront.OrderBy(p => p.Objectives![0]).ToList();
        for (int i = 0; i < s1.Count; i++)
            Assert.Equal(s1[i].Objectives, s2[i].Objectives);
    }

    // ── Cancellation ─────────────────────────────────────────────────────

    [Fact]
    public void Cancellation_StopsBeforeMaxGenerations()
    {
        using var cts = new CancellationTokenSource();
        var obj       = new Zdt1Objective(3);
        var optimizer = new NsgaIIIOptimizer(
            obj, Zdt1Extractor, 20, maxGenerations: 200, seed: 1);

        cts.Cancel();
        var result = optimizer.Run(cts.Token);

        Assert.True(result.GenerationsCompleted < 200,
            $"GenerationsCompleted = {result.GenerationsCompleted}; expected < 200");
        Assert.NotNull(result.ParetoFront);
    }

    // ── Constraint dominance ─────────────────────────────────────────────

    private sealed class ConstrainedObj2D : IObjective
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
                return new EvaluationResult(double.PositiveInfinity,
                    new[] { new FeasibilityViolation("SUM", "x+y>=1", x + y, 1.0) },
                    new[] { x, y });
            return new EvaluationResult(x + y, Array.Empty<FeasibilityViolation>(), new[] { x, y });
        }
    }

    [Fact]
    public void ConstraintDominance_FeasibleAlwaysDominatesInfeasible()
    {
        var optimizer = new NsgaIIIOptimizer(
            new ConstrainedObj2D(),
            eval => (double[])eval.EngineSpecificBreakdown!,
            populationSize: 30,
            maxGenerations:  50,
            seed: 7);
        var result = optimizer.Run();

        Assert.NotEmpty(result.ParetoFront);
        foreach (var ind in result.ParetoFront)
            Assert.True(ind.IsFeasible,
                $"Rank-0 front contains infeasible individual: violation={ind.ConstraintViolation:F3}");
    }

    // ── All-infeasible population ─────────────────────────────────────────

    private sealed class AlwaysInfeasibleObj : IObjective
    {
        private readonly DesignVariableInfo[] _vars =
        {
            new("x", 0.0, 1.0),
            new("y", 0.0, 1.0),
        };
        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;
        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default) =>
            new EvaluationResult(double.PositiveInfinity,
                new[] { new FeasibilityViolation("ALWAYS", "always infeasible", 0.0, 1.0) },
                new[] { vector[0], vector[1] });
    }

    [Fact]
    public void InfeasiblePopulation_RunsWithoutCrashAndReturnsFront()
    {
        var optimizer = new NsgaIIIOptimizer(
            new AlwaysInfeasibleObj(),
            eval => (double[])eval.EngineSpecificBreakdown!,
            populationSize: 10,
            maxGenerations:  5,
            seed: 3);
        var result = optimizer.Run();

        // Must not throw; Pareto front is non-empty even if all are infeasible.
        Assert.NotNull(result);
        Assert.NotEmpty(result.ParetoFront);
    }

    // ── PopulationSize less than referencePointCount ──────────────────────

    [Fact]
    public void PopulationSize_CanBeLessThanReferencePointCount_NoCrash()
    {
        // p=3, H=12 → 91 reference directions; populationSize = 20 < 91.
        // Must not crash -- niches will simply be sparse.
        var obj = new Zdt1Objective(3);

        // We need a 3-objective extractor for p=3.
        static double[] ThreeObjExtractor(EvaluationResult eval)
        {
            var base2 = (double[])eval.EngineSpecificBreakdown!;
            return new[] { base2[0], base2[1], 1.0 - base2[0] };
        }

        var optimizer = new NsgaIIIOptimizer(
            obj, ThreeObjExtractor,
            populationSize: 20,
            maxGenerations:  5,
            referencePointDivisions: 12,
            seed: 5);

        var result = optimizer.Run();
        Assert.NotEmpty(result.ParetoFront);
    }

    // ── Pareto front mutual non-domination ────────────────────────────────

    [Fact]
    public void ParetoFront_AllPairsAreMutuallyNonDominated()
    {
        var optimizer = new NsgaIIIOptimizer(
            new Zdt1Objective(4), Zdt1Extractor, 20, 30, seed: 17);
        var result = optimizer.Run();

        var front = result.ParetoFront;
        for (int i = 0; i < front.Count; i++)
        for (int j = 0; j < front.Count; j++)
        {
            if (i == j) continue;
            Assert.False(StrictPareto(front[i].Objectives!, front[j].Objectives!),
                $"Rank-0 individual {i} dominates {j}");
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

    // ── Larger H → more reference points → denser front ──────────────────

    [Fact]
    public void LargerDivisions_MoreReferencePoints()
    {
        var dirsH3 = NsgaIIIOptimizer.GenerateReferenceDirections(3, 3);
        var dirsH6 = NsgaIIIOptimizer.GenerateReferenceDirections(3, 6);
        Assert.True(dirsH6.Count > dirsH3.Count,
            $"H=6 should produce more reference points than H=3 for p=3: {dirsH6.Count} vs {dirsH3.Count}");
    }

    // ── Diagnostics ───────────────────────────────────────────────────────

    [Fact]
    public void ElapsedMilliseconds_NonNegative()
    {
        var result = new NsgaIIIOptimizer(
            new Zdt1Objective(3), Zdt1Extractor, 10, 2, seed: 1).Run();
        Assert.True(result.ElapsedMilliseconds >= 0,
            $"ElapsedMilliseconds = {result.ElapsedMilliseconds}");
    }

    [Fact]
    public void TotalEvaluations_ApproximatelyPopSizeTimesGenerations()
    {
        int pop  = 10;
        int gens = 5;
        var result = new NsgaIIIOptimizer(
            new Zdt1Objective(2), Zdt1Extractor, pop, gens, seed: 1).Run();

        // Initial pop (N) + N offspring per generation + 1 dummy eval in ctor.
        long expected = (long)pop * (gens + 1) + 1; // rough upper bound
        Assert.True(result.TotalEvaluations <= expected + 2L * pop,
            $"TotalEvaluations {result.TotalEvaluations} seems unexpectedly high (expected ~{expected})");
        Assert.True(result.TotalEvaluations >= pop,
            "TotalEvaluations should be at least populationSize");
    }

    [Fact]
    public void GenerationsCompleted_EqualsMaxGenerations_WhenNotCancelled()
    {
        int gens  = 5;
        var result = new NsgaIIIOptimizer(
            new Zdt1Objective(2), Zdt1Extractor, 10, gens, seed: 1).Run();
        Assert.Equal(gens, result.GenerationsCompleted);
    }

    // ── Constructor argument validation ───────────────────────────────────

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        var obj = new Zdt1Objective(3);

        Assert.Throws<ArgumentNullException>(() =>
            new NsgaIIIOptimizer(null!, Zdt1Extractor, 10, 10));
        Assert.Throws<ArgumentNullException>(() =>
            new NsgaIIIOptimizer(obj, null!, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NsgaIIIOptimizer(obj, Zdt1Extractor, 1, 10));   // pop < 2
        Assert.Throws<ArgumentException>(() =>
            new NsgaIIIOptimizer(obj, Zdt1Extractor, 11, 10));  // pop odd
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NsgaIIIOptimizer(obj, Zdt1Extractor, 10, 0));   // gens < 1
    }
}
