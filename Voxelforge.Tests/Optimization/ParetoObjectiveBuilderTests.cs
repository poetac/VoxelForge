// ParetoObjectiveBuilderTests.cs — Sprint EC.W12 tests.
//
// Pins for the multi-objective extractor helpers that feed
// NsgaIIOptimizer / NsgaIIIOptimizer:
//   • PhysicsAndCost / PhysicsAndMass produce correct 2-vectors.
//   • PhysicsCostAndCo2 produces a 3-vector in the expected order.
//   • Infeasible candidates (Violations non-empty OR Score = +∞) map to
//     all-+∞ tuples regardless of breakdown content.
//   • Null-argument guards on every factory.
//   • Determinism: same input ⇒ same output across repeated calls.

using System;
using System.Collections.Generic;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Optimization;

public sealed class ParetoObjectiveBuilderTests
{
    private static EvaluationResult Feasible(
        double score,
        (double Cost, double Mass, double Co2) breakdown) =>
        new(score, Array.Empty<FeasibilityViolation>(), breakdown);

    private static EvaluationResult Infeasible_ViaViolations(
        double score,
        (double Cost, double Mass, double Co2) breakdown) =>
        new(
            score,
            new[] { new FeasibilityViolation("MOCK_FAIL", "synthetic", ActualValue: 0, Limit: 1) },
            breakdown);

    private static EvaluationResult Infeasible_ViaInfinityScore(
        (double Cost, double Mass, double Co2) breakdown) =>
        new(
            double.PositiveInfinity,
            Array.Empty<FeasibilityViolation>(),
            breakdown);

    // ── PhysicsAndCost ────────────────────────────────────────────────

    [Fact]
    public void PhysicsAndCost_ReturnsExpectedTuple_OnFeasible()
    {
        var extractor = ParetoObjectiveBuilder.PhysicsAndCost(
            physicsScoreFn: r => -3300.0,  // negative Isp (maximise Isp = minimise -Isp)
            costFn:         b => (((double Cost, double Mass, double Co2))b!).Cost);
        var tuple = extractor(Feasible(score: -3300.0,
            breakdown: (Cost: 500.0, Mass: 95.0, Co2: 250.0)));
        Assert.Equal(2, tuple.Length);
        Assert.Equal(-3300.0, tuple[0]);
        Assert.Equal(  500.0, tuple[1]);
    }

    [Fact]
    public void PhysicsAndCost_RoutesViolationsToInfinityTuple()
    {
        var extractor = ParetoObjectiveBuilder.PhysicsAndCost(
            physicsScoreFn: r => r.Score,
            costFn:         b => (((double Cost, double Mass, double Co2))b!).Cost);
        var tuple = extractor(Infeasible_ViaViolations(score: 100.0,
            breakdown: (Cost: 1.0, Mass: 1.0, Co2: 1.0)));
        Assert.Equal(2, tuple.Length);
        Assert.True(double.IsPositiveInfinity(tuple[0]));
        Assert.True(double.IsPositiveInfinity(tuple[1]));
    }

    [Fact]
    public void PhysicsAndCost_RoutesInfinityScoreToInfinityTuple()
    {
        // Some pillar objectives return +∞ with empty Violations (soft
        // infeasibility). The extractor must catch that too.
        var extractor = ParetoObjectiveBuilder.PhysicsAndCost(
            physicsScoreFn: r => r.Score,
            costFn:         b => 0.0);
        var tuple = extractor(Infeasible_ViaInfinityScore(
            breakdown: (Cost: 0.0, Mass: 0.0, Co2: 0.0)));
        Assert.True(double.IsPositiveInfinity(tuple[0]));
        Assert.True(double.IsPositiveInfinity(tuple[1]));
    }

    [Fact]
    public void PhysicsAndCost_RoutesNaNExtractorOutputToInfinityTuple()
    {
        // Audit follow-up (#509): even when the inner result reports
        // feasible (Violations empty, Score finite), a NaN escaping the
        // extractor lambdas must NOT propagate into the Pareto tuple —
        // NSGA-II / NSGA-III dominance comparisons silently treat NaN as
        // non-dominated and corrupt the front.
        var extractor = ParetoObjectiveBuilder.PhysicsAndCost(
            physicsScoreFn: r => r.Score,
            costFn:         _ => double.NaN);          // cost model divides by zero
        var tuple = extractor(Feasible(score: -3300.0,
            breakdown: (Cost: 0.0, Mass: 0.0, Co2: 0.0)));
        Assert.Equal(2, tuple.Length);
        Assert.True(double.IsPositiveInfinity(tuple[0]),
            $"NaN slice must route the whole tuple to +∞ (got tuple[0]={tuple[0]})");
        Assert.True(double.IsPositiveInfinity(tuple[1]),
            $"NaN slice must route the whole tuple to +∞ (got tuple[1]={tuple[1]})");
        Assert.False(double.IsNaN(tuple[0]) || double.IsNaN(tuple[1]),
            "NaN must NOT leak into the Pareto tuple.");
    }

