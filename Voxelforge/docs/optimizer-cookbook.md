# Optimizer cookbook — `IObjective` composition patterns

> Recipe-style walkthrough of the post-PR-#497 optimizer surface.
> Aimed at a contributor wiring a new SA / CMA-ES / NSGA-II / Bayesian
> run against a pillar adapter. Companion to [ADR-023 optimizer
> portfolio](ADR/ADR-023-optimizer-portfolio.md), [ADR-025 IEngine
> abstraction](ADR/ADR-025-iengine-engine-family-abstraction.md),
> [ADR-030 CostObjective Economics wire](ADR/ADR-030-cost-objective-economics-wire.md),
> and [ADR-032 IObjective composition pattern](ADR/ADR-032-iobjective-composition-pattern.md).

## Layer cake

The `IObjective` stack composes bottom-up:

```
  physics objective                                                  (innermost)
      ↓ wrapped by
  EngineObjectiveAdapter<TDesign, TConditions, TResult>              (typed pillar adapter)
      ↓ wrapped by (optional)
  BoundedObjective                                                   (defensive bounds clip)
      ↓ wrapped by (optional)
  CachedObjective                                                    (memoization)
      ↓ wrapped by (optional)
  CostObjective / variant                                            (single-objective re-score)
      ↓ wrapped by (optional)
  TeeObjective                                                       (logging tee)              (outermost)
```

Parallel — not in the stack — `ParetoObjectiveBuilder` returns a
`Func<EvaluationResult, double[]>` extractor consumed by NSGA-II /
NSGA-III alongside the wrapped `IObjective` (which still provides
DimensionCount + Variables + Evaluate).

`GradientProbe` + `SubsamplingObjective` are also available; the former
is a diagnostic helper (not in the stack), the latter is a noise-robust
wrapper that slots between the pillar adapter and `CostObjective`.

## Recipe 1 — single-objective `$/N` minimization on the rocket pillar

Minimize cost-per-Newton on a regen-cooled chamber.

```csharp
using Voxelforge.Engines;
using Voxelforge.Optimization;

// 1. Pillar adapter — physics objective.
var inner = new EngineObjectiveAdapter<RegenChamberDesign, OperatingConditions, RocketEngineResult>(
    engine:     RocketEngine.Instance,
    conditions: myConditions,
    baseline:   myBaselineDesign,
    variables:  RegenChamberObjective.DefaultBounds,
    unpack:     (vec, baseline) => RegenChamberOptimization.Unpack(vec, baseline),
    evaluate:   result => new EvaluationResult(
        Score:                   -result.Thrust_N,            // maximise thrust
        Violations:              result.Violations,
        EngineSpecificBreakdown: result));

// 2. Bound it defensively (NSGA / CMA mutations can overshoot).
var bounded = new BoundedObjective(inner);

// 3. Cache repeated vectors (multi-chain SA + restart workloads).
var cached = new CachedObjective(bounded);

// 4. Re-score by cost. Cost extractor reads from the pillar's
//    internal RocketEngineCostFactory (Economics namespace, internal
//    to Voxelforge.Core).
var costObj = CostObjective.PerOutputUnit(
    inner:    cached,
    costFn:   b => Voxelforge.Economics.RocketEngineCostFactory.For(
        (RegenScoreResult)b!).CapitalCost_USD,
    outputFn: b => ((RegenScoreResult)b!).Thrust_N);

// 5. (Optional) tee the full evaluation log for post-mortem.
//    NOTE: TeeObjective is explicitly non-deterministic by contract —
//    each TeeRecord carries a DateTime.UtcNow timestamp. The
//    EvaluationResult (Score/Violations/Breakdown) stays deterministic,
//    but the trace metadata varies between runs. Do NOT hash or
//    serialise-compare tee.Log across runs. Analyzer rule VFD012 catches
//    the same wall-clock pattern on any *other* IObjective implementation;
//    TeeObjective.Evaluate carries an explicit
//    [SuppressMessage("Voxelforge.Determinism", "VFD012", …)] to record
//    the intentional opt-out at the call site.
var tee = new TeeObjective(costObj);

// 6. Drive the optimizer.
var sa = new MultiChainOptimizer(tee, chainCount: 4, ...);
var result = sa.Run(...);

// Post-run: inspect the full evaluation history.
foreach (var record in tee.Log)
{
    Console.WriteLine($"{record.WallClockUtc:O}: {string.Join(", ", record.Vector)} → {record.Result.Score:F2}");
}
```

Cache hit-rate diagnostic:

```csharp
Console.WriteLine($"Cache: {cached.HitCount} hits, {cached.MissCount} misses, "
                + $"{100.0 * cached.HitCount / (cached.HitCount + cached.MissCount):F1}% hit rate");
```

## Recipe 2 — Pareto sweep (cost ↔ Isp) on the electric-propulsion pillar

