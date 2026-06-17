# ADR-005 — Physics tests live in Benchmarks console app

**Status:** **RETIRED 2026-05-04** (resolved by PicoGK 2.0.0; see "Resolution" section).
**Date:** 2026-04-21 (documented; workaround adopted ~v4.30).
**Retired:** 2026-05-04 after [PR #374](https://github.com/poetac/voxelforge/pull/374) upgraded to PicoGK 2.0.0 and the disposal-probe + first in-process voxel tests passed cleanly.

## Resolution

PicoGK 2.0.0 introduced non-global scoped `Library` instantiation with proper
lifetime management (`new Library(voxelSize)` no longer auto-registers as the
process-wide singleton; the `Voxels(Library, IImplicit, BBox3)` overload binds
voxels to a specific scope). Combined with the in-repo `LibraryScope`
ambient-thread-local helper (Voxelforge.Voxels / Voxelforge.Airbreathing.Voxels),
xUnit can now construct + dispose `Library` instances repeatedly without
crashing the test host.

Verified via:
- `PicoGKLibraryDisposalProbeTests.PicoGK_ScopedLibrary_DisposesCleanly_InXUnit`
  (sphere + mesh round-trip; 2 ms wall clock).
- `ThrustTakeoutAdapterVoxelTests.ChamberVoxelBuilder_AdapterMeshHasMoreTrianglesThanBaseline`
  (full ChamberVoxelBuilder build path with mounting flange + thrust-takeout
  adapter; ~3 s wall clock for two builds).
- `ExpansionDeflectionPlugTests.EdTopology_HasMoreTrianglesThanBellBaseline`
  (E-D nozzle topology vs. Axial baseline; ~2 s wall clock).

The xUnit host disposes both Library instances cleanly between test cases —
no native crash, no resource leak.

## What this means going forward

- **New voxel-building tests should default to in-process xUnit.** The
  pattern is:

  ```csharp
  [Fact]
  [Trait("Category", "VoxelBuild")]
  public void MyVoxelTest()
  {
      using var lib = new PicoGK.Library(voxel_mm);
      using var libScope = LibraryScope.Set(lib);
      // ... call into builders, mesh, assert ...
  }
  ```

- **Subprocess tests remain valuable for three legitimate reasons** (not
  pitfall #8):
  1. **CLI surface tests** — `voxelforge-eval` round-trip, kiosk
     `--headless` mode. Test the actual stdin/stdout/JSONL contract,
     not just the underlying library.
  2. **Cross-process determinism tests** — `BenchSADeterminismTests`
     verifies SA produces byte-identical output across two separate
     subprocess invocations. By definition single-process can't do that.
  3. **Cross-platform test projects** — `Voxelforge.Airbreathing.Tests`
     deliberately targets `net9.0` (not `net9.0-windows`) and does not
     reference `Voxelforge.Airbreathing.Voxels` so the test host stays
     cross-platform. Migrating ramjet voxel tests in-process would
     trade away that property.

- **`SubprocessRunner` and `LibraryScope` both stay.** The first because
  reasons 1-3 above are still real; the second because production CLI
  entry points (StlExporter, Kiosk, Airbreathing.StlExporter) still
  need it for headless ambient-library binding.

- **`Bench_*` test-naming convention in `.Benchmarks`** — no longer needed
  for new voxel tests. Existing Bench_* entries can stay (they are also
  the canonical SA/perf benchmark surface) or migrate as-needed.

## Original context (preserved for history)

PicoGK 1.7.7.5's `Library` class was a process-global singleton initialized
by `Library.Go(voxelSize, fnTask)` (interactive) or `new Library(voxelSize)`
(headless). On dispose, it unloaded native resources.

When xUnit host disposed the test assembly after a test run that included
`new Voxels(implicit, bounds)` calls, the `Library` dispose path **crashed
the test host** (native access violation). This occurred deterministically
on any test that constructed voxels in-process. The workaround was to put
all such tests in `Voxelforge.Benchmarks` console app and either run them
manually or invoke them via `Process.Start` from xUnit.

`IVoxelHandle` ([ADR-016](ADR-016-infra-followups-A5-ivoxelhandle-B1.md)) was
floated as a longer-term abstraction (marker interface so Core could hold
voxel results without a PicoGK reference) but the full `IVoxelKernel` mock
path was not pursued. PicoGK 2.0.0's scoped Library made that work moot.

## Follow-on cleanup

- `Bench_*` voxel tests in `Voxelforge.Benchmarks` may migrate to xUnit
  opportunistically; not blocking.
- `Voxelforge.Airbreathing.Tests` could relax its `net9.0` TFM (and gain
  three in-process voxel tests) if cross-platform-test discipline is no
  longer a target. Tracked as a separate decision; defer until explicitly
  surfaced.
- ADR-005 entry in `Voxelforge/docs/ADR/README.md` marked Retired.
