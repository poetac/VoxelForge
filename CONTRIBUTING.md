# Contributing to voxelforge

Welcome. This document is the operating manual for contributing to the repo. Read it once; refer to [`CLAUDE.md`](CLAUDE.md) for the developer guide (build, project layout, PicoGK pitfalls) and the [`Voxelforge/docs/ADR/`](Voxelforge/docs/ADR/) folder for architectural decisions.

## Getting started

```bash
git clone https://github.com/poetac/voxelforge.git
cd voxelforge
dotnet restore voxelforge.sln
dotnet build  voxelforge.sln
dotnet test   Voxelforge.Tests/Voxelforge.Tests.csproj
dotnet run    --project Voxelforge
```

**Requirements:** Windows (WinForms target), .NET 9 SDK, 16–64 GB RAM. See [`CLAUDE.md`](CLAUDE.md) § Hardware guidance for voxel-size rules.

## Branching

- `main` is always green (CI builds + tests pass).
- Work on a feature branch. Name it:
  - `feat/<short-name>` — new capability
  - `fix/<short-name>` — bug fix
  - `refactor/<short-name>` — no behavior change
  - `docs/<short-name>` — docs/ADR only
- Keep branches short-lived (≤ 1 sprint). Long-running branches collide with the monolithic hotspot files.

## Pull requests

