# Design-variable registry

The simulated-annealing optimiser walks a **34-dimension** search space.
Every dimension lives on a C# property tagged with
`[SaDesignVariable(index, min, max, gate)]`. The attribute is the single
source of truth for:

- the position of the variable in the SA vector,
- its min / max sampling bounds,
- the conditional-application gate that decides whether an unpacked
  value actually gets written back to the baseline (see [ADR-012](ADR/ADR-012-adding-an-sa-design-variable.md)).

No parallel array, no hand-maintained list. `RegenChamberOptimization.Bounds`
is a one-line delegation to `DesignVariableRegistry.BoundsForMany(...)`.
To add a new dimension, add one attribute — see ADR-012.

This file is the user-facing summary of what the SA vector looks like.
It is *generated from the same attributes the optimiser reads*, so if
this document drifts from the code, the code wins — and an index / bounds
drift-guard test fails CI before any drift can merge.

## Three gate classes

| Gate | When the dim is applied | Examples |
|---|---|---|
| `SaGate.None` | Always applied in Unpack | Plain geometric dims (contraction ratio, channel dimensions, …) |
| `SaGate.InjectorPatternPresent` | Only when `baseline.InjectorElementPattern != null` | 5 injector dims — inert if the user hasn't configured an injector pattern |
| `SaGate.TpmsTopology` | Only when `baseline.ChannelTopology` ∈ {TpmsGyroid, TpmsSchwarzP, TpmsSchwarzD} | TPMS cell-edge, solid fraction |
| `SaGate.AerospikeTopology` | Only when `baseline.ChannelTopology = Aerospike` | Plug length ratio, aerospike contraction ratio |

Gated dims are still *packed* (so the vector length is stable across
topologies) but *not unpacked* when the baseline's categorical state
doesn't match — preventing a TPMS cell-edge value from silently
overwriting an axial baseline's field and re-emerging as a ghost
perturbation the next time the user flipped topology.

## The 34 dimensions

### Chamber & nozzle geometry — dims 0–5

Declared on [`RegenChamberDesign`](../Optimization/RegenChamberDesign.cs).

| # | Property | Range | Units | Gate | Notes |
|---|---|---|---|---|---|
| 0 | `ContractionRatio` | 3.0 – 10.0 | — | None | A_chamber / A_throat for the bell-chamber regen baseline. |
| 1 | `ExpansionRatio` | 3.0 – 25.0 | — | None | A_exit / A_throat. Upper bound covers vacuum nozzles. |
| 2 | `CharacteristicLength_m` | 0.7 – 1.6 | m | None | L\*, chamber volume / throat area. |
| 3 | `BellEntranceAngle_deg` | 20.0 – 38.0 | deg | None | Converging-section wall angle at the chamber / contour boundary. |
| 4 | `BellExitAngle_deg` | 6.0 – 16.0 | deg | None | Diverging-section exit angle. |
| 5 | `BellLengthFraction` | 0.6 – 0.9 | — | None | Bell length as a fraction of the 15° conical-equivalent. |

### Regen cooling channels — dims 6–12

Declared on [`RegenChamberDesign`](../Optimization/RegenChamberDesign.cs).

| # | Property | Range | Units | Gate | Notes |
|---|---|---|---|---|---|
| 6 | `ChannelCount` | 40 – 180 | count | None | Number of axial or helical channels; ignored for TPMS. |
| 7 | `ChannelHeightChamber_mm` | 1.0 – 5.0 | mm | None | Channel radial height at the chamber barrel. |
| 8 | `ChannelHeightThroat_mm` | 0.8 – 3.0 | mm | None | Channel radial height at the throat. Narrower here raises coolant velocity → higher h_c. |
| 9 | `ChannelHeightExit_mm` | 1.0 – 5.0 | mm | None | Channel radial height at the nozzle exit. |
| 10 | `RibThickness_mm` | 0.5 – 2.0 | mm | None | Rib separating adjacent channels. |
| 11 | `GasSideWallThickness_mm` | 0.5 – 5.0 | mm | None | Uniform-fallback gas-side liner thickness. Per-station overrides at dims 28-30. |
| 12 | `OuterJacketThickness_mm` | 1.0 – 6.0 | mm | None | Pressure-containing outer shell. F-1 class envelope at upper bound. |

### Injector pattern — dims 13–17

Declared on [`InjectorPattern`](../Injector/InjectorPattern.cs). Gated
on `SaGate.InjectorPatternPresent` — inert when no pattern is configured.