    [Fact]
    public void PhysicsAndCost_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => ParetoObjectiveBuilder.PhysicsAndCost(null!, _ => 0.0));
        Assert.Throws<ArgumentNullException>(
            () => ParetoObjectiveBuilder.PhysicsAndCost(_ => 0.0, null!));
    }

    // ── PhysicsAndMass ────────────────────────────────────────────────

    [Fact]
    public void PhysicsAndMass_ReturnsExpectedTuple_OnFeasible()
    {
        var extractor = ParetoObjectiveBuilder.PhysicsAndMass(
            physicsScoreFn: r => -3300.0,
            massFn:         b => (((double Cost, double Mass, double Co2))b!).Mass);
        var tuple = extractor(Feasible(score: -3300.0,
            breakdown: (Cost: 500.0, Mass: 95.0, Co2: 250.0)));
        Assert.Equal(2, tuple.Length);
        Assert.Equal(-3300.0, tuple[0]);
        Assert.Equal(   95.0, tuple[1]);
    }

    [Fact]
    public void PhysicsAndMass_RoutesInfeasibleToInfinityTuple()
    {
        var extractor = ParetoObjectiveBuilder.PhysicsAndMass(
            physicsScoreFn: r => r.Score,
            massFn:         b => 0.0);
        var tuple = extractor(Infeasible_ViaViolations(score: 0.0,
            breakdown: (Cost: 0.0, Mass: 0.0, Co2: 0.0)));
        Assert.True(double.IsPositiveInfinity(tuple[0]));
        Assert.True(double.IsPositiveInfinity(tuple[1]));
    }

    [Fact]
    public void PhysicsAndMass_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => ParetoObjectiveBuilder.PhysicsAndMass(null!, _ => 0.0));
        Assert.Throws<ArgumentNullException>(
            () => ParetoObjectiveBuilder.PhysicsAndMass(_ => 0.0, null!));
    }

    // ── PhysicsCostAndCo2 ─────────────────────────────────────────────

    [Fact]
    public void PhysicsCostAndCo2_ReturnsThreeTuple_OnFeasible()
    {
        var extractor = ParetoObjectiveBuilder.PhysicsCostAndCo2(
            physicsScoreFn: r => -3300.0,
            costFn:         b => (((double Cost, double Mass, double Co2))b!).Cost,
            co2Fn:          b => (((double Cost, double Mass, double Co2))b!).Co2);
        var tuple = extractor(Feasible(score: -3300.0,
            breakdown: (Cost: 500.0, Mass: 95.0, Co2: 250.0)));
        Assert.Equal(3, tuple.Length);
        Assert.Equal(-3300.0, tuple[0]);  // physics
        Assert.Equal(  500.0, tuple[1]);  // cost
        Assert.Equal(  250.0, tuple[2]);  // co2
    }

    [Fact]
    public void PhysicsCostAndCo2_RoutesInfeasibleToInfinityTuple()
    {
        var extractor = ParetoObjectiveBuilder.PhysicsCostAndCo2(
            physicsScoreFn: r => r.Score,
            costFn:         b => 1.0,
            co2Fn:          b => 1.0);
        var tuple = extractor(Infeasible_ViaViolations(score: 0.0,
            breakdown: (Cost: 0.0, Mass: 0.0, Co2: 0.0)));
        Assert.Equal(3, tuple.Length);
        foreach (var v in tuple)
            Assert.True(double.IsPositiveInfinity(v));
    }

    [Fact]
    public void PhysicsCostAndCo2_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => ParetoObjectiveBuilder.PhysicsCostAndCo2(null!, _ => 0.0, _ => 0.0));
        Assert.Throws<ArgumentNullException>(
            () => ParetoObjectiveBuilder.PhysicsCostAndCo2(_ => 0.0, null!, _ => 0.0));
        Assert.Throws<ArgumentNullException>(
            () => ParetoObjectiveBuilder.PhysicsCostAndCo2(_ => 0.0, _ => 0.0, null!));
    }

    // ── PhysicsAndLcoe ────────────────────────────────────────────────

    [Fact]
    public void PhysicsAndLcoe_ReturnsExpectedTuple_OnFeasible()
    {
        // Power-gen Pareto: minimise -P_electric + minimise $/kWh.
        var extractor = ParetoObjectiveBuilder.PhysicsAndLcoe(
            physicsScoreFn: r => -650.0,         // -650 W electric output
            lcoeFn:         b => 0.05);          // 5¢/kWh
        var tuple = extractor(Feasible(score: -650.0,
            breakdown: (Cost: 1000.0, Mass: 5.0, Co2: 100.0)));
        Assert.Equal(2, tuple.Length);
        Assert.Equal(-650.0, tuple[0]);
        Assert.Equal(  0.05, tuple[1]);
    }

    [Fact]
    public void PhysicsAndLcoe_RoutesInfeasibleToInfinityTuple()
    {
        var extractor = ParetoObjectiveBuilder.PhysicsAndLcoe(
            physicsScoreFn: r => r.Score,
            lcoeFn:         b => 0.05);
        var tuple = extractor(Infeasible_ViaViolations(score: 100.0,
            breakdown: (Cost: 1.0, Mass: 1.0, Co2: 1.0)));
        Assert.True(double.IsPositiveInfinity(tuple[0]));
        Assert.True(double.IsPositiveInfinity(tuple[1]));
    }

    [Fact]
    public void PhysicsAndLcoe_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => ParetoObjectiveBuilder.PhysicsAndLcoe(null!, _ => 0.0));
        Assert.Throws<ArgumentNullException>(
            () => ParetoObjectiveBuilder.PhysicsAndLcoe(_ => 0.0, null!));
    }

    // ── CostAndCo2PerOutputUnit ───────────────────────────────────────

    [Fact]
    public void CostAndCo2PerOutputUnit_DividesByOutput()
    {
        // cost = 500, co2 = 250, output (thrust) = 0.270 N
        //   → $/N = 500/0.270 ≈ 1852, kg/N = 250/0.270 ≈ 926.
        var extractor = ParetoObjectiveBuilder.CostAndCo2PerOutputUnit(
            costFn:   b => (((double Cost, double Mass, double Co2))b!).Cost,
            co2Fn:    b => (((double Cost, double Mass, double Co2))b!).Co2,
            outputFn: b => 0.270);
        var tuple = extractor(Feasible(score: -100.0,
            breakdown: (Cost: 500.0, Mass: 95.0, Co2: 250.0)));
        Assert.Equal(2, tuple.Length);
        Assert.Equal(500.0 / 0.270, tuple[0], precision: 6);
        Assert.Equal(250.0 / 0.270, tuple[1], precision: 6);
    }

    [Fact]
    public void CostAndCo2PerOutputUnit_RoutesNonPositiveOutputToInfinityTuple()
    {
        var extractor = ParetoObjectiveBuilder.CostAndCo2PerOutputUnit(
            costFn:   b => 100.0,
            co2Fn:    b => 50.0,
            outputFn: b => 0.0);
        var tuple = extractor(Feasible(score: -100.0,
            breakdown: (Cost: 100.0, Mass: 1.0, Co2: 50.0)));
        Assert.True(double.IsPositiveInfinity(tuple[0]));
        Assert.True(double.IsPositiveInfinity(tuple[1]));
    }

    [Fact]
    public void CostAndCo2PerOutputUnit_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => ParetoObjectiveBuilder.CostAndCo2PerOutputUnit(null!, _ => 0.0, _ => 1.0));
        Assert.Throws<ArgumentNullException>(
            () => ParetoObjectiveBuilder.CostAndCo2PerOutputUnit(_ => 0.0, null!, _ => 1.0));
        Assert.Throws<ArgumentNullException>(
            () => ParetoObjectiveBuilder.CostAndCo2PerOutputUnit(_ => 0.0, _ => 0.0, null!));
    }

    // ── Determinism ───────────────────────────────────────────────────

    [Fact]
    public void PhysicsAndCost_Deterministic_AcrossRepeatedCalls()
    {
        // Pure extractor — repeated calls must produce equal tuples.
        var extractor = ParetoObjectiveBuilder.PhysicsAndCost(
            physicsScoreFn: r => r.Score,
            costFn:         b => (((double Cost, double Mass, double Co2))b!).Cost);
        var input = Feasible(score: -1500.0,
            breakdown: (Cost: 300.0, Mass: 40.0, Co2: 100.0));
        var t1 = extractor(input);
        var t2 = extractor(input);
        Assert.Equal(t1[0], t2[0]);
        Assert.Equal(t1[1], t2[1]);
    }
}
