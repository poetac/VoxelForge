// BayesianInfeasiblePoisoningTests — red-team round 3 regression (fail-on-old /
// pass-on-new). A single hard-infeasible (+∞) sample in the training set
// poisoned the GP solve (α = K⁻¹y → all NaN), which made every acquisition
// value NaN; `NaN > −∞` never improves, so the loop's candidate stayed its
// `new double[dim]` initializer — the UNCLAMPED ZERO VECTOR, outside the
// variable bounds — and was evaluated for every remaining iteration. These
// tests pin the contract "the optimizer only ever evaluates points inside
// the declared bounds", which the fix restores by fitting the GP on the
// finite-score subset and falling back to Sobol exploration when no finite
// sample exists yet.

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Voxelforge.Optimization.Bayesian;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class BayesianInfeasiblePoisoningTests
{
    /// <summary>
    /// 1-D bowl centred at x = 700 on bounds [100, 1000]; everything below
    /// the feasibility wall at x = 550 reports the +∞ infeasibility sentinel.
    /// Records every x it is asked to evaluate.
    /// </summary>
    private sealed class GatedBowlObjective : IObjective
    {
        public List<double> EvaluatedPoints { get; } = new();
        private readonly bool _alwaysInfeasible;

        public GatedBowlObjective(bool alwaysInfeasible = false)
            => _alwaysInfeasible = alwaysInfeasible;

        public int DimensionCount => 1;

        public IReadOnlyList<DesignVariableInfo> Variables { get; } =
            new[] { new DesignVariableInfo("x", 100.0, 1000.0) };

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            double x = vector[0];
            EvaluatedPoints.Add(x);
            if (_alwaysInfeasible || x < 550.0)
            {
                var violations = new[]
                {
                    new FeasibilityViolation("FEAS_WALL", "x below feasibility wall", x, 550.0),
                };
                return new EvaluationResult(double.PositiveInfinity, violations, null);
            }
            double d = x - 700.0;
            return new EvaluationResult(d * d, Array.Empty<FeasibilityViolation>(), null);
        }
    }

    [Fact]
    public void MixedFeasibility_EvaluatesOnlyInsideDeclaredBounds()
    {
        // The Sobol initial design straddles the wall, so the training set
        // contains a mix of finite and +∞ scores — the poisoning trigger.
        var objective = new GatedBowlObjective();
        var optimizer = new BayesianOptimizer(
            objective, initialDesignSize: 8, maxIterations: 10, seed: 42);

        var result = optimizer.Run();

        Assert.All(objective.EvaluatedPoints, x =>
            Assert.True(x is >= 100.0 and <= 1000.0,
                $"BO evaluated x = {x}, outside the declared bounds [100, 1000] " +
                "— the poisoned-GP zero-vector escape"));
        Assert.True(double.IsFinite(result.BestScore),
            "a feasible region was sampled, so the best score must be finite");
        Assert.InRange(result.BestParams[0], 100.0, 1000.0);
    }

    [Fact]
    public void AllInfeasible_FallsBackToBoundedExplorationWithoutThrowing()
    {
        // With zero finite samples the GP has nothing to condition on; the
        // optimizer must keep exploring inside bounds (deterministic Sobol),
        // not spiral on the unclamped zero vector.
        var objective = new GatedBowlObjective(alwaysInfeasible: true);
        var optimizer = new BayesianOptimizer(
            objective, initialDesignSize: 4, maxIterations: 6, seed: 42);

        var result = optimizer.Run();

        Assert.All(objective.EvaluatedPoints, x =>
            Assert.True(x is >= 100.0 and <= 1000.0,
                $"BO evaluated x = {x}, outside the declared bounds [100, 1000]"));
        Assert.True(double.IsPositiveInfinity(result.BestScore));
        Assert.Equal(4 + 6, objective.EvaluatedPoints.Count);   // full budget spent exploring
    }
}