| # | Property | Range | Units | Gate | Notes |
|---|---|---|---|---|---|
| 13 | `ElementCount` | 8 – 48 | count | InjectorPatternPresent | Total injector elements. |
| 14 | `DeltaPInjFraction` | 0.13 – 0.30 | — | InjectorPatternPresent | ΔP_inj / Pc. Huzel & Huang §8.3 feasible band is [0.15, 0.25]; outside that the chug rating degrades. |
| 15 | `OuterRowFilmFraction` | 0.00 – 0.15 | — | InjectorPatternPresent | Fraction of total fuel drawn from outermost ring to the film-cooling slot. |
| 16 | `CdOx` | 0.40 – 0.95 | — | InjectorPatternPresent | Discharge coefficient, oxidiser orifice. |
| 17 | `CdFuel` | 0.40 – 0.95 | — | InjectorPatternPresent | Discharge coefficient, fuel orifice. |

### TPMS coolant cells — dims 18–19

Declared on [`RegenChamberDesign`](../Optimization/RegenChamberDesign.cs).
Gated on `SaGate.TpmsTopology`.

| # | Property | Range | Units | Gate | Notes |
|---|---|---|---|---|---|
| 18 | `TpmsCellEdge_mm` | 2.0 – 6.0 | mm | TpmsTopology | Unit-cell edge length for the TPMS lattice. Smaller → more surface area → more heat uptake + more ΔP. |
| 19 | `TpmsSolidFraction` | 0.35 – 0.65 | — | TpmsTopology | 1 − porosity. Higher fraction = thicker struts = cheaper ΔP but worse heat transfer. |

### Feed-system & preburner — dims 20–21

Declared on [`RegenChamberDesign`](../Optimization/RegenChamberDesign.cs).

| # | Property | Range | Units | Gate | Notes |
|---|---|---|---|---|---|
| 20 | `PreburnerMrRatio` | 0.30 – 1.00 | — | None (silently inert on non-preburner cycles) | Fuel-rich preburner MR. 0 = inherit from `OperatingConditions.PreburnerMrRatio`. |
| 21 | `FlangeRadialProjection_mm` | 8.0 – 24.0 | mm | None (inert on non-monolithic builds) | Pump-mount flange projection on monolithic engine composition. |

### Aerospike — dims 22–23

Declared on [`RegenChamberDesign`](../Optimization/RegenChamberDesign.cs).
Gated on `SaGate.AerospikeTopology`.

| # | Property | Range | Units | Gate | Notes |
|---|---|---|---|---|---|
| 22 | `PlugLengthRatio` | 0.15 – 1.00 | — | AerospikeTopology | Plug truncation. 1.0 = full spike; 0.20–0.40 typical (≈1 % C_F cost at vacuum for ≈60 % length reduction + mountable base). |
| 23 | `AerospikeContractionRatio` | 3.0 – 10.0 | — | AerospikeTopology | A_chamber / A_throat for the aerospike pre-throat chamber. Independent of dim 0 so SA can tune each separately on mixed-topology Pareto fronts. |

### Film cooling — dims 24–25

Declared on [`RegenChamberDesign`](../Optimization/RegenChamberDesign.cs).
Override-style (default 0 = use `FilmCoolingInputs.FuelFractionAsFilm` / `FilmCoolingInputs.FilmSlotHeight_mm` baseline).

| # | Property | Range | Units | Gate | Notes |
|---|---|---|---|---|---|
| 24 | `FilmFuelFraction` | 0.02 – 0.15 | — | None | Fuel fraction routed to face-injected film cooling. Below 2 % the film provides little wall-T attenuation; above 15 % the Isp penalty dominates. |
| 25 | `FilmSlotHeightOverride_mm` | 0.5 – 15.0 | mm | None | Film slot radial height. Higher → thicker film → more wall coverage at higher Isp cost. |

### Pintle injector — dims 26–27

Declared on [`RegenChamberDesign`](../Optimization/RegenChamberDesign.cs).
Override-style; ignored on non-pintle injector patterns.

| # | Property | Range | Units | Gate | Notes |
|---|---|---|---|---|---|
| 26 | `PintleDiameterOverride_mm` | 6.0 – 30.0 | mm | None | Pintle post diameter. Larger pintle → wider blockage band → different stable-combustion regime. |
| 27 | `PintleSleeveHoleCountOverride` | 8 – 32 | count | None | Number of fuel sleeve holes. Heister 2017: 12 (LMDE family) through 24-32 (SuperDraco-class). |

### Per-station gas-side wall thickness — dims 28–30 (Track B, 2026-04-27)

Declared on [`RegenChamberDesign`](../Optimization/RegenChamberDesign.cs).
Override-style: each defaults to 0 (uniform fallback to dim 11 `GasSideWallThickness_mm`). When set, the structural check + proof-test analysis interpolate a per-station thickness profile linearly between the three anchors. Primary use case: RL10-class large-ε designs where the exit hoop dominates feasibility — thicken the exit without paying the chamber/throat thermal penalty.

| # | Property | Range | Units | Gate | Notes |
|---|---|---|---|---|---|
| 28 | `ChamberWallThicknessOverride_mm` | 0.5 – 8.0 | mm | None | Chamber-section wall thickness. 0 = use `GasSideWallThickness_mm`. |
| 29 | `ThroatWallThicknessOverride_mm` | 0.5 – 8.0 | mm | None | Throat-section wall thickness. |
| 30 | `ExitWallThicknessOverride_mm` | 0.5 – 8.0 | mm | None | Exit-section wall thickness. Primary lever for ε > 50 designs. |

