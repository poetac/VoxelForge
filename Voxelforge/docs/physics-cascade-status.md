# Physics-cascade status

Living doc summarizing the known physics-correctness gaps surfaced
during the post-#544 runner-outage triage. Each entry is a pinned
test-failure that exists *to* surface the gap; do not "fix" the
gap by loosening the test threshold without first understanding
which physics is broken.

> Updated 2026-07-14: No active pinned-failure entries. Four post-public
> red-team rounds (CHANGELOG Sprints A.111, A.112, A.116, A.120–A.123) have
> surfaced 30+ correctness defects and fixed nearly all in the same sprint
> with a fail-on-old / pass-on-new regression test — none ever became a
> pinned failure here. Their calibration-blocked / design-intent residue is
> tracked in § Documented gaps below (round 3 added the Sobol and
> DesignPersistence entries; round 4 added the RDE annulus-fill-time gate
> degeneracy — a data-plumbing gap, not a calibration one, but likewise not
> a same-file fix); fixes whose regression tests target the
> offline Windows leg are queued in § Windows-leg validation pending. Two
> known CI/infrastructure flakes documented below (§ Known CI/infrastructure
> flakes) — neither is a physics regression. **Round 4 completed** across
> Sprints A.120–A.123, surviving three separate mid-round subagent-session-
> limit failures via solo continuation each time — ROADMAP → Now §1 marks
> it SATISFIED; no further red-team round currently gates the v0.1.0 tag.
> Refresh whenever an entry's fix lands (drop the entry, add a
> CHANGELOG sprint line). Stale-after: 1 sprint past the last refresh.

## Layout

Each entry below carries:

1. **What's wrong** — symptom from a fixture-test assertion failure.
2. **Where** — the file + line range that holds the broken physics.
3. **Hypothesis** — the suspected root cause (when known).
4. **Fix candidates** — sketches to choose between when a fix lands.
5. **Tracking issue** — open GitHub issue + PR history.

---

## Active failures

*None.* All previously-tracked physics-correctness gaps are resolved.

---

## Documented gaps (calibration-blocked — known wrong or dead, deliberately not auto-fixed)

Unlike the pinned-failure entries this doc was built for, these have **no
failing test**: each was found by the A.111/A.112 red-team rounds (or is
long-standing) and left unfixed because a bare code fix without
recalibration would break validated fixtures or encode a guess. They are
the release-notes "known issues" list for v0.1.0 (ROADMAP → Now §4).

### Crocco n-τ stability screen is non-discriminating — `STABILITY_FAIL` cannot fire

- **What's wrong:** the growth-rate term `(cos ωτ − 1)` is ≤ 0 for all inputs, so the Fail/Marginal branches are dead and the screen always returns Pass — yet it feeds the hard `STABILITY_FAIL` gate, which is therefore currently a no-op.
- **Where:** `Voxelforge.Core/Combustion/Stability/CroccoNTau.cs`.
- **Why not auto-fixed:** the canonical sensitive-time-lag sign is `+(1 − cos ωτ)`, but a bare flip marks flight-proven stable engines (RL10, LOX/CH4, LOX/RP1: |σ| ≈ 0.02–0.09 > `FailThreshold` 0.02) as infeasible, breaking published-engine validation.
- **Fix path:** restore the sign **and** recalibrate the threshold (ideally after adding the omitted acoustic-damping term) against the validated-engine fixtures. Detail: CHANGELOG Sprint A.111.
- **Tracking issue:** #76.

### VASIMR Isp is unbounded at low ionisation fraction

- **What's wrong:** energy balances (jet power ≤ η_nozzle·P_icrh), but at very low η_i the per-ion energy — hence exit velocity / Isp — grows without ceiling; a constructed low-η_i design reports Isp > 100 000 s yet stays feasible (only the advisory `VASIMR_IONIZATION_FRACTION_LOW` fires). Reachable only via direct/CLI/deserialised construction — no VASIMR optimiser path exists.
- **Where:** `Voxelforge.ElectricPropulsion.Core/Solvers/HeliconIcrhMagneticNozzleModel.cs`.
- **Fix path:** a hard cap needs an empirical ion-energy/Isp ceiling calibrated against VX-200 data. Detail: CHANGELOG Sprint A.112.
- **Tracking issue:** #79.

