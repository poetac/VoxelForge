// NsgaNaNFeasibilityTests — red-team round 3 regression (fail-on-old /
// pass-on-new). NSGA-II/III classified candidates by `IsPositiveInfinity`
// only, so a NaN evaluation score (contract-legal: "infinite or NaN scores
// are surfaced verbatim") got constraint-violation 0 — *feasible*. NaN
// objectives compare false in both directions inside `Dominates`, making
// the NaN individual permanently non-dominated: rank 0, straight into the
// returned Pareto front. These tests pin "a NaN-scored individual is
// treated as infeasible and cannot appear in the front while any feasible
// point exists".

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class NsgaNaNFeasibilityTests
{
    /// <summary>
    /// 2-objective 1-D problem: minimise (x, 1−x). A poison pocket around
    /// x ≈ 0.5 returns Score = NaN (e.g. a solver singularity), everywhere
    /// else the score is finite and the extractor returns clean objectives.
    /// </summary>
    private sealed class NaNPocketObjective : IObjective
    {
        public int DimensionCount => 1;

        public IReadOnlyList<DesignVariableInfo> Variables { get; } =
            new[] { new DesignVariableInfo("x", 0.0, 1.0) };

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            double x = vector[0];
            double score = Math.Abs(x - 0.5) < 0.05 ? double.NaN : x;
            return new EvaluationResult(score, Array.Empty<FeasibilityViolation>(), null);
        }
    }

    private static double[] Extract(EvaluationResult eval)
        // A custom extractor (the documented 4+-objective path) that
        // trusts Score — NaN flows straight into the objective tuple.
        => new[] { eval.Score, 1.0 - eval.Score };

    [Fact]
    public void NsgaII_NaNScoredIndividuals_AreInfeasibleAndKeptOutOfTheFront()
    {
        var opt = new NsgaIIOptimizer(
            new NaNPocketObjective(), Extract,
            populationSize: 24, maxGenerations: 12, seed: 42);

        var result = opt.Run();

        Assert.NotEmpty(result.ParetoFront);
        foreach (var ind in result.ParetoFront)
        {
            Assert.NotNull(ind.Objectives);
            foreach (double obj in ind.Objectives!)
                Assert.False(double.IsNaN(obj),
                    "a NaN-scored individual reached the Pareto front — it must " +
                    "be classified infeasible (CV > 0) and dominated out");
        }
    }

    [Fact]
    public void NsgaIII_NaNScoredIndividuals_AreInfeasibleAndKeptOutOfTheFront()
    {
        var opt = new NsgaIIIOptimizer(
            new NaNPocketObjective(), Extract,
            populationSize: 24, maxGenerations: 12, seed: 42);

        var result = opt.Run();

        Assert.NotEmpty(result.ParetoFront);
        foreach (var ind in result.ParetoFront)
        {
            Assert.NotNull(ind.Objectives);
            foreach (double obj in ind.Objectives!)
                Assert.False(double.IsNaN(obj),
                    "a NaN-scored individual reached the NSGA-III front — it must " +
                    "be classified infeasible (CV > 0) and dominated out");
        }
    }
}
