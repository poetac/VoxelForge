# ADR-019: Declarative gate registry (Sprint 0 PR-1)

**Status:** Accepted (2026-04-29) — both phases shipped. Phase 1 (infrastructure) and Phase 2 (gate migration) both completed in PR #281. Gate count has since grown to **53** via individual `RegisterGate()` additions; see `docs/GATES.md` for the current live inventory. The pulsejet Wave-1 commit (2026-05-05) added an **additive** generic shape (`FeasibilityGateDescriptor<TResult>` + `GateRegistry<TResult>`) in `GenericGateRegistry.cs` for non-rocket pillars; the rocket-side non-generic types in this ADR remain unchanged and continue to host all 53 rocket gates.
**Supersedes:** —
**Related:** ADR-009 (feasibility-gate discipline), ADR-015 (Core/Voxels/App split), ADR-026 (multi-pillar coordination conventions).

## Context

`FeasibilityGate.Evaluate(RegenGenerationResult)` is currently a 1,150-line monolithic `if`-chain (`Voxelforge.Core/Optimization/FeasibilityGate.cs`, ~lines 662–1903) that emits 49 distinct gate violations. Adding a gate today means editing the megaswitch and threading conditional state lookups (WallMaterial, propellant pair metadata, etc.) inline. The post-Phase-6 feasibility-audit cascade (PRs #73–#88) added 11 gates in rapid succession, each touching the same file at the same lines — a clear friction signal.

ADR-009 ("feasibility-gate discipline") promises that "every gate is independently testable" and "adding a gate is additive." Today, both are *true* but harder than they should be: the testability is per-test against the megamethod, not per-gate, and additions multiply merge conflicts on the megaswitch. A 2026-04-28 architecture review (recommendation #5) identified this as a structural problem worth refactoring before the next gate-addition wave (~15-20 air-breathing gates per Step-1 sub-step).

The Sprint 0 trimmed scope (this commit) executes recommendation #5 in two phases. Phase 1 (this ADR's first acceptance) lays the infrastructure with zero behavior change. Phase 2 migrates the 49 gates iteratively, snapshot-tested between batches.

The contradictory broader recommendation (#1, `IEngine<TDesign,TResult>`) was deliberately deferred per the rule of three.

## Decision

### Phase 1 (this commit) — infrastructure-only

A new `Voxelforge.Core/Optimization/GateRegistry.cs` introduces three additive types and one static class:

- **`EngineFamilyMask`** — `[Flags]` enum with `None`, `RocketRegen`, `RocketAerospike`, convenience `Rocket = RocketRegen | RocketAerospike`, and `All`. `Airbreathing = 1 << 2` is reserved (commented out) for Step 1 of the scope-expansion roadmap.
- **`GateSeverity`** — `Hard` (rejects candidate) vs `Advisory` (warning only). Distinct from `GateKind` (which classifies the *physics basis* of the gate); see [ADR-009](ADR-009-feasibility-gates.md) and `GateKind.cs`.
- **`FeasibilityGateDescriptor`** record — declarative gate description with `Id`, `Severity`, `Kind`, `Applicability`, `AdrRef`, and `Predicate(RegenGenerationResult) -> FeasibilityViolation?`. *Named with the `Descriptor` suffix to avoid colliding with the existing `static class FeasibilityGate` that hosts the evaluator entry points.*
- **`GateRegistry`** static class — lazy-initialised registry with `All`, `ById(string)`, `TryGetById(string, out)`, and `internal Register(FeasibilityGateDescriptor)`. Enforces unique IDs by throwing on duplicate registration.

`FeasibilityGate.Evaluate` and `FeasibilityGate.PreScreen` are **unchanged** in Phase 1. The registry is empty at first use; per-family registration helpers will populate it during Phase 2. Phase 1 is provably bit-identical to pre-commit behaviour because the registry is never read by the evaluator.

### Phase 2 (shipped PR #281, 2026-04-29) — iterative gate migration

Each migration step:

1. Extracts a gate's emit logic into a static method returning `FeasibilityViolation?` (or `null` for non-firing).
2. Registers it via a per-family helper:
   - `RocketRegenGates.RegisterAll()` — gates today emitted from `FeasibilityGate.Evaluate`.
   - `RocketStructuralGates.RegisterAll()` — yield, burst, shaft-whirl.
   - `RocketManufacturingGates.RegisterAll()` — LPBF, TPMS, printability.
   - `AerospikeFeasibility.RegisterGates()` — aerospike gates with `EngineFamilyMask.RocketAerospike`.
3. Removes the inline `if`-block from `FeasibilityGate.Evaluate`.
4. Verifies `GateOrderingSnapshotTests` stay green — Hyrum's-law guard that `(ConstraintId, ordering)` invariants hold.
5. Verifies the `FeasibilityGateBench` microbench is within 5% of pre-refactor baseline (delegate-call overhead).

Once the last gate migrates, `FeasibilityGate.Evaluate` becomes a thin loop:

```csharp
public static FeasibilityGateResult Evaluate(RegenGenerationResult gen)
{
    var violations = new List<FeasibilityViolation>(capacity: 4);
    foreach (var gate in GateRegistry.All)
    {
        if ((gate.Applicability & EngineFamilyMask.Rocket) == 0) continue;
        if (gate.Predicate(gen) is { } v) violations.Add(v);
    }
    return new FeasibilityGateResult(violations.Count == 0, violations.ToArray());
}
```

The 1,150-line megamethod retires.

### PreScreen handling

`FeasibilityGate.PreScreen(OperatingConditions, RegenChamberDesign)` runs a curated 3-gate subset (CONTRACTION_RATIO_OUT_OF_BAND, L_STAR_BELOW_PROPELLANT_MIN, TPMS_CELL_FEATURE_TOO_SMALL) against `(cond, design)` — not against `RegenGenerationResult`. Because the predicate signature differs, PreScreen is **not** unified into the registry in this sprint. The hand-coded fast path stays. A follow-up sprint may introduce a parallel `IPreScreenGate` interface if a second pre-screen-eligible gate appears; deferred until that demand is real.

The `Snapshot_PreScreen_*` tests in `GateOrderingSnapshotTests` pin PreScreen's first-fire-wins ordering separately.

## Consequences

### Positive

- **Adding a gate becomes one `RegisterGate(...)` call** in the appropriate per-family file. No edits to a megaswitch.
- **Gate metadata becomes structured** — `Id`, `Severity`, `Kind`, `Applicability`, `AdrRef`. Auto-generated gate-inventory documentation becomes possible.
- **Air-breathing pre-empts a follow-up refactor.** The `EngineFamilyMask` enum + filter-at-evaluation-time pattern is in place today. When air-breathing ships ~15-20 new gates, they self-register with `EngineFamilyMask.Airbreathing` (after uncommenting the enum value) without touching rocket code.
- **Duplicate-ID detection at registration time** — typos surface immediately at type-init, not at the next test run.
- **Snapshot tests pin ordering** — `GateOrderingSnapshotTests` (9 tests, all green at sprint start) prevent silent gate-firing-order drift across the migration.
- **Phase 1 is risk-free** — bit-identical to pre-commit by construction (registry empty, evaluator unchanged).

### Negative

- **Per-call overhead** — delegate invocation is ~1-3 ns per gate vs JIT-inlined branches. Net SA hot-path impact ≤3% (≈ 55 declarative gates × ~2 ns × millions of evaluations). Tracked by `FeasibilityGateBench`; >5% regression is investigated, not silently merged.
- **Sprint 0 scope** — Phase 2's full migration is ~5-7 days of careful per-gate work. Each gate has nuances (cross-state lookups, conditional sub-emissions, multi-violation patterns) that can't all be migrated in one sitting. The plan calls for batched migrations with snapshot-test verification between batches.
- **`PreScreen` not unified** — two evaluators (full + pre-screen) coexist. Pragmatic given the predicate-signature mismatch; revisit when a second pre-screen-eligible gate forces the abstraction.

### Neutral

- **Type names** — `FeasibilityGateDescriptor` (not `FeasibilityGate`) avoids the static-class collision; the existing `static class FeasibilityGate` retains its evaluator role.
- **PublicAPI surface expansion** — adds 4 public types (`EngineFamilyMask`, `GateSeverity`, `FeasibilityGateDescriptor`, `GateRegistry`) to `Voxelforge.Core/PublicAPI.Unshipped.txt`. Promoted to Shipped on next release.
- **No `[SaDesignVariable]` re-numbering** — registry refactor is gate-only; SA design-variable space is untouched (ADR-012 hard rule preserved).

## Alternatives considered

1. **Defer the refactor entirely.** The memo lists this as an option (S-1 / Issue [#205](https://github.com/poetac/voxelforge/issues/205) tracked it as deferred tech debt). Rejected because the friction is real — 11 gates added in cascade, each touched the same file. Quality-of-life win is concrete regardless of air-breathing.

2. **Build the gate registry as part of `IEngine<TDesign,TResult>`.** The architecture review listed this as recommendation #1. Rejected per rule of three: with one engine family today, the abstraction would be designed against rocket only and likely need rework when air-breathing surfaces real shape differences. Gate registry stands on its own value.

3. **Generalise the predicate signature to `Func<IEngineResult, FeasibilityViolation?>`.** Tempting but locks in an `IEngineResult` interface against rocket-only — exactly the rule-of-three trap. Stay typed to `RegenGenerationResult` today; when ramjet ships a `RamjetResult` and turbojet ships a `TurbojetResult`, design the unifying interface against three real shapes.

4. **Source generator for gate registration** (analogous to T1.4 source-gen for `[SaDesignVariable]`). Rejected as scope creep for Sprint 0. Hand-written registration is fine for 49 + ~20 future gates; the source generator only pays off if gate count grows past ~150.

5. **Unify `PreScreen` and `Evaluate` into one registry.** Rejected because predicate signatures differ — `Evaluate` takes a fully-solved `RegenGenerationResult`, `PreScreen` takes raw `(OperatingConditions, RegenChamberDesign)` to short-circuit the ~50-200 ms thermal solver. Forcing a common signature means either solving twice or weakening the pre-screen contract. Deferred until a real demand surfaces.

## Verification

- **Pre-Phase-1:** all 2199 tests green on `main` HEAD.
- **Post-Phase-1:** `dotnet test` reports 2208 pass + 1 skip (the 9 new `GateOrderingSnapshotTests` joined the 2199 baseline). Bench-regression: not run (refactor-only, registry empty so `Evaluate` path is bit-identical by construction).
- **Per-Phase-2-batch:** `dotnet test --filter "GateOrdering|GateRegistry|Feasibility|Sprint36"` plus the `FeasibilityGateBench` microbench. Snapshot drift = revert. Microbench >5% regression = investigate before merge. (8 batches of 4-7 gates each, all green.)
- **Phase 2 final state (PR [#281](https://github.com/poetac/voxelforge/pull/281), 2026-04-29):** all 49 founding gates migrated; test suite 2213 pass + 1 skip. 5 `Registry_*` completeness tests pin distinct ConstraintIds (multi-emit gates like PURGE / TRAPPED_POWDER collapse to one descriptor each), registration order, `RocketRegen` mask coverage, `ById`/`TryGetById` contracts. `FeasibilityGate.Evaluate()` reduced from 1,150-line if-chain to a thin loop over `GateRegistry.All`. **Post-Phase-2 additions** through HEAD `3bdc8ed` brought rocket-pillar registered count to ≈ 55 (OOB-6 acoustic dampers ×2 [PR #319], OOB-13 E-D nozzle [PR #328], OOB-1 Sprint 2 coolant calibration [PR #351], plus a handful of subsequent advisory and physics-limit gates). Each used the one-call `RegisterGate()` pattern — no if-chain edits.

## References

- Architecture review recommendation #5 (gate registry) — original auditor recommendation.
- [ADR-009: Feasibility-gate discipline](ADR-009-feasibility-gates.md) — the gate-as-SSOT principle this ADR operationalises.
- [ADR-012: Adding an SA design variable](ADR-012-adding-an-sa-design-variable.md) — the registry pattern this ADR mirrors at the gate level.
- [ADR-015: Core/Voxels/App split](ADR-015-core-voxels-app-split.md) — the boundary that puts `FeasibilityGate` + `GateRegistry` in `.Core` (no PicoGK reference).
- `Voxelforge.Tests/Optimization/GateOrderingSnapshotTests.cs` — the 9-test ordering safety net for the migration.
