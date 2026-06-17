# ADR-034 — Electric Propulsion Wave-3 roadmap

**Status:** Accepted (2026-05-12)
**Sprint:** Post-PR #489 follow-on
**Related:** [ADR-026 Multi-pillar coordination](ADR-026-multi-pillar-coordination.md) ·
[ADR-029 Plasma-chamber abstraction](ADR-029-plasma-chamber-abstraction.md) ·
[ADR-029a IPlasmaState promotion](ADR-029a-iplasmastate-promotion.md)

## Context

The EP pillar after PR #489 + PR #497 covers:

- **Wave-1** (Sprint E.0 – E.4): Resistojet electrothermal
- **Wave-2** (PR #473, #477, #479, PR #489): HET (Busch / Goebel-Katz), Arcjet (Maecker-Kovitya), PPT (Solbes-Vondra), GIT (Child-Langmuir), self-field MPD (Maecker)
- **Wave-3 (partial, this branch)**: Applied-field MPD (Sankaran-2004), VASIMR design-scaffold + reserved enum slot (physics deferred to EP.W4 phase 2)

The pillar carries 6 implemented kinds + 1 reserved (VASIMR). [ADR-029](ADR-029-plasma-chamber-abstraction.md) deferred the "Wave-3" naming explicitly; ADR-034 closes that loop by laying out the remaining Wave-3 work and the order it should land in.

## Decision

**D1. Wave-3 scope.** Three additional EP variants, each at a different physics maturity:

| Kind | Status | Effort | Trigger |
|---|---|---|---|
| **Applied-field MPD (LiLFA-style)** | **Shipped on branch PR #497** (Sprint EP.W3.AF) | — | — |
| **VASIMR** | Design scaffold + reserved slot shipped (PR #497, EP.W4 phase 1); physics deferred | ~3-4d | EP.W4 phase 2 — issue [#498](https://github.com/poetac/voxelforge/issues/498) |
| **FEEP** (field-emission electric propulsion) | Reserved enum slot not yet allocated | ~2-3d | Demand-driven (no specific trigger) |
| **Helicon Double Layer Thruster (HDLT)** | Reserved enum slot not yet allocated | ~2-3d | Demand-driven |
| **Gridded ion w/ ion-cyclotron heating** | Not in scope (use VASIMR instead) | — | — |

**D2. Schema-version policy.** EP schema is at **v10** after this branch (Wave-2 closed at v6, Wave-3 brought it through three bumps: AF-MPD v7, VASIMR v8, FEEP+HDLT v10 — combined in one bump). Subsequent Wave-3 sprints follow the identity-migration pattern: each new kind adds 3-6 init-only design fields with NaN defaults; schema bump per kind; no breaking-change migrations expected through Wave-3 close.

Schema-bump trail across this branch:

- **v6 → v7** — EP.W3.AF: applied-field MPD fields (`AppliedFieldStrength_T`, `AppliedFieldThrust_N`).
- **v7 → v8** — EP.W3.VASIMR scaffold: 3 init-only design fields (`VasimrHeliconPower_W`, `VasimrIchPower_W`, `VasimrMassFlow_kgs`).
- **v8 → v10** — EP.W3.FEEP + EP.W3.HDLT scaffolds (combined): 3 FEEP fields + 3 HDLT fields landed in one bump; v9 was the FEEP-only intermediate transiently before the HDLT scaffold landed on the same branch.

Next reserved slots (post-branch pickup):

- **v10 → v11** — VASIMR physics activation (EP.W4 phase 2): no new design fields, the v8 scaffold already covers them. The bump signals "VASIMR now has working physics" rather than scaffold-only.
- **v11 → v12** — FEEP physics activation (EP.W5): if it needs Mair-Lozano-specific calibration constants beyond the v9-introduced field block.
- **v12 → v13** — HDLT physics activation (EP.W6): same shape.

**D3. EngineFamilyMask bit reservations.** Used bits in the EP family group:

| Bit | Allocation |
|---|---|
| 1 << 7  | `ElectricResistojet` (Wave-1) |
| 1 << 8  | `ElectricHallEffect` (Wave-2) |
| 1 << 9  | `ElectricGriddedIon` (Wave-2) |
| 1 << 10 | `ElectricArcjet` (Wave-2) |
| 1 << 11 | `ElectricMpd` (Wave-2, self-field) |
| 1 << 12 | `ElectricPpt` (Wave-2) |
| 1 << 15 | `ElectricVasimr` (Wave-3, EP.W4 phase 1 reserved this branch) |
| **1 << 16** | **Reserved for FEEP** |
| **1 << 17** | **Reserved for HDLT** |
| **1 << 18** | **Reserved for AF-MPD as separate bit** (only if EP.W3.AF gates ever need to discriminate from self-field MPD; current design reuses `ElectricMpd` since the AF augmentation flag lives on the design record, not the kind) |

Bits 13 + 14 belong to Marine (Marine = 0x2000, MarineHull = 0x4000) per ADR-026.

**D4. Validation-tolerance ladder.** Wave-3 fixtures inherit the ADR-029-D4-generalised tolerance ladder:

| Kind | Thrust ± | Isp ± | Notes |
|---|---|---|---|
| Resistojet (Wave-1, calibration-grade) | 10 % | 8 % | Tight; closed-form |
| HET (Wave-2) | 20 % | 15 % | Busch fit has cluster spread |
| Arcjet (Wave-2) | 20 % | 15 % | Same envelope as HET |
| PPT (Wave-2) | 25 % | 15 % | Solbes-Vondra empirical |
| GIT (Wave-2) | 20 % | 15 % | Child-Langmuir is closed-form |
| MPD self-field (Wave-2) | 25 % | 15 % | Bare-Maecker underpredicts real ~50 % |
| **MPD applied-field (Wave-3 EP.W3.AF)** | **35 %** | **15 %** | **Sankaran k_af coefficient cluster spread** |
| **VASIMR (Wave-3 EP.W4)** | **25 %** | **15 %** | **VX-200 anchor; variable-Isp invariant tested separately** |
| **FEEP (Wave-3+)** | **20 %** | **10 %** | **Closed-form Mair-Lozano; tighter than thermal kinds** |
| **HDLT (Wave-3+)** | **30 %** | **20 %** | **Empirical; large spread across published campaigns** |

**D5. IPlasmaState promotion track.** The abstraction was promoted to `Voxelforge.Core/Plasma/` after the rule-of-three fired on HET + Arcjet + PPT ([ADR-029a](ADR-029a-iplasmastate-promotion.md)). Wave-3 additions (AF-MPD, VASIMR, FEEP, HDLT) all become `IPlasmaState` consumers. The concrete records stay pillar-local:

- `AfMpdPlasmaState` — reuses `MpdPlasmaState` with the W3 init-only fields (`AppliedFieldStrength_T`, `AppliedFieldThrust_N`, `SelfFieldThrust_N`); no new type, EP.W3.AF used the same record for backwards compat.
- `VasimrPlasmaState` — new type when EP.W4 phase 2 ships. Carries `IonTemperature_eV`, `BeamCurrent_A` = ion saturation current, `MagneticNozzleExpansionRatio`.
- `FeepPlasmaState` (deferred) — would carry `EmitterTipTemperature_K`, `BeamCurrent_A`, `IndiumIonizationFraction`.
- `HdltPlasmaState` (deferred) — would carry `DoubleLayerStrength_V`, `IonExitVelocity_ms`, `PlumeDivergenceHalfAngle_rad`.

The rule-of-three is already met; no further interface-promotion gating applies.

## Consequences

**Positive:**
- Wave-3 EP closure becomes mechanical: VASIMR phase 2 is the next sprint, with FEEP + HDLT as demand-gated follow-ons.
- Bit reservations + schema-version sequence pre-allocated so no future PR has to renegotiate the layout.
- Validation-tolerance ladder is single-source-of-truth for all 9+ EP fixture families.

**Negative:**
- Reserving bits 16 + 17 for FEEP + HDLT without firm demand could waste mask space. Mitigation: 32-bit mask has ample room; reuse policy if a new physics class wants bit 16 before FEEP demand surfaces.
- FEEP + HDLT physics are less universally-validated than MPD/GIT — choosing the right cluster-anchor reference matters more. Mitigation: choose anchor at scaffold time, document in the per-fixture file header (ADR-029 D4 generalised pattern).

## Alternatives considered

**A1. Lift AF-MPD to a separate kind enum value + bit.** Reject — the W3.AF augmentation is parameterized over the design record's `MpdAppliedFieldStrength_T` (NaN → self-field only), not a kind discriminator. Adding a separate kind would force callers to branch on Wave-3 enablement; the current design flags Wave-3 via finite B-field value.

**A2. Defer Wave-3 entirely; ship Wave-4 (NTR-electric coupling) first.** Reject — NTR-electric coupling is cross-pillar work that needs both the EP physics and the Nuclear physics mature. EP Wave-3 closure is a prerequisite.

**A3. Activate VASIMR physics in this branch instead of just the scaffold.** Reject — the physics involves three coupled stage models (helicon + ICRH + magnetic nozzle) with non-trivial validation work; deferring to EP.W4 phase 2 keeps the branch scope focused.

## Implementation status

- **EP.W3.AF (applied-field MPD)** — shipped on this branch (PR #497).
  - Schema v6 → v7 identity migration.
  - 2 new init-only design fields.
  - 2 new gates.
  - LiLFA Polk 1991 + Princeton X9 + Stuttgart ZT-1 fixtures.
- **EP.W4 phase 1 (VASIMR scaffold)** — shipped on this branch (PR #497).
  - Enum slot `Vasimr = 7`.
  - `EngineFamilyMask.ElectricVasimr = 1 << 15`.
  - 5 new init-only design fields with NaN defaults.
  - Schema v7 → v8 identity migration.
  - Physics dispatch throws `NotImplementedException` with EP.W4 marker.
- **EP.W4 phase 2 (VASIMR physics)** — issue [#498](https://github.com/poetac/voxelforge/issues/498).
- **EP.W5 (FEEP scaffold)** — not yet scoped; demand-gated.
- **EP.W6 (HDLT scaffold)** — not yet scoped; demand-gated.

## Follow-ons

- **Wave-3 closure tracker** — meta-issue covering all the per-kind EP.W3 sub-sprints with a checklist. Useful once VASIMR phase 2 starts.
- **VX-200 published-engine validation fixture** — paired with EP.W4 phase 2 (shipped as part of #498 work).
- **Wave-4 cross-pillar coupling (NTR-electric)** — once Wave-3 closes, the NEP (nuclear electric propulsion) coupling between Nuclear + EP becomes the next obvious cross-cutting work. ADR-035 (TBD).