### Bimodal NTR Hybrid mode double-counts reactor power

- **What's wrong:** in `BimodalMode.Hybrid` the thrust cycle heats propellant with the full `ReactorThermalPower_MW` while the Brayton loop *also* taps the full reactor power — a design can draw ~1.13× what the reactor produces. The documented ~20 % thrust / 80 % electric throttle split is never applied. Pure-Thrust and pure-Electric modes are unaffected.
- **Where:** `Voxelforge.Nuclear.Core/NuclearOptimization.cs` (Hybrid dispatch pipeline).
- **Fix path:** split reactor power between the two consumers (pipeline re-ordering); changes Hybrid thrust/Isp, so it is a design-intent + fixture-recalibration decision. Detail: CHANGELOG Sprint A.112.
- **Tracking issue:** #77.

### HET thrust model is uncoupled from discharge current (gate-guarded)

- **What's wrong:** the 0-D Hall-thruster model computes thrust from V_d and ṁ but not I_d, while P_d = V_d·I_d — so a low-I_d corner used to report physically impossible η_T > 1 designs as feasible. Sprint A.112 added the hard `HET_POWER_BALANCE_VIOLATED` conservation gate, which now rejects those corners; the *model* defect (beam current not coupled to I_d) remains.
- **Where:** model `Voxelforge.ElectricPropulsion.Core/Plasma/HetPlasmaState.cs` (`BuschDischargeModel`); gate `Voxelforge.ElectricPropulsion.Core/ElectricPropulsionFeasibility.cs`.
- **Fix path:** couple beam current to I_d and recalibrate against the BPT-4000 / SPT-100 / HiVHAc anchors. Detail: CHANGELOG Sprint A.112.
- **Tracking issue:** #78.

### Stirling free-piston output over-predicted 10–100×

- **What's wrong:** the Wave-1 cluster fit over-predicts free-piston power by 1–2 orders of magnitude; no validation fixture exists (the second-anchor was deferred out of Track C.1 for exactly this reason).
- **Where:** `Voxelforge.Core/Stirling/StirlingSolver.cs`.
- **Fix path:** MEP-model refinement (STR.W2) before a defensible fixture lands. Tracked in ROADMAP (Done → Stirling deferred) and issue #10's fixture list.
- **Tracking issue:** #84 (model work; #10 covers only the deferred fixture).

### Antenna voxel builders: Helical + Patch geometry defects (Windows-leg)

- **What's wrong:** the Helical SDF builds disconnected toroidal rings instead of a continuous helix; the Patch builder reports RF metrics from the unfloored design while building geometry from floored dimensions. (The third finding in the same review — Horn `sdCappedCone` factor-2 — was fixed in Sprint A.112.)
- **Where:** `Voxelforge.Voxels/Antenna/HelicalAntennaVoxelBuilder.cs`, `Voxelforge.Voxels/Antenna/PatchAntennaVoxelBuilder.cs` (net9.0-windows — not built on the Linux leg).
- **Tracking issue:** #46.

### `SobolSequence` direction numbers are mis-transcribed vs. Joe-Kuo (quality, not correctness)

- **What's wrong:** dimension 3 pairs `a = 2` with `s = 4` (`m = {1,1,3,3}`), whose decoded polynomial x⁴+x²+1 = (x²+x+1)² over GF(2) is *reducible* — never a valid Sobol polynomial; one row's `a` was fused with the next row's `m`. Dims 4–7 match no genuine Joe-Kuo row, and the dims ≥ 8 fallback is neither Sobol nor Halton despite the header's claim. Output stays deterministic and in [0, 1), so the blast radius is warmup-coverage *quality* (MultiChain/Bayesian seeding low-discrepancy property) — not a physics or feasibility defect.
- **Where:** `Voxelforge.Core/Optimization/SobolSequence.cs`.
- **Fix path:** transcribe the genuine `new-joe-kuo-6` table plus a property test (m_i odd, m_i < 2^i, primitive polynomial). Detail: CHANGELOG Sprint A.116.
- **Tracking issue:** #80.

### `DesignPersistence` stores enums as raw ordinals, not strings (data-integrity, not physics)

