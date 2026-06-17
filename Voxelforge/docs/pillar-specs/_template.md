# Pillar spec — `<Variant Name>`

**Status:** Draft / Accepted / Shipped (Sprint X / PR #N).
**Family:** `<rocket | airbreathing | ...>` (per [`family-allocations.md`](../family-allocations.md)).
**Variant kind:** `<EnumName>.<Value> = <int>` (e.g. `AirbreathingEngineKind.Pulsejet = 8`).
**Sprint:** `<Sprint code, e.g. R2>`.
**Authored:** YYYY-MM-DD.
**Related ADRs:** ADR-026 (multi-pillar coordination), ADR-019 (gate registry), ADR-022 (schema versioning), ADR-007 (Smoothen 25 % cap, if voxel work).

## Overview

One paragraph: what the engine is, what physical principle it operates on, why it's being added now, what use cases it addresses. Cite the closest existing variant as the structural template (e.g. "structurally mirrors `RamjetCycleSolver` — no rotating machinery, same `IAirbreathingCycleSolver` contract").

## Physics model

Describe the cycle solver in one to three paragraphs. Required content:

- **Thermodynamic basis** — Brayton, Humphrey, Otto, Rankine, etc.
- **Station-by-station description** — list the SAE AS755 (or analogous) station numbering used; explicitly note which stations are NaN / degenerate for this variant.
- **Closed-form correlations.** Every correlation cited inline with `[Author year, §X.Y eq Z]`. Examples: `[Foa 1960, §11.2 eq 11-3]`, `[Mattingly §5.3]`, `[Glassman §3]`.
- **Simplifying assumptions.** Spell out the load-bearing approximations (constant-property gas, perfect expansion, lumped 0-D, etc.) so a future fix-up sprint knows what's open.
- **Dependencies.** Which existing helpers / fuel tables / atmosphere models are reused (e.g. `AirbreathingFuelTables.Lookup`, `StandardAtmosphere.At`).

## Design variables

List each new field on the pillar's design record. Include any reused fields if the semantics differ for this variant.

| Field | Type | Units | Default | Rationale |
|---|---|---|---|---|
| `<FieldName>` | `<C# type>` | `<units>` | `<default>` | What it represents and why it can't reuse an existing field. Cite the equation that consumes it. |

If schema bumps: note the chain (e.g. `v5 → v6, additive identity`).

## Feasibility gates

| ConstraintId | Severity | Category | Triggers when | Source |
|---|---|---|---|---|
| `<GATE_ID>` | `Hard / Advisory` | `PhysicsLimit / EmpiricalBand / ManufacturabilityFloor / RegressionGuard` | Plain-English condition | Reference (Foa §X, Glassman §Y, NACA TM ZZZZ, etc.) |

Notes:

- New gates self-register from `<Variant>Gates.RegisterAll()` against the pillar's registry instance.
- Gates inherited from the pillar baseline (e.g. `COMBUSTOR_BLOWOUT_LEAN` for any airbreathing variant) are not re-listed; only **net-new** gates appear here.
- ConstraintIds use SHOUTING_SNAKE_CASE per ADR-026 §3.

## Voxel geometry

Describe the voxel-builder pipeline for this variant. Required content:

- **Contour shape.** Axis-symmetric? Revolved? Sketch the section list (e.g. "intake horn → diffuser → combustor → tailpipe → exit").
- **SDF primitives.** Which existing primitives are reused (`RevolvedContourImplicit`, `CylinderImplicit`, etc.).
- **Boolean topology.** Outer shell + inner cavity + (optional) sub-features. Order matters per CLAUDE.md pitfall #2 (no BoolSubtract through TPMS).
- **Smoothen budget.** Max `d` per ADR-007 (25 % of minimum wall thickness).
- **LPBF analysis.** Whether `LpbfPrintabilityAnalysis.Run` is invoked; any variant-specific overhang / drain-path concerns.
- **Cross-platform discipline.** Voxel tests live in the pillar's `*.Tests` project as subprocess tests if the test project is not net9.0-windows (per CLAUDE.md pitfall #8 follow-on).

## LPBF printability

Anything variant-specific about additive manufacturing of the voxel output. Examples: thin acoustic-resonator dimensions, no-moving-parts simplification, support-strategy concerns. If nothing is variant-specific, state "Inherits pillar baseline; no additional concerns."

## Validation fixtures

| Fixture | Reference engine | Tolerance bands | Citations |
|---|---|---|---|
| `<FixtureName>` | `<Real flying engine + variant>` | `±X% thrust / ±Y% Isp / ±Z% station T` | `<Open-literature sources, NACA TM, textbook>` |

Each fixture lives in the pillar's `Tests/Validation/` and is added to the pillar's fixture catalogue (e.g. `AirbreathingFixtures.All`). Fixture-anchored tests in the pillar's `Validation/` test class.

## Verification checklist

The variant's PR series walks the [ADR-026 §4.5 Definition-of-Done checklist](../ADR/ADR-026-multi-pillar-coordination.md#45-definition-of-done-checklist) plus these variant-specific items:

- [ ] Cycle solver registered in pillar's `<Pillar>CycleSolvers.BuildRegistry()`.
- [ ] Variant kind value reserved in `family-allocations.md` and (if status flipped) the table updated to Live.
- [ ] Schema bump (if applicable) lands in same PR as field addition; identity migration test passes.
- [ ] New gates have unit tests (one nominal-pass, one threshold-fires).
- [ ] Validation fixture tests pass with stated tolerances.
- [ ] Voxel build (if applicable) produces non-empty output; LPBF analysis runs without overhang violations on the canonical fixture.
- [ ] CHANGELOG entry under `## Unreleased`.

## References

Inline citations to the textbooks / NACA reports / vendor data the spec relies on. Keep concrete — `[Foa 1960, §11.2]` rather than `[Foa 1960]`.
