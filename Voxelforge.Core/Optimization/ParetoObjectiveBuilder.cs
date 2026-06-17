// ParetoObjectiveBuilder.cs — Sprint EC.W12 / ADR-030 follow-on.
//
// Static helper for building the Func<EvaluationResult, double[]>
// multi-objective extractor that NsgaIIOptimizer + NsgaIIIOptimizer
// consume. Closes the gap left by CostObjective (ADR-030, single-
// objective) toward genuine Pareto-front exploration in the cost ↔
// performance plane.
//
// NSGA-II / NSGA-III already accept an arbitrary objective-extractor;
// what was missing was a vetted shape for the common multi-objective
// patterns. Three patterns ship here:
//
//   1. PhysicsAndCost($)         → 2-vector for $/N or $/kW trade studies
//   2. PhysicsAndMass(kg)        → 2-vector for mass-budget trade studies
//   3. PhysicsCostAndCo2($, kg)  → 3-vector for sustainability +
//                                  performance + cost simultaneously
//
// All three extractors honour the inner IObjective's feasibility
// contract: infeasible candidates (Violations non-empty or Score = +∞)
// map to a fully-+∞ vector, which NSGA's constraint-handling extension
// (Deb 2002 §V — dominate any feasible solution) routes away from.
//
// Determinism: every extractor is a pure function over the
// EvaluationResult. Same input → same output. NSGA-II's strict-
// determinism contract is preserved iff the extractor is pure.
//
// Sign convention: every objective is MINIMISED. For "maximise Isp"
// pass `-result.IspVacuum_s`; for "minimise cost" pass the cost
// directly. The NSGA optimizers operate on the unsigned-minimisation
// shape; the sign-flip is the caller's responsibility (consistent with
// how single-objective wrappers like ResistojetObjective already work).

using System;

namespace Voxelforge.Optimization;

/// <summary>
/// Static helpers that build multi-objective extractor delegates for
/// <see cref="NsgaIIOptimizer"/> + <see cref="NsgaIIIOptimizer"/>.
/// Sister to <see cref="CostObjective"/> (single-objective scalar
/// cost) — this class returns the <c>Func&lt;EvaluationResult,
/// double[]&gt;</c> delegate NSGA consumes directly.
/// </summary>
/// <remarks>
/// Three preset shapes cover the common Pareto sweeps:
/// <list type="bullet">
/// <item>2-D (physics, cost) for $/N or $/kW trade studies.</item>
/// <item>2-D (physics, mass) for spacecraft weight-budget studies.</item>
/// <item>3-D (physics, cost, CO₂) for sustainability triple-objective.</item>
/// </list>
/// For unusual combinations (4+ objectives, custom metric mixes), build
/// the <c>Func&lt;EvaluationResult, double[]&gt;</c> inline at the call
/// site — this helper covers the curated common cases.
/// </remarks>
public static class ParetoObjectiveBuilder
{
    /// <summary>
    /// Build a 2-objective extractor (physics-score, cost). Both
    /// objectives are MINIMISED — pass negative physics score from
    /// inside <paramref name="physicsScoreFn"/> when "maximise Isp"
    /// semantics are wanted.
    /// </summary>
    /// <param name="physicsScoreFn">
    /// Extracts the physics score from the inner objective's
    /// <see cref="EvaluationResult"/>. The simplest implementation
    /// is <c>r =&gt; r.Score</c> (which is what the inner objective
    /// already minimises); pass a custom function to read a different
    /// metric from <c>EngineSpecificBreakdown</c>.
    /// </param>
    /// <param name="costFn">
    /// Extracts the cost scalar from the engine-specific breakdown.
    /// Typical implementation reads
    /// <c>CostEstimate.CapitalCost_USD</c> from the pillar's
    /// <c>Economics</c> namespace factory.
    /// </param>
    /// <returns>
    /// A <c>Func&lt;EvaluationResult, double[]&gt;</c> ready to pass to
    /// <see cref="NsgaIIOptimizer"/> / <see cref="NsgaIIIOptimizer"/>.
    /// Infeasible candidates (Violations non-empty or Score = +∞) map
    /// to <c>{ +∞, +∞ }</c>.
    /// </returns>
    public static Func<EvaluationResult, double[]> PhysicsAndCost(
        Func<EvaluationResult, double> physicsScoreFn,
        Func<object?, double> costFn)
    {
        if (physicsScoreFn is null) throw new ArgumentNullException(nameof(physicsScoreFn));
        if (costFn         is null) throw new ArgumentNullException(nameof(costFn));

        return r =>
        {
            if (r.Violations.Count > 0 || !double.IsFinite(r.Score))
                return InfeasibleTuple(2);
            return ValidateOrInfeasible(new[]
            {
                physicsScoreFn(r),
                costFn(r.EngineSpecificBreakdown),
            });
        };
    }

