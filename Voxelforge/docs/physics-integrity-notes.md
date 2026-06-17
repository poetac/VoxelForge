# Physics integrity notes (2026-04-27)

**Purpose.** Catalogs every place
in the codebase where the physics model:

1. Has been **calibrated against inferred (not measured) targets** ("yellow flags"), or
2. Contains a **known simplification** that is documented but not yet fixed.

This document exists because the post-PR-#87 feasibility-audit cascade
(PRs #72-#90) shipped 16 PRs of model improvements and calibrations.
Some of those calibrations involved **reverse-engineering parameter
values to match published-engine outcomes** — a defensible practice
when literature data is the best ground truth available, but one that
must be disclosed to prevent the model from drifting toward "passes
because we tuned it to pass."

When evaluating a future calibration sprint, consult this doc to:
- Identify which knobs are already calibration-tuned (avoid re-tuning
  without re-justification).
- Identify which simplifications are next-up for principled fixes.
- Trace the citation provenance for each calibration constant.

---

## 🟡 Yellow flags — calibrated to inferred targets

### YF-1 — Sprint E: Stechman β reverse-engineered ✅ RESOLVED 2026-04-27

**Status.** **Shipped in physics-integrity-bundle-1 (PR #92).** Bundle-1
fixed the upstream bugs (ID-1 + ID-2) and verified that β = 0.03 still
produces η in the production-class target band, this time from
principled physics (real fuel density + real chamber gas velocity)
rather than tangled-cancellation. The reverse-direction discipline
test `FilmCoolingPublishedEngineCalibrationTests` locks in the
target band [0.30, 0.55] for the four production-class presets
(merlin / rl10 / aerospike / pintle). Pressure-fed-small excluded
as flagged separately under "small-thruster regime" follow-up.

**Resolution audit:**
- Pre-bundle-1 η measurements (β = 0.03, default density 10 kg/m³,
  default u_g = 50 m/s):
  - merlin 0.36, rl10 0.31, aerospike 0.26, pintle 0.19
- Post-bundle-1 η measurements (β = 0.03, real density per propellant,
  u_g from M=0.1 × c_chamber):
  - merlin 0.525, rl10 0.366, aerospike 0.442, pintle 0.383
- Multi-chain SA × 16 × 100-iter feasibility-rate impact:
  - merlin: 20/1050 → **139/1149** feasible (7× more, first iter 181 → 48)
  - aerospike: 2/1020 → **20/1024** feasible (10× more)

The original concern about "tangled cancellation" was real but
ironic: β = 0.03 was *coincidentally* the right value once the
upstream bugs were fixed. The fix is now physics-justified rather
than tuned-to-target.

**File.** [`Voxelforge.Core/Optimization/AutoSeeder.cs`](../../Voxelforge.Core/Optimization/AutoSeeder.cs) — β = 0.03 unchanged from PR #88.

**Discipline test.** [`Voxelforge.Tests/FilmCoolingPublishedEngineCalibrationTests.cs`](../../Voxelforge.Tests/FilmCoolingPublishedEngineCalibrationTests.cs) — pins each preset's η at peak-heat-flux station inside [0.30, 0.55].

---

### YF-2 — Sprint M: Coax/Showerhead mixingEff calibration

**File.** [`Voxelforge.Core/HeatTransfer/InjectorFaceThermal.cs`](../../Voxelforge.Core/HeatTransfer/InjectorFaceThermal.cs) (line ~95, `MixingLayerEffectivenessFor`).

**The constant.** `Coax = 0.65` and `Showerhead = 0.65` in
`MixingLayerEffectivenessFor(elementType)`.

**Why this is a yellow flag.** Same pattern as YF-1. The 0.65 value
was chosen by reverse-engineering: "merlin face T should be
700-900 K per Merlin documentation, our model says 1244 K, so
mixingEff needs to be ~0.65 to bring it into range." The inversion
target (Merlin face T ≈ 800-900 K) is inferred from public engine
descriptions, not measured.

Mitigating factors:
- The 0.50 pre-Sprint-M value was already documented as a placeholder
  ("PR #79 baseline / calibration-grade").
- The face thermal model itself has a documented ±200 K accuracy band.
- The 0.65 value aligns Coax with ImpingingDoublet (0.65), which is
  a defensible cross-element consistency.

So this is a calibration of a *known calibration parameter* against
a *known empirical target*, not a model dumb-down.

**Recommended fix.** Schedule Sprint T2.3 (CFD validation loop) per
the optimization-infrastructure roadmap. CFD-derived face thermal
distributions would let us compute mixingEff per element type from
physics rather than inferring from outcomes.

**Effort.** ~3-4 days for Sprint T2.3.

**Risk.** LOW immediate. The 0.65 value brings the model closer to
real engine data, just via a soft path (calibration not derivation).

**Status.** Documented in source (line ~108 of InjectorFaceThermal.cs).
Not strictly broken; could be improved.

---

### YF-3 — Z3-F4 Mach-attenuation calibration (PR #230, 2026-04-29)

**File.** [`Voxelforge.Core/HeatTransfer/InjectorFaceThermal.cs`](../../Voxelforge.Core/HeatTransfer/InjectorFaceThermal.cs) — `MixingLayerEffectivenessFor(string?, double)` overload + the three constants
`ChamberMachReference = 0.10`, `ChamberMachAttenuationSlope = 0.5`,
`MinMachAttenuatedFactor = 0.5`.

**The constants.** Linear attenuation of the per-element-type mixing-
layer effectiveness above M = 0.10:
`η(M) = η_base · max(1 − 0.5·(M − 0.10), 0.5)`. At M = 0.10 the factor
is 1.0; at M = 0.50 the factor is 0.80; floored at 0.50.

**Why this is a yellow flag.** Same pattern as YF-1 (Stechman β) and
YF-2 (Coax mixingEff). The slope and floor were chosen for
defensible-direction shifts on small-ε_c designs, not derived from
CFD or empirical face-thermal data. The reference Mach (0.10) is
defensible (Sutton 9e §3.3 typical chamber Mach band 0.10-0.30 for
ε_c ≥ 5); the attenuation slope and floor are tuning knobs.

Mitigating factors:
- The default 0 on `InjectorFaceGeometry.ChamberMach` keeps the legacy
  constant-mixing-eff path bit-identically. The Mach-aware path only
  fires when `RegenGenerationResult.ToInjectorFaceGeometry` populates
  it from station-0 area ratio (production flow).
- Direction is defensible (thicker mixing layer at higher M → reduced
  film protection).
- Floor (0.5) prevents collapse to η = 0 on pathological inputs.

**Recommended fix.** Same as YF-2 — Sprint T2.3 (CFD validation loop,
issue [#160](https://github.com/poetac/voxelforge/issues/160)) would
let the slope and floor be derived from real face-thermal CFD instead
of inferred from outcomes.

**Status.** Documented in source. Not strictly broken; could be
improved.

---

## A note on `file:line` references in this document

This file cites source locations as `\`File.cs:N\`` —
markdown link text containing a line number. **Those line numbers
are best-effort snapshots at the time the entry was written.** The
codebase evolves; line refs drift. The path inside the link
(`../../Voxelforge.Core/HeatTransfer/InjectorFaceThermal.cs`)
remains valid via the file move, but the `:N` suffix will go stale —
particularly for `[SHIPPED]` findings where the original bug location
was usually rewritten by the fix.

When updating an entry, search the file by the surrounding text
(symbol name, distinctive substring) rather than the line number.
Issue [#237](https://github.com/poetac/voxelforge/issues/237) tracked
a one-shot pass to refresh the most blatantly drifted refs;
further drift is expected and not actionable per-cite.

---

## 🔴 Identified simplifications (ID-X) — not introduced by recent
cascade, but discovered or surfaced during the audit

### ID-1 — FilmCooling.Compute default film density 10 kg/m³ ✅ RESOLVED 2026-04-27

**Status.** **Shipped in physics-integrity-bundle-1 (PR #92).** Caller
at [`RegenCoolingSolver.cs:282`](../../Voxelforge.Core/HeatTransfer/RegenCoolingSolver.cs) now passes the real fuel density at injection conditions
via `fluid.GetState(inp.CoolantInletTemp_K, inp.CoolantInletPressure_Pa).Density_kgm3`.
The default `10 kg/m³` in `FilmCooling.Compute`'s signature is
preserved for back-compat with synthetic test fixtures but no longer
used by the production code path.

**Pre-fix vs post-fix density values used in the regen solver:**

| Propellant | Pre-fix | Post-fix (real) |
|---|---:|---:|
| LOX/CH4 | 10 | ~430 (LCH4 cold liquid) |
| LOX/H2 | 10 | ~70 (LH2 supercritical at 16 MPa) |
| LOX/RP1 | 10 | ~810 (RP-1 ambient) |

Joint resolution with YF-1 (Stechman β) preserved η in target band.

---

### ID-2 — RegenCoolingSolver constant chamber gas velocity 50 m/s ✅ RESOLVED 2026-04-27

**Status.** **Shipped in physics-integrity-bundle-1 (PR #92).** Replaced
the constant `u_g = 50.0` with `M_chamber × c_chamber` where
`M_chamber = 0.1` (typical LRE chamber Mach per Sutton 9e §3.3) and
`c_chamber = sqrt(γ_chamber · R · T_c)` from existing `inp.Gas` fields.

For the canonical bench-sa presets this gives:
- LOX/CH4 (merlin / aerospike / pintle): u_g ≈ 130 m/s (was 50)
- LOX/H2 (rl10): u_g ≈ 158 m/s (was 50)
- LOX/RP1 (pressure-fed-small): u_g ≈ 130 m/s (was 50)

The chamber Mach itself is still a soft assumption (M = 0.1
constant); follow-up could derive M from the contour station 0
area ratio if PH-4 station-by-station propellant tables surface it.

---

### ID-3 — TurbopumpSizing single shared dischargePressure for both pumps ✅ RESOLVED 2026-04-27

**Status.** **Shipped in physics-integrity-bundle-2 (PR #93).** Added
`oxDischargePressure_Pa` parameter to
[`TurbopumpSizing.Size()`](../../Voxelforge.Core/FeedSystem/TurbopumpSizing.cs)
(default 0 = back-compat to legacy shared-discharge behavior). Caller
[`SizeTurbopumpFor`](../../Voxelforge/Optimization/RegenChamberOptimization.cs)
passes `max(Pc × 1.2, 0.5 MPa)` for the OX pump — chamber pressure
plus typical 20 % injector ΔP per Huzel & Huang §3.2, with a low-Pc
floor for small thrusters.

**Pre-fix vs post-fix on RL10 (Pc 4 MPa, ClosedExpander, post-Sprint-F1):**

| Pump | Pre-bundle-2 head | Post-bundle-2 head |
|---|---:|---:|
| Fuel | (16.5 MPa - 1.5 MPa) / 70 / 9.81 ≈ 21,800 m | unchanged |
| Ox   | (16.5 MPa - 1.5 MPa) / 1140 / 9.81 ≈ 1,340 m | (4.8 MPa - 1.5 MPa) / 1140 / 9.81 ≈ 295 m |

OX pump head rise dropped 4.5× — matches real RL10's ~5 MPa OX discharge
(2.8× lower than fuel). The expander still has comfortable margin
(5704 kW available vs reduced required shaft power), so feasibility
isn't affected, but the RequiredShaftPower number is now physically
meaningful instead of inflated by the shared-discharge approximation.

**Discipline tests.** [`Voxelforge.Tests/TurbopumpOxDischargeBundle2Tests.cs`](../../Voxelforge.Tests/TurbopumpOxDischargeBundle2Tests.cs):
- `DefaultOxDischarge_PreservesLegacyBehavior` — back-compat regression
  guard.
- `ExplicitOxDischarge_ProducesLowerOxShaftPower` — pins the ≥ 30 %
  ox-shaft-power drop when the new param is supplied.
- `ExplicitOxDischarge_RecordsCorrectDischargeOnOxPump` — pins the
  routing so future drift is caught.
- `ExpanderCycle_OxPumpNotOverSpecdByFuelDischarge` — pins the RL10-
  class scenario (5× Pc fuel discharge with 1.2× Pc OX) to make sure
  the bug doesn't reappear.

---

### ID-4 — Sprint G' chamber-Pc startup floor dropped when gasGamma > 0

**File.** [`Voxelforge.Core/Structure/StructuralCheck.cs`](../../Voxelforge.Core/Structure/StructuralCheck.cs) (post-Sprint-G' on PR #90; not yet on `main`).

**The simplification.** When `gasGamma > 0`, the formula
`dP_Pa = max(absNetDP_Pa, chamberPressure_Pa)` (the Pc floor
representing "startup conditions where coolant is primed and gas
is at vacuum") is dropped in favor of `dP_Pa = absNetDP_Pa` (pure
steady-state differential).

**Why this is acceptable for now.** The original PR #82 author
already noted: "startup/shutdown structural margin is a separate
transient analysis, not the steady-state gate target." Sprint G'
formalized that — the steady-state gate is now correct, and
startup is now explicitly the responsibility of a yet-to-be-shipped
transient sprint.

**Recommended fix.** Ship Sprint hot-fire-readiness Item 4
(startup/shutdown transient model). It's already on the roadmap;
this audit just elevates its priority.

**Effort.** ~2 sprints (~4-6 days).

**Risk.** LOW. Steady-state is more accurate post-G'; the missing
transient envelope is documented as a known gap.

**Status.** Documented in source comment (line ~120 of
StructuralCheck.cs post-G'). Hot-fire Item 4 already on roadmap.

---

### ID-5 — Composite WallMaterial 80/20 area-weighted blend ✅ RESOLVED 2026-04-27

**Status.** **Shipped via A1 sprint (post-deferred-items audit, 2026-04-27).** `GRCop42_Inconel625` revised to use series-resistance for conductivity + elastic modulus and min-of-layers for yield strength, both at the assumed 25 % liner / 75 % jacket thickness ratio. CTE / density / specific heat / cost / melting point retained as area-weighted blends (not stack-direction physics). Bond-zone shear stress from CTE mismatch is **not yet modelled** — captured as an A1 follow-on; would need a new stress component in `StructuralCheck`.

**Resolution audit (cold properties):**
- Pre-A1: `k = 263.6 W/m·K` (parallel blend) → vastly overstates effective conductivity
- Post-A1: `k = 13.2 W/m·K` (series at 25/75) → matches Hibbeler §8.3 composite-cylinder analysis
- Pre-A1: `σ_y = 462 MPa` → biased to IN625's 520 MPa
- Post-A1: `σ_y = 230 MPa` → GRCop-42's 230 MPa floor
- Pre-A1: `E = 192 GPa` → biased to IN625
- Post-A1: `E = 179 GPa` → series-stack composite

**Bench-baseline impact.** Will shift WALL_TEMP + YIELD predictions on the four composite-wall canonical presets (merlin, rl10, aerospike, pintle). Direction: stricter (more designs fail). Recommend a `bench-sa --multi-chain` baseline refresh on those four presets post-merge.

**Discipline tests.** `A1BimetallicSeriesResistanceTests` pin the composition formulas (series for k + E, min for σ_y) so a future "simplification" regression is caught at unit-test time.

---

### ID-5 (original draft, retained for context)

**File.** [`Voxelforge.Core/HeatTransfer/WallMaterial.cs`](../../Voxelforge.Core/HeatTransfer/WallMaterial.cs) line ~147 (`GRCop42_Inconel625`).

**File.** [`Voxelforge.Core/HeatTransfer/WallMaterial.cs`](../../Voxelforge.Core/HeatTransfer/WallMaterial.cs) line ~147 (`GRCop42_Inconel625`).

**The simplification.** The composite material's properties are
arithmetic blends of GRCop-42 and IN625:
- `ConductivityCold_WmK = 0.80 × 326 + 0.20 × 10 = 263.6`
- `YieldStrengthCold_MPa = 0.20 × 230 + 0.80 × 520 = 462`
- `CTE_perK = 0.25 × 17.5e-6 + 0.75 × 12.8e-6 = 14.0e-6`

Real bimetallic walls have:
- Conductivity dominated by gas-side liner (heat path is sequential
  through the bond zone, not parallel area-weighted)
- Yield governed by the WEAKEST material (failure at the bond zone
  or in the softer Cu first)
- CTE mismatch creates additional thermal-mechanical stress at the
  bond (not modeled at all)

The 80/20 split is also somewhat arbitrary — real LPBF bimetallic
walls have variable thickness ratios (1mm Cu / 3mm IN625 typical for
Merlin-class).

**Recommended fix.** Two-layer thermal resistance model:
- `R_thermal_bimetallic = t_inner / k_GRCop42 + t_jacket / k_IN625`
- Per-station structural: yield = min(σ_yield_Cu, σ_yield_IN625)
- CTE mismatch: bond-zone shear stress = ΔT × |α_Cu - α_IN625| × E_eff

**Effort.** ~1 sprint (~2-3 days).

**Risk.** MEDIUM. Will shift heat-flux and YIELD predictions for
every composite-wall design (merlin, rl10, aerospike, pintle —
4 of 5 canonical presets).

**Status.** Pre-existing. Documented in source. Not on roadmap yet.

---

### ID-6 — InjectorFaceThermal lumped equilibrium model

**File.** [`Voxelforge.Core/HeatTransfer/InjectorFaceThermal.cs`](../../Voxelforge.Core/HeatTransfer/InjectorFaceThermal.cs) header comment.

**The simplification.** Whole-face equilibrium between gas-side
heating and bore-cooled propellant. ±200 K accuracy band per the
header. No spatial resolution of the face (every element treated as
identical), no transient analysis (face heat-up takes ~1 sec on
real engines), no per-element-type bore geometry.

**Status.** Pre-existing. Well-documented in header. Bundled with
YF-2 for any future fix path.

---

### ID-7 — No transient (startup/shutdown) structural analysis

**See ID-4.** Same gap. Sprint hot-fire-readiness Item 4 covers it.

---

### ID-8 — `gasGamma` defaults to chamber γ for full-station static-P calc

**File.** [`Voxelforge\Optimization\RegenChamberOptimization.cs`](../../Voxelforge/Optimization/RegenChamberOptimization.cs) ~line 384 (Sprint G').

**The simplification.** Sprint G' passes `gas.GammaThroat` as the
single γ used for ALL stations' isentropic-flow static-P calculation.
Real flows have varying γ (chamber γ ≈ 1.20-1.25, throat γ ≈ 1.18-1.20,
exit γ → ratio of frozen specific heats ≈ 1.4 for cooled exhaust).

**Why acceptable for now.** The variation is modest (~10% across
the contour). Choosing γ_throat is the standard simplification and
errs slightly conservative (lower γ → smaller static-P drop than
strictly correct). Acknowledged in source comment.

**Recommended fix.** Swap the single scalar γ for a per-station array
that varies axially (γ_chamber → γ_throat → γ_exit).

**Real blocker.** The earlier note said this unblocks once PH-4 ships.
Re-evaluated 2026-04-27: PH-4's 2-D bilinear (Pc × MR) tables are
**frozen-flow** (`CeaTable2DBase.cs:96` — `GammaThroat = GammaChamber`),
so they don't actually provide a different γ at the throat or exit
within one design. The real blocker is **shifting-equilibrium CEA
tables** (composition + γ change with T as the gas expands), or an
empirical γ(M) scheme. That data isn't in the project today.

**Effort.** ~2-3 days when shifting-eq tables ship (or an empirical
γ(M) scheme is approved as a stopgap). Not 1-2 hours as previously
noted.

**Risk.** LOW for the structural-side wiring; MEDIUM-HIGH for any
empirical γ(M) scheme (would need its own justification + uncertainty
band).

**Status.** Acknowledged in Sprint G' source comments. **Blocked on
shifting-equilibrium tables, not on PH-4** (corrected 2026-04-27).

---

## Summary table

| ID | Severity | Effort | Risk | Status |
|---|---|---|---|---|
| ~~YF-1 — Stechman β tuned to inferred target~~ | ~~YELLOW~~ | ~~2-3 hrs~~ | ~~MEDIUM~~ | ✅ **Shipped 2026-04-27 (PR #92, bundle-1)** |
| YF-2 — Coax mixingEff tuned to inferred target | YELLOW | 3-4 days (CFD) | LOW immediate | Documented; long-term Sprint T2.3 |
| ~~ID-1 — FilmCooling default film density wrong~~ | ~~RED~~ | ~~bundle w/ YF-1~~ | ~~MEDIUM~~ | ✅ **Shipped 2026-04-27 (PR #92, bundle-1)** |
| ~~ID-2 — RegenCoolingSolver constant gas velocity~~ | ~~RED~~ | ~~30 min~~ | ~~LOW~~ | ✅ **Shipped 2026-04-27 (PR #92, bundle-1)** |
| ~~ID-3 — TurbopumpSizing single shared discharge~~ | ~~RED~~ | ~~2-3 hrs~~ | ~~MEDIUM~~ | ✅ **Shipped 2026-04-27 (PR #93, bundle-2)** |
| ID-4 — Steady-state-only structural | RED | 2 sprints | LOW | On roadmap (Item 4) |
| ~~ID-5 — Composite WallMaterial 80/20 blend~~ | ~~RED~~ | ~~2-3 days~~ | ~~MEDIUM~~ | ✅ **Shipped 2026-04-27 (A1 sprint, this PR)** — series-resistance for k + E, min(layers) for σ_y |
| ID-6 — Lumped face thermal model | RED | bundle with YF-2 | LOW | Pre-existing |
| ID-7 — No transient analysis | (== ID-4) | (== ID-4) | LOW | (== ID-4) |
| ID-8 — Single γ for all stations | RED-low | ~2-3 days (shifting-eq tables) | LOW (wiring) / MED (empirical) | Acknowledged. Real blocker is shifting-eq CEA tables, not PH-4 (corrected 2026-04-27). |

## Recommended consolidation sprint — "Physics integrity" bundle

Combining the bundle-able items into a single sprint maximizes the
joint-calibration efficiency (β + film density together; γ_throat +
station-γ together once PH-4 lands). Recommended scope:

**Physics-integrity-bundle-1 (~4-5 hours):**
- ID-1 — Use real fuel density in `FilmCooling.Compute`
- ID-2 — Use real chamber gas velocity (Mach × sound speed)
- YF-1 — Re-calibrate β jointly with ID-1 / ID-2 fixes
- New test: `FilmCoolingProductionEngineCalibration` pinning η at
  the published-engine target stations

**Physics-integrity-bundle-2 (~3-4 hours, separate sprint):**
- ID-3 — Separate fuel/ox pump discharge in `TurbopumpSizing`

**Future sprints (deferred):**
- ID-4 / ID-7 — Transient analysis (already roadmapped as Item 4)
- ID-5 — Bimetallic two-layer thermal+structural model
- YF-2 / ID-6 — CFD validation loop (Sprint T2.3)

## Audit-completion checklist

When a future sprint ships a fix from this list, update this doc to:
1. Move the entry from "Status: Not yet fixed" to "Status: Shipped
   in Sprint X (PR #N)."
2. If a calibration was re-derived, document the new derivation path
   AND any new yellow flags introduced.
3. Cross-link from the sprint's CHANGELOG entry to this doc.

## Z1 hot-fix bundle (2026-04-28) — externally-audited correctness fixes

Six external audits (three on PRs #105 + #106; one geometry sweep; one
net-new physics-error sweep; one adversarial physics-correctness
sweep) consolidated and validated by three parallel verification
passes. Z1 ships the must-ship-before-next-SA-run hot-fixes. Z2 / Z3
remain as next-sprint follow-ons + opportunistic items respectively.

### Z1-C1 — TPMS pre-screen strut formula inverted (SHIPPED 2026-04-28)

**Status.** **Shipped Z1.1.** [`FeasibilityGate.cs:480`](../../Voxelforge.Core/Optimization/FeasibilityGate.cs)
pre-screen path used `(1 − sf) × cellEdge` (the void size); the full-
eval path at `:1174` correctly used `TpmsCorrelations.StrutThickness_mm`
(= `sf × cellEdge`). The two paths disagreed for every `sf ≠ 0.50`
inside the SA envelope `[0.35, 0.65]`. Pre-screen was rejecting designs
the full pipeline accepted (and vice versa), corrupting the optimizer's
acceptance contour at zero compute cost.

**Resolution.** `:480` now calls the same `TpmsCorrelations.StrutThickness_mm`
helper. New `T1_5PreScreenTests.PreScreen_TpmsStrutFormula_AgreesWithFullEval`
[Theory] pins parity across `solidFraction ∈ {0.35, 0.45, 0.50, 0.55, 0.65}`
× `cellEdge ∈ {1.5, 2.5, 4.0, 6.0}` — 20 InlineData rows that catch
any future formula drift.

**Why this was missed.** Pre-screen was added in PR #105 (T1.5
progressive-fidelity SA) as a 2-3× SA-throughput optimization. The
formula was hand-translated from the full-eval path and the inversion
slipped through review because the existing tests at `sf = 0.50` are
accidentally invariant to the bug (`(1 − 0.5) × ce` ≡ `0.5 × ce`).

### Z1-F1 — Bimetallic E_eff: series → parallel (Voigt) (SHIPPED 2026-04-28)

**Status.** **Shipped Z1.2.** [`WallMaterial.cs:184-188`](../../Voxelforge.Core/HeatTransfer/WallMaterial.cs)
used the SAME series-resistance formula for E_eff that conductivity
uses: `1 / (0.25 / E_liner + 0.75 / E_jacket)`. **Wrong physics for a
bonded composite cylinder under hoop tension.**

**Textbook derivation.** Both layers elongate together by the same
ε_θ (strain compatibility from the bonded interface). Force balance:
`P · r = σ_liner · t_liner + σ_jacket · t_jacket = ε_θ · (E_liner · t_liner + E_jacket · t_jacket)`,
so `E_eff = f_liner · E_liner + f_jacket · E_jacket` — the **Voigt
average / parallel form**, NOT series. The intuition is opposite to
conductivity: heat flow is normal to the wall (resistances stack along
the heat-flow direction), but hoop strain is along the wall
(stiffnesses act in parallel).

**Resolution.** `:184-188` now uses `0.25 · E_liner + 0.75 · E_jacket`.
Cold E_eff: 179 → 187.75 GPa (+5 %); hot E_eff: 142 → 148.75 GPa (+5 %).
Test renamed `_MatchesSeriesStack` → `_MatchesParallelStack` with new
`ParallelE` helper. The conductivity test correctly stays on `SeriesK`.
Rationale comment block at `WallMaterial.cs:149-157` rewritten to
explain the opposite-direction physics.

**Resolution audit.**
- Bench-baseline refresh (`bench-sa-merlin-2026-04-28.jsonl` + 3
  others) captures the structural shift. Pre-Z1 (04-27, but actually
  on the pre-A1 SHA) merlin reported `peak_wall_t_k=1890.3`,
  `coolant_dp_pa=7,327,618`. Post-A1 + Z1 (04-28): `peak_wall_t_k=1551.2`
  (−339 K), `coolant_dp_pa=1,522,789` (−4.8×). The 04-27 → 04-28 delta
  combines A1 conductivity + Z1 E correction. Both are
  physics-correctness shifts in the conservative direction.

### Z1-B1 — Track B closed-loop break: thermal solver + voxel builder (SHIPPED 2026-04-28)

**Status.** **Shipped Z1.3.** Track B (Sprint 2026-04-27, PR #106) added
SA design variables for per-station gas-side wall thickness overrides
(`ChamberWallThicknessOverride_mm`, `ThroatWallThicknessOverride_mm`,
`ExitWallThicknessOverride_mm`, dims 28-30). The override flowed via
`StructuralCheck.BuildGasSideWallProfile_mm(...)` into
`StructuralCheck.Evaluate` and `ProofTestAnalysis.Evaluate` — but the
THERMAL SOLVER kept reading uniform `inp.Channels.GasSideWallThickness_mm`
at `RegenCoolingSolver.cs:482, 502, 620, 695, 830`, and the VOXEL BUILDER
read uniform `ch.GasSideWallThickness_mm` at
`ChamberVoxelBuilder.cs:161, 241, 337, 458`. So the override was a
silent no-op for everything except the standalone structural check.

**Resolution.** Three-file change:
1. **`RegenCoolingSolver` (Z1.3a):** new `IReadOnlyList<double>? GasSideWallProfile_mm = null`
   parameter on `RegenSolverInputs`. New `GasSideWallAt(idx)` helper hoisted
   before the per-station loop falls back to the uniform value when null
   OR length mismatch. All 5 occurrences inside per-station loops + the
   axial-conduction post-pass now use `t_wall_i_mm`.
2. **`ChamberVoxelBuilder` (Z1.3b):** new `GasSideWallProfile_mm` parameter
   on `ChamberBuildOptions`. `BuildAnalytical` averages adjacent stations
   for the frustum integral. Outer-jacket revolve indexes per station.
   TPMS implicit takes `profile.Min()` (most conservative — keeps
   strut-to-wall clearance honoured everywhere). Smoothen safety cap
   uses `profile.Min()` so the 25 % feature-floor cap holds across the
   whole wall.
3. **`RegenChamberOptimization` (Z1.3c):** new
   `StructuralCheck.FindThroatStationIndex(ChamberContour)` overload
   lets the orchestrator build `wallProfile` BEFORE the thermal solve
   (the throat index from the contour matches the solver-output
   throat index because the thermal march doesn't move station radii).
   The same `wallProfile` then feeds the post-solver `StructuralCheck`
   + `ProofTestAnalysis` calls — eliminating the duplicate
   construction at the old `:387` and `:1346`.

**Test pin.** `TrackBPerStationWallThicknessTests` extended with four
new tests: `FindThroatStationIndex_ContourOverload_AgreesWithSolverOverload`,
`RegenSolver_ThroatWallOverride_ShiftsThroatTwg` (pins ≥ 5 K shift
when throat goes 1→4 mm), `RegenSolver_NullProfile_BitIdenticalToUniformBaselineArray`
(back-compat invariant), `BuildAnalytical_ExitWallOverride_ShiftsTotalMass`.
The voxel STL bbox third regression from the plan is intentionally
omitted (ADR-005: PicoGK can't run in-process under xUnit); voxel-side
coverage relies on bench-baseline refresh diffs.

### Z1-M3c — Pre-screen violation propagation (SHIPPED 2026-04-28)

**Status.** **Shipped Z1.5.** Both single-chain and multi-chain SA
paths discarded the `FeasibilityViolation` returned by `PreScreen`,
so log diagnostics couldn't distinguish pre-screen rejects from
gate-eval rejects from exception throws. `MakeInfeasibleScore()` in
[`Program.cs`](../../Voxelforge/Program.cs) now accepts an
optional `FeasibilityViolation? preScreenViolation` and an optional
`exceptionReason` string, populating the `Warnings` + `FeasibilityViolations`
fields differently per origin.

### Z1-M1 — Stale bench baselines refreshed (2026-04-28)

**Status.** **Shipped Z1.4.** `bench-sa-{merlin, rl10, aerospike, pintle}-2026-04-27.jsonl`
were generated against `git_sha = 59924d7` (PR #65, pre-A1). The
post-A1 + post-Z1 baselines committed as `bench-sa-{preset}-2026-04-28.jsonl`.
04-27 baselines kept on disk as a frozen pre-A1 reference (matching the
pattern used for the 04-24 pre-cascade baselines).

## Z2 / Z3 follow-on items

The external-audit consolidation flagged these for follow-on sprints —
NOT in scope for Z1 but tracked here so they don't get lost:

- **Z2 #6 / Bug #2:** `StructuralCheck.cs:261` evaluates yield at
  `T_mean = 0.5·(Twg+Twc)` instead of `Twg`. Non-conservative for
  liner-dominated bimetallic. One-line fix; needs re-baseline.
- ~~**Z2 #7 / F-2:** `TurbopumpSizing.cs:408+` NPSHR=0 fallback when
  `dischargeP ≤ inletP`: dP clamped → rpm=0 → NPSHR_m=0 → `NPSHA ≥ 0`
  always passes. `PUMP_PRESSURE_INVERTED` (gate 14b) catches the
  inversion post-hoc but cycle-balance / `TURBINE_POWER_DEFICIT` see
  fake-zero ShaftPower first.~~ **SHIPPED 2026-04-28 (Z2.7).** Early-exit
  branch added at `TurbopumpSizing.SizeOnePump` returns sentinel with
  `ShaftPower_W = +Infinity` (cycle-balance fails consistently),
  `NPSHAcceptable = false` (NPSH gate fires too), and `Efficiency = NaN`
  (downstream NaN propagation flags the result). The
  `Math.Max(dP, 0)` clamp removed — non-inverted cases have `dP > 0` by
  construction now. Three regression tests in `Tier1CorrectnessBundleTests`:
  inverted-feed sentinel pin, gate-still-fires belt-and-suspenders pin,
  healthy-feed bit-identical back-compat pin.
- ~~**Z2 #8 / F-3:** No `BURST_MARGIN_INSUFFICIENT` feasibility gate.
  PR #104's 2.0× → 2.5× ASME bump landed in `ProofTestAnalysis`
  warning thresholds + `SafetyReport`, NOT a gate — designs below
  burst margin pass feasibility today.~~ **SHIPPED 2026-04-28 (Z2.8).**
  New gate 14c `BURST_MARGIN_INSUFFICIENT` keyed on
  `gen.BurstMarginFactor < ProofTestAnalysis.MinBurstMarginFactor`
  (= 2.5×). Cheap thin-wall hoop calc factored into
  `ProofTestAnalysis.ComputeBurstMarginFactor` and called from
  `RegenChamberOptimization.GenerateWith` so the SA hot path doesn't
  pay for full proof-test analysis. Three regression tests in
  `FeasibilityGateTests` (below-threshold fires, at-threshold passes,
  zero-value short-circuits for legacy call sites). Bench-baseline
  refresh: all 4 composite-wall canonical presets still report 0
  feasible at seed (dominated by gate 1 `WALL_TEMP` which fires
  before burst margin); the new gate is additive on already-
  infeasible designs and doesn't shift the bench cliff. Gate census
  46 → 47.
- **Z2 #9 / F-5:** `PreburnerCooling.cs:88` hardcodes recovery-factor
  at 0.90; the correct `PropellantTables.RecoveryFactor(Pr) = ∛Pr`
  helper exists but isn't called.
- ~~**Z2 #10 / C2-hardcoded:** `WallMaterial.cs:172-189` 25/75 ratio
  hardcoded; doesn't track actual liner/jacket split. Recommended
  fix: make `GRCop42_Inconel625` a method that takes `linerFraction`.
  Schedule alongside Z1-B1 to avoid double-rework.~~ **SHIPPED
  2026-04-29 (Z2.10-followon).** `WallMaterials.GRCop42_Inconel625`
  converted from a `static readonly` field to a method
  `GRCop42_Inconel625(double linerFraction = 0.25)`. All composite
  properties (k_eff series, σ_y composition blend, E_eff parallel /
  Voigt, ρ, Cp, α, cost, melting point) recomputed at the supplied
  ratio. Default 0.25 preserves the historical pre-Z2.10 ratio
  bit-identically (regression-pinned by
  `GRCop42_Inconel625_DefaultParameter_BitIdenticalToExplicit025`).
  Out-of-range fractions throw `ArgumentOutOfRangeException`. The
  `WallMaterials.All` array uses `GRCop42_Inconel625()` (default
  fraction). +14 tests in `A1BimetallicSeriesResistanceTests` cover
  the parametric path (theory data points 0.20 / 0.25 / 0.40 / 0.50)
  plus the back-compat invariant. PublicAPI.Unshipped.txt updated:
  the static field signature is replaced by the method signature.
- **Z2 #11 / M3a/b:** No `--no-pre-screen` disable flag, no
  `PreScreenRejections` counter on `MultiChainSession`. ~~Determinism
  test needed (100-iter SA with vs without pre-screen on a wide-open
  corpus must produce identical `(BestParams, BestScore)`).~~
  **Determinism test SHIPPED 2026-04-29 (Z2.11a).** Three
  regression tests in `MultiChainOptimizerTests`:
  `PreScreen_DeterminismInvariant_WithVsWithoutProducesIdenticalSA`
  (4-chain × 100-iter, baseSeed 42, asserts identical
  `BestScore` / `BestParams` / `WinningChain` / `TotalIterations`
  across with-pre-screen and without-pre-screen evaluator wrappers
  on a synthetic convex corpus with a band-reject mimicking the
  CONTRACTION_RATIO_OUT_OF_BAND-style production gate);
  `PreScreen_DeterminismInvariant_HoldsOnSingleChainDegenerate`
  (chainCount=1 sister run — covers the no-migration-barrier
  path); `PreScreen_DeterminismHarness_ActuallyExercisesRejectPath`
  (counts band-reject hits inside the SA loop and asserts ≥ 1 so
  the determinism check doesn't silently degenerate to
  `FullEval == FullEval` if the synthetic corpus drifts off-band
  in a future refactor). The `--no-pre-screen` CLI flag and
  `PreScreenRejections` counter remain DEFERRED — diagnostic
  conveniences not gating the correctness invariant the
  determinism test pins.
- **Z3 #12-23:** ~~Per-station `G_g` in FilmCooling (F-1 film)~~ **SHIPPED
  2026-04-29 (Z3.F-1, bundled with PH-37).** New optional
  `gasMassFluxPerStation_kg_m2_s` parameter on `FilmCooling.Compute`;
  `RegenCoolingSolver.Solve` populates it via mass conservation
  `G(x)·A(x) = ṁ_total` from the chamber-side scalar G + station areas.
  Stechman momentum-ratio factor `(G_g/G_f)^0.25` now reflects axial
  G_g variation; pre-Z3.F-1 the chamber-only scalar under-predicted G_g
  at the throat by ~the contraction ratio, biasing η high mid-chamber.
  +1 regression test (`Z3F1_PerStationGasMassFlux_ShiftsEffectivenessVsScalar`).
  ~~Mach-dependent throat mixing penalty (F-4)~~ **SHIPPED 2026-04-29
  (Z3.F-4, closes [#216](https://github.com/poetac/voxelforge/issues/216)).**
  New `MixingLayerEffectivenessFor(elementType, chamberMach)` overload
  attenuates the per-element-type baseline by a linear factor above
  `ChamberMachReference = 0.10`: `η(M) = η_base · max(1 − 0.5·(M − 0.10), 0.5)`.
  `RegenGenerationResult.ToInjectorFaceGeometry` computes the chamber
  Mach from the station-0 area ratio (= 1/ε_c) via the subsonic
  isentropic area-Mach relation and forwards it as
  `InjectorFaceGeometry.ChamberMach`. Small-ε_c designs (ε_c ≈ 2.5,
  M ≈ 0.25) now see meaningful η attenuation; large-ε_c designs
  (ε_c ≥ 6, M ≤ 0.10) see the legacy baseline. Calibration-grade —
  attenuation slope + floor are tunable constants. +4 tests in
  `InjectorFaceThermalUnitTests`.
  ~~`EquilibriumCorrection` clamp diagnostic (F-6)~~ **SHIPPED
  2026-04-29 (Z3.14).** New `IReadOnlyList<string>? Warnings`
  property on `PropellantState`. `LogPcDissociationCorrection.Correct`
  populates it with a per-clamp diagnostic note when any of the
  three factors (tcFactor / cStarFactor / gammaFactor) hits the
  conservative-bound clamp at far-from-reference Pc. Pre-Z3.14 the
  clamps fired silently. +8 tests in
  `EquilibriumCorrectionWarningsTests` cover in-envelope (no
  warnings), extreme-Pc clamping (warnings populate), idempotency
  (no double-emission on already-corrected states), and prior-
  warning preservation across the correction.
  ~~`StructuralCheck.gasGamma` required (F-7, claim via [#217](https://github.com/poetac/voxelforge/issues/217))~~ **SHIPPED 2026-04-29 (Z3-F7, closes #217)** — `gasGamma` promoted to required parameter (position 5, before optional `outerJacketThickness_mm`); all 3 non-test callers updated; +1 regression test `GasGamma_Required_NonZeroChangesHoopVsZeroPath` + renamed `DefaultParameters_PreserveLegacyBehavior` → `GasGamma_ZeroExplicit_SameAsExplicitZeroOptionals`.,
  gate-kind categorization (F-8), end-to-end A1 thermal regression
  (F-9), ~~wall-conductivity per-layer at layer T (m1)~~ **SHIPPED 2026-04-29
  (Z3.m1, closes [#218](https://github.com/poetac/voxelforge/issues/218)).**
  New `WallMaterial.LinerFraction` field (default 0 = pure material).
  `WallMaterials.GRCop42_Inconel625(linerFraction)` populates it.
  `RegenCoolingSolver.WallResistanceLogMean_KperWperM2` accepts the
  `WallMaterial` + (T_wg, T_wc) and dispatches to a new
  `BimetallicLogMeanResistance_KperWperM2` helper when LinerFraction > 0.
  Per-layer log-mean conduction: `R_liner = r_inner·ln(r_iface/r_inner)/k_liner(T_wg)`,
  `R_jacket = r_inner·ln(r_outer/r_iface)/k_jacket(T_wc)`. Pre-Z3-m1 used
  the single-T `ConductivityAt(T_wg)` for both layers — overstated jacket k
  by ~10-15% at typical T_wg=900 K vs the ~T_wc=400 K the jacket really
  sees. +3 tests in `A1BimetallicSeriesResistanceTests`. Bench-baseline
  shifts on bimetallic-wall presets are in the expected direction (slightly
  higher T_wg under the same heat load) and within fixture tolerance.
  geometry B2/~~B3~~/~~B5~~/B6, elevate
  T2.3 CFD validation (F-10, tracked via [#160](https://github.com/poetac/voxelforge/issues/160)). ~~Z3 #20 / Geometry B3:~~ **SHIPPED 2026-04-29
  (Z3.20).** New `TPMS_AND_MANIFOLD_OVERLAP` AdvisoryHeuristic gate
  (gate census 47 → 48) fires when `2 × ManifoldLength_mm ≥
  TotalLength_mm` on a TPMS-topology design, pre-empting PicoGK
  pitfall #2 (BoolSubtract through TPMS-filled regions) for small-
  chamber TPMS designs where the inlet + outlet manifolds at
  opposite ends would overlap each other in the chamber centre.
  +5 tests in `FeasibilityGateTests` cover firing-on-overlap, no-
  fire-when-fits, no-fire-on-non-TPMS-topology, no-fire-on-zero-
  ManifoldLength (legacy fixture), gate-kind=AdvisoryHeuristic.
  ~~Z3-M2 / Geometry B5 (bond-zone shear, claim via #214):~~ **SHIPPED
  2026-04-29 (closes #214).** New `BIMETALLIC_BOND_ZONE_SHEAR`
  AdvisoryHeuristic gate (gate census 48 → 49).
  Formula: `τ_bond = ΔT · |α_liner − α_jacket| · E_eff` (Hibbeler
  §8.4 thermo-mechanical composite). `E_eff = 0.5·(E_liner(T_mean) +
  E_jacket(T_mean))` — arithmetic mean of the two LPBF-grade moduli at
  the inter-layer temperature. Gate fires when `τ_bond > σ_y_min·0.5`.
  Only active for bimetallic wall index 4 (GRCop-42 + IN625); Δα ≈
  4.7 × 10⁻⁶ /K. First-order simplification: no length-scale
  correction factor (accounts for constraint geometry); conservative
  (over-predicts shear slightly for short specimens) — real FEA
  advised before fabrication.
  `StructuralSummary` gains two new optional fields:
  `BondZoneShearStress_MPa` (peak τ across stations) and
  `BondZoneShearRatio` (τ / (σ_y_min·0.5)); both default 0.0 for
  single-material walls (fully backward-compatible).
  +6 tests in `A1BimetallicSeriesResistanceTests` cover single-
  material zero-shear, high-ΔT ratio > 1, low-ΔT ratio < 1, gate-kind
  advisory, gate fires, gate silent on single-material.

## Disputed audit claims (rejected on verification)

External auditors flagged these but verification rejected them:

- **"Strain compatibility → series resistance"** for E_eff (one
  Explore-agent commentary on Z1-F1). Wrong physics: bonded composite
  cylinder under hoop tension uses Voigt/parallel, not series. The Z1
  F-1 audit's derivation is correct; this commentary misapplied the
  in-code "strain compatibility" comment.
- **`AerospikePlugChannel.cs:69` wrong radius** — `R_inner_mm` IS the
  plug's gas-facing surface for an aerospike. Code correct.
- **Channel-height discontinuity at t=0.5** in `BuildAnalytical` — both
  branches evaluate to `h_throat`; C⁰ continuous, intentional C¹ kink.
- **Rao θ_e non-monotonic in ε** — auditor misread two adjacent rows;
  table is strictly monotone decreasing.
- **LinearAerospikeImplicits null Voxels** — documented Sprint-27+
  follow-on with explicit `if (!spec.IsLinear) throw` guard.
- **Helix sin/cos convention drift** between RegenCoolingSolver and
  CoolantCorrelations — same convention, no drift.

These items are NOT actionable; documented here so future audits
don't re-litigate them.

## Cross-references

- [`ADR-018-feasibility-audit-2026-04-26.md`](ADR/ADR-018-feasibility-audit-2026-04-26.md) — feasibility-audit cascade verdict.
