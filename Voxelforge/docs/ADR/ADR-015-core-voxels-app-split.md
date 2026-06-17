# ADR-015: Split Voxelforge into Core + Voxels + App

**Status:** Accepted (2026-04-25)
**Supersedes:** —
**Related:** ADR-005 (PicoGK + xUnit incompatibility), ADR-013 (Benchmark JSONL schema)

## Context

Pre-split, `Voxelforge/` was a single ~169-LOC-file project that
mixed three concerns:

1. Pure physics (Combustion / HeatTransfer / Coolant / FeedSystem sizing /
   Optimization data records / IO / Manufacturing / Structure / Analysis).
   No PicoGK, no WinForms.
2. PicoGK-using voxel + SDF builders (ChamberVoxelBuilder, AerospikeBuilder,
   the various Implicit classes, the manifold router, the turbopump geometry
   generators).
3. WinForms UI + orchestrators (Program, RegenChamberForm and friends,
   RegenChamberOptimization, AerospikeOptimization, MonolithicEngineBuilder,
   platform-specific Windows code).

The architectural-prep audit (project docs, 2026-04-25) flagged this as the
single highest-leverage structural change for enabling concurrent work
tracks: a future air-breathing pillar, the T2.2 subprocess oracle, T2.3
CFD validation, headless CI scoring of PRs, future Avalonia / MAUI / Blazor
frontends — all benefit from a clean physics ↔ UI seam.

