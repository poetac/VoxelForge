# ADR-036 — Validation-tolerance ladder (canonical reference)

**Status:** Accepted (2026-05-12)
**Sprint:** Post-PR #489 follow-on
**Supersedes:** [ADR-029 D4](ADR-029-plasma-chamber-abstraction.md) (Wave-2 EP tolerance subset)
**Related:** [ADR-034 EP Wave-3 roadmap](ADR-034-electric-propulsion-wave-3-roadmap.md) ·
[`published-engine-validation.md`](../published-engine-validation.md)

## Context

Voxelforge ships published-engine validation fixtures across every pillar — 24 rocket, 12 electric propulsion, 6 airbreathing, 7 marine, 1 nuclear (post-PR-#497 estimate). Each fixture has a `±X%` tolerance band per measured quantity (thrust, Isp, mass flow, throat radius, etc.).

The tolerance bands have been chosen ad hoc across multiple sprints. Three sub-ADRs that touched the topic:

- **ADR-029 D4** — "Wave-2 plasma-chamber abstraction" specified ±20% thrust / ±15% Isp for HET; later sprints (EP.W2.AJ, EP.W2.PPT, EP.W2.GIT, EP.W2.MPD) added their own bands without a single authoritative table.
- **ADR-034 D4** — "EP Wave-3 roadmap" extended the ladder to AF-MPD / VASIMR / FEEP / HDLT.
- **`published-engine-validation.md`** — captures the rocket-pillar bands per fixture, with the per-fixture `Tolerances` record.

Without a single canonical reference, the bands drift: new fixtures pick whatever felt right; reviewers struggle to know if `±35%` is appropriate.

This ADR consolidates the bands into one table + documents the rationale + the rules for setting bands on future fixtures.

## Decision

**D1. Canonical tolerance table.** Bands per pillar + variant + measured quantity:

### Rocket pillar

| Variant | Thrust | Isp | Mass flow | Throat radius |
|---|---|---|---|---|
| Regen chamber, calibrated (RL10A-3-3A, J-2, Vinci, J-2X) | ±5–8% | ±5–9% | ±5% | ±5–7% |
| Regen chamber, gas-generator (Merlin-1D, NK-33, F-1) | ±5–20% | ±5–20% | ±5–10% | ±7–15% |
| Regen chamber, staged combustion (Raptor 1/2, RD-180/170, BE-4) | ±20% (default) | ±20% (default) | ±10% (default) | ±15% (default) |
| Aerospike (none flown today) | ±25% | ±20% | — | ±20% |

### Air-breathing pillar

| Variant | Thrust | Isp / TSFC | Notes |
|---|---|---|---|
| Turbojet (J79, J33) | ±15% | ±10% | Per fixture in PublishedEngineFixtures.cs |
| Turbofan (F404, F100) | ±20% | ±15% | Bypass ratio adds 1 dim of uncertainty |
| Ramjet | ±20% | ±15% | Inlet recovery + mode |
| Scramjet / RBCC | ±25% | ±20% | Limited published data |
| Pulsejet (V-1 / Argus 109-014) | ±25% | ±20% | Closed-form 1-D fit |

### Electric propulsion pillar

| Variant | Thrust | Isp | Power |
|---|---|---|---|
| Resistojet (MR-501B) | ±10% | ±8% | ±5% (exact arithmetic) |
| HET (BPT-4000, SPT-100) | ±20% | ±15% | ±5% |
| Arcjet (MR-509 ATOS, MR-510) | ±20% | ±15% | ±5% |
| PPT (Aerojet EO-1, LES-6) | ±25% impulse-bit | ±15% | ±5% |
| GIT (NSTAR, NEXT-C) | ±20% | ±15% | ±2% beam power |
| MPD self-field (NASA-Lewis SF-MPD) | ±25% | ±15% | — |
| **MPD applied-field (LiLFA, Princeton X9, Stuttgart ZT-1)** | **±35%** | **±15%** | — |
| **VASIMR** (deferred, EP.W4 phase 2) | **±25%** | **±15%** | — |
| **FEEP** (deferred, EP.W5 phase 2) | **±20%** | **±10%** | — |
| **HDLT** (deferred, EP.W6 phase 2) | **±30%** | **±20%** | — |

### Marine pillar

| Variant | Resistance | Wetted area | Notes |
|---|---|---|---|
| Displacement AUV (REMUS-100, -600, -6000, Bluefin-21) | ±40% [a] | ±10% | Hoerner / Myring physics |
| Planing (Savitsky) | ±30% | ±15% | Empirical |
| Displacement-surface (Holtrop-Mennen simplified) | ±25% | ±10% | Full Holtrop drops the form factors |
| Semi-displacement (Holtrop high-Fn) | ±30% | ±15% | Cluster spread is wider |

[a] *Widened 2026-05-17 from initial ±25% (#755). The bare-cylinder Hoerner correlation (Hoerner 1965 §6-2) has documented ±35–40% scatter at Re_L < 10⁷ across the REMUS-100/600/6000/Bluefin-21 cluster — sensitivity to laminar→turbulent transition position on the nose fairing, surface roughness, and appendage drag, none of which the wetted-area model captures. Per D3.2 each fixture cites this in its file header. Tightening would require Holtrop-Mennen form-factor decomposition or a per-fixture empirical-cluster calibration sprint.*

### Nuclear pillar

| Variant | Thrust | Isp | Notes |
|---|---|---|---|
| NTR (NERVA NRX-A6) | ±5% | ±5% | Single fixture; calibrated tight |
| NTR bimodal Brayton (deferred Wave-2+) | ±10% | ±10% | Twin-output variant |

**D2. Rationale band assignments.** Three categories of physics drive the spread:

| Category | Examples | Typical band |
|---|---|---|
| **Closed-form analytical** | Child-Langmuir (GIT), Fowler-Nordheim (FEEP), ITTC-1957 friction (Marine AUV) | **±10–20%** |
| **Semi-empirical fit** | Bartz throat heating, Busch HET discharge, Maecker-Kovitya arcjet, Solbes-Vondra PPT, Savitsky planing | **±20–25%** |
| **Loose empirical cluster** | Self-field MPD bare-Maecker (underpredicts ~50%), AF-MPD k_af spread, Holtrop simplified, HDLT double-layer, Hoerner-class AUV drag at Re_L < 10⁷ (transition + roughness + appendage scatter) | **±25–40%** |

A band of **±X%** means: published target T_pub ± X% must contain the model's prediction T_model. If the model is consistently 10% high but within band, fine. If the band is ±35%, the model is allowed to be wildly different — keep it tight unless physical cluster spread justifies otherwise.

**D3. Rules for new fixtures.**

1. **Default to the variant's existing band** in the table above. Don't widen unless there's evidence.
2. **Widening above ±25% needs documentation** in the fixture file header explaining which cluster-spread effect drives the loose band (e.g. "Sankaran k_af coupling coefficient spans 0.05–0.30 across the LiLFA / Princeton X9 / Stuttgart ZT-1 campaigns; band absorbs that").
3. **Tightening below ±10% needs calibration** — either the fixture matches the model's closed-form prediction exactly (e.g. Beam power = V_b × J_b is exact arithmetic), or the model has been calibrated against the specific cluster anchor (e.g. NERVA NRX-A6).
4. **A test that consistently lands at the band edge is a model bug, not a tolerance bug.** If three independent fixtures all sit at the +20% edge of their ±20% bands, the model is systematically biased and the calibration needs updating.

**D4. Mass-flow + geometry tolerance follows thrust.** As a rule of thumb, `MassFlowToleranceFraction ≈ ThrustToleranceFraction × 0.6` (per the existing `Mr509Atos` + `Bpt4000` + `Nstar` fixture conventions). Geometry tolerances (throat radius, chamber diameter) sit at `ThrustToleranceFraction × 0.6 – 0.8` per the rocket-pillar PublishedEngineFixtures.cs default.

**D5. Cluster-anchor citation requirement.** Every fixture file header must cite the specific published reference + paragraph / table number / figure number that the target values come from. Vague citations ("Sutton 9e §6.5") that span a chapter are insufficient; pinpoint citations ("Sutton 9e Table 6-4, p. 232") let a reviewer find the exact data.

## Consequences

**Positive:**
- One table to consult when setting bands on a new fixture.
- Reviewer can flag drift: "your fixture says ±40% — that's outside the ADR-036 D2 cluster-anchored ladder; either tighten or add the rationale per D3.2."
- Future fixture audits can mechanically check that bands sit in the documented ranges.

**Negative:**
- The table grows as new variants ship; this ADR is "living" in the sense that band assignments for future variants must be added by the sprint that ships them. Mitigation: link this ADR from every fixture file header; the link discourages copy-paste without thought.
- The "physical cluster spread" rationale in D2 is subjective. A reviewer may argue that ±25% is too loose for a particular fixture; the rule is "default to the table, widen only with citation." Document the widening; don't argue it ad hoc.

## Alternatives considered

**A1. Per-pillar tolerance ADRs.** Reject — fragments the ladder across 5+ docs and forces maintenance synchronization. The single-table approach scales.

**A2. Programmatic default (`DefaultTolerances` constant per variant).** Already partially in place via `EpsilonFraction.DefaultTolerances` in `PublishedEngineFixtures.cs`. The ADR makes the values explicit + reviewer-checkable; the code constants don't replace the ADR.

**A3. Allow per-fixture bands without policy.** Reject — it's what we had, and the resulting drift is what motivates this ADR.

## Implementation status

- Tolerance table: this ADR (above).
- Per-pillar fixture file headers: already cite their tolerance band per existing convention (see `Mr509Atos.cs`, `Lilfa_Polk1991.cs`, etc.).
- Cross-reference: link added from `published-engine-validation.md` (deferred to a follow-on docs PR if not in this branch).
- ADR-029 D4 is *not* removed — that decision still stands for Wave-2 EP; this ADR is its successor with broader scope.

## Follow-ons

- **ADR-036 supplement** (when needed) — tolerance band for the next pillar (e.g. a hypothetical Aerostructures pillar's wing-spar tolerance band).
- **`published-engine-validation.md` cross-link** — small docs PR adding "see ADR-036 for the canonical ladder" near the top.
- **Fixture-audit CI check** (eventually) — programmatic check that no fixture's `EpsilonFraction.IspS_Frac` (or equivalent) sits outside the documented range without a `[TolerancesPerAdr037]` attribute justifying it. Demand-gated.
