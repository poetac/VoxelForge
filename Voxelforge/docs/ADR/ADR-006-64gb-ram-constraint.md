# ADR-006 — 64 GB RAM as the reference workstation budget

**Status:** Accepted
**Date:** 2026-04-21 (documented; constraint surfaced during the v4.31 → v4.44 envelope work; framing softened from "hard constraint" to "reference budget" 2026-04-22)

## Context

Voxel-based geometry generation has memory cost that scales roughly with
`(bbox_x / voxel_mm) × (bbox_y / voxel_mm) × (bbox_z / voxel_mm) × bytes_per_voxel`.

At 0.4 mm voxel on the 500 N baseline (bbox ~200 × 60 × 60 mm) this is
~10.5 M dense voxels × 4 bytes = ~42 MB. Sparse storage brings it down
further (~0.50 factor). Tractable on any modern machine.

At 0.4 mm voxel on a 500 kN design (bbox ~400 × 250 × 250 mm scaled via
cube-root thrust) this would project to ~60 GB — uncomfortably close to a
typical 64 GB workstation when the process is also holding the C# heap,
the PicoGK viewer, intermediate implicits, and the STL mesh.

Internal planning through v4.43 implicitly assumed a 128 GB reference
box. That assumption didn't survive contact with how the project actually
gets used.

## Decision

**Treat 64 GB as the reference workstation budget that envelope
heuristics, voxel-size floors, and pre-flight projection are calibrated
against.** This is a *guideline* for what should work comfortably out of
the box on a reasonable user workstation — not a hard ceiling on what
the project supports.

Concretely:

- `MegaScaleEnvelope.Presets64GB` curates a `(thrust → voxel, tiles, mode)`
  map that's been validated against this reference budget.
- `ResourceMode` defaults: `Quiet = 25 %`, `Balanced = 50 %`,
  `Maximum = uncapped` of system RAM. A user on 32 GB or 128 GB sees the
  same percentage budget against their actual hardware.
- `MegaScaleEnvelope.Recommend(thrust_N, budgetBytes)` rescales any
  64 GB preset to a target budget via cube-root voxel growth:
  `voxel_scaled = voxel_64GB × (32 GB / budget)^(1/3)`,
  clamped to PicoGK's [0.05, 2.0] mm range, rounded up to 0.01 mm.
- Pre-flight projection (`MemoryProjectionGate.ProjectPreflight`) blocks
  too-large generates at the UI before allocation, regardless of whether
  the host machine is 16 GB or 256 GB.

A 128 GB user on Maximum mode gets a ~1.25× voxel-resolution
improvement on every preset. A 16 GB laptop on Quiet mode gets a ~1.6×
coarser voxel and proportionally larger tile counts. Larger machines
get more headroom; smaller machines degrade gracefully.

## Alternatives rejected

- **No RAM model at all; let the OS thrash** — caused the 5-hour
  pagefile-crash incident that triggered the v4.31 `MemoryProjectionGate`
  + `MemoryBudgetExceededException` work.
- **Single hardcoded voxel limit** — doesn't scale with thrust; would
  either over-restrict small designs or fail large ones.
- **No reference budget at all, derive everything from the host's RAM** —
  loses the curated preset table that gives users a known-good
  `(thrust → voxel, tiles, mode)` map.

## Consequences

Positive:
- Pre-flight projection gate blocks too-large generates at the UI,
  before voxel allocation.
- 10-preset envelope table (`MegaScaleEnvelope.Presets64GB`) gives
  users a curated `(thrust → voxel, tiles, mode)` map they can trust on
  the reference workstation.
- `AutoCoarsenVoxelToFitBudget` and the tiled-build path let large
  designs succeed by degrading gracefully instead of crashing.

Negative:
- Designs above ~500 kN typically need `ResourceMode.Maximum` + tiling +
  coarse voxel on a 64 GB box.
