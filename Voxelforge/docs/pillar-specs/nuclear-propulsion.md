# Pillar spec — Nuclear Propulsion (NERVA-class NTR)

> Source of truth for the `Voxelforge.Nuclear.*` subtree.
> Fold any change to bounds / gates / fixture / physics-model citations
> back here in the same PR that lands the code change.

**Status (Wave-1, PR #465):** NERVA-class solid-core NTR only. Bimodal
NTR, Project Pluto nuclear ramjet, and plasma-core variants are deferred
to Wave-2+ with a thermal-neutronics audit gating their start (per
[ADR-026 §6](../ADR/ADR-026-multi-pillar-coordination.md)).

**Schema version:** v1 (initial).
**Family bits owned:** 4 (`NuclearPropulsion`) per
[`family-allocations.md`](../family-allocations.md).

---

## §1 — Overview

Nuclear thermal propulsion replaces the combustion chamber of a
conventional rocket with a nuclear reactor as the heat source. A
solid-core nuclear thermal rocket (NTR) heats liquid hydrogen propellant
by passing it through channels in a reactor core, reaching propellant
temperatures of 2000–2500 K — roughly 4× the flame temperature limit of
LOX/H2 combustion. The resulting specific impulse of 800–900 s is
approximately double that of the best chemical rockets (~450 s).

The Wave-1 entry models the **NERVA-class lumped solid-core NTR**:

- Reactor heats liquid hydrogen from cryogenic inlet (~80 K) to core
  exit temperature (~2300 K) via a UO₂-cermet fuel matrix in an
  Inconel-718 structural shell.
- Heated hydrogen expands through a regeneratively-cooled converging-
  diverging bell nozzle.
- No turbopump: propellant is pressure-fed through the core.

The physics is structurally identical to the rocket pillar's
regenerative-cooling pass — a `RegenCoolingSolver` from `Voxelforge.Core`
is reused for the nozzle cooling path. The novel element is the
`NtrCycleSolver` that replaces combustion with a lumped thermal exchange
between reactor power and propellant mass flow.

**Why now:** OOB-17 in the OOTB roadmap identifies the NERVA-class NTR
as the single largest Isp leap achievable from existing infrastructure.
Published NRX-A6 ground-test data (Jackass Flats NV, 1969) provides a
well-characterised validation anchor within ±5 % tolerance.

---

## §2 — Six SA design variables

Bounds calibrated to the NRX-A6 regime (1100 MW, 33 kg/s, 34 bar) with
headroom for high-thrust / lower-power variants. SA score: minimize
`−Isp_vacuum_s` (maximize Isp).

| # | Name | Bounds | Unit | Source / rationale |
|---|------|--------|------|--------------------|
| 0 | `ReactorThermalPower_MW` | 50 – 2000 | MW | NRX-A6: 1100 MW. Lower 50 MW captures small experiment-class NTRs (e.g. NERVA eXperimental Engine XE); upper 2000 MW covers Pewee-class direct descendants. |
| 1 | `PropellantMassFlow_kgs` | 1 – 50 | kg/s | NRX-A6: 33 kg/s. Lower 1 kg/s corresponds to small experiment-class (<10 kN thrust); upper 50 kg/s covers full NERVA operational-class (~400 kN). |
| 2 | `ChamberPressure_bar` | 25 – 80 | bar | NRX-A6: ~34 bar. Lower 25 bar is the regen-jacket inlet pressure floor (hard gate `NTR_CHAMBER_PRESSURE_TOO_LOW`); upper 80 bar tracks NERVA Program historical high-end. |
| 3 | `ThroatRadius_mm` | 5 – 200 | mm | NRX-A6 nozzle: ~120 mm throat radius. Lower 5 mm = smallest practicable LPBF-printable throat (regen channel wall ~ 1 mm). |
| 4 | `ExpansionRatio` | 20 – 200 | — | NRX-A6: ε ≈ 100. Lower 20 maintains usable nozzle efficiency (ε < 20 loses >5 % Isp for vacuum). Upper 200 is NERVA-Programme envelope ceiling. |
| 5 | `RegenChannelDepth_mm` | 0.5 – 5.0 | mm | Regen cooling channel depth in the nozzle wall. LPBF minimum feature ~0.5 mm; upper 5 mm balanced against wall structural integrity. |

**Non-SA fields preserved via `baseline with { ... }` in `NtrObjective.Unpack`:**
`Kind`, `FuelLoadingFraction`, `ReactorCoreLength_mm`, `ReactorCoreDiameter_mm`,
`NozzleWallThickness_mm`, `NozzleChannelWidth_mm`, `NozzleManifoldDepth_mm`,
`RegenChannelCount`.

**NervaBounds factory** (`NtrObjective.WithNervaBounds`): tighter sub-range
calibrated to the NRX-A6 regime — ReactorThermalPower [500,1500], PropellantMassFlow
[10,50], ChamberPressure [30,60], ThroatRadius [50,150], ExpansionRatio [50,150],
RegenChannelDepth [1,4].

---

## §3 — `NuclearThermalConditions` record

```csharp
namespace Voxelforge.Nuclear;

public sealed record NuclearThermalConditions(
    double PropellantInletTemp_K   // LH2 inlet to reactor core [K]. Typical 80 K.
) : IEngineConditions
{
    public string Family => EngineFamilies.Nuclear;
}
```

Wave-1 deliberately uses a single-field conditions record. Future variants
(bimodal NTR, nuclear ramjet) will extend this with additional fields
under the same record (kind-discriminated, analogous to `AirbreathingEngineDesign`).
The conditions record does not carry ambient pressure because NTR operates
in vacuum; sea-level testing (Jackass Flats) is handled in the fixture via
the zero-altitude assumption (vacuum Isp dominates performance specification).

---

## §4 — `NtrGenerationResult` record

```csharp
namespace Voxelforge.Nuclear;

public sealed record NtrGenerationResult(
    NuclearThermalDesign           Design,
    NuclearThermalConditions       Conditions,
    double                          IspVacuum_s,
    double                          ThrustVacuum_N,
    double                          CoreExitTemp_K,
    double                          CStar_ms,
    double                          GammaEff,
    double                          VolumetricHeatFlux_MWm3,
    double                          KEffHeuristic,
    IReadOnlyList<FeasibilityViolation> Violations,
    IReadOnlyList<FeasibilityViolation> Advisories,
    bool                            IsFeasible
) : IEngineResult
```

`Violations` carries hard-gate failures; `Advisories` carries advisory
warnings; `IsFeasible = Violations.Count == 0`. The result echoes back
`Design` + `Conditions` for correlation in reporting and optimizer
objective scoring.

---

## §5 — Physics model

### 5.1 — `NtrCycleSolver` (lumped 0-D)

`NtrCycleSolver.Solve(design, conditions) → NtrCycleResult`

The solver is a lumped 0-D heat exchanger. Axial-march reactor modeling
(spatially resolved neutron flux + Dittus-Boelter per channel per node)
is Wave-2+ once a thermal-neutronics audit identifies a tractable
open-source solver. The lumped model matches NRX-A6 Isp within ±5 % and
is the literature standard for NTR preliminary design (Borowski et al.
2012, AIAA-2012-3889 §2).

**Step 1 — Core exit temperature (Newton iteration, 8 iterations max):**

```
T_exit = T_inlet + P_MW × 1e6 / (ṁ × cp_mean)
cp_mean = LH2ThermalProperties.Cp_J_kgK((T_inlet + T_exit) / 2)
```

`LH2ThermalProperties` provides cp(T) = 14 000 + 0.700 T [J/(kg·K)],
a linear fit valid 300–3000 K (±3 %; anchored to NASA CEA equilibrium
tables, McBride & Gordon 1994). Newton iteration converges in ≤ 6
iterations for all physically plausible NTR operating points.

**Step 2 — Effective γ at T_exit:**

```
γ_eff = LH2ThermalProperties.Gamma(T_exit) = 1.400 − 4.0×10⁻⁵ × (T_exit − 300)
```

Linear fit valid 300–3000 K. At T_exit ≈ 2300 K: γ ≈ 1.32, consistent
with frozen-flow H2 at that temperature (NASA CEA frozen-mode result).

**Step 3 — Characteristic velocity c\*:**

```
c* = √(R_H2 × T_exit / γ) × ((γ+1)/2)^((γ+1)/(2×(γ−1)))
R_H2 = 4124 J/(kg·K)
```

[Sutton & Biblarz, "Rocket Propulsion Elements" 9e, §3.2 eq. 3-32]

**Step 4 — Vacuum Isp (large ε, Pe → 0 approximation, valid for ε ≥ 20):**

```
Isp_vac = η_eff × √(2γ/(γ−1) × R_H2 × T_exit) / g₀
η_eff = 0.87
g₀ = 9.80665 m/s²
```

`η_eff = 0.87` combines:
- Frozen-flow efficiency ≈ 0.88 (H₂ at 2260 K, ε ≈ 100;
  Illes & Ohler 1998, "Frozen-Flow Losses in NTR Nozzles," AIAA-98-3897)
- 15° half-angle divergence efficiency ≈ 0.99 (Sutton §3.3)
- Product: 0.88 × 0.99 ≈ 0.87

Named constant so reviewers can locate the physical basis; it is not a
fit parameter hidden from the reader.

[Sutton §3.4 (specific impulse); Illes & Ohler 1998 (frozen-flow NTR)]

**Step 5 — Vacuum thrust:**

```
F_vac = ṁ × Isp_vac × g₀
```

**Step 6 — Volumetric heat flux (gate input):**

```
V_core = π × (D_mm/2000)² × (L_mm/1000)   [m³]
Q_vol = P_MW × 1e6 / V_core                [W/m³] → MW/m³
```

**Step 7 — k_eff heuristic (advisory only):**

```
k_eff = 0.98 + FuelLoadingFraction × 0.04
```

UO₂-cermet volume fraction tunes effective multiplication factor
within the engineering band [0.99, 1.05]. This is an engineering
heuristic (not a neutronics solve); the advisory gate flags when the
heuristic lands outside the band but makes no physics claim about
actual criticality. Full neutronics (MCNP / OpenMC) is Wave-2+.

### 5.2 — Regen cooling reuse

`NuclearOptimization.RunRegenCooling` builds a `PropellantState` for
gas-phase H₂ at T_exit (MixtureRatio = 0, H₂ as both propellant and
coolant), constructs a synthetic `ChamberContour` (throat + exit stations,
contraction ratio 3.0, L* 0.5 m), constructs a `ChannelSchedule` from
the 6 regen SA dims, and calls `RegenCoolingSolver.Solve(inputs)` from
`Voxelforge.Core/HeatTransfer/`.

This reuse is intentional: the nozzle heat-transfer physics is identical
to the rocket pillar; only the coolant (LH₂ instead of CH₄/LOX) and
the upstream heat source (reactor vs combustion chamber) differ.
`HydrogenFluid.Instance` from `Voxelforge.Core/Coolant/` provides the
H₂ transport properties (covers 30–1500 K; NTR coolant-side inlet ~80 K
→ outlet ~800 K lies within this range).

Regen failure is **advisory only**: if `RegenCoolingSolver` throws, the
nozzle cooling pass returns `regenWallExceedsLimit = false` (no wall
advisory); the gate `NTR_REGEN_COOLING_BUDGET` fires only when the solver
runs to completion and `regen.WallTempExceedsLimit` is true. This matches
the electric-propulsion pillar's treatment of sub-solvers that can
encounter ill-conditioned inputs at the optimizer boundary.

### 5.3 — Conscious omissions

- **No axial neutron-flux profile.** The lumped 0-D model treats the
  reactor as a uniform volumetric heat source. NRX-A6 validation is within
  ±5 % tolerance, which is acceptable for preliminary design optimization.
  Axial resolution is Wave-2+ behind a thermal-neutronics ADR.
- **No pump/valve feed-system modeling.** NTR propellant feed is treated
  as pressure-fed (chamber pressure back-pressures the core exit). An
  expander-cycle bleed turbopump is architecturally possible but not
  required to match NRX-A6 Isp.
- **No real neutronics.** `k_eff` is an engineering heuristic only.
  MCNP / OpenMC integration is Wave-2+.
- **No LPBF/UO₂-cermet fuel-pin printability gates.** Fuel-pin geometry
  lives in the reactor core, not the nozzle. Wave-2+ when 3D-printed
  NTR concepts (e.g. BWX BWXT-NTP) become a specific design target.

---

## §6 — Feasibility gates

Three hard + three advisory gates. All evaluate inside
[`NuclearGates.Evaluate`](../../../Voxelforge.Nuclear.Core/Optimization/NuclearGates.cs)
— a parallel evaluator (not registry-driven), per
[ADR-026 §6 risk #2](../ADR/ADR-026-multi-pillar-coordination.md).

### Hard gates (3)

| # | ConstraintId | Kind | Threshold | Citation |
|---|---|---|---|---|
| 1 | `NTR_REACTOR_OVERTEMP` | PhysicsLimit | `CoreExitTemp_K > 3000` — UO₂-cermet fuel centerline melting limit. | Lyon "Refractory Metal Properties" NASA-CR-179614; NERVA NRX-A6 operational envelope (Borowski et al. 2012 Table 1). |
| 2 | `NTR_THERMAL_FLUX_EXCEEDED` | PhysicsLimit | `VolumetricHeatFlux_MWm3 > 4000` — NERVA historical operating maximum ~4 GW/m³. | Bennett 1972 AIAA-72-1161 (NRX programme heat-flux data). |
| 3 | `NTR_CHAMBER_PRESSURE_TOO_LOW` | ManufacturabilityFloor | `ChamberPressure_bar < 30` — regen jacket inlet pressure floor; below this the coolant-channel pressure drop exceeds available driving head. | Voxelforge.Core `RegenCoolingSolver` pressure-drop model (engineering floor). |

### Advisory gates (3)

| # | ConstraintId | Kind | Threshold | Citation |
|---|---|---|---|---|
| 4 | `NTR_K_EFF_OUT_OF_BAND` | EmpiricalBand | `k_eff < 0.99 or > 1.05` — heuristic criticality band for NERVA-class UO₂-cermet / Inconel geometry. | NERVA NRX programme operational k_eff data (Borowski et al. 2012). |
| 5 | `NTR_FUEL_CTE_MISMATCH` | EmpiricalBand | `FuelLoadingFraction > 0.80` — UO₂-cermet / Inconel-718 CTE mismatch risk above 80 vol% fuel. | Lyon NASA-CR-179614 §4 (UO₂-cermet failure modes above 80% packing). |
| 6 | `NTR_REGEN_COOLING_BUDGET` | EmpiricalBand | `regen.WallTempExceedsLimit` — nozzle wall temperature exceeds Inconel-718 service limit. | `RegenSolverOutputs.WallTempExceedsLimit` (same threshold as rocket-pillar gate `WALL_TEMP_EXCEEDED`). |

**Severity discipline:** Hard gates make the model output non-meaningful
(the fuel melts; the reactor disassembles; the coolant cannot flow).
Advisory gates flag designs outside the NERVA historical flight envelope
but where the physics model output is still interpretable.

---

## §7 — Voxel build (`NtrChamberVoxelBuilder`)

PicoGK voxel build at
`Voxelforge.Nuclear.Voxels/Geometry/NtrChamberVoxelBuilder.cs`.

### Geometry

1. **Nozzle contour (inner wall)** — computed via
   `ChamberContourGenerator.Generate(ThroatRadius_mm, 3.0, ExpansionRatio, 0.5)`,
   which produces an array of (r, z) stations describing the bell nozzle
   inner surface. This is pure math (no PicoGK); all contour point arrays
   are generated on any thread.
2. **Nozzle outer wall** — each inner point radially offset by
   `NozzleWallThickness_mm` to produce the outer surface.
3. **Voxel build** — `LibraryScope.MakeVoxels(new RevolvedContourImplicit(inner), bbox)`
   gives the solid nozzle volume; subtract the inner bore gives the annular
   shell. Nuclear.Voxels holds its own `LibraryScope` (ThreadStatic,
   identical to the Marine and EP pillar pattern) so it does not depend on
   `Voxelforge.Geometry.LibraryScope` which is internal to Voxelforge.Voxels.
4. **Stub reactor core** — a cylindrical solid of radius = CoreDiameter/2 mm
   and length = CoreLength mm, built as a two-point revolved contour and
   BoolAdded to the aft face of the nozzle assembly. No fuel-pin or channel
   geometry (Wave-1; comment documents the deferral). The stub core makes
   the STL useful for envelope / mass estimation.

### SDF primitives

`RevolvedContourImplicit` from `Voxelforge.Voxels` (public; referenced
directly by `Voxelforge.Nuclear.Voxels`). `ChamberContourGenerator` from
`Voxelforge.Voxels` (public). Both are VFA001-safe — Nuclear.Voxels is
explicitly permitted to reference Voxelforge.Voxels for these two types.

### Smoothen budget

Cap = min(0.10 mm, 25 % × NozzleWallThickness_mm) per ADR-007. Default
`SmoothenRadius_mm = 0.10 mm`.

### LPBF discipline

`LpbfPrintabilityAnalysis.Run` is NOT called in Wave-1 for the NTR nozzle.
Reactor core geometry (stub cylinder) has no overhang concern. This
matches the EP pillar (Wave-1 defers printability gate invocation).

### LibraryScope + threading

The builder follows PicoGK pitfall #4: voxel ops run on the task thread.
`NtrChamberVoxelBuilder.Build(design, options)` is a synchronous call;
callers (StlExporter entry point, future UI dispatch) marshal via their
own task-thread management.

### STL export

`Voxelforge.Nuclear.StlExporter/Program.cs` reads `NuclearThermalDesign`
from JSON on disk, constructs a scoped `Library`, calls
`NtrChamberVoxelBuilder.Build(...)`, writes STL via `NtrStlExport.Save`,
and prints `BENCH` key=value lines for subprocess parsing. Exit codes:
0 success / 1 build failure / 2 malformed JSON / 3 bad CLI args. Mirrors
`Voxelforge.ElectricPropulsion.StlExporter`.

---

## §8 — Validation fixture: NERVA NRX-A6

The NRX-A6 is the most extensively published NERVA-class solid-core NTR
ground test. It ran at Jackass Flats, Nevada in 1969 at the Nevada Test
Site Nuclear Rocket Development Station (NRDS). Published data provides
multiple independently verifiable performance anchors.

### Test conditions

| Property | Value | Source |
|---|---|---|
| Reactor thermal power | 1100 MW | Borowski et al. 2012 Table 1 |
| LH₂ mass flow | 33.0 kg/s | Borowski et al. 2012 Table 1 |
| Chamber pressure | ~34 bar | Bennett 1972 AIAA-72-1161 |
| Expansion ratio | ~100 | NERVA programme nozzle data |
| Propellant inlet temp | 80 K | Cryogenic LH₂ feed |

### Tolerance bands

| Property | Target | Tolerance | Pass band | Source |
|---|---|---|---|---|
| Isp (vacuum) | 825 s | ± 5 % | 784 – 866 s | Borowski et al. 2012; Bennett 1972 |
| Thrust (vacuum) | 267 kN | ± 5 % | 254 – 280 kN | Derived: F = ṁ × Isp × g₀ |
| Core exit temperature | ~2260 K | sanity band | 2100 – 2500 K | Borowski et al. 2012 Table 1 |

### Fixture location

`Voxelforge.Nuclear.Tests/Fixtures/NervaNrxA6Fixture.cs` — five `[Fact]`
test methods:

1. `NrxA6_IspVacuum_WithinFivePercent` — primary validation
2. `NrxA6_ThrustVacuum_WithinFivePercent` — secondary
3. `NrxA6_CoreExitTemp_InReasonableBand` — sanity check
4. `NrxA6_IsFeasible` — no hard-gate violations at NRX-A6 conditions
5. `NrxA6_IsDeterministic` — two independent calls return bit-identical results

### Citations

- Borowski SK et al., "Nuclear Thermal Propulsion (NTP): A Proven,
  Cost-Effective Technology for Future Human Space Exploration Missions,"
  AIAA-2012-3889, 2012.
- Bennett GL, "A Look at the Nuclear Rocket Program," AIAA-72-1161, 1972.
- NERVA NRX-A6 ground test data, Nevada Test Site, 1969 (declassified,
  cited via Borowski et al. 2012 and Bennett 1972).

---

## §9 — Wave-2+ deferred variants note

**Bimodal NTR (power + propulsion)** — adds a Brayton-cycle power
conversion loop (alternator + radiator) sharing the reactor core. The
cycle solver adds a second steady-state operating point; the voxel
builder adds a radiator-panel assembly. Gated on a power-generation
architecture review (overlaps with Step 2 PowerGeneration pillar; bit 5
`Reserved`).

**Project Pluto nuclear ramjet** — atmospheric nuclear propulsion
(Mach 3+, sea-level air). Structurally similar to the airbreathing
ramjet pillar; the reactor replaces the combustion chamber. Cross-pillar
reuse of `AirbreathingEngine` inlet physics. Gated on an airbreathing-
nuclear cross-pillar review (ADR-026 §7 import discipline: Nuclear.Core
must not directly import Airbreathing.Core).

**Plasma-core / gas-core NTR** — reactor core operates as a uranium
plasma rather than solid fuel matrix. Three-fold Isp improvement
(2000+ s) at the cost of fissile-gas containment engineering. Requires a
plasma-state abstraction analogous to the electric-propulsion Wave-2
plasma audit.

**Fuel-pin geometry in the voxel builder** — LPBF-printed UO₂-cermet
fuel-pin bundles (e.g. BWX BWXT-NTP concept). Requires fuel-pin geometric
constraint data and a printability-gate pass for cermet matrix density.

None of the above share a bit in `family-allocations.md` yet; the
Reserved protocol (§2 of `family-allocations.md`) applies when a
specific Wave-2 NTR sub-step is committed.

---

## §10 — Sprint plan (Wave-1)

| Sprint | Days | Deliverables |
|--------|------|--------------|
| N.0 | 1–2 | Docs: this file + `family-allocations.md` bit-4 flip + `shared-abstractions-ledger.md` rows. GitHub issue #465. Solution folder + 4 .csproj files. `EngineFamilies.Nuclear` + `LH2ThermalProperties`. |
| N.1 | 3–5 | `NtrCycleSolver` + `NuclearOptimization` (regen reuse path). `NuclearEngine` singleton (IEngine adapter). `NuclearThermalDesign` record + all satellite types (`NuclearKind`, `NuclearConstraintIds`, `NuclearThermalConditions`, `NtrGenerationResult`). |
| N.2 | 6–7 | `NuclearGates` (3 hard + 3 advisory). `NtrObjective` (6-dim SA via `EngineObjectiveAdapter`). `NuclearSchemaVersion` + `NuclearDesignPersistence` (JSON + completeness guard). |
| N.3 | 8–9 | `Voxelforge.Nuclear.Tests`: `NervaNrxA6Fixture`, `NtrCycleSolverTests`, `NuclearGatesTests`, `NuclearSchemaTests`. All pass. |
| N.4 | 10–11 | `Voxelforge.Nuclear.Voxels`: `LibraryScope`, `PicoGKVoxelHandle`, `NtrBuildOptions`, `NtrGeometryResult`, `NtrChamberVoxelBuilder`, `NtrStlExport`. `Voxelforge.Nuclear.StlExporter`. STL test (non-empty output). |
| N.5 | 12 | CHANGELOG entry. Full regression (`dotnet test` all 5 pillars). PR #465. |

**Total:** ~12 days. Each sprint is independently shippable and leaves
the solution in a buildable state.

**Out of scope this Wave:** bimodal NTR, Project Pluto, real neutronics,
LPBF/UO₂-cermet printability gates, axial-march reactor solver, CFD
calibration, WinForms / Avalonia UI panels, multi-zone fuel-pin geometry.
