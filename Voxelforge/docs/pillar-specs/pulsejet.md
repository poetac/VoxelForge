# Pillar spec — Valveless pulsejet

**Status:** Draft (PR-0 of Wave 1, sub-step 1a.5).
**Family:** `airbreathing` (per [`family-allocations.md`](../family-allocations.md) §1).
**Variant kind:** `AirbreathingEngineKind.Pulsejet = 8` (reserved 2026-05-05; activates in PR-3 of Wave 1).
**Sprint:** R2 — air-breathing entry, sub-step 1a.5.
**Authored:** 2026-05-05.
**Related ADRs:** [ADR-026](../ADR/ADR-026-multi-pillar-coordination.md) (multi-pillar coordination), [ADR-019](../ADR/ADR-019-gate-registry.md) (gate registry; this commit ships an additive `GateRegistry<TResult>` overlay alongside the existing rocket-only types), [ADR-022](../ADR/ADR-022-design-persistence-schema-versioning.md) (schema versioning), [ADR-007](../ADR/ADR-007-smoothen-25pct-cap.md) (Smoothen cap).

## Overview

The pulsejet is a self-resonating intermittent-combustion airbreather: ambient air enters through a forward-firing diffuser, mixes with fuel in a combustor, ignites, expands out a long tailpipe, and the inertia of the column draws fresh charge in for the next cycle. The **valveless** variant (Argus / Lockwood Hiller geometry) uses no mechanical valves; pressure waves alone gate the intake. **Zero rotating parts, zero moving parts** — an excellent LPBF story.

This spec covers Wave 1 only — valveless. Valved Argus-type variants (V-1 reed-valve canonical) are deferred to a later sub-step.

