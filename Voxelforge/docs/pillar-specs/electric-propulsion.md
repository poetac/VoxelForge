# Pillar spec — Electric Propulsion

> Source of truth for the `Voxelforge.ElectricPropulsion.*` subtree.
> Fold any change to bounds / gates / fixture / physics-model citations
> back here in the same PR that lands the code change.

**Status (Wave-1, this PR):** resistojet only. HET / MPD / gridded ion /
arcjet are deferred to Wave-2 with a Team-P plasma-abstraction audit
gating their start (per
[ADR-026 §6](../ADR/ADR-026-multi-pillar-coordination.md)).

**Schema version:** v1 (initial).
**Family bits owned:** 3 (`ElectricPropulsion`) + 7 (`ElectricResistojet`)
per [`family-allocations.md`](../family-allocations.md).

---

## §1 — Overview

Electric propulsion encompasses any thrust-producer where electrical bus
power, not chemical combustion, supplies the energy added to the propellant.
The pillar's Wave-1 entry is the **resistojet** — the simplest electric
propulsion variant, with no plasma physics:

- Catalyst-decomposed propellant (typically hydrazine) flows through a
  resistively-heated chamber containing a refractory-metal heater coil.
- The coil radiates and convects heat into the gas; gas temperature rises
  from the catalyst-bed exit (~900 K) to the chamber design temperature
  (1200–1500 K typical).
- Heated gas accelerates through a converging-diverging conical nozzle to
  the vacuum of space.

Performance regime: thrust 0.05–1 N, specific impulse 280–320 s, electrical
power 200 W to 3 kW. Used historically for spacecraft station-keeping
(Iridium constellation, EOS-AM1, Aerojet MR-501-series flight heritage).

**Wave-1 deliberately scopes to electrothermal-only.** The next variants
(arcjet — distributed plasma; HET — Hall-effect crossed-field plasma; MPD —
magnetoplasmadynamic Lorentz force; gridded ion — electrostatic acceleration)
all need plasma-state physics that the resistojet model does not. Bringing
those online without a plasma-abstraction audit risks calcifying around
resistojet-shaped concerns (the trap the
"rule of three" prevents). Wave-2 starts after the audit.

---

## §2 — Six SA design variables

Bounds derived from the flown-resistojet cluster (MR-501A/B, MR-503,
NASA-TP-2382 NASA Lewis ammonia resistojet) plus headroom for unflown
high-power designs. Tighter than physically plausible at the lower
end — sub-200 W resistojets ("micro-resistojet" regime, e.g. Surrey/QinetiQ
STRV) trade thrust for radiation-loss-dominated heat balance and aren't a
useful optimization target.

