# Published-engine validation library (OOB-3, 2026-06 v11)

A test-only fixture library that drives voxelforge's `GenerateWith` against the published specifications of real flying rocket engines and asserts the predictions land inside a documented tolerance band of the published numbers.

## Why this exists

`Voxelforge.Benchmarks/CanonicalDesigns.cs` carries the **bench-fingerprint presets** used by SA regression CI. Those presets are tuned for SA exploration (broad seed values, deliberate "design space exploration" flavour) — they intentionally don't try to reproduce specific historical hardware. The bench-regression workflow detects model-behaviour drift by diffing each preset's `bench-sa-{preset}-YYYY-MM-DD.jsonl` fingerprint against a frozen baseline.

This file is the **complement**: each fixture maps to actual hardware that flew (RL10A-3-3A, Merlin-1D, …) with its published thrust / Pc / MR / propellants / cycle / expansion ratio. Tests then assert voxelforge's prediction of vacuum Isp, total mass flow, throat radius, etc. lands within a documented band of what's in NASA / SpaceX / Pratt & Whitney data sheets.

Keeping the two kinds of fixtures separate is deliberate: the SA bench presets are tuned for stability-of-fingerprint over time; the published-engine fixtures are tuned for matching reality. Tuning either to satisfy the other would corrupt both purposes.

## What's covered (sample of the 24-fixture library; v11 — 2026-06)

| Engine | Variant | Cycle | F (vac) | Pc | ε | MR | Pair | Source |
|---|---|---|---:|---|---:|---:|---|---|
| **RL10A-3-3A** | Centaur upper stage | Closed expander | 73.4 kN | 3.27 MPa | 61 | 5.00 | LOX/H2 | NASA SP-4404 |
| **Merlin-1D** | Falcon 9 first stage | Gas generator | 914 kN | 9.7 MPa | 16 | 2.36 | LOX/RP-1 | SpaceX FAA / FCC filings |
| **J-2** | Saturn V S-II + S-IVB | Gas generator | 1.03 MN | 5.27 MPa | 27.5 | 5.50 | LOX/H2 | NASA SP-4204; Rocketdyne TM-65-115 |
| **Vinci** | Ariane 6 upper stage | Closed expander | 180 kN | 6.05 MPa | 240 | 5.80 | LOX/H2 | ESA Ariane 6 user manual; AIAA-2017-4670 |
| **Merlin-1D Vacuum** | Falcon 9 second stage | Gas generator | 934 kN | 9.7 MPa | 165 | 2.36 | LOX/RP-1 | SpaceX FCC filings |
| **BE-4** | Vulcan / New Glenn first | Staged combustion | 2.7 MN | 13.4 MPa | 12 | 3.60 | LOX/CH4 | ULA Vulcan UG; AIAA SciTech 2020 |
| **Raptor 2** | Starship Super Heavy / upper | Full-flow staged | 2.69 MN | 30 MPa | 40 | 3.60 | LOX/CH4 | SpaceX FAA / FCC; AIAA-2017-5044 |
| **HM7B** | Ariane 4 H10 / Ariane 5 ESC-A | Gas generator | 64.8 kN | 3.5 MPa | 83 | 5.14 | LOX/H2 | Snecma data sheet; AIAA-95-2630 |
| **J-2X** | Ares I / SLS Block 1B EUS (designed) | Gas generator | 1.31 MN | 9.85 MPa | 92 | 5.50 | LOX/H2 | NASA J-2X data sheets; AIAA-2007-5447 |
| **SSME** | Space Shuttle Orbiter / SLS RS-25 | Staged combustion (fuel-rich) | 2.28 MN | 20.64 MPa | 69 | 6.03 | LOX/H2 | NASA SP-4205; AIAA-2009-5093; Sutton 9e §6.7 |
| **NK-33** | Soviet N1 / Antares 1xx as AJ26 | Staged combustion (oxidiser-rich) | 1.64 MN | 14.55 MPa | 27 | 2.40 | LOX/RP-1 | Kuznetsov specs; AIAA-2003-4475 |
| **RD-180** | Atlas V first stage (per-chamber) | Staged combustion (oxidiser-rich) | 2.26 MN | 26.7 MPa | 36.4 | 2.72 | LOX/RP-1 | NPO Energomash; AIAA-2010-6883 |
| **Raptor 1** | Starship test prototypes (SN5-SN15, 2020-2022) | Full-flow staged | 2.0 MN | 25.5 MPa | 35 | 3.60 | LOX/CH4 | SpaceX FAA / FCC; Musk public statements |
| **RS-68A** | Delta IV first stage (Common Booster Core, 2012-2024) | Gas generator | 3.14 MN | 9.72 MPa | 21.5 | 6.00 | LOX/H2 | Aerojet Rocketdyne; AIAA-2010-6878 |
| **RD-191** | Angara A5 first stage (URM-1 booster + core, 2014-) | Staged combustion (oxidiser-rich) | 2.09 MN | 25.8 MPa | 37 | 2.63 | LOX/RP-1 | NPO Energomash; IAC-17-D2.5 |

