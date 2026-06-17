// CookbookRecipeTests.cs — Sprint J — compile-tested cookbook recipes.
//
// Companion to Voxelforge/docs/optimizer-cookbook.md. Each recipe in
// the cookbook gets a corresponding test here that exercises the
// composition against the PUBLIC IObjective surface. Two purposes:
//
//   1. **Compile-test the cookbook.** If a future PublicAPI change
//      breaks one of the cookbook composition patterns, this test
//      project fails. The cookbook stays valid as APIs evolve.
//
//   2. **Sample-run each recipe.** Verify each composition produces
//      a deterministic + finite result on a synthetic inner objective.
//      Doesn't validate pillar-physics — the recipes use synthetic
//      inner-objective implementations because pillar-internal cost
//      factories (RocketEngineCostFactory, ElectricPropulsionCostFactory,
//      etc.) are NOT part of the public surface.
//
// The synthetic inner is `CookbookInner` — returns a fixed score + a
// fixed breakdown record `CookbookBreakdown` that mimics the shape of
// a real pillar's EngineSpecificBreakdown. The breakdown carries
// Thrust_N + Cost_USD + Mass_kg + Co2_kgCO2eq fields so all of
// CostObjective's factories + ParetoObjectiveBuilder's extractors
// have something to read.
//
// Recipes covered:
//   • Recipe 1 — Single-objective $/N (CostObjective.PerOutputUnit)
//   • Recipe 5 — Budget-constrained (CostObjective.WithBudgetCeiling)
//   • Recipe 6 — Noise-robust (SubsamplingObjective)
//   • Recipe 7 — Gradient probe post-SA (GradientProbe)
//   • Recipe 8a — Maximize (MaximizeAdapter)
//   • Recipe 8b — Composite total-system cost (CompositeCostObjective)
//   • Stack-composition — Bounded + Cached + Tee composed correctly
//
// Recipes 2/3/4/8 (NSGA-II/III Pareto sweeps) need an actual optimizer
// run — kept in the cookbook narrative but not compile-tested here
// (the NSGA loops would dominate the unit-test runtime budget).

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Cookbook;

/// <summary>
/// Synthetic breakdown record mimicking the shape of a real pillar's
/// EngineSpecificBreakdown. Carries the four metrics every cookbook
/// recipe touches: thrust output + cost + mass + embodied carbon.
/// </summary>
public sealed record CookbookBreakdown(
    double Thrust_N,
    double Cost_USD,
    double Mass_kg,
    double Co2_kgCO2eq);

/// <summary>
/// Synthetic inner objective producing a fixed (Score, Breakdown)
/// pair regardless of input vector. Suitable as a stand-in for any
/// pillar-specific objective in cookbook recipe compilation tests.
/// </summary>
public sealed class CookbookInner : IObjective
{
    private readonly CookbookBreakdown _breakdown;
    private readonly double _score;
    private readonly bool _feasible;
    public int CallCount { get; private set; }

    public CookbookInner(
        double score = -3300.0,            // negative Isp by default
        CookbookBreakdown? breakdown = null,
        bool feasible = true,
        int dim = 2)
    {
        _score = score;
        _breakdown = breakdown ?? new CookbookBreakdown(
            Thrust_N:    0.270,
            Cost_USD:    500_000.0,
            Mass_kg:     45.0,
            Co2_kgCO2eq: 1_200.0);
        _feasible = feasible;
        _vars = new DesignVariableInfo[dim];
        for (int i = 0; i < dim; i++)
            _vars[i] = new DesignVariableInfo($"x{i}", 0.0, 1.0);
    }

    private readonly DesignVariableInfo[] _vars;
    public int DimensionCount => _vars.Length;
    public IReadOnlyList<DesignVariableInfo> Variables => _vars;

    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        CallCount++;
        var violations = _feasible
            ? (IReadOnlyList<FeasibilityViolation>)Array.Empty<FeasibilityViolation>()
            : new[] { new FeasibilityViolation("SYNTHETIC_FAIL", "stub", ActualValue: 0, Limit: 1) };
        return new EvaluationResult(
            Score:                   _feasible ? _score : double.PositiveInfinity,
            Violations:              violations,
            EngineSpecificBreakdown: _breakdown);
    }
}

public sealed class CookbookRecipeTests
{
    // ── Recipe 1 — Single-objective $/N minimization ──────────────────

