# Shared-abstractions ledger

> Central registry of pillar implementations behind the cross-cutting Core
> abstractions (`IEngine<,,>`, `IObjective`, gate evaluator). Update this
> file whenever a pillar adds or removes a row in any of the tables below.
> The ledger is the source of truth for "which pillars are wired into which
> shared surface today" — code rows in this table must point to a real
> file path; if a row is aspirational, mark it explicitly.

## §1 — `IEngine<TDesign, TConditions, TResult>` implementations

The ternary engine-family abstraction (ADR-025) is the optimizer's narrow
waist. Every pillar that wants to be reachable through the family-agnostic
optimizer / CLI / UI dispatch surfaces an `IEngine` adapter here.

| Family               | Implementation                                                                                                                                              | Design type                  | Conditions type           | Result type                  | Phase                                                                                              |
|----------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------|---------------------------|------------------------------|----------------------------------------------------------------------------------------------------|
| Rocket               | [`RocketEngine`](../../Voxelforge.Core/Engines/RocketEngine.cs) (singleton via `Instance`)                                                                  | `RegenChamberDesign`         | `OperatingConditions`     | `RocketEngineResult`         | Phase 1 (ADR-025)                                                                                  |
| Airbreathing         | [`AirbreathingEngine`](../../Voxelforge.Airbreathing.Core/Engines/AirbreathingEngine.cs) (singleton)                                                        | `AirbreathingEngineDesign`   | `FlightConditions`        | `AirbreathingResult`         | Phase 1 (ADR-025)                                                                                  |
| ElectricPropulsion   | [`ElectricPropulsionEngine`](../../Voxelforge.ElectricPropulsion.Core/Engines/ElectricPropulsionEngine.cs) (singleton)                                      | `ElectricPropulsionEngineDesign` | `ResistojetConditions` | `ElectricPropulsionResult`   | Wave-1 (this PR)                                                                                   |
| Nuclear              | [`NuclearEngine`](../../Voxelforge.Nuclear.Core/Engines/NuclearEngine.cs) (singleton)                                                                       | `NuclearThermalDesign`           | `NuclearThermalConditions` | `NtrGenerationResult`    | Wave-1 (PR #465)                                                                                   |

A pillar that has not yet shipped its IEngine adapter should NOT have a row
here. Aspirational rows live in the pillar spec's "Deferred" section instead.

## §2 — `IObjective` implementations

The optimizer (multi-chain SA / CMA-ES / NSGA-II / Bayesian) sees only the
`IObjective` contract: `DimensionCount`, `Variables` (bounds metadata),
`Evaluate(vector, ct) -> EvaluationResult`. Each pillar variant ships one
`IObjective` (or constructs one via `EngineObjectiveAdapter` over its `IEngine`).

| Variant                   | Implementation                                                                                                                            | Engine wrapped               | Vector dim | Notes                                                                            |
|---------------------------|-------------------------------------------------------------------------------------------------------------------------------------------|------------------------------|------------|----------------------------------------------------------------------------------|
| Rocket-regen              | [`RegenObjective`](../../Voxelforge/Optimization/RegenObjective.cs)                                                                       | (legacy direct)              | 34         | Pre-IEngine; will migrate to `EngineObjectiveAdapter<RegenChamberDesign,…>` in Phase 2. |
| Airbreathing — ramjet     | [`RamjetObjective`](../../Voxelforge.Airbreathing.Core/Optimization/RamjetObjective.cs)                                                   | (direct via `AirbreathingOptimization.GenerateWith`) | 6 | Will migrate to `EngineObjectiveAdapter<AirbreathingEngineDesign,FlightConditions,AirbreathingResult>` in Phase 2. |
| Airbreathing — turbojet   | [`TurbojetObjective`](../../Voxelforge.Airbreathing.Core/Optimization/TurbojetObjective.cs)                                               | (direct)                     | 7          | "                                                                                |
| Airbreathing — turbofan   | [`TurbofanObjective`](../../Voxelforge.Airbreathing.Core/Optimization/TurbofanObjective.cs)                                               | (direct)                     | 8          | "                                                                                |
| Airbreathing — scramjet   | [`ScramjetObjective`](../../Voxelforge.Airbreathing.Core/Optimization/ScramjetObjective.cs)                                               | (direct)                     | 7          | "                                                                                |
| Airbreathing — RBCC       | [`RbccObjective`](../../Voxelforge.Airbreathing.Core/Optimization/RbccObjective.cs)                                                       | (direct)                     | 8          | "                                                                                |
| Electric — resistojet     | [`ResistojetObjective`](../../Voxelforge.ElectricPropulsion.Core/Optimization/ResistojetObjective.cs)                                     | `ElectricPropulsionEngine`   | 6          | First IObjective wired through `EngineObjectiveAdapter<…>` from day one (Wave-1, Sprint E.4). `ResistojetObjective.Build(conditions, baseline)` returns the adapter; `Pack` / `Unpack` round-trip the 6-dim vector against a baseline design preserving categorical state (HeaterMaterial, ChamberEmissivity, ChamberWallThickness_mm, RadiativelyCooledNozzle). |
| Electric — HET            | [`HetObjective`](../../Voxelforge.ElectricPropulsion.Core/Optimization/HetObjective.cs)                                                  | `ElectricPropulsionEngine`   | 6          | Wave-2 (Sprint EP.W2.HET, ADR-029). Same `EngineObjectiveAdapter<…>` wiring as `ResistojetObjective`. Vector slots: DischargeVoltage_V, DischargeCurrent_A, MagneticField_T, AnodeRadius_mm, ChannelLength_mm, XenonMassFlow_kgs. Bind-time clip on dim 1 (I_d) so V_d × I_d ≤ BusPower_W_avail. Categorical state preserved: Kind, AnodeMaterial, CathodeType. |
| Nuclear — NERVA-class NTR | [`NtrObjective`](../../Voxelforge.Nuclear.Core/Optimization/NtrObjective.cs)                                                              | `NuclearEngine`              | 6          | `NtrObjective.Build(conditions, baseline)` returns `EngineObjectiveAdapter<NuclearThermalDesign,…>`; `NervaBounds` calibrated to NRX-A6 regime; score = −Isp_vacuum. Wave-1 (PR #465). |

When a new variant ships, add a row here. The vector dim must match the
`Variables` array length in the implementation; mismatches fail the
ScaffoldingSmokeTests by construction.

## §3 — Feasibility-gate evaluators per pillar

Two parallel patterns coexist:

- **Registry-driven** (`GateRegistry.EnsureInitialized` → per-pillar `RegisterAll`):
  rocket today. Each gate is a `FeasibilityGateDescriptor` record with
  applicability mask + `Action<RegenGenerationResult, List<…>>` predicate. The
  registry's predicate signature is rocket-shaped; this is risk #2 in
  ADR-026 §9, deferred for unification under a future ADR-027.
- **Parallel evaluator** (per-pillar static `Evaluate(design, conditions, result)`):
  airbreathing today; electric propulsion in Wave-1. Bypasses the rocket-
  shaped registry signature; called directly from the pillar's
  `Engine.Evaluate` method.

| Pillar                | Pattern               | Entry point                                                                                                                  | Gate count                                                                  |
|-----------------------|-----------------------|------------------------------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------|
| Rocket-regen          | Registry              | [`RocketGates.RegisterAll`](../../Voxelforge.Core/Optimization/RocketGates.cs) (called from `GateRegistry.EnsureInitialized`) | 55 ConstraintIds (snapshot-pinned in `GateOrderingSnapshotTests`)           |
| Rocket-aerospike      | Parallel evaluator    | [`AerospikeFeasibility.Evaluate`](../../Voxelforge.Core/Geometry/AerospikeFeasibility.cs)                                    | 5 ConstraintIds                                                             |
| Airbreathing          | Parallel evaluator    | [`AirbreathingFeasibility.Evaluate`](../../Voxelforge.Airbreathing.Core/AirbreathingFeasibility.cs)                          | 40 ConstraintIds across 12 engine kinds                                     |
| ElectricPropulsion    | Parallel evaluator    | [`ElectricPropulsionFeasibility.Evaluate`](../../Voxelforge.ElectricPropulsion.Core/ElectricPropulsionFeasibility.cs)        | 54 ConstraintIds across 9 kinds (kind-predicated per ADR-029 D6)            |
| Nuclear               | Parallel evaluator    | [`NuclearGates.Evaluate`](../../Voxelforge.Nuclear.Core/Optimization/NuclearGates.cs)                                        | 15 ConstraintIds (NERVA-class solid-core NTR + bimodal NTR-Brayton)         |

The registry-vs-parallel split is intentional and will not be unified in
Wave-1. Unification waits on ADR-027 (post-Wave-1) once we have three concrete
parallel evaluators (rocket-aerospike, airbreathing, electric) to design
against.

## §4 — Schema-version constants

See [`family-allocations.md` §3](family-allocations.md) for the per-pillar
schema-version constant index. The ledger does not duplicate it; check there
when adding a pillar to keep the rows in sync.

## §4b — Verification-track consumed types

Verification tracks (see ADR-026 §2) are **not** engine pillars — they have no `IEngine`
adapter, no SA dims, and no schema version. They consume pillar outputs and emit
calibration reports. The frozen read-only types they consume are listed here so a
future refactor of `Voxelforge.Core` knows what's load-bearing for Team C.

| Consumed type | Owned by | Read-only for | Purpose |
|---|---|---|---|
| `CfdFieldExport` (`Write`, `WriteAerospike`) | `Voxelforge.Core/IO/CfdFieldExport.cs` | `Voxelforge.Cfd.Core` | Produces VTI initial-conditions file; Team C reads the VTI only for warm-start IC |
| `ChamberContour` | `Voxelforge.Core/Chamber/ChamberContour.cs` | `Voxelforge.Cfd.Core` | Source of r(x) profile for Su2MeshWriter |
| `RegenSolverOutputs` | `Voxelforge.Core/HeatTransfer/` | `Voxelforge.Cfd.Core` | Per-station Bartz HTC for q_wall → T_w conversion |
| `CalibrationPosterior` | `Voxelforge.Core/Analysis/CalibrationPosterior.cs` | `Voxelforge.Cfd.Core` | Existing 5-knob MAP; BartzScalingFactor is knob #3 (`hasThermal` axis) |
| `MeasuredSummary` | `Voxelforge.Core/Analysis/MeasuredDataOverlay.cs` | `Voxelforge.Cfd.Core` | Input struct to `CalibrationPosterior.Calibrate()`; SU2 T_w feeds `PeakWallT_K` |

**Rule:** Team C must not modify any type in this table. If a new field is needed on
`CfdFieldExport` or `ChamberContour`, raise a review request with Team P (rocket pillar).

## §5 — Known duplication (rule-of-three tracking)

The following abstractions exist in three or more parallel-pillar copies. The
rule-of-three trigger is met; unification is deferred until the platform
consolidation sprint (WinForms → Avalonia, [#289](https://github.com/poetac/voxelforge/issues/289)) or
an earlier driver surfaces.

| Duplicated abstraction | Copies | Trigger met | Deferred until |
|---|---|---|---|
| Axisymmetric revolved-contour SDF (binary-search + linear-lerp `IImplicit`) | `MarineProfileImplicit` (Marine.Voxels), `RevolvedContourImplicit` (Airbreathing.Voxels), `RevolvedContourImplicit` (ElectricPropulsion.Voxels), `RevolvedContourImplicit` (Nuclear.Voxels) | **Yes** (4 concrete impls) | Platform consolidation sprint or next pillar addition |
| `SubprocessRunner` + `SubprocessResult` (test helper) | Airbreathing.Tests, Marine.Tests | **No** (2 copies — trigger at 3) | Rule-of-three |
| `IPlasmaState` (plasma-state abstraction, ADR-029 + ADR-029a) | `Voxelforge.Core/Plasma/` (HET + Arcjet + PPT) | **Yes** (3 of 3 plasma engines — HET + Arcjet + PPT shipped) | **Promoted to `Voxelforge.Core/Plasma/` (Sprint EP.W2.PPT, ADR-029a)** — rule-of-three met; cross-pillar consumers can reference without an EP-pillar dependency. |

## §6 — Cross-references

- [ADR-025 — `IEngine<,,>` engine-family abstraction](ADR/ADR-025-iengine-engine-family-abstraction.md)
- [ADR-026 — multi-pillar coordination](ADR/ADR-026-multi-pillar-coordination.md)
- [Family allocations (bit-mask + schema)](family-allocations.md)
- Pillar specs under [`pillar-specs/`](pillar-specs/) (one per pillar — drives the rows in §1–§3 above)
- §5 duplication table updated with Nuclear.Voxels Wave-1 (axisymmetric SDF fourth copy noted)
