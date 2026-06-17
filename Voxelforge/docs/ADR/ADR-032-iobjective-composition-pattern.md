# ADR-032 — `IObjective` composition pattern

> Superseded by [ADR-043](ADR-043-objectivewrappers-maturity.md) — wrapper-inventory refresh.

**Status:** Superseded by ADR-043 (2026-05-16)
**Sprint:** Post-PR #489 follow-on
**Related:** [ADR-023 Optimizer portfolio](ADR-023-optimizer-portfolio.md) ·
[ADR-025 IEngine engine-family abstraction](ADR-025-iengine-engine-family-abstraction.md) ·
[ADR-030 CostObjective Economics wire](ADR-030-cost-objective-economics-wire.md)

## Context

After ADR-030 shipped `CostObjective` + `ParetoObjectiveBuilder`, the
codebase grew a half-dozen `IObjective` wrapper types beyond the
foundational `EngineObjectiveAdapter`:

- `CostObjective` (single-objective cost / mass / CO₂ / LCOE replacement
  of inner score), 6 static factories.
- `ParetoObjectiveBuilder` (multi-objective extractor for NSGA-II/III),
  5 static factories.
- `CachedObjective` (memoization on vector identity).
- `TeeObjective` (record every evaluation to a log).
- `BoundedObjective` (clamp out-of-bounds vectors).

Looking at the broader codebase, there is also `EngineObjectiveAdapter`
(generic adapter over `IEngine<TDesign, TConditions, TResult>` —
the most-used adapter on production paths), plus inner per-pillar
adapters that consumers can write themselves.

The question this ADR settles: **what is the composition contract?**
Specifically, when do we add a new wrapper vs. extending an existing
one, what invariants must every wrapper preserve, and what's the
canonical wrapping order?

## Decision

**D1. Single-class-per-concern.** Each cross-cutting concern (cost
extraction, caching, logging, bounds clamping, Pareto-vector emission)
is exactly one wrapper class. The class may carry multiple static
factories that vary the same concern (e.g.
`CostObjective.PerOutputUnit` / `ByEmbodiedCO2` / `ByLcoe`) but does
NOT mix concerns (caching is not an option on `CostObjective`; wrap
the cost in `CachedObjective` if you want both).

**D2. Inner-objective preservation invariants.** Every wrapper MUST
preserve:

- `DimensionCount` (no padding or truncation of the input vector).
- `Variables` (downstream optimizers read `Min` / `Max` for sampling).
- The inner objective's feasibility contract: when the inner emits
  `Violations.Count > 0` OR `Score == +∞`, the wrapper's output also
  signals infeasible. The exact infeasible-signal value may differ
  (`CostObjective` routes to a configurable `InfeasibleScore`;
  `ParetoObjectiveBuilder` routes to an all-`+∞` tuple).
- Determinism: same vector → same output. Wrappers that introduce
  state (`CachedObjective.HitCount`, `TeeObjective.Log`) must be
  monotonic-only — never erase or rewrite historical evaluations.
- Thread-safety: wrappers added at top of the stack must be thread-safe
  iff the bottom is. (Concretely: every shipped wrapper is thread-safe
  via either statelessness or `ConcurrentDictionary` / `lock` / atomic
  fields.)

**D3. Canonical wrapping order** (bottom-up — closest-to-physics first):

```
inner physics objective                                          (innermost)
    ↓ wraps
EngineObjectiveAdapter<TDesign, TConditions, TResult>            (typed pillar adapter)
    ↓ wraps (optional)
BoundedObjective                                                 (defensive bounds)
    ↓ wraps (optional)
CachedObjective                                                  (memoization)
    ↓ wraps (optional)
CostObjective / variant                                          (single-objective re-score)
    ↓ wraps (optional)
TeeObjective                                                     (logging tee)              (outermost)
```

Outside this stack: `ParetoObjectiveBuilder` returns a delegate
(`Func<EvaluationResult, double[]>`), not an `IObjective` — it is
consumed by NSGA-II/III alongside the wrapped IObjective, not as
another wrapper.

Notes on ordering:

- **`BoundedObjective` should be INSIDE caching.** A cache hit on a raw
  out-of-bounds vector is wasted (the bounded layer clamps it and the
  underlying objective sees identical inputs). Putting bounded inside
  cached means cached keys are on the raw vector (which is what the
  optimizer asked); bounded then runs on cache MISS only.

Actually, the opposite — **`BoundedObjective` should be OUTSIDE
caching.** A cache hit on the raw vector returns the cached score for
the clamped equivalent, which IS what the optimizer expects (the inner
objective evaluated on the clamped vector). Cached's key is the raw
vector so two distinct OOB requests at the same clamped target hit
the same cache slot. This is correct.

Empirical: the SI.W22-shipped `Composition_Bounded_Cached_Tee_TogetherWork`
test pins the OUTSIDE-cached-INSIDE-bounded ordering. The wrapper
stack documented in this ADR matches that test.

- **`TeeObjective` should be OUTERMOST.** Tee should log the actual
  candidates the optimizer asked about, not the post-clamped post-
  caching version that the inner saw. If a downstream consumer wants
  to log inner-side state too, write a second `TeeObjective` inside
  the cost layer.

