# ADR-009 — Feasibility gate discipline

**Status:** Accepted
**Date:** 2026-04-21 (documented; discipline established earlier in the project lifecycle). Gate inventory has grown organically as new failure modes are identified — see the project sprint history for which sprint added which gate. The 2026-04-22 pre-production audit restored `ELEMENT_DENSITY_TOO_HIGH` to the enumerated regen list.

> **Founding gate count: 38; current rocket-pillar count: 65** (the
> project-wide total is 196 across five pillars — see the census note
> later in this ADR and `docs/GATES.md`). The 38 founding gates are
> enumerated below for historical traceability; later additions live in
> the project sprint history and `docs/GATES.md`, the live SSOT for the
> full inventory. See also [ADR-019](ADR-019-gate-registry.md) for how
> gates are now registered (declarative `GateRegistry`, not an if-chain
> in `FeasibilityGate.cs`).

## Context

The optimizer (ADR-003) needs a way to reject candidate designs that are
infeasible — violating a physical, manufacturing, or safety constraint —
without contaminating the scoring function. A weighted-sum score that
includes "maybe-this-is-okay" penalty terms for infeasibility makes the
optimizer indistinguishable between "slightly penalised feasible design"
and "clearly infeasible design."

## Decision

**Every hard feasibility constraint is a named gate registered in the
`GateRegistry`** ([ADR-019](ADR-019-gate-registry.md)). The evaluator
collects violations from ALL gates (no fail-fast), returns a structured
`FeasibilityViolation[]`, and the optimizer scoring path returns `+∞` on
any violation.

