# ADR-030 — `CostObjective` wires Economics into the IObjective optimizer surface

**Status:** Accepted (2026-05-12)
**Sprint:** Post-PR #489 follow-on
**Related:** [ADR-023 Optimizer portfolio](ADR-023-optimizer-portfolio.md) ·
[ADR-025 IEngine engine-family abstraction](ADR-025-iengine-engine-family-abstraction.md) ·
PR #489 Sprints EC.W1 – EC.W10

## Context

PR #489 (multi-pillar burst, 2026-05-12) shipped cluster-anchored 2026 cost / mass
/ CO₂ factories for every pillar (Sprints EC.W1 – EC.W10) — `Voxelforge.Core
/Economics/` with `CostEstimate`, `EconomicAnalyzer`, `SystemCostBreakdown`,
`CostRegistry`, `LcoeCalculator`; pillar-side `RocketEngineCostFactory`,
`AirbreathingCostFactory`, `ElectricPropulsionCostFactory`,
`MarineCostFactory`, `NuclearCostFactory` lifting per-pillar designs into
`$ / kg-CO₂ / kg-mass` triplets.

What PR #489 did **not** ship: a wire from those factories back to the
SA / CMA-ES / NSGA-II / NSGA-III / Bayesian portfolio. Every pillar's
existing `IObjective` adapter (`RegenChamberObjective`, `RamjetObjective`,
`MpdObjective`, `DisplacementHullObjective`, …) scores by physics
performance — `-Isp` or `-Thrust` or `+ChamberWallTemp` — but never by cost.

The user-visible gap: a designer who has cost / Isp tables for every
feasible candidate cannot ask the optimizer to *find* the cheapest design
at a given Isp floor, or the highest-Isp design at a given cost ceiling,
or any Pareto frontier in the cost ↔ performance plane.

## Decision

**D1.** Add `Voxelforge.Optimization.CostObjective` — an `IObjective`
adapter that wraps any inner objective and replaces its score with a
caller-supplied scalar cost (or any economic metric — mass, CO₂, $) via
`Func<object?, double>` over the inner objective's
`EngineSpecificBreakdown`. The cost function is engine-family-agnostic;
each pillar's `Economics` namespace (which stays internal to that
pillar's Core assembly) is the canonical caller.

**D2.** Honour the inner feasibility contract. When the wrapped objective
emits a non-empty `Violations` list **or** returns `+∞` score, the
`CostObjective` scores the candidate at a configurable `InfeasibleScore`
(default `+∞`) regardless of cost. Rationale: a $1 design that violates a
structural margin must never beat a $100 design that passes every gate.

**D3.** Stay deterministic + thread-safe. The wrapper holds the inner
objective + the cost function as immutable references after construction;
thread-safety reduces to that of the inner objective + the supplied
delegate. The SA strict-determinism contract (same vector → same score
across runs) is preserved iff the cost function is pure.

**D4.** Ship one static factory — `CostObjective.PerOutputUnit(inner,
costFn, outputFn, infeasibleScore)` — for the common cost-per-output
case ($/N propulsion, $/kW power generation, $/kWh LCOE). Zero or
negative output short-circuits to `InfeasibleScore`.

**D5.** Public-API surface stays minimal — one class, two constructors
(primary + `PerOutputUnit` factory), one property (`InfeasibleScore`),
plus the inherited `IObjective` members. No new types in the public
surface beyond `CostObjective` itself.

## Consequences

**Positive:**
- Closes the optimizer ↔ Economics loop with ~140 LOC + 7 PublicAPI symbols.
- Per-pillar Economics namespaces stay internal — cost details (chemistry-
  aware $/kg, manufacturing-process derating, currency baseline) don't
  leak through the public IObjective surface.
- Composable with every existing optimizer (`MultiChainOptimizer`,
  `CmaEsOptimizer`, `NsgaIiOptimizer`, `NsgaIiiOptimizer`,
  `BayesianOptimizer`, hybrid SA/CMA-ES) — they all consume `IObjective`.
- A Pareto-multi-objective wrapper (ADR-030a follow-on candidate) is a
  thin shell over `CostObjective`: pack (cost, -performance) into a tuple
  score for NSGA-II/III.

**Negative:**
- The cost function is `Func<object?, double>` over an `object?`
  breakdown — callers must downcast. Downcasts in callers shift a
  compile-time error to runtime, but the alternative (generic
  `CostObjective<TBreakdown>`) would force `EngineObjectiveAdapter`'s
  `object?` boundary to leak generic types up the IObjective hierarchy
  — a larger architectural cost.
- The `+∞` infeasible-score sentinel collides with optimizers that
  natively understand violations (e.g. NSGA-II uses constraint dominance).
  CostObjective routes infeasible-via-+∞; downstream optimizers that
  consume the `Violations` list directly still see them.
- Adds one more "wrapper objective" pattern to the codebase. Future
  metric-objective wrappers (mass, CO₂, LCOE) re-use `CostObjective`
  itself — the cost function arg makes them parametric.

## Alternatives considered

**A1. Per-pillar `CostXObjective` per pillar.** Reject — duplicates the
infeasibility-routing + breakdown-extraction boilerplate across 5+
pillars. The `Func<object?, double>` cost-extractor is precisely the
boilerplate this approach would force every pillar to re-write.

**A2. Generic `CostObjective<TBreakdown>`.** Reject — forces the
`EngineSpecificBreakdown` typed leak up through `IObjective`, which is
deliberately typed as `object?` (see ADR-025 §D4). The cost-extractor
pattern keeps the existing object? boundary.

**A3. Multi-objective only (NSGA-style).** Reject (this ADR) — scope
expansion. Single-objective cost minimization is the immediate gap;
multi-objective wrappers are a follow-on layer on top of single-objective
wrappers, not a substitute. Tracked as a follow-on candidate (ADR-030a or
similar).

**A4. Bake cost into pillar objectives directly (e.g. extend
`RegenScoreResult.TotalScore` to include cost penalties).** Reject —
couples physics-objective contract to economics, breaking the
single-responsibility shape that ADR-023 + ADR-025 established. Cost is
a separate concern; the wrapper composition is the right boundary.

## Implementation

`Voxelforge.Core/Optimization/CostObjective.cs` (~140 LOC); 10 unit
tests in `Voxelforge.Tests/Optimization/CostObjectiveTests.cs` pin:
score replacement on feasible, infeasibility routing (with explicit
`+∞` sentinel + non-empty `Violations`), variable delegation,
`PerOutputUnit` arithmetic + zero/negative-output short-circuits, and
strict-determinism repeat-call invariant.

PublicAPI.Unshipped.txt gains 7 new symbols. No PicoGK touch, no schema
bump, no analyzer / generator changes.

## Follow-ons

- **ADR-030a (candidate)** — Pareto multi-objective wrapper. Pack (cost,
  -performance) into a tuple score for NSGA-II / NSGA-III. ~80 LOC.
- **Embodied-CO₂ + mass objectives** — static factories on
  `CostObjective.ByEmbodiedCO2(...)` / `ByMass(...)`. Each is ~10 LOC; the
  cost function is the only differentiator.
- **`CostFloorObjective` (constrained variant)** — minimise physics score
  subject to `cost ≤ budget`. Lagrangian penalty on cost-overrun. ~60 LOC.
