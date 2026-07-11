# Roadmap

Forward-looking view of what's planned, in recommended-pickup order. For the state of shipped work, see [`CHANGELOG.md`](CHANGELOG.md).

Items scoped as 1–3 day sprints at single-dev cadence unless noted. Shippable-at-any-step — you can stop after any item and the project remains coherent.

## Strategic framing

The project pursued **Framing B (multi-physics breadth + depth)** through May 2026 — polish + depth on the existing pillar catalogue (5 production pillars + 22 Wave-1 internal pillars), no new pillars until the catalogue was hardened. All three phases — Phase 1 (stabilize), Phase 2 (depth/polish), Phase 3 (coverage backfill) — are complete (see the Done section below).

With framing-B closed, the project is in a **deliberate pause**: the repository went public on 2026-06-17, and the remaining committed work is closing the v0.1.0 release-readiness criteria (see Now), after which development is demand-driven rather than committed to a new multi-month track.

Two larger directions remain **documented and available, but neither is being actively pursued** — each is a demand-gated unlock, not a queued plan:

- **Framing A — rocket-engine depth toward physical hot-fire.** Real-gas EOS, preburner axial-march, additional propellant pairs. Unlocks when hot-fire data demands higher fidelity.
- **Framing C — mission / system designer with cross-pillar composition.** NEP coupling, end-to-end vehicle demos, Avalonia UI Phase 2+. Unlocks when a concrete multi-pillar mission demand lands.

## Done — framing-B Phase 3 (Coverage backfill) ✓

**All three phases of framing-B are COMPLETE** as of 2026-05-24.

- **Phase 1 (Stabilize)** — CI green, docs trustworthy. Caveat: GitHub Pages enable still open — one-click in repo Settings → Pages.
- **Phase 2 (Depth/polish)** — EP Wave-3 (VASIMR / FEEP / HDLT), Electrolyser Wave-3 (SOEC), CN-NEWTON A-stability, IObjective wrappers, VFD011 analyzer fix, SI integrator polish queue. Shipped Sprints A.55–A.64.
- **Phase 3 (Coverage backfill)** — Track C.1: second-anchor fixtures for all 10 remaining Wave-1 pillars (HeatExchanger, Radiator, HeatPipe, Compressor, Pump, Refrigeration, Tankage, Aerostructures, ChemicalReactor, SolarThermal; Stirling deferred). Track C.2: voxel-pipeline builders for Flywheel, Tankage, HeatPipe, Refrigeration, Aerostructures, Antenna. Shipped Sprints A.66–A.83. Umbrella tracking issues for C.1 + C.2 closed.

**Stirling deferred** — Wave-1 cluster fit over-predicts free-piston output by 10–100×; needs MEP-model refinement before a defensible fixture lands.

See `CHANGELOG.md` for sprint-level detail.

## Now — v0.1.0 release readiness