Pinned predictions per fixture: vacuum Isp, total mass flow, throat radius, plus a defensive "GenerateWith doesn't throw" guard. **All 24 fixtures land within tolerance bands as of 2026-06 v11** — voxelforge's preliminary-design predictions track published vacuum performance across:

- **Thrust:** 65 kN (HM7B) → 3.14 MN (RS-68A), 48× span
- **Chamber pressure:** 3.27 MPa (RL10) → 30 MPa (Raptor 2, at AutoSeeder cap), 9× span
- **Expansion ratio:** 12 (BE-4) → 240 (Vinci), 20× span
- **Cycles:** closed expander (RL10, Vinci) + gas generator (HM7B, J-2, J-2X, RS-68A, Merlin variants) + staged combustion (BE-4, SSME, NK-33, RD-180) + full-flow staged (Raptor 1, Raptor 2) — all four supported voxelforge cycle paths exercised. Gas generator has 7 fixtures spanning **65 kN → 3.14 MN (48× thrust range)**, 5 of them LOX/H2 from upper-stage to first-stage thrust class. Staged combustion has 4 fixtures across both preburner sub-cycles + 3 propellant pairs; full-flow staged has 2 fixtures (Raptor 1, Raptor 2) at two Pc points.
- **Propellants:** LOX/H2 (RL10, J-2, J-2X, HM7B, Vinci, SSME, RS-68A — **7 fixtures, all 3 production cycle types**), LOX/RP-1 (Merlin variants + NK-33 + RD-180 + RD-191 — **5 fixtures, GG + staged combustion across 3 Pc points**), LOX/CH4 (BE-4, Raptor 1, Raptor 2 — **3 fixtures, staged combustion + full-flow at two Pc points**). **LOX/RP-1 ox-rich SC now has a 3-point Pc sweep (NK-33 14.55 MPa → RD-191 25.8 MPa → RD-180 26.7 MPa per chamber) — pins the Isp/efficiency response to Pc lift on a continuous axis.**

Cross-validation pairs the library exposes:

