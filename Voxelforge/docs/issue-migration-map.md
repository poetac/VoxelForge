# Issue migration map — voxelforge-old → voxelforge

On 2026-06-22 the **50 open issues** from the original development tracker
(`poetac/voxelforge-old`, the predecessor of this repository) were restored
into this repo. When the public `poetac/voxelforge` repository was recreated,
its issue history did not carry over — every `#NNN` reference in the codebase
and docs (e.g. the dead links in
[`physics-cascade-status.md`](physics-cascade-status.md)) pointed at issues
that 404'd here. This table is the crosswalk back to the live issues.

## How to use this table

- A code comment or doc that says `#480` (an *original-tracker* number) maps to
  the **New** column on the same row.
- Each restored issue carries a provenance line linking back to its
  `voxelforge-old` source, and its original number is preserved there.
- Cross-references **between the 50 restored issues** were rewritten in-place to
  the new numbers, so links inside the restored issues already resolve here.
  References to items *not* in the migrated set (PRs, closed issues) were left
  pointing at their original numbers.

## Caveats

- **Numbers changed.** GitHub assigns numbers sequentially and `#1` was already
  taken (a Dependabot PR), so the restored issues are `#2`–`#51`. Original
  numbers are preserved in each issue's provenance line and in the table below.
- **Open issues only.** Closed issues were not restored.
- **Comment threads were not migrated** — only issue bodies. The comment counts
  are noted in each provenance line; the originals remain on `voxelforge-old`.
- **Label colours / descriptions:** label *names* were applied on creation
  (GitHub auto-created any missing). To restore the original 29 labels' colours
  and descriptions, run [`tools/restore-labels.sh`](../../tools/restore-labels.sh)
  (requires `gh auth login`).
- **Verbatim fidelity.** A few originals contained quirks from how they were
  first posted (backslash-escaped quotes; a handful of mojibake `?` where
  arrows/symbols were lost at creation time). These were preserved as-is rather
  than silently "corrected".

## Map (original tracker → this repo)