### Acoustic dampers — dims 31–33 (OOB-6 / Sprint B-3, 2026-04-30)

Declared on [`RegenChamberDesign`](../Optimization/RegenChamberDesign.cs).
Override-style: co-tuned against the chamber's stability response when
`DamperType` is set (Helmholtz / quarter-wave); inert noise when
`DamperType = None` (the default). See `AcousticDamper.Evaluate` /
`StabilityScreening`. The lower-leverage damper knobs (`HelmholtzNeckLength_mm`,
`QuarterWaveDiameter_mm`) are deliberately kept off the SA vector.

| # | Property | Range | Units | Gate | Notes |
|---|---|---|---|---|---|
| 31 | `HelmholtzNeckArea_mm2` | 4.0 – 120.0 | mm² | None | Helmholtz neck area; drives the Helmholtz resonance band when `DamperType = Helmholtz`. |
| 32 | `HelmholtzCavityVolume_mm3` | 200.0 – 8000.0 | mm³ | None | Helmholtz cavity volume; pairs with dim 31 to set the resonance frequency. |
| 33 | `QuarterWaveLength_mm` | 6.0 – 80.0 | mm | None | Quarter-wave cavity length. Direct f₀ = c/(4·L) inverse, so the SA vector tunes resonance directly. |

## What the SA vector does not cover

Not everything that could be an optimiser knob *should* be. These
parameters live on `RegenChamberDesign` / `OperatingConditions` but are
intentionally kept out of the SA vector:

- **Categorical choices** (propellant pair, engine cycle, wall material,
  port / flange / igniter / umbilical standards, channel topology).
  These partition the search space rather than parameterise it; the
  user picks the region, SA walks within it.
- **Tolerance analysis knobs** (sample count, per-dimension tolerances).
  These affect the Monte-Carlo fidelity, not the design under test.
- **Solver knobs** (axial-conduction sweeps, radial wall nodes,
  contour station count). Numerical, not physical.
- **Ablative / chilldown / start-transient opt-ins and their knobs**.
  These are analyses layered on top of the steady-state design; the
  user opts in, they don't compete as a design dimension.
- **Preburner channel geometry** (count, width, depth, wall thickness).
  Could be promoted to SA in a future sprint if an end-to-end preburner-
  cooling optimisation study needs it; today they're user-set.

## SA Solve latency budgets (per ResourceMode)

Wall-clock budgets for `MultiChainOptimizer.Run` on representative
designs, by `ResourceMode` preset. These budgets were measured so
future Performance P21 (parallel per-station wall-T cooling solve) has
a decision criterion — "is the solver too slow?" finally has a
numerical answer.

Method: measured locally with `--bench-sa --design-preset merlin
--iterations 300 --no-infeasible-exit` on the reference workstation
(96 GB DDR5, Ryzen 9, RTX 5070). Budgets carry a **3 ×** headroom so
slow CI hardware + cold-start JIT don't false-fail the property test
in `Voxelforge.Tests/Optimization/SaLatencyBudgetTests.cs`. Headroom
deliberately wide because the bench-regression workflow already pins
the *physics* fingerprint; this layer pins the *wall-clock* envelope.

| ResourceMode | Chains | Measured (300 iters) | Budget |
|---|---|---|---|
| Quiet     | 1 chain (single-thread surrogate)   | ~660 ms median | ≤ 2 000 ms |
| Balanced  | 4 chains                            | ~800 ms median | ≤ 2 500 ms |
| Maximum   | `Environment.ProcessorCount` chains | concurrency-bound; sub-budget | ≤ 3 000 ms |

The Balanced budget is intentionally above Quiet because synthetic
benchmarks at 300 iters don't recoup the multi-chain coordination
overhead — that flips once SA iterations climb to 1 000 +, where the
parallelism pays off. Earlier placeholder values
(300 / 500 / 800 ms) were optimistic; the actual ceiling under load
is closer to the table above.

**Promote P21 to active sprint when:** any representative design
breaches its budget by > 20 % under realistic conditions for two
consecutive bench-regression runs. Until then, the per-station wall-T
solve stays sequential and this budget table is the falsifier.

## How to add a new dimension

See [ADR-012](ADR/ADR-012-adding-an-sa-design-variable.md). In one line:

```csharp
[SaDesignVariable(index: 31, min: 0.0, max: 1.0, gate: SaGate.None)]
public double MyNewDimension { get; init; } = 0.5;
```

Then bump the vector-length assertions, add a round-trip test, done.
The registry picks up the attribute, `BoundsForMany` surfaces the new
bounds, `DesignVariableBinder` handles Pack / Unpack by reflection.