    [Fact]
    public void Recipe1_CostPerNewton_ReturnsExpectedRatio()
    {
        var inner = new CookbookInner();
        var costPerN = CostObjective.PerOutputUnit(
            inner:    inner,
            costFn:   b => ((CookbookBreakdown)b!).Cost_USD,
            outputFn: b => ((CookbookBreakdown)b!).Thrust_N);

        var r = costPerN.Evaluate(new[] { 0.5, 0.5 });
        // 500_000 / 0.270 ≈ 1 851 851 $/N.
        Assert.Equal(500_000.0 / 0.270, r.Score, precision: 6);
    }

    // ── Recipe 5 — Budget-constrained ─────────────────────────────────

    [Fact]
    public void Recipe5_BudgetCeiling_PreservesScoreWhenUnderBudget()
    {
        // Inner cost = $500k, budget = $1M → under budget, score preserved.
        var inner = new CookbookInner(score: -3300.0);
        var constrained = CostObjective.WithBudgetCeiling(
            inner:      inner,
            costFn:     b => ((CookbookBreakdown)b!).Cost_USD,
            budget_USD: 1_000_000.0);

        var r = constrained.Evaluate(new[] { 0.5, 0.5 });
        Assert.Equal(-3300.0, r.Score, precision: 6);   // inner -Isp preserved
    }

    [Fact]
    public void Recipe5_BudgetCeiling_RoutesToInfeasibleWhenOver()
    {
        var inner = new CookbookInner();   // cost = $500k
        var constrained = CostObjective.WithBudgetCeiling(
            inner:      inner,
            costFn:     b => ((CookbookBreakdown)b!).Cost_USD,
            budget_USD: 100_000.0);                       // tight budget

        var r = constrained.Evaluate(new[] { 0.5, 0.5 });
        Assert.True(double.IsPositiveInfinity(r.Score));
    }

    // ── Recipe 6 — Noise-robust optimization ──────────────────────────

    [Fact]
    public void Recipe6_NoiseRobust_MedianAcrossNeighbours()
    {
        var inner = new CookbookInner();
        var robust = new SubsamplingObjective(inner, neighbourCount: 2);

        robust.Evaluate(new[] { 0.5, 0.5 });
        // 1 + 2 · 2 = 5 inner calls per outer call.
        Assert.Equal(5, inner.CallCount);
    }

    // ── Recipe 7 — Gradient probe ─────────────────────────────────────

    [Fact]
    public void Recipe7_GradientProbe_ReturnsGradientVector()
    {
        var inner = new CookbookInner();
        var probe = new GradientProbe(inner);
        var grad = probe.ComputeGradient(new[] { 0.5, 0.5 });
        Assert.Equal(inner.DimensionCount, grad.Length);
        // The synthetic inner returns a fixed score regardless of vector,
        // so the gradient should be zero. Confirms the wrapper compiles
        // + dispatches correctly.
        foreach (var g in grad)
            Assert.Equal(0.0, g, precision: 6);
    }

    // ── Recipe 8a — MaximizeAdapter ───────────────────────────────────

    [Fact]
    public void Recipe8a_Maximize_NegatesInnerScore()
    {
        // Inner returns +100 (positive thrust) — maximizing → wrapper
        // emits -100 so the minimization optimizer maxes it.
        var inner = new CookbookInner(score: 100.0);
        var max = new MaximizeAdapter(inner);
        var r = max.Evaluate(new[] { 0.5, 0.5 });
        Assert.Equal(-100.0, r.Score, precision: 6);
    }

    // ── Recipe 8b — Composite total-system cost ───────────────────────

    [Fact]
    public void Recipe8b_Composite_SumsMultipleCostExtractors()
    {
        // Synthetic NEP-system cost = engine + tank + radiator + PPU.
        // Use the same Cost_USD field for all 4 (simulating ~$125k per
        // component → $500k total when summed).
        var inner = new CookbookInner();
        var totalCost = new CompositeCostObjective(inner, new Func<object?, double>[]
        {
            b => ((CookbookBreakdown)b!).Cost_USD * 0.40,   // engine fraction
            b => ((CookbookBreakdown)b!).Cost_USD * 0.20,   // tank fraction
            b => ((CookbookBreakdown)b!).Cost_USD * 0.25,   // radiator fraction
            b => ((CookbookBreakdown)b!).Cost_USD * 0.15,   // PPU fraction
        });

        var r = totalCost.Evaluate(new[] { 0.5, 0.5 });
        Assert.Equal(500_000.0, r.Score, precision: 6);     // sums to full cost
        Assert.Equal(4, totalCost.ExtractorCount);
    }

    // ── Stack composition — Bounded + Cached + Tee ────────────────────

