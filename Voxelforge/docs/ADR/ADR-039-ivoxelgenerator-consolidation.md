# ADR-039 — `IVoxelGenerator` consolidation: canonical interface, dispatch surface, per-pillar opt-out

**Status:** Proposed (2026-05-16)
**Sprint:** B-audit follow-up (red-team architecture audit)
**Closes (in part):** [#565](https://github.com/poetac/voxelforge/issues/565) — this ADR covers the ADR-039 portion only; ADRs 040–042 are tracked separately under the same issue.
**Related:**
[ADR-015](ADR-015-core-voxels-app-split.md) (Core/Voxels/App split) ·
[ADR-021](ADR-021-orchestrator-decoupling.md) (the original rocket-only `IVoxelGenerator` seam) ·
[ADR-026](ADR-026-multi-pillar-coordination.md) (multi-pillar coordination) ·
the 2026-05-16 architecture audit, finding F-10.

## Context

ADR-021 (2026-04-30) introduced a single Core-side seam,
`Voxelforge.Optimization.IVoxelGenerator`, so the rocket orchestrator
(`RegenChamberOptimization`) could call into PicoGK-using voxel builders
without referencing the `Voxelforge.Voxels` project directly. ADR-021's
anti-recommendations explicitly deferred unifying the seam across future
pillars:

> Don't try to hide `IVoxelHandle` behind a generic `IVoxelGenerator<T>`.
> A future air-breathing pillar may use a different voxel type, but it
> will use a different `IVoxelGenerator` too — the rule-of-three
> abstraction trap argues for
> keeping this rocket-shaped until a concrete second consumer exists.

The 2026-05-16 architecture audit (finding F-10) finds
that consumer count is no longer one. Four pillars now ship parallel
`IXxxVoxelGenerator` interfaces that occupy the same architectural slot
but have drifted in shape:

| Pillar             | Interface                                                                     | Signature                                                                            | Notes                                                  |
| ------------------ | ----------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ | ------------------------------------------------------ |
| Rocket             | `Voxelforge.Optimization.IVoxelGenerator`                                     | `Build(ChamberBuildOptions, double voxelSize_mm)` + `BuildAnalytical(ChamberBuildOptions)` | Canonical pair; analytical opt-out via `AnalyticalOnlyVoxelGenerator.Instance`. |
| Air-breathing      | `Voxelforge.Airbreathing.IAirbreathingVoxelGenerator`                         | Per-variant overloads (`Build(RamjetContour, RamjetBuildOptions)`, `Build(PulsejetContour, PulsejetBuildOptions)`, …) with default `NotSupportedException` stubs | Adapters implement the variants they support.         |
| Electric Propulsion| `Voxelforge.ElectricPropulsion.IElectricPropulsionVoxelGenerator`             | `object Build(ElectricPropulsionEngineDesign design)`                                | Returns `object` because the concrete `ResistojetGeometryResult` lives in the Voxels project and Core can't reference it without a circular dep. |
| Marine             | `Voxelforge.Marine.Geometry.IMarineVoxelGenerator`                            | `Build(MarineDesign, MarineHullBuildOptions)`                                        | No analytical-only sibling.                            |

The rule of three is over-met. The shapes differ on three axes:

1. **Voxel-size parameter.** Rocket threads `double voxelSize_mm`
   explicitly so callers can retry at a coarser grid on memory-budget
   exceptions (`MemoryBudgetExceededException`). The pillar variants
   bury voxel-size inside the per-pillar build-options record (or do not
   expose it at all).
2. **Analytical-only path.** Rocket has a dedicated `BuildAnalytical`
   method and a Core-side singleton `AnalyticalOnlyVoxelGenerator.Instance`
   that routes both `Build` and `BuildAnalytical` through the pure-C#
   `ChamberAnalyticalBuilder`. No other pillar has an analytical-only
   opt-out — headless callers either skip the geometry step entirely
   (airbreathing tests) or take a hard dependency on the Voxels project.
3. **Return type.** Rocket / air-breathing / marine each return a
   typed `*GeometryResult` record. EP returns `object` and casts at the
   call site (`Voxelforge.ElectricPropulsion.StlExporter`) because the
   concrete result type lives in the Voxels project. The opaque return
   is acknowledged as debt in the EP interface XML comment:
   "A future ADR may extract a shared `IVoxelBuildResult` interface to
   the Core layer."

ADR-021's anti-recommendation is now itself outdated guidance. This ADR
records the unification direction without forcing a single-sprint big-
bang rewrite.

## Decision

**D1. The canonical contract for any pillar voxel generator is
`Build(typed build-options) → typed result` plus an optional
`BuildAnalytical(typed build-options) → typed result` for pillars whose
physics evaluation can score without a voxel allocation.** The two
methods take the same options record and return the same typed result
shape; an analytical-only implementation produces a result whose
voxel-handle field is `null` (or an inert handle) and whose mass / cost
/ bounding-box numbers are populated by closed-form code.

Concretely:

```csharp
// Rocket — canonical shape (Voxelforge.Core/Optimization/IVoxelGenerator.cs)
public interface IVoxelGenerator
{
    Geometry.ChamberGeometryResult Build(
        Geometry.ChamberBuildOptions opts,
        double voxelSize_mm);

    Geometry.ChamberGeometryResult BuildAnalytical(
        Geometry.ChamberBuildOptions opts);
}
```

The `voxelSize_mm` parameter is a deliberate rocket-shape concession to
the auto-coarsening path (`GenerateWithAutoCoarsen`), which retries at
larger voxel sizes when the projected grid exceeds the memory budget.
Future pillars whose builders need to expose voxel-size for the same
reason should adopt the same explicit parameter; pillars where the
build-options record already carries the grid resolution may keep
voxel-size internal.

**D2. The dispatch surface is the orchestrator's optional generator
parameter.** Pillar orchestrators (`RegenChamberOptimization.GenerateWith`,
the equivalent air-breathing / EP / marine entry points) accept the
generator as an optional parameter:

```csharp
public static RegenGenerationResult GenerateWith(
    OperatingConditions cond, RegenChamberDesign design, double voxelSize_mm = 0.0,
    bool skipVoxelGeometry = false,
    ...
    IVoxelGenerator? voxelGenerator = null,
    ...)
```

Three call-site shapes are valid (rocket is the reference):

- `skipVoxelGeometry: true, voxelGenerator: null` — headless / bench-SA
  / unit-test path. The orchestrator never consults the generator.
- `skipVoxelGeometry: false, voxelGenerator: <adapter>` — App-side
  full-build path. The adapter (e.g.
  `Voxelforge.Geometry.ChamberVoxelBuilderAdapter` for rocket) is the
  PicoGK escape hatch from the optimization pipeline.
- `skipVoxelGeometry: false, voxelGenerator: AnalyticalOnlyVoxelGenerator.Instance`
  — explicit per-pillar opt-out (D3 below). Same shape as a real build,
  but no voxel allocation.

When `skipVoxelGeometry: false` AND `voxelGenerator: null`, the
orchestrator raises a diagnostic `InvalidOperationException`. This is
the "missing adapter" trip-wire that catches App-side callers that
forgot to inject; it never fires in test runs.

**D3. The per-pillar opt-out is a Core-side singleton on the canonical
interface — for the rocket pillar that is
`AnalyticalOnlyVoxelGenerator.Instance`.** The opt-out exists so a
headless caller can run physics-scoring code paths that *would* call
the generator without paying the voxel-allocation cost and without
inventing parallel `*Analytical` overloads on the orchestrator. Two
properties matter:

- **Stateless singleton.** The opt-out has a private constructor and a
  single static `Instance` field; the generator is pure (no per-call
  state) and shared across threads. Reflection-allocating new instances
  is a code smell — the singleton is the right answer.
- **Both `Build` and `BuildAnalytical` route through the analytical
  builder.** The result's voxel-mesh field is `null!` (callers must not
  dereference it). Mass / cost / bounding-box numbers are correct.
  This matches the pre-A-3 behaviour where bench-SA fixtures called
  `GenerateWith(cond, design)` at `voxelSize_mm = 0` and ignored the
  mesh-less result.