| # | Name                       | Bounds            | Unit        | Source / rationale                                                     |
|---|----------------------------|-------------------|-------------|------------------------------------------------------------------------|
| 1 | `HeaterPower_W`            | 200 – 3000        | W           | MR-501A/B/MR-503 cluster (500–900 W) + headroom; >3000 W crosses into low-power-arcjet territory (MR-509 ATOS regime), out of scope for Wave-1. |
| 2 | `PropellantMassFlow_kgs`   | 1 × 10⁻⁵ – 5 × 10⁻⁴ | kg/s      | F = ṁ · Isp · g₀ — upper bound 5 × 10⁻⁴ caps thrust at ~1.5 N (resistojet envelope ceiling); lower bound 1 × 10⁻⁵ matches sub-mg/s micro-class. |
| 3 | `NozzleThroatRadius_mm`    | 0.1 – 2.0         | mm          | MR-501B throat ≈ 0.2 mm; sub-mm typical for 0.05–1 N thrust class. Upper 2 mm covers high-mass-flow upper-resistojet envelope. |
| 4 | `NozzleAreaRatio`          | 25 – 150          | (dimensionless) | Flown hardware band (MR-501B ε ≈ 100, MR-503 ε ≈ 60). Wider band trips advisory gate (§6 #6); tightened deliberately to keep optimizer in physically-relevant region. |
| 5 | `HeaterChamberLength_mm`   | 5 – 50            | mm          | L/D ratio 1–6 per Sutton/Biblarz "Rocket Propulsion Elements" 9e §16; combined with bound #6 this respects the L/D advisory band. |
| 6 | `HeaterChamberRadius_mm`   | 2 – 15            | mm          | Heater-coil containment + structural margin; flown hardware in 4–10 mm range. |

**Bind-time clip (not a gate):** `HeaterPower_W` upper bound is dynamically
clipped to `min(3000, conditions.BusPower_W_avail)` inside
`ResistojetObjective.Variables` so the optimizer never wastes evaluations
on candidates that exceed the spacecraft's available bus power. This is a
binding constraint, not a feasibility gate.

Citations:

- Aerojet Rocketdyne MR-501-series datasheet (public marketing
  material).
- NASA Technical Memorandum 2002-211314, "Electrothermal Resistojet
  Propulsion: A Survey," NASA-Lewis 2002.
- NASA Technical Paper 2382, "Performance of an Ammonia Resistojet,"
  NASA Lewis 1985.
- Sutton GP, Biblarz O., "Rocket Propulsion Elements," 9th ed., Wiley
  2017, §16 (electrothermal propulsion).

---

## §3 — `ResistojetConditions` record

Wave-1 sub-step uses a single conditions record because resistojet is the
only variant. When Wave-2 lands (HET / MPD / ion), this generalises to
`ElectricPropulsionConditions` with kind-discriminated fields (similar
to `AirbreathingEngineDesign` carrying turbine / steam knobs that only
matter for specific kinds).

```csharp
namespace Voxelforge.ElectricPropulsion;

public sealed record ResistojetConditions(
    double BusVoltage_V,           // Spacecraft bus voltage (typically 28 or 100 V).
    double BusPower_W_avail,       // Maximum continuous bus power available.
    double AmbientPressure_Pa,     // 0.0 in vacuum; non-zero only for in-atmosphere ground test.
    Propellant Propellant,         // NH3 | N2H4Decomposed | H2 | H2O.
    double InletTemperature_K,     // Pre-heater gas temperature; ~900 K post-catalyst for hydrazine.
    PropellantInletComposition InletComposition  // Mole fractions for catalyst products.
) : IEngineConditions
{
    public string Family => EngineFamilies.ElectricPropulsion;
}
```

**Note on `InletComposition`:** the upstream catalyst chemistry (e.g.
hydrazine decomposition into NH₃ / N₂ / H₂ over Shell-405) is **not**
modeled inside the pillar. The conditions record carries the
post-catalyst gas composition + temperature; the resistojet solver
consumes these and only models the electrothermal superheat. This
matches the standard literature treatment (NASA TM-2002-211314 §3,
Sutton §16).

**Conscious omission:** there is no `StationMap` analog of the
airbreathing pillar. Resistojet flow is single-zone (heater chamber +
nozzle) — station numbering would be over-engineering. Future plasma
variants may need it; Wave-2 audit decides.

---

## §4 — `ElectricPropulsionResult` record

```csharp
namespace Voxelforge.ElectricPropulsion;

public sealed record ElectricPropulsionResult(
    ElectricPropulsionEngineDesign Design,
    ResistojetConditions           Conditions,
    double                          Thrust_N,
    double                          IspVacuum_s,
    double                          ExitVelocity_ms,
    double                          ThrustEfficiency,    // η_T = useful kinetic / electrical input
    double                          HeaterTemp_K,
    double                          ChamberTemp_K,
    double                          ExitMachNumber,
    double                          ExitPressure_Pa,
    double                          RadiationLossFraction,  // q_rad / P_in
    bool                            ChokedFlow,
    IReadOnlyList<FeasibilityViolation> Violations,
    bool                            IsFeasible
) : IEngineResult
{
    public IReadOnlyList<FeasibilityViolation> Advisories { get; init; }
        = Array.Empty<FeasibilityViolation>();
}
```

The result echoes back `Design` + `Conditions` so reporting can correlate.
`Violations` carries hard-gate failures; `Advisories` carries soft
warnings (severity from §6); `IsFeasible` is `Violations.Count == 0`.

---

## §5 — Physics model

Four solvers under `Voxelforge.ElectricPropulsion.Core/Solvers/`. Each
is `[Deterministic]` (VFD001-VFD006 clean) and has its own per-solver
unit test file in `Voxelforge.ElectricPropulsion.Tests/Solvers/`.

### 5.1 — `ElectrothermalHeaterSolver`

Lumped 0-D energy balance over the heater chamber:

```
P_in = ṁ · cp · (T_chamber − T_inlet) + q_rad,heater
q_rad,heater = ε_emit · σ · A_heater_ext · (T_heater⁴ − T_∞⁴)
```

Where `T_heater` is approximated as `T_chamber + ΔT_film` with `ΔT_film`
from a Nusselt-correlation film-coefficient on the gas side
(Dittus-Boelter for low-Re subsonic flow). Newton solve on `T_chamber`.
Lumped 0-D fidelity is correct for resistojet preliminary design per
NASA TM-2002-211314 §3 + Sutton §16.5.

**Inputs:** `P_in` (HeaterPower_W), `ṁ` (PropellantMassFlow_kgs), `cp(T)`
+ `μ(T)` from `RealGasGammaSolver` lookup, geometry (chamber L/R), inlet
state from conditions, propellant emissivity from
`Thermo/PropellantTables.cs`.

**Outputs:** `T_chamber`, `T_heater`, `q_rad_heater`.

**Citations:** Sutton §16.5; NASA TM-2002-211314 §3; Holman "Heat
Transfer" 10e §6 (Dittus-Boelter).

### 5.2 — `IsentropicNozzleSolver`

Choked flow at throat, isentropic expansion to exit:

```
ṁ = (P_chamber · A_throat / √(R · T_chamber)) · √γ · (2/(γ+1))^((γ+1)/(2(γ-1)))
ε = A_exit / A_throat = (1/M_exit) · ((2/(γ+1)) · (1 + ((γ-1)/2) · M_exit²))^((γ+1)/(2(γ-1)))
P_exit/P_chamber = (1 + ((γ-1)/2) · M_exit²)^(-γ/(γ-1))
T_exit/T_chamber = (1 + ((γ-1)/2) · M_exit²)^(-1)
V_exit = M_exit · √(γ · R · T_exit)
F = ṁ · V_exit + (P_exit − P_∞) · A_exit
Isp_vac = F / (ṁ · g₀)  with P_∞ = 0
```

Newton iteration on `M_exit` given ε. Choking validated by checking
`P_chamber / P_∞ > ((γ+1)/2)^(γ/(γ-1))`; non-choked → gate
`RESISTOJET_NOZZLE_UNCHOKED` fires (§6 #3).

**Inputs:** `T_chamber`, `P_chamber` (computed from ṁ + throat area), `ε`,
`γ` from `RealGasGammaSolver`, `P_∞` from conditions.

**Outputs:** `Thrust_N`, `IspVacuum_s`, `ExitVelocity_ms`, `M_exit`,
`P_exit`, `ChokedFlow`.

**Citations:** Sutton §3 (choked nozzle theory); Anderson "Modern
Compressible Flow" 4e §5.

### 5.3 — `RealGasGammaSolver`

Per-propellant `γ(T)`, `cp(T)`, `μ(T)`, `MW`, `ε_emit` lookup. **20
anchors per propellant in log-T space**, linear interpolation between
anchors. Cached as static read-only struct per propellant; lookup is
`O(log n)` binary search.

Anchor temperatures bracket 200 K (catalyst-bed-cold limit) to 3500 K
(H₂ dissociation onset, beyond which our ideal-gas assumption breaks
and gate `RESISTOJET_PROPELLANT_DECOMPOSITION` fires). Anchors taken
from the NIST WebBook fluid-properties tables, sampled at 200, 300,
400, 500, 600, 800, 1000, 1200, 1500, 1800, 2100, 2400, 2700, 3000,
3300 K (and below 200 K for cold-start, plus three above 3000 K for
limit-checking). Linear interpolation in log-T (γ varies smoothly in
log-T even where it varies non-monotonically in T).

**Propellants:**

- `NH3` (ammonia gas).
- `N2H4Decomposed` (hydrazine catalyst products: 2 N₂H₄ → 2 NH₃ + N₂ + H₂
  via Shell-405; products further crack at higher catalyst-bed
  temperature — Wave-1 uses a fixed mole-fraction post-catalyst).
- `H2` (gaseous hydrogen).
- `H2O` (water vapor; resistojet variant for low-Isp / high-density
  station-keeping).

**Citations:** NIST Chemistry WebBook
(https://webbook.nist.gov/chemistry/), per-species pages.

### 5.4 — `RadiationLossSolver`

Stefan-Boltzmann emission from the chamber outer wall (the energy budget
sink that limits heater steady-state):

```
q_rad,chamber_ext = ε_chamber · σ · A_chamber_ext · (T_wall⁴ − T_∞⁴)
```

Where `T_wall` is solved from a chamber-wall heat balance (gas-side
convection in, vacuum-side radiation out), `ε_chamber` is the chamber
material emissivity (refractory metal: 0.30–0.45 typical), and
`T_∞ = 3 K` (cosmic background; vacuum conditions).

**Optional second emission surface:** real flown resistojets use a
radiatively-cooled niobium nozzle that operates at ~1500 K wall
temperature, radiating the diverging-section heat balance to space. When
the design specifies radiative cooling (default for Wave-1), the solver
adds a second `q_rad,nozzle_ext` term tracked separately. Wave-1 ships
with the second surface always-on for vacuum operation and never-on for
non-zero `AmbientPressure_Pa`.

**Citations:** Holman "Heat Transfer" 10e §8 (radiation); Aerojet MR-501B
heater + niobium nozzle hardware photos.

### 5.5 — Conscious omissions

- **No 1-D axial heater model.** Lumped 0-D is the literature standard
  for this thrust class; 1-D is appropriate for arcjet (distributed-arc
  variant — Wave-2). Resistojet temperature gradient along the chamber
  is small relative to the radial gradient.
- **No nozzle thermal-soak solver.** Folded into `RadiationLossSolver`
  as the optional second emission surface.
- **No upstream-catalyst kinetics.** Modeled as pre-decomposed
  `InletComposition` per §3. Catalyst chemistry is the domain of
  rocket-side `Voxelforge.Combustion.Monoprop`; we deliberately do not
  reach into it (cross-pillar import discipline per
  [ADR-026 §7](../ADR/ADR-026-multi-pillar-coordination.md)).

---

## §6 — Feasibility gates

Five hard + five advisory gates. Hard gates fail the candidate (optimizer
treats `Score = +∞`); advisory gates emit warnings on `Result.Advisories`
without gating optimization. All gates evaluate inside
[`ElectricPropulsionFeasibility.Evaluate`](../../../Voxelforge.ElectricPropulsion.Core/ElectricPropulsionFeasibility.cs)
— a parallel evaluator (not registry-driven), per
[ADR-026 §6 risk #2](../ADR/ADR-026-multi-pillar-coordination.md).

### Hard gates (5)

| # | ConstraintId                              | Kind                | Threshold (fires when …)                                                  | Citation                                                                                  |
|---|-------------------------------------------|---------------------|---------------------------------------------------------------------------|-------------------------------------------------------------------------------------------|
| 1 | `RESISTOJET_HEATER_TEMP_EXCEEDED`         | PhysicsLimit        | `T_heater > T_max(material)` — Pt-grain 2500 K, W-Re 2800 K.              | Lyon, "Refractory Metal Properties," NASA-CR-179614 (1986).                               |
| 2 | `RESISTOJET_RADIATION_FRACTION_EXCESSIVE` | PhysicsLimit        | `q_rad / P_in > 0.50` — heater can't reach steady state.                  | NASA TM-2002-211314 §3 (resistojet thermal-balance regime).                               |
| 3 | `RESISTOJET_NOZZLE_UNCHOKED`              | PhysicsLimit        | `P_chamber / P_∞ < ((γ+1)/2)^(γ/(γ-1))` — sub-critical nozzle.            | Sutton §3.3 (choking criterion).                                                          |
| 4 | `RESISTOJET_PROPELLANT_DECOMPOSITION`     | EmpiricalBand       | `T_chamber > T_decomp(propellant)` — NH₃ 1100 K, N₂H₄-products 1400 K, H₂O 2700 K, H₂ 3500 K. | NASA TM-2002-211314 §4 (species stability limits); Sutton §16.5 (NH₃ cracking).         |
| 5 | `RESISTOJET_HEAT_LEAK_EXCEEDS_INPUT`      | PhysicsLimit        | `q_rad + q_cond ≥ P_in` — no net heating; structurally unphysical solve.   | Holman "Heat Transfer" 10e §1.3 (energy balance).                                         |

### Advisory gates (5)

| # | ConstraintId                              | Kind                | Threshold (fires when …)                                                  | Citation                                                                                  |
|---|-------------------------------------------|---------------------|---------------------------------------------------------------------------|-------------------------------------------------------------------------------------------|
| 6 | `RESISTOJET_AREA_RATIO_OUT_OF_BAND`       | AdvisoryHeuristic   | `ε < 25` or `ε > 150` — outside flown-hardware envelope.                  | Aerojet MR-501-series datasheets; NASA-TP-2382 (NASA Lewis ammonia resistojet).           |
| 7 | `RESISTOJET_THRUST_BELOW_MIN`             | AdvisoryHeuristic   | `F < 0.05 N` — below typical mission-floor for station-keeping use.       | Iridium / EOS-AM1 mission specs (public).                                                |
| 8 | `RESISTOJET_ISP_BELOW_FLOOR`              | AdvisoryHeuristic   | `Isp < 200 s` — uncompetitive vs cold-gas thrusters.                      | Sutton §16.1 (electric vs cold-gas comparison).                                          |
| 9 | `RESISTOJET_EFFICIENCY_BELOW_FLOOR`       | AdvisoryHeuristic   | `η_T < 0.65` — typical resistojet efficiency floor.                        | NASA TM-2002-211314 §3 (efficiency-band literature survey).                              |
| 10 | `RESISTOJET_FROZEN_FLOW_LOSS_EXCESSIVE` | EmpiricalBand       | `T_chamber > 2500 K` with N or H species present — recombination suppressed in sub-mm-throat residence time, 5–15 % Isp loss. | NASA TM-2002-211314 §4 (frozen-flow loss anchors). |

**Severity discipline:** the line between Hard and Advisory follows
[ADR-009 (rocket-side gate doctrine)](../ADR/ADR-009-feasibility-gates.md):
exceeding a Hard threshold makes the model output non-meaningful (e.g.
the heater literally melts; the gas literally dissociates into species
the solver can't handle). Advisory thresholds flag a region where the
model is still trustworthy but the design is operationally suboptimal
or outside the flown-hardware envelope.

**`BUS_POWER_EXCEEDS_AVAILABLE` is intentionally NOT a gate.** Spacecraft-
system-level constraints (bus power, total propellant mass, mission Δv)
clip SA bounds at bind-time, not after evaluation. See §2 bind-time clip
note.

---

## §7 — Voxel build (ResistojetVoxelBuilder)

PicoGK voxel build at `Voxelforge.ElectricPropulsion.Voxels/Geometry/ResistojetVoxelBuilder.cs`.

### Geometry

1. **Heater chamber** — cylindrical solid. Length =
   `HeaterChamberLength_mm`, radius = `HeaterChamberRadius_mm`. Wall
   thickness 1.5 mm (default; Wave-1 doesn't expose as SA dim).
2. **Heater coil** — single-loop helix at ~80 % of chamber radius.
   Decorative for Wave-1 (not load-bearing for thermal solve; the lumped
   0-D model treats it as a uniform volumetric source).
3. **Converging nozzle section** — cone-frustum from chamber radius to
   `NozzleThroatRadius_mm`. Half-angle 30° (constant, not SA-dim).
4. **Diverging nozzle section** — **cone-frustum at 15° half-angle** from
   throat to exit, exit radius computed from `NozzleAreaRatio` × throat
   area.

### Why conical, not Rao-parabolic

Real flown resistojets (Aerojet MR-501-series, NASA Lewis ammonia test
articles, Iridium thrusters) are uniformly conical. Three reasons:

1. **Manufacturing precision.** At sub-mm throats, LPBF (or any
   metal-AM) resolution makes Rao-parabolic curves impractical — the
   layer-thickness floor (~30 µm) approaches the nozzle wall thickness.
2. **Mass payoff is zero.** Sutton §3.7 says Rao-parabolic saves mass
   above ε ≈ 25 by reducing nozzle length. But resistojet nozzle mass is
   < 5 % of total thruster mass (heater assembly dominates); the
   optimization payoff is not measurable.
3. **Manufacturing standardization.** A cone half-angle is one number;
   Rao-parabolic requires per-design contour computation. The pillar
   does not need that complexity at Wave-1.

When Wave-2 brings high-Isp arcjet / HET (where ε > 100 and mass
matters), Rao support can land in the voxel builder as a topology
discriminator. Until then, conical only.

### LibraryScope discipline

The voxel build runs on the task thread inside a `using var lib = new
PicoGK.Library(voxel_mm); using var libScope = LibraryScope.Set(lib);`
block (CLAUDE.md PicoGK pitfall #4 — voxel ops must run on the task
thread). The `Voxelforge.ElectricPropulsion.Voxels/LibraryScope.cs` file
re-exports `Voxelforge.Voxels.LibraryScope` (does NOT reimplement) per
[ADR-026 §2](../ADR/ADR-026-multi-pillar-coordination.md).

### STL export

`Voxelforge.ElectricPropulsion.StlExporter/Program.cs` invokes
`ResistojetVoxelBuilder.Build(...)` via subprocess (the same pattern as
`Voxelforge.Airbreathing.StlExporter` for ramjet). Subprocess test in
`Voxelforge.ElectricPropulsion.Tests/Voxels/ResistojetStlExportTests.cs`
asserts non-empty STL file (> 10 KB) for an MR-501B-class design.

---

## §8 — Validation fixture: Aerojet MR-501B

The MR-501B is a flight-heritage hydrazine resistojet used on the Iridium
satellite constellation and Earth Observation System AM-1 (EOS-AM1).
Wave-1 ships exactly one fixture; additional fixtures (MR-501A, MR-503,
NASA Lewis ammonia) are claimable post-Wave-1 as separate one-day
PRs.

### Inputs

| Property                 | Value                          | Source                                   |
|--------------------------|--------------------------------|------------------------------------------|
| `HeaterPower_W`          | 870                            | Aerojet datasheet (public).              |
| `PropellantMassFlow_kgs` | 1.2 × 10⁻⁴ (≈ 120 mg/s)         | Aerojet datasheet.                       |
| `Propellant`             | `N2H4Decomposed`               | Shell-405 catalyst-bed products.         |
| `InletTemperature_K`     | 900                            | Standard catalyst-bed exit temperature.  |
| `NozzleThroatRadius_mm`  | 0.20                           | Aerojet datasheet (approximate).         |
| `NozzleAreaRatio`        | 100                            | Aerojet datasheet.                       |
| `HeaterChamberLength_mm` | 25                             | Hardware photo measurements.             |
| `HeaterChamberRadius_mm` | 6                              | Hardware photo measurements.             |
| `BusVoltage_V`           | 28                             | Standard spacecraft bus.                 |
| `BusPower_W_avail`       | 900                            | MR-501B requires < 900 W steady-state.   |

### Output tolerance bands

| Property              | Target | Tolerance | Source                                                                   |
|-----------------------|--------|-----------|--------------------------------------------------------------------------|
| `Thrust_N`            | 0.36   | ± 10 %    | Aerojet datasheet (steady-state spec).                                  |
| `IspVacuum_s`         | 300    | ± 8 %     | Aerojet datasheet + NASA TM-2002-211314 (validation).                   |
| `ThrustEfficiency`    | 0.70   | ± 15 %    | NASA TM-2002-211314 §3 (resistojet efficiency cluster).                 |

The fixture is `ElectricPropulsionFixture_MR501B.cs` in
`Voxelforge.ElectricPropulsion.Tests/Validation/`. xUnit `[Fact]` per
property; failure within Wave-1 acceptance band must be tightened
before merge.

### Citations

- Aerojet Rocketdyne, "MR-501B Hydrazine Thruster" datasheet. Public
  marketing material, accessed via Wayback Machine.
- NASA TM-2002-211314, "Electrothermal Resistojet Propulsion: A
  Survey," NASA-Lewis 2002. Tables 3.1 and 4.2 provide the calibration
  anchors.
- Iridium NEXT spacecraft technical specification (Aerojet thruster
  application).
- EOS-AM1 (Terra satellite) propulsion subsystem documentation, NASA
  Goddard Space Flight Center.

---

## §9 — Wave-2 plasma-chamber abstraction note

The Wave-2 variants (HET / MPD / ion / arcjet) all need a plasma-state
abstraction the resistojet model does not. Specifically:

- **Hall-effect thrusters (HET)** — crossed-field plasma physics:
  Bohm sheath thickness, Hall parameter (ω_e · τ_en), electron drift
  velocity, ion species fractions, plasma potential profile.
- **MPD thrusters** — magnetoplasmadynamic Lorentz force on partially
  ionized plasma; current-density distribution; magnetic-field topology.
- **Gridded ion engines** — sheath voltages at acceleration grids; ion
  transparency; child-Langmuir current-limited extraction.
- **Arcjets** — distributed-arc plasma chamber (vs resistojet's localized
  resistive heating); arc voltage / current relationships;
  electrothermal-vs-electrostatic energy-deposition split.

None of those have a clean home in the resistojet record family. **Wave-2
is gated on a Team-P plasma-state audit producing a follow-up ADR
(`ADR-027` or later)** per
[ADR-026 §6](../ADR/ADR-026-multi-pillar-coordination.md). The audit
decides:

1. Whether to introduce an `IPlasmaState` interface analogous to
   `IThermodynamicState`, or whether plasma-state lives in a parallel
   record family per pillar variant.
2. The bit-allocation lock for `ElectricHallEffect` (8),
   `ElectricGriddedIon` (9), `ElectricArcjet` (10), `ElectricMpd` (11)
   per `family-allocations.md` §1.
3. Whether `ElectricPropulsionConditions` generalises (kind-discriminated
   like `AirbreathingEngineDesign`) or whether each variant gets its
   own `Conditions` record (parallel-pillar-internal).

**Wave-1 deliberately does NOT prefigure plasma support.** Adding
plasma-shaped fields to `ResistojetConditions` or
`ElectricPropulsionResult` ahead of the audit is exactly the trap the
rule of three prevents.

---

## §10 — Sprint plan (Wave-1)

| Sprint | Days   | Deliverables                                                                         |
|--------|--------|--------------------------------------------------------------------------------------|
| E.0    | 1–5    | Wave-0 docs (this file + ADR-026 + family-allocations + shared-abstractions-ledger), lift Airbreathing as template, EngineFamilies + GateRegistry wiring, scaffolding smoke tests. |
| E.1    | 6–10   | Four physics solvers (ElectrothermalHeaterSolver, IsentropicNozzleSolver, RealGasGammaSolver, RadiationLossSolver) wired into `ElectricPropulsionEngine.Evaluate`, per-solver unit tests. |
| E.2    | 11–13  | `ElectricPropulsionFeasibility.Evaluate` with 5 hard + 5 advisory gates, per-gate unit tests + ordering-snapshot test. |
| E.3    | 14–15  | `ResistojetVoxelBuilder` + `Voxelforge.ElectricPropulsion.StlExporter` + subprocess STL test. |
| E.4    | 16–19  | `ElectricPropulsionFixture_MR501B`, `ResistojetObjective` (via `EngineObjectiveAdapter`), `ElectricPropulsionDesignPersistence` (JSON ser/de), shared-abstractions-ledger update. |

**Total:** 17–19 days. Each sprint is independently shippable (the project
remains coherent at every stop).

**Out of scope this Wave:** any plasma physics (HET / MPD / ion / arcjet);
multi-resistojet cluster design; cathode-life modeling; thermal-cyclic
fatigue (1000+ cycle life); test-stand interface artifact (Item 6 of
hot-fire readiness — N/A for electric, ground vacuum-chamber test rig is
its own roadmap).

---

## §11 — HET physics (Wave-2, Sprint EP.W2.HET)

ADR-029 closes the §9 plasma-state audit and unblocks the Hall-Effect
Thruster (HET) variant. This section pins the contract:

### Design vector (6 dims, mirrors §2 layout)

| Dim | Variable                | Bounds                  | Citation                          |
|----:|-------------------------|-------------------------|-----------------------------------|
| 0   | `DischargeVoltage_V`    | 200 – 400 V             | Goebel & Katz §3.4                |
| 1   | `DischargeCurrent_A`    | 5 – 25 A                | Goebel & Katz §3.4                |
| 2   | `MagneticField_T`       | 0.01 – 0.03 T           | Goebel & Katz §3.6 (peak in channel) |
| 3   | `AnodeRadius_mm`        | 20 – 60 mm              | Goebel & Katz §3.3 (annular geom)  |
| 4   | `ChannelLength_mm`      | 15 – 40 mm              | Goebel & Katz §3.3                 |
| 5   | `XenonMassFlow_kgs`     | 5e-6 – 3e-5 kg/s        | Goebel & Katz §3.4                 |

Bind-time clip on V_d × I_d ≤ BusPower_W_avail. Categorical state preserved
across SA iterations: `Kind`, `AnodeMaterial`, `CathodeType`.

### Physics — Busch discharge model (Goebel & Katz §3)

The lumped first-principles model lives in
[`BuschDischargeModel`](../../../Voxelforge.ElectricPropulsion.Core/Solvers/BuschDischargeModel.cs):

- v_i = √(2·e·V_d·η_b / m_xe)              (Eq 3.36)
- I_b = η_t · I_d                            (current utilisation)
- ṁ_ion = I_b · m_xe / e                     (singly-ionised conservation)
- η_m = ṁ_ion / ṁ_total                      (mass utilisation, capped 1)
- θ = arctan(K_div / B)                      (plume divergence)
- T = ṁ_ion · v_i · cos(θ)                   (axial thrust)
- Isp = T / (g₀ · ṁ_total)

**Calibration constants** (anchored to BPT-4000 to land within ADR-029 D4
±20 %/±15 % envelope):
- η_t = 0.75 (current utilisation)
- η_b = 0.95 (beam efficiency)
- K_div = 0.012 T·rad (plume calibration)
- AnodeLossFraction = 0.30 (Goebel & Katz §3.5)

### Gates (6 = 3 hard + 3 advisory, ADR-029 D6)

Hard:
- `HET_DISCHARGE_VOLTAGE_OUT_OF_BAND` — V_d ∉ [150, 500] V
- `HET_ANODE_OVERHEAT` — T_anode > AnodeMaterial limit (graphite 2000 K /
  BoronNitride 1500 K / AluminaSiC 1900 K)
- `HET_MAGNETIC_FIELD_INSUFFICIENT` — B < 0.005 T (Hall parameter cutoff)

Advisory:
- `HET_PLUME_DIVERGENCE_EXCESSIVE` — θ > 30° (cosine loss > 13 %)
- `HET_CATHODE_LIFE_LIMIT` — I_d > 1.2 × rated cathode current
  (HollowCathode 20 A; FilamentCathode 5 A; Goebel & Katz §6.2)
- `HET_MASS_UTILIZATION_LOW` — η_m < 0.85

All 6 gates are kind-predicated by `design.Kind == HallEffect` inside
[`ElectricPropulsionFeasibility.Evaluate`](../../../Voxelforge.ElectricPropulsion.Core/ElectricPropulsionFeasibility.cs);
resistojet gates do not fire on HET designs and vice versa.

### Validation — BPT-4000 fixture

[`ElectricPropulsionFixture_BPT4000`](../../../Voxelforge.ElectricPropulsion.Tests/Validation/ElectricPropulsionFixture_BPT4000.cs)
asserts:
- Thrust 0.270 N ± 20 % → [0.216, 0.324] N
- Isp 1543 s ± 15 % → [1311, 1775] s
- Discharge power 4500 W ± 5 % (V_d × I_d arithmetic check)
- Mass utilisation η_m ∈ [0.85, 1.0]
- `result.PlasmaState is HetPlasmaState`

### Voxel pipeline

[`HetEnvelopeBuilder`](../../../Voxelforge.ElectricPropulsion.Voxels/Geometry/HetEnvelopeBuilder.cs)
produces an annular outer body (anode + magnetic-shroud-ring integrated
in the wall) plus a central cathode post. Wave-3 will add detailed
magnet-pole geometry, cathode-keeper hollow construction, and a
gas-distribution plenum.

### Out of scope this sub-Wave

- Other plasma variants (arcjet, GriddedIon, MPD) — keep their `_ => throw`
  arms in the `Kind` switch per ADR-029 D2.
- Real magnetostatic solver — `MagneticField_T` is an SA design knob.
- Lifetime / erosion model — gate fires on a static threshold.
- Power Processing Unit (PPU) modelling — bus → discharge treated as ideal.
- WinForms / Avalonia UI panels — SA reaches `HetObjective` via the
  family-agnostic `IObjective` adapter; UI dispatch is automatic once
  the schema flips.
