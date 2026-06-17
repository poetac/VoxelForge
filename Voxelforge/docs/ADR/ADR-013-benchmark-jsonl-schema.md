# ADR-013 — Benchmark JSONL schema v1

**Status:** Accepted
**Date:** 2026-04-24 (Sprint BB pre-cascade)

## Context

Pre-Sprint-29, every `--bench-*` subcommand emitted ad-hoc JSONL with no
shared schema. `RunRecord.AppendJsonl` in
`Voxelforge.Benchmarks/Program.cs` was the only typed emitter;
aerospike / monolithic / mega-sweep wrote diagnostic stdout but no JSONL,
and the SA bench in BB-2 had no emitter at all (the `--bench-sa` recipe
in `baselines/README.md` referenced a flag that never landed on `main`).

The committed baselines under `Voxelforge.Benchmarks/baselines/`
had no per-record provenance: "captured on the dev box on 2026-04-23"
lived only in the README prose, not the JSONL data itself.

Sprints 30-37 (the physics-correctness cascade) will shift physics-
fingerprint scalars (`peak_wall_t_k`, `coolant_dp_pa`, `coolant_t_out_k`,
`mass_g`, ...) by 10-30 % per design. Without versioned schema +
per-record provenance, future diffs cannot tell "Sprint 32 changed
Bartz" from "the dev box was upgraded" from "the bench is running on a
different git SHA than the baseline."

## Decision

**Every JSONL record under `Voxelforge.Benchmarks/baselines/`
follows schema v1.** Centralised emit lives in `JsonlSchema.cs`;
provenance fields are sourced from `MachineInfo.cs`.

Schema v1 record shape — fixed field order, all values written with
`CultureInfo.InvariantCulture`:

| Order | Field | Type | Source |
|------:|-------|------|--------|
| 1 | `schema_version` | int | constant `1` |
| 2 | `machine_id` | string (16 hex chars) | first 16 chars of SHA-256(`MachineInfo` field tuple) |
| 3 | `git_sha` | string (40 hex chars or `"unknown"`) | `git rev-parse HEAD`, cached process-lifetime |
| 4 | `bench_name` | string | one of the `BenchRegistry` flag names (`voxel`, `autonomous`, `mega-sweep`, `aerospike`, `turbopump`, `monolithic`, `bench-sa`, ...) |
| 5 | `build_config` | string | `"Debug"` or `"Release"` (from `#if DEBUG`) |
| 6 | `timestamp` | string (ISO-8601 UTC) | `DateTime.UtcNow.ToString("O")` |
| 7+ | per-bench payload | varies | bench-specific |

`MachineInfo` field tuple (joined with `|` for SHA-256 hashing):
`cpu_model | logical_cores | physical_cores | ram_gb | os_version |
dotnet_version | picogk_version | build_config`.

## Versioning rules

- **Adding a payload field** is a `schema_version` v1 → v2 bump. No
  silent "we added a field, parsers should ignore unknowns" — the schema
  is strict so historic baselines stay comparable.
- **Removing or renaming a payload field is forbidden** in any version.
  If a metric becomes obsolete, it stays in the schema with a sentinel
  value (`null` or `-1` per type) and `BenchmarkJsonSchemaTests` notes
  its retirement.
- **Provenance fields (rows 1-6) are stable across all versions.** A
  v2 schema still emits them in the same order; only payload changes
  drive the version bump.
- Every schema bump requires (a) a new ADR superseding ADR-013, (b) a
  migration note in the new ADR, (c) `BenchmarkJsonSchemaTests` updated
  to recognise both versions or pin to the new one.
- Adding a new `bench_name` value (e.g. `bench-cfd-export`,
  `bench-microbenches` in BB-3+) does **NOT** bump the schema — only
  payload-field changes do.

## What `machine_id` is good for

- Detecting a baseline captured on a different machine ("baseline
  `machine_id` is `a3f0…`, my run is `b912…` — different machines,
  expect cross-machine noise").
- Sharding `baselines/<machine_id>/` later (BB-roadmap deferred item)
  without a schema break.

## What `machine_id` is NOT good for

- Reproducibility audit. Does NOT cover BIOS settings, OS patch level
  beyond major version, background process load, thermal throttling
  state, GPU presence/state.
- Identifying a *user* — it's a hash of hardware fingerprint, not an
  account ID.

## Alternatives rejected

- **Per-bench schema (one schema per `bench_name`)** — multiplies
  maintenance, defeats single-schema-pinning test. Rejected: one schema
  with optional payload fields per bench keyed by `bench_name`.
- **Embed full machine info in every record** — bloats JSONL by
  ~200 bytes/record. Rejected: hash-only via `machine_id`; full info
  goes to a separate `MachineInfo.Capture()` log line at run start.
- **Use `git describe` instead of `git rev-parse HEAD`** — depends on
  tags being current. Rejected: `rev-parse HEAD` is unambiguous and
  always available in a git checkout.

## Consequences

**Positive:**
- Future bench-diff CLI (BB-6) can group by `(bench_name, machine_id,
  git_sha)` triple. CI artifacts are self-describing.
- Re-runs on the same machine + same `git_sha` are diffable to the
  noise floor; cross-machine runs flag automatically.
- Stale baselines self-identify (mismatched `git_sha` jumps out).

**Negative:**
- Every record gains ~150 bytes of provenance overhead. At ~5
  records/baseline × ~12 baselines = ~9 KB total — negligible.
- Schema-bump ceremony adds friction to adding payload fields. Counted
  as a feature.

## Related ADRs

- ADR-005 (physics tests live in Benchmarks console) — explains why
  `BenchmarkJsonSchemaTests` is the only `.Tests` interaction with this
  data and why BB-2's SA-determinism test must subprocess.
- ADR-006 (64 GB RAM constraint) — drives the 0.4 mm voxel default in
  baselines.
- ADR-011 (PicoGK 1.7.7.5 version pin) — `picogk_version` is sourced
  from `AssemblyInformationalVersion` on the loaded `PicoGK.dll`.

## References in code

- `Voxelforge.Benchmarks/JsonlSchema.cs` — the emitter.
- `Voxelforge.Benchmarks/MachineInfo.cs` — the provenance
  source.
- `Voxelforge.Tests/BenchmarkJsonSchemaTests.cs` — the
  pinning test.

## Open issues

- **Merlin-class downgrade (2026-04-24).** BB-2 `CanonicalDesigns.Merlin`
  preset is currently 100 kN @ Pc 7 MPa LOX/CH4 GG, not the roadmap-
  intended 900 kN @ 10 MPa. Pre-flight against the Sprint-29 gate
  calibration returned 46/46 infeasible candidates (WALL_TEMP,
  YIELD_EXCEEDED, INJECTOR_FACE_T_EXCEEDED, IGNITER_MISSING all
  firing). Holds the LOX/CH4 + GG cycle topology constant while
  landing inside the gate envelope. Revisit when the Sprint 30-37
  physics cascade widens the feasibility window.
