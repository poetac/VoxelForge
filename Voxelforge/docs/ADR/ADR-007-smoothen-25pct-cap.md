# ADR-007 — `Smoothen(d)` capped at 25 % of wall thickness

**Status:** Accepted
**Date:** 2026-04-21 (documented; cap introduced mid-2024 after feature-
loss incident)

## Context

PicoGK's `Voxels.Smoothen(d)` applies a morphological smoothing that
erodes then dilates features below distance `d`. It's valuable for
cleaning up voxel-discretization artefacts around sharp edges.

**Hazard**: if `d` is set larger than half the minimum wall thickness or
rib thickness in the model, the smooth operation destroys features below
`2d` entirely. A user applying `d = 1.0 mm` to a design with a 0.8 mm
rib will end up with a ribless chamber.

This was a real incident — sprint notes report a user who lost all ribs
on a chamber, silently, after applying a smoothing that was too large
for the design.

## Decision

In `ChamberVoxelBuilder.cs`, cap the applied smoothing at **25 % of
`min(wallThickness, ribThickness)`**:

```csharp
float dCapped = MathF.Min(userSmoothing, 0.25f * MathF.Min(tWall, tRib));
if (dCapped < 0.02f) dCapped = 0f; // skip no-op smoothing
voxels.Smoothen(dCapped);
```

This preserves the smoothing benefit on contours and surface finish while
guaranteeing no feature below `2 × 0.25 × minWall = 0.5 × minWall` is
destroyed.

## Alternatives rejected

- **No cap, trust the user** — lost-feature incident demonstrates users
  don't reliably pick safe values.
- **Cap at 10 %** — too aggressive; smoothing below 0.04 mm on a 0.4 mm
  wall is ineffective.
- **Cap at 50 %** — still allows catastrophic feature loss on thin ribs.
- **Warn but don't cap** — a warning the user can miss still lets the
  silent feature loss happen.

## Consequences

Positive:
- No silent feature loss. `Smoothen` is now a safe default-on operation.
- User-facing control stays simple (one `smoothingDistance` input).

Negative:
- Advanced users who genuinely want stronger smoothing on heavy designs
  have no escape hatch. Would need a `force: true` parameter. No one has
  asked.

## Verification

The cap lives in `Voxelforge.Voxels/Geometry/ChamberVoxelBuilder.cs` — the
`safeRadius = Math.Min(opt.SmoothingRadius_mm, 0.25 * minWall)` clamp,
followed by the `Smoothen((float)safeRadius)` call when the radius
exceeds 0.02 mm. Mirrored in the project docs "PicoGK pitfalls" §1. This is
NOT a refactor candidate — the cap is what keeps `Smoothen` safe to
expose as a default-on user control.
