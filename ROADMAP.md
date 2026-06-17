# Roadmap

Forward-looking view of what's planned, in recommended-pickup order. For the state of shipped work, see [`CHANGELOG.md`](CHANGELOG.md).

Items scoped as 1–3 day sprints at single-dev cadence unless noted. Shippable-at-any-step — you can stop after any item and the project remains coherent.

## Strategic framing

The project pursued **Framing B (multi-physics breadth + depth)** through May 2026 — polish + depth on the existing pillar catalogue (6 production pillars + 22 Wave-1 internal pillars), no new pillars until the catalogue was hardened. All three phases — Phase 1 (stabilize), Phase 2 (depth/polish), Phase 3 (coverage backfill) — are complete (see the Done section below).

With framing-B closed, the project is in a **deliberate pause**: the immediate work is the public release (security hardening + doc trim, then the v0.1.0 tag), after which development is demand-driven rather than committed to a new multi-month track.

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

## Now — public release + v0.1.0 tag

Framing-B is closed; the active work is shipping the repository publicly as a portfolio piece.

1. **Public-release hardening — landed.** Fork-PR security guards on every self-hosted-runner CI job + de-personalized docs/tooling. This was the gate to flip the repo public.
2. **Flip the repository public** and **enable GitHub Pages** — both one-click in repo Settings (Pages → Source → GitHub Actions). The Pages workflow is already committed.
3. **Cut the v0.1.0 release tag** per ADR-037 (`git tag -a v0.1.0 …`). ADR-037 D7's preconditions are now both resolved, so the tag is unblocked. Marks framing-B Phase 3 (Tracks C.1 + C.2) + the framing-C ANT.W1–W7 antenna parity block as a coherent shippable release.

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
- **CI runner parallelization** — multi-runner install at 2 / 3 / 6 instances. Revisit when CI wall-clock becomes a daily annoyance or a second machine arrives.
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
