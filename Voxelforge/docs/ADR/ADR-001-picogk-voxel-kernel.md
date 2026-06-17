# ADR-001 — PicoGK as voxel kernel

**Status:** Accepted
**Date:** 2026-04-21 (documented; decision predates)

## Context

The project needs a voxel / implicit-SDF geometry kernel capable of:
- Revolved contours, cylinders, boxes, annuli, plenums (cooling jackets).
- Boolean ops (union, subtract) at production speed.
- Emitting printable STL for LPBF manufacturing.
- Running on Windows x64 with a managed .NET surface.

Alternatives considered: OpenVDB direct (C++, no managed wrapper), self-
rolled SDF + marching cubes (multi-year effort), IntelliCAD / commercial
kernels (expensive, closed).

## Decision

Adopt **PicoGK** (Leap71 open-source voxel kernel) version 1.7.7.5 via
NuGet, running on .NET 9.0 Windows.

## Alternatives rejected

- **OpenVDB direct** — no stable managed wrapper; cost of maintaining a
  wrapper exceeds PicoGK's ecosystem benefit.
- **Self-rolled SDF engine** — well over 12 months of work; PicoGK already
  has march-cubes meshing, STL export, viewer, thread model.
- **Commercial (Autodesk Platform Services, etc.)** — license cost
  unacceptable for a solo dev / skunkworks posture.

## Consequences

Positive:
- ~15 k LOC we didn't have to write.
- Active upstream (Leap71) — Noyron / Helix HX reference implementations.
- Free (Apache 2.0).

Negative:
- Coupled to .NET (language rewrite would require abandoning the kernel).
- `Library.Go(voxelSize, fnTask)` sets voxel size process-globally for
  lifetime of the process — can't switch mid-session (drives
  [ADR-005](ADR-005-physics-tests-in-benchmarks.md) and the
  `StlExporter` subprocess pattern).
- Single global `Library` singleton hostile to xUnit dispose lifecycle.
  (PicoGK 2.0.0, released ~April 2026, introduced non-global `Library`
  instantiation with scoped lifetime — this constraint may lift once the
  v2.0 upgrade ships; see issue #290 and [ADR-005](ADR-005-physics-tests-in-benchmarks.md).)
- Voxel ops not thread-safe — forces a three-thread model: WinForms UI
  thread, PicoGK task thread (all voxel ops), and `SharedState` for
  marshalling between them (see the project docs "PicoGK pitfalls" §4).

## Notes

- Post-[ADR-015](ADR-015-core-voxels-app-split.md) project split,
  PicoGK imports are confined exclusively to the `Voxelforge.Voxels/`
  project (geometry builders). App-project callers access voxel results
  via `IVoxelHandle` ([ADR-016](ADR-016-infra-followups-A5-ivoxelhandle-B1.md))
  and `IVoxelGenerator` ([ADR-021](ADR-021-orchestrator-decoupling.md))
  interfaces. No PicoGK in Core or Tests.
- PicoGK 2.2.0 is the current pin (bumped 2026-06-15, [PR #861](https://github.com/poetac/voxelforge/pull/861)); see [ADR-011](ADR-011-picogk-version-pinning.md).
- Updating PicoGK requires a full regression-suite pass. See
  [ADR-011](ADR-011-picogk-version-pinning.md).