The same audit also flagged the xUnit + PicoGK incompatibility (PicoGK
pitfall #8) as cultural-only — a developer could currently put a voxel-
building test in `.Tests` and crash the test host, and only documentation
prevents that.

## Decision

Split into three projects with explicit dependency direction:

```
.Tests / .Benchmarks / .StlExporter   →   App   →   Voxels   →   Core
                                         ↘         ↗
                                            Core (direct)
```

- **`Voxelforge.Core`** (.NET 9, no PicoGK, no WinForms,
  ~117 .cs files): pure physics + data records.
- **`Voxelforge.Voxels`** (.NET 9-windows + PicoGK,
  ~19 .cs files): leaf voxel/SDF builders only. References Core.
- **`Voxelforge`** (App; .NET 9-windows + WinForms + PicoGK
  transitively, ~33 .cs files): UI + orchestrators + platform code.
  References both Core and Voxels.

`.Tests` references App + StlExporter and does NOT directly reference
Voxels — an explicit project-reference would be required to add a
voxel-using test, making the xUnit + PicoGK rule structural rather than
cultural.

## Inline records that had to be extracted

Several pure-data records were defined inline alongside the PicoGK
builders that produced them. To keep the records visible to both Core
consumers (HeatTransfer, Optimization data records) and App / Voxels
consumers (the orchestrators), each inline record was extracted to its
own file in Core:

- `StructuralConfidence` enum, `RegenGenerationResult`, `RegenScoreResult`,
  `ScoringProfile` (was in `RegenChamberOptimization.cs`)
- `AerospikeBuildResult`, `AerospikeThermalResult`, `AerospikeInjectorSizing`
  (was in `AerospikeBuilder.cs`)
- `ChamberGeometryResult` (was in `ChamberVoxelBuilder.cs`)
- `BuildProfile` data record (was in `BuildProfile.cs` alongside
  `BuildProfiler` — the profiler class stays in Voxels because it uses
  PicoGK)
- `TurbopumpGeometry`, `TurbineGeometry` (was in
  `Turbopump/*GeometryGenerator.cs`)
- `ToleranceInputs`, `ToleranceQuantile`, `ToleranceResult` (was in
  `Analysis/ToleranceAnalysis.cs` — extracted to break a Voxels↔App
  dependency loop introduced by A2)

Each extraction is mechanical and the extracted file uses the same
namespace as its original home, so consumers don't need any `using`
changes.

## Known wart: `object?` for Voxels-bearing record fields

`AerospikeBuildResult.Voxels` and `ChamberGeometryResult.Voxels` are
typed `object?` / `object` in Core (pre-split they were
`PicoGK.Voxels?` / `PicoGK.Voxels`). Core does not reference PicoGK,
so the typed-handle cannot live there.

App-side consumers cast back to `(PicoGK.Voxels)result.Voxels` when they
need the voxel body. There are 9 such cast sites today across
`Program.cs` (3), `MonolithicEngineBuilder.cs` (2), `StlExporter` (2),
`Benchmarks` (3) — all flagged with `// A1 cast:` comments.

**Deferred cleanup:** introduce an `IVoxelHandle` interface in Core that
PicoGK.Voxels implements via an App-side wrapper. ~1 day of work.
Tracked under the Optimization Infrastructure roadmap as a follow-up.

## Why some files DIDN'T move

The audit deliberately scoped this split as "minimum viable Core" —
files with deep coupling to App-side concerns stay in App rather than
trigger cascading refactoring:

- `RegenChamberOptimization.cs` / `AerospikeOptimization.cs` (orchestrators)
  call into UI status callbacks + ToleranceAnalysis + MemoryProjectionGate.
  Moving these to Core or Voxels would require an `IVoxelGenerator`
  interface refactor (~2-3 days) which was out of scope.
- `MonolithicEngineBuilder.cs` is itself an orchestrator (composes
  chamber + turbopump + feed manifold + preburner via the Optimization
  classes).
- `ToleranceAnalysis.cs` / `MemoryProjectionGate.cs` /
  `MegaScaleEnvelope.cs` reference the orchestrator.
- `BuildSubprocess.cs` uses `Win32 JobObject` (App-platform code).

## Consequences

**Wins:**
- Headless physics is now a real shippable artifact. T2.2 (subprocess
  oracle) and T2.3 (CFD validation) become straightforward consumers
  of `Voxelforge.Core.dll`.
- xUnit + PicoGK pitfall is structural, not cultural.
- Future air-breathing pillar can be added as a parallel `Voxelforge.*`
  project that consumes Core directly without inheriting WinForms /
  PicoGK constraints (per the scope-expansion roadmap).
- Multiple concurrent tracks can edit Core without UI churn risk.

**Costs:**
- 9 `object?` casts at App-side voxel-handle consumers. Documented in
  comments; cleanup deferred to the `IVoxelHandle` follow-up.
- Inline records had to be extracted (12 new standalone files in Core
  for what were previously inline definitions).
- Sln file went from 4 projects to 6.
- `.csproj` references became more explicit (App now references Core +
  Voxels; consumers transit via App).

**No physics behaviour changes.** All 1343 tests pass at the same scores
+ baselines as pre-split. Both Sprint BB pre-cascade fingerprints in
`.Benchmarks/baselines/` should produce bit-identical output (verified
via the green test suite, since the SA pack/unpack round-trip tests
exercise the canonical-design SA path).

## Follow-ups (status as of 2026-05-01)

1. **`IVoxelHandle` interface** — **SHIPPED** via [ADR-016](ADR-016-infra-followups-A5-ivoxelhandle-B1.md)
   (PR #54, 2026-04-25). Retired all 9 `object?` cast sites.
2. **B1 — bench-regression CI workflow (BB-6)** — **SHIPPED** via
   [ADR-016](ADR-016-infra-followups-A5-ivoxelhandle-B1.md). `bench-diff`
   CLI + `.github/workflows/bench-regression.yml` on PR + nightly.
3. **A5 — `Microsoft.CodeAnalysis.PublicApiAnalyzers`** — **SHIPPED** via
   [ADR-016](ADR-016-infra-followups-A5-ivoxelhandle-B1.md). Core + Voxels
   public surfaces baselined.
4. **Orchestrator decoupling** (moving `RegenChamberOptimization`,
   `AerospikeOptimization`, `ToleranceAnalysis`, `MemoryProjectionGate`
   to Core) — **SHIPPED** via [ADR-021](ADR-021-orchestrator-decoupling.md)
   Phase 1 (PR #321) + Phase 2 (PR #324), 2026-04-30.