Gates are declared via `GateRegistry.Register(descriptor)` calls in
`Voxelforge.Core/Optimization/RocketGates.cs` (regen path) and
`Voxelforge.Core/Geometry/AerospikeFeasibility.cs` (aerospike-parallel
path). The former 1,150-line `FeasibilityGate.Evaluate()` if-chain was
replaced by a thin registry loop in ADR-019 (PR #281, 2026-04-29).

Founding gate inventory (38, at the time this ADR was written): 31 regen + 5 aerospike-parallel + 2 monolithic.

**Regen-path gates (31):**

1. `WALL_TEMP` — peak gas-side wall T > material service limit.
2. `YIELD_EXCEEDED` — min SF < 1.0 on any station.
3. `FEATURE_TOO_SMALL` — any feature < 0.30 mm LPBF floor.
4. `COOLANT_T_EXCEEDED` — coolant bulk T > fluid service limit.
5. `STABILITY_FAIL` — composite stability traffic-light = Fail.
6. `ELEMENT_DENSITY_TOO_HIGH` — injector element density > 0.7 elements/cm² of chamber face area (Huzel & Huang §8.2 face-plate burnout rule). Only evaluated when an implemented `InjectorPattern` is set (`gen.InjectorSizing != null`). Remediation: fewer elements / larger chamber / multi-row pattern.
7. `VOXEL_RESOLUTION` — voxel-adequacy gate 2/3-voxel rule. Evaluated in `RegenChamberOptimization.Evaluate` (not in `FeasibilityGate.cs`) because it depends on the requested voxel size, which is an optimizer-level setting rather than a design-level one.
8. `INJECTOR_FACE_T_EXCEEDED` — predicted injector face T > wall material service limit. Only fires when the `InjectorFaceThermal` estimate is populated.
9. `FEED_PRESSURE_INSUFFICIENT` — feed stackup can't sustain target Pc.
10. `IGNITER_ENERGY_INSUFFICIENT` — Addendum §21 + Sprint 29 (2026-04-24). Rated energy of the selected <see cref="Geometry.IgniterType"/> preset < the propellant-pair-specific floor from <see cref="Combustion.IgnitionRequirements"/>. Per-pair floors: LOX/CH4 50 mJ, LOX/H2 5 mJ, LOX/RP-1 500 mJ (kerosene atomisation is slow, spark-torch rated 150 mJ is marginal per Huzel & Huang §7.2). Hypergolic pairs (N2O4/MMH, H2O2/RP-1 catalyst start) short-circuit this gate. Replaces the pre-Sprint-29 universal 50 mJ JANNAF floor, which was right for LOX/CH4 but wrong for kerosene and overly strict for hydrogen.
11. `PURGE_FLOW_INSUFFICIENT` — per-port (one violation per failing port).
12. `ABLATIVE_BURNTHROUGH` — ablative recession + char > thickness × SF.
13. `CHILLDOWN_BUDGET_EXCEEDED` — chilldown time > budget.
14. `HARD_START_RISK` — start-transient Pc overshoot above threshold.
15. `NPSH_INSUFFICIENT` — pump NPSHA < NPSHR.
16. `TPMS_CELL_FEATURE_TOO_SMALL` — TPMS strut < 2.0 mm LPBF curved-strut floor.
17. `TURBINE_POWER_DEFICIT` — `TurbopumpResult.Turbine.PowerBalanceOK == false`. At least one shaft's available turbine power (preburner enthalpy drop × mass flow × η) is less than the pump's required shaft power. Only evaluated on cycles with a turbine (Turbopump != null AND Turbine != null); silent on PressureFed / ElectricPump.
18. `SHAFT_WHIRL` — promotes `FeedSystem.ShaftCriticalSpeed`'s `WhirlOk` flag from an advisory warning to a hard gate. Fires whenever either `TurbopumpResult.FuelShaft` or `TurbopumpResult.OxShaft` reports `WhirlOk == false` (operating RPM within ±20 % of the shaft's first bending critical). Aggregates both shafts into a single violation (worst-margin shaft drives `ActualValue` / `Limit`). Reuses `ShaftCriticalSpeed.FormatWarning` for the description so remediation guidance (thicken shaft / shorten span / retune RPM) is identical to the former warning. Skipped on PressureFed / ElectricPump and whenever `ShaftCriticalSpeed.Estimate` returned null (pump or turbine geometry unavailable).
19. `PREBURNER_WALL_TEMP` — fires when the opt-in preburner regen-cooling solver (`HeatTransfer.PreburnerCooling.Solve`) predicts a wall T above the material service limit. Evaluated only when `RegenChamberDesign.IncludePreburnerRegenCooling` is true AND the cycle has a preburner (GasGenerator / StagedCombustion / FullFlow). Both fuel-rich and ox-rich (FFSC) preburners are checked — one violation per over-temperature side. Remediation: increase channel depth or count, raise coolant flow, switch to a higher-temperature wall material, or reduce preburner Pc / MR.
20. `PINTLE_BLOCKAGE_OUT_OF_BAND` — Sprint 18 (2026-04-23). Fires when the sized pintle blockage factor `BL = N · d_sleeve / (π · D_pintle)` is outside the Dressler stable-combustion band [0.40, 0.85]. Only evaluated when `InjectorPattern.ElementType == "Pintle"` AND `InjectorSizing` is populated; silent on every other element type (non-pintle elements leave `OrificeResult.PintleBlockageFraction` at 0, which the gate short-circuits on). Remediation: below floor → reduce `PintleDiameter_mm` or raise `PintleSleeveHoleCount`; above ceiling → increase `PintleDiameter_mm` or lower `PintleSleeveHoleCount`.
21. `PINTLE_TMR_OUT_OF_BAND` — Sprint 18 (2026-04-23). Fires when the sized pintle total momentum ratio `TMR = (ṁ_f · v_f) / (ṁ_ox · v_ox)` is outside the mixing-quality band [0.2, 4.0] (Dressler / TRW heritage). Log-symmetric around 1.0. Only fires under the same pintle-pattern gate as #20. Remediation: tune main-chamber MR or injector ΔP to bring TMR toward 1.0.
22. `BLOW_DOWN_INSUFFICIENT` — Sprint 19 (2026-04-23). Pressure-fed blow-down mode only. Fires when the end-of-burn predicted chamber pressure falls below the target at the reduced tank pressure even though the start-of-burn stackup is feasible — the classic blow-down failure mode (engine starts fine but can't sustain chamber pressure through the burn). Only evaluated when `OperatingConditions.BlowDownFinalPressure_Pa > 0`. Regulated pressure-fed designs and non-pressure-fed cycles skip the gate entirely. Remediation: raise the final tank pressure (smaller initial ullage volume) or switch to a regulated-pressure feed.
23. `EXPANDER_TURBINE_ENTHALPY_DEFICIT` — Sprint 23 (2026-04-23). Expander cycles only (`OpenExpander` / `ClosedExpander`). Fires when the coolant enthalpy picked up in the regen jacket isn't enough to drive the required pump shaft power: `ṁ_coolant · η · w_isen < P_pump`. Only evaluated when `RegenGenerationResult.ExpanderTurbine` is populated (cycle is expander-family AND jacket absorbed heat). Remediation: raise jacket ΔT (smaller channel / more flow / longer chamber), raise jacket outlet pressure, or switch to a preburner cycle.
24. `ORSC_PREBURNER_OXCORROSION` — Sprint 24 (2026-04-23). Ox-rich staged-combustion cycle only (`EngineCycle == ORSC`). Fires when the ox-rich preburner peak wall T exceeds the material service limit minus a 50 K corrosion margin. Ox-rich combustion accelerates metal-oxidation — RD-180-class Russian ORSC hardware runs turbine inlet ~1050 K versus fuel-rich ~1100 K on the same alloy family. Only evaluated when `gen.OxidizerPreburner.Thermal` is populated (opt-in `IncludePreburnerRegenCooling`) AND cycle is ORSC. FFSC keeps the slacker hard-only service-limit margin pending a real ox-rich design pushing near the line. Remediation: increase preburner cooling, lower preburner Pc / MR, or switch to a corrosion-resistant alloy (Cu-coated Inconel 718 / NiCrAl heritage).
25. `TAPOFF_HOT_GAS_TOO_HOT` — Sprint 25 (2026-04-23). Tap-off cycle only (`EngineCycle == TapOff`). Fires when the tap-point temperature (the fuel-film-cooled boundary T, heuristic 35 % of chamber Tc) exceeds the uncooled-turbine-wheel material limit (~1100 K for Inconel 718). Only evaluated when `RegenGenerationResult.TapOffTurbine` is populated. Remediation: lower chamber Pc (lower Tc), boost film-cooling fraction (cooler boundary layer), or switch to a preburner / expander cycle.
26. `OVERHANG_ANGLE_EXCEEDED` — Sprint 27 (2026-04-23). LPBF printability screen: one or more surface patches overhang below the material-specific angle floor (IN718 35°, GRCop / IN625 40°, CuCrZr / 316L 45°). Only evaluated when `RegenChamberDesign.IncludeLpbfPrintabilityAnalysis` is true (else `Printability == null` and the gate is silent). Reads `LpbfPrintabilityResult.Overhang.WorstOverhangAngle_deg` and the material-profile threshold. Remediation: add sacrificial supports, re-orient the build (see `Printability.Orientation` for the advisor's recommendation), or soften the steepest slopes.
27. `TRAPPED_POWDER_REGION` — Sprint 27 (2026-04-23). LPBF printability screen: at least one closed void pocket cannot evacuate powder to any external surface or configured opening. One violation per connected component. Only evaluated when `Printability.TrappedPowder` is populated (the fast SA path skips the 3-D flood-fill; opt in at STL-export time or explicitly from a Benchmarks CLI run). Remediation: add a drain port, reroute the offending passage, or re-orient the build so gravity helps evacuation.
28. `DRAIN_PATH_MISSING` — Sprint 27 (2026-04-23). LPBF printability screen: at least one plumbing branch is a dead-end (degree-1 node not flagged as external port) or belongs to a subgraph that contains no external port at all. One violation per bad node. Only evaluated when `Printability` is populated (opt-in). Remediation: wire the dead-end to an external port, remove the branch, or add a drain tap.
29. `INSTRUMENTATION_TAP_INTERFERENCE` — Sprint 28 (2026-04-24). Hot-fire-readiness item 2. Fires when an instrumentation boss drilled through the regen jacket clashes with a cooling channel or another boss: arc distance at the chamber wall falls below (boss bore radius + effective channel half-width + safety clearance + LPBF floor) for channel overlap, or (larger boss OD + clearance) for boss-vs-boss. One violation emitted per offender — the UI sees every bad boss, not just the first. Only evaluated when `RegenGenerationResult.SensorBosses` is non-empty (pre-Sprint-28 designs carry an empty list → gate silent). Channel-overlap branch only runs on `ChannelTopology == Axial`; helical / TPMS / aerospike / linear-aerospike topologies skip it (conservative — no channels to clash with on those paths). Boss-vs-boss is topology-agnostic. Remediation: shift boss azimuth toward mid-rib, reduce channel count, separate conflicting bosses axially, or place them on opposite sides of the chamber.
30. `IGNITER_MISSING` — Sprint 29 (2026-04-24). Hot-fire-readiness item 3. Fires when `IgniterType.None` is selected on a non-hypergolic propellant pair. Pre-Sprint-29 `None` always passed silently — a LOX/CH4 design with no igniter shipped as feasible, which is a hot-fire-unsafe configuration. Gate stays silent on N2O4/MMH and H2O2/RP-1 (self-ignition / catalyst start). Remediation: pick at least the pair's minimum modality from `Combustion.IgnitionRequirements.For(pair).MinModality`.
31. `IGNITER_MODALITY_UNSUITABLE` — Sprint 29 (2026-04-24). Selected igniter modality ordinal < the pair's minimum recommended ordinal (`None` < `SparkTorch` < `AugmentedSpark` < `PyrotechnicCartridge`). Catches the case where a spark-torch's 150 mJ rated energy clears the LOX/RP-1 500 mJ floor but is still below the `AugmentedSpark` minimum required by field practice on kerosene (Huzel & Huang §7.2 — plain spark torches produce unreliable ignition in RP-1's slow-atomisation regime). Fires independently of `IGNITER_ENERGY_INSUFFICIENT` so both complaints surface together.

**Aerospike-parallel gates (5):**

32. `AEROSPIKE_PLUG_WALL_TEMP` — plug peak gas-side wall T > material service limit. Only fires when `AerospikeBuildResult.Thermal != null` (i.e. plug-channel regen cooling opted in via `AerospikeSpec.IncludeRegenChannels`). Lives in a separate evaluator because the two pipelines produce different result records; a future `IEngineResult` refactor could unify them.
33. `AEROSPIKE_COOLANT_CAVITATION_RISK` — plug-cooling coolant pressure fell below the 0.1 MPa cavitation floor at one or more stations. Previously the clamp emitted a silent warning; promoted to a gate so the SA sampler doesn't hide film-boiling risk in the plug channels.
34. `AEROSPIKE_ELEMENT_CLEARANCE` — fires when the sized injector pattern packs elements closer than `element OD + 2 mm LPBF floor` on the pre-throat chamber face. `AerospikeInjectorSizing.ArcSpacing_mm` vs `MinClearance_mm` reports the exact gap. Only fires when `AerospikeSpec.InjectorPattern != null` (pattern absent → `AerospikeBuildResult.InjectorSizing` null → gate skipped). Remediation: fewer elements / larger chamber / split into multiple rows / element type with smaller housing footprint.
35. `AEROSPIKE_INJECTOR_FACE_TEMP` — fires when `AerospikeInjectorFaceThermal.Estimate` predicts T_face above the wall-material service limit. Analogous to regen Gate 8 (`INJECTOR_FACE_T_EXCEEDED`) but keyed to the aerospike pre-throat chamber geometry. Only fires when `AerospikeSpec.InjectorPattern != null` (no pattern → no thermal estimate → gate skipped). Remediation: raise outer-row film fraction / switch to a higher-temperature wall material / pick an element type with larger bore coverage / reduce chamber pressure.
36. `LINEAR_AEROSPIKE_ASPECT_RATIO` — Sprint 26 (2026-04-23). Linear (extruded-rectangular) aerospike topology only (`ChannelTopology == LinearAerospike`). Fires when the plug aspect ratio (`PlugTruncatedLength_mm / LinearAerospikePlugWidth_mm`) falls outside `[0.30, 5.00]`. Below the floor the side-wall recirculation bubble covers more than half the expansion surface (Angelino 2D model loses physical meaning — X-33 XRS-2200 test-campaign observation); above the ceiling the plug becomes a long-span cantilever whose thermal-bending stiffness is unmanageable at LPBF scale with the shipped wall-material library. Only evaluated when `AerospikeBuildResult.Contour.IsLinear == true` (axisymmetric aerospikes short-circuit). Remediation — below floor: reduce plug transverse width, raise expansion ratio, or raise plug-length ratio; above ceiling: increase plug transverse width, reduce expansion ratio, or reduce plug-length ratio.

**Monolithic-assembly gates (2):**

These live in `Geometry/MonolithicFeasibility.Evaluate(MonolithicBodyEnvelopes, tubes, clearance)` and only fire on the monolithic-engine pipeline (`--monolithic` CLI or `MonolithicEngineBuilder.Build`). They do not contaminate the regen-only optimizer path.

37. `MONOLITHIC_BODY_INTERSECTION` — any routed feed tube clips a physical body envelope (chamber, fuel/ox pump casing, preburner, fuel/ox turbine wheel, or aerospike plug). Endpoint-touch whitelist (2 mm default tolerance) keeps legitimate branch joints at pump discharges or preburner inlets feasible. Evaluates 8 interior samples per leg at stations t = k/9. The aerospike plug envelope is axisymmetric, sampled via `MonolithicBodyEnvelopes.AerospikePlug` with station-interpolated radius from `AerospikeContour.Stations`.
38. `MONOLITHIC_TUBE_INTERSECTION` — any unordered pair of routed feed tubes has closest-approach gap < `tubeA.OuterRadius + tubeB.OuterRadius + clearance`. Uses Eberly / Goldman segment-to-segment closest-points across every leg pair with a parallel-segment fallback. Shared-endpoint whitelist (default 2 mm) admits branch joints. Aggregated per unordered tube pair so a three-tube starburst emits exactly three violations, never six.

**Founding gate census: 31 regen + 5 aerospike-parallel + 2 monolithic = 38. Project-wide census (2026-06): 196 unique `ConstraintId`s across five pillar evaluators — 65 rocket + 40 air-breathing + 54 electric + 22 marine + 15 nuclear. Each pillar uses its own evaluator surface; the rocket-side `GateRegistry` is typed to `RegenGenerationResult`, the other four are parallel evaluators per ADR-026 §3. See `docs/GATES.md` for the per-gate inventory.**

Advisory, non-gate: a turbine-wheel **rim-stress advisory** on `TurbineStage.RimStressOk` (Timoshenko-Goodier σ = (3+ν)/8·ρ·U_tip², Inconel 718 / SF 2.0) emits a WARNING but does not gate — the deliberate choice was to surface rotordynamic risk to the user without blocking SA exploration on a single-formula rim-stress approximation. Promote to a gate only after a hot-fire test campaign or multi-rotor FE validation justifies the stricter constraint.

## Alternatives rejected

- **Soft penalties only** — optimizer can score "slightly infeasible" as
  better than a feasible-but-suboptimal design, producing unprintable
  outputs.
- **Fail-fast on first violation** — loses diagnostic value. The user
  wants to see ALL violations so they know which constraints drive the
  current search into a corner.
- **Combined numeric "feasibility score"** — composition of disparate
  constraints (mm floors, MPa pressures, K temperatures) into one number
  is arbitrary and loses information.

## Consequences

Positive:
- Optimizer never picks an infeasible design.
- Users see every violation, can diagnose which constraints bind.
- Each gate is independently testable — the current suite has at least
  one test per gate that crafts a fixture the gate fires on.
- Adding a new gate is an additive one-call operation (see Operational
  invariant below).

Negative:
- No "almost feasible" exploration. A design that's 1 K over the wall
  temperature limit scores identically to one 1000 K over. (Addressed
  by scoring profiles; `RegenScoring.WallTPenalty` is quadratic in the
  *feasible* region, `+∞` outside.)

## Operational invariant

> **Every new feasibility gate must be additive.** The evaluator collects
> all violations (no fail-fast) and existing gates must keep firing in
> their documented conditions. Adding a new gate means:
> (a) one `GateRegistry.Register(new FeasibilityGateDescriptor(...))` call
> in the appropriate `RegisterAll()` method in `Voxelforge.Core/Optimization/RocketGates.cs`
> (or `AerospikeFeasibility.cs` for aerospike-parallel gates),
> (b) at least one test that crafts a fixture the gate fires on,
> (c) a new row in `docs/GATES.md`.

**Do not remove or relax a gate without ADR-level review.** Each gate
represents a real failure mode that has historically caused a bad design
to get printed or a test-stand incident.