1. Push your branch: `git push -u origin feat/<name>`
2. Open a PR against `main`: `gh pr create --fill` (fills title/body from your commits) or use the GitHub UI — the PR template will auto-populate.
3. CI (`.github/workflows/ci.yml`) must pass: build + test on `windows-latest`.
4. At least one review approval before merge (see CODEOWNERS for required reviewers on hotspot files).
5. Squash-merge preferred for feature branches; the individual commits are usually noise.
6. **CHANGELOG.md entry** for production-code changes (anything under `Voxelforge*/` outside test projects and per-project `docs/`). The [`changelog-check`](.github/workflows/changelog-check.yml) workflow posts a sticky PR comment when the entry is missing; non-blocking on this free-tier repo, but address before merge. Apply the `skip-changelog` label for legitimate exemptions: reverts, hotfixes, infrastructure / tooling-only PRs. Follow the [Keep a Changelog](https://keepachangelog.com/) format for new entries.
## Claiming work via Issues

**Canonical workflow (formalized 2026-05-17 via #623).** The next 3-7 ready-to-pick items at any moment live as [GitHub Issues](https://github.com/poetac/voxelforge/issues) tagged with `track:*` labels (e.g. `track:optimization-infra`, `track:physics-cascade`). The audit docs under `Voxelforge/docs/` remain the SSOT for *rationale*; issues are atomic claim tickets with three-line bodies that link back to the SSOT doc.

**Protocol:**

1. Self-assign before starting: `gh issue edit <N> --add-assignee @me`.
2. Branch from `main`: `git switch main && git pull && git switch -c auto/issue-<N>-<short-slug>`.
3. Open the PR with `Closes #<N>` in the body — merge auto-closes the issue.
4. Squash-merge (repo setting; see [§ Pull requests](#pull-requests) above).

**Protocol when picking up an issue:**

1. **Find a candidate.** `gh issue list --state open --label track:<area>` (or just visit the [issues page](https://github.com/poetac/voxelforge/issues)). Pick one with no assignee.
2. **Claim it before starting work.** `gh issue edit <N> --add-assignee @me` — this is the conflict-prevention signal for other contributors.
3. **Branch + work** per the "Branching" section above. Use `feat/<short-name>` even when the issue's track is something other than `feat` (the issue label carries the track signal already).
4. **PR closes the issue.** Include `Closes #<N>` in your PR body — GitHub auto-links and auto-closes on merge.
5. **If you discover the work is bigger than the issue suggests**, comment on the issue with the revised scope and either split into sub-issues or update the existing body.
6. **If you abandon the work**, unassign yourself (`gh issue edit <N> --remove-assignee @me`) so the next contributor can pick it up.

**When to open a new issue:** only after the issue queue has < 3 items still unclaimed. Issues are the queue; the durable docs (CHANGELOG, ADRs, ROADMAP) are the source of truth.

**Items NOT on the issue queue:**
- Blocked items (e.g. A8/ID-8 needs shifting-equilibrium CEA tables we don't have).
- Demand-driven items (E-D nozzle, BB-3..BB-5 benchmarking expansion, additional propellant pairs).
- Long-term scope expansion (air-breathing pillar — 9-12 mo out, ~70-100 day Sprint 0 prerequisite).
- Visual elegance / Noyron parity (deferred until Sprint 37 ships).
- Dependent items (e.g. T2.4b NSGA-II UI is held until T2.4a algorithm lands).

**Evaluation.** This experiment runs through the next 2-3 sprints. If issues clearly speed up parallel pickup without bloating the doc surface, expand the queue. If they don't pull weight, stop opening new ones — the audit docs continue to work regardless.

## Hotspot file coordination

These files are large, central, and touched by many features. Only one developer should have in-flight work on them at a time:

| File | Approx LOC | Why |
|------|-----------:|-----|
| `Voxelforge/UI/RegenChamberForm.cs` | ~2,800 | Monolithic WinForms form; event handlers, the constructor body, and dispatch glue. Sibling partials carry `Builders`, `ParameterIO`, `ResultsDisplay`, `ConstructorGroups`. |
| `Voxelforge/UI/RegenChamberForm.ConstructorGroups.cs` | ~670 | Partial sibling holding the `BuildXxxGroup()` factory methods extracted from the main constructor body. New control groups should land here. |
| `Voxelforge/UI/RegenChamberForm.ResultsDisplay.cs` | ~490 | Partial sibling: `PopulateXxxReadouts` helpers (one per result-panel block: engine summary, thermal, feasibility, aerospike, preburner cooling, etc.). `UpdateResults` orchestrates them in order. |
| `Voxelforge/UI/RegenChamberForm.Builders.cs` | ~290 | Partial sibling: UI control factories (`Num`, `Out`, `Pill`, `Row`, `Group`, …). Called ~300× from the main form. |
| `Voxelforge/UI/RegenChamberForm.ParameterIO.cs` | ~400 | Partial sibling: `ReadConditions`, `ReadDesign`, `ApplyDesign` — the controls ↔ `OperatingConditions` / `RegenChamberDesign` mappings. |
| `Voxelforge/Program.cs` | ~900 | CLI args + thread/viewer orchestration + main dispatch loop. Houses `TryStartOpt` / `TryStartMultiChainOpt` / `FinalizeMultiChainOpt`. |
| `Voxelforge.Core/Optimization/RegenChamberOptimization.cs` | ~2,100 | Scoring + `GenerateWith` orchestrator; imports 9+ subsystems. `Bounds` / `Pack` / `Unpack` are one-line delegations to the registry + binder (ADR-010 / ADR-012). |
| `Voxelforge.Voxels/Geometry/ChamberVoxelBuilder.cs` | ~1,560 | Sequential phase build; all chamber geometry collides here. Voxel boolean-op temporaries route through `BoolSubtractTemp` / `BoolAddTemp` extensions to avoid the OpenVDB-grid leak pattern. |
| `Voxelforge.Core/Optimization/RegenChamberDesign.cs` | ~1,500 | SA design record — declares 29 of the 34 SA dimensions (the 5 injector dims 13–17 live on `InjectorPattern`). Each carries a `[SaDesignVariable(index, min, max, gate)]` attribute; adding a new dim is a one-line attribute annotation + a length-assertion bump (see `ADR-012-adding-an-sa-design-variable.md`). |
| `Voxelforge.Core/Optimization/FeasibilityGate.cs` | ~700 | Rocket regen feasibility-gate evaluator. The legacy inline `Evaluate()` if-chain still coexists with the declarative `GateRegistry` (`RocketGates.cs`, ADR-019); completing the migration is tracked by [#629](https://github.com/poetac/voxelforge/issues/629). Gate IDs are hand-typed strings (no compile-time uniqueness — refactor candidate). |
| `Voxelforge.Voxels/Geometry/MonolithicFeasibility.cs` | ~640 | Body-intersection + tube-vs-tube evaluator for the monolithic-engine pipeline. |
| `Voxelforge.Voxels/Geometry/AerospikeBuilder.cs` | ~800 | `AerospikeSpec` → `AerospikeBuildResult`. `BuildPhysicsOnly` is the analytical-only entry; `Build` is the voxel path. |
| `Voxelforge.Core/Optimization/DesignVariableBinder.cs` | ~380 | Reflection-based `Pack` / `Unpack` over `[SaDesignVariable]`-tagged properties. Caches `PropertyInfo` accessors. Source-generator replacement is on the optimization-infrastructure roadmap (T1.4). |

**Protocol:** before opening a PR that edits one, check open PRs first to make sure no other in-flight work touches the same file. Hold until the other PR lands, or rebase after.

LOC counts drift; the column is for relative scale, not exact stat. Run `wc -l <file>` if you need the live number.

## Tests

- **Run before every push:** `dotnet test Voxelforge.Tests/Voxelforge.Tests.csproj`. The full multi-pillar suite (rocket + airbreathing + EP + marine + nuclear + CFD) is ~5,700 tests across 6 test projects; use the per-pillar `dotnet test Voxelforge.<Pillar>.Tests/...` form when you only touched one pillar.
- **Don't break the green baseline.** If your change intentionally changes physics results, update baselines in `Voxelforge.Benchmarks/baselines/` and justify in the PR.
- **xUnit + PicoGK works under PicoGK 2.0.0+.** ADR-005 (which documented the pre-2.0 incompatibility) was retired on 2026-05-04 when PicoGK 2.0.0 shipped. The canonical idiom in xUnit tests is:
  ```csharp
  using var lib = new Library(voxel_mm);
  using var libScope = LibraryScope.Set(lib);
  ```
  Subprocess-style voxel tests via `Voxelforge.StlExporter/` (and `Process.Start` from the test) are still valid and required for: CLI-surface tests, cross-process determinism checks, and any pillar test project that targets `net9.0` (not `net9.0-windows`) — those can't load PicoGK at all.
- New feasibility gates must include a test that *violates* the gate and asserts it fires.
- New design variables must have an assertion covering Pack → Unpack round-trip and bounds respect.

## Commit messages

Short present-tense imperative, one line preferred:

```
Add N2O4/MMH propellant tables and stability gate
Fix NPSH gate off-by-one on inducer outlet radius
Refactor FeedManifoldRouter to drop duplicate stack-up branch
```

Longer body welcome when the "why" isn't obvious. Follow existing repo history style (`git log`).

## Code style

- **No `TODO` / `FIXME` / `HACK` comments.** This repo has zero as of Sprint 0 — keep it that way. If you find a gap, open an ADR or a GitHub issue instead of leaving a comment rot. **The one allowed exception** (#623, 2026-05-17): `// TODO(#NNN): description` with a mandatory GitHub-issue reference. Naked `// TODO` / `// FIXME` / `// HACK` stay banned. Use the form sparingly — preferred patterns are still an issue + an ADR, or splitting the work out of the current PR.
- **Don't add comments that restate what the code does.** Only comment *why* something non-obvious is true.
- **Match the existing naming conventions.** `record` types are pervasive; use `x with { … }` for cloning (do not define `Clone()` — CS8859).
- **Respect ADR-007:** `Smoothen(d)` must stay ≤ 25 % of the minimum feature thickness in the region.
- **Respect ADR-009:** feasibility gates are additive and all fire (no fail-fast) for diagnostics.
- **Voxel ops must run on the task thread only.** UI marshals via `SharedState`; never call PicoGK from the form thread.

## Error-handling conventions

Every method that rejects input throws a typed exception with the
offending value in the message. The canonical reference impl lives at
[`Voxelforge.Airbreathing.Core/Cycles/IsolatorRecovery.cs`](Voxelforge.Airbreathing.Core/Cycles/IsolatorRecovery.cs)
lines 61-72:

```csharp
/// <exception cref="ArgumentOutOfRangeException">
/// Thrown when <paramref name="isolatorInletMach"/> is &lt; 1.
/// </exception>
public static double Pi_iso(double isolatorInletMach)
{
    if (double.IsNaN(isolatorInletMach) || isolatorInletMach < 1.0)
    {
        throw new ArgumentOutOfRangeException(nameof(isolatorInletMach),
            $"Isolator-inlet Mach {isolatorInletMach:F3} must be ≥ 1 (supersonic "
          + "flow required; subsonic flow cannot sustain a pseudo-shock train).");
    }
    // ...
}
```

Five rules, in order of how often they're missed:

1. **Use the narrowest typed exception.** `ArgumentOutOfRangeException`
   for numeric / range failures; `ArgumentNullException` for null
   checks (prefer `ArgumentNullException.ThrowIfNull(x)` over the
   legacy `if (x is null) throw …` long-form); `NotSupportedException`
   for unimplemented-enum switch arms; `InvalidOperationException` for
   object-state failures. Bare `ArgumentException` throws away
   structural information the caller can use — reserve it for
   genuinely categorical failures (wrong `Kind`, reserved sentinel,
   missing combination) where no narrower type fits.

2. **Trap NaN explicitly.** `double.IsNaN(x) || x < lower` is the
   canonical form. A bare `x < lower` returns `false` for NaN and the
   bad value slips through — silent divergence downstream.

3. **Put the offending value in the message** with a precision suffix
   (`:F3` for floats, `:F1` for percentages, `:F0` for integer-shaped
   doubles). The reader is usually debugging from a CI log; they need
   the actual number, not just the field name.

4. **Use `nameof(param)` as the first throw argument**, not a string
   literal. Refactors stay in sync; literal parameter names go stale.

5. **Document with `<exception cref="...">` XML tags** on the method's
   doc comment. Both for IDE tooltips and for the eventual rendered
   API docs. Records' `ValidateSelf()` and solvers' public entrypoints
   are the highest-leverage targets.

Pillar audits flag specific migration targets where the existing code
predates this style.

## Feasibility gates

Adding a new gate:
1. Add the enum value to `FeasibilityGate.cs`.
2. Implement the check; make it *additive* (returns infeasibility cost ≥ 0; 0 means pass).
3. Wire into `RegenChamberOptimization.Evaluate`.
4. Add a test in `FeasibilityGateTests.cs` that constructs a violating fixture and asserts the gate fires.
5. Do **not** short-circuit other gates — all gates run every evaluation (diagnostics).

## Design variables

**ADR-010 is fully resolved as of Sprint 7 Track C** — adding a new SA variable is now a one-line attribute annotation. See `Voxelforge/docs/ADR/ADR-012-adding-an-sa-design-variable.md` for the full step-by-step workflow.

TL;DR:

1. Pick the next unused index (look at `DesignVariableRegistry.DescriptorsForMany(...)[^1].Index + 1`).
2. Pick `(min, max)` bounds — SA samples the range and Unpack clamps to it.
3. Pick an `SaGate` value (`None` / `InjectorPatternPresent` / `TpmsTopology` / `AerospikeTopology`).
4. Annotate: `[SaDesignVariable(index: N, min: X, max: Y, gate: SaGate.Z)]` on the `public … { get; init; }` property on `RegenChamberDesign` or `InjectorPattern`.
5. Bump the hard-coded length assertions in `SprintUpgradesTests.cs` + `NoyronTierB1ProperTests.cs` (current value is `31` post-Track B — increment to `32`).
6. Add a round-trip test (+ a gate-suppression test if the new dim uses a non-`None` gate). See the existing `PackUnpack_RoundTrips*` patterns in `SprintUpgradesTests.cs`.

You do NOT need to edit `RegenChamberOptimization.Bounds`, `Pack`, or `Unpack` — all three derive from the registry.

## ADRs

Architectural decisions go in `Voxelforge/docs/ADR/` as `ADR-XXX-<slug>.md`. Follow the existing template. Status is `Accepted`, `Open`, or `Superseded`. Don't delete old ADRs — supersede them so history is preserved.

## Scheduled benchmarks

Recurring overnight benchmarks (nightly / weekly) use a shared reusable
workflow at [`.github/workflows/_scheduled-bench-template.yml`](.github/workflows/_scheduled-bench-template.yml)
(ADR-045). The template owns: checkout, provenance capture, restore, Release
build, job summary, failure annotation, and artifact upload. Individual
scheduled workflows own only their cron trigger and the specific bench command.

**Adding a new scheduled benchmark:**

1. Create `.github/workflows/<name>.yml` with a `schedule:` trigger (nightly
   02:00 UTC Mon–Fri or weekly 03:00 UTC Saturday per ADR-045 D1) plus
   `workflow_dispatch`.
2. Add a `concurrency:` group so rapid re-triggers serialize without
   cancelling an in-progress run (use `cancel-in-progress: false` for benches
   that must complete before results are meaningful).
3. Call the template:
   ```yaml
   jobs:
     bench:
       uses: ./.github/workflows/_scheduled-bench-template.yml
       with:
         bench-name: my-bench-slug          # used in artifact name
         bench-command: |
           mkdir -p current
           dotnet run --project Voxelforge.Benchmarks/... -- \
             --my-flag --git-sha "$VF_GIT_SHA" --machine-id "$VF_MACHINE_ID" \
             --out current/output.jsonl
         output-path: current/*.jsonl
         retention-days: 30                 # optional, default 30
         timeout-minutes: 60                # optional, default 60
   ```
4. The template sets `VF_GIT_SHA` and `VF_MACHINE_ID` before `bench-command`
   runs — embed them in your JSONL provenance fields per ADR-013.
5. Artifacts appear in the Actions UI named `{bench-name}-{YYYY-MM-DD}-{sha7}`.
   They expire after `retention-days` (max 30); committed baselines are the
   only persistent record in git.

**Stagger your cron.** If you add a nightly workflow, offset the minute by
5–10 from existing nightly workflows to avoid a burst at exactly 02:00 UTC
that would queue all jobs simultaneously on the two-runner pair.

## Ad-hoc sweeps

One-shot parameter sweeps (e.g. "how does Isp vary with chamber pressure?")
run via the `--sweep` CLI flag on `Voxelforge.Benchmarks` and the
[`sweep-on-demand`](.github/workflows/sweep-on-demand.yml) `workflow_dispatch`
workflow (#830). No session token spend required after the first run.

**Running locally:**

```bash
dotnet run --project Voxelforge.Benchmarks -- --sweep \
  --preset merlin --variable p_c --range 2e6,8e6 --samples 30 \
  --objective isp --out current/sweep.csv
# Writes current/sweep.csv + current/sweep.png
```

**Running remotely (via `gh workflow run`):**

```bash
gh workflow run sweep-on-demand.yml \
  -F preset=merlin -F variable=p_c -F range=2e6,8e6 \
  -F samples=30 -F objective=isp
# Artifact: sweep-merlin-p_c-{date}-{sha7}/sweep-merlin-p_c.csv + .png
```

**Supported `--variable` values:**

| Name | Description |
|------|-------------|
| Any `MemberName` from `DesignVariableRegistry` | SA design variable (geometry, channel dims, etc.) |
| `p_c` | `OperatingConditions.ChamberPressure_Pa` |
| `thrust` | `OperatingConditions.Thrust_N` |

Run `--sweep --list` (or let `--variable` validation error print the full list)
to see all available SA design variable names for a given preset.

**Supported `--objective` values:** `score`, `peak_wall_t`, `coolant_dp`,
`mass`, `min_sf`, `coolant_t_out`, `isp`.

**Output:** CSV + PNG artifact. The CSV has columns
`variable_value`, `objective_{name}`, `feasible`, `feasibility_violation_count`.
The PNG is a line chart of feasible points (blue) and infeasible points (red).

## Performance

### Bench-regression soft gate

`bench-regression.yml` runs the SA physics fingerprint for all 7 canonical
presets (5 rocket + 2 airbreathing) and diffs each against its committed
baseline in `Voxelforge.Benchmarks/baselines/`. It triggers automatically on
pull requests that touch physics code (`Voxelforge.Core/**`, pillar `Core`
directories, or `Voxelforge.Benchmarks/**`).

**When drift > 5% is detected** a sticky comment is posted on the PR:

```
⚠ Bench regression: `merlin` (commit `abc1234`)
The SA physics fingerprint for `merlin` drifted > 5% from its committed baseline.
[diff output…]

To acknowledge a deliberate physics change: reply `/acknowledge-regression merlin` in this thread, then merge.
```

The comment updates on each push (no duplicate spam). There is no automated
branch-protection block (free-tier private repo, audit-prep C1) — a reviewer
must read the diff and decide.

**Acknowledgment ceremony:**

| Situation | Action |
|-----------|--------|
| Deliberate physics change (e.g. updated combustion table) | Reply `/acknowledge-regression <preset>` in the PR thread, refresh baselines in the same PR, merge. |
| Accidental regression | Fix the regression, push; the comment updates to show the new (passing) diff. |
| First-night false positive (new baseline not yet committed) | Commit fresh baselines with `--bench-sa` and `--bench-sa-airbreathing`, push in same PR. |

**Refreshing baselines after a deliberate physics change:**

```bash
# Regenerate rocket baselines
dotnet run --project Voxelforge.Benchmarks -- --bench-sa --design-preset merlin \
  --out Voxelforge.Benchmarks/baselines/rocket/bench-sa-merlin-$(date +%Y-%m-%d).jsonl
# … repeat for each drifting preset

# Regenerate airbreathing baselines
dotnet run --project Voxelforge.Benchmarks -- --bench-sa-airbreathing --preset mattingly-ramjet \
  --out Voxelforge.Benchmarks/baselines/airbreathing/bench-sa-airbreathing-mattingly-ramjet-$(date +%Y-%m-%d).jsonl
```

Commit the new JSONL files in the same PR as the physics change so the
nightly fingerprint workflow (#651) does not see a false regression overnight.

## Questions

Ask in the team channel or open a draft PR with `[WIP]` in the title to get early feedback.