    /// <summary>
    /// Build a 2-objective extractor (physics-score, mass). Useful for
    /// vehicle-integration weight-budget studies — spacecraft, EV,
    /// aircraft. Both objectives are MINIMISED.
    /// </summary>
    /// <param name="physicsScoreFn">Inner physics score extractor.</param>
    /// <param name="massFn">
    /// Extracts the dry-mass scalar [kg] from the engine-specific
    /// breakdown. Typical implementation reads
    /// <c>CostEstimate.Mass_kg</c> from the pillar's <c>Economics</c>
    /// namespace factory.
    /// </param>
    public static Func<EvaluationResult, double[]> PhysicsAndMass(
        Func<EvaluationResult, double> physicsScoreFn,
        Func<object?, double> massFn)
    {
        if (physicsScoreFn is null) throw new ArgumentNullException(nameof(physicsScoreFn));
        if (massFn         is null) throw new ArgumentNullException(nameof(massFn));

        return r =>
        {
            if (r.Violations.Count > 0 || !double.IsFinite(r.Score))
                return InfeasibleTuple(2);
            return ValidateOrInfeasible(new[]
            {
                physicsScoreFn(r),
                massFn(r.EngineSpecificBreakdown),
            });
        };
    }

    /// <summary>
    /// Build a 3-objective extractor (physics-score, cost, CO₂) for
    /// sustainability + cost + performance trade-studies. All three
    /// objectives are MINIMISED.
    /// </summary>
    /// <param name="physicsScoreFn">Inner physics score extractor.</param>
    /// <param name="costFn">Cost scalar extractor [USD].</param>
    /// <param name="co2Fn">Embodied-CO₂ scalar extractor [kg CO₂-eq].</param>
    public static Func<EvaluationResult, double[]> PhysicsCostAndCo2(
        Func<EvaluationResult, double> physicsScoreFn,
        Func<object?, double> costFn,
        Func<object?, double> co2Fn)
    {
        if (physicsScoreFn is null) throw new ArgumentNullException(nameof(physicsScoreFn));
        if (costFn         is null) throw new ArgumentNullException(nameof(costFn));
        if (co2Fn          is null) throw new ArgumentNullException(nameof(co2Fn));

        return r =>
        {
            if (r.Violations.Count > 0 || !double.IsFinite(r.Score))
                return InfeasibleTuple(3);
            return ValidateOrInfeasible(new[]
            {
                physicsScoreFn(r),
                costFn(r.EngineSpecificBreakdown),
                co2Fn(r.EngineSpecificBreakdown),
            });
        };
    }

