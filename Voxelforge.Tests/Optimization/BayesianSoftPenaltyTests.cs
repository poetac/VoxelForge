// BayesianSoftPenaltyTests — Phase 2 of #627 (tracked under #743). Pins
// the BayesianOptimizer.useSoftPenalty=true behavior:
//   • Default (useSoftPenalty=false) → infeasible candidates still hit +∞ cliff
//   • useSoftPenalty=true → infeasible candidates get finite soft-penalty score
//   • Default behavior bit-identical to explicit useSoftPenalty=false (back-compat)

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Voxelforge.Optimization.Bayesian;
using Xunit;

namespace Voxelforge.Tests.Optimization;

public sealed class BayesianSoftPenaltyTests
{
    /// <summary>
    /// Same synthetic infeasibility-emitter pattern as
    /// <c>CmaEsSoftPenaltyTests.AlwaysInfeasibleObjective</c> — duplicated
    /// here to keep the BayesianOptimizer test fixture self-contained.
    /// </summary>
    private sealed class AlwaysInfeasibleObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;

        public AlwaysInfeasibleObjective(int dim)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++)
                _vars[i] = new DesignVariableInfo($"x{i}", 0.0, 1.0);
        }

        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            var violation = new FeasibilityViolation(
                ConstraintId: "WALL_TEMP",
                Description:  "synthetic test violation",
                ActualValue:  1100.0 + vector[0] * 100.0,
                Limit:        1100.0);
            return new EvaluationResult(
                double.PositiveInfinity,
                new[] { violation },
                null);
        }
    }

    [Fact]
    public void Default_NoSoftPenalty_BestScoreStaysInfinity()
    {
        var opt = new BayesianOptimizer(
            objective:          new AlwaysInfeasibleObjective(dim: 2),
            initialDesignSize:  4,
            maxIterations:      2,
            seed:               42);
        var result = opt.Run();
        Assert.True(double.IsPositiveInfinity(result.BestScore));
    }

    [Fact]
    public void UseSoftPenalty_True_ProducesFiniteScore()
    {
        var opt = new BayesianOptimizer(
            objective:          new AlwaysInfeasibleObjective(dim: 2),
            initialDesignSize:  4,
            maxIterations:      2,
            seed:               42,
            useSoftPenalty:     true);
        var result = opt.Run();
        Assert.True(double.IsFinite(result.BestScore));
        Assert.True(result.BestScore > 0.0,
            "Soft penalty should be strictly positive on infeasible candidates");
    }

    [Fact]
    public void Default_BitIdenticalTo_ExplicitFalse()
    {
        var opt1 = new BayesianOptimizer(
            objective:          new AlwaysInfeasibleObjective(dim: 2),
            initialDesignSize:  3,
            maxIterations:      1,
            seed:               42);
        var r1 = opt1.Run();

        var opt2 = new BayesianOptimizer(
            objective:          new AlwaysInfeasibleObjective(dim: 2),
            initialDesignSize:  3,
            maxIterations:      1,
            seed:               42,
            useSoftPenalty:     false);
        var r2 = opt2.Run();

        Assert.Equal(r1.BestScore, r2.BestScore);
        Assert.Equal(r1.BestParams, r2.BestParams);
        Assert.Equal(r1.TotalEvaluations, r2.TotalEvaluations);
    }

    [Fact]
    public void UseSoftPenalty_True_BoundedNearSinglePenaltyScale()
    {
        var opt = new BayesianOptimizer(
            objective:          new AlwaysInfeasibleObjective(dim: 1),
            initialDesignSize:  3,
            maxIterations:      1,
            seed:               42,
            useSoftPenalty:     true);
        var result = opt.Run();
        Assert.True(result.BestScore > 0.0);
        Assert.True(result.BestScore <= SoftPenalty.PenaltyScale,
            $"BestScore {result.BestScore} should be ≤ PenaltyScale {SoftPenalty.PenaltyScale} for a single-violation candidate");
    }
}
