# ADR-011 — PicoGK version pin

**Status:** Accepted (current pin: PicoGK 2.2.0, adopted 2026-06-15)
**Date:** 2026-04-21 (original 1.7.7.5 pin); refreshed 2026-05-04 (2.0.0 adoption via [PR #374](https://github.com/poetac/voxelforge/pull/374), closes [#346](https://github.com/poetac/voxelforge/issues/346)); refreshed 2026-06-15 (2.2.0 adoption via [PR #861](https://github.com/poetac/voxelforge/pull/861) — Dependabot grouped bump; PicoGK viewer-crash fix on hybrid-graphics + Magick.NET 14.14.0 ImageMagick security update).

## Context

PicoGK is an active open-source project (ADR-001). Its release cadence is
approximately every 2-6 months, with non-trivial API breaks between
minor versions (e.g. `IImplicit` evaluation order, `Library` lifecycle,
threading model changes).

The project needs a stable PicoGK version to:
- Keep the rocket + per-pillar test suites passing.
- Avoid mid-sprint upgrade churn.
- Give the user a reliable target when printing.

## Decision

**Pin PicoGK via NuGet to a specific version.** Upgrade is a deliberate, scheduled sprint activity, not an automatic update. **Current pin: 2.2.0** across all PicoGK-using projects (`Voxelforge`, `Voxelforge.Voxels`, `Voxelforge.Airbreathing.Voxels`, `Voxelforge.ElectricPropulsion.Voxels`, `Voxelforge.Marine.Voxels`, `Voxelforge.Nuclear.Voxels`, `Voxelforge.Kiosk`, `Voxelforge.Spike.Avalonia`). Original pin (2026-04-21) was 1.7.7.5; bumped to 2.0.0 (2026-05-04) because non-global `Library` instantiation resolved the xUnit + PicoGK disposal pitfall (ADR-005, retired); bumped to 2.2.0 (2026-06-15, [PR #861](https://github.com/poetac/voxelforge/pull/861)) for the PicoGK viewer-crash fix on Windows hybrid-graphics machines plus the bundled Magick.NET 14.14.0 ImageMagick security update.

```xml
<!-- Every PicoGK-using csproj -->
<PackageReference Include="PicoGK" Version="2.2.0" />
```

## Alternatives rejected

- **`Version="*"` or a floating range** — every `dotnet restore` risks
  pulling in a new minor version. Reproducibility lost.
- **Always use the latest** — breaks tests on upgrade; no batching of
  upgrade cost.
- **Fork PicoGK, maintain our own version** — not sustainable for a
  solo dev.

## Consequences

Positive:
- CI reproducibility. Every build uses the same PicoGK binary.
- Regression surface known. PicoGK-specific bugs get absorbed into the
  project's regression tests at each upgrade.
- Predictable behaviour. Third-party bugs don't surprise mid-sprint.

Negative:
- Manual work to upgrade. Must read PicoGK release notes, run full test
  suite, fix any regressions. ~1 day budget per upgrade.
- Behind upstream. Any new PicoGK features (improved VDB writer,
  new implicits, non-global `Library` lifecycle in v2.0) require an
  upgrade sprint to access. The pin advanced to PicoGK 2.2.0 on
  2026-06-15 ([PR #861](https://github.com/poetac/voxelforge/pull/861)).

## Upgrade protocol

When upgrading PicoGK (e.g. 1.7.7.5 → 2.0.x, tracked in issue #290):

1. Cut a feature branch: `git switch -c feat/picogk-2.0.Y`.
2. Bump `<PackageReference>` version in **all four** projects that
   reference PicoGK: `Voxelforge/Voxelforge.csproj`,
   `Voxelforge.Voxels/Voxelforge.Voxels.csproj`,
   `Voxelforge.Benchmarks/Voxelforge.Benchmarks.csproj`,
   `Voxelforge.Kiosk/Voxelforge.Kiosk.csproj`.
3. Run the full test suite (`dotnet test`) and the Release build with
   `-warnaserror`.
4. Run the Benchmarks console app to exercise voxel-construction paths
   not covered by xUnit (per [ADR-005](ADR-005-physics-tests-in-benchmarks.md)).
5. If regressions appear, fix the usage or revert the version and open
   an upstream issue.
6. Capture baseline timing data if voxel performance shifts noticeably.
7. For the 2.0.x upgrade specifically: verify whether the non-global
   `Library` scoped lifetime resolves the xUnit dispose crash described
   in ADR-005. If yes, update ADR-005 status and relax the Benchmarks-
   only constraint.
8. Update this ADR's header with the new pinned version and any release-
   notes-driven behavioural changes worth recording.