- **What's wrong:** no `JsonStringEnumConverter` is registered, contradicting the v24→v25 migration comment that claims string serialisation. Today's enums are append-only so round-trip is correct, but any future insertion/reorder would silently remap every saved design's topology/damper/igniter fields with no migration hook and no validation catch.
- **Where:** `Voxelforge.Core/IO/DesignPersistence.cs`.
- **Fix path:** a deliberate schema bump (v32) adding the converter with a numeric→name migration — a design-intent decision, not a bug fix, so documented rather than patched. Detail: CHANGELOG Sprint A.116.
- **Tracking issue:** #81.

### RDE annulus fill-time gate's inter-wave period is wave-count-independent (`RDE_ANNULUS_FILL_STARVED`)

- **What's wrong:** the gate estimates the annulus circumference by inverting `DetonationWaveCount`'s own formula (`C_nominal = N·v_frac·L_cj`) because the true circumference isn't threaded through to `RegenGenerationResult`. Substituting that estimate back into `f_wave = v_wave/C` and `interWavePeriod = 1/(N·f_wave)` makes both `N` and `v_frac` cancel algebraically — the computed threshold collapses to the constant `L_cj/cjSpeed` (≈ 8.333 µs) for every `RdeWaveCount`. Confirmed numerically: N=2 and N=8 both evaluate to exactly 8.333... µs. The Hard gate still fires correctly against `RdeAnnulusFillTime_us` vs. that constant, but has no actual dependence on wave count despite its own Description text citing N and f_wave as if they were independently derived.
- **Where:** `Voxelforge.Core/Optimization/RocketGates.cs` (`EmitRdeAnnulusFillStarved`).
- **Fix path:** thread the true annulus outer circumference from `RdeCombustion`/the RDE design through to `RegenGenerationResult` so the gate computes a genuine per-design inter-wave period instead of reconstructing a self-cancelling proxy from the wave count it's trying to check — a same-file fix isn't possible, the circularity is structural. Found and code-commented (not fixed) in red-team round 4. Detail: CHANGELOG Sprint A.120.
- **Tracking issue:** #75.

---

## Windows-leg validation pending

Sprints A.111/A.112/A.121 shipped fixes in `net9.0-windows` code whose
regression tests **have never executed** — they are math-verified only,
because the self-hosted Windows runner has been offline. When the runner
returns, run these first; a failure here is fresh signal, not pre-existing
green:

- **Horn cone-frustum SDF `2·halfH` fix** — `Voxelforge.Voxels/Antenna/HornAntennaVoxelBuilder.cs` (+ its Linux math-mirror invariant test already passes).
- **NTR `NozzleLength_mm` = bell length fix** — `NtrChamberVoxelBuilder`.
- **Setup-wizard wall-material default sync** — `Voxelforge/UI/SetupWizardForm.cs`.
- **VFD013 write-vs-read + VFD016 qualified-receiver analyzer fixes** — regression tests in `Voxelforge.Tests/Analyzers/` (e.g. `Vfd016AnalyzerTests.cs`).
- The **documented analyzer preventive gaps** (VFD012 instance-`Stopwatch`, VFD005 `.Keys`/`.Values` iteration, generator FQN fast-path) also await the Windows analyzer-test harness before broadened detection can be validated rather than shipped blind (CHANGELOG A.112).
- **`AirbreathingForm`'s `--engine-kind`/ComboBox mapping fix** (red-team round 4, A.121) — `KindToIndex`/`SelectedKind` in `Voxelforge/AirbreathingForm.cs` now cover all 12 `AirbreathingEngineKind` values (previously silently dropped `LiquidAirCycle` + `RotatingDetonation`, falling back to Ramjet with no error); `AirbreathingFormKindCoverageTests.DisplayNameMatchesEnumValue` extended from 10 to 12 cases to actually pin the count its own comment claimed to enforce.

---

## Known CI/infrastructure flakes

These are **not physics regressions**. They appear in CI output but have known,
non-physics root causes. Neither should be treated as a blocker if no physics
code changed.

### SA_Solve_StaysWithinBudget(Maximum) — runner CPU contention

- **Symptom:** `SA_Solve_StaysWithinBudget` with `modeLabel=Maximum` reports
  ~4–6 s elapsed vs. the 3 000 ms budget, causing a test failure.
