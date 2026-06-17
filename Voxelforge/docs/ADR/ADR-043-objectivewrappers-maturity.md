# ADR-043 — `ObjectiveWrappers` maturity (supersedes ADR-032)

**Status:** Accepted (2026-05-16)
**Supersedes:** [ADR-032](ADR-032-iobjective-composition-pattern.md)
**Closes (in part):** [#565](https://github.com/poetac/voxelforge/issues/565) — audit F-4 follow-up.
**Related:**
[ADR-023](ADR-023-optimizer-portfolio.md) (optimizer portfolio) ·
[ADR-030](ADR-030-cost-objective-economics-wire.md) (`CostObjective`).

## Context

The 2026-05-16 architecture audit (finding F-4) flagged ADR-032 as stale on two axes. First, its
"Implementation status" section listed three shipped wrappers in
`ObjectiveWrappers.cs` (Cached / Tee / Bounded) and tagged Subsampling /
Surrogate / Async as Follow-ons; today the file ships eleven `IObjective`
implementations plus one non-`IObjective` sibling helper. Second, D3's
"Notes on ordering" carries two consecutive paragraphs arguing OPPOSITE
verdicts on whether `BoundedObjective` sits inside or outside
`CachedObjective`, with the test `Composition_Bounded_Cached_Tee_
TogetherWork` in `ObjectiveWrappersTests.cs` as the actual SSOT.

This ADR refreshes the inventory, pins the canonical wrapping order
against the test SSOT, and freezes the wrapper public surface.

## Decision

**D1. Confirmed wrapper inventory.** Eleven `IObjective` implementations
plus one builder and one sibling helper ship today:

- `Voxelforge.Core/Optimization/CostObjective.cs` — `CostObjective`
  (single-objective cost / mass / CO₂ / LCOE re-score, ADR-030).
- `Voxelforge.Core/Optimization/ParetoObjectiveBuilder.cs` —
  `ParetoObjectiveBuilder` (static helper that returns a
  `Func<EvaluationResult, double[]>` extractor for NSGA-II/III; NOT
  itself an `IObjective` — it sits parallel to the wrapper stack).
- `Voxelforge.Core/Optimization/ObjectiveWrappers.cs` — `CachedObjective`,
  `TeeObjective`, `BoundedObjective`, `SubsamplingObjective`,
  `TimeoutObjective`, `RetryingObjective`, `MaximizeAdapter`,
  `CompositeCostObjective`, `NormalizingObjective`, `SurrogateObjective`,
  `AsyncObjective`. Plus `GradientProbe` as a non-`IObjective` sibling
  helper for finite-difference gradient queries.

**D2. Canonical wrapping order.** Bottom-up — closest-to-physics first:

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
TeeObjective                                                     (logging tee, outermost)
```

`BoundedObjective` sits INSIDE `CachedObjective`. The cache hashes the
raw vector the optimizer asked about; on a cache miss the bounded layer
clamps and the inner objective sees legal inputs. Two distinct
out-of-bounds vectors at the same clamped target produce two cache
misses; identical OOB vectors produce one miss + one hit. The SSOT is
`ObjectiveWrappersTests.cs::Composition_Bounded_Cached_Tee_TogetherWork`
which constructs the stack as `new TeeObjective(new CachedObjective(
new BoundedObjective(inner)))` and asserts exactly that hit/miss
accounting. This resolves ADR-032 D3's self-contradictory "Notes on
ordering" paragraphs unambiguously.

**D3. Public-surface freeze.** The wrapper inventory in D1 is closed.
Adding a twelfth `IObjective` implementation to `ObjectiveWrappers.cs`
(or a fourth named pillar adapter) requires a successor ADR carrying
rule-of-three justification — three concrete consumers driving the
need, plus a sketch of where the new wrapper sits in the D2 stack.

**D4. Composition discipline preserved from ADR-032.** Every wrapper
MUST preserve the inner objective's `DimensionCount`, `Variables`,
feasibility contract (`Violations.Count > 0` OR `Score == +∞` → wrapper
also signals infeasible), determinism, and thread-safety. The per-
wrapper test minimum from ADR-032 D5 stands: happy path + null-inner
guard + wrong-vector-length guard + composition test + deterministic-
repeat test. Wrappers with stateful diagnostics (`HitCount`,
`SampleCount`, `Log`, `InnerCallCount`, etc.) add coverage for the
state-mutation surface.

## Consequences

**Positive:**

- Documented inventory matches the shipped surface; readers landing
  here from the project docs see the real list, not the SI.W12 snapshot.
- ADR-032 D3's self-contradiction is resolved against the test SSOT.
- The wrapper inventory is frozen, so a new cross-cutting concern
  cannot land in `ObjectiveWrappers.cs` without an ADR.

**Negative:**

- Any future wrapper requires a new ADR. Acceptable cost given the
  3 → 10 wrapper inventory churn between ADR-032 and this ADR.

## References

- Audit finding F-4 (2026-05-16 architecture audit) — drift
  between ADR-032 and the live `ObjectiveWrappers.cs`.
- [ADR-032](ADR-032-iobjective-composition-pattern.md) (superseded).
- Issue [#565](https://github.com/poetac/voxelforge/issues/565).
- `Voxelforge.Core/Optimization/ObjectiveWrappers.cs` — wrapper class
  definitions.
- `Voxelforge.Tests/Optimization/ObjectiveWrappersTests.cs` —
  `Composition_Bounded_Cached_Tee_TogetherWork` is the canonical-order
  SSOT.