Structurally mirrors [`RamjetCycleSolver`](../../Voxelforge.Airbreathing.Core/Cycles/RamjetCycleSolver.cs) — same `IAirbreathingCycleSolver` contract, same null `CompressorDiagnostics` / `TurbineDiagnostics` shape on the result, same constant-property `cp`/`γ` gas. Differs in the combustion model (constant-volume Humphrey vs ramjet's constant-pressure Brayton) and adds a Helmholtz-resonance frequency calculation that drives the acoustic-overpressure feasibility gate.

## Physics model

**Thermodynamic basis.** Constant-volume deflagration (Humphrey cycle approximation). Distinct from ramjet's constant-pressure Brayton in the combustion phase: Humphrey models combustion at constant `V`, raising both `T` and `P`, where Brayton models combustion at constant `P` raising only `T`. Cite [Foa 1960, *Elements of Flight Propulsion*, §11.4] + [Glassman §3 deflagration vs. detonation taxonomy].

**Station-by-station (SAE AS755, time-averaged over the buzz cycle).**

| Station | Populated? | Meaning |
|---|---|---|
| 0 | Yes | Freestream — same convention as ramjet. |
| 1 | Yes | Intake face (= station 0 in lumped 0-D). |
| 2 | Yes | Diffuser exit. Forward-firing diffuser pressure recovery `π_d ≈ 0.85` typical (Foa §11.3). |
| 3 | NaN | No compressor. |
| 4 | Yes | Combustor cycle-mean exit `T_t4`/`P_t4` from Humphrey energy balance. |
| 5 | Yes | Pre-tailpipe = combustor exit. |
| 6, 7 | NaN | No afterburner. |
| 8 | Yes | Tailpipe throat (sonic during peak chamber-P moments; cycle-mean subsonic). |
| 9 | Yes | Tailpipe exit (subsonic, near-atmospheric). |

**Helmholtz-resonant frequency.** `f = (c / (2π)) · √(A_neck / (V_combustor · L_neck))` where `A_neck` is the intake area, `V_combustor` is the combustor volume, `L_neck` is the effective neck length (intake horn length). Cite [Foa 1960, §11.2 eq 11-3]. The valveless Argus variant typically resonates at 40–50 Hz (V-1 buzz bomb measured 45 Hz at sea-level static — [NACA RM E50A04, fig 4]).

**Peak chamber-pressure model.** Humphrey constant-volume combustion produces a peak-to-steady chamber pressure ratio `P_peak / P_steady ≈ T_t4 / T_t2` (ideal-gas constant-V relation). Real engines run lower because the combustor isn't perfectly closed during combustion (some venting through the tailpipe occurs); engineering rule of thumb is 1.0× to 1.5× the steady value. Above ~1.3× the cycle-stability margin tightens enough to fire `PULSEJET_ACOUSTIC_OVERPRESSURE` (Foa §11.4, NACA RM E50A04 V-1 instrumented data).

**Simplifying assumptions.** All load-bearing — open for follow-up sprints:

1. **Time-averaged station map.** Cycle dynamics (buzz frequency, valve-less reverse flow during charge phase) are absorbed into mean values. A transient solver is not in Wave 1.
2. **Constant-property gas (`cp`/`γ`).** Hot-side cp(T) variation deferred per the same trade-off [`RamjetCycleSolver`](../../Voxelforge.Airbreathing.Core/Cycles/RamjetCycleSolver.cs) accepts.
3. **Perfect expansion at the tailpipe exit** (`P_9 = P_∞`).
4. **Forward-firing-diffuser pressure recovery hard-coded to 0.85.** A real Lockwood-Hiller diffuser recovery depends on tailpipe-to-intake area ratio + buzz frequency; deferred.
5. **Humphrey peak-pressure model is a closed-form approximation.** Real cycle dynamics would require a 1-D unsteady solver.

**Dependencies.** Reused as-is from the air-breathing pillar:

- `AirbreathingFuelTables.Lookup` — same JP-8 / JetA / H₂ tables as ramjet.
- `StandardAtmosphere.At` — sea-level + altitude state.
- `IdealGasAir.SpeedOfSound_m_s` / `StagnationTemperatureRatio` / `StagnationPressureRatio`.
- `InletRecovery.Pi_d` — applicable to subsonic forward-firing diffuser.
- `TurbojetCycleSolver.SolveCombustorExitT` — public static helper for hot-side cp routing on JP-8 (kerosene curve) vs H₂ (constant-cp). For Wave 1, pulsejet uses the same routing as ramjet; constant-volume Humphrey applies the same enthalpy balance with the additional `T_t4` → `P_peak` post-step.

New helpers ship in [`Voxelforge.Airbreathing.Core/Cycles/HelmholtzFrequencyCalculator.cs`](../../Voxelforge.Airbreathing.Core/Cycles/HelmholtzFrequencyCalculator.cs) (PR-4) and [`Voxelforge.Airbreathing.Core/Cycles/HumphreyCyclePerformance.cs`](../../Voxelforge.Airbreathing.Core/Cycles/HumphreyCyclePerformance.cs) (PR-4).

## Design variables

| Field | Type | Units | Default | Rationale |
|---|---|---|---|---|
| `PulsejetTubeLength_m` | `double` | m | `0.0` | Total resonant tube length (intake horn + combustor + tail). Drives Helmholtz `f` per Foa §11.2 eq 11-3. Distinct from `CombustorLength_m`, which is just the combustor segment. |
| `PulsejetIntakeArea_m2` | `double` | m² | `0.0` | Forward-firing diffuser intake area `A_neck` (the "neck" area in the Helmholtz lump). Cannot reuse `InletThroatArea_m2` because for valveless geometry the intake is structurally distinct from the nozzle throat. |
| `PulsejetTailpipeArea_m2` | `double` | m² | `0.0` | Tailpipe exit area. Different from `NozzleExitArea_m2` semantically (no CD nozzle on a pulsejet); solver aliases to `NozzleExitArea_m2` when 0.0 so v5 designs round-trip. |

**Reused fields:** `CombustorArea_m2` (combustor cross-section `A_c`), `CombustorLength_m` (combustor axial length), `EquivalenceRatio` (fuel-air ratio for the lean-blowout gate). `InletThroatArea_m2` is reserved for ramjet/turbojet — valveless pulsejet uses `PulsejetIntakeArea_m2` instead.

**Schema bump.** `v5 → v6` (additive identity migration). All three new fields default `0.0` so existing v5 saves load v6 bit-identically. Migration entry `("v5","v6") → identity` in `AirbreathingDesignPersistence.Migrations`.

## Feasibility gates

| ConstraintId | Severity | Category | Triggers when | Source |
|---|---|---|---|---|
| `PULSEJET_BLOWOUT_LEAN` | Hard | PhysicsLimit | Fuel-air mass fraction `f < 0.030` (LFL for hydrocarbon mass basis; 1.4 % vol → ~0.030 mass for JP-8). Fires regardless of φ-bookkeeping because pulsejets blow off below LFL whatever the equivalence-ratio model says. | Glassman §3 Table 3.1 (lower flammability limit for hydrocarbons). |
| `PULSEJET_ACOUSTIC_OVERPRESSURE` | Advisory | EmpiricalBand | `HumphreyCyclePerformance.PeakChamberPressureRatio(...) > 1.30` — predicted resonant pressure spike exceeds 1.3× steady chamber pressure suggests cycle-stability margin is tight. Advisory only (model is empirical). | Foa 1960 §11.4; NACA RM E50A04 instrumented V-1 buzz-bomb data. |

Both gates register from [`AirbreathingGates.RegisterAll()`](../../Voxelforge.Airbreathing.Core/Optimization/AirbreathingGates.cs) against `AirbreathingGateRegistry.Instance` (the air-breathing-pillar wrapper around the generic `GateRegistry<AirbreathingGateInput>`). Gate applicability mask = `EngineFamilyMask.Airbreathing`; predicate guards on `input.Design.Kind == AirbreathingEngineKind.Pulsejet` to avoid firing on ramjet/turbojet/etc.

**Inherited gates** (no re-listing): `COMBUSTOR_BLOWOUT_LEAN`/`COMBUSTOR_BLOWOUT_RICH` (φ-based, separate basis from the LFL gate above), `T_T4_EXCEEDS_LIMIT`, `NOZZLE_INSUFFICIENT_DRIVE_PRESSURE`. These continue to apply because they're keyed on the pillar mask, not the variant kind.

**Gate census:** airbreathing 60 → 62 (after PR-4 of Wave 1 lands).

## Voxel geometry

**Contour shape.** Axis-symmetric, revolved, single-piece tube. Section list:

1. **Intake horn** — divergent flare from forward-facing intake plane to diffuser throat.
2. **Diffuser** — convergent to combustor entrance.
3. **Combustor** — straight cylindrical section, length = `CombustorLength_m`, cross-section = `CombustorArea_m2`.
4. **Tailpipe** — long straight section transitioning to the tapered exhaust.
5. **Exit** — tapered to `PulsejetTailpipeArea_m2`.

**SDF primitives.** Reuse [`RevolvedContourImplicit`](../../Voxelforge.Airbreathing.Voxels/Geometry/RevolvedContourImplicit.cs) — same axis-symmetric SDF the ramjet builder uses. Construct two: inner gas path + outer shell offset by `WallThickness_mm`.

**Boolean topology.** Outer shell – inner cavity, then `Smoothen` clamped per ADR-007 to ≤ 25 % of `WallThickness_mm`. No internal sub-features (no sensor bosses for Wave 1 — pulsejet is a "minimum LPBF" target).

**Smoothen budget.** Capped at `0.25 × WallThickness_mm`, then snapped to 0 if below voxel-size floor (mirroring `RamjetVoxelBuilder` clamp logic).

**LPBF analysis.** `LpbfPrintabilityAnalysis.Run` invoked on the final voxel handle. Pulsejet's long-tube + thin-wall geometry makes `OVERHANG_ANGLE_EXCEEDED` the most likely advisory; expect to size the wall such that the rule-of-thumb 45° overhang floor isn't tripped on the diffuser convergent.

**Cross-platform discipline.** Voxel tests live in `Voxelforge.Airbreathing.Tests/PulsejetVoxelBuilderSubprocessTests.cs` as **subprocess tests** invoking `Voxelforge.Airbreathing.StlExporter.exe` with `--kind=Pulsejet`. The test project stays `net9.0` (not `net9.0-windows`) per the existing `RamjetVoxelBuilderSubprocessTests.cs` pattern. Cross-platform `PulsejetContourTests.cs` (PR-5) test the contour-derivation logic in-process without PicoGK.

## LPBF printability

Variant-specific concerns:

- **Long-aspect-ratio tube** — V-1 reference geometry is 3.4 m long with a ~310 mm combustor diameter (aspect ratio ~11:1). Print-orientation strategy is critical; expect to require horizontal-bed orientation with rotation, not vertical.
- **Thin-wall acoustic resonator** — wall thickness drives both the Helmholtz frequency (via combustor volume) and the LPBF feature floor. Avoid walls below ~0.8 mm to stay clear of `OVERHANG_ANGLE_EXCEEDED` and gas-sealing tolerances.
- **No moving parts** — major LPBF advantage. No reed-valve cavity to drain, no rotating shaft to balance, no internal supports needed.
- **Drain path** — the long tube needs a drain channel for trapped powder removal; the natural exit aperture (tailpipe exit) doubles as the drain in horizontal orientation.

## Validation fixtures

| Fixture | Reference engine | Tolerance bands | Citations |
|---|---|---|---|
| `FockeWulfV1_Pulsejet` | Argus As 109-014 (V-1 buzz bomb) — sea-level static. ~3,000 N thrust, ~600 m/s effective Isp, ~45 Hz buzz frequency. | ±15 % thrust / ±12 % Isp / ±20 % station T (model is closed-form Humphrey approximation; real cycle is highly non-linear). | Foa 1960 *Elements of Flight Propulsion* §11.3; NACA RM E50A04 Cleveland-instrumented V-1 buzz-bomb static-thrust tests. |

The fixture lives in [`Voxelforge.Airbreathing.Tests/Validation/AirbreathingFixtures.cs`](../../Voxelforge.Airbreathing.Tests/Validation/AirbreathingFixtures.cs) (extended; not split into a separate file — `All` is the iteration anchor for parameterised tests). Three fixture-anchored tests in [`AirbreathingValidationTests.cs`](../../Voxelforge.Airbreathing.Tests/Validation/AirbreathingValidationTests.cs):

- `FockeWulfV1_Pulsejet_NetThrust_WithinTolerance`
- `FockeWulfV1_Pulsejet_SpecificImpulse_WithinTolerance`
- `FockeWulfV1_Pulsejet_HelmholtzFrequency_MatchesPublished` (asserts ~45 Hz ± tolerance against Foa §11.2 + NACA RM E50A04).

Wider tolerances than ramjet's ±5%/±10% are deliberate: the time-averaged Humphrey approximation does not capture the full unsteady cycle dynamics, and NACA RM E50A04's static-thrust measurements themselves carry ~5 % uncertainty.

## Verification checklist

The pulsejet PR series (Wave 1, PRs 0–6) walks the [ADR-026 §4.5 Definition-of-Done checklist](../ADR/ADR-026-multi-pillar-coordination.md#45-definition-of-done-checklist) plus these variant-specific items:

- [ ] PR-0: this spec accepted; `family-allocations.md` table notes `Pulsejet = 8` reserved.
- [ ] PR-3: `AirbreathingEngineKind.Pulsejet = 8` shipped; schema v5 → v6 with identity migration; `RoundTrip_Pulsejet_AllFields_Exact` passes; placeholder solver entry in `AirbreathingCycleSolvers.BuildRegistry`.
- [ ] PR-4: real `PulsejetCycleSolver` registered; `HelmholtzFrequencyCalculator` + `HumphreyCyclePerformance` helpers shipped; both gates registered from `PulsejetGates.RegisterAll()`; gate unit tests pass; V-1 fixture tests pass with stated tolerances; `FockeWulfV1_Pulsejet_HelmholtzFrequency_MatchesPublished` returns ~45 Hz ± tolerance.
- [ ] PR-5: `PulsejetVoxelBuilder` produces non-empty output on the V-1 fixture geometry; LPBF analysis runs without overhang violations.
- [ ] PR-6: `--airbreathing` UI dispatches Kind=Pulsejet; subprocess voxel test passes; CHANGELOG entry under `## Unreleased` notes schema v5→v6, gate census +2, `family-allocations.md` Pulsejet flipped Reserved → Live.
- [ ] No optimizer wiring (per Wave 1 stop condition — pulsejet not present in `Voxelforge.Airbreathing.Core/Optimization/*Objective.cs` until Team P's `EngineObjectiveAdapter` lands).

## References

- [Foa, J. V., 1960. *Elements of Flight Propulsion*. Wiley, §11 (Pulsejet engines).](https://archive.org/details/elementsofflight0000foaj) §11.2 Helmholtz resonance, §11.3 Argus / Lockwood-Hiller geometry, §11.4 cycle dynamics.
- [Glassman, I., 1996. *Combustion*, 3rd ed., Academic Press.] §3 Lower flammability limits + deflagration vs detonation.
- [NACA RM E50A04, 1950. *Static thrust and pressure measurements on a German V-1 buzz-bomb pulsejet*.] Cleveland-instrumented test rig, ~3 kN sea-level static, ~45 Hz buzz frequency.
- [Mattingly, J. D., 2006. *Elements of Propulsion*, AIAA.] §5.3 Brayton cycle (for contrast with Humphrey).
