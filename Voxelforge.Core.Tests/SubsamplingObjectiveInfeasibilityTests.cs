// SubsamplingObjectiveInfeasibilityTests — pins the infeasibility-sentinel
// invariant of SubsamplingObjective on the cross-platform Linux CI leg.
//
// Red-team finding: when the central candidate is INFEASIBLE (Score == +∞)
// but its ±ε neighbours are feasible, the naive median of the score set is
// finite, and `return centralResult with { Score = median }` would relabel the
// infeasible centre with that finite score — erasing the +∞ sentinel the
// IObjective contract relies on and letting a bare SA/Func loop accept an
// infeasible design as a candidate / new best. The fix keeps the sentinel for
// an infeasible centre while preserving the robustness median for a feasible
// one (including a finite score that carries advisory, non-infeasible warnings).

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class SubsamplingObjectiveInfeasibilityTests
{
    /// <summary>
    /// 1-dim stub: the centre (x == 0.5) returns <paramref name="centerScore"/>
    /// (optionally with one advisory violation); every perturbed neighbour
    /// (x = 0.5 ± step) returns a clean feasible <paramref name="neighbourScore"/>.
    /// </summary>
    private sealed class CentreVsNeighbourObjective : IObjective
    {
        private readonly double _centerScore;
        private readonly bool _centerHasViolation;
        private readonly double _neighbourScore;

        public CentreVsNeighbourObjective(double centerScore, bool centerHasViolation, double neighbourScore)
        {
            _centerScore = centerScore;
            _centerHasViolation = centerHasViolation;
            _neighbourScore = neighbourScore;
        }

        public int DimensionCount => 1;

        public IReadOnlyList<DesignVariableInfo> Variables { get; } =
            new[] { new DesignVariableInfo("x", 0.0, 1.0) };

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            bool isCentre = Math.Abs(vector[0] - 0.5) < 1e-9;
            if (isCentre)
            {
                IReadOnlyList<FeasibilityViolation> violations = _centerHasViolation
                    ? new[] { new FeasibilityViolation("TEST_GATE", "centre breach", 1.0, 0.0) }
                    : Array.Empty<FeasibilityViolation>();
                return new EvaluationResult(_centerScore, violations, null);
            }

            return new EvaluationResult(_neighbourScore, Array.Empty<FeasibilityViolation>(), null);
        }
    }

    [Fact]
    public void InfeasibleCentre_KeepsInfinitySentinel_DoesNotLeakFiniteMedian()
    {
        // Centre is infeasible (+∞); both neighbours feasible (10.0). The naive
        // median of {+∞, 10, 10} is 10.0 — the wrapper must NOT report that.
        var sub = new SubsamplingObjective(
            new CentreVsNeighbourObjective(
                centerScore: double.PositiveInfinity, centerHasViolation: true, neighbourScore: 10.0),
            neighbourCount: 1);

        var r = sub.Evaluate(new[] { 0.5 });

        Assert.True(double.IsPositiveInfinity(r.Score),
            $"Infeasible centre leaked a finite score {r.Score}; the +∞ sentinel must survive subsampling.");
        Assert.NotEmpty(r.Violations);
    }

    [Fact]
    public void FeasibleCentreWithAdvisoryWarnings_StillReturnsNeighbourMedian()
    {
        // A FINITE central score carrying advisory (non-infeasible) Violations is
        // a valid "feasible with warnings" state — it must still get the
        // robustness median (the guard keys on infeasibility, not on the mere
        // presence of Violations). Median of {5, 10, 10} = 10.0, and the central
        // result's advisory violations are preserved.
        var sub = new SubsamplingObjective(
            new CentreVsNeighbourObjective(
                centerScore: 5.0, centerHasViolation: true, neighbourScore: 10.0),
            neighbourCount: 1);

        var r = sub.Evaluate(new[] { 0.5 });

        Assert.Equal(10.0, r.Score, precision: 9);
        Assert.NotEmpty(r.Violations);
    }
}