Framing-B is closed and the repository **is public**: initial public release 2026-06-17, GitHub Pages enabled 2026-06-18 with green deploys (issue #2 is satisfied by observation and can close). The planned "harden → flip → tag" sequence deliberately paused before the tag: three post-public red-team rounds (CHANGELOG Sprints A.111, A.112, A.116) have each surfaced real correctness bugs that green CI had not caught — several safety-relevant (inverted transpiration-cooling effectiveness, a never-invoked `ValidateSelf` letting NaN nuclear designs report feasible, HET designs violating energy conservation, a combustion-stability screen whose Fail branch is unreachable, a Bayesian-optimizer GP-poisoning bug that silently evaluated points outside the declared bounds). All are fixed or explicitly documented, but the discovery rate has not dried up — round 3 found 4 distinct defects (one a genuine crash) on a surface (the optimizer portfolio) rounds 1–2 never touched. ~3,900 of ~5,900 test methods (~66 %, the PicoGK voxel + WinForms suites in `Voxelforge.Tests`) still execute only on the often-offline self-hosted Windows runner, so "green CI" currently attests the Linux two-thirds of the suite.

Cut the tag when the criteria below hold. They extend — without amending — ADR-037 D7, whose original preconditions are both resolved:

1. **Red-team dry round.** One further adversarial audit round finds zero new hard-gate-class defects (conservation violations, dead gates, erased infeasibility sentinels). Rounds 1–3 each found real defects (round 3, Sprint A.116: 4 in the optimizer stack — GP poisoning, a cross-chain breakdown/score mismatch, NaN-as-feasible in both NSGA variants, and an NSGA-III empty-generation crash); tag on the first (near-)dry round. Swept so far: rocket/marine/nuclear/EP/airbreathing physics formulas, system-integration/economics, the Wave-1 energy/thermal solver bodies, the optimizer/IO/gate-mechanics/memory-envelope layer. **Not yet swept**: `Voxelforge.Core/IO/` beyond the persistence-migration mechanics already audited clean, the per-pillar `*.Voxels` builders (Windows-only, low priority until criterion 3), the CLI/`Program.cs` dispatch layer, and the per-pillar `Optimization.cs` orchestration files (`RegenChamberOptimization`, `MarineOptimization`, etc. — distinct from the generic optimizers just hardened). Sandbox-runnable — no Windows runner needed.
2. **Linux coverage parity for pure-Core physics.** **16 of 23** Wave-1 pillar solver families now have Linux-leg regression tests in `Voxelforge.Core.Tests` (was 4 at the start of this pass) — HeatExchanger, Radiator, Tankage, Refrigeration, Flywheel, Aerostructures, HydrogenStorage, HeatPipe, Thermoelectric, SolarThermal, Pump, Compressor, ElectricMotor, Battery, Photovoltaic, PowerGen(PemFuelCell). **7 remain uncovered**: WindTurbine, Hydroelectric, Electrolyser (4 sub-variants: AEM/PEM/Alkaline/SOEC — count as one family), Antenna, ChemicalReactor, HybridRocket (all portable, PicoGK-free — same backfill pattern applies), plus **Stirling**, which is a special case: no defensible fixture exists yet because the Wave-1 cluster fit over-predicts free-piston output 10–100× (see Done → Stirling deferred; MEP-model refinement is a prerequisite, not a test-writing task). Voxel/UI suites stay Windows-bound by nature — this criterion covers what *can* move.
3. **One green Windows-leg run before the tag.** A.111/A.112 shipped Windows-only fixes (voxel SDFs, setup wizard, analyzer rules) whose regression tests are math-verified but have never executed. A full green `ci.yml` run converts "shipped blind" into "validated". This is the only runner-blocked criterion — consistent with the "runner restored" close condition already encoded in issue #14.
4. **Known-gaps ledger current at the cut.** [`physics-cascade-status.md`](Voxelforge/docs/physics-cascade-status.md) refreshed as of Sprint A.113; the calibration-blocked gaps (Crocco stability screen, VASIMR Isp ceiling, bimodal-Hybrid power split, HET beam-current coupling, Stirling MEP) are recorded there. **Two new A.116 documented-not-fixed items still need adding to that ledger** (currently only in CHANGELOG): the mis-transcribed `SobolSequence` Joe-Kuo direction numbers (quality-only, warmup-coverage degradation, not correctness), and `DesignPersistence`'s raw-ordinal enum serialization contradicting its own migration comment (needs a schema-v32 decision). Re-check staleness (1-sprint rule) before the tag; release notes reference it per ADR-037 D6.
5. **Release mechanics** per ADR-037: D6-structure release notes, `PublicAPI.Unshipped.txt` → `Shipped.txt` (note: A.116 added one new entry, `SimulatedAnnealingOptimizer.MigrateFrom` 3-arg overload — carry it through), `git tag -a v0.1.0 …`. Marks framing-B Phase 3 (Tracks C.1 + C.2) + the framing-C ANT.W1–W7 antenna parity block as a coherent shippable release.

## Later — demand-gated unlocks (not actively pursued)

Framing-B has closed, so these are technically actionable — but under the current deliberate pause none is queued. Each unlocks on concrete demand:

**Framing C (mission / system designer):**

- **Cross-pillar coupling** — NEP (NTR-Brayton + EP) via SI adapters per ADR-035 (D1 revised by ADR-035a).
- **End-to-end mission demos** — first integrated vehicle binding multiple pillars (e.g. lunar transfer with NTR + EP stage, or CPU cooler with heat-pipe + LPBF print).
- **Avalonia migration Phase 2+** — UX parallel concern; the ADR-002 / ADR-027 exit path.

**Framing A (rocket depth toward hot-fire):**

- **Real-gas EOS** — `FLUID_MIXTURE` for combustion products above ~ 3500 K. Demand-gated until LOX/CH4 hot-fire data shows > 5 % T_aw error.
- Additional propellant pairs + preburner axial-march (see Demand-driven below).

## Demand-driven (no slot until triggered)

- **Additional propellant pairs** — N2O4/MMH, H2O2/RP-1, N2O4/N2H4. Blocked on CEA table data.
- **Preburner axial march** — current `PreburnerCooling` is lumped-parameter. Build per-station solver only if a real design lands near `PREBURNER_WALL_TEMP` and the lumped estimate can't discriminate.
- **CI runner parallelization** — multi-runner install at 2 / 3 / 6 instances. Revisit when CI wall-clock becomes a daily annoyance or a second machine arrives. The runner-*offline* SPOF is a different risk: it is mitigated by the Linux leg (Sprint A.110) plus the coverage-parity backfill (Now §2), not by more Windows runners.
- **Marine hybrid ramjet** (Al/H₂O underwater) — scoped as MHR.W1–W5 but not started. Deferred under the current pause; pick up only if marine propulsion is confirmed strategic.
- **Performance P20** — TPMS implicit bounds hint. PicoGK-API-blocked.

## Declined

These were evaluated and explicitly declined; do not reconsider without new evidence:

- **GPU voxelization** — Declined for production adoption. Reconsider only when a deterministic GPU prototype produces bit-identical voxel fingerprints versus the CPU path on at least one mature fixture. No sprint allocated to building such a prototype — opportunistic only.
- Differentiable physics (empirical-correlation port cost)
- Thermoacoustic instability solver (data-starved)
- LPBF slicer integration (vendor-locked)
- Another roadmap document (recurring failure mode — this file covers the design space)
- **Premature** UI rewrite. The Avalonia migration is the official exit path (ADR-002, ADR-027); it is demand-gated as a framing-C unlock (see Later), not a wholesale rewrite. Port one form per non-pillar sprint when it is picked up.
- JSON serialization replacement (schema-versioning works)

## Claiming work

Active items live as [GitHub Issues](https://github.com/poetac/voxelforge/issues). Self-assign via `gh issue edit <N> --add-assignee @me` before starting; PR closes the issue. See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the protocol.

Tracker state (2026-07-08): the 33 open issues are all 2026-06-22 restorations from the retired tracker. Hygiene notes:

- **#2 (enable GitHub Pages) is satisfied by observation** — Pages has been enabled and deploying green since 2026-06-18; close it.
- **#46 is one-third fixed** — the Horn `sdCappedCone` factor-2 slip landed in Sprint A.112; the Helical not-a-helix and Patch RF/built-geometry mismatch remain open (both Windows-leg voxel code).
- **#30 and #32 both claim the VFA003 diagnostic ID** — de-conflict before either lands.
- The v0.1.0 checklist deliberately lives in this file, not in a tracking issue (per Declined: no separate planning doc; issues are the queue, this file is the design space).