    [Fact]
    public void StackComposition_BoundedCachedTeeAroundCostObjective()
    {
        var inner = new CookbookInner();
        var bounded = new BoundedObjective(inner);
        var cached = new CachedObjective(bounded);
        var costObj = CostObjective.PerOutputUnit(
            inner:    cached,
            costFn:   b => ((CookbookBreakdown)b!).Cost_USD,
            outputFn: b => ((CookbookBreakdown)b!).Thrust_N);
        var tee = new TeeObjective(costObj);

        // Out-of-bounds vector → clamped by BoundedObjective before
        // the cache/cost see it. Two evals at same OOB vector → cache
        // hit on the second.
        var oob = new[] { 1.5, 1.5 };
        tee.Evaluate(oob);
        tee.Evaluate(oob);

        Assert.Equal(2, tee.Log.Count);                  // tee captures both
        Assert.Equal(1, inner.CallCount);                // inner only called once
        Assert.Equal(1, cached.HitCount);
        Assert.Equal(1, cached.MissCount);
    }

    // ── Pareto extractor cross-recipe pin ─────────────────────────────

    [Fact]
    public void ParetoExtractor_PhysicsAndCost_ProducesTwoTuple()
    {
        // Recipe 2 NSGA-II sweep uses ParetoObjectiveBuilder.PhysicsAndCost
        // to produce a (-Isp, $) extractor. Pin that the extractor
        // compiles + returns the expected tuple on a synthetic result.
        var inner = new CookbookInner();
        var extractor = ParetoObjectiveBuilder.PhysicsAndCost(
            physicsScoreFn: r => r.Score,                                       // pass through inner -Isp
            costFn:         b => ((CookbookBreakdown)b!).Cost_USD);

        var r = inner.Evaluate(new[] { 0.5, 0.5 });
        var tuple = extractor(r);
        Assert.Equal(2, tuple.Length);
        Assert.Equal(-3300.0,    tuple[0], precision: 6);   // -Isp
        Assert.Equal(500_000.0,  tuple[1], precision: 6);   // cost
    }

    [Fact]
    public void ParetoExtractor_PhysicsCostAndCo2_ProducesThreeTuple()
    {
        // Recipe 3 NSGA-III sustainability sweep uses
        // PhysicsCostAndCo2 to produce a (-Isp, $, kg-CO2) extractor.
        var inner = new CookbookInner();
        var extractor = ParetoObjectiveBuilder.PhysicsCostAndCo2(
            physicsScoreFn: r => r.Score,
            costFn:         b => ((CookbookBreakdown)b!).Cost_USD,
            co2Fn:          b => ((CookbookBreakdown)b!).Co2_kgCO2eq);

        var r = inner.Evaluate(new[] { 0.5, 0.5 });
        var tuple = extractor(r);
        Assert.Equal(3, tuple.Length);
        Assert.Equal( -3300.0, tuple[0], precision: 6);
        Assert.Equal(500_000.0, tuple[1], precision: 6);
        Assert.Equal(  1_200.0, tuple[2], precision: 6);
    }

    [Fact]
    public void ParetoExtractor_PhysicsAndMass_ProducesTwoTupleForMassBudget()
    {
        // Recipe 8 mass-budget Pareto sweep.
        var inner = new CookbookInner();
        var extractor = ParetoObjectiveBuilder.PhysicsAndMass(
            physicsScoreFn: r => r.Score,
            massFn:         b => ((CookbookBreakdown)b!).Mass_kg);

        var r = inner.Evaluate(new[] { 0.5, 0.5 });
        var tuple = extractor(r);
        Assert.Equal(-3300.0, tuple[0], precision: 6);
        Assert.Equal(   45.0, tuple[1], precision: 6);
    }

    // ── Wrapper invariants ────────────────────────────────────────────

    [Fact]
    public void EveryWrapper_PreservesDimensionCount()
    {
        var inner = new CookbookInner(dim: 3);

        Assert.Equal(3, new BoundedObjective(inner).DimensionCount);
        Assert.Equal(3, new CachedObjective(inner).DimensionCount);
        Assert.Equal(3, new TeeObjective(inner).DimensionCount);
        Assert.Equal(3, new MaximizeAdapter(inner).DimensionCount);
        Assert.Equal(3, new SubsamplingObjective(inner).DimensionCount);
        Assert.Equal(3, new NormalizingObjective(inner).DimensionCount);
        Assert.Equal(3, new CompositeCostObjective(inner,
            new Func<object?, double>[] { _ => 1.0 }).DimensionCount);
        Assert.Equal(3, CostObjective.ByMass(inner, _ => 0.0).DimensionCount);
        Assert.Equal(3, CostObjective.ByLcoe(inner, _ => 0.0).DimensionCount);
    }
}
