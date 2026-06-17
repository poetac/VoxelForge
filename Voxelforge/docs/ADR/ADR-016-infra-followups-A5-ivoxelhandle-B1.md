# ADR-016: Infrastructure follow-ups — A5 + IVoxelHandle + B1

**Status:** Accepted (2026-04-25)
**Supersedes:** —
**Related:** ADR-015 (Core/Voxels/App split), ADR-013 (Benchmark JSONL schema)

## Context

ADR-015 shipped the Core/Voxels/App project split with three named
follow-ups deferred to a separate sprint:

1. **A5** — `Microsoft.CodeAnalysis.PublicApiAnalyzers` to track Core's
   public-API drift now that it's a real library boundary.
2. **IVoxelHandle** interface to retire the 9 `object?` cast sites that
   were the documented wart from A1's Voxels-bearing record extraction.
3. **B1 (BB-6)** — bench-regression CI workflow + bench-diff CLI to
   detect physics drift against the canonical-design fingerprints.

This ADR records the resolution of all three in one bundle since they
are individually small but architecturally connected (B1 depends on
the manifest discipline A5 sets up; IVoxelHandle is the
A5-manifest-trackable interface that closes the A1 wart).

## Decision

### A5 — PublicApiAnalyzers

`Microsoft.CodeAnalysis.PublicApiAnalyzers` (3.11.0-beta1.24454.1) is
referenced from both `Voxelforge.Core` and
`Voxelforge.Voxels`. Each project carries:

- `PublicAPI.Shipped.txt` — the frozen baseline of the public API
  surface as of this commit. Future PRs that add public types/members
  trigger RS0016 unless added to this file or `Unshipped.txt`.
- `PublicAPI.Unshipped.txt` — the staging area for in-flight API
  additions; promoted to `Shipped.txt` on release.

Initial baselines were populated by capturing the analyzer's RS0016
warnings on a clean rebuild and writing them to `PublicAPI.Shipped.txt`:

| Project | Entries |
|---|---|
| Core | 4595 (after IVoxelHandle additions) |
| Voxels | 619 (after PicoGKVoxelHandle additions) |

**Workflow:**
- Adding a public API → append the analyzer-emitted line to
  `PublicAPI.Unshipped.txt` (the IDE quick-fix does this automatically).
- Releasing → move from `Unshipped.txt` to `Shipped.txt`.
- Removing → delete from `Shipped.txt` AND add `*REMOVED*<entry>` to
  `Unshipped.txt`.

### IVoxelHandle

A1's wart: `AerospikeBuildResult.Voxels` and `ChamberGeometryResult.Voxels`
were typed `object?` / `object` so Core could stay PicoGK-free. App-side
consumers had to write `(PicoGK.Voxels)result.Voxels` at every read
site (9 such sites, all flagged with `// A1 cast:` comments).

**Resolution:**

- `Core/IVoxelHandle.cs` — opaque marker interface (`public interface
  IVoxelHandle { }`).
- `Voxels/PicoGKVoxelHandle.cs` — sealed wrapper class
  `public sealed class PicoGKVoxelHandle(PicoGK.Voxels Inner) : IVoxelHandle`
  + extension method
  `public static Voxels AsPicoGK(this IVoxelHandle handle)`.

Records updated:

- `AerospikeBuildResult.Voxels`: `object?` → `IVoxelHandle?`
- `ChamberGeometryResult.Voxels`: `object` → `IVoxelHandle`

Construction sites in Voxels wrap with `new PicoGKVoxelHandle(...)`.
Consumer sites use `result.Voxels.AsPicoGK()` — net result is one
member call per site instead of one C-style cast, but the cast happens
inside `AsPicoGK` (a one-liner with a sealed-class downcast that the
JIT inlines).

**Behaviour preservation:** zero runtime overhead; bit-identical
output (the wrapper holds a single field reference and the extension
method inlines).

### B1 (BB-6) — bench-regression workflow

Two artifacts:

1. **`bench-diff` CLI** — a new dispatch arm in
   `Voxelforge.Benchmarks/Program.cs` (`--bench-diff
   <baseline.jsonl> <current.jsonl> [--threshold-percent N]
   [--summary-only]`). Implementation in
   [`BenchDiff.cs`](../../Voxelforge.Benchmarks/BenchDiff.cs).
   Loads both JSONL files, groups by `(preset, seed)`, diffs 12
   physics scalars at the configured threshold + 4 boolean fields
   exactly. Skips provenance + timing fields by design.