Use NSGA-II to map the cost ↔ Isp trade frontier on a Hall-effect
thruster.

```csharp
// 1. Pillar adapter (same shape as Recipe 1, different design type).
var inner = HetObjective.Build(myConditions, myBaselineHetDesign);

// 2. Multi-objective extractor: (-Isp, $).
var extractor = ParetoObjectiveBuilder.PhysicsAndCost(
    physicsScoreFn: r => -((ElectricPropulsionResult)r.EngineSpecificBreakdown!).IspVacuum_s,
    costFn:         b => Voxelforge.ElectricPropulsion.Economics
        .ElectricPropulsionCostFactory.For((ElectricPropulsionResult)b!).CapitalCost_USD);

// 3. NSGA-II run.
var nsga = new NsgaIIOptimizer(
    objective:           inner,
    objectiveExtractor:  extractor,
    populationSize:      100,
    maxGenerations:      50,
    seed:                42);
var pareto = nsga.Run();

// Post-run: walk the Pareto front.
foreach (var ind in pareto.ParetoFront)
{
    var negIsp = ind.Objectives![0];   // negative Isp
    var cost   = ind.Objectives![1];   // $ cost
    Console.WriteLine($"Isp={-negIsp:F0} s, Cost=${cost:N0}");
}
```

## Recipe 3 — three-objective sustainability sweep

Same physics, three objectives: −Isp + $ + kg CO₂-eq. NSGA-III is the
preferred algorithm for ≥ 3 objectives (reference-direction-based).

```csharp
var extractor = ParetoObjectiveBuilder.PhysicsCostAndCo2(
    physicsScoreFn: r => -((ElectricPropulsionResult)r.EngineSpecificBreakdown!).IspVacuum_s,
    costFn:         b => Voxelforge.ElectricPropulsion.Economics
        .ElectricPropulsionCostFactory.For((ElectricPropulsionResult)b!).CapitalCost_USD,
    co2Fn:          b => Voxelforge.ElectricPropulsion.Economics
        .ElectricPropulsionCostFactory.For((ElectricPropulsionResult)b!).EmbodiedCO2_kgCO2eq);

var nsga3 = new NsgaIIIOptimizer(
    objective:          inner,
    objectiveExtractor: extractor,
    populationSize:     200,
    maxGenerations:     100,
    seed:               42);
```

## Recipe 4 — LCOE Pareto sweep on a power-gen pillar

Two-objective (Isp ↔ LCOE in $/kWh):

```csharp
var extractor = ParetoObjectiveBuilder.PhysicsAndLcoe(
    physicsScoreFn: r => -((PowerGenResult)r.EngineSpecificBreakdown!).AnnualEnergy_kWh,
    lcoeFn:         b => Voxelforge.Economics.LcoeCalculator.Compute(
        Voxelforge.Economics.PowerGenCostFactory.For((PowerGenResult)b!),
        lifetime_yr:   25,
        discountRate:   0.05));
```

## Recipe 5 — budget-constrained single-objective

Minimize negative-Isp subject to `cost ≤ $5M`:

```csharp
var inner = HetObjective.Build(myConditions, myBaselineHetDesign);

var constrained = CostObjective.WithBudgetCeiling(
    inner:      inner,
    costFn:     b => Voxelforge.ElectricPropulsion.Economics
        .ElectricPropulsionCostFactory.For((ElectricPropulsionResult)b!).CapitalCost_USD,
    budget_USD: 5_000_000.0);

// Inner-score is preserved for budget-feasible candidates; +∞ for over-budget.
var sa = new MultiChainOptimizer(constrained, ...);
```

## Recipe 6 — noise-robust optimization

A pillar whose physics solver has stochastic noise (e.g. a Monte-Carlo
surrogate). Wrap in `SubsamplingObjective` to evaluate at 5 perturbed
neighbours and return the median:

```csharp
var inner    = MyNoisyPillarObjective.Build(...);
var bounded  = new BoundedObjective(inner);
var robust   = new SubsamplingObjective(bounded, neighbourCount: 2);
var cached   = new CachedObjective(robust);

var sa = new MultiChainOptimizer(cached, ...);
```

Cost: 5 inner evaluations per outer call. With `CachedObjective` outside
`SubsamplingObjective`, the 5-evaluation median is cached as a single
score per outer vector — re-querying the same outer vector hits the
cache.

## Recipe 7 — gradient polish post-SA

`GradientProbe` is sibling, not in the stack. After SA finds a winner,
compute the gradient at the winner and step against it:

```csharp
var saWinner = sa.Run();

var probe = new GradientProbe(myInner);   // wrap the inner physics directly
var gradient = probe.ComputeGradient(saWinner.BestParams);

// Apply a small step against the gradient and re-evaluate.
const double stepSize = 1e-3;
var polished = saWinner.BestParams.ToArray();
for (int i = 0; i < polished.Length; i++)
    polished[i] -= stepSize * gradient[i];
var polishedResult = myInner.Evaluate(polished);
```

