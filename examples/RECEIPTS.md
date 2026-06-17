# Reproducibility receipts

Every committed example design will ship with a SHA-256 hash of its
generated output STL. A reader who runs voxelforge against the same
`input.json` should then get an output file whose hash matches the row
below. (No example carries a receipt yet — see **Status** below.) If it
doesn't, either:

1. voxelforge's physics or geometry path changed (check `git log`),
2. the PicoGK version drifted (should be 2.2.0 per [ADR-011](../Voxelforge/docs/ADR/ADR-011-picogk-version-pinning.md)),
3. something in your build environment is non-deterministic and we need to know about it.

The reproducibility CI job (coming in a future sprint) will regenerate
one fixture STL per PR and fail merge if the hash drifts without a
corresponding receipt update.

## How to generate a receipt row

1. Run voxelforge against `input.json` as described in [`README.md`](README.md).
2. Hash the output:
   ```powershell
   Get-FileHash <path-to-output.stl> -Algorithm SHA256
   ```
3. Add a row below. Include the voxelforge commit SHA at the time of generation — physics changes will invalidate the hash even on identical input.

## Receipts

> **Status:** scaffold only. Populated when example designs land.

| Design | Input | Output size | SHA-256 | voxelforge commit | PicoGK |
|---|---|---|---|---|---|
| *(none yet)* | | | | | |

## Example row format (for contributors)

```
| small-pressure-fed-5kn | examples/small-pressure-fed-5kn/input.json | 12.3 MB | a1b2c3d4e5f6… | `ac9ff88` | 2.2.0 |
```

Abbreviate the hash to the first 16 characters in the table; keep the
full 64-character hash in a sidecar file alongside the input JSON
(e.g. `examples/<class>/output.stl.sha256`) so a downstream verifier
has the authoritative value.
