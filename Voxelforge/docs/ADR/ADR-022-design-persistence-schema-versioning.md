# ADR-022 — Design persistence schema versioning contract

**Status:** Accepted (2026-05-01)
**Supersedes:** —
**Related:** `Voxelforge.Core/IO/DesignPersistence.cs`, JSON round-trip tests.

## Context

`RegenChamberDesign` is persisted as JSON and embedded in `.3mf` metadata.
The record has grown from ~18 fields at project creation to 80+ fields today
as new physics knobs, SA dimensions, and feature flags have been added.
Adding, renaming, or removing a field without a migration step causes one
of two failure modes:

- **Silent data corruption** — new field added without migration: existing
  saved files load successfully but the new field reads as `default(T)`,
  silently discarding the seeded value the user had tuned.
- **Deserialisation error** — renamed or type-changed field: existing files
  throw a JSON exception or produce garbage.

The project has shipped 12 schema bumps on the rocket persistence record
since v19 (v19 → v31 as of 2026-05-06). Each bump was an *identity
migration*: new fields get their default values, all existing fields
round-trip unchanged. This contract is load-bearing and must be followed
by every developer who touches the design record.

The Wave-1 multi-pillar burst (2026-05-05/06) introduced **per-pillar
schema chains** — each pillar's persistence record gets its own
`CurrentSchemaVersion` constant + `Known[]` ordered list. As of 2026-06:
rocket = v31, airbreathing = v12, electric propulsion = v10, marine = v5,
nuclear = v5. The contract below applies to each pillar's persistence
record independently; pillars never share a schema version.

## Decision

Every change to a persisted record's schema requires:

1. **Bump `CurrentSchemaVersion`** in `Voxelforge.Core/IO/DesignPersistence.cs`.
   The version is a `string` (`"v26"`, etc.) embedded in every serialised file.
2. **Add a migration step** in the schema upgrader (the `SchemaUpgrader` logic
   in `DesignPersistence.cs`) that handles the prior-version format:
   - Field additions: `record.NewField ?? defaultValue` (JSON null → default).
   - Field renames: read old key, write new key, set old key absent.
   - Field removals: silently skip on read; old JSON just carries an extra key
     that deserialisation ignores.
   - Type changes: deserialise old type, convert, write new type.
3. **Bit-identical round-trip** — existing fields in a file written at version N
   must deserialise identically at version N+1. A round-trip test at the new
   version is required for every bump.

The migration chain is **forward-only**. No downgrade path is provided.

## What constitutes a valid identity migration

An identity migration:
- Does NOT change the serialised form of any pre-existing field.
- Adds `default(T)` for every new field when reading older files.
- Updates `SchemaVersion` in the output to the new version.
- Passes a test of the form:

  ```csharp
  var design = DesignPersistence.Deserialise(legacyJsonAtPriorVersion);
  Assert.Equal(DesignPersistence.CurrentSchemaVersion, design.SchemaVersion);
  Assert.Equal(expectedDefault, design.NewField);
  // ... all pre-existing fields are bit-identical to input ...
  ```

## Alternatives rejected

- **No versioning** — first field addition breaks all existing saved files on
  the next `Deserialise` call with no diagnostic. Unacceptable.
- **Semantic versioning (major.minor.patch)** — over-engineered for a single
  persisted record with a small user base. Major/minor distinction adds
  ceremony without benefit.
- **JSON default values only** (no explicit migration step) — sufficient for
  additions but not renames, type changes, or removals. Hides the contract
  inside the JSON library's default behaviour, making it easy to miss.
- **Additive-only constraint** (never rename or remove a field) — would force
  stale field names to accumulate indefinitely. Acceptable short-term but
  untenable as the schema matures.

## Current state

Schema version **v26** as of 2026-05-01. Migration chain:

| Bump | PR | Trigger |
|------|----|---------|
| v19 → v20 | #269 | PH-47: electric-pump battery energy budget + common-shaft RPM |
| v20 → v21 | #264 | PH-49: tap-off cycle axial-station knob |
| v21 → v22 | #292 | PH-40: LCF gate + `MissionCycles` field |
| v22 → v23 | #317 | B-2: voxel thrust-takeout adapter fields |
| v23 → v24 | #319 | B-3: acoustic damper fields (6 new fields) |
| v24 → v25 | #328 | C-1: E-D nozzle (no new fields; topology enum value) |
| v25 → v26 | #351 | OOB-1 Sprint 2: `CoolantHtcScalingFactor` + `CoolantFrictionScalingFactor` |

## Key files

- `Voxelforge.Core/IO/DesignPersistence.cs` — `CurrentSchemaVersion` const,
  serialise/deserialise entry points, migration chain.
- JSON round-trip tests in `Voxelforge.Tests/` — pin current schema version
  and validate identity migration for each bump.
