# Feasibility gates

voxelforge screens every candidate design through a ring of feasibility
gates before the scoring function ever runs. Across all five physics
pillars the project enforces **196 unique feasibility constraints**
(`ConstraintId`s) — **65 rocket · 40 air-breathing · 54 electric · 22
marine · 15 nuclear** — each pillar evaluated by its own surface per
ADR-026 §3. Any violation makes the candidate infeasible and its total
score becomes `+∞`, so the optimiser unconditionally rejects it — but
every violation is collected (not fail-fast), so the UI and reports can
explain *every* reason a design was rejected, not just the first.

This document catalogues **all five pillars** by machine-readable
`ConstraintId`, with the firing condition and source evaluator for each.
The rocket pillar is documented gate-by-gate first; the air-breathing,
electric, marine, and nuclear pillars follow. The authoritative registries
are the `*Feasibility` / `*Gates` evaluators in `Voxelforge.Core` and
`Voxelforge.{Airbreathing,ElectricPropulsion,Marine,Nuclear}.Core`.

**Why this matters.** Most preliminary-design tools optimise first and
validate second. voxelforge inverts that: physics reality is an
*admission criterion*, not a penalty term. The scoring function only
ever sees designs that are already buildable, cool-able, startable, and
structurally sound — which means the Pareto front you look at at the
end is made of real candidates, not optimiser artefacts that would
burn a test stand down.

Gates are **necessary but not sufficient**. They reject the worst
violations of well-understood constraints with the uncertainty bands
documented in [`PHYSICS.md`](PHYSICS.md). They do *not* replace a
dynamic, full-3D CFD / FEA check before first fire.

## Rocket pillar — five gate families

The rocket evaluator's full surface is **65 `ConstraintId`s**, split across
the five families below. The regen, monopropellant and aerospike families
are evaluated per applicable design; the monolithic family fires only on
fused single-STL builds; the voxel-adequacy gate is an optimiser-level
check on the requested session voxel size.

| Family | Count | Source | Applies to |
|---|---|---|---|
| Regen-cooled bell chamber | 55 | `Voxelforge.Core/Optimization/RocketGates.cs` (incl. `PUMP_PRESSURE_INVERTED` post-Tier-1 bundle) | Every candidate |
| Aerospike-parallel | 5 | `Voxelforge.Core/Geometry/AerospikeFeasibility.cs` | Aerospike + LinearAerospike topologies |
| Monolithic composition | 2 | `Voxelforge.Voxels/Geometry/MonolithicFeasibility.cs` | Monolithic-engine builds only |
| Monopropellant | 2 | `Voxelforge.Core/Optimization/MonopropGates.cs` | Monopropellant designs only |
| Voxel adequacy | 1 | `Voxelforge.Core/Optimization/RegenChamberOptimization.cs` (`VOXEL_RESOLUTION`) | Every candidate (optimiser-level) |

The five families sum to the rocket evaluator's **65** `ConstraintId`s
(55 regen + 5 aerospike + 2 monolithic + 2 monopropellant + 1 voxel).

## The 55 regen gates

Each gate has a stable machine-readable ID — these appear in test
fixtures, PR descriptions, and the `[INFEASIBLE] <ID>` lines on the
optimiser's stdout. Changing an ID is a breaking change. The table below
enumerates all 55 regen `ConstraintId`s; the authoritative registry lives
in `Voxelforge.Core/Optimization/RocketGates.cs` (registration order is
snapshot-pinned by `GateOrderingSnapshotTests`). The first block carries
detailed provenance; the remainder (newer advisory-band and
cycle-specific gates) is listed in brief. Advisory-severity gates are
marked *(advisory)*.

