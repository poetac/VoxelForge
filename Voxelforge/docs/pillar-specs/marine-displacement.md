# Pillar spec — Marine / marine-displacement (M1 AUV mid-body + M2 CylindricalHemi)

**Family:** `EngineFamilies.Marine = "marine"`  
**Variant family ID:** `"marine"` (uses `MarineKind.AuvMidBody`)  
**EngineFamilyMask bits claimed:** bit 13 = `Marine`, bit 14 = `MarineHull` (see `family-allocations.md`)  
**Schema version:** v1 (Wave-1) → v2 (Wave-2 M2, adds `HullFamily`)  
**Wave:** Wave 1 (M1 Myring) + Wave 2 (M2 CylindricalHemi, M3 voxel, M4 fixtures)  
**Sprint:** Sprint M.0 – M.4 (Wave-1); M2/M3/M4 (Wave-2, closes [#414](https://github.com/poetac/voxelforge/issues/414))  
**ADR:** ADR-026 (multi-pillar coordination)

---

## Physics domain

Incompressible hydrodynamics + hydrostatics for fully-submerged axisymmetric
hulls. No combustion, no compressible flow, no free-surface effects (M1
is fully submerged — surface metacentric height deferred to M4-M5).

Governing physics:
- **Drag:** empirical Hoerner form-drag + Prandtl-Schlichting turbulent
  skin friction for axisymmetric streamlined bodies.
- **Geometry:** Myring (1976) parametric nose/tail fairing profiles.
- **Structural:** thin-walled cylindrical shell under external hydrostatic
  pressure, per ASME BPVC §VIII Div 1 UG-28 (Windenburg-Trilling 1934).
- **Buoyancy:** Archimedes' principle. Displaced volume integrated
  numerically (200-station trapezoidal) over Myring profile.

Domain constraints:
- Reynolds number regime: turbulent (Re_L > 10⁵ for any design in the
  parameter space; Prandtl-Schlichting C_f formula valid).
- Depth rating: ≤ 500 m (shallow-water ASME UG-28 thin-shell limit;
  deep-sea requires ASME pressure-vessel qualification beyond this scope).
- Slenderness: L/D ∈ [3, 20] (outside this range Hoerner form-drag
  correlation loses accuracy).

---

## Design record — `MarineDesign`

Implements `IEngineDesign` with `Family = EngineFamilies.Marine`.

| Field | Type | Unit | Description |
|---|---|---|---|
| `Kind` | `MarineKind` | — | `AuvMidBody` for M1 |
| `Length_m` | `double` | m | Overall hull length |
| `Diameter_m` | `double` | m | Maximum hull diameter |
| `NoseFairingFraction` | `double` | — | Nose fairing length / total length [-] |
| `TailFairingFraction` | `double` | — | Tail fairing length / total length [-] |
| `WallThickness_m` | `double` | m | Pressure hull shell wall thickness |
| `MaterialIndex` | `int` | — | 0=Ti-6Al-4V, 1=Al-6061, 2=AISI-316L (LPBF) |
| `DepthRating_m` | `double` | m | Target operating depth rating |

Computed properties (not persisted):
- `NoseLength_m = Length_m × NoseFairingFraction`
- `TailLength_m = Length_m × TailFairingFraction`
- `MidBodyLength_m = Length_m − NoseLength_m − TailLength_m`
- `FinenessRatio = Length_m / Diameter_m`

---

## Conditions record — `MarineConditions`

Implements `IEngineConditions` with `Family = EngineFamilies.Marine`.

| Field | Type | Unit | Description | Default |
|---|---|---|---|---|
| `CruiseSpeed_ms` | `double` | m/s | Design cruise speed | — (required) |
| `MaxDepth_m` | `double` | m | Maximum operating depth | — (required) |
| `WaterTemperature_K` | `double` | K | Seawater temperature | 277.15 (4°C) |
| `Salinity_ppt` | `double` | g/kg | Salinity | 35.0 (ocean) |

Computed properties:
- `WaterDensity_kgm3` ≈ 1025 + 0.8×Salinity_ppt − 0.2×(WaterTemperature_K − 277) (Millero & Poisson 1981 simplified)
- `HydrostaticPressure_Pa = WaterDensity_kgm3 × 9.80665 × MaxDepth_m`
- `KinematicViscosity_m2s` ≈ 1.35×10⁻⁶ m²/s at 4°C (Van Mieghem 1954)

---

## Result record — `MarineResult`

Implements `IEngineResult` (sealed record).

| Field | Type | Unit | Description |
|---|---|---|---|
| `Design` | `MarineDesign` | — | Input design (echo-back) |
| `Conditions` | `MarineConditions` | — | Input conditions (echo-back) |
| `DragForce_N` | `double` | N | Total hull drag at cruise speed |
| `DragCoefficient` | `double` | — | C_D based on frontal area |
| `BuoyancyForce_N` | `double` | N | Archimedes uplift |
| `DisplacedVolume_m3` | `double` | m³ | External hull volume |
| `BuoyantWeight_N` | `double` | N | BuoyancyForce_N − HullWeight_N (+ = positive buoyancy) |
| `CriticalBucklingPressure_Pa` | `double` | Pa | ASME UG-28 P_cr |
| `BucklingSafetyFactor` | `double` | — | P_cr / P_hydrostatic |
| `HullMass_kg` | `double` | kg | Structural shell mass |
| `CgCbOffset_m` | `double` | m | \|z_CG − z_CB\| (0 for symmetric hull) |
| `Violations` | `IReadOnlyList<FeasibilityViolation>` | — | Hard gate failures |
| `Advisories` | `IReadOnlyList<FeasibilityViolation>` | — | Advisory warnings |
| `IsFeasible` | `bool` | — | `Violations.Count == 0` |

---

## Gate census

### Hard gates

| Constraint ID | Condition | Reference |
|---|---|---|
| `HULL_BUOYANCY_NEGATIVE` | ΔF < 0 (hull sinks) | Archimedes' principle |
| `HULL_BUCKLING_INSUFFICIENT` | SF_buckling < 1.5 | ASME BPVC §VIII UG-28 |
| `HULL_WATERTIGHT_INTEGRITY` | WallThickness_m < 0.0015 | LPBF min feature (2 mm) + margin |
| `PRESSURE_HULL_SF_BELOW_THRESHOLD` | SF_buckling < 1.5 | ASME BPVC §VIII UG-28 |
| `DEPTH_RATING_EXCEEDED` | MaxDepth_m > DepthRating_m | Spec compliance |

Note: `HULL_BUCKLING_INSUFFICIENT` and `PRESSURE_HULL_SF_BELOW_THRESHOLD` both
check SF_buckling < 1.5; they differ in naming intent (structural vs. spec).
Both fire when the condition is met, consistent with gate-per-concern principle.

### Advisory gates

| Constraint ID | Condition | Reference |
|---|---|---|
| `HULL_DRAG_ABOVE_BAND` | C_D_total > 0.12 | Hoerner 1965 §6-2 slender-body upper band |
| `APPENDAGE_INTERFERENCE_DRAG_HIGH` | k_app > 1.3 | Hoerner 1965 §8 appendage factors |
| `CG_CB_OFFSET_LARGE` | \|z_CG − z_CB\| > 0.05×Diameter_m | AUV stability rule-of-thumb |
| `FINENESS_RATIO_OUT_OF_BAND` | L/D < 5 or L/D > 12 | Hoerner 1965 §6-2 optimum range |
| `LPBF_HULL_WALL_TOO_THIN` | WallThickness_m < 0.002 | CLAUDE.md LPBF floor (2.0 mm) |

---

## Design variables for `DisplacementHullObjective : IObjective`

| Dim | Name | Lo | Hi | Unit | Notes |
|---|---|---|---|---|---|
| 0 | `Length_m` | 0.5 | 5.0 | m | — |
| 1 | `Diameter_m` | 0.05 | 1.0 | m | — |
| 2 | `NoseFairingFraction` | 0.10 | 0.35 | — | — |
| 3 | `TailFairingFraction` | 0.15 | 0.40 | — | — |
| 4 | `WallThickness_m` | 0.002 | 0.020 | m | — |
| 5 | `MaterialIndex` | 0.0 | 2.0 | — | Rounded to int in Unpack |
| 6 | `DepthRating_m` | 10.0 | 500.0 | m | — |

Score: minimize `DragForce_N` at cruise speed. Infeasible → `double.PositiveInfinity`.

---

## Hull families (Wave-2 M2)

Two `HullFamily` variants are supported. Both dispatch through `MarineOptimization.GenerateWith`;
all downstream solvers (Hoerner drag, Hydrostatic, W-T buckling) are family-agnostic.

| Family | Geometry | S_wet | V_ext |
|---|---|---|---|
| `Myring` (default) | Three-part: Myring nose (n=2) + cylinder + Myring tail (m=1.5, p=0.5) | π·D·(L_n/3 + L_m + 0.7·L_t) | Numerical integration 200 stations |
| `CylindricalHemi` | Hemisphere caps (R = D/2) + cylinder | π·D·L | (π/6)·D³ + (π/4)·D²·(L−D) |

For `CylindricalHemi`, nose/tail fairing fractions in `MarineDesign` are stored but geometrically
ignored. `ValidateSelf()` only requires `Diameter_m < Length_m`.

W-T formula applied to the cylindrical section for both families; hemispherical caps are
geometrically stronger (conservative for Myring tails; over-conservative for hemispheres).

### CylindricalHemi radial profile r(x)

```
x ∈ [0, R]:       r = sqrt(R² − (R−x)²)   (nose hemisphere)
x ∈ [R, L−R]:     r = R                    (cylinder)
x ∈ [L−R, L]:     r = sqrt(R² − (x−(L−R))²)  (tail hemisphere)
```

where R = D/2 and L = total hull length.

---

## Validation fixtures

| Fixture class | Real system | L (m) | D (m) | t (m) | Material | Depth (m) | Expected drag |
|---|---|---|---|---|---|---|---|
| `MarineHullFixture_REMUS100` | REMUS-100 (Hydroid) | 1.595 | 0.190 | 0.005 | Al-6061 | 100 | 3.9 N @ 1.5 m/s |
| `MarineHullFixture_REMUS600` | REMUS-600 (Kongsberg) | 3.25 | 0.324 | 0.015 | Al-6061 | 600 | 19 N @ 2.0 m/s |
| `MarineHullFixture_REMUS6000` | REMUS-6000-class (Kongsberg) | 3.84 | 0.71 | 0.026 | Ti-6Al-4V | 800† | 31 N @ 1.5 m/s |
| `MarineHullFixture_Bluefin21` | Bluefin-21-class (GD) | 4.93 | 0.533 | 0.018 | Al-6061 | 300 | 36 N @ 1.8 m/s |

†REMUS-6000 fixture uses 800 m model-limited depth; real vehicle uses ring-stiffened sections not captured by the W-T formula.

All fixtures: 8 tests each (IsFeasible, drag ±40%, SF ≥ 1.5, BuoyancyForce > 0, IsPositivelyBuoyant, HullMass in range, C_D in [0.001, 0.20], deterministic).

---

## Cited empirical correlations

| Symbol / formula | Source | §Section | Notes |
|---|---|---|---|
| Myring nose profile r(x) | Myring 1976, *Aeronautical Quarterly* 27(3) 186-194 | §2 | n=2.0 default |
| Myring tail profile r(ξ) | Myring 1976 | §3 | m=1.5, p=0.5 default |
| C_f = 0.455 / (log₁₀ Re_L)^2.58 | Prandtl-Schlichting (Schlichting 1979, *Boundary Layer Theory* 7th ed.) | §21.3 | Turbulent flat-plate skin friction |
| C_D_form = C_f × (1 + 1.5(D/L)^1.5 + 7(D/L)^3) | Hoerner 1965, *Fluid-Dynamic Drag* | §6-2 | Streamlined axisymmetric body |
| P_cr = 2E(t/D)^3 / (1−ν²) | Windenburg & Trilling 1934, NACA-TN-517 | eq.(5) | Long-cylinder elastic thin-shell |
| ρ_water ≈ 1025 + 0.8S − 0.2(T−277) | Millero & Poisson 1981, *DSR* 28(6) 625-629 | eq.(4) simplified | Valid for S∈[30,40], T∈[270,290 K] |