- **Staged-combustion cross-propellant + cross-Pc sweep** — SSME (LOX/H2 fuel-rich, 2.28 MN, 20.64 MPa) + BE-4 (LOX/CH4 ox-rich, 2.4 MN, 13.4 MPa) + NK-33 (LOX/RP-1 ox-rich, 1.64 MN, 14.55 MPa) + RD-180 (LOX/RP-1 ox-rich, 2.26 MN, 26.7 MPa). Four staged-combustion fixtures spanning all 3 propellant pairs and both fuel-rich + oxidiser-rich preburner sub-cycles. Pc range 13.4 → 26.7 MPa. Pins the staged-combustion cycle behaviour across the propellant-pair × Pc envelope.
- **NK-33 → RD-191 → RD-180 (LOX/RP-1 ox-rich SC 3-point Pc sweep)** — same propellant, same cycle architecture, three distinct Pc operating points: NK-33 14.55 MPa (1.64 MN, 1969 design) → RD-191 25.8 MPa (2.09 MN, 2014 single-chamber) → RD-180 26.7 MPa per-chamber (2.26 MN, 2002 dual-chamber). Tests Isp/efficiency response to Pc lift on a continuous axis, plus validates that voxelforge's per-chamber convention (RD-180) matches a natively-single-chamber engine (RD-191) at the same operating point — analogous to J-2 → J-2X for GG LOX/H2 but with an additional mid-point.
- **Raptor 1 → Raptor 2 (LOX/CH4 FullFlow same-family Pc-step pair)** — same engine family, same cycle (full-flow staged combustion), Pc 25.5 → 30 MPa (1.18× lift), thrust 2.0 → 2.26 MN per engine, ε 35 → 40, vacuum Isp 356 → 363 s (+2 %). Smaller Pc step than NK-33/RD-180 but spans the FullFlow cycle path which other same-family pairs do not. Completes the **same-family Pc-step pattern across all 3 propellant pairs**: J-2 / J-2X (LOX/H2 GG), NK-33 / RD-180 (LOX/RP-1 SC), Raptor 1 / Raptor 2 (LOX/CH4 FullFlow), plus the same-engine operating-point pair Merlin-1D / Merlin-1D Vacuum (LOX/RP-1 GG, ε swap).
- **LOX/H2 cycle-coverage triangle** — closed expander (RL10 73 kN, Vinci 180 kN), gas generator (HM7B 65 kN, J-2 1.03 MN, J-2X 1.31 MN), and staged combustion (SSME 2.28 MN). All three production cycle types on the same propellant pair, spanning 35× thrust range. Pins propellant-pair vs cycle-architecture interactions.
- **LOX/RP-1 cycle pair** — NK-33 (staged combustion, 14.55 MPa, 1.64 MN) vs Merlin-1D (gas generator, 9.7 MPa, 845 kN). Same propellant pair, very different cycles + 1.5× Pc step. Tests the cycle-architecture lift on a kerolox.
- **NK-33 vs BE-4 (cross-propellant ox-rich SC)** — both oxidiser-rich preburner staged combustion at similar thrust class (1.64 vs 2.4 MN). Different fuel (RP-1 vs CH4) at the same cycle architecture. Pins fuel-pair effects under the same combustion topology.
- **HM7B → J-2 → J-2X thrust-class triangle** — LOX/H2 gas-generator at three thrust scales (65 kN, 1.03 MN, 1.31 MN; 20× span). Pins same-cycle / same-propellant scaling consistency over a wide thrust range.
- **HM7B vs Vinci (Ariane upper-stage cycle pair)** — LOX/H2 upper-stage at similar thrust (65 vs 180 kN, 2.8×) but different cycles (gas generator vs closed expander). Probes whether the Isp/cycle-efficiency split is captured.
- **J-2 vs J-2X (same-family Pc step)** — same Rocketdyne lineage at 1.9× higher Pc (5.27 → 9.85 MPa) and 3.3× higher ε (27.5 → 92). Tests whether the model predicts the correct Isp lift from the design-point upgrade.
- **J-2 vs RL10** — LOX/H2 at very different thrust scales (1 MN vs 73 kN, 14×); pins thrust-class scaling consistency on the same propellant.
- **SSME vs J-2 (LOX/H2 cycle architecture pair)** — same propellant pair, very different cycles (staged combustion vs gas generator) and very different Pc (20.64 vs 5.27 MPa, 3.9×). Tests Isp lift from the cycle-architecture upgrade at fixed propellant. Expected published Isp delta: SSME 452.3 s vs J-2 421 s (~7 % gain).
- **SSME vs BE-4 (staged-combustion cross-propellant pair)** — same cycle family at similar thrust class (2.28 vs 2.4 MN) but different propellant pair (LOX/H2 vs LOX/CH4) and sub-cycle (fuel-rich vs oxidiser-rich preburners). Pins propellant-pair effects at the same cycle architecture.
- **SSME vs RS-68A (LOX/H2 cycle-architecture pair at similar thrust class)** — same propellant (LOX/H2), similar thrust class (2.28 MN SC vs 3.14 MN GG, 1.4× scaling), very different cycle (staged combustion vs gas generator) + Pc (20.64 vs 9.72 MPa, 2.1×). Tests the model's prediction of cycle-architecture loss: SSME vacuum Isp 452.3 s vs RS-68A 411.6 s (~10 % delta), captures the GG-cycle bleed loss + lower-Pc penalty in a single same-propellant pair.
- **Merlin-1D first vs Merlin-1D Vacuum** — same hardware family at two operating points (ε = 16 vs 165); pins the high-ε prediction path against a same-family reference.
- **Vinci vs RL10** — closed-expander LOX/H2 at the edge (ε = 240) vs middle (ε = 61) of the envelope.
- **BE-4 vs Raptor 2** — LOX/CH4 staged-combustion at very different Pc (13.4 vs 30 MPa) and different sub-cycle (oxidiser-rich vs full-flow). The two LOX/CH4 fixtures in the library.
- **BE-4 vs Merlin-1D** — same thrust class (~2 MN) at very different Pc (13.4 vs 9.7 MPa) and different propellants/cycles. Cross-validates the high-thrust + high-Pc regime.

