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

- **Phase 1 (Stabilize)** — CI green, docs trustworthy, GitHub Pages enabled 2026-06-18 (issue #2 closed).
- **Phase 2 (Depth/polish)** — EP Wave-3 (VASIMR / FEEP / HDLT), Electrolyser Wave-3 (SOEC), CN-NEWTON A-stability, IObjective wrappers, VFD011 analyzer fix, SI integrator polish queue. Shipped Sprints A.55–A.64.
- **Phase 3 (Coverage backfill)** — Track C.1: second-anchor fixtures for all 10 remaining Wave-1 pillars (HeatExchanger, Radiator, HeatPipe, Compressor, Pump, Refrigeration, Tankage, Aerostructures, ChemicalReactor, SolarThermal; Stirling deferred at the time — see Now §2, resolved Sprint A.130). Track C.2: voxel-pipeline builders for Flywheel, Tankage, HeatPipe, Refrigeration, Aerostructures, Antenna. Shipped Sprints A.66–A.83. Umbrella tracking issues for C.1 + C.2 closed.

See `CHANGELOG.md` for sprint-level detail.

## Now — v0.1.0 release readiness

Framing-B is closed and the repository **is public**: initial public release 2026-06-17, GitHub Pages enabled 2026-06-18 with green deploys (issue #2 closed 2026-07-13). The planned "harden → flip → tag" sequence deliberately paused before the tag: four post-public red-team rounds (CHANGELOG Sprints A.111, A.112, A.116, A.120–A.122) surfaced real correctness bugs that green CI had not caught — several safety-relevant (inverted transpiration-cooling effectiveness, a never-invoked `ValidateSelf` letting NaN nuclear designs report feasible, HET designs violating energy conservation, a combustion-stability screen whose Fail branch is unreachable, a Bayesian-optimizer GP-poisoning bug, a structural gap that let aerospike designs skip their own safety gates during optimization). All are fixed or explicitly documented; round 4 (see criterion 1) closed with the exit rule satisfied — the discovery rate finally dried up on the last surface swept. ~3,900 of ~5,900 test methods (~66 %, the PicoGK voxel + WinForms suites in `Voxelforge.Tests`) still execute only on the often-offline self-hosted Windows runner, so "green CI" currently attests the Linux two-thirds of the suite.

Cut the tag when the criteria below hold. They extend — without amending — ADR-037 D7, whose original preconditions are both resolved:

1. **Red-team round 4 — SATISFIED.** Exit rule (set in A.119): **(a)** a round finishes the first full pass over the not-yet-swept surfaces, **(b)** every finding from that pass is fixed-or-documented, **(c)** a sampled re-audit of already-swept surfaces comes back clean. **All three now hold.** Round 4 spanned three sprints (A.120–A.122) and repeated infrastructure interruptions (subagent session-limit deaths mid-round, each followed by either a clean agent recovery or a solo continuation) but completed the entire not-yet-swept list from A.119: the gate-registry surface (7 files — `RocketGates.cs`, `MonopropGates.cs`, `AirbreathingGateInput/Gates/GateRegistry.cs`, `MarineGates.cs`, `NuclearGates.cs`), the CLI/`Program.cs` dispatch layer (+ `AirbreathingForm.cs` + 5 `*.StlExporter/Program.cs` entry points), `Voxelforge.Core/IO/` (8 files + the Nuclear IO variant), electric-propulsion + nuclear optimization wiring, and rocket + airbreathing + marine optimization wiring (`RegenChamberOptimization.cs`, `AerospikeOptimization.cs`, `RegenObjective.cs`, 5 airbreathing objectives + orchestrator, 2 marine objectives + orchestrator) are all swept. **(c)**: NSGA-III niching/normalization + the combustion-stability composite screen (A.120) and two additional leads on the rocket-wiring surface (A.122 — a film-cooling-precedence question and a sea-level-Isp display-only formula, both personally spot-verified) all came back clean on re-read. Eight real hard-gate-class defects found and fixed, each with a fail-on-old/pass-on-new test (Linux-verified except one Windows-only-to-execute fix, math/logic-verified by hand per the Horn-antenna precedent): `MarineGates.HULL_CG_CB_OFFSET_LARGE` missing `Math.Abs()`; `CfdFieldExport`'s VTK point data written in the wrong axis order (spatially scrambled every exported field whenever Nx≠Nz); `CfdFieldExport` hardcoding 0.8 mm gas-side wall thickness instead of reading the design's actual value; `AirbreathingForm`'s `--engine-kind` mapping silently dropping 2 of 12 engine kinds; `ResistojetObjective.Build` missing the baseline-`Kind` guard all 5 sibling EP objectives have (a mismatched-Kind baseline silently optimized the wrong physics pipeline); `NtrObjective.ScoreNegativeIsp` producing `NaN` instead of `+∞` for BimodalNtr+Electric-mode designs, NaN-poisoning the SA optimizer's acceptance logic; and `RegenChamberOptimization.Evaluate` computing the aerospike scoring sidecar but never invoking `AerospikeFeasibility.Evaluate`, leaving aerospike's 5 dedicated hard/advisory gates structurally unreachable from the main SA-facing optimization path. Plus one documented-not-fixed: `RDE_ANNULUS_FILL_STARVED`'s inter-wave-period formula is algebraically wave-count-independent (needs a data-plumbing change, not a same-file fix). Five dead-end leads chased and refuted across the round. **No further red-team round is required before the tag.** A round 5 stays available on demand if a future change reopens one of these surfaces, but nothing currently gates the tag on it.
2. **Linux coverage parity for pure-Core physics — SATISFIED, 23 of 23.** All 23 Wave-1 pillar solver families now have Linux-leg regression tests in `Voxelforge.Core.Tests` (was 4 at the start of this pass; 16 before wave 4) — HeatExchanger, Radiator, Tankage, Refrigeration, Flywheel, Aerostructures, HydrogenStorage, HeatPipe, Thermoelectric, SolarThermal, Pump, Compressor, ElectricMotor, Battery, Photovoltaic, PowerGen(PemFuelCell), and (wave 4, Sprint A.118) WindTurbine, Hydroelectric, Electrolyser (4 sub-variants: AEM/PEM/Alkaline/SOEC — count as one family), Antenna, ChemicalReactor, HybridRocket, and (Sprint A.130, issue #84) Stirling — unblocked by the West's-number MEP-model refinement that replaced the Wave-1 cluster fit (see CHANGELOG Sprint A.130). Voxel/UI suites stay Windows-bound by nature — this criterion covers what *can* move.
3. **One green Windows-leg run before the tag.** A.111/A.112 shipped Windows-only fixes (voxel SDFs, setup wizard, analyzer rules) whose regression tests are math-verified but have never executed. A full green `ci.yml` run converts "shipped blind" into "validated". This is the only runner-blocked criterion — consistent with the "runner restored" close condition already encoded in issue #14. The self-hosted machine has been offline for every sprint since the public release; #13 and #51 already hardened CI *against* that SPOF (the Linux leg absorbs the PicoGK-free majority of the suite, and the native-shutdown-crash false-red is tolerated), but neither closes the gap for the ~66 % of test methods that only execute on Windows (see intro above). **Spike candidate worth one small sprint before continuing to wait on hardware**: PicoGK ships as a NuGet `<PackageReference>` in `Voxelforge.Voxels.csproj`, not a vendored/local path, and CLAUDE.md pitfall #8's `Library`/`LibraryScope` pattern is already headless-safe for repeated xUnit construction — both suggest a GitHub-hosted `windows-latest` runner could execute `Voxelforge.Tests` (or at minimum its non-voxel analyzer/wizard subset) without the self-hosted machine at all. If large voxel fixtures prove too memory-hungry for the hosted runner's allowance, that narrows what genuinely needs self-hosted hardware instead of blocking everything on it by default. Tracked as issue #83.
4. **Known-gaps ledger current at the cut — SATISFIED, refreshed Sprint A.130.** [`physics-cascade-status.md`](Voxelforge/docs/physics-cascade-status.md) § Documented gaps records every remaining calibration-blocked gap: Crocco stability screen, VASIMR Isp ceiling, bimodal-Hybrid power split, HET beam-current coupling, `DesignPersistence` raw-ordinal enums. Resolved and dropped from the ledger since A.120: `RDE_ANNULUS_FILL_STARVED`'s wave-count-independent formula (#75, A.127), `SobolSequence` Joe-Kuo direction numbers (#80, A.129), Stirling MEP (#84, A.130). Re-check the 1-sprint staleness rule immediately before cutting the tag; release notes reference this ledger per ADR-037 D6.
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

**v0.1.x known-gaps burn-down** (calibration/design-intent items, not framing-scoped):

Everything below is fully recorded in [`physics-cascade-status.md`](Voxelforge/docs/physics-cascade-status.md) § Documented gaps (Now §4 confirms the ledger is current), and — as of 2026-07-15 — each item now has its own claimable tracker issue, and each of #75–#84 carries a self-contained **implementation guide comment** (exact file:line anchors, change plan, fail-on-old test recipe, and the container build/test commands) so pickup needs no re-discovery:

- Crocco n-τ stability screen sign flip + recalibration against validated-engine fixtures (#76).
- HET beam-current/discharge-current coupling + BPT-4000 / SPT-100 / HiVHAc recalibration (#78).
- Bimodal NTR Hybrid-mode reactor power split (design-intent decision + fixture recalibration) (#77).
- VASIMR Isp ceiling calibrated against VX-200 data (#79).
- `DesignPersistence` schema-v32 enum-string migration decision (#81).

Demand-gated like the rest of Later — pick up piecemeal as calibration data / decisions become available, not as a single tracked sprint.

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

Tracker state (2026-07-15): 42 open issues — the 32 surviving restorations plus ten filed 2026-07-15 (#75–#84) giving every ledgered known-gap, the damper-volume follow-on, and the windows-latest CI spike a claimable tracker entry (previously they lived only in docs). Notes:

- **#2 (enable GitHub Pages) closed 2026-07-13** — satisfied by observation; Pages has been enabled and deploying green since 2026-06-18.
- **#46 retitled to its actual one-third-fixed status** — the Horn `sdCappedCone` factor-2 slip landed in Sprint A.112 (commit `27c113e`); its Linux math-mirror test passes, the PicoGK smoke test itself awaits the Windows runner. The Helical not-a-helix and Patch RF/built-geometry mismatch remain open (both Windows-leg voxel code).
- **#30 and #32's VFA-ID collision is worse than the ID number alone, and is now documented on both issues**: VFA004 and VFA005 shipped for unrelated purposes (test-naming convention; ambient-pragma-suppression) after #32 was filed, so #32's entire proposed VFA003–005 band needs renumbering regardless of which of #30/#32 lands first — not just the VFA003 clash with #30.
- The v0.1.0 checklist deliberately lives in this file, not in a tracking issue (per Declined: no separate planning doc; issues are the queue, this file is the design space).
