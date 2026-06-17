# Benchmark baselines

Committed baseline captures for the voxel + STL export pipeline plus
the BB-2 pre-Sprint-30 SA physics fingerprint corpus. Each JSONL is
append-only history: one record per recorded run (warm-up excluded).
Stdout logs live alongside each JSONL as `<basename>.stdout.log` so
the `BENCH_MEDIAN` summary block is grep-able without re-parsing the
JSON.

**Schema:** all records under this directory follow JSONL schema v1
per [ADR-013](../../Voxelforge/docs/ADR/ADR-013-benchmark-jsonl-schema.md).
Every record carries a 6-field provenance prefix (`schema_version`,
`machine_id`, `git_sha`, `bench_name`, `build_config`, `timestamp`)
followed by the per-bench payload. `BenchmarkJsonSchemaTests` in the
xUnit suite pins this contract.

These files are the **perf regression floor** for the voxel +
STL export hot path. Any future kernel rewrite (CUDA, SIMD, multi-
threaded marching cubes, etc.) must beat the channel-voxelise median
captured here on the same hardware before merge. The CUDA path is
not on the active roadmap (per ADR-011's PicoGK pin); future
acceleration is whatever PicoGK upstream ships.

## Files

| File | Schema | Captured | Bench | Notes |
|------|:------:|----------|-------|-------|
| `baseline-0.4mm.jsonl` | v1 | 2026-04-24 (Sprint BB) | `voxel` | Default chamber (L=180 mm, OD=56 mm, 80 channels) at 0.40 mm voxel, **N=10** runs (was N=5 pre-Sprint-BB). Schema-v1 re-capture supersedes the 2026-04-23 N=5 capture. |
| `aerospike-0.4mm.stdout.log` | n/a (stdout only) | 2026-04-24 (Sprint BB) | `aerospike` | Aerospike plug-nozzle, 20 kN LOX/CH4, Pc 7 MPa, ε 15, 30 % plug truncation, 24 regen channels, 48-element Coax injector. **N=5** runs (was N=1). Demonstrates all three aerospike-parallel gates firing simultaneously (`AEROSPIKE_PLUG_WALL_TEMP`, `AEROSPIKE_ELEMENT_CLEARANCE`, `AEROSPIKE_INJECTOR_FACE_TEMP`) — designed over-aggressive so it regresses every gate if one silently breaks. |
| `phase4-perf-xunit-2026-04-24.txt` | n/a (test stdout) | 2026-04-24 (Sprint BB) | xUnit `Phase4PerfBenchmarks` | Captured stdout of the four xUnit perf-ceiling tests. Useful as a snapshot of the in-process thermal-solve, tolerance-sweep, 8-SA-candidates, and propellant-cache timings before the Sprint 30+ physics-correctness cascade lands. |
| `bench-sa-merlin-2026-04-24.jsonl` | v1 | 2026-04-24 (Sprint BB-2) | `bench-sa` | **Pre-Sprint-30 physics fingerprint** — Merlin-class LOX/CH4, see "Pre-Sprint-30 physics fingerprint" section below. |
| `bench-sa-rl10-2026-04-24.jsonl` | v1 | 2026-04-24 (Sprint BB-2) | `bench-sa` | Pre-Sprint-30 fingerprint — RL-10-class LOX/H2 closed-expander. |
| `bench-sa-pressure-fed-small-2026-04-24.jsonl` | v1 | 2026-04-24 (Sprint BB-2) | `bench-sa` | Pre-Sprint-30 fingerprint — pressure-fed small thruster (LOX/RP-1 fallback for unimplemented N2O4/MMH). |
| `bench-sa-aerospike-2026-04-24.jsonl` | v1 | 2026-04-24 (Sprint BB-2) | `bench-sa` | Pre-Sprint-30 fingerprint — aerospike LOX/CH4 20 kN, cross-correlates with `aerospike-0.4mm.stdout.log`. |
| `bench-sa-pintle-2026-04-24.jsonl` | v1 | 2026-04-24 (Sprint BB-2) | `bench-sa` | Pre-Sprint-30 fingerprint — pintle LOX/CH4 10 kN, SuperDraco-class topology. |
| `bench-cfd-export.jsonl` | v1 | 2026-04-29 (Sprint BB-3 / PR #206) | `bench-cfd-export` | Single-record CFD VTI export benchmark, 50 iterations × 96³ grid. Replaces the 2026-04-23 phantom; the `--bench-cfd-export` CLI is now real (BenchCfdExport.cs / restored via BB-3). file_bytes 21,234,640 ≈ phantom's 21,234,617 within 23 bytes; median_ms within ±5% of the phantom's 15.38 on a comparable Release build (machine-variance dependent). |
| `bench-cfd-export.stdout.log` | n/a (stdout only) | 2026-04-29 (Sprint BB-3 / PR #206) | `bench-cfd-export` | Stdout capture of the run that produced the JSONL above. |
| `bench-dual-bell-2026-04-29.jsonl` | v1 | 2026-04-29 (Sprint BB-5b / closes [#256](https://github.com/poetac/voxelforge/issues/256)) | `bench-dual-bell` | Dual-bell contour generation timing, 200 iterations, 240 stations. canonical 10 kN LOX/CH4 bell chamber, seaLevelEps=8, inflectionDeg=5. To be regenerated on first Windows run with `--bench-dual-bell --iterations 200 --out baselines/bench-dual-bell-<date>.jsonl`. |
| `bench-linear-aerospike-2026-04-29.jsonl` | v1 | 2026-04-29 (Sprint BB-5b / closes [#256](https://github.com/poetac/voxelforge/issues/256)) | `bench-linear-aerospike` | Linear-aerospike physics-only timing, 200 iterations, X-33-class 20 kN LOX/CH4. To be regenerated on first Windows run with `--bench-linear-aerospike --iterations 200 --out baselines/bench-linear-aerospike-<date>.jsonl`. |

**Deferred to BB-3 / BB-5:** monolithic-engine baseline + mega-sweep
envelope. The monolithic build at 0.4 mm voxel is wall-clock-prohibitive
on the 64 GB workstation budget (PicoGK warns ~15 min, ~19 GB peak per
run). BB-3+ will capture monolithic at 0.8 mm voxel (the tool's
recommended exploration floor) and a smaller mega-sweep envelope.

## Feature coverage matrix (Sprint BB-5, 2026-04-29)

Tracks every Sprint 18-27 feature against its perf baseline. Required
by [#208](https://github.com/poetac/voxelforge/issues/208) acceptance.
BDN benches live in `Voxelforge.MicroBenchmarks/`; SA-style
fingerprint benches live in this directory.

| Feature | Sprint introduced | Baseline source | Bench / file |
|--------|------------------|-----------------|--------------|
| Pintle injector | Sprint 18 (PR #88) | `bench-sa-pintle-*.jsonl` (SA preset) + `FeasibilityGateBench.cs` (gate latencies via Evaluate) | BB-2 + BB-4 |
| Pressure-fed polish | Sprint 19 | `bench-sa-pressure-fed-small-*.jsonl` | BB-2 |
| Dual-bell | Sprint 20 | `bench-dual-bell-2026-04-29.jsonl` (contour-only, 200 iter, 240 stations) | BB-5b |
| Cycle-balance refactor (CycleSolvers registry) | Sprint 21 | `EngineCyclesBench.cs` (BDN, 9 cycle Get-dispatch rows) | BB-5 |
| Closed expander | Sprint 22 | `bench-sa-rl10-*.jsonl` | BB-2 |
| ORSC / Tap-off | Sprint 23-24 | `EngineCyclesBench.Get_ORSC` / `Get_TapOff` | BB-5 |
| Linear aerospike | Sprint 25 | `bench-linear-aerospike-2026-04-29.jsonl` (physics-only, 200 iter, 20 kN LOX/CH4) | BB-5b |
| Aerospike (axisymmetric) | Sprint 10 / 25 | `bench-sa-aerospike-*.jsonl` + `aerospike-0.4mm.stdout.log` | BB-0 / BB-2 |
| LPBF printability | Sprint 27 | `LpbfPrintabilityBench.cs` (24 + 48 azimuthal samples) | BB-5 |
| Tolerance sweep | Sprint 14 / Phase 4 | `ToleranceSweepBench.cs` (BDN: 100 / 500 / 1000 samples) | BB-5 |
| JSON design persistence | Sprint 14 / P14 | `JsonRoundTripBench.cs` (Save + Load) | BB-5 |
| 3MF export | Phase 7 (2026-04-20) | `ThreeMfExportBench.cs` (synthetic-STL → 3MF) | BB-5 |
| CFD VTI export | Sprint 14 / P3 | `bench-cfd-export.jsonl` + `CfdExportBench.cs` | BB-3 |
| Thermal solver hot path | Sprint 16 / P5 | `ThermalSolverBench.cs` (Cold / Warm 80 / Warm 160) | BB-3 |
| SA Pack/Unpack | ADR-010 / Sprint 7 | `PackUnpackBench.cs` (default + patterned) | BB-3 |
| Propellant table cache | Phase 4 / P14 | `PropellantLookupBench.cs` (per pair + interpolated) | BB-3 |
| Coolant correlations | Sprint 16 / 33 | `CoolantCorrelationsBench.cs` (12 rows) | BB-4 |
| Bartz heat-flux | Sprint 32 / PH-5 | `BartzPerStationBench.cs` (4 rows) | BB-4 |
| Feasibility gates (47 total) | ADR-009 / Sprint 9 | `FeasibilityGateBench.cs` (Evaluate × 2 fixtures, PreScreen × 2) | BB-4 |

### BB-5b (shipped 2026-04-29 — closes [#256](https://github.com/poetac/voxelforge/issues/256))

Both CLI subcommands originally deferred from BB-5a have now shipped:

- **`--bench-dual-bell`** (`BenchDualBell.cs`) — times
  `ChamberContourGenerator.Generate` with `dualBell: true` on a
  canonical 10 kN LOX/CH4 chamber. Also captures the single-bell
  baseline for the Sprint 20 "byte-identical single-bell" regression.
  No voxels — PicoGK-free. Emit: `dual_median_ms`, `single_median_ms`,
  `dual_vs_single_ratio`, inflection-point scalars.
  Baseline: `bench-dual-bell-2026-04-29.jsonl` (regenerable via
  `--bench-dual-bell --iterations 200 --out .../bench-dual-bell-<date>.jsonl`).

- **`--bench-linear-aerospike`** (`BenchLinearAerospike.cs`) — times
  `AerospikeBuilder.BuildLinearPhysicsOnly` on an X-33 / XRS-2200-class
  LOX/CH4 20 kN spec (`IsLinear=true`, `LinearPlugWidth_mm=60`). No
  voxels — PicoGK-free. Emit: `median_ms`, sizing scalars
  (`total_length_mm`, `estimated_mass_g`, `throat_outer_radius_mm`).
  Baseline: `bench-linear-aerospike-2026-04-29.jsonl` (regenerable via
  `--bench-linear-aerospike --iterations 200 --out .../bench-linear-aerospike-<date>.jsonl`).

`--bench-pintle` remains structurally covered by `bench-sa-pintle`
(SA fingerprint) + `FeasibilityGateBench` (gate-march cost via Evaluate);
no separate CLI bench planned.

## Pre-Sprint-30 physics fingerprint (canonical 5-design SA corpus)

Captured 2026-04-24 (Sprint BB-2) against post-Sprint-29 worktree
(git SHA `7acb58b94`).

**These are FROZEN reference values.** Sprints 30-37 (the physics-
correctness cascade) will shift the captured `peak_wall_t_k`,
`coolant_dp_pa`, `coolant_t_out_k`, `mass_g`, `min_safety_factor`
scalars by 10-30 % per design. The post-cascade diff against this
snapshot IS the cascade's measured impact.

### Pre-cascade fingerprint (seed score from AutoSeeder defaults)

| Preset | peak_wall_t_k | coolant_t_out_k | throat_q̇ (MW/m²) | mass_g | min_sf | Baseline file |
|--------|--------------:|----------------:|-----------------:|-------:|-------:|---------------|
| `merlin`²            | 1729.1 K | 448.9 K | (see jsonl) |  23 680 | 0.093 | `bench-sa-merlin-2026-04-24.jsonl` |
| `rl10`               |  836.0 K | 305.0 K | (see jsonl) | 283 859 | 0.032 | `bench-sa-rl10-2026-04-24.jsonl` |
| `pressure-fed-small`¹| 2067.0 K | 700.0 K | (see jsonl) |   5 088 | 0.463 | `bench-sa-pressure-fed-small-2026-04-24.jsonl` |
| `aerospike`          | 1688.0 K | 523.0 K | (see jsonl) |     931 | 0.167 | `bench-sa-aerospike-2026-04-24.jsonl` |
| `pintle`             | 1662.0 K | 629.0 K | (see jsonl) |   5 651 | 0.192 | `bench-sa-pintle-2026-04-24.jsonl` |

### Configuration

| Preset | Propellant | Thrust | Pc | ε | Cycle/Topology |
|--------|-----------|-------:|---:|--:|----------------|
| `merlin`² | LOX/CH4 | **100 kN** | 7 MPa | 16 | GasGenerator |
| `rl10` | LOX/H2 | 100 kN | 4 MPa | 84 | ClosedExpander |
| `pressure-fed-small`¹ | **LOX/RP-1** | 1 kN | 0.7 MPa | 25 | PressureFed |
| `aerospike` | LOX/CH4 | 20 kN | 7 MPa | 15 | Aerospike (plug 0.30) |
| `pintle` | LOX/CH4 | 10 kN | 6 MPa | 25 | Pintle injector |

¹ Roadmap originally called for N2O4/MMH; AutoSeeder only implements
LOX_CH4 / LOX_H2 / LOX_RP1 (see `AutoSeeder.cs:134`). LOX/RP-1 swap
holds the cycle topology and small-thrust class constant.

² **Merlin BB-2 downgrade (2026-04-24):** roadmap called for 900 kN @
Pc 10 MPa to span the upper end of the cycle-balance envelope.
Pre-flight against Sprint-29 gate calibration returned 46/46 infeasible
candidates (WALL_TEMP, YIELD_EXCEEDED, INJECTOR_FACE_T_EXCEEDED, plus
IGNITER_MISSING from AutoSeeder's IgniterType.None default).
Downgraded to 100 kN @ Pc 7 MPa — holds the LOX/CH4 + GG topology
constant. ADR-013 "Open issues" tracks. Revisit when the cascade
widens the feasibility window.

### Notes on the captured baselines

- Each preset captured at **N=3 repeats** with seeds (42, 43, 44)
  per the `--bench-sa --repeat 3` default.
- SA exits early on the persistent-infeasibility streak
  (`MaxConsecutiveInfeasibleBeforeExit = 60`) for all 5 presets — the
  AutoSeeder defaults trip thermal/structural gates that the Sprint
  30+ cascade will recalibrate. Per-record `iterations_completed: 60`
  reflects this; `infeasible_exit: true` flags it explicitly.
- The fingerprint scalars (`peak_wall_t_k` etc.) are sourced from the
  **seeded design's preflight Generate+Evaluate**, not from SA's
  best-found candidate (no candidate is feasible). This is the
  meaningful pre-cascade reference: "what physics does AutoSeeder's
  default produce TODAY?"
- All five presets carry `IgniterType.SparkTorch` as a CanonicalDesigns
  override since AutoSeeder leaves IgniterType.None which trips the
  Sprint 29 IGNITER_MISSING gate. Pre-cascade structural / thermal
  gates remain.
- Per-iter SA timing percentiles (p50/p90/p99/mean/stdev/CV) recorded
  alongside the fingerprint scalars. p50 lands ~1.9-2.3 ms per SA
  iteration on the captured machine.

## Headline medians (0.4 mm, **N=10**, 2026-04-24)

(To be updated from the schema-v1 `baseline-0.4mm.jsonl` median row.
Pre-BB capture at N=5 had `grid_build_total_ms` ≈ 42.7 s and
`grid_build_channel_voxelise_ms` ≈ 37.7 s as the 88 %-of-build hot
spot.)

## How to regenerate (schema v1)

Default voxel + STL benchmark:

```bash
dotnet run --project Voxelforge.Benchmarks -c Release -- \
  --voxel 0.4 --repeat 10 \
  --out Voxelforge.Benchmarks/baselines/baseline-0.4mm.jsonl \
  2>&1 | tee Voxelforge.Benchmarks/baselines/baseline-0.4mm.stdout.log
```

Aerospike standalone STL pipeline (loop 5× — `--aerospike` has no
`--repeat`; one process per run because `Library` is per-process):

```bash
for i in 1 2 3 4 5; do
  dotnet run --project Voxelforge.Benchmarks -c Release -- \
    --aerospike --propellant LOX_CH4 --thrust 20000 --pc 7e6 --eps 15 \
    --plug 0.30 --voxel 0.4 --channels --pattern-elements 48 \
    --out "$TMP/aerospike-${i}.stl"
done | tee Voxelforge.Benchmarks/baselines/aerospike-0.4mm.stdout.log
```

Pre-Sprint-30 physics fingerprint (one preset; loop over the 5
canonical designs for the full corpus):

```bash
dotnet run --project Voxelforge.Benchmarks -c Release -- \
  --bench-sa --design-preset merlin --seed 42 --iterations 2000 --repeat 3 \
  --out Voxelforge.Benchmarks/baselines/bench-sa-merlin-2026-04-24.jsonl
```

xUnit perf snapshot:

```bash
dotnet test Voxelforge.Tests/Voxelforge.Tests.csproj -c Release \
  --no-build --filter "FullyQualifiedName~Phase4PerfBenchmarks" \
  --logger "console;verbosity=detailed" \
  > Voxelforge.Benchmarks/baselines/phase4-perf-xunit-2026-04-24.txt 2>&1
```

List all subcommands the harness knows about:

```bash
dotnet run --project Voxelforge.Benchmarks -c Release -- --list-benches
```

## Tiled-build baselines

Add `--tiles <N>` to the default `--voxel` benchmark to compare
monolithic vs axial-tiled memory + wall clock on the same inputs.
Example for a 4-tile split:

```bash
dotnet run --project Voxelforge.Benchmarks -c Release -- \
  --voxel 0.4 --tiles 4 --out-stl "$TMP/chamber-tiled-4.stl"
```

The harness emits `BENCH tiled_*` lines (per-tile build ms, weld ms,
input/output/dropped triangle counts, output STL bytes) plus a
per-tile breakdown showing which flanges / manifolds / ports landed
in which tile.

## Recipes — `--bench-cfd-export`

```bash
dotnet run -c Release --project Voxelforge.Benchmarks \
  -- --bench-cfd-export --iterations 50 --grid-nx 96 \
     --out Voxelforge.Benchmarks/baselines/bench-cfd-export.jsonl
```

Runs `CfdFieldExport.Write` 50 times against the canonical bell chamber
+ 80-station thermal solve at a 96³ structured grid. Median wall-clock
(~15 ms on a 5950X-class workstation) defends Sprint 14 P3's buffered
`FileStream` + span bulk-write claim. Output JSONL carries the schema-v1
6-field provenance prefix per ADR-013; a separate `.stdout.log` captures
the BENCH summary block.

Restored 2026-04-29 under BB-3 (PR #206) — replaces the original 2026-04-23
phantom that was captured by an abandoned branch and never landed.

## Superseded recipes

- ~~`--bench-sa --iterations 200` (SA-binder microbench)~~ — superseded.
  The new `--bench-sa` is the BB-2 SA physics fingerprint capture
  documented above; the original SA-binder Pack/Unpack microbench moved
  to the BB-3 `Voxelforge.MicroBenchmarks` project (see
  `PackUnpackBench.cs`).
