# ADR-002 — WinForms UI

**Status:** Accepted (with exit path flagged)
**Date:** 2026-04-21 (documented; decision predates)

## Context

The project needs a desktop UI for:
- ~80 numeric inputs + 14 standalone checkboxes + combo boxes.
- Live voxel viewer hosted in the same window (PicoGK's GLFW viewer).
- Custom paint panels (Pareto scatter, axial profile, convergence trace,
  start-transient chart, tolerance histogram, design compare).
- Windows 10/11 target.

Alternatives considered: WPF, Avalonia, Electron, web frontend (Blazor or
three.js).

## Decision

Use **WinForms** on .NET 9 with `UseWindowsForms=true`.

## Alternatives rejected

- **WPF** — PicoGK viewer is a GLFW window; unverified hosting inside WPF.
  Would need spike.
- **Avalonia** — cross-platform win (future Linux) but same GLFW-hosting
  risk; solo dev can't absorb the learning curve today.
- **Electron / Web** — PicoGK output is managed C#; would need an RPC
  layer. Scope too large for v1.
- **No desktop UI at all (CLI + viewer only)** — rejected because the
  optimizer + tolerance sweep + feasibility gates benefit from
  interactive exploration.

## Consequences

Positive:
- Ships fast. Single-process integration with PicoGK's viewer works
  immediately.
- Familiar layout primitives (`Panel`, `FlowLayoutPanel`, `GroupBox`).

Negative:
- Windows-only. Cross-platform (Linux CI, cloud compute) blocked on UI
  rewrite.
- DPI scaling is fragile at 125 % / 150 %. Several historical incidents
  (button widths, label widths, row heights, `GroupBox` +
  `FlowLayoutPanel` `AutoSize` collisions) drove the explicit `Width` /
  `Height` discipline visible in `RegenChamberForm.Builders.cs`. Don't
  introduce auto-sizing controls without re-validating at 125 % DPI.
- WinForms is in maintenance mode — Microsoft is not adding features.

## Exit path

A UI-only rewrite (Avalonia / Blazor) is on the horizon **only** when a
concrete customer asks for cross-platform or web access. Do NOT trigger
the rewrite speculatively. The strangler-fig operating principle applies:
refactor in place, do not cold-rewrite.

A headless solver core + thin UI client is the natural extraction path.
The first step would be extracting an `ISolverHost` interface from
`RegenChamberForm.cs` so the WinForms UI is one consumer among others.
[ADR-021](ADR-021-orchestrator-decoupling.md) partially realizes this
direction — the `IVoxelGenerator` seam already separates the physics
orchestrator from the PicoGK back-end. The remaining step is a
UI-facing command/event abstraction over `RegenChamberForm.cs`. This is
not in any planned sprint; it's listed here so a future contributor
knows the shape of the eventual exit.
