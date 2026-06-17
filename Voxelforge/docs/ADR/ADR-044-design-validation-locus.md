## ADR-044: Design-validation locus pattern (`*Design.ValidateSelf` vs. entry-point)

**Status:** Proposed (2026-05-16)
**Supersedes:** —
**Related:**
[ADR-019](ADR-019-gate-registry.md) (declarative feasibility-gate registry — a separate, downstream validation layer) ·
[ADR-022](ADR-022-design-persistence-schema-versioning.md) (design-record schema versioning) ·
[ADR-026](ADR-026-multi-pillar-coordination.md) §4.6 (per-pillar Definition-of-Done) ·
[ADR-039](ADR-039-ivoxelgenerator-consolidation.md) (canonical-shape codification for parallel pillar interfaces).
**Issue:** [#588](https://github.com/poetac/voxelforge/issues/588)

## Context

The 2026-05 [#558](https://github.com/poetac/voxelforge/issues/558) error-idiom migration sweep (PRs [#578](https://github.com/poetac/voxelforge/pull/578) Marine, [#579](https://github.com/poetac/voxelforge/pull/579) Nuclear, [#581](https://github.com/poetac/voxelforge/pull/581) Airbreathing, [#583](https://github.com/poetac/voxelforge/pull/583) Electric Propulsion — all merged) walked every `throw new` site under the four propulsion-family pillar Cores and replaced them with the house-style `ArgumentOutOfRangeException` / `ArgumentException` shapes from PR-A. Walking those sites surfaced a structural divergence in *where* each pillar concentrates its field-range validation:

| Pillar             | `*Design.ValidateSelf` present? | Migrated throw-site profile per #558            |
| ------------------ | ------------------------------- | ----------------------------------------------- |
| Marine             | Yes — `MarineDesign.ValidateSelf` (single file, single record) | 22 sites, all in `MarineDesign.cs`              |
| Nuclear            | Yes — `NuclearThermalDesign.ValidateSelf`; range checks then re-asserted at solver entry (`NtrCycleSolver`, `BraytonGasLoopSolver`, `FuelPinHeatModel`, etc.) | 48 throws across 9 files (validation + downstream solver guards) |
| Airbreathing       | No — `AirbreathingEngineDesign` is a parameterless monolithic record; field validation lives at solver / contour / objective entry (`RamjetCycleSolver`, `RamjetContour`, `RamjetObjective`, …) | 23 sites across `*CycleSolver` / `*Contour` / `*Objective` / `StationMap` / `AirbreathingFuelTables` |
| Electric Propulsion| No — `ElectricPropulsionEngineDesign` is a monolithic record with field-default sentinels (NaN / 0); field validation lives in cycle-solver / objective / model entry points (`HetCycleSolver`, `MpdCycleSolver`, `MaeckerKovityaArcModel`, …) | ~77 sites across 23 files (`*CycleSolver`, `*Objective`, plasma-state models, propellant tables) |

The 22 Wave-1 internal pillars in `Voxelforge.Core/` (Battery, Pump, Compressor, Stirling, PV, HAWT, Hydro, Refrigeration, Pemfuelcell, PvPanel, Electrolyser × 3, Antenna, Flywheel, Heatpipe, Heat exchanger, Hydrogen storage, Motor, Pressure vessel, Radiator, Reactor, Solar collector, Spar, Stirling, TEG) unanimously follow the *`Design.ValidateSelf`* pattern — 27 of 27 such records carry a public `ValidateSelf()` (`grep -rln "void ValidateSelf" Voxelforge.Core/*/`).

The divergence has different consequences:

1. **`*Design.ValidateSelf` (Marine + Nuclear + Wave-1 internal):** validates the design at *construction or before-first-use* time. Any consumer of a constructed `Design` (optimizer candidate, persistence-deserialised record, hand-written test fixture) can rely on the record being well-formed. The validation surface is one searchable symbol per pillar.

2. **Entry-point validation (Airbreathing + EP):** validates at the boundary of the solver / objective / contour. Constructed designs may be ill-formed but unused designs do not fail. The validation surface is N entry points, none singularly authoritative; readers re-derive the field-range contract from whichever entry the consumer happened to call.

Neither pattern is wrong. Both are testable from the outside; both have shipped working pillars in production. But future contributors will pick whichever pattern they see in the nearest neighbour — Wave-3 EP variants will continue to favour entry-point validation; the next Wave-1 internal pillar will continue to favour `ValidateSelf`. Without a documented decision the drift perpetuates and the per-pillar Definition-of-Done (ADR-026 §4.6) cannot reference a canonical surface.

This ADR documents both patterns, recommends the pragmatic default, and gives future pillars a citation to point at.

## Decision

**D1 — Both patterns are legitimate; selection follows record shape.** A pillar's choice of validation locus is determined by the structure of its design record, not by ambition:

- A pillar whose physics splits across multiple *focused* design records (Nuclear-style — `NuclearThermalDesign` plus per-subsystem records for the Brayton loop, fuel pin geometry, etc.) SHOULD provide `ValidateSelf()` on each focused record. The single-record surface is small; the cost of authoring `ValidateSelf` is bounded by the field count of one focused record; consumers benefit from a record-shape contract that holds at construction time.
- A pillar whose physics rides on a single *monolithic* design record with `Kind`-discriminated fields (Airbreathing-style — one `AirbreathingEngineDesign` whose ramjet fields are NaN-sentineled when `Kind = Turbojet`; Electric Propulsion-style — one `ElectricPropulsionEngineDesign` whose HET fields are 0-defaulted when `Kind = Resistojet`) MAY validate at solver / objective / contour entry points instead. A monolithic-record `ValidateSelf` would have to be `Kind`-switched and would duplicate the per-kind boundary checks the entry points already perform.

The decision rule is structural: **focused record → ValidateSelf at construction; monolithic Kind-switched record → entry-point validation**. Marine + Nuclear satisfy the former; Airbreathing + EP satisfy the latter; no refactor is required of any current pillar.

**D2 — A pillar that adopts entry-point validation MUST funnel through a single canonical entry surface per kind.** The entry-point pattern is only legitimate when *every* path that consumes the design goes through a guarded entry. Concretely, an Airbreathing / EP pillar with N solvers + M objectives must guarantee:

- Every `*CycleSolver.Solve(design, conditions, …)` validates the design's solver-relevant fields up-front.
- Every `*Objective.Build` / `*Objective.Evaluate` validates before delegating to the solver.
- Every `*Contour.Build` / `*Geometry.*` ditto.

A pillar that ships a solver path bypassing the entry-point validation is in latent debt; the fix is either (a) add a guard to the bypassing path or (b) extract a private `ValidateForX` helper the bypassing path can call. Current Airbreathing + EP code largely satisfies this — the #558 migration enumerated 23 + ~77 entry-point validations across the two pillars — but this ADR codifies the *rule*, not just the observation.

**D3 — A future pillar may also adopt a hybrid pattern.** Cheap structural checks (NaN, sign, finite-range) in `ValidateSelf`; expensive cross-coupling checks (`NoseFairing + TailFairing < 1` style; `BypassRatio * CompressorRatio < limit` style) at solver entry. This is the natural extension when a monolithic record has a small subset of fields that are *always* meaningful regardless of `Kind`. Neither Airbreathing nor EP needs the hybrid today, but the pattern is reserved for a future pillar whose monolithic record has, say, mass / power / volumetric-budget fields that are universal across kinds.

**D4 — Per-pillar Definition-of-Done addition.** ADR-026 §4.6's DoD list currently requires "VFD001-VFD006 clean" (extended by ADR-042 to VFD001-VFD015). Add a sibling requirement: **"design-record validation locus documented in pillar README OR in the pillar's primary design record file's header XML comment."** A future contributor reading `MarineDesign.cs` or `AirbreathingEngineDesign.cs` should see, in three lines or fewer, which pattern the pillar follows and where to find the validation guards.

**D5 — No refactor is committed by this ADR.** Airbreathing + EP keep entry-point validation. Marine + Nuclear keep `ValidateSelf`. Wave-1 internal pillars keep `ValidateSelf`. The 100 + entry-point throw sites in Airbreathing + EP and the 27 Wave-1 `ValidateSelf` methods all stay. The decision is documentary: the divergence is a *pattern*, not a *bug*; the pattern is selected by record shape; the per-pillar DoD names the chosen pattern so a reader can find it.

## Consequences

**Positive:**

- Future pillar contributors land on a documented decision rule instead of copying whichever neighbour they read first. The rule is single-axis (record shape — focused vs. monolithic-Kind-switched) and a 30-second test against the new pillar's design record.
- The per-pillar Definition-of-Done extension (D4) makes the chosen pattern discoverable from the design-record file alone. A reader does not have to grep the pillar to find the validation surface.
- No refactor cost. The four pillars proceed unchanged.

**Neutral:**

- The two patterns retain different ergonomics. Marine / Nuclear constructions can throw at the persistence-deserialise step; Airbreathing / EP constructions cannot. A persistence-deserialise that produces an ill-formed Airbreathing / EP record fails at first solver call instead of at construction. Both behaviours are documented and load-bearing for the entry-point-pattern pillars (their `IO/*Persistence.cs` round-trip tests rely on round-tripping designs that are not validated at deserialise).

**Negative:**

- The codebase will continue to ship two validation patterns. A future contributor refactoring the EP monolithic record into per-kind focused records (a plausible Wave-3 + outcome if the Kind-switched field set continues to grow) would pick up an obligation to add `ValidateSelf` per the D1 rule — a small extra step at refactor time.
- The hybrid pattern (D3) is reserved but unused. If no pillar adopts it within two waves it is a YAGNI candidate; revisit with a superseding ADR.

## Alternatives considered

- **Option A — Converge every pillar on `*Design.ValidateSelf`.** Rejected. The empirical motivation is weak: Airbreathing + EP currently work fine without it, and migrating their monolithic records to `ValidateSelf` would either (i) duplicate the entry-point guards already in place, or (ii) require a `Kind`-switched `ValidateSelf` body whose per-`Kind` case overlaps perfectly with the existing per-`Kind` cycle-solver entry. The refactor is mostly motion. The construction-time-validity property is real but not load-bearing for the two affected pillars given they round-trip ill-formed designs through `IO/*Persistence.cs` test fixtures by design.
- **Option B — Document entry-point validation as the canonical pattern for *all* pillars** (i.e. retire `ValidateSelf` everywhere). Rejected. The 22 Wave-1 internal pillars in `Voxelforge.Core/` unanimously use `ValidateSelf` and benefit from a single-symbol-per-pillar validation surface; the 27 such records would all need migration to fan their checks across entry points that, in many cases, are themselves single-method classes. The refactor cost dwarfs Option A's, and the construction-time guarantee is genuinely valuable for the focused-record pillars (an `Antenna.AntennaLinkDesign` consumer can rely on the record being well-formed at construction; an `Antenna.LinkBudgetSolver.Solve` consumer would need to re-derive that guarantee at every solver entry).
- **Option D — Pick the loser of D1's rule case-by-case.** Rejected as cargo-culting. The rule "focused record → ValidateSelf; monolithic-Kind-switched record → entry-point" is structural and falsifiable; case-by-case selection without a rule reproduces the original drift.

## References

- Issue [#588](https://github.com/poetac/voxelforge/issues/588) — discovered during #558 error-idiom migrations.
- Migration PRs:
  [#578](https://github.com/poetac/voxelforge/pull/578) Marine ·
  [#579](https://github.com/poetac/voxelforge/pull/579) Nuclear ·
  [#581](https://github.com/poetac/voxelforge/pull/581) Airbreathing ·
  [#583](https://github.com/poetac/voxelforge/pull/583) EP.
- `Voxelforge.Marine.Core/MarineDesign.cs:184` — canonical `ValidateSelf` for the focused-record pattern.
- `Voxelforge.Nuclear.Core/NuclearThermalDesign.cs` — `ValidateSelf` + downstream re-assertion in `NtrCycleSolver` / `BraytonGasLoopSolver`.
- `Voxelforge.Airbreathing.Core/AirbreathingEngineDesign.cs` — monolithic record without `ValidateSelf`; entry-point guards in `Optimization/*Objective.cs` + `Geometry/*Contour.cs`.
- `Voxelforge.ElectricPropulsion.Core/ElectricPropulsionEngineDesign.cs` — monolithic record without `ValidateSelf`; entry-point guards in `Solvers/*CycleSolver.cs` + `Solvers/*Model.cs` + `Optimization/*Objective.cs`.
- [ADR-026](ADR-026-multi-pillar-coordination.md) §4.6 — per-pillar Definition-of-Done checklist (D4 above extends this).
- [ADR-042](ADR-042-per-pillar-deterministic-marking.md) — sibling per-pillar discipline ADR (`[Deterministic]` marking).
