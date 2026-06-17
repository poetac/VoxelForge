// CostObjective.cs — Sprint EC.W11 (post-PR #489 follow-on).
//
// Adapter IObjective that scores candidates by cost (or any derived
// economic metric) instead of the inner objective's physics-driven
// scalar. Closes the loop opened by Sprints EC.W1 – EC.W10: every
// pillar now has cost / mass / CO₂ factories but no wire from those
// factories back into the SA / CMA-ES / NSGA-II portfolio.
//
// Design intent:
//   • Stay engine-family-agnostic. The cost-extraction function is
//     caller-supplied as a Func<object?, double> over the inner
//     objective's EngineSpecificBreakdown, so each pillar can wire its
//     own Economics namespace (which lives internal to that pillar's
//     own Core assembly + InternalsVisibleTo its consumers) without
//     leaking internal types through the public IObjective surface.
//   • Honour the inner feasibility contract. When the wrapped objective
//     emits a non-empty Violations list, the CostObjective scores the
//     candidate at the configurable infeasible sentinel (+∞ by default)
//     so the optimizer routes away from infeasible designs regardless
//     of cost. Categorical: a $1 infeasible design must not beat a $100
//     feasible one.
//   • Stay deterministic + thread-safe. The cost function must be pure
//     and re-entrant; the wrapper itself holds no mutable state past
//     construction.
//
// Two cost-flavours covered by static factories:
//   1. Raw cost ($, kg-CO₂, kg-mass): pass the metric directly.
//   2. Cost-per-output ($/N for propulsion, $/kW for power-gen, $/kWh
//      LCOE for energy systems): pass a cost + a denominator-extractor.
//
// Usage (rocket regen chamber, minimise $/N):
//   var inner = RegenChamberObjective.Build(conditions, baseline);
//   var costObj = CostObjective.PerOutputUnit(
//       inner:        inner,
//       costFn:       breakdown => RocketEngineCostFactory.For((RegenScoreResult)breakdown).CapitalCost_USD,
//       outputFn:     breakdown => ((RegenScoreResult)breakdown).Thrust_N);
//   var sa = new MultiChainOptimizer(costObj, ...);
//
// The pillar-side cost factories that PR #489 EC.W6 – EC.W10 shipped
// (RocketEngineCostFactory, AirbreathingCostFactory, ElectricPropulsionCostFactory,
// MarineCostFactory, NuclearCostFactory) are the canonical callers of
// the costFn delegate — each lives inside its pillar's Economics
// namespace and stays internal to the pillar.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Voxelforge.Optimization;

/// <summary>
/// <see cref="IObjective"/> adapter that scores by a caller-supplied
/// cost extractor instead of the inner objective's physics score.
/// Honours the inner feasibility contract: infeasible candidates score
/// at <see cref="InfeasibleScore"/> regardless of cost.
/// </summary>
/// <remarks>
/// <para>
/// The wrapper is engine-family-agnostic. The cost function consumes
/// the inner objective's <see cref="EvaluationResult.EngineSpecificBreakdown"/>
/// as <c>object?</c>, so per-pillar Economics namespaces (which stay
/// internal to their pillar) can be wired without leaking internal
/// types through the public IObjective surface.
/// </para>
/// <para>
/// Thread-safety: the wrapper holds the inner objective + the cost
/// function as immutable references after construction; thread-safety
/// then reduces to the thread-safety of the inner objective and the
/// supplied delegate. Determinism: pure cost functions produce
/// bit-identical scores across runs, preserving the SA
/// strict-determinism contract.
/// </para>
/// </remarks>
public sealed class CostObjective : IObjective
{
    private readonly IObjective _inner;
    private readonly Func<object?, double> _costFn;

    /// <summary>
    /// Score returned when the inner objective emits at least one
    /// <see cref="FeasibilityViolation"/>. Defaults to
    /// <see cref="double.PositiveInfinity"/> — the canonical SA
    /// "never-accept" sentinel.
    /// </summary>
    public double InfeasibleScore { get; }

    /// <inheritdoc />
    public int DimensionCount => _inner.DimensionCount;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

    /// <summary>
    /// Build a cost objective that scores candidates by the raw scalar
    /// returned from <paramref name="costFn"/> over the inner objective's
    /// engine-specific breakdown.
    /// </summary>
    /// <param name="inner">
    /// Inner physics objective. Drives the design-vector → physics-result
    /// path; this wrapper only re-scores the result.
    /// </param>
    /// <param name="costFn">
    /// Pure, thread-safe function that extracts a scalar cost (or any
    /// economic metric — mass, CO₂, $) from the inner objective's
    /// <see cref="EvaluationResult.EngineSpecificBreakdown"/>. Must
    /// return a finite non-negative value for feasible designs; infinite
    /// or NaN scores are surfaced verbatim to the optimizer.
    /// </param>
    /// <param name="infeasibleScore">
    /// Optional override for the infeasible-candidate score. Defaults
    /// to <see cref="double.PositiveInfinity"/>.
    /// </param>
    public CostObjective(
        IObjective inner,
        Func<object?, double> costFn,
        double infeasibleScore = double.PositiveInfinity)
    {
        _inner          = inner          ?? throw new ArgumentNullException(nameof(inner));
        _costFn         = costFn         ?? throw new ArgumentNullException(nameof(costFn));
        InfeasibleScore = infeasibleScore;
    }

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        var innerResult = _inner.Evaluate(vector, ct);