2. **`.github/workflows/bench-regression.yml`** — matrix workflow
   over the 5 canonical presets. Runs `--bench-sa` per preset, then
   `--bench-diff` against the most recent matching baseline file.
   Triggers on PR (paths-filtered to source projects) + nightly +
   manual dispatch.

**Branch protection note:** since rulesets / branch-protection are
unavailable on this free-private repo (audit-prep decision C1), a
workflow FAIL is INFORMATIONAL — it does NOT block merge. The maintainer can
choose whether to merge despite drift; the workflow's purpose is to
surface drift, not to gate it.

## Known limitation: baseline staleness

The five baselines under `Voxelforge.Benchmarks/baselines/`
are dated **2026-04-24** — captured PRE-cascade for the BB-2
sprint. Sprints 30 + 32 + 33 + 36 + 34a + 37a + 37b have all shipped
since then, shifting the canonical-design physics scalars by an
expected 10-30 % per the audit's projected impact.

**Therefore the bench-regression workflow is expected to FAIL on the
first PR that lands after this ADR**, including this very PR (#54),
because the current main physics has drifted from the frozen baselines.

This is **expected and non-blocking**. To refresh:

1. Wait for the relevant physics PRs to land in main (PR #54 itself
   counts since it didn't change physics, but the cascade PRs already
   merged are what shifted things).
2. On main, run `--bench-sa --design-preset <preset>` for each of the
   5 presets, with output to a new dated file
   (`bench-sa-<preset>-<YYYY-MM-DD>.jsonl`).
3. Commit the new baselines to `Voxelforge.Benchmarks/baselines/`.
4. The next workflow run will pick up the new baseline (the workflow
   uses `ls -t ... | head -1` to find the most recent dated file).

The old 2026-04-24 baselines are kept on disk as the historical
pre-cascade reference (per the BB-2 charter).

## Consequences

**Wins:**
- Public API drift is now compile-time visible — accidental additions
  to Core's surface trigger RS0016, accidental removals trigger RS0017.
- The IVoxelHandle wart is closed; Core's record fields carry typed
  references instead of `object?`.
- Physics regressions get a second line of defence (in addition to
  the existing 1343-test suite) via the bench-regression workflow.
- The `bench-diff` CLI is a reusable building block for future
  benchmarking work (BB-3 BenchmarkDotNet integration, BB-4 gate
  microbenches, etc.).

**Costs:**
- ~5200 new lines under `PublicAPI.Shipped.txt` files (mechanical;
  diff-friendly because each entry is a single line).
- Two new files in Core (`IVoxelHandle.cs`) and Voxels
  (`PicoGKVoxelHandle.cs`) — both small.
- One new C# file in Benchmarks (`BenchDiff.cs`, ~200 LOC).
- One new GitHub Actions workflow file.
- Total tests still 1343/1343 + 1 skipped — no behaviour drift.

## Follow-up session orientation

If a future session is asked to continue this work:

1. **Refresh baselines** is the most likely first task — see the
   "Known limitation: baseline staleness" section above.
2. **Mark `bench-regression` as a required check** (audit-prep
   decision C1) once branch protection is available — either the
   account upgrades to GitHub Pro or the repo goes public.
3. **Promote `Unshipped.txt` entries to `Shipped.txt`** as part of the
   release workflow when one is established.
4. **Extend the bench corpus** beyond the 5 canonical presets if
   useful — BB-3 / BB-4 / BB-5 from the benchmarking-expansion
   roadmap.
5. **Move Optimization orchestrators to Voxels** to retire the last
   App ↔ Voxels asymmetry — would require an `IVoxelGenerator`
   interface refactor (~2-3 days) called out in ADR-015.

## Scope amendment (2026-05-17 via #623)

`PublicApiAnalyzers` stays enabled on `Voxelforge.Core` +
`Voxelforge.Voxels` **only**. Pillar `.Core` projects
(`Voxelforge.Airbreathing.Core`, `.ElectricPropulsion.Core`,
`.Marine.Core`, `.Nuclear.Core`, `.Cfd.Core`) deliberately do **not**
carry `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`. The
rationale per ADR-026 §3 ("pillar-as-facade"): each pillar's surface
is sealed behind a small set of family-namespace types
(`AirbreathingOptimization`, `ElectricPropulsionOptimization`,
`MarineOptimization`, etc.); internal types are free to churn within
the pillar.

Promote a pillar's internal type to the Core surface when the
**rule-of-three** hits — i.e. when a third sibling project needs it
(precedent: ADR-029a's `IPlasmaState` lift to `Voxelforge.Core/Plasma/`
after EP, Nuclear, and Marine all needed the same plasma-state
shape). Until the third caller appears, the type stays in its owning
pillar's internal namespace.