- **`CostObjective` sits BETWEEN cached and tee.** Re-scoring by cost
  is part of the candidate's "real" score; both cache lookup and tee
  log should see the post-cost score. The cost layer doesn't introduce
  vector keys; both layers above + below it operate on the same
  vector key.

**D4. Public-surface minimalism.** Each wrapper exposes:

- Its primary constructor (taking the inner `IObjective`).
- Zero-or-more static factories for common variants.
- `DimensionCount` + `Variables` properties (interface delegates).
- `Evaluate(ReadOnlySpan<double>, CancellationToken)` (interface impl).
- Diagnostic accessors (`HitCount` on `CachedObjective`, `Log` on
  `TeeObjective`, `LastDetectedEvents` on the SI.W23-shipped event-
  detection — even though that's not strictly an `IObjective`).
- Optional `Reset()` for stateful wrappers.

NO public mutation surface beyond construction + Reset.

**D5. Test coverage minimum per wrapper.** Each wrapper ships:

- Happy-path pass-through (inner score preserved or transformed as
  documented).
- Null-inner-argument guard.
- Wrong-vector-length guard.
- Composition test (verify the wrapper preserves DimensionCount +
  Variables when stacked).
- Deterministic-repeat test (same input → same output).

Specific wrappers add specific tests (Cached: hit/miss accounting +
Reset; Tee: defensive vector copy; Bounded: per-dim clamp).

## Consequences

**Positive:**
- Composition stack is clear + load-bearing for future extensions
  (e.g. a future `GradientObjective` for finite-difference polish would
  sit between `BoundedObjective` and `CachedObjective`).
- Per-pillar Economics namespaces stay internal — only the cost
  function delegate crosses the public surface (ADR-030 D5).
- Adding a new concern is mechanical: extend the recipe; tests follow
  the per-wrapper minimum (D5); no upstream coordination needed.

**Negative:**
- The recipe is conventional, not enforced. A wrapper that violates
  D2 (e.g. perturbs `DimensionCount`) compiles cleanly but breaks
  downstream optimizers in non-obvious ways. Add a runtime assertion
  in `MultiChainOptimizer` that checks `inner.DimensionCount` matches
  what bounds say (already implicit via the bounds array; could be
  hardened with a defensive runtime check).
- The `ParetoObjectiveBuilder` is parallel to but NOT layered with
  the wrapper stack — it produces a delegate, not an `IObjective`.
  Users sometimes confuse the two; the documentation in
  `ParetoObjectiveBuilder.cs` clarifies, but the asymmetry is real.

## Alternatives considered

**A1. Universal `IObjective<TBreakdown>` generic.** Rejected per
ADR-025 D4 — would force the typed leak up through the entire stack
+ every adapter. The `object?` boundary is deliberate; downcasts in
callers are the agreed cost.

**A2. Builder-pattern fluent API
(`MyObjective.Cached().Bounded().Tee().ToObjective()`).** Rejected —
adds a chain syntax without changing capability. The explicit
`new CachedObjective(new BoundedObjective(inner))` is more readable
and self-documents the layer order.

**A3. Attribute-based wrapper config.** Rejected — couples wrapper
selection to compile-time decoration of pillar adapters. Per-call
composition (choose wrappers at the call site, not at the adapter)
is more flexible.

**A4. Drop wrappers entirely, inline the concerns into each pillar
adapter.** Rejected — duplicates the cross-cutting boilerplate across
every pillar (rocket + airbreathing + electric + marine + nuclear),
matching the failure mode that the wrapper layer was introduced to
prevent (ADR-030 D5 + the original `EngineObjectiveAdapter`).

## Implementation status

Live in `Voxelforge.Core/Optimization/`:

- `IObjective.cs` (ADR-025) — the contract
- `EngineObjectiveAdapter.cs` (ADR-025) — typed pillar adapter
- `CostObjective.cs` (ADR-030) — cost-replacement wrapper
- `ParetoObjectiveBuilder.cs` (ADR-030 follow-on) — multi-objective extractor
- `ObjectiveWrappers.cs` (this ADR) — Cached / Tee / Bounded

Tests in `Voxelforge.Tests/Optimization/`:

- `IObjectiveContractTests.cs`
- `CostObjectiveTests.cs`
- `ParetoObjectiveBuilderTests.cs`
- `ObjectiveWrappersTests.cs`
- `EngineObjectiveAdapterTests.cs`

## Follow-ons

- **`SubsamplingObjective`** — sample the inner objective at sub-tick
  resolution (e.g. evaluate at 10 perturbed neighbours, return the
  median). Useful for noise-robust optimization on transient solvers.
- **`SurrogateObjective`** — wrap a Bayesian-GP surrogate
  (`GaussianProcessSurrogate` already exists) around an `IObjective`
  + an inner-call budget; serve N - budget calls from the surrogate.
- **`AsyncObjective`** — `IObjective` wrapper that returns a Task; for
  scenarios where the inner objective is a subprocess (e.g.
  `voxelforge-eval`).