A more substantial polish would iterate this with adaptive step (or use
the `GradientPolisher` shipped via [PR #401 / T4.1](
https://github.com/poetac/voxelforge/pull/401)).

## Recipe 8a — Maximize instead of minimize

Every optimizer in the portfolio minimizes. To MAX a quantity, either
negate it manually (`r => -r.Thrust_N`) or wrap with `MaximizeAdapter`
for readable callsites:

```csharp
var inner = ThrustObjective.Build(...);     // returns positive Thrust_N as score
var maximised = new MaximizeAdapter(inner); // optimizer minimizes -Thrust_N → max Thrust_N
var sa = new MultiChainOptimizer(maximised, ...);
```

`MaximizeAdapter` preserves infeasibility — never produces a negative-
infeasible-score that would confuse minimizers. The wrapper sits at the
SAME layer as `CostObjective` (mutually exclusive — either re-score by
cost or sign-flip, not both at once; use `CompositeCostObjective` if
you want cost-summing with a max).

## Recipe 8b — Total-system cost via CompositeCostObjective

NEP-cargo-vehicle total-system cost (engine + tank + radiator + power-
conditioning). Each component cost is a separate extractor; the
composite sums them:

```csharp
var inner = NepSystemObjective.Build(...);   // physics solver for the full vehicle

var totalCost = new CompositeCostObjective(inner, new Func<object?, double>[]
{
    b => Voxelforge.Nuclear.Economics.NtrCostFactory
        .For((NepSystemResult)b!).CapitalCost_USD,
    b => Voxelforge.ElectricPropulsion.Economics.ElectricPropulsionCostFactory
        .For((NepSystemResult)b!).CapitalCost_USD,
    b => Voxelforge.Economics.RadiatorCostFactory
        .For((NepSystemResult)b!).CapitalCost_USD,
    b => Voxelforge.Economics.PowerConditioningCostFactory
        .For((NepSystemResult)b!).CapitalCost_USD,
});

var sa = new MultiChainOptimizer(totalCost, ...);
```

Equivalent to wrapping with `new CostObjective(inner, b => sum(...))`
but keeps the four component extractors individually inspectable and
unit-testable. The `ExtractorCount` property surfaces how many
components contribute, useful for diagnostic reports.

## Recipe 8 — Mass-budget Pareto sweep

Spacecraft mass-budget design study: minimize negative-thrust subject
to dry-mass minimization. NSGA-II two-objective sweep:

```csharp
var extractor = ParetoObjectiveBuilder.PhysicsAndMass(
    physicsScoreFn: r => -((ElectricPropulsionResult)r.EngineSpecificBreakdown!).Thrust_N,
    massFn:         b => Voxelforge.ElectricPropulsion.Economics
        .ElectricPropulsionCostFactory.For((ElectricPropulsionResult)b!).Mass_kg);
```

## When to stack what

| Workload | Recommended stack |
|---|---|
| Tight SA loop, no restart | `EngineObjectiveAdapter` → `BoundedObjective` only |
| CMA-ES restart / Bayesian acquisition | + `CachedObjective` |
| Cost ↔ Isp Pareto | + `ParetoObjectiveBuilder` (delegate alongside, not in stack) |
| Sustainability triple-objective | NSGA-III + `PhysicsCostAndCo2` extractor |
| Power-gen LCOE | `PhysicsAndLcoe` extractor |
| Budget constraint | `CostObjective.WithBudgetCeiling` instead of `PerOutputUnit` |
| Stochastic physics | + `SubsamplingObjective` (innermost above pillar adapter) |
| Post-mortem analysis | + `TeeObjective` (outermost) — **non-deterministic by contract**: trace records stamp `DateTime.UtcNow`. Suppresses analyzer rule **VFD012**; do NOT hash or serialise-compare `tee.Log` across runs. |
| Gradient polish | `GradientProbe` (sibling, post-SA) |
| Maximize instead of minimize | `MaximizeAdapter` (sign-flip wrapper, mutually exclusive with CostObjective) |
| Total-system cost (NEP / multi-component) | `CompositeCostObjective` (sums N extractors) |
| Subprocess-oracle resilience | `TimeoutObjective` → `RetryingObjective` (outermost on async path) |

## See also

- [ADR-023](ADR/ADR-023-optimizer-portfolio.md) — the optimizer
  portfolio (SA / CMA-ES / NSGA-II / NSGA-III / Bayesian / hybrid /
  gradient).
- [ADR-025](ADR/ADR-025-iengine-engine-family-abstraction.md) —
  `IEngine<TDesign, TConditions, TResult>` typed pillar adapter.
- [ADR-030](ADR/ADR-030-cost-objective-economics-wire.md) —
  `CostObjective` design contract.
- [ADR-032](ADR/ADR-032-iobjective-composition-pattern.md) — the
  wrapper-stack architecture.