The same shape — `<Pillar>AnalyticalOnlyVoxelGenerator.Instance` in
Core — is the canonical pattern for any future pillar that wants an
opt-out. Pillars whose physics score depends on the actual voxel field
(SIMP topology, LPBF surface-roughness analysis) need not provide an
analytical-only opt-out; in those cases the headless test path takes
`skipVoxelGeometry: true` and the generator parameter stays null.

**D4. The four parallel interfaces are not consolidated into one
generic in this ADR.** A generic `IVoxelGenerator<TOptions, TResult>`
seam was considered and rejected (see Alternatives) on the grounds
that:

- The pillar-specific options and result records are typed at the
  pillar boundary (`ChamberBuildOptions`, `RamjetBuildOptions`,
  `MarineHullBuildOptions`, …); a generic seam mostly renames the
  problem.
- The four pillars already discover the same dispatch surface through
  the orchestrator's optional `voxelGenerator` parameter — naming
  consistency, not a shared type, is the binding contract.

What this ADR does instead is **codify the parallel-interface pattern
as the canonical convention**: future pillars adopt the existing
`IVoxelGenerator` shape (per-pillar typed interface with `Build` and
optional `BuildAnalytical`), provide a Core-side
`AnalyticalOnly<Pillar>VoxelGenerator.Instance` opt-out when the pillar
supports analytical-only scoring, and dispatch through the
orchestrator's optional parameter.