| ID | Fires when | Source reference |
|---|---|---|
| `WALL_TEMP` | Peak gas-side wall T > `WallMaterial.MaxServiceTemp_K` | Bartz gas-side HTC; see [`PHYSICS.md`](PHYSICS.md#bartz). |
| `YIELD_EXCEEDED` | Minimum structural safety factor < 1.0 | Axisymmetric pressure-vessel + thermal-stress model. |
| `FEATURE_TOO_SMALL` | Any rib / wall / channel < 0.30 mm | Universal LPBF print floor (`LpbfFeatureFloor_mm`). |
| `COOLANT_T_EXCEEDED` | Coolant outlet T > fluid `MaxBulkT_K` (coking / embrittlement) | Per-fluid service limits in `CoolantRegistry`. |
| `STABILITY_FAIL` | Crocco N-τ composite rating = Fail | Chug / buzz / screech screen; see [`PHYSICS.md`](PHYSICS.md#combustion-stability). |
| `ELEMENT_DENSITY_TOO_HIGH` | Injector elements per cm² > 0.7 on the face disc | Rule-of-thumb from Huzel & Huang §8.2; face-plate burnout risk. |
| `INJECTOR_FACE_T_EXCEEDED` | Predicted face T > wall material service limit | Equilibrium model: Bartz-ish h_g + bore-scale Dittus-Boelter h_back. |
| `FEED_PRESSURE_INSUFFICIENT` | Feed-stack predicted Pc < target Pc | Tank ullage → line → valve → filter → umbilical → injector stackup. Only fires when `OperatingConditions.TankUllagePressure_Pa > 0`. |
| `IGNITER_ENERGY_INSUFFICIENT` | Selected igniter rated energy < propellant-pair-specific floor | Sprint 29: per-pair floors from `Combustion.IgnitionRequirements` — LOX/CH4 50 mJ, LOX/H2 5 mJ, LOX/RP-1 500 mJ. Hypergolic pairs short-circuit. Replaces the pre-Sprint-29 universal 50 mJ floor. |
| `IGNITER_MISSING` | `IgniterType.None` selected on a non-hypergolic propellant pair | Sprint 29. Pre-Sprint-29 None always passed; now required on every LOX-* pair. Silent on N2O4/MMH + H2O2/RP-1. Remediation: pick the pair's minimum modality. |
| `IGNITER_MODALITY_UNSUITABLE` | Modality ordinal (None=0, SparkTorch=1, AugmentedSpark=2, PyrotechnicCartridge=3) below the pair's recommended minimum | Sprint 29. Fires on e.g. LOX/RP-1 + SparkTorch even when rated energy clears the floor — Huzel & Huang §7.2: plain spark torches unreliable on kerosene cold start. |
| `PURGE_FLOW_INSUFFICIENT` | Any purge port delivers < 95 % of requested mass flow | One violation emitted per under-sized port. |
| `ABLATIVE_BURNTHROUGH` | `(recession + char_depth) × safety_factor > initial_thickness` | Opt-in via `AblativeMaterial != None`. |
| `CHILLDOWN_BUDGET_EXCEEDED` | Integrated chilldown time > user budget | Lumped-jacket two-phase integrator; opt-in via `IncludeChilldownTransient`. |
| `HARD_START_RISK` | Start-transient predicts Pc overshoot > `StartHardStartFactor` | 0-D lumped simulator; opt-in via `IncludeStartTransient`. |
| `NPSH_INSUFFICIENT` | NPSHA < NPSHR on at least one turbopump | Only fires on cycles other than PressureFed / ElectricPump. |
| `TPMS_CELL_FEATURE_TOO_SMALL` | TPMS strut thickness < 2.0 mm LPBF floor | `(1 − solid_fraction) × cell_edge`. Fires only for TPMS topologies; non-TPMS designs keep the universal 0.30 mm floor. |
| `TURBINE_POWER_DEFICIT` | Available shaft power < required on at least one shaft | Preburner-driven turbine cannot supply its pump; fires only on cycles with a turbine. |
| `SHAFT_WHIRL` | Operating RPM lands within ±20 % of the first bending critical | Bearing fatigue + uncontained whirl risk; skipped on PressureFed / ElectricPump. |
| `PREBURNER_WALL_TEMP` | Preburner peak wall T > material service limit | Lumped-parameter Bartz + Dittus-Boelter; fires on both fuel-rich and ox-rich (FFSC) preburners. Opt-in via `IncludePreburnerRegenCooling`. |
| `PINTLE_BLOCKAGE_OUT_OF_BAND` | Pintle blockage factor `BL = N · d_sleeve / (π · D_pintle)` outside [0.40, 0.85] | Dressler stable-combustion band. Fires only on pintle injector patterns. Remediation: tune `PintleDiameter_mm` / `PintleSleeveHoleCount`. |
| `PINTLE_TMR_OUT_OF_BAND` | Pintle total momentum ratio `TMR = (ṁ_f v_f)/(ṁ_ox v_ox)` outside [0.2, 4.0] | Dressler / TRW mixing-quality band (log-symmetric around 1.0). Pintle-only. Remediation: tune main-chamber MR or injector ΔP toward TMR ≈ 1.0. |
| `BLOW_DOWN_INSUFFICIENT` | End-of-burn predicted chamber pressure < target even though start-of-burn stackup is feasible | Pressure-fed blow-down mode only (`OperatingConditions.BlowDownFinalPressure_Pa > 0`). Classic blow-down failure mode. Regulated pressure-fed designs and non-pressure-fed cycles skip the gate. Remediation: raise final tank pressure (smaller initial ullage volume) or switch to a regulated-pressure feed. |
| `EXPANDER_TURBINE_ENTHALPY_DEFICIT` | Coolant enthalpy picked up in regen jacket insufficient to drive pump shaft power on expander cycles | Expander cycles only (`OpenExpander` / `ClosedExpander`). Only evaluated when `RegenGenerationResult.ExpanderTurbine` is populated (cycle is expander-family AND jacket absorbed heat). Remediation: raise jacket ΔT (smaller channel / more flow / longer chamber), raise jacket outlet pressure, or switch to a preburner cycle. |
| `ORSC_PREBURNER_OXCORROSION` | Ox-rich preburner peak wall T > (material service limit − 50 K) | ORSC cycle only (`EngineCycle.ORSC`). Tighter margin than `PREBURNER_WALL_TEMP` because ox-rich combustion accelerates metal-oxidation. RD-180-class hardware runs turbine inlet ~1050 K vs fuel-rich ~1100 K. Only evaluated when `OxidizerPreburner.Thermal` is populated (opt-in preburner-cooling solver) AND cycle is ORSC. Remediation: raise preburner cooling, lower preburner Pc / MR, or switch to a corrosion-resistant alloy (Cu-coated Inconel 718 / NiCrAl heritage). |
| `TAPOFF_HOT_GAS_TOO_HOT` | Tap-point T (~35 % of chamber Tc on a fuel-film-cooled boundary) exceeds uncooled-wheel limit (~1100 K) | Tap-off cycle only (`EngineCycle.TapOff`). J-2S / BE-4 heritage: tapped chamber gas drives the turbine. Only evaluated when `RegenGenerationResult.TapOffTurbine` is populated. Remediation: lower chamber Pc, boost film-cooling fraction, or switch to a preburner / expander cycle. |
| `OVERHANG_ANGLE_EXCEEDED` | One or more surface patches overhang below the material-specific angle floor (IN718 35°, GRCop / IN625 40°, CuCrZr / 316L 45°) | LPBF printability screen, Sprint 27. Only evaluated when `RegenChamberDesign.IncludeLpbfPrintabilityAnalysis` is true. Remediation: add sacrificial supports, re-orient the build (see advisor output on `Printability.Orientation`), or soften the steepest slopes. |
| `TRAPPED_POWDER_REGION` | At least one closed void pocket cannot evacuate powder to any external surface or configured opening | LPBF printability screen, Sprint 27. Only fires on opted-in designs with a voxel snapshot attached to the printability result (the fast SA path skips the flood-fill — opt in at STL-export time). Remediation: add a drain port, reroute the passage, or re-orient the build. One violation per connected component. |
| `DRAIN_PATH_MISSING` | At least one plumbing branch is a dead-end or belongs to a subgraph isolated from every external port | LPBF printability screen, Sprint 27. Only evaluated on opted-in designs. Remediation: wire the dead-end to an external port, remove the branch, or add a drain tap. |
| `INSTRUMENTATION_TAP_INTERFERENCE` | A sensor boss clashes with a cooling channel (arc distance < boss bore radius + channel half-width + clearance) or with another boss (arc spacing < OD sum + clearance) | Hot-fire-readiness screen, Sprint 28. One violation per offender. Channel check runs only on `ChannelTopology = Axial`; helical / TPMS / aerospike skip it conservatively. Boss-vs-boss is topology-agnostic. Silent on every pre-Sprint-28 design (empty sensor-boss list short-circuits). Remediation: shift boss toward mid-rib, reduce channel count, or move to the pre-manifold region / opposite side of the chamber. |
| `CONTRACTION_RATIO_OUT_OF_BAND` | Contour ε_c outside [2.5, 10.0] | Sprint 36 / PH-17. Sutton §8.2 / Huzel & Huang §4.1: below 2.5 → chamber Mach > 0.2 with combustion-instability risk; above 10 → wasted wall area + cooling-surface bloat. Topology-agnostic (reads `Contour.ContractionRatio`). Remediation: tune `ContractionRatio` toward [3, 8]. |
| `CHANNEL_ASPECT_RATIO_EXCEEDED` | Any regen-channel station has depth/width > 8 (warn) or > 10 (strict) | Sprint 36 / PH-23. LPBF rib-buckling threshold per EOS / Wolfram process maps. One violation per design (worst station) to avoid spam. Skipped on TPMS topologies and ablative-only. Remediation: lower channel height or raise channel count. |
| `G_INJ_TOO_LOW` / `G_INJ_TOO_HIGH` | Injector mass flux ṁ_total / A_total outside [140, 500] kg/(m²·s) | Sprint 36 / PH-21. Sutton §6.3 / Yang LPCI §5: below floor → chug instability; above ceiling → over-mix / face-burnout. Only evaluated when `InjectorSizing` is populated (sized, implemented element pattern). Remediation: tune orifice count or diameters. |
| `L_STAR_BELOW_PROPELLANT_MIN` | Contour L\* < 95 % of pair nominal (LOX/CH4 = 1.10 m, LOX/H2 = 0.90 m, LOX/RP-1 = 1.20 m) | Sprint 36 / PH-11. Real engines below this floor lose 2-5 % on C\* — the η_C\* default (~0.95) does not capture the penalty. Pair nominals from `AutoSeeder.CharacteristicLengthFor`. Remediation: raise `CharacteristicLength_m` or accept a less-aggressive chamber-volume target. |
| `INSTRUMENTATION_THERMAL_BRIDGE_RISK` | Sensor boss in a station with q\" > 80 % of peak gas-side flux AND wall material conductivity differs sharply (delta > 50 %) from a typical 16 W/m·K stainless-boss assumption | Sprint 36 / PH-22. CuCrZr (k ≈ 300 W/m·K) and GRCop-42 (k ≈ 305 W/m·K) walls trigger on every high-flux boss; Inconel walls don't. Conservative: voxelforge does not yet surface per-boss material; assumes 316L LPBF default. Remediation: move boss out of the throat region or specify a matching alloy. |
| `TURBINE_UNCHOKED` | Turbine stator throat does not choke: π = p_out / p_in > π_crit = (2/(γ+1))^(γ/(γ-1)) | Sprint 34a / PH-26. Sutton §10.4. Subsonic flow on a supersonic-stator wheel collapses the assumed η ≈ 0.55-0.60 to ~0.30. Each non-null sized turbine result on the design is checked independently; one violation per unchoked stage. Tap-off cycle is the most exposed (low-Pc designs discharging to ambient may not choke); closed-expander is also at risk because jacket ΔP is modest. Remediation: raise inlet pressure, lower back-pressure, or switch cycles. |
| `PUMP_SPECIFIC_SPEED_OFF_BAND` | Pump N_s = rpm · √Q_gpm / H_ft^0.75 outside [600, 9000] (US units) | Sprint 34b (minimum viable) / PH-8. Karassik §2.5 / Stepanoff §2.7. Below 600 → axial-flow regime where centrifugal-pump similarity math no longer holds; above 9000 → multi-stage / mixed-flow territory beyond the single-stage model. Each non-null pump on the design is checked independently. Pre-Sprint-34b every design reported the constant N_s = 2500 silently; post-fix users opt into RPM-as-input via `RegenChamberDesign.PumpRpm_rpm` and the gate fires on out-of-band diagnostic N_s. Auto-derive (PumpRpm_rpm = 0) keeps N_s ≈ 2500 by construction so legacy designs remain silent. Remediation: tune `PumpRpm_rpm` toward the 1500-12000 rpm range typical for LRE-class centrifugal pumps. |
| `PUMP_PRESSURE_INVERTED` | Pump discharge ≤ inlet pressure (feed wired backwards / override too low) | Post-Tier-1 feed-integrity bundle. Fires on any turbopump cycle. |
| `BURST_MARGIN_INSUFFICIENT` | Elastic burst margin < ASME §VIII proof-test threshold | Only fires when a burst-pressure target is set on the design. |
| `COMMON_SHAFT_RPM_INCONSISTENT` | Common-shaft cycle: fuel vs ox pump RPM discrepancy > 0.5 % | Common-shaft turbopump cycles only. |
| `TPMS_AND_MANIFOLD_OVERLAP` | TPMS topology + 2× manifold span ≥ chamber length (overlap risk) | TPMS topologies only; geometric overlap screen. |
| `BIMETALLIC_BOND_ZONE_SHEAR` | Bond-zone shear τ > σ_y · 0.5 (CTE-mismatch crack risk) | Bimetallic builds (e.g. GRCop liner + Inconel jacket). |
| `LCF_LIFE_INSUFFICIENT` | Predicted LCF cycles < SF × mission cycle demand | Low-cycle-fatigue life vs mission reuse demand. |
| `ACOUSTIC_DAMPER_DETUNED` | Damper f₀ outside ±tuning-band of any chamber mode *(advisory)* | Acoustic-cavity / Helmholtz damper designs only. |
| `ACOUSTIC_DAMPER_OVERSIZED` | Damper resonator count > 16 (sub-22.5° azimuthal pitch) *(advisory)* | Acoustic-cavity damper designs only. |
| `EXPANSION_DEFLECTION_PLUG_CLEARANCE` | E-D cowl throat radius < 12 mm advisory floor *(advisory)* | Expansion-deflection (E-D) nozzles only. |
| `TOPOLOGY_CHANNEL_NOT_PRINTABLE` | SIMP topology channel width < LPBF feature floor *(advisory)* | SIMP topology-optimised channel designs only. |
| `COMBINED_AXIAL_BENDING_INSUFFICIENT` | Peak combined axial-bending σ_VM > σ_y / 1.5 | Fires when gimbal offset > 0 (combined axial + bending load). |
| `TRANSPIRATION_BLEED_EXCESSIVE` | Transpiration bleed fraction > 0.15 of coolant flow *(advisory)* | Transpiration-cooled designs only. |
| `ABLATIVE_THROAT_RECESSION_EXCEEDS_BUDGET` | Ablative penetration × SF > initial thickness before end-of-burn | Ablative-throat designs; recession-budget check. |
| `ABLATIVE_REGEN_INTERFACE_OVERTEMP` | Regen peak wall T > ablative char temperature *(advisory)* | Ablative-throat + regen hybrid; interface temperature. |
| `FINITE_RATE_ISP_PENALTY_LARGE` | Finite-rate Isp factor < 0.985 (> 1.5 % penalty) *(advisory)* | Finite-rate-chemistry Isp-penalty screen. |
| `RDE_ANNULUS_FILL_STARVED` | RDE annulus fill time > inter-wave period (starved between waves) | Rotating-detonation-engine chamber designs only. |
| `RDE_WAVE_COUNT_BELOW_MINIMUM` | RDE estimated wave count < 2 (single-wave / deflagration) *(advisory)* | Rotating-detonation-engine chamber designs only. |

## The 5 aerospike-parallel gates

Fire when `ChannelTopology = Aerospike` or `LinearAerospike`. All live in
[`AerospikeFeasibility.cs`](../../Voxelforge.Core/Geometry/AerospikeFeasibility.cs).

| ID | Fires when |
|---|---|
| `AEROSPIKE_PLUG_WALL_TEMP` | Plug-body peak wall T > material service limit |
| `AEROSPIKE_COOLANT_CAVITATION_RISK` | Plug-coolant local pressure drops into the saturation pocket |
| `AEROSPIKE_ELEMENT_CLEARANCE` | Injector elements don't fit the pitch-circle / arc-spacing geometry |
| `AEROSPIKE_INJECTOR_FACE_TEMP` | Aerospike-injector face T > material service limit (parallel to `INJECTOR_FACE_T_EXCEEDED`) |
| `LINEAR_AEROSPIKE_ASPECT_RATIO` | Linear-plug aspect ratio (length / transverse width) outside [0.30, 5.00]. X-33 XRS-2200 heritage — below floor: side-wall recirculation dominates; above ceiling: plug becomes an unmanageable long-span cantilever at LPBF scale. Fires only when `Contour.IsLinear == true`. |

## The 2 monolithic gates

Fire when multiple subsystems (chamber + turbopump + feed manifold ±
aerospike plug) are fused into a single printable STL. Live in
[`MonolithicFeasibility.cs`](../../Voxelforge.Voxels/Geometry/MonolithicFeasibility.cs).

| ID | Fires when |
|---|---|
| `MONOLITHIC_BODY_INTERSECTION` | Two voxel bodies overlap (e.g. a turbopump casing clipping the chamber shell) |
| `MONOLITHIC_TUBE_INTERSECTION` | Two routed tubes (feed lines, purge lines, coolant return) interfere with each other |

## The 2 monopropellant + 1 voxel-adequacy gates

The monopropellant gates fire only on `EngineCycle.Monopropellant`
designs (`Voxelforge.Core/Optimization/MonopropGates.cs`); the
voxel-adequacy gate is evaluated at the optimiser level — it depends on
the requested session voxel size, not the design
(`Voxelforge.Core/Optimization/RegenChamberOptimization.cs`).

| ID | Fires when |
|---|---|
| `MONOPROP_CATALYST_OVERLOADED` | Catalyst loading > spec catalyst-loading limit (kg·m⁻²·s⁻¹) |
| `MONOPROP_CHAMBER_TEMP_EXCEEDS_BED` | Chamber Tc > 1700 K Ir/Al₂O₃ catalyst-bed service limit |
| `VOXEL_RESOLUTION` | Worst feature size < 2× the session voxel (2/3-voxel implicit-fidelity rule) |

---

## Air-breathing pillar — 40 gates

`Voxelforge.Airbreathing.Core/AirbreathingFeasibility.cs` (36) plus the
`Optimization/AirbreathingGates.cs` registry (the final 4: `PULSEJET_*`,
`AFTERBURNER_LINER_OVERTEMP`, `TURBOPROP_SHAFT_POWER_INSUFFICIENT`).
Kind-predicated across ramjet / turbojet (± afterburner) / turbofan /
scramjet / RBCC / gas-turbine / Rankine steam / pulsejet / turboprop /
turboshaft / LACE / RDE. Advisory gates marked *(advisory)*.

| ID | Fires when |
|---|---|
| `COMBUSTOR_BLOWOUT_LEAN` | Equivalence ratio φ < 0.20 lean blowout floor |
| `COMBUSTOR_BLOWOUT_RICH` | Equivalence ratio φ > 1.5 rich blowout limit |
| `INLET_UNSTART` | Inlet pressure recovery π_d < 0.50 floor (ramjet / turbojet / turbofan) |
| `T_T4_EXCEEDS_LIMIT` | Combustor exit stagnation T_t4 > 2200 K uncooled limit |
| `NOZZLE_INSUFFICIENT_DRIVE_PRESSURE` | Nozzle exit Mach NaN (P_t9 ≤ ambient, no expansion) |
| `THERMAL_CHOKING` | Combustor exit Mach M_4 > 0.7 (Rayleigh choking proxy) |
| `OVERHANG_ANGLE_EXCEEDED` | Printability overhang below material min unsupported angle |
| `TRAPPED_POWDER_REGION` | Printability unevacuable powder pocket detected |
| `DRAIN_PATH_MISSING` | Printability dead-end / isolated plumbing (no drain path) |
| `COMPRESSOR_RATIO_OUT_OF_BAND` | Compressor π_c < 2.0 or > 50.0 (turbojet / turbofan) |
| `TIT_EXCEEDED` | Turbine inlet T_t4 > 1700 K uncooled / 2200 K cooled |
| `CORRECTED_MASS_FLOW_OUT_OF_MAP` | Compressor / turbine past surge or choke margin on the map |
| `SURGE_MARGIN_INSUFFICIENT` | Compressor surge margin < 0.10 floor *(advisory)* |
| `ISOLATOR_UNSTART` | Scramjet isolator recovery π_iso < 0.30 floor |
| `COMBUSTION_EFFICIENCY_BELOW_FLOOR` | φ ≥ 0.4 and T_t4/T_t3 < 1.2 (near-quench) |
| `STATIC_T_T_RATIO_OUT_OF_BAND` | Scramjet T_t4/T_t3 > 6.0 (near thermal choke) |
| `BYPASS_RATIO_OUT_OF_BAND` | BPR < 0.10 or > spool-dependent ceiling (2.0 / 8.0) |
| `BYPASS_MIXER_ENTHALPY_IMBALANCE` | Mixer energy-balance residual fraction > 0.005 |
| `FAN_STALL` | Effective fan pressure ratio π_fan > 1.9 stall floor *(advisory)* |
| `BYPASS_DUCT_CHOKED` | Bypass-duct exit Mach M_16 > 0.9 choke floor |
| `RBCC_MODE_OUT_OF_ENVELOPE` | DuctedRocket mode at M > 2.5, or Scramjet mode at M < 4.0 |
| `STEAM_CONDENSE_BELOW_VACUUM` | Condenser pressure < 0.01 bar minimum |
| `GAS_TURBINE_NET_WORK_NEGATIVE` | Net shaft work W_turb − W_comp ≤ 0 |
| `GAS_TURBINE_EFFICIENCY_BELOW_FLOOR` | Cycle thermal efficiency η_th < 0.25 *(advisory)* |
| `GAS_TURBINE_RECUPERATOR_OVERTEMPERATURE` | Recuperator on and turbine-exit T < compressor-exit T (reverse flow) *(advisory)* |
| `LACE_PRECOOLER_EFFECTIVENESS_LOW` | Precooler effectiveness ε < 0.70 hard min |
| `LACE_AIR_LIQUEFACTION_INSUFFICIENT` | Precooler air-outlet T_t2 > 95 K liquefaction target |
| `LACE_AIR_TO_FUEL_OUT_OF_BAND` | Air-to-fuel ratio < 2.0 or > 50.0 hard band |
| `LACE_CHAMBER_PRESSURE_OUT_OF_BAND` | Chamber pressure < 20 bar or > 250 bar hard band |
| `LACE_AIR_TO_FUEL_OUT_OF_ADVISORY` | Air-to-fuel in hard band but < 5.0 or > 35.0 *(advisory)* |
| `LACE_PRECOOLER_FROST_LINE_RISK` | Precooler outlet T_t2 in 95–220 K frost-line band *(advisory)* |
| `RDE_PRESSURE_GAIN_OUT_OF_BAND` | Pressure-gain ratio < 1.0 or > 1.50 hard band |
| `RDE_WAVE_COUNT_OUT_OF_BAND` | Wave count < 1 or > 10 hard band |
| `RDE_CHANNEL_WIDTH_BELOW_CELL_SIZE` | Annular channel width (D_o − D_i)/2 < 0.001 m hard min |
| `RDE_CHANNEL_WIDTH_ABOVE_ADVISORY` | Channel width ≥ floor but > 0.020 m advisory max *(advisory)* |
| `RDE_LENGTH_TO_DIAMETER_OUT_OF_BAND` | Annulus L/D < 0.20 or > 4.0 band *(advisory)* |
| `PULSEJET_BLOWOUT_LEAN` | Pulsejet (non-H₂) fuel-air mass fraction < 0.030 lean limit |
| `PULSEJET_ACOUSTIC_OVERPRESSURE` | Pulsejet Humphrey P_peak/P_steady > 1.30 ceiling *(advisory)* |
| `AFTERBURNER_LINER_OVERTEMP` | Afterburner on and exit T_t7 > Inconel 625 liner limit |
| `TURBOPROP_SHAFT_POWER_INSUFFICIENT` | Turboprop propeller power-extraction fraction < 0.50 |

One further ID, `RBCC_TRANSITION_THRUST_GAP`, is reserved in source but is
comment-only in Phase 1 (always-pass, emits no violation), so it is **not**
counted in the 40.

---

## Electric-propulsion pillar — 54 gates

`Voxelforge.ElectricPropulsion.Core/ElectricPropulsionFeasibility.cs`.
Every gate is predicated on the thruster family; there is no shared group.
Advisory gates marked *(advisory)*.

### Resistojet (10)

| ID | Fires when |
|---|---|
| `RESISTOJET_HEATER_TEMP_EXCEEDED` | Heater T > material limit (Pt 2500 K / WRe 2800 K) |
| `RESISTOJET_RADIATION_FRACTION_EXCESSIVE` | Radiation loss fraction > 0.50 |
| `RESISTOJET_NOZZLE_UNCHOKED` | Flow not choked (sub-critical) |
| `RESISTOJET_PROPELLANT_DECOMPOSITION` | Chamber T > inlet-mixture decomposition limit |
| `RESISTOJET_HEAT_LEAK_EXCEEDS_INPUT` | Radiation loss fraction ≥ 1.0 (losses ≥ input power) |
| `RESISTOJET_AREA_RATIO_OUT_OF_BAND` | Nozzle area ratio < 25 or > 150 *(advisory)* |
| `RESISTOJET_THRUST_BELOW_MIN` | Thrust < 0.05 N *(advisory)* |
| `RESISTOJET_ISP_BELOW_FLOOR` | Vacuum Isp < 200 s *(advisory)* |
| `RESISTOJET_EFFICIENCY_BELOW_FLOOR` | Thrust efficiency < 0.65 *(advisory)* |
| `RESISTOJET_FROZEN_FLOW_LOSS_EXCESSIVE` | Chamber T > 2500 K with N/H mole fraction > 0.01 *(advisory)* |

### Hall-effect thruster (6)

| ID | Fires when |
|---|---|
| `HET_DISCHARGE_VOLTAGE_OUT_OF_BAND` | Discharge voltage < 100 V or > 1000 V |
| `HET_ANODE_OVERHEAT` | Anode wall T > material limit (BN 1500 / AluminaSiC 1900 / graphite 2000 K) |
| `HET_MAGNETIC_FIELD_INSUFFICIENT` | Magnetic field < 0.005 T |
| `HET_PLUME_DIVERGENCE_EXCESSIVE` | Plume divergence half-angle > 0.524 rad (~30°) *(advisory)* |
| `HET_CATHODE_LIFE_LIMIT` | Discharge current > 1.2× cathode rating *(advisory)* |
| `HET_MASS_UTILIZATION_LOW` | Mass utilization < 0.85 *(advisory)* |

### Arcjet (4)

| ID | Fires when |
|---|---|
| `ARCJET_VOLTAGE_OUT_OF_BAND` | Arc voltage < 40 V or > 400 V |
| `ARCJET_ANODE_OVERHEAT` | Anode wall T > material limit (Mo 2890 / Re 3460 / W 3650 K) |
| `ARCJET_THERMAL_EFFICIENCY_LOW` | Thermal efficiency < 0.25 *(advisory)* |
| `ARCJET_FROZEN_FLOW_LOSS_EXCESSIVE` | Chamber T > 4500 K *(advisory)* |

### Pulsed plasma thruster (4)

| ID | Fires when |
|---|---|
| `PPT_CAPACITOR_ENERGY_OUT_OF_BAND` | Capacitor energy < 0.5 J or > 50 J |
| `PPT_NO_BREAKDOWN` | Capacitor energy < 1.0 J (no stable arc) |
| `PPT_IMPULSE_BIT_BELOW_FLOOR` | Impulse bit < 100 µN·s *(advisory)* |
| `PPT_ABLATION_RATE_EXCESSIVE` | Mass per pulse > 2e-7 kg *(advisory)* |

### Gridded ion thruster (5)

| ID | Fires when |
|---|---|
| `GIT_BEAM_VOLTAGE_OUT_OF_BAND` | Beam voltage < 200 V or > 12000 V |
| `GIT_PERVEANCE_LIMIT_EXCEEDED` | Requested beam current > Child-Langmuir limit |
| `GIT_NEUTRALIZER_CURRENT_MISMATCH` | \|J_neut − J_beam\| / J_beam > 0.10 |
| `GIT_PLUME_DIVERGENCE_EXCESSIVE` | Plume divergence half-angle > 0.524 rad (~30°) *(advisory)* |
| `GIT_GRID_LIFETIME_BELOW_FLOOR` | Estimated grid life < 1000 h *(advisory)* |

### Magnetoplasmadynamic (7)

| ID | Fires when |
|---|---|
| `MPD_ARC_CURRENT_OUT_OF_BAND` | Arc current < 200 A or > 10000 A |
| `MPD_CATHODE_OVERHEAT` | Cathode wall T > material limit (LaB6 2200 / ThW 3200 / W 3700 K) |
| `MPD_GEOMETRY_INVERTED` | Anode radius ≤ cathode radius |
| `MPD_APPLIED_FIELD_OUT_OF_BAND` | Finite applied B < 0.05 T or > 0.50 T |
| `MPD_ONSET_PARAMETER_EXCESSIVE` | Onset ξ = J²/ṁ > 120 (80% of 150) *(advisory)* |
| `MPD_THRUST_EFFICIENCY_LOW` | Maecker thrust efficiency < 0.05 *(advisory)* |
| `MPD_APPLIED_FIELD_DOMINATES` | T_af / (T_self + T_af) > 0.80 *(advisory)* |

### FEEP (6)

| ID | Fires when |
|---|---|
| `FEEP_ACCELERATING_VOLTAGE_OUT_OF_BAND` | Accelerating voltage < 5000 V or > 12000 V |
| `FEEP_EMITTER_TIP_RADIUS_OUT_OF_BAND` | Emitter tip radius < 0.001 mm or > 0.050 mm |
| `FEEP_BEAM_CURRENT_OUT_OF_BAND` | Beam current < 1e-6 A or > 1e-3 A |
| `FEEP_TOTAL_POWER_EXCEEDS_BUS` | V_acc·I_beam > available bus power (bus > 0) |
| `FEEP_TIP_FIELD_BELOW_FN_THRESHOLD` | Emitter tip field < 1e9 V/m *(advisory)* |
| `FEEP_THRUST_BELOW_FLOOR` | Thrust > 0 but < 1 µN *(advisory)* |

### Helicon double-layer thruster (6)

| ID | Fires when |
|---|---|
| `HDLT_RF_POWER_BELOW_IONIZATION_THRESHOLD` | Helicon RF power < 50 W |
| `HDLT_DOUBLE_LAYER_TOO_WEAK` | Double-layer strength < 5 V |
| `HDLT_CHANNEL_GEOMETRY_INSUFFICIENT` | Integrated ∇B·L < 0.5 T |
| `HDLT_TOTAL_POWER_EXCEEDS_BUS` | Helicon RF power > available bus power (bus > 0) |
| `HDLT_PLUME_DIVERGENCE_EXCESSIVE` | Plume divergence half-angle > 0.70 rad (~40°) *(advisory)* |
| `HDLT_IONIZATION_FRACTION_LOW` | Ionisation fraction < 0.01 *(advisory)* |

### VASIMR (6)

| ID | Fires when |
|---|---|
| `VASIMR_TOTAL_POWER_EXCEEDS_BUS` | P_helicon + P_icrh > available bus power (bus > 0) |
| `VASIMR_SOLENOID_FIELD_OUT_OF_BAND` | Solenoid field < 0.3 T or > 6.0 T |
| `VASIMR_MAGNETIC_MIRROR_INVERTED` | Magnetic mirror ratio < 1.0 |
| `VASIMR_HELICON_TO_ICRH_RATIO_OUT_OF_BAND` | Helicon power fraction < 0.05 or > 0.50 *(advisory)* |
| `VASIMR_IONIZATION_FRACTION_LOW` | Ionisation fraction < 0.50 *(advisory)* |
| `VASIMR_NOZZLE_CONVERSION_LOW` | Nozzle conversion efficiency < 0.30 *(advisory)* |

---

## Marine pillar — 22 gates

IDs declared in `Voxelforge.Marine.Core/Optimization/MarineConstraintIds.cs`,
evaluated in `Optimization/MarineGates.cs` — 9 hard, 13 advisory.
Families: pressure-hull (10), planing (Savitsky, 6), displacement /
semi-displacement (Holtrop, 6). Advisory gates marked *(advisory)*.

| ID | Fires when |
|---|---|
| `HULL_BUOYANCY_NEGATIVE` | Net buoyant weight < 0 |
| `HULL_BUCKLING_INSUFFICIENT` | Buckling safety factor < 1.5 (ASME UG-28) |
| `HULL_WATERTIGHT_INTEGRITY` | Wall thickness < 0.0015 m (LPBF min feature) |
| `DEPTH_RATING_EXCEEDED` | Max operating depth > hull depth rating |
| `HULL_FINENESS_EXTREME` | Fineness ratio < 4.0 or > 15.0 hard band |
| `HULL_DRAG_ABOVE_BAND` | Drag coefficient > 0.20 slender-body ceiling *(advisory)* |
| `HULL_FINENESS_OUT_OF_BAND` | Fineness in hard band but < 5.0 or > 12.0 optimum band *(advisory)* |
| `HULL_CG_CB_OFFSET_LARGE` | CG-CB offset > 5% of diameter *(advisory)* |
| `HULL_LPBF_WALL_TOO_THIN` | Wall thickness in 0.0015–0.0020 m advisory band *(advisory)* |
| `HULL_BUCKLING_SF_MARGINAL` | Buckling SF in 1.5–2.0 marginal band *(advisory)* |
| `PLANING_SPEED_COEFFICIENT_OUT_OF_BAND` | Savitsky C_v < 1.0 or > 13.0 |
| `PLANING_TRIM_OUT_OF_BAND` | Trim angle < 1.0° or > 10.0° |
| `PLANING_WETTED_LENGTH_TO_BEAM_OUT_OF_BAND` | Savitsky λ < 0.8 or > 6.0 |
| `PLANING_DEADRISE_OUT_OF_BAND` | Deadrise angle < 5.0° or > 25.0° *(advisory)* |
| `PLANING_LCG_OUT_OF_BAND` | Longitudinal CG fraction < 0.42 or > 0.58 *(advisory)* |
| `PLANING_RESISTANCE_ABOVE_BAND` | Resistance coefficient > 0.20 *(advisory)* |
| `HOLTROP_FROUDE_OUT_OF_BAND` | Froude < 0.05 or > 0.40 (0.55 with semi-displacement correction) |
| `HOLTROP_SEMI_DISPLACEMENT_REGIME` | SD correction on and 0.30 < Froude ≤ 0.55 *(advisory)* |
| `HOLTROP_LENGTH_TO_BEAM_OUT_OF_BAND` | L/B < 4.0 or > 12.0 *(advisory)* |
| `HOLTROP_BEAM_TO_DRAFT_OUT_OF_BAND` | B/T < 1.5 or > 5.0 *(advisory)* |
| `HOLTROP_FORM_FACTOR_ABOVE_BAND` | Form factor (1+k₁) > 1.30 *(advisory)* |
| `HOLTROP_WAVE_MAKING_DOMINANT` | Wave-making / total resistance > 0.60 *(advisory)* |

---

## Nuclear pillar — 15 gates

IDs in `Voxelforge.Nuclear.Core/NuclearConstraintIds.cs`, evaluated in
`Optimization/NuclearGates.cs` — 7 hard, 8 advisory. Covers NERVA-class
NTR core / fuel-pin thermal-hydraulics and the bimodal NTR-Brayton power
tap. Advisory gates marked *(advisory)*.

| ID | Fires when |
|---|---|
| `NTR_REACTOR_OVERTEMP` | Core exit T > 3000 K (UO2-cermet centerline limit) |
| `NTR_THERMAL_FLUX_EXCEEDED` | Volumetric heat flux > per-enrichment-tier ceiling (HEU 4000 MW/m³) |
| `NTR_CHAMBER_PRESSURE_TOO_LOW` | Chamber pressure < 30 bar (regen jacket inlet floor) |
| `NTR_K_EFF_OUT_OF_BAND` | k_eff < 0.99 or > 1.05 criticality band *(advisory)* |
| `NTR_FUEL_CTE_MISMATCH` | Fuel loading fraction > 0.80 (CTE mismatch) *(advisory)* |
| `NTR_REGEN_COOLING_BUDGET` | Peak gas-side wall T above Inconel 718 limit *(advisory)* |
| `NTR_FUEL_PIN_OVERTEMP` | Peak fuel centerline T > per-material limit (UO2-cermet 3200 K) |
| `NTR_FUEL_PIN_SURFACE_OVERTEMP` | Pin surface T > 2800 K (chemical-compatibility limit) |
| `NTR_HOT_CHANNEL_FACTOR_EXCESSIVE` | Hot-channel factor > 1.80 *(advisory)* |
| `NTR_PER_PIN_POWER_ABOVE_BAND` | Per-pin power > 2400 W (NERVA-class ceiling) *(advisory)* |
| `NTR_PIN_PITCH_RATIO_OUT_OF_BAND` | Pitch/diameter < 1.05 or > 1.80 *(advisory)* |
| `NTR_BIMODAL_BRAYTON_TURBINE_OVERTEMP` | Brayton turbine inlet T > 1500 K (refractory-blade limit) |
| `NTR_BIMODAL_ALTERNATOR_RPM_OUT_OF_BAND` | Alternator RPM < 10000 or > 100000 |
| `NTR_BIMODAL_BRAYTON_THERMAL_EFFICIENCY_LOW` | Brayton thermal efficiency < 0.15 *(advisory)* |
| `NTR_BIMODAL_REACTOR_TAP_EXCESSIVE` | Reactor-power-to-Brayton tap ratio > 0.95 *(advisory)* |

---

## How gates compose with scoring

```
SA proposes candidate
       │
       ▼
Physics solvers run (Bartz, Dittus-Boelter, pump sizing, stability, …)
       │
       ▼
FeasibilityGate.Evaluate(gen)  ◄──  collects ALL violations, not fail-fast
       │
       ├── violations.Length > 0  →  score = +∞         →  SA rejects
       │
       └── violations.Length == 0 →  score = weighted sum →  SA compares
```

Soft-threshold gates (`CHILLDOWN_BUDGET_EXCEEDED`, `HARD_START_RISK`)
mean the *threshold* is user-supplied rather than physics-absolute —
the gate mechanism itself is the same hard reject.

## Uncertainty caveats

The gates carry the uncertainty bands of the solvers they screen on.
Canonical bands documented in [`PHYSICS.md`](PHYSICS.md):

- Wall T: ±25–50 % (Bartz baseline). `WALL_TEMP`, `PREBURNER_WALL_TEMP`, `INJECTOR_FACE_T_EXCEEDED`, `AEROSPIKE_*_WALL_TEMP` inherit this.
- Coolant ΔP: ±20 % (Dittus-Boelter / Petukhov).
- Structural safety factor: pressure-vessel model; ±15 % on peak stress.

Passing every gate means "this design is not obviously broken in the
ways we know how to check." It does not mean "this design will fly."
See [`LIMITATIONS.md`](LIMITATIONS.md) for the full honest list of what
these gates do not cover.
