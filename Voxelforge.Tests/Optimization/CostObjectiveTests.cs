// CostObjectiveTests.cs — Sprint EC.W11 cost-objective wrapper tests.
//
// Pins:
//   • Cost extractor replaces the inner score when feasible.
//   • Infeasible candidates fall through to InfeasibleScore.
//   • Variables / DimensionCount delegate to the wrapped objective.
//   • PerOutputUnit divides cost by output and short-circuits at zero.
//   • Constructor + Evaluate guards (null arguments, wrong vector length).

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Optimization;

public sealed class CostObjectiveTests
{
    /// <summary>
    /// Synthetic mock objective. The breakdown carries a hand-stuffed
    /// (cost, output) pair so tests can verify the wrapper pipes through
    /// the cost extractor without invoking real pillar code.
    /// </summary>
    private sealed class MockObjective : IObjective
    {
        private readonly Func<double[], (double Cost, double Output, bool Feasible)> _fn;
        private readonly DesignVariableInfo[] _vars;

        public MockObjective(
            Func<double[], (double, double, bool)> fn,
            int dim = 2)
        {
            _fn = fn;
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++)
                _vars[i] = new DesignVariableInfo($"x{i}", 0.0, 1.0);
        }

        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            var arr = vector.ToArray();
            var (cost, output, feasible) = _fn(arr);
            var violations = feasible
                ? (IReadOnlyList<FeasibilityViolation>)Array.Empty<FeasibilityViolation>()
                : new[]
                {
                    new FeasibilityViolation("MOCK_FAIL", "synthetic", ActualValue: 0, Limit: 1),
                };
            return new EvaluationResult(
                Score: feasible ? 100.0 : double.PositiveInfinity,
                Violations: violations,
                EngineSpecificBreakdown: (cost, output));
        }
    }

    [Fact]
    public void Evaluate_ReplacesInnerScoreWithCost_OnFeasible()
    {
        var inner = new MockObjective(_ => (500.0, 0.270, true));
        var cost = new CostObjective(inner, b => (((double Cost, double Output))b!).Cost);
        var result = cost.Evaluate(new[] { 0.5, 0.5 });

        Assert.Equal(500.0, result.Score);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Evaluate_RoutesToInfeasibleScore_WhenInnerInfeasible()
    {
        var inner = new MockObjective(_ => (1.0, 1.0, false));
        var cost = new CostObjective(inner, _ => 1.0);  // would-be tiny cost
        var result = cost.Evaluate(new[] { 0.5, 0.5 });

        Assert.True(double.IsPositiveInfinity(result.Score),
            "Infeasible candidates must score +∞ regardless of cost. A $1 broken design "
          + "must NOT beat a $100 working design.");
        Assert.NotEmpty(result.Violations);
    }

    [Fact]
    public void Evaluate_HonoursCustomInfeasibleScore()
    {
        var inner = new MockObjective(_ => (1.0, 1.0, false));
        var cost = new CostObjective(inner, _ => 1.0, infeasibleScore: 1e9);
        var result = cost.Evaluate(new[] { 0.5, 0.5 });

        Assert.Equal(1e9, result.Score);
    }

    [Fact]
    public void Evaluate_RoutesToInfeasibleScore_WhenInnerScoreIsPositiveInfinity()
    {
        // Some pillar objectives return +∞ score with an empty Violations
        // list (a "soft" infeasible). The wrapper must still route around
        // them rather than passing the cost through.
        var inner = new MockObjective(_ => (1.0, 1.0, true))
        {
            // Mock above always emits feasible — switch behavior by giving
            // a custom one that returns +∞ + no violations.
        };
        var alwaysInfScore = new InlineObjective(
            score: double.PositiveInfinity,
            violations: Array.Empty<FeasibilityViolation>(),
            breakdown: (5.0, 5.0));
        var cost = new CostObjective(alwaysInfScore, _ => 1.0);
        var result = cost.Evaluate(new[] { 0.5 });
        Assert.True(double.IsPositiveInfinity(result.Score));
    }

    [Fact]
    public void Variables_DimensionCount_DelegateToInner()
    {
        var inner = new MockObjective(_ => (1.0, 1.0, true), dim: 4);
        var cost = new CostObjective(inner, _ => 0.0);
        Assert.Equal(4, cost.DimensionCount);
        Assert.Equal(4, cost.Variables.Count);
        Assert.Same(inner.Variables, cost.Variables);
    }

    [Fact]
    public void Constructor_RejectsNullInner()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CostObjective(null!, _ => 0.0));
    }

    [Fact]
    public void Constructor_RejectsNullCostFn()
    {
        var inner = new MockObjective(_ => (1.0, 1.0, true));
        Assert.Throws<ArgumentNullException>(
            () => new CostObjective(inner, null!));
    }

    [Fact]
    public void PerOutputUnit_DividesCostByOutput()
    {
        // cost = 500, output = 0.270 → $/N = 500 / 0.270 ≈ 1852.
        var inner = new MockObjective(_ => (500.0, 0.270, true));
        var perN = CostObjective.PerOutputUnit(
            inner:    inner,
            costFn:   b => (((double Cost, double Output))b!).Cost,
            outputFn: b => (((double Cost, double Output))b!).Output);
        var result = perN.Evaluate(new[] { 0.5, 0.5 });
        Assert.Equal(500.0 / 0.270, result.Score, precision: 6);
    }

    [Fact]
    public void PerOutputUnit_RoutesToInfeasible_OnZeroOutput()
    {
        var inner = new MockObjective(_ => (500.0, 0.0, true));
        var perN = CostObjective.PerOutputUnit(
            inner:    inner,
            costFn:   b => (((double Cost, double Output))b!).Cost,
            outputFn: b => (((double Cost, double Output))b!).Output);
        var result = perN.Evaluate(new[] { 0.5, 0.5 });
        Assert.True(double.IsPositiveInfinity(result.Score),
            "Zero-output designs must score infeasible — division by zero "
          + "is not a valid operating point even if every hard gate passes.");
    }

    [Fact]
    public void PerOutputUnit_RoutesToInfeasible_OnNegativeOutput()
    {
        var inner = new MockObjective(_ => (500.0, -0.1, true));
        var perN = CostObjective.PerOutputUnit(
            inner:    inner,
            costFn:   b => (((double Cost, double Output))b!).Cost,
            outputFn: b => (((double Cost, double Output))b!).Output);
        var result = perN.Evaluate(new[] { 0.5, 0.5 });
        Assert.True(double.IsPositiveInfinity(result.Score));
    }

    [Fact]
    public void Deterministic_AcrossRepeatedCalls()
    {
        // SA's strict-determinism contract — same vector ⇒ same score.
        var inner = new MockObjective(v => (v[0] * 1000.0, v[1], true));
        var cost = new CostObjective(inner, b => (((double Cost, double Output))b!).Cost);
        var v = new[] { 0.42, 0.17 };
        var r1 = cost.Evaluate(v);
        var r2 = cost.Evaluate(v);
        var r3 = cost.Evaluate(v);
        Assert.Equal(r1.Score, r2.Score);
        Assert.Equal(r2.Score, r3.Score);
    }

    // ── ByEmbodiedCO2 + ByMass factories ────────────────────────────────

    [Fact]
    public void ByEmbodiedCO2_ScoresByCarbonExtractor()
    {
        // Breakdown stuffed with 250 kg CO2-eq; extractor reads index 2.
        var inner = new MockObjective(_ => (500.0, 0.27, true));
        var co2 = CostObjective.ByEmbodiedCO2(inner, b => 250.0);
        var result = co2.Evaluate(new[] { 0.5, 0.5 });
        Assert.Equal(250.0, result.Score);
    }

    [Fact]
    public void ByMass_ScoresByMassExtractor()
    {
        var inner = new MockObjective(_ => (500.0, 0.27, true));
        var mass = CostObjective.ByMass(inner, b => 95.0);
        var result = mass.Evaluate(new[] { 0.5, 0.5 });
        Assert.Equal(95.0, result.Score);
    }

    [Fact]
    public void ByEmbodiedCO2_RoutesInfeasibleThroughInnerViolations()
    {
        var inner = new MockObjective(_ => (1.0, 1.0, false));
        var co2 = CostObjective.ByEmbodiedCO2(inner, b => 50.0);
        var result = co2.Evaluate(new[] { 0.5, 0.5 });
        Assert.True(double.IsPositiveInfinity(result.Score));
    }

    // ── Co2PerOutputUnit factory ────────────────────────────────────────

    [Fact]
    public void Co2PerOutputUnit_DividesCo2ByOutput()
    {
        // co2 = 250 kg, output = 0.270 N → kg-CO2-eq/N = 250/0.270.
        var inner = new MockObjective(_ => (500.0, 0.270, true));
        var co2pn = CostObjective.Co2PerOutputUnit(
            inner:    inner,
            co2Fn:    b => 250.0,
            outputFn: b => (((double Cost, double Output))b!).Output);
        var result = co2pn.Evaluate(new[] { 0.5, 0.5 });
        Assert.Equal(250.0 / 0.270, result.Score, precision: 6);
    }

    [Fact]
    public void Co2PerOutputUnit_RoutesNonPositiveOutputToInfeasible()
    {
        var inner = new MockObjective(_ => (500.0, 0.0, true));
        var co2pn = CostObjective.Co2PerOutputUnit(
            inner:    inner,
            co2Fn:    b => 250.0,
            outputFn: b => (((double Cost, double Output))b!).Output);
        var result = co2pn.Evaluate(new[] { 0.5, 0.5 });
        Assert.True(double.IsPositiveInfinity(result.Score));
    }

    // ── ByLcoe factory ──────────────────────────────────────────────────

    [Fact]
    public void ByLcoe_ScoresByLcoeExtractor()
    {
        var inner = new MockObjective(_ => (500.0, 0.270, true));
        var lcoe = CostObjective.ByLcoe(inner, _ => 0.05);   // $0.05 / kWh
        var result = lcoe.Evaluate(new[] { 0.5, 0.5 });
        Assert.Equal(0.05, result.Score, precision: 6);
    }

    [Fact]
    public void ByLcoe_RoutesInfeasible_OnInnerViolations()
    {
        var inner = new MockObjective(_ => (1.0, 1.0, false));
        var lcoe = CostObjective.ByLcoe(inner, _ => 0.05);
        var result = lcoe.Evaluate(new[] { 0.5, 0.5 });
        Assert.True(double.IsPositiveInfinity(result.Score));
    }

    // ── WithBudgetCeiling — inner score preserved when in budget ────────

    [Fact]
    public void WithBudgetCeiling_PreservesInnerScore_WhenCostBelowBudget()
    {
        // Inner score is 100 (mock always returns 100 when feasible).
        // Cost 500, budget 1000 → under budget → score = 100.
        var inner = new MockObjective(_ => (500.0, 0.27, true));
        var gated = CostObjective.WithBudgetCeiling(
            inner:    inner,
            costFn:   b => (((double Cost, double Output))b!).Cost,
            budget_USD: 1000.0);
        var result = gated.Evaluate(new[] { 0.5, 0.5 });
        Assert.Equal(100.0, result.Score);
    }

    [Fact]
    public void WithBudgetCeiling_RoutesToInfeasible_WhenCostExceedsBudget()
    {
        // Cost 1500, budget 1000 → over budget.
        var inner = new MockObjective(_ => (1500.0, 0.27, true));
        var gated = CostObjective.WithBudgetCeiling(
            inner:    inner,
            costFn:   b => (((double Cost, double Output))b!).Cost,
            budget_USD: 1000.0);
        var result = gated.Evaluate(new[] { 0.5, 0.5 });
        Assert.True(double.IsPositiveInfinity(result.Score));
    }

    [Fact]
    public void WithBudgetCeiling_RoutesToInfeasible_WhenInnerInfeasible()
    {
        // Inner infeasible takes priority over budget check.
        var inner = new MockObjective(_ => (1.0, 1.0, false));
        var gated = CostObjective.WithBudgetCeiling(
            inner:    inner,
            costFn:   b => (((double Cost, double Output))b!).Cost,
            budget_USD: 1000.0);
        var result = gated.Evaluate(new[] { 0.5, 0.5 });
        Assert.True(double.IsPositiveInfinity(result.Score));
    }

    [Fact]
    public void WithBudgetCeiling_RejectsNonPositiveBudget()
    {
        var inner = new MockObjective(_ => (1.0, 1.0, true));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CostObjective.WithBudgetCeiling(inner, b => 0.0, budget_USD: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CostObjective.WithBudgetCeiling(inner, b => 0.0, budget_USD: -100.0));
    }

    [Fact]
    public void WithBudgetCeiling_DelegatesVariables_ToInner()
    {
        var inner = new MockObjective(_ => (1.0, 1.0, true), dim: 3);
        var gated = CostObjective.WithBudgetCeiling(inner, b => 0.0, budget_USD: 1.0);
        Assert.Equal(3, gated.DimensionCount);
        Assert.Equal(3, gated.Variables.Count);
    }

    // ── NaN-path hardening tests (audit follow-up to commit 2c15ad7) ──

    [Fact]
    public void Evaluate_RoutesNaNInnerScore_ToInfeasibleScore()
    {
        // Companion test to PositiveInfinity routing. The audit-hardening
        // pass extended infeasibility from +∞-only to !IsFinite to catch
        // NaN, +∞, and -∞ uniformly. NaN inner score would otherwise have
        // passed silently because the (Violations.Count > 0) check is false
        // and the +∞ check missed NaN.
        var alwaysNaNScore = new InlineObjective(
            score: double.NaN,
            violations: Array.Empty<FeasibilityViolation>(),
            breakdown: (5.0, 5.0));
        var cost = new CostObjective(alwaysNaNScore, _ => 1.0);
        var result = cost.Evaluate(new[] { 0.5 });
        Assert.True(double.IsPositiveInfinity(result.Score),
            $"NaN inner score must route to InfeasibleScore (got {result.Score})");
    }

    [Fact]
    public void Evaluate_RoutesNegativeInfinityInnerScore_ToInfeasibleScore()
    {
        // -∞ also fails !IsFinite. Symmetric coverage with +∞ + NaN.
        var alwaysNegInfScore = new InlineObjective(
            score: double.NegativeInfinity,
            violations: Array.Empty<FeasibilityViolation>(),
            breakdown: (5.0, 5.0));
        var cost = new CostObjective(alwaysNegInfScore, _ => 1.0);
        var result = cost.Evaluate(new[] { 0.5 });
        Assert.True(double.IsPositiveInfinity(result.Score),
            $"-∞ inner score must route to InfeasibleScore (got {result.Score})");
    }

    [Fact]
    public void WithBudgetCeiling_RoutesNaNCost_ToInfeasibleScore()
    {
        // The BudgetGatedObjective's `cost > _budget` check would be false
        // for NaN cost (NaN-vs-anything compares false), letting a NaN-
        // costed design silently pass the budget gate. The audit fix
        // extends the gate to reject !IsFinite(cost) outright.
        var inner = new MockObjective(_ => (10.0, 5.0, true));
        var gated = CostObjective.WithBudgetCeiling(
            inner, _ => double.NaN, budget_USD: 100.0);
        var result = gated.Evaluate(new[] { 0.5 });
        Assert.True(double.IsPositiveInfinity(result.Score),
            $"NaN cost must route to InfeasibleScore not silently pass gate "
          + $"(got {result.Score})");
    }

}

/// <summary>
/// Returns the supplied score / violations / breakdown verbatim. Used
/// across optimization tests to construct edge cases the MockObjective
/// shape can't express (e.g. +∞ score with empty violations).
/// </summary>
internal sealed class InlineObjective : IObjective
{
    private readonly double _score;
    private readonly IReadOnlyList<FeasibilityViolation> _violations;
    private readonly object? _breakdown;
    private readonly DesignVariableInfo[] _vars = { new("x", 0.0, 1.0) };

    public InlineObjective(double score, IReadOnlyList<FeasibilityViolation> violations, object? breakdown)
    {
        _score = score;
        _violations = violations;
        _breakdown = breakdown;
    }

    public int DimensionCount => _vars.Length;
    public IReadOnlyList<DesignVariableInfo> Variables => _vars;

    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default) =>
        new(_score, _violations, _breakdown);
}
