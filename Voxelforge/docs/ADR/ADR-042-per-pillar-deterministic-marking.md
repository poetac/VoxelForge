# ADR-042: Per-pillar `[Deterministic]` marking

**Status:** Accepted (2026-05-16)
**Supersedes:** —
**Related:** ADR-020 (`[Deterministic]` analyzer policy — parent), ADR-026 §4.6 (per-pillar Definition of Done — "VFD001-VFD006 clean")
**Issue:** [#565](https://github.com/poetac/voxelforge/issues/565)

## Context

ADR-020 introduced the `Voxelforge.Analyzers` Roslyn analyzer and its
`[Deterministic]` opt-in marker, initially marking six surfaces on the
cross-cutting optimizer (`MultiChainOptimizer`, `SimulatedAnnealingOptimizer`,
`CmaEsOptimizer`, `NsgaIIOptimizer`, `BayesianOptimizer`, and
`RegenChamberOptimization.GenerateWith`). The analyzer has since grown to
VFD001-VFD015 and is wired (via `<ProjectReference OutputItemType="Analyzer">`)
into every pillar Core that has an optimizer surface.

The 2026-05-16 architecture audit (finding F-5) raises a follow-on: each pillar Core ships its
own optimizer factories (`*Objective.Build`) and per-design entry-points
(`*Optimization.GenerateWith` or equivalent) but **none currently carry the
`[Deterministic]` attribute**. Per-assembly call-graph closure (ADR-020 § Wiring)
therefore yields an empty taint set on every pillar Core, and VFD001-VFD015
silently pass on the pillar bodies — including the new VFD013/014/015 rules
shipped under PR 5 / #565.

Enumeration via `grep -rn "\[Deterministic\]" Voxelforge.{Airbreathing,ElectricPropulsion,Marine,Nuclear,Cfd}.Core/`
on the worktree (2026-05-16):

| Pillar Core                       | `[Deterministic]` marks |
| --------------------------------- | ----------------------- |
| `Voxelforge.Airbreathing.Core`    | 0                       |
| `Voxelforge.ElectricPropulsion.Core` | 0 (one comment-only "to-be-marked" TODO in `Thermo/PropellantTables.cs`) |
| `Voxelforge.Marine.Core`          | 0                       |
| `Voxelforge.Nuclear.Core`         | 0                       |
| `Voxelforge.Cfd.Core`             | 0                       |

The analyzer is wired everywhere but firing on nothing in those five projects.

## Decision

**D1 — Per-pillar `[Deterministic]` audit-and-mark sprints.** Each pillar gets
a dedicated audit-and-mark sprint that adds `[Deterministic]` to its
`*Objective.Build` factory, its `*Optimization.GenerateWith` (or equivalent
orchestrator entry point), and any internal helper the analyzer can usefully
taint via call-graph closure. Cadence is incremental — one pillar per sprint —
so the inevitable cleanup work to satisfy VFD001-VFD015 is bounded.

**D2 — Default to method-level marking.** Mark the entry-point method (`Build`,
`GenerateWith`, `Evaluate` on `IObjective` implementations) rather than the
entire class, mirroring ADR-020's `RegenChamberOptimization.GenerateWith`
pattern. Class-level marking is reserved for types that are end-to-end
deterministic surfaces with no non-deterministic siblings (the cross-cutting
`MultiChainOptimizer` / `CmaEsOptimizer` / etc.). Pillar `*Optimization`
classes typically host UI-status-callback methods that are not deterministic,
so class-level marking would force false-positive suppressions.

**D3 — Per-pillar Definition-of-Done extension.** ADR-026 §4.6 already
requires "VFD001-VFD006 clean" for a pillar ship. Extend the DoD to
"VFD001-VFD015 clean AND at least one `[Deterministic]` mark on the pillar's
optimizer entry point". Without a marked entry, the per-assembly call-graph
closure is empty and the analyzer cannot fire on the pillar body — the
"clean" claim is vacuously true and load-bearing nothing.

**D4 — Roadmap.** Queue five pillar-marking sprints (one per pillar:
Airbreathing × 5 cycles, EP × 6 kinds, Marine × 2 hull types, Nuclear × 1
reactor family, CFD oracle). Sprint sequencing and tracking move to a
separate umbrella issue; this ADR commits to the discipline, not to the
work itself.

## Consequences

**Positive.** The `[Deterministic]` promise becomes mechanically enforceable
on every pillar's optimizer hot path, not just the cross-cutting framework.
New analyzer rules (VFD013 static mutable-field reads / VFD014 FP-accumulated
time loops / VFD015 unstable sorts — from PR 5 / #565) fire immediately on the
newly-marked surfaces, catching any latent issues before the pillar ships.
The per-pillar DoD entry (D3) keeps the marking step from being silently
skipped at PR time.

**Negative.** Each pillar-marking sprint may require source changes to
satisfy VFD001-VFD015 before the `[Deterministic]` mark can land green —
the marking can't be added until the body is fire-free. The audit work is
deferred per-pillar; this ADR commits to the discipline but does not do the
work. Estimated cost: one sprint per pillar, dominated by Vfd008 (filesystem)
and Vfd013 (static-mutable-field) cleanup risk.

## References

- Audit finding F-5 — 2026-05-16 architecture audit
- [ADR-020](ADR-020-deterministic-analyzer.md) — parent analyzer policy + initial six-surface marking
- [ADR-026](ADR-026-multi-pillar-coordination.md) §4.6 — per-pillar Definition-of-Done checklist
- Issue [#565](https://github.com/poetac/voxelforge/issues/565) — audit follow-on umbrella