- Meganewton designs (1-2 MN) are feasible but slow (10-60 min per
  build) and only at ≥ 1.5 mm voxel. They trade fidelity for
  feasibility.

## Rescaling notes

The 64 GB framing is a *calibration anchor* for the preset table, not
a runtime check. Larger and smaller workstations are first-class
citizens — `MegaScaleEnvelope.Recommend` does the cube-root scaling,
`ResourceBudget.AutoProbeDefaults` discovers the host's actual RAM at
form-construction time, and `MemoryProjectionGate` runs against the
detected budget regardless of what 64 GB would have allowed.

If the user community shifts toward larger or smaller workstations as
the dominant configuration, recalibrating the presets is mechanical:
re-run the envelope sweep on the new reference machine and regenerate
the preset table (or rename it to whatever the new anchor is).

## Amendment 2026-05-17 — 96 GB current-workstation tier (#661, #663)

The 64 GB calibration anchor in this ADR is historical. As of
2026-05-17 the canonical workstation tier is **96 GB DDR5**
(Ryzen 9, RTX 5070). To make the asymmetry explicit and remove the
hardware suffix from the public API, the preset constants are now
named by role rather than tier:

| Old name | New name | Bytes | Role |
|---|---|---|---|
| `Budget_64GB_Balanced` | `Budget_ReferenceWorkstation_Balanced` | 32 GB | 64 GB historical anchor — Balanced (50 %) |
| `Budget_64GB_Maximum`  | `Budget_ReferenceWorkstation_Maximum`  | 58 GB | 64 GB historical anchor — Maximum (~91 %) |
| `Presets64GB`          | `PresetsReferenceWorkstation`          | —     | 64 GB original calibration (retained) |
| *(new)* | `Budget_Current_Balanced` | 48 GB | 96 GB current tier — Balanced (50 %) |
| *(new)* | `Budget_Current_Maximum`  | 87 GB | 96 GB current tier — Maximum (~91 %) |
| *(new)* | `PresetsCurrent`          | —     | 96 GB calibration (canonical default) |

`MegaScaleEnvelope.Recommend()` and `BuildSweep()` default
`budgetBytes` switched from `Budget_64GB_Balanced` to
`Budget_Current_Balanced`. `PickPresetBracket()` now selects from
`PresetsCurrent`. The cube-root rescaling math is unchanged; only
the anchor moved (32 GB → 48 GB), so all in-range budgets continue
to receive a calibrated recommendation.

### Derivation of `PresetsCurrent`

`PresetsCurrent` voxel sizes are derived analytically from
`PresetsReferenceWorkstation` via the same cube-root memory math
that `Recommend()` uses at runtime:

```
voxel_96 = voxel_64 × (Budget_ReferenceWorkstation_Balanced / Budget_Current_Balanced)^(1/3)
        = voxel_64 × (32 / 48)^(1/3)
        ≈ voxel_64 × 0.874
```

rounded up to 0.01 mm to match `Recommend()`'s
`Math.Ceiling(scaledVoxel × 100.0) / 100.0` convention. Tile counts
and resource modes carry over unchanged — the larger budget tolerates
them and the finer voxel does most of the resolution gain. The 1.5×
safety factor on top of 0.50 sparsity (this ADR's original Decision §)
is preserved by construction: a smaller voxel uses less memory at the
same bbox, so the 96 GB tier sits further inside the safety envelope
than the original 64 GB calibration did.

### When to do an empirical recalibration

Analytical derivation is rigorous when the PicoGK sparsity model is
stable (it has been since PicoGK 2.0). If a future PicoGK upgrade
materially shifts the sparsity ratio, run a fresh envelope sweep on
the 96 GB tier and overwrite `PresetsCurrent` with measured values.
The `MegaScaleBudgetInvariantTests` property test (#637) sweeps both
tiers and fires if any (thrust × mode) point crosses the 1.5×
ceiling — that's the regression signal.