| New | Original | Source | Title |
| --- | --- | --- | --- |
| [#2](https://github.com/poetac/voxelforge/issues/2) | `#349` | [voxelforge-old#349](https://github.com/poetac/voxelforge-old/issues/349) | Site: enable GitHub Pages (GitHub Actions source) |
| [#3](https://github.com/poetac/voxelforge/issues/3) | `#418` | [voxelforge-old#418](https://github.com/poetac/voxelforge-old/issues/418) | Performance P20 — TPMS implicit bounds hint (PicoGK API blocked) |
| [#4](https://github.com/poetac/voxelforge/issues/4) | `#456` | [voxelforge-old#456](https://github.com/poetac/voxelforge-old/issues/456) | Sprint C.4 / future: real-gas EOS for combustion products above ~3500 K |
| [#5](https://github.com/poetac/voxelforge/issues/5) | `#480` | [voxelforge-old#480](https://github.com/poetac/voxelforge-old/issues/480) | CFD: verify Sutherland-S placeholder values in SutherlandFromCea (Sprint C.2 follow-on) |
| [#6](https://github.com/poetac/voxelforge/issues/6) | `#482` | [voxelforge-old#482](https://github.com/poetac/voxelforge-old/issues/482) | UI: Avalonia migration Phase 2+ — port remaining WinForms surfaces |
| [#7](https://github.com/poetac/voxelforge/issues/7) | `#485` | [voxelforge-old#485](https://github.com/poetac/voxelforge-old/issues/485) | CFD: per-pair MU_REF override in Su2ConfigWriter (Sprint C.2 follow-on) |
| [#8](https://github.com/poetac/voxelforge/issues/8) | `#494` | [voxelforge-old#494](https://github.com/poetac/voxelforge-old/issues/494) | SI + EC: lift INTERNAL types to public + PublicAPI.Unshipped.txt update |
| [#9](https://github.com/poetac/voxelforge/issues/9) | `#502` | [voxelforge-old#502](https://github.com/poetac/voxelforge-old/issues/502) | NEP cross-pillar coupling — NuclearBraytonComponent + ElectricPropulsionComponent SI adapters |
| [#10](https://github.com/poetac/voxelforge/issues/10) | `#556` | [voxelforge-old#556](https://github.com/poetac/voxelforge-old/issues/556) | [audit] Test coverage gaps — ~32 missing test files across 7 production roots |
| [#11](https://github.com/poetac/voxelforge/issues/11) | `#557` | [voxelforge-old#557](https://github.com/poetac/voxelforge-old/issues/557) | [audit] Performance: top-5 hot-path fixes to drop SA Gen0 pressure 70-80% |
| [#12](https://github.com/poetac/voxelforge/issues/12) | `#559` | [voxelforge-old#559](https://github.com/poetac/voxelforge-old/issues/559) | [audit] PublicAPI hygiene: RS0026 risk + 3 over-public utilities + BOM drift |
| [#13](https://github.com/poetac/voxelforge/issues/13) | `#563` | [voxelforge-old#563](https://github.com/poetac/voxelforge-old/issues/563) | [audit] CI resilience: single self-hosted runner SPOF + bench-regression baseline picker |
| [#14](https://github.com/poetac/voxelforge/issues/14) | `#623` | [voxelforge-old#623](https://github.com/poetac/voxelforge-old/issues/623) | Decision-cleanup omnibus PR — 17 amendments + 2 small code changes |
| [#15](https://github.com/poetac/voxelforge/issues/15) | `#624` | [voxelforge-old#624](https://github.com/poetac/voxelforge-old/issues/624) | Add VoxelBuild smoke tests for all voxel generators |
| [#16](https://github.com/poetac/voxelforge/issues/16) | `#625` | [voxelforge-old#625](https://github.com/poetac/voxelforge-old/issues/625) | Voxelforge.Analyzers: cross-family contract enforcement + test-naming rule |
| [#17](https://github.com/poetac/voxelforge/issues/17) | `#627` | [voxelforge-old#627](https://github.com/poetac/voxelforge-old/issues/627) | Add signed violation magnitudes to IObjective for non-SA optimizers |
| [#18](https://github.com/poetac/voxelforge/issues/18) | `#629` | [voxelforge-old#629](https://github.com/poetac/voxelforge-old/issues/629) | Sunset legacy FeasibilityGate.Evaluate() if-chain; complete migration to GateRegistry |
| [#19](https://github.com/poetac/voxelforge/issues/19) | `#630` | [voxelforge-old#630](https://github.com/poetac/voxelforge-old/issues/630) | Add conformance test parsing ADR-036 validation-tolerance table |
| [#20](https://github.com/poetac/voxelforge/issues/20) | `#638` | [voxelforge-old#638](https://github.com/poetac/voxelforge-old/issues/638) | Add per-fixture justification to published-engine validation tolerance bands |
| [#21](https://github.com/poetac/voxelforge/issues/21) | `#641` | [voxelforge-old#641](https://github.com/poetac/voxelforge-old/issues/641) | Opportunistic backlog: small policy refinements (fold into adjacent PRs) |
| [#22](https://github.com/poetac/voxelforge/issues/22) | `#642` | [voxelforge-old#642](https://github.com/poetac/voxelforge-old/issues/642) | Performance P21 — Parallelize per-station wall-T cooling solve |
| [#23](https://github.com/poetac/voxelforge/issues/23) | `#643` | [voxelforge-old#643](https://github.com/poetac/voxelforge-old/issues/643) | Performance P10 — Strip Math.Max guards from wall-T iteration loop |
| [#24](https://github.com/poetac/voxelforge/issues/24) | `#653` | [voxelforge-old#653](https://github.com/poetac/voxelforge-old/issues/653) | Nightly cross-pillar fixture validation consolidated report |
| [#25](https://github.com/poetac/voxelforge/issues/25) | `#654` | [voxelforge-old#654](https://github.com/poetac/voxelforge-old/issues/654) | Weekly multi-seed SA convergence study |
| [#26](https://github.com/poetac/voxelforge/issues/26) | `#655` | [voxelforge-old#655](https://github.com/poetac/voxelforge-old/issues/655) | Weekly Pareto frontier characterization (NSGA-II per preset) |
| [#27](https://github.com/poetac/voxelforge/issues/27) | `#656` | [voxelforge-old#656](https://github.com/poetac/voxelforge-old/issues/656) | Nightly CFD verification suite |
| [#28](https://github.com/poetac/voxelforge/issues/28) | `#658` | [voxelforge-old#658](https://github.com/poetac/voxelforge-old/issues/658) | GPU-1 — Blender renderer CUDA / OptiX backend |
| [#29](https://github.com/poetac/voxelforge/issues/29) | `#660` | [voxelforge-old#660](https://github.com/poetac/voxelforge-old/issues/660) | GPU-3 — SU2 CFD CUDA build investigation (spike) |
| [#30](https://github.com/poetac/voxelforge/issues/30) | `#726` | [voxelforge-old#726](https://github.com/poetac/voxelforge-old/issues/726) | VFA003: build-time cross-family IEngine contract enforcement (#625 follow-on) |
| [#31](https://github.com/poetac/voxelforge/issues/31) | `#739` | [voxelforge-old#739](https://github.com/poetac/voxelforge-old/issues/739) | #557 item 1 Phase 4: flatten ComponentNetwork port-value dicts to double[] |
| [#32](https://github.com/poetac/voxelforge/issues/32) | `#757` | [voxelforge-old#757](https://github.com/poetac/voxelforge-old/issues/757) | Add VFA analyzer enforcing the per-fixture tolerance-band rationale convention (#745 follow-on) |
| [#33](https://github.com/poetac/voxelforge/issues/33) | `#759` | [voxelforge-old#759](https://github.com/poetac/voxelforge-old/issues/759) | #743 follow-up: refine per-pillar BreachDirection annotations from foundation AboveLimit defaults |
| [#34](https://github.com/poetac/voxelforge/issues/34) | `#760` | [voxelforge-old#760](https://github.com/poetac/voxelforge-old/issues/760) | #743 follow-up: wire useSoftPenalty into HybridSACmaEsOrchestrator (CMA-ES phase only) |
| [#35](https://github.com/poetac/voxelforge/issues/35) | `#777` | [voxelforge-old#777](https://github.com/poetac/voxelforge-old/issues/777) | MHR.W1 — Marine hybrid ramjet: project scaffold + Al/H₂O thermodynamic state |
| [#36](https://github.com/poetac/voxelforge/issues/36) | `#778` | [voxelforge-old#778](https://github.com/poetac/voxelforge-old/issues/778) | MHR.W2 — Marine hybrid ramjet: two-phase nozzle flow solver |
| [#37](https://github.com/poetac/voxelforge/issues/37) | `#779` | [voxelforge-old#779](https://github.com/poetac/voxelforge-old/issues/779) | MHR.W3 — Marine hybrid ramjet: seawater ram inlet + depth operating conditions |
| [#38](https://github.com/poetac/voxelforge/issues/38) | `#780` | [voxelforge-old#780](https://github.com/poetac/voxelforge-old/issues/780) | MHR.W4 — Marine hybrid ramjet: fuel grain regression + combustion completeness |
| [#39](https://github.com/poetac/voxelforge/issues/39) | `#781` | [voxelforge-old#781](https://github.com/poetac/voxelforge-old/issues/781) | MHR.W5 — Marine hybrid ramjet: remaining gates + published-design validation fixture |
| [#40](https://github.com/poetac/voxelforge/issues/40) | `#825` | [voxelforge-old#825](https://github.com/poetac/voxelforge-old/issues/825) | Audit ITU-R P.838-3 k_H/α_H rain-attenuation table in ItuAtmosphericModels.cs |
| [#41](https://github.com/poetac/voxelforge/issues/41) | `#828` | [voxelforge-old#828](https://github.com/poetac/voxelforge-old/issues/828) | Feasibility-gate fire-rate study — characterize utility of all 177 ConstraintIds |
| [#42](https://github.com/poetac/voxelforge/issues/42) | `#829` | [voxelforge-old#829](https://github.com/poetac/voxelforge-old/issues/829) | Optimizer-portfolio bake-off — empirical SA vs CMA-ES vs Bayesian vs Hybrid (close ADR-023 with data) |
| [#43](https://github.com/poetac/voxelforge/issues/43) | `#831` | [voxelforge-old#831](https://github.com/poetac/voxelforge-old/issues/831) | Auto-baseline-refresh detector — uniform-drift heuristic auto-PRs baseline refresh |
| [#44](https://github.com/poetac/voxelforge/issues/44) | `#832` | [voxelforge-old#832](https://github.com/poetac/voxelforge-old/issues/832) | Pre-compute lookup tables for runtime hot paths (ITU-R, IGRF, atmosphere) — compute-once / runtime-cheap |
| [#45](https://github.com/poetac/voxelforge/issues/45) | `#835` | [voxelforge-old#835](https://github.com/poetac/voxelforge-old/issues/835) | j85-turbojet bench Isp drift: 5529 → 4322 s after #432 (turbojet afterburner augmentation) |
| [#46](https://github.com/poetac/voxelforge/issues/46) | `#849` | [voxelforge-old#849](https://github.com/poetac/voxelforge-old/issues/849) | Antenna voxel builders — Horn sdCappedCone, Helical not-a-helix, Patch RF/built mismatch (codex review PR #822) |
| [#47](https://github.com/poetac/voxelforge/issues/47) | `#850` | [voxelforge-old#850](https://github.com/poetac/voxelforge-old/issues/850) | Nightly drift detection — false-positive green when baselines missing or no kernels match (codex review PR #840, #841, #843) |
| [#48](https://github.com/poetac/voxelforge/issues/48) | `#852` | [voxelforge-old#852](https://github.com/poetac/voxelforge-old/issues/852) | BenchSweep — reject design variables gated off by the selected preset (codex review PR #834) |
| [#49](https://github.com/poetac/voxelforge/issues/49) | `#854` | [voxelforge-old#854](https://github.com/poetac/voxelforge-old/issues/854) | VFD016 CheckMathFClamp — bind by symbol, not identifier text (codex review PR #837) |
| [#50](https://github.com/poetac/voxelforge/issues/50) | `#855` | [voxelforge-old#855](https://github.com/poetac/voxelforge-old/issues/855) | Generate-FixtureReport.ps1 — status cell mangled to `❌ FAIL True` when failures occur (codex review PR #844) |
| [#51](https://github.com/poetac/voxelforge/issues/51) | `#868` | [voxelforge-old#868](https://github.com/poetac/voxelforge-old/issues/868) | PicoGK native shutdown crash (0xC0000005) intermittently false-reds the rocket-tests CI job |
