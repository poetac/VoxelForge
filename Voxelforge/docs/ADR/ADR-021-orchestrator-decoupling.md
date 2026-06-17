# ADR-021: Orchestrator decoupling — `IVoxelGenerator` seam (Sprint A-3)

**Status:** Accepted (2026-04-30) — Phase 1 (seam infrastructure) shipped via [PR #321](https://github.com/poetac/voxelforge/pull/321); Phase 2 (orchestrator file move) shipped via this PR closing [#204](https://github.com/poetac/voxelforge/issues/204).
**Supersedes:** —
**Related:** ADR-015 (Core/Voxels/App split, deferred follow-up), ADR-016 (IVoxelHandle infra).

## Context

ADR-015 (the Core/Voxels/App split, 2026-04-25) deliberately left
`RegenChamberOptimization`, `AerospikeOptimization`, and
`MonolithicEngineBuilder` in the WinForms App project. The reasoning at the
time: those three orchestrators couple to `UI.ResourceBudget` (the user-
configurable memory cap), `Analysis.ToleranceAnalysis` (the per-design
tolerance sweep), and the `BuildSubprocess.cs` voxel-build escape hatch —
all sitting in the App. Pulling them into Core would have required either
moving those couplings too (high risk for a single sprint) or designing an
abstraction seam (`IVoxelGenerator`-shaped) that wasn't urgent at the time.

The deferred follow-up has now become a hard blocker for three downstream
tracks (per [#204](https://github.com/poetac/voxelforge/issues/204)):

- **T2.2 follow-on / OOB-1 / OOB-13** — headless callers (`voxelforge-eval`
  subprocess oracle, test-data assimilation, the causal gate explainer)
  consume the orchestrators by reflection / subprocess only because the
  direct App-level reference would drag in WinForms + PicoGK. A clean
  Core-side API removes the workaround.
- **The air-breathing pillar entry** cannot
  start without engine-family abstraction, which assumes the orchestrators
  are already headless-callable. Moving them now matches architecture-review
  recommendation #8.
- **Sprint A-2 (#167) AutoSeeder hardening, MERGED 2026-04-30 as
  [PR #318](https://github.com/poetac/voxelforge/pull/318)** — proved the
  scheduler + canonical preset specs are stable, removing the last
  reservation against touching `RegenChamberOptimization.cs` (the
  ~1.7 k-line hotspot per `CONTRIBUTING.md`).

## Decision

A new interface `Voxelforge.Optimization.IVoxelGenerator` lives in Core
and abstracts the `ChamberVoxelBuilder.Build` / `BuildAnalytical` calls
that are the only PicoGK touch-points inside the orchestrators:

```csharp
public interface IVoxelGenerator
{
    /// <summary>
    /// Build the chamber voxel body at the supplied voxel size. Throws
    /// <see cref="Voxelforge.Analysis.MemoryBudgetExceededException"/>
    /// when the projected grid exceeds the runtime memory budget.
    /// </summary>
    Geometry.ChamberGeometryResult Build(
        Geometry.ChamberBuildOptions opts,
        double voxelSize_mm);

    /// <summary>
    /// Analytical-only path: return the same <see cref="ChamberGeometryResult"/>
    /// shape but with no voxel body (or an inert <see cref="IVoxelHandle"/>).
    /// Used by the bench-SA + `voxelforge-eval` paths that score physics
    /// without paying the voxel allocation cost.
    /// </summary>
    Geometry.ChamberGeometryResult BuildAnalytical(
        Geometry.ChamberBuildOptions opts);
}
```

`RegenChamberOptimization.GenerateWith` accepts an optional
`IVoxelGenerator? voxelGenerator = null` parameter:

- When `skipVoxelGeometry == true`, the orchestrator never touches the
  generator (legacy bench-SA / unit-test path) — preserves the existing
  bit-identical behaviour for callers that pass `skipVoxelGeometry: true`.
- When `skipVoxelGeometry == false` AND `voxelGenerator == null`, the
  orchestrator throws `InvalidOperationException` with a clear "App
  callers must provide a `ChamberVoxelBuilderAdapter` for full voxel
  builds" message. This is the diagnostic path catching App-side callers
  that forgot to inject the adapter; it never fires in test runs.
- When `skipVoxelGeometry == false` AND `voxelGenerator != null`, the
  generator is consulted exactly where the legacy code called
  `ChamberVoxelBuilder.Build / BuildAnalytical` directly.

The default WinForms-side adapter
`Voxelforge.Voxels.ChamberVoxelBuilderAdapter` (lives in the Voxels
project, registered via App `Program.Main` / form constructors) wraps the
two static calls with a one-line `IVoxelGenerator` implementation. The
adapter is the only spot in the codebase that imports `PicoGK` from the
optimization pipeline.

## Migration phases

### Phase 1 — seam infrastructure (this PR)

The IVoxelGenerator boundary is established and threaded through every
App-side caller, but the orchestrator files themselves stay in App until
Phase 2.

1. **`ChamberBuildOptions` + `InjectorFaceImportOptions` → Core.**
   Extracted from `Voxelforge.Voxels/Geometry/{ChamberVoxelBuilder,
   InjectorFaceImport}.cs` into `Voxelforge.Core/Geometry/`. Both records
   are pure-data (no PicoGK references); extracting them lets Core code
   reference the types without dragging in the Voxels project.
2. **`ChamberAnalyticalBuilder` → Core.** The pure-C# analytical mass /
   cost / bounding-box estimate (formerly the `BuildAnalytical` method
   on `ChamberVoxelBuilder`) lifted into
   `Voxelforge.Core/Geometry/ChamberAnalyticalBuilder.cs`. The PicoGK-
   using `ChamberVoxelBuilder.Build` stays in Voxels; the legacy
   `BuildAnalytical` becomes a one-line pass-through to the Core helper
   for back-compat.
3. **`IVoxelGenerator` interface + `ChamberVoxelBuilderAdapter` +
   `AnalyticalOnlyVoxelGenerator`.** Interface in Core; the adapter
   (App-callable, PicoGK-using) in Voxels; the `AnalyticalOnly`
   singleton (Core-only, no PicoGK) routes both `Build` and
   `BuildAnalytical` through `ChamberAnalyticalBuilder` for headless /
   bench / unit-test callers.
4. **App-side callers updated.** `Program.cs`, `MonolithicEngineBuilder`,
   `KioskPipeline`, `StlExporter` — every full-voxel build site now
   passes `voxelGenerator: new ChamberVoxelBuilderAdapter()` explicitly.
   The orchestrator's signature gains
   `IVoxelGenerator? voxelGenerator = null` defaulting to
   `AnalyticalOnlyVoxelGenerator.Instance` so the existing
   `skipVoxelGeometry: true` headless path keeps working unchanged.

### Phase 2 — orchestrator file move (**SHIPPED** PR #324, 2026-04-30)

The interface refactors (`IAerospikeBuilder`, `ITurbopumpGenerator`,
`ITurbineGenerator`) landed in Phase 2, enabling the file moves. The
App now provides four adapters (`ChamberVoxelBuilderAdapter`,
`AerospikeBuilderAdapter`, `TurbopumpGeneratorAdapter`,
`TurbineGeneratorAdapter`) that implement the Core interfaces and
delegate to PicoGK-using builders in Voxels.

Completed moves:

5. **`RegenChamberOptimization` → `Voxelforge.Core/Optimization/`.**
6. **`AerospikeOptimization` → `Voxelforge.Core/Optimization/`.**
7. **`MonolithicEngineBuilder` → `Voxelforge.Voxels/Geometry/`.** PicoGK-heavy;
   lives in Voxels where PicoGK is a project reference.
8. **`ToleranceAnalysis` + `MemoryProjectionGate` → `Voxelforge.Core/`.**
9. **`ResourceBudget` partial-class split** — Core holds `ResourceBudgetSettings`;
   the `SessionSettings`-binding methods remain in App as a partial-class extension.

## Consequences

- **Headless API surface is finally clean.** `voxelforge-eval`,
  test-data assimilation (OOB-1), and the causal gate explainer (OOB-13)
  can call `RegenChamberOptimization.GenerateWith` with `skipVoxelGeometry:
  true` from any non-WinForms host without spawning a subprocess.
- **Engine-family abstraction (greenfield rec #1) is unblocked.** The
  air-breathing pillar in Step 1 of the scope-expansion roadmap can wire
  a parallel `AirbreathingOptimization` to the same `IVoxelGenerator`
  seam — no rewrites required.
- **App-side voxel callers explicit.** The only callers passing a
  non-null `IVoxelGenerator` today are the WinForms form constructors,
  `Program.cs` batch runs, and `MonolithicEngineBuilder` (now in Voxels).
  Test fixtures and benchmarks all pass `skipVoxelGeometry: true`,
  matching the existing convention.
- **`ChamberVoxelBuilderAdapter` is the single PicoGK escape hatch from
  the optimization pipeline.** Anyone adding a new App-side caller passes
  the same adapter; anyone adding a new headless caller passes null +
  `skipVoxelGeometry: true`. The "which path am I on?" question has a
  single answer at the call site.
- **No public API change for SA / bench callers.** The SA optimizer's
  `RegenChamberOptimization.Bounds` / `Pack` / `Unpack` / `Evaluate`
  surfaces are unchanged. Bench-SA fingerprints round-trip bit-identical
  vs the post-A-2 baselines (`bench-sa-*-2026-04-30-post-A2.jsonl`).

## Anti-recommendations

- **Don't move `ChamberVoxelBuilder.Build` itself to Core.** It directly
  allocates `new PicoGK.Voxels(...)` and calls `Library.Log`; abstracting
  through an interface costs more than it saves and makes the voxel
  build path harder to follow when debugging.
- **Don't add an analytical-only stub in Core.** `BuildAnalytical` lives
  alongside `Build` in Voxels because they share construction-helpers
  (per-station radius interpolation, channel-rib fitting). Splitting them
  would force duplicating ~200 LOC of helpers into Core.
- **Don't try to hide `IVoxelHandle`** behind a generic `IVoxelGenerator<T>`.
  A future air-breathing pillar may use a different voxel type, but it
  will use a different `IVoxelGenerator` too — the rule-of-three
  abstraction trap argues for
  keeping this rocket-shaped until a concrete second consumer exists.