        // Honour inner feasibility — infeasible designs lose regardless
        // of cost. A $1 design that violates a structural margin must
        // never beat a $100 design that passes every gate. !IsFinite
        // catches +∞, −∞, and NaN uniformly so −∞ can't sneak through
        // as "infinitely cheap" (an SA-acceptable score would win).
        if (innerResult.Violations.Count > 0 || !double.IsFinite(innerResult.Score))
        {
            return innerResult with { Score = InfeasibleScore };
        }

        double cost = _costFn(innerResult.EngineSpecificBreakdown);
        return innerResult with { Score = cost };
    }

    /// <summary>
    /// Build a cost-per-output-unit objective: cost / output. Useful for
    /// <c>$/N</c> (propulsion), <c>$/kW</c> (power generation), and the
    /// kgCO₂/kg-payload sustainability metric.
    /// </summary>
    /// <param name="inner">Inner physics objective.</param>
    /// <param name="costFn">
    /// Extracts the cost numerator from the engine-specific breakdown.
    /// </param>
    /// <param name="outputFn">
    /// Extracts the output denominator (thrust, electrical power, mass,
    /// etc.). Designs that produce zero or negative output are scored
    /// at <paramref name="infeasibleScore"/> — division by zero is not a
    /// valid physical operating point even if every gate passes.
    /// </param>
    /// <param name="infeasibleScore">
    /// Optional override for the infeasible-candidate score.
    /// </param>
    public static CostObjective PerOutputUnit(
        IObjective inner,
        Func<object?, double> costFn,
        Func<object?, double> outputFn,
        double infeasibleScore = double.PositiveInfinity)
    {
        if (outputFn is null) throw new ArgumentNullException(nameof(outputFn));

        double Combined(object? breakdown)
        {
            double output = outputFn(breakdown);
            if (!(output > 0)) return infeasibleScore;
            return costFn(breakdown) / output;
        }

        return new CostObjective(inner, Combined, infeasibleScore);
    }

    /// <summary>
    /// Build an embodied-CO₂ objective: scores by the cradle-to-gate
    /// kg CO₂-eq carried on the engine-specific breakdown. Sister to
    /// the cost / mass variants; the only difference is which metric
    /// the extractor reads from the per-pillar cost factory output.
    /// </summary>
    /// <param name="inner">Inner physics objective.</param>
    /// <param name="co2Fn">
    /// Extracts the embodied CO₂ scalar from the engine-specific
    /// breakdown. Typical implementation reads
    /// <c>CostEstimate.EmbodiedCO2_kgCO2eq</c> from the pillar's
    /// <c>Economics</c> namespace factory.
    /// </param>
    /// <param name="infeasibleScore">
    /// Optional override for the infeasible-candidate score.
    /// </param>
    public static CostObjective ByEmbodiedCO2(
        IObjective inner,
        Func<object?, double> co2Fn,
        double infeasibleScore = double.PositiveInfinity)
        => new(inner, co2Fn, infeasibleScore);

    /// <summary>
    /// Build a CO₂-per-output-unit objective: kg CO₂-eq / output.
    /// Useful for sustainability-per-Newton (propulsion) or
    /// kg CO₂-eq/kWh (power generation) single-objective sweeps.
    /// Designs with non-positive output short-circuit to
    /// <paramref name="infeasibleScore"/>.
    /// </summary>
    /// <param name="inner">Inner physics objective.</param>
    /// <param name="co2Fn">Embodied-CO₂ scalar extractor.</param>
    /// <param name="outputFn">
    /// Output denominator (thrust, electrical power). Zero / negative
    /// short-circuits.
    /// </param>
    /// <param name="infeasibleScore">
    /// Optional override for the infeasible-candidate score.
    /// </param>
    public static CostObjective Co2PerOutputUnit(
        IObjective inner,
        Func<object?, double> co2Fn,
        Func<object?, double> outputFn,
        double infeasibleScore = double.PositiveInfinity)
    {
        if (outputFn is null) throw new ArgumentNullException(nameof(outputFn));

        double Combined(object? breakdown)
        {
            double output = outputFn(breakdown);
            if (!(output > 0)) return infeasibleScore;
            return co2Fn(breakdown) / output;
        }
        return new CostObjective(inner, Combined, infeasibleScore);
    }

    /// <summary>
    /// Build an LCOE (levelized cost of energy) objective: scores by
    /// the caller-supplied LCOE scalar [USD/kWh]. Useful for power-
    /// generation single-objective sweeps where the underlying solver
    /// + cost factory has already computed an LCOE on the breakdown.
    /// </summary>
    /// <param name="inner">Inner physics objective.</param>
    /// <param name="lcoeFn">
    /// LCOE scalar extractor [USD/kWh]. Typical implementation reads
    /// from <c>LcoeCalculator.Compute(...)</c> in the pillar's
    /// <c>Economics</c> namespace.
    /// </param>
    /// <param name="infeasibleScore">
    /// Optional override for the infeasible-candidate score.
    /// </param>
    public static CostObjective ByLcoe(
        IObjective inner,
        Func<object?, double> lcoeFn,
        double infeasibleScore = double.PositiveInfinity)
        => new(inner, lcoeFn, infeasibleScore);

    /// <summary>
    /// Build a mass-minimization objective: scores by the dry mass
    /// carried on the engine-specific breakdown. Useful for vehicle-
    /// integration weight budgets (spacecraft + EV + aircraft).
    /// </summary>
    /// <param name="inner">Inner physics objective.</param>
    /// <param name="massFn">
    /// Extracts the dry-mass scalar [kg] from the engine-specific
    /// breakdown. Typical implementation reads
    /// <c>CostEstimate.Mass_kg</c> from the pillar's <c>Economics</c>
    /// namespace factory.
    /// </param>
    /// <param name="infeasibleScore">
    /// Optional override for the infeasible-candidate score.
    /// </param>
    public static CostObjective ByMass(
        IObjective inner,
        Func<object?, double> massFn,
        double infeasibleScore = double.PositiveInfinity)
        => new(inner, massFn, infeasibleScore);

    /// <summary>
    /// Build a budget-ceiling constrained objective: minimise the
    /// inner physics score subject to <c>cost ≤ budget</c>. Designs
    /// that exceed the budget are scored at
    /// <paramref name="infeasibleScore"/>; budget-feasible designs
    /// pass through the inner objective's score unchanged. Returns
    /// the wrapper as <see cref="IObjective"/> — the semantics differ
    /// from the primary <see cref="CostObjective"/> constructor (which
    /// replaces the inner score with cost), so the budget variant
    /// lives as a sibling class on the <see cref="IObjective"/> surface
    /// rather than as another <see cref="CostObjective"/>.
    /// </summary>
    /// <param name="inner">Inner physics objective whose score is preserved
    /// for budget-feasible candidates.</param>
    /// <param name="costFn">
    /// Cost extractor; same shape as the primary constructor.
    /// </param>
    /// <param name="budget_USD">
    /// Cost ceiling [USD]. Designs costing more than this are scored
    /// infeasible regardless of physics performance.
    /// </param>
    /// <param name="infeasibleScore">
    /// Optional override for the infeasible-candidate score.
    /// </param>
    public static IObjective WithBudgetCeiling(
        IObjective inner,
        Func<object?, double> costFn,
        double budget_USD,
        double infeasibleScore = double.PositiveInfinity)
    {
        if (inner  is null) throw new ArgumentNullException(nameof(inner));
        if (costFn is null) throw new ArgumentNullException(nameof(costFn));
        if (!(budget_USD > 0))
            throw new ArgumentOutOfRangeException(nameof(budget_USD),
                $"Budget must be positive; got {budget_USD}.");

        return new BudgetGatedObjective(inner, costFn, budget_USD, infeasibleScore);
    }

    /// <summary>
    /// Internal IObjective wrapper for the budget-ceiling variant.
    /// Composition (not inheritance) over <see cref="CostObjective"/>
    /// because the semantics differ — primary CostObjective replaces
    /// the inner score with cost; budget-ceiling preserves the inner
    /// score when cost ≤ budget and routes to infeasible otherwise.
    /// </summary>
    private sealed class BudgetGatedObjective : IObjective
    {
        private readonly IObjective _inner;
        private readonly Func<object?, double> _costFn;
        private readonly double _budget;
        private readonly double _infeasibleScore;

        public BudgetGatedObjective(
            IObjective inner,
            Func<object?, double> costFn,
            double budget,
            double infeasibleScore)
        {
            _inner           = inner;
            _costFn          = costFn;
            _budget          = budget;
            _infeasibleScore = infeasibleScore;
        }

        public int DimensionCount => _inner.DimensionCount;
        public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            var innerResult = _inner.Evaluate(vector, ct);
            // Audit-hardened (2026-05-13): also catch NaN inner scores.
            if (innerResult.Violations.Count > 0
                || double.IsPositiveInfinity(innerResult.Score)
                || double.IsNaN(innerResult.Score))
            {
                return innerResult with { Score = _infeasibleScore };
            }

            double cost = _costFn(innerResult.EngineSpecificBreakdown);
            // Audit-hardened (2026-05-13): cost-extractor returning NaN
            // would make `cost > _budget` evaluate to false (NaN
            // comparisons always false), letting an unevaluable cost
            // pass through. Route to infeasible explicitly.
            if (double.IsNaN(cost) || cost > _budget)
            {
                return innerResult with { Score = _infeasibleScore };
            }
            return innerResult;  // preserve inner score when budget-feasible
        }
    }
}