**D5. The EP `object`-return debt is tracked, not closed here.** The
`IElectricPropulsionVoxelGenerator.Build` method returning `object`
predates this ADR and stays a known pillar-specific deviation. A future
ADR may either (a) extract a marker interface `IVoxelBuildResult` to
Core that all pillar result records implement, or (b) move the
concrete `ResistojetGeometryResult` to a Core-visible namespace. Both
options are deferrable; the pillar works correctly today.

## Consequences

**Positive:**

- The rule-of-three audit finding is closed: the canonical shape is
  now documented, and any future pillar voxel generator lands in a
  known structure (typed interface, optional `BuildAnalytical`, Core-
  side singleton opt-out, orchestrator-parameter dispatch).
- The per-pillar opt-out (`AnalyticalOnlyVoxelGenerator.Instance`) is
  the *single* point where "I'm not voxelising right now" is expressed
  to the pipeline. Bench-SA / `voxelforge-eval` / unit-test paths all
  go through one named surface, making the headless-vs-full split
  discoverable by reading the orchestrator signature.
- No code change required. The four parallel interfaces already match
  the canonical shape closely enough that the ADR ratifies the existing
  practice rather than mandating a rewrite. Drift between this ADR and
  the live code surfaces are bounded: EP's `object` return type (D5),
  the absence of an analytical-only opt-out outside rocket.

**Neutral:**

- The per-pillar opt-out adds an explicit "I'm not voxelising" surface
  visible at the call site (the orchestrator parameter `voxelGenerator`).
  This is intentional — the alternative (silent skipping based on a
  bool flag) makes the code path harder to follow. Discoverability is
  served by naming: `AnalyticalOnlyVoxelGenerator.Instance` is one
  searchable symbol whose name explains its purpose.

**Negative:**

- Pillars without an analytical-only path (air-breathing, EP, marine
  today) cannot use the orchestrator-parameter convention to request
  "analytical-only scoring"; they must either provide the opt-out or
  fall back to `skipVoxelGeometry: true`. A small inconsistency until
  per-pillar `AnalyticalOnly<Pillar>VoxelGenerator` singletons land.
- This ADR ratifies four parallel interfaces rather than collapsing
  them. A future contributor may reasonably re-open the "should this be
  one generic?" question; the answer documented here is "not yet, and
  only with a concrete cross-pillar consumer driving the need."

## Alternatives considered

- **Keep ADR-021's deferral (status quo).** Rejected — the rule of
  three is met (per audit F-10) and ADR-021's anti-recommendation
  refers to a state of the codebase that no longer exists. Leaving the
  guidance frozen means the next pillar contributor inherits ambiguous
  "should I copy rocket or invent my own shape?" pressure.
- **Collapse into a single generic
  `IVoxelGenerator<TOptions, TResult>`.** Rejected — the four pillars'
  options / result records are typed at the pillar boundary, and the
  generic seam mostly relocates the type-naming problem. The
  orchestrator-parameter convention already gives the four pillars a
  shared dispatch shape without forcing a shared type. Revisit if a
  fifth pillar ships with the same shape and the parallel maintenance
  cost becomes the binding concern.
- **Abstract base class instead of interface.** Rejected — adapters
  are thin (one delegation per method); inheritance adds nothing the
  interface doesn't already provide and forces single-inheritance
  constraints on the concrete builders that live in pillar Voxels
  projects.
- **Sealed registry pattern (one `VoxelGeneratorRegistry` keyed by
  pillar enum).** Rejected — the orchestrator already knows its pillar
  at compile time; a runtime registry just relocates the dispatch
  decision to a less type-safe place. The optional-parameter convention
  is simpler.

## References

- `Voxelforge.Core/Optimization/IVoxelGenerator.cs` — canonical rocket interface.
- `Voxelforge.Core/Optimization/AnalyticalOnlyVoxelGenerator.cs` — Core-side singleton opt-out.
- `Voxelforge.Voxels/Geometry/ChamberVoxelBuilderAdapter.cs` — rocket PicoGK escape hatch.
- `Voxelforge.Airbreathing.Core/IAirbreathingVoxelGenerator.cs` — air-breathing per-variant pattern.
- `Voxelforge.ElectricPropulsion.Core/IElectricPropulsionVoxelGenerator.cs` — EP `object`-return debt (D5).
- `Voxelforge.Marine.Core/Geometry/IMarineVoxelGenerator.cs` — marine pattern.
- The 2026-05-16 architecture audit, finding F-10.