Raptor 2's `Pc = 30 MPa` is exactly at `AutoSeeder.MaxPc_Pa = 30 MPa`. The fact that voxelforge generates and validates a Raptor-class design at the cap without throwing is itself a meaningful test of the envelope-edge behaviour.

## What's NOT covered (yet, by design)

- **Wall temperatures, coolant ΔT, pressure drop** — these depend on the auto-seeded regen jacket geometry, which isn't part of the published spec. The existing `FilmCoolingPublishedEngineCalibrationTests.cs` (separate file) handles the film-effectiveness slice of this.
- **Transient behaviour** (startup, shutdown, hard-start margin). Steady-state model only.
- **Sea-level Isp, ground-test thrust loss**. We test against published *vacuum* numbers and force `AmbientPressure_Pa = 0` in the fixture to match.

## Tolerance philosophy

Per-property `EpsilonFraction` records on each fixture. Defaults (first issue):

```
IspS_Frac    = 0.20    ± 20 % on Isp
ThrustFrac   = 0.05    ± 5  % on derived thrust
MdotFrac     = 0.10    ± 10 % on total mass flow
GeometryFrac = 0.15    ± 15 % on throat / chamber radius
```

These bands are **wide on purpose**:

- Voxelforge is a preliminary-design tool. Frozen-flow CEA tables, 1-D quasi-equilibrium combustion, no finite-rate chemistry. ±20 % on Isp is the sensible band where the model can claim "in the ballpark."
- Real engines diverge from a steady-state thermodynamic model in ways the published numbers don't disclose: nozzle erosion, film cooling, turbine bleed, partial flow separation, manufacturing tolerances. A test that demanded ±2 % accuracy would fail half the fixtures on principle.
- When the model improves (PR #105 progressive-fidelity, post-A1 series-resistance composite, future T2.3 CFD validation loop), the bands can be tightened in `EpsilonFraction` per-fixture without rewriting tests.

If a future physics PR pushes a prediction outside its band, the failing test forces a documented response: widen the band with rationale, fix the underlying model, or retire the fixture. The audit-trail discipline matches `FilmCoolingPublishedEngineCalibrationTests` and `physics-integrity-notes.md`'s Z* item pattern.

## Per-chamber convention for dual-chamber engines

Some Russian engines (RD-170, RD-180, RD-191, RD-275) drive multiple combustion chambers from a single shared turbopump. voxelforge models a single combustion chamber, so for these engines the fixture validates **per-chamber** values:

- **Per chamber:** thrust, mass flow, chamber geometry (throat radius, contour) → halved from the engine-total numbers.
- **Shared:** propellant pair, cycle, Pc, MR, ε, Isp (Isp is identical at the engine-total or per-chamber level since it's a thrust / mass-flow ratio).

The fixture's `Variant` field documents the architecture (e.g. RD-180 = "Atlas V first stage (per-chamber; dual-chamber engine)"). Future dual-chamber Energomash entries follow this convention.

The chambers themselves are thermodynamically independent; they share a turbopump + preburner but their combustion + flow behaviour matches a single-chamber model at the per-chamber thrust level.

## Configuration choices

Each fixture forces these in `BuildSeed`:

- **`AmbientPressure_Pa = 0`** — we're validating against the published *vacuum* numbers; AutoSeeder defaults to ~101 325 Pa which makes the C_F formula's pressure-thrust term `(P_e − P_amb) / P_c · ε` go large-negative on high-ε engines (RL10 ε=61: P_e ≈ 3 kPa vs P_amb = 101 kPa → term ≈ −1.8 → C_F collapses 1.85 → 0.05). Using vacuum ambient matches the published-Isp configuration.
- **`MixtureRatio = spec.MixtureRatio`** — overrides AutoSeeder's "MR at peak C\*" pick with the engine's published operating MR. AutoSeeder's choice is fine for design exploration; published validation needs the engine's actual MR.

Everything else is left to AutoSeeder defaults: contraction ratio, L*, channel count, wall thickness, etc. The validation tests are deliberately blind to those — they pin the *first-order* predictions a steady-state thermodynamic model owes to a published spec, not the regen-jacket detail.

## Adding a new engine

1. Add a `public static readonly PublishedEngineSpec` to `PublishedEngineFixtures.cs` with a `<summary>` citing primary sources.
2. Append to `PublishedEngineFixtures.All`.
3. Run `dotnet test --filter "PublishedEngineValidation"` — the `Theory` tests pick the new fixture up automatically.
4. If a property test fails, decide:
   - **Widen the per-property tolerance**: legitimate model imprecision; document in the fixture's `<summary>`.
   - **Fix the model**: a real bug or modelling gap; coordinate with Dev A's physics-correctness backlog.
   - **Skip the property**: e.g., engine in a regime voxelforge doesn't support (very low or very high Pc, exotic propellants). Add `[Theory(Skip = "rationale")]` on a per-fixture per-property basis.

## Recommended next engines (by priority, post-v11)

After v11's 24-engine coverage, the library has comprehensive cross-validation across propellant pairs, cycle types, thrust classes, and Pc operating points. LOX/RP-1 SC has a 3-point Pc sweep; LOX/H2 spans 7 fixtures across all 3 production cycles; same-family pairs exist on every propellant. Remaining unblocked additions are progressively-narrower fills.

**Highest-priority unblocked additions:**

1. **YF-77** (CZ-5 / Long March 5 core) — LOX/H2 staged combustion, 510 kN per chamber. Adds geographic + design-philosophy diversity (Chinese hardware) and complements SSME at lower thrust class. ~1 sprint, requires English-language sources cross-check.
2. **LE-7A** (H-IIA first stage) — LOX/H2 staged combustion, 1.07 MN, Pc 12 MPa. Japanese counterpart to RS-68A (similar role) but staged-combustion cycle. ~1 sprint.
3. **F-1** (Saturn V first stage) — LOX/RP-1, GG cycle, 6.77 MN. Tests the high-thrust regime. **Blocked on `AutoSeeder.MaxThrust_N` (currently 5 MN)** — bumping to 10 MN unlocks F-1, Saturn-V class engines, and post-2020 super-heavy lifters.
4. **RL10B-2** (Delta IV upper stage) — LOX/H2 closed expander, ε = 285. **Blocked on `AutoSeeder.MaxExpansion` (currently 250)** — bumping to 300 unlocks RL10B-2 and other ε-aggressive vacuum-only upper stages. Pairs with v1's RL10A-3-3A (ε=61) for a same-family expansion-ratio sweep across the envelope.
5. **AJ10-118K** (Delta II second stage) — small storable hypergolic (N2O4/Aerozine 50). **Blocked on AutoSeeder support for storable hypergolic propellants.** Adding `PropellantPair.N2O4_A50` is part of the propellant-pair expansion roadmap (see CLAUDE.md "Demand-driven" section).
6. **Apollo LMDE** (Lunar Module Descent Engine) — same blocker as AJ10 (storables) plus throttling regime which voxelforge doesn't model.

Each fixture is < 1 sprint per engine when the data is good; the bottleneck is usually source-data quality, not implementation. Items 3, 5, 6 are blocked on out-of-scope work tracked elsewhere.

**Library has reached a natural inflection point.** Further v11+ slices are increasingly thrust-class triangulation rather than structural-coverage gains. At this point the highest leverage on validation work shifts to:

1. **Tightening per-fixture tolerance bands** based on observed prediction accuracy (some fixtures may be predicted at ±5% even though the band is set to ±20%). Converting "passes by margin" to "passes near edge" makes the regression tests more sensitive to model drift.
2. **Adding wall-T comparisons** for the regen-cooled fixtures where published peak-T data exists. Currently the library validates first-order vacuum performance only (Isp, mdot, geometry); regen-jacket validation is the next dimension.
3. **CFD-anchoring the predictions** via T2.3 (CFD validation loop, optimization-infrastructure roadmap). Eliminates the "calibrated against published numbers, validated against published numbers" circularity that the current library cannot escape on its own.

## Cross-references

- [`FilmCoolingPublishedEngineCalibrationTests.cs`](../../Voxelforge.Tests/FilmCoolingPublishedEngineCalibrationTests.cs) — sibling pattern for film-effectiveness validation
- [`CanonicalDesigns.cs`](../../Voxelforge.Benchmarks/CanonicalDesigns.cs) — sister bench-fingerprint preset library
- [`physics-integrity-notes.md`](physics-integrity-notes.md) — the project's audit-trail discipline that this library plugs into