    /// <summary>
    /// Build a 2-objective extractor (physics-score, LCOE) for power-
    /// generation Pareto sweeps. LCOE [USD/kWh] is the levelized cost
    /// of energy — capital recovery factor × CapEx / (lifetime energy
    /// production). Both objectives MINIMISED.
    /// </summary>
    /// <param name="physicsScoreFn">Inner physics score extractor.</param>
    /// <param name="lcoeFn">
    /// LCOE scalar extractor [USD/kWh]. Typical implementation reads
    /// from a per-pillar LCOE calculator (e.g.
    /// <c>LcoeCalculator.Compute(...)</c>) that consumes the breakdown
    /// plus a capital-recovery factor + lifetime-energy estimate.
    /// </param>
    public static Func<EvaluationResult, double[]> PhysicsAndLcoe(
        Func<EvaluationResult, double> physicsScoreFn,
        Func<object?, double> lcoeFn)
    {
        if (physicsScoreFn is null) throw new ArgumentNullException(nameof(physicsScoreFn));
        if (lcoeFn         is null) throw new ArgumentNullException(nameof(lcoeFn));

        return r =>
        {
            if (r.Violations.Count > 0 || !double.IsFinite(r.Score))
                return InfeasibleTuple(2);
            return ValidateOrInfeasible(new[]
            {
                physicsScoreFn(r),
                lcoeFn(r.EngineSpecificBreakdown),
            });
        };
    }

    /// <summary>
    /// Build a 2-objective extractor (cost-per-output, CO₂-per-output)
    /// for cost ↔ sustainability trade-studies on the same output unit
    /// (e.g. $/N + kg CO₂-eq/N for propulsion, $/kW + kg CO₂-eq/kW for
    /// power-gen). Both objectives MINIMISED. Designs with non-positive
    /// output short-circuit to an all-+∞ tuple.
    /// </summary>
    /// <param name="costFn">Cost scalar extractor [USD].</param>
    /// <param name="co2Fn">Embodied-CO₂ scalar extractor [kg CO₂-eq].</param>
    /// <param name="outputFn">
    /// Output denominator (thrust, electrical power, etc.). Zero or
    /// negative outputs map to all-+∞.
    /// </param>
    public static Func<EvaluationResult, double[]> CostAndCo2PerOutputUnit(
        Func<object?, double> costFn,
        Func<object?, double> co2Fn,
        Func<object?, double> outputFn)
    {
        if (costFn   is null) throw new ArgumentNullException(nameof(costFn));
        if (co2Fn    is null) throw new ArgumentNullException(nameof(co2Fn));
        if (outputFn is null) throw new ArgumentNullException(nameof(outputFn));

        return r =>
        {
            if (r.Violations.Count > 0 || !double.IsFinite(r.Score))
                return InfeasibleTuple(2);
            double output = outputFn(r.EngineSpecificBreakdown);
            if (!(output > 0)) return InfeasibleTuple(2);
            return ValidateOrInfeasible(new[]
            {
                costFn(r.EngineSpecificBreakdown) / output,
                co2Fn(r.EngineSpecificBreakdown) / output,
            });
        };
    }

    private static double[] InfeasibleTuple(int dimension)
    {
        var infeasible = new double[dimension];
        for (int i = 0; i < dimension; i++)
            infeasible[i] = double.PositiveInfinity;
        return infeasible;
    }

    // Walk the extractor output and route to InfeasibleTuple if any slice
    // is non-finite (NaN, +∞, -∞). Cost / mass / LCOE extractors can
    // legitimately surface NaN if the breakdown record contains a
    // degenerate field (division by zero in $/N when thrust=0, NaN
    // propagating from an upstream physics failure). NaN in a Pareto
    // tuple silently corrupts NSGA-II / NSGA-III dominance because NaN
    // compares false against everything, so the candidate would land in
    // the front by accident. Companion to the 5-wrapper hardening pass
    // (commit 2c15ad7).
    private static double[] ValidateOrInfeasible(double[] tuple)
    {
        for (int i = 0; i < tuple.Length; i++)
        {
            if (!double.IsFinite(tuple[i]))
                return InfeasibleTuple(tuple.Length);
        }
        return tuple;
    }
}
