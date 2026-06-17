# ADR-028 — Turbofan two-shell voxel pattern

**Status:** Accepted (2026-05-06)
**Issue:** Wave-2 follow-on to [#441](https://github.com/poetac/voxelforge/issues/441)
**Supersedes:** —
**Related:** ADR-007 (`Smoothen(d)` 25 % cap), ADR-015 (Core / Voxels split), ADR-022 (schema versioning), ADR-026 (multi-pillar coordination)

---

## Context

The air-breathing pillar's voxel pipeline previously shipped two single-shell
builders: `RamjetVoxelBuilder` (annular pressure shell around one revolved
contour) and `PulsejetVoxelBuilder` (the same pattern, with valveless-pulsejet
intake horn). Both use the same idiom: build inner SDF + outer SDF (= inner +
wall thickness), voxelise both within a generous bounding box, `BoolSubtract`
to get an annular shell, smoothen with a 25 %-of-wall clamp.

A turbofan adds a structural complication: it has **two concentric flow
paths** (cold-stream bypass duct + hot-stream core) that share an inlet face
but exit separately. Real turbofans also carry fan blades, low- and
high-pressure turbine stages, optional mixers/chevrons — but those are
out-of-scope for an LPBF-printable structural shell. The pillar's voxel
pipeline only needs to produce the printable pressure-shell geometry; the
fan/turbine stages are non-printed assemblies that mate to the shell post-print.

The decision in this ADR is *how* to model the two flow paths in the voxel
shell.

---

## Decision

Model the turbofan as **two concentric annular shells joined at the inlet
face**, where each shell is constructed with the same `outer.BoolSubtract(inner)`
idiom the ramjet builder uses:

1. **Core shell** — bounded by the core inner SDF (gas-path radius from the
   `TurbofanContour` core stations) and the core outer SDF (= core inner +
   `WallThickness_mm`).
2. **Bypass-duct shell** — bounded by the bypass inner SDF (= the
   `BypassOuterRadii_m` array on the contour, sampled at the same X
   positions as the core stations) and the bypass outer SDF (= bypass
   inner + `BypassDuctWallThickness_mm`).

The combined voxel field is `coreShell.BoolAdd(bypassShell)` — the two
annular shells share material at the inlet face after the union, which is
exactly the structural ring that joins the two flow paths in real engines.

**Bypass-duct outer radius via area scaling.** The bypass radius profile is
not an independent design knob; it is derived from the bypass ratio:

```
r_bypass_outer(x) = √(r_core(x)² · (1 + BPR))
```

This follows from total-area conservation at each station:

```
A_bypass(x) + A_core(x) = π · r_outer(x)²
A_bypass(x) = BPR · A_core(x)
⇒  r_outer(x)² = r_core(x)² · (1 + BPR)
```

`BPR = 0` cleanly degenerates to a turbojet limit (bypass shell collapses
onto the core outer wall). The chosen BPR range for the single-spool
turbofan model (0.10–2.00, per `BYPASS_RATIO_OUT_OF_BAND`) keeps both
shells geometrically distinct.

**Two distinct wall thicknesses.** The cold-stream bypass duct sees lower
pressures than the hot-stream core shell, so the design record carries
`BypassDuctWallThickness_mm` separately from the existing
`WallThickness_mm` (used by the rest of the airbreathing pillar).
Schema **v9 → v10** adds this field with default 2.0 mm and an identity
migration. The two-thickness split lets the LPBF printability gates see
realistic per-flow-path geometry — a uniform "use the core wall everywhere"
shortcut would over-mass the bypass duct by ≈ 50 % at typical low-bypass
ratios.

**LPBF surface sampling.** A turbofan-aware `TurbofanSurfaceSampler` walks
both flow paths and emits four wall-sample sets per (station × azimuthal
slot):

| # | Wall | Normal direction |
|---|------|------------------|
| 1 | Core inner (gas side) | toward axis |
| 2 | Core outer (faces bypass annulus) | outward |
| 3 | Bypass inner (faces bypass annulus from outer shell) | toward axis |
| 4 | Bypass outer (atmosphere side) | outward |

All four matter for LPBF overhang analysis since each can have
downward-facing patches that need support during the print. The sampler is
otherwise structurally identical to `RamjetSurfaceSampler` — same
finite-difference meridional slope, same half-neighbour segment-length
weighting, same axisymmetric `MeridionalNormal` helper.

**Smoothen-clamp scope.** Per ADR-007, smoothen radius is capped at 25 %
of the *minimum* wall thickness across both shells. With distinct wall
thicknesses, `min(WallThickness_mm, BypassDuctWallThickness_mm)` is the
operative bound; the auto voxel-size resolves to
`min(thinnestWall / 4, MaxAutoVoxelSize_mm)` for the same printability +
structural-margin tolerance the ramjet pipeline targets.

---

## Alternatives considered

1. **Single contour with a step in radius at the fan face.** Would
   produce a single annular shell with a discontinuous outer surface
   transitioning from "core radius + wall" to "bypass outer radius +
   wall". Rejected because it (a) doesn't represent the inner core wall,
   making the core-side LPBF sample set impossible, and (b) the step
   discontinuity is not printable without explicit axial fillets that
   would inflate the geometry code substantially.

2. **Two separate voxel builds with no `BoolAdd`.** Rejected because the
   downstream code expects a single `IVoxelHandle` per build (consistent
   with ramjet/pulsejet/rocket-side patterns); splitting into two
   handles would force every consumer (viewer, STL exporter, LPBF
   sampler) to special-case turbofan.

3. **Real fan/turbine geometry (3D blade rows, not axisymmetric).**
   Rejected for Wave-2 — out of scope for the LPBF-printable shell. The
   axisymmetric pressure shell is what gets printed; fan/turbine stages
   are assembled post-print. If a future sprint needs to model blade
   geometry, the right place is a separate `TurbofanRotorAssembly`
   project, not the voxel-shell builder.

4. **Independent bypass radius array (not derived from BPR).** Rejected
   because BPR is already a first-class SA design variable on
   `AirbreathingEngineDesign`. Adding a separate `BypassOuterRadius_m`
   knob would create two correlated knobs that the SA optimizer would
   need to search jointly — wasteful when the area-scaling identity
   suffices.

---

## Consequences

**Positive:**
- Reuses the established `outer.BoolSubtract(inner)` annular-shell idiom from
  ramjet — no new low-level voxel ops, no new SDF primitive.
- Same auto-voxel-size + smoothen-clamp + LPBF-safe smoothing pipeline as
  the ramjet builder, just scaled to the thinnest of the two walls.
- Schema v9 → v10 migration is an identity (additive field with default
  2.0 mm); existing v9 designs round-trip cleanly.
- BPR=0 degenerates correctly to a turbojet voxel — future turbojet
  voxel work (currently absent — only ramjet + pulsejet + turbofan ship)
  could simply call `TurbofanVoxelBuilder` with BPR=0 instead of building
  a third near-duplicate.

**Negative:**
- The two-shell pattern means every voxel build does **four** SDF
  rasterisations (core inner, core outer, bypass inner, bypass outer)
  instead of the ramjet's two. Wall-clock time at 1 mm voxel is roughly
  doubled. Acceptable at the airframe-integrated scales the pipeline
  targets (200-1000 mm engine length, 50-300 mm OD).
- The combined-shell `BoolAdd` produces a single `IVoxelHandle` so
  downstream code can't trivially separate core from bypass for
  two-tone STL colourability. Future enhancement: emit two handles
  alongside the combined one (sibling fields on `TurbofanGeometryResult`)
  if a real consumer needs them.
- The BPR-derived bypass radius assumes incompressible-area conservation,
  which is fine at typical fan-face Mach numbers (M ≤ 0.8) but
  under-represents the bypass-stream area at higher subsonic regimes.
  Acceptable until a real consumer surfaces — a more rigorous
  derivation pulls density variation into the bypass stream.

---

## How a future sprint extends this

- **Mixer / chevron geometry.** Add a fifth contour station downstream of
  the LP turbine where the bypass radius converges back to the core
  radius for mixed-flow exhaust. The four-shell pattern still works
  unchanged; the bypass-outer radius array just collapses to the core
  outer at the mixer station.
- **Two-spool fan-pressure-ratio split.** `AirbreathingEngineDesign.PiFan`
  is already on the record (nullable, defaults to single-spool proxy
  `π_fan = √π_c`). The voxel pipeline doesn't currently care about
  `PiFan` — fan blades aren't modelled — but a future
  `TurbofanRotorAssembly` would use it.
- **Trapped-powder analysis.** The current builder passes
  `voxelField: null` to `LpbfPrintabilityAnalysis.Run`. Extending to
  support trapped-powder detection means routing the combined voxel
  field through; the LPBF analysis already supports this
  (`RamjetVoxelBuilder` does the same).

---

## Status

Accepted 2026-05-06. Shipped via [PR #445](https://github.com/poetac/voxelforge/pull/445)
together with the schema v9 → v10 bump and 5 subprocess tests.
`TurbofanVoxelBuilder.cs`, `TurbofanContour.cs`, `TurbofanBuildOptions.cs`,
`TurbofanGeometryResult.cs`, `TurbofanSurfaceSampler.cs` are the
load-bearing files.