- **Root cause:** `Maximum` mode sets `chainCount=0` → `DefaultChainCount =
  Math.Clamp(ProcessorCount − 2, 1, 16)`. When both self-hosted runners (A + B)
  execute in parallel, the Ryzen 9 is over-subscribed and the parallel SA chains
  queue behind each other, blowing the wall-clock budget even though the iteration
  count is unchanged.
- **Fix landed (2026-05-24):** `Category=Performance` is now excluded from the
  `rocket-tests` CI filter in `.github/workflows/ci.yml`. The test still runs on
  the dev loop and can be triggered manually:
  `dotnet test Voxelforge.Tests/Voxelforge.Tests.csproj --filter Category=Performance`
- **Why the budget isn't raised:** `SA_Solve_StaysWithinBudget` is the falsifier
  for Performance P21 (#642). Loosening it would mask a genuine regression.
  Excluding it from CI preserves its value for local verification without
  incurring false positives on a shared runner.

### PicoGK 0xC0000005 crash at suite end — intermittent native access violation

- **Symptom:** After the last xUnit test completes, the test host process
  exits with a fatal `0xC0000005` (STATUS_ACCESS_VIOLATION) inside
  PicoGK's native DLL cleanup path. The violation is in native code during
  `Library` disposal or the PicoGK shutdown hook — not in managed code.
  All tests pass; only the process exit is unclean.
- **Root cause:** Intermittent race in PicoGK shutdown (native
  OpenVDB / TBB task-scheduler teardown). Manifests only when multiple
  `LibraryScope` instances were created and destroyed during the session;
  single-Library runs are clean.
- **Workaround:** The crash does not affect test results. CI uploads the
  `.trx` artifact before the crash (the `Upload test results` step runs
  `if: always()`), so results are preserved. Treat a run that shows
  only this crash as green if all test counts match expectations.
- **Resolution path:** Pinned to PicoGK 2.2.0 as of 2026-06-15 ([#861](https://github.com/poetac/voxelforge/pull/861)). PicoGK 2.2.0's release notes address a viewer crash on hybrid-graphics machines, **not** this OpenVDB / TBB teardown race, so the workaround above still stands — re-evaluate if the crash's frequency changes under 2.2.0.

---

## Resolved (kept for reference, drop at next refresh)

- **Sprints A.111 + A.112 red-team fixes (2026-06)** — 30+ correctness defects (inverted physics formulas, NaN/robustness guards, dead validation, gate logic, determinism leaks, analyzer/generator tooling) found by two adversarial audit rounds, each fixed same-sprint with a fail-on-old / pass-on-new regression test; none ever became a pinned failure here. Full inventory with file pointers: CHANGELOG Sprints A.111 + A.112. The calibration-blocked residue lives in § Documented gaps above; the Windows-only regression tests among them are queued in § Windows-leg validation pending.
- **Entries resolved before 2026-06** (the #544–#548 cascade and the Sprint A.85–A.100 era: MultiChainOptimizer cancellation, Nexis GIT re-anchor, HetMassUtilizationLow re-architecture, MR-510 √-enthalpy invariant, Holtrop Froude threshold, EcW5 antenna fixture, parabolic-dish gain, RDE pressure-gain calibration, segmented-TEG cascade formula, heat-pipe k_eff, XRS2200 aerospike geometry, HET/GIT V_d-scaling, MPD cathode-tip radiation, CN-Newton A-stability) — dropped at this refresh per the header rule. The record lives in CHANGELOG sprint entries and this file's git history (`git log -- Voxelforge/docs/physics-cascade-status.md`).

---

## How to use this doc

- **Before judging a CI red as a regression:** check this list first. If the failing test is listed, it's a pinned-failure diagnostic surface, not new breakage.
- **Before fixing a sub-bug:** verify the symptom and where pointer above are still accurate; the production code may have moved.
- **Before loosening a test threshold:** the test exists *to* surface the gap. Loosening it weakens the falsifier. Pick one of the fix candidates instead.
- **After a fix lands:** drop the entry from "Active" → "Resolved" and add the resolving PR number. Refresh the `Updated YYYY-MM-DD` header.
