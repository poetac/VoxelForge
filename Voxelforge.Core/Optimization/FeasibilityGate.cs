// FeasibilityGate.cs — Hard constraint screening for the SA optimizer.
//
// UPGRADE 3: Before the optimizer scores a candidate, this gate checks five
// physics-based hard constraints. Any violation makes the design infeasible;
// the caller (RegenChamberOptimization.Evaluate) must return
// TotalScore = double.PositiveInfinity so the SA algorithm unconditionally
// rejects the candidate and never promotes it as a new best.
//
// The gate collects ALL violations (not fail-fast) so that the UI and report
// can explain exactly why a design was rejected — useful for diagnosis during
// optimization and for manual design review.
//
// Hard gates (ordered by severity):
//   1. WALL_TEMP       — Peak gas-side wall T > material MaxServiceTemp_K
//   2. YIELD_EXCEEDED  — Structural safety factor < 1.0 (yield imminent)
//   3. FEATURE_TOO_SMALL — Min LPBF feature < 0.30 mm (universal print floor)
//   4. COOLANT_T_EXCEEDED — Coolant outlet T > fluid MaxBulkT_K (coking / embrittlement)
//   5. STABILITY_FAIL  — Stability composite == Fail (explicit instability)
//   6. ELEMENT_DENSITY_TOO_HIGH — Injector elements / cm² > 0.7 (face burnout risk)
//      (SPRINT 1.3; only evaluated when a pattern is set + implemented)
//   7. INJECTOR_FACE_T_EXCEEDED — Predicted face T > wall material service limit
//      (PHASE 2, 2026-04-20; only when InjectorFace estimate is populated)
//   8. FEED_PRESSURE_INSUFFICIENT — Predicted chamber pressure from
//      the feed-system stackup falls below the target. Only
//      evaluated when the user opted in by setting
//      OperatingConditions.TankUllagePressure_Pa > 0.
//   9a. IGNITER_MISSING — IgniterType.None selected on a
//       non-hypergolic propellant pair. Hard gate for every pair
//       except N2O4/MMH and H2O2/RP-1 (catalyst start).
//   9b. IGNITER_ENERGY_INSUFFICIENT — Rated energy of the selected
//       preset < propellant-pair minimum (LOX/CH4: 50 mJ, LOX/H2:
//       5 mJ, LOX/RP-1: 500 mJ). Per-pair floors live in
//       `Combustion.IgnitionRequirements`. Supersedes an earlier
//       universal 50 mJ floor.
//   9c. IGNITER_MODALITY_UNSUITABLE — Selected modality ordinal <
//       recommended minimum for the pair. Fires on LOX/RP-1 +
//       SparkTorch even when rated energy clears the floor (Huzel &
//       Huang §7.2 — plain spark torches unreliable on kerosene cold
//       start).
//  10. PURGE_FLOW_INSUFFICIENT — Any configured purge port delivers
//      less than 95 % of its requested mass flow at the specified
//      inlet pressure against chamber pressure. Skipped when no
//      purge ports are configured.
//  11. ABLATIVE_BURNTHROUGH — Predicted (recession + char_depth) ×
//      safety factor exceeds the initial ablative liner thickness.
//      Only evaluated when the user has selected an ablative
//      material on the design.
//  12. CHILLDOWN_BUDGET_EXCEEDED — Soft gate; integrated chilldown
//      time exceeds the user-specified budget. Only evaluated when
//      chilldown is opted in.
//  13. HARD_START_RISK — Start-transient simulator predicts a Pc
//      overshoot beyond the user-specified hard-start factor. Only
//      evaluated when the start transient is opted in.
//  14. NPSH_INSUFFICIENT — Turbopump sizing reports NPSHA < NPSHR on
//      at least one pump. Only evaluated when EngineCycle !=
//      PressureFed.
//  14b. PUMP_PRESSURE_INVERTED — Pump discharge ≤ inlet on fuel or ox
//      side. SizeOnePump clamps dP to 0 silently; without this gate an
//      inverted feed reports NPSHAcceptable=true and slips the pump
//      gate entirely. Sutton §6.5: a pump with non-positive head rise
//      is not a pump. Only evaluated when EngineCycle != PressureFed.
//  14c. BURST_MARGIN_INSUFFICIENT — Elastic burst margin (=
//      P_burst_elastic / MEOP) < 2.5 × per ASME BPVC §VIII Div 1
//      ground-test threshold. PR #104 raised the warning threshold
//      from 2.0× → 2.5× in `ProofTestAnalysis` + `SafetyReport` but
//      didn't add a feasibility gate; designs with margin in
//      [2.0, 2.5) passed feasibility while failing ASME proof-test
//      review. Z2.8 (2026-04-28) closes this loophole. Computed
//      cheaply via thin-wall hoop on the gas-side wall profile so
//      the SA hot path doesn't pay for full proof-test analysis.
//      Skipped when `gen.BurstMarginFactor == 0` (synthetic / legacy
//      call sites that don't populate it).
//  15. TPMS_CELL_FEATURE_TOO_SMALL — TPMS channel topology implies
//      strut thickness (solid_fraction × cell_edge) below the 2.0 mm
//      LPBF floor from TpmsCorrelations.MinStrutThickness_mm. Only
//      evaluated when ChannelTopology ∈ {TpmsGyroid, TpmsSchwarzP,
//      TpmsSchwarzD}; axial / helical / ablative designs keep the
//      existing universal 0.3 mm FEATURE_TOO_SMALL check.
//  16. TURBINE_POWER_DEFICIT — Sized turbine (TurbopumpResult.Turbine)
//      reports AvailableShaftPower_W < RequiredShaftPower_W on at
//      least one shaft. Deficit means the preburner enthalpy drop
//      cannot drive the pump it's wired to — raise preburner Pc,
//      raise turbine mass flow, lower pump head, or accept a
//      different cycle. Only evaluated on cycles with a turbine
//      (Turbopump != null AND Turbine != null).
//  17. SHAFT_WHIRL — Promotes the shaft-critical-speed whirl-band
//      advisory into a hard gate. Fires when the pump RPM lands
//      within ±20 % of the first
//      bending critical on either the fuel or ox shaft — continuous
//      operation inside that band risks bearing fatigue and uncontained
//      whirl. Skipped on PressureFed / ElectricPump (no rotating
//      shaft) and whenever ShaftCriticalSpeed.Estimate returned null
//      (pump or turbine geometry unavailable).
//  18. PREBURNER_WALL_TEMP — Fires when the preburner regen-cooling
//      lumped-parameter solver predicts a wall T above the material
//      service limit. Only evaluated when
//      RegenChamberDesign.IncludePreburnerRegenCooling is true AND
//      the cycle has a preburner (StagedCombustion / GasGenerator /
//      FullFlow) — no opt-in → no gate. Preburner-side analogue of
//      regen Gate 1 WALL_TEMP.
//  19. PINTLE_BLOCKAGE_OUT_OF_BAND — Fires when the sized pintle's
//      blockage factor BL = N·d_sleeve / (π·D_pintle) is outside
//      Dressler's stable-combustion band [0.40, 0.85]. Only evaluated
//      when InjectorPattern.ElementType is "Pintle" AND
//      InjectorSizing is populated; silent on every other element
//      type (non-pintle elements leave
//      OrificeResult.PintleBlockageFraction at 0).
//  20. PINTLE_TMR_OUT_OF_BAND — Fires when the sized pintle's total
//      momentum ratio TMR = (ṁ_f·v_f)/(ṁ_ox·v_ox) is outside the
//      mixing-quality band [0.2, 4.0] (Dressler / TRW heritage). Only
//      evaluated under the same pintle-pattern gate as #19.
//      Log-symmetric around 1.0; tune by changing the main-chamber
//      MR or the injector ΔP.
//  21. BLOW_DOWN_INSUFFICIENT — Pressure-fed blow-down mode only
//      (OperatingConditions.BlowDownFinalPressure_Pa > 0). Fires
//      when the end-of-burn predicted chamber pressure falls below
//      target even though the start-of-burn stackup is feasible.
//      Classic blow-down failure mode: engine starts fine but can't
//      complete the burn as the tank pressure decays. Regulated
//      pressure-fed designs and non-pressure-fed cycles skip this
//      gate entirely (EndOfBurnTankPressure_Pa stays 0).
//  22. EXPANDER_TURBINE_ENTHALPY_DEFICIT — Expander cycles only
//      (OpenExpander / ClosedExpander). Fires
//      when the coolant enthalpy picked up in the regen jacket isn't
//      enough to drive the required pump shaft power. Remediation:
//      raise jacket ΔT (smaller channel / more flow / longer chamber),
//      raise jacket outlet pressure, or switch to a preburner cycle.
//      Only evaluated when RegenGenerationResult.ExpanderTurbine is
//      populated (i.e., cycle is expander-family AND jacket did work).
//  23. ORSC_PREBURNER_OXCORROSION — Ox-rich staged-combustion cycle
//      only. Fires when the ox-rich preburner
//      peak wall T exceeds (material service limit − 50 K). Ox-rich
//      combustion accelerates metal-oxidation — RD-180-class hardware
//      runs turbine inlet ~1050 K vs fuel-rich ~1100 K on the same
//      alloy. Gate only evaluated when OxidizerPreburner.Thermal is
//      populated (design.IncludePreburnerRegenCooling true) AND cycle
//      is ORSC. FFSC keeps the slacker hard-only margin pending a
//      real ox-rich design reaching the limit.
//  24. TAPOFF_HOT_GAS_TOO_HOT — Tap-off cycle only (EngineCycle ==
//      TapOff). Fires when the tap-point T (the
//      fuel-film-cooled boundary T, ~35 % of chamber Tc) exceeds the
//      uncooled-turbine-wheel material limit (~1100 K for Inconel 718).
//      Only evaluated when RegenGenerationResult.TapOffTurbine is
//      populated. Remediation: lower chamber Pc (lower Tc), boost
//      film-cooling fraction (cooler boundary layer), or switch to
//      a preburner or expander cycle.
//  25. OVERHANG_ANGLE_EXCEEDED — LPBF printability screen: one or
//      more surface patches overhang
//      below the material-specific angle floor (35°–45° depending
//      on alloy). Fires only when the user has opted in via
//      RegenChamberDesign.IncludeLpbfPrintabilityAnalysis. Remediation:
//      add sacrificial supports, re-orient build (see the orientation
//      advisor's recommendation on RegenGenerationResult.Printability),
//      or stretch the chamber to soften the worst slopes.
//  26. TRAPPED_POWDER_REGION — LPBF printability screen: at least
//      one closed void pocket cannot
//      evacuate powder to the external surface or any configured
//      opening. Fires only on opted-in designs with a voxel snapshot
//      attached to the printability result (the fast SA path doesn't
//      pay for the flood-fill — opt in per-run at STL-export time).
//      Remediation: add a drain port, reroute the offending passage,
//      or re-orient the build so gravity helps evacuation.
//  28. INSTRUMENTATION_TAP_INTERFERENCE — A sensor boss drilled
//      through the regen jacket overlaps a cooling channel (boss
//      bore arc-distance < channel half-pitch + boss radius + LPBF
//      floor) or another boss (arc spacing < OD + clearance). Only
//      evaluated when the design carries at least one SensorBoss;
//      silent on legacy designs (empty list short-circuits). Channel
//      check runs only on Axial topology; helical / TPMS / aerospike
//      topologies skip that branch. Boss-vs-boss is topology-agnostic.
//
//  27. DRAIN_PATH_MISSING — LPBF printability screen: at least one
//      dead-end plumbing branch can't evacuate powder under standard
//      post-processing orientations. Fires only on opted-in designs.
//      Remediation: wire the dead-end to an external port, remove
//      the branch, or add a drain tap.
//
//  29. CONTRACTION_RATIO_OUT_OF_BAND — Sprint 36 / PH-17 (2026-04-24).
//      Contour ε_c outside [2.5, 10.0]. Below 2.5 → chamber Mach
//      pushes past 0.2 with combustion-instability risk; above 10 →
//      wasted wall area + cooling-surface bloat. Sutton §8.2 / Huzel
//      & Huang §4.1. Reads from Contour.ContractionRatio (topology-
//      agnostic).
//
//  30. CHANNEL_ASPECT_RATIO_EXCEEDED — Sprint 36 / PH-23 (2026-04-24).
//      Regen-channel depth/width > 8 (warn) or > 10 (strict) on at
//      least one station. Fires once per design at the worst station;
//      not per station to avoid spam. Skipped on TPMS topologies and
//      ablative-only (None).
//
//  31. G_INJ_TOO_LOW / G_INJ_TOO_HIGH — Sprint 36 / PH-21 (2026-04-24).
//      Injector mass flux ṁ_total / A_total outside [140, 500]
//      kg/(m²·s) — Sutton §6.3 / Yang LPCI §5 stable band. Below →
//      chug instability; above → over-mix / face-burnout regime.
//      Only evaluated when InjectorSizing carries positive total
//      orifice areas (sized, implemented pattern).
//
//  32. L_STAR_BELOW_PROPELLANT_MIN — Sprint 36 / PH-11 (2026-04-24).
//      Characteristic length below 95 % of pair nominal. Real engines
//      below this floor lose 2-5 % on C\* — the η_C\* default does
//      not capture the penalty. Pair nominals from
//      AutoSeeder.CharacteristicLengthFor.
//
//  33. INSTRUMENTATION_THERMAL_BRIDGE_RISK — Sprint 36 / PH-22
//      (2026-04-24). Sensor boss in a station where q\" exceeds 80 %
//      of peak gas-side flux AND the wall material conductivity is
//      sharply different (delta > 50 %) from a typical 16 W/m·K
//      stainless boss assumption. Soft-warns on instrumentation
//      placement that creates thermal-bridge hot-spots beyond what
//      the 1-D wall solver predicts.
//
//  34. TURBINE_UNCHOKED — Sprint 34a / PH-26 (2026-04-25). Turbine
//      stator throat does not choke: π = p_out / p_in > π_crit =
//      (2/(γ+1))^(γ/(γ-1)). Subsonic flow on a supersonic-stator
//      wheel collapses the assumed η ≈ 0.55-0.60 to ~0.30. Each
//      non-null sized turbine result on the design is checked
//      independently; one violation per unchoked stage. Tap-off
//      cycle is the most exposed (low-Pc designs discharging to
//      ambient may not choke); closed-expander is also at risk.
//
//  35. PUMP_SPECIFIC_SPEED_OFF_BAND — Sprint 34b minimum viable /
//      PH-8 (2026-04-25). Pump N_s = rpm · √Q_gpm / H_ft^0.75 outside
//      [600, 9000] (US units). Below 600 → axial-flow regime where
//      centrifugal-pump similarity math no longer holds; above 9000
//      → multi-stage / mixed-flow territory beyond the single-stage
//      model. Karassik §2.5 / Stepanoff §2.7. Each non-null pump on
//      the design is checked independently. Pre-Sprint-34b every
//      design reported the constant N_s = 2500; post-fix users opt
//      into RPM-as-input via RegenChamberDesign.PumpRpm_rpm and the
//      diagnostic N_s drives this gate. Auto-derive (PumpRpm_rpm = 0)
//      keeps N_s ≈ 2500 by construction.
//
//  36. TPMS_AND_MANIFOLD_OVERLAP — Z3 #20 / Geometry B3 (2026-04-29).
//      AdvisoryHeuristic gate. Fires when
//      2 × ManifoldLength_mm ≥ TotalLength_mm on a TPMS-topology
//      design. Pre-empts PicoGK pitfall #2 (BoolSubtract through
//      TPMS-filled regions produces fragments) for small-chamber
//      TPMS designs where the inlet + outlet manifolds at opposite
//      ends would overlap each other in the chamber centre, leaving
//      no clear TPMS unit-cell region. Skipped on Axial / Helical
//      / Aerospike / Pattern topologies (those use their own subtract
//      paths that do not hit pitfall #2). Remediation: shorten the
//      manifolds, lengthen the chamber, or switch to Axial topology.
//
//  37. BIMETALLIC_BOND_ZONE_SHEAR — Z3-M2 (2026-04-29). On bimetallic
//      walls (Cu-alloy liner + Ni-alloy jacket), CTE mismatch drives
//      interfacial shear stress at the bond zone:
//         τ_bond = ΔT · |α_liner − α_jacket| · E_eff
//      (Hibbeler §8.4 thermo-mechanical composite; E_eff = arithmetic
//      mean of liner and jacket moduli at the wall mean temperature,
//      a first-order conservative upper bound). Advisory gate fires
//      when τ_bond > σ_y_min · 0.5 — a conservative advisory
//      threshold marking designs where bond-zone shear is material vs
//      the bulk yield strength. Known concern on LPBF bimetallic
//      walls per NASA PURS data on GRCop-42 / IN625 coupons.
//      Not a hard gate: a full FEA with residual stress + constraint
//      geometry accounts for the length-scale factor not in this
//      first-order formula. Remediation: reduce ΔT across the wall
//      (more coolant flow / wider throat channels), or introduce a
//      CTE-gradient interlayer between liner and jacket.
//      Only evaluated when StructuralCheck populated a non-zero
//      BondZoneShearStress_MPa (single-material designs stay silent).
//
//  38. COMMON_SHAFT_RPM_INCONSISTENT — PH-48 (issue #193, 2026-04-29).
//      PhysicsLimit gate. On common-shaft cycles (GasGenerator,
//      StagedCombustion, ORSC, OpenExpander, ClosedExpander, TapOff)
//      both the fuel and ox pumps are mechanically coupled to a single
//      shaft and must run at the same angular velocity ω. Any RPM
//      discrepancy > 0.5 % is physically impossible. After the PH-48
//      fix, TurbopumpSizing.Size() enforces the common RPM by re-sizing
//      the higher-speed pump at the constraining (lower) RPM. This gate
//      acts as a regression guard: it fires if a code path bypasses the
//      enforcement and the result arrives with mismatched RPMs.
//
// Preliminary-design fidelity: gates carry the same ±25–50 % wall-T and
// ±20 % ΔP caveats as the underlying solver. They are NECESSARY not
// SUFFICIENT conditions for a safe design.

using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Optimization;

/// <summary>
/// One hard-constraint violation.
/// <list type="bullet">
///   <item><see cref="ConstraintId"/> — stable machine-readable key used in tests.</item>
///   <item><see cref="ActualValue"/> and <see cref="Limit"/> — natural-unit values for display.
///   NaN for categorical constraints (e.g. stability rating) where "actual" has no numeric form.</item>
/// </list>
/// </summary>
public sealed record FeasibilityViolation(
    string ConstraintId,
    string Description,
    double ActualValue,
    double Limit)
{
    /// <summary>
    /// Phase 1 of [#627](https://github.com/poetac/voxelforge/issues/627)
    /// — unsigned magnitude of the constraint breach in natural units
    /// (<c>|ActualValue − Limit|</c>). Smooth (non-cliff) landscape signal
    /// that non-SA optimizers (CMA-ES, Bayesian, gradient-polish) can
    /// use for soft-penalty shaping, vs the <c>+∞</c> hard-cliff score
    /// SA tolerates. NaN when either input is NaN (categorical gates).
    /// </summary>
    /// <remarks>
    /// Per-gate <em>signed</em> magnitude (negative inside feasible
    /// region, with a known sign convention per ConstraintId) is
    /// Phase 2 work — it requires per-gate metadata (which side of
    /// the limit is infeasible). For WALL_TEMP / COOLANT_T it's
    /// <c>actual &gt; limit = breach</c>; for SF / NPSH it's
    /// <c>actual &lt; limit = breach</c>. Phase 2 ships the lookup +
    /// the optimizer-side opt-in shaping. Until then, this magnitude
    /// is unsigned but still strictly more informative than a
    /// <c>+∞</c> cliff.
    /// </remarks>
    public double BreachMagnitude
    {
        get
        {
            if (double.IsNaN(ActualValue) || double.IsNaN(Limit)) return double.NaN;
            return System.Math.Abs(ActualValue - Limit);
        }
    }

    /// <summary>
    /// Phase 2 of [#627](https://github.com/poetac/voxelforge/issues/627)
    /// (tracked under [#743](https://github.com/poetac/voxelforge/issues/743))
    /// — signed magnitude of the constraint breach. Direction is looked up
    /// per <see cref="ConstraintId"/> via <see cref="ConstraintDirections.For"/>:
    /// <list type="bullet">
    ///   <item><see cref="BreachDirection.AboveLimit"/>: <c>ActualValue − Limit</c> (positive = depth into infeasible region).</item>
    ///   <item><see cref="BreachDirection.BelowLimit"/>: <c>Limit − ActualValue</c> (positive = depth into infeasible region).</item>
    ///   <item><see cref="BreachDirection.Categorical"/>: <c>NaN</c> (no numeric direction).</item>
    /// </list>
    /// Returns <c>NaN</c> when either <see cref="ActualValue"/> or
    /// <see cref="Limit"/> is <c>NaN</c>.
    /// </summary>
    /// <remarks>
    /// Non-SA optimizers (CMA-ES, Bayesian) consume this for soft-penalty
    /// shaping via <c>penalty_scale · Σ tanh(|SignedBreachMagnitude| / scale)</c>
    /// when constructed with <c>useSoftPenalty: true</c> (Phase 2 wrapper
    /// follow-up). SA stays bit-identical because it never reads this
    /// property — it only sees the <c>+∞</c> hard-cliff score baked in at
    /// the gate level.
    /// </remarks>
    public double SignedBreachMagnitude
    {
        get
        {
            if (double.IsNaN(ActualValue) || double.IsNaN(Limit)) return double.NaN;
            return ConstraintDirections.For(ConstraintId) switch
            {
                BreachDirection.AboveLimit  => ActualValue - Limit,
                BreachDirection.BelowLimit  => Limit - ActualValue,
                BreachDirection.Categorical => double.NaN,
                _                           => double.NaN,
            };
        }
    }
}

/// <summary>
/// Output of <see cref="FeasibilityGate.Evaluate"/>.
/// <c>IsFeasible == (Violations.Length == 0)</c>.
/// </summary>
public sealed record FeasibilityGateResult(
    bool IsFeasible,
    FeasibilityViolation[] Violations);

/// <summary>
/// Hard-constraint checker. Call <see cref="Evaluate"/> before scoring any
/// candidate design. Return <see cref="double.PositiveInfinity"/> as the
/// total score when <see cref="FeasibilityGateResult.IsFeasible"/> is false.
/// </summary>
public static class FeasibilityGate
{
    /// <summary>
    /// LPBF universal feature-size floor [mm].
    /// Any rib, wall, or channel thinner than this cannot be printed on any
    /// commercial laser powder-bed fusion machine at 30 µm layer thickness.
    /// </summary>
    public const double LpbfFeatureFloor_mm = 0.30;

    /// <summary>
    /// Maximum injector element density before face-plate burnout risk becomes
    /// the dominant failure mode. Widely-cited rule-of-thumb from Huzel &amp; Huang
    /// §8.2: for LOX/HC engines, dense packing (&gt; 0.7 elements/cm²) reduces
    /// face plate area available for conductive / film cooling to below the
    /// regen safety band. Units: elements per cm² of chamber face area.
    /// </summary>
    public const double ElementDensityCeiling_per_cm2 = 0.7;

    /// <summary>
    /// Sprint 18 (2026-04-23): pintle blockage-factor band.
    /// BL = N · d_sleeve / (π · D_pintle). Below the lower bound →
    /// sheet breakup is starved (poor atomisation); above the upper
    /// bound → jets collide too aggressively, causing combustion
    /// instability.
    ///
    /// **Sprint feasibility-audit-H1 (2026-04-27):** band widened
    /// 0.40-0.85 → 0.35-0.90 per Heister 2017 ("Pintle Injectors",
    /// AIAA Progress Series 260) which surveys empirically observed
    /// stable-combustion data across SuperDraco, LMDE, TRW LMA, and
    /// Apollo SPS pintles and reports the wider [0.30-0.95] envelope.
    /// We adopt the Heister sub-band [0.35-0.90] as the practical
    /// design target — slightly wider than Dressler's 2000 envelope
    /// (which was the smaller TRW historical dataset) but inside
    /// the Heister 2017 observed range with margin. This widening
    /// reduces false PINTLE_BLOCKAGE_OUT_OF_BAND firing on SA
    /// candidates from ~99.9 % toward a more representative rate
    /// (the canonical pintle preset SA-explores into 0.85-0.90 BL
    /// territory because none of D_pintle / N_sleeve / target are
    /// SA-tunable today, so the seed BL ≈ 0.83 leaves no headroom
    /// for SA-driven mass-flow shifts).
    /// </summary>
    public const double PintleBlockageFloor  = 0.35;
    public const double PintleBlockageCeiling = 0.90;

    /// <summary>
    /// Sprint 18 (2026-04-23): pintle total-momentum-ratio band.
    /// TMR = (ṁ_f · v_f) / (ṁ_ox · v_ox). Outside [0.2, 4.0] the
    /// unlike-streams-mixing quality drops off sharply per the pintle
    /// literature (below 0.2 → fuel under-drives; above 4.0 → ox
    /// under-drives). The band is symmetric around 1.0 on a log scale.
    /// </summary>
    public const double PintleTmrFloor   = 0.2;
    public const double PintleTmrCeiling = 4.0;

    /// <summary>
    /// Sprint 36 / PH-17 (2026-04-24): contraction-ratio (ε_c =
    /// A_chamber / A_throat) practical band. Below 2.5 → chamber Mach
    /// pushes past 0.2 with combustion-instability risk; above 10 →
    /// wasted wall area and cooling-surface bloat that scoring already
    /// penalises but doesn't outright reject. Sutton §8.2, Huzel &amp;
    /// Huang §4.1 cite both as hard envelopes for liquid-bipropellant
    /// chambers.
    /// </summary>
    public const double ContractionRatioFloor   = 2.5;
    public const double ContractionRatioCeiling = 10.0;

    /// <summary>
    /// Sprint 36 / PH-23 (2026-04-24): regen-channel cross-section
    /// aspect-ratio band (depth / width). LPBF-printed channels with AR
    /// &gt; 8 buckle during print as the rib slenderness ratio exceeds
    /// the EOS / Wolfram process-map limit; AR &gt; 10 is firmly in
    /// "expensive failure" territory on the recoater stroke.
    /// </summary>
    public const double ChannelAspectRatioWarn   = 8.0;
    public const double ChannelAspectRatioStrict = 10.0;

    /// <summary>
    /// Sprint 36 / PH-21 — calibration corrected 2026-04-26. Injector
    /// mass-flux band per <b>Sutton 9e Chapter 8 (p. 270)</b>: per
    /// element cross-section, propellant mass-flux densities range
    /// from ~7 lb/(in²·s) (low-impulse, low-Pc) to ~60 lb/(in²·s)
    /// (high-Pc regen-cooled engines). Converted to SI:
    /// <c>4,925 → 42,200 kg/(m²·s)</c>. The metric is
    /// <c>(ṁ_total) / (ΣA_orifice)</c> across all bores, which is the
    /// same number whether you sum per-element or compute total/total.
    ///
    /// Original constants from PH-21 (140-500 kg/(m²·s)) were ~30-100×
    /// too low — likely a units-conversion error (the published
    /// reference is in lb/(in²·s), and 7 lb/(in²·s) ≈ 4,925 kg/(m²·s)
    /// not 140). Cross-checked against published engines:
    ///   • F-1:    ~16,300 kg/(m²·s)  (Saturn V first stage)
    ///   • SSME:   ~7,100 kg/(m²·s)
    ///   • Merlin-1D: ~8,000-15,000 kg/(m²·s) (per FAA filings + LPRE estimates)
    /// All three sit comfortably in the new band.
    ///
    /// Below 3,000 kg/(m²·s) → low spray momentum, chug-instability
    /// floor (Yang LRECI §5). Above 50,000 kg/(m²·s) → over-mix +
    /// face-burnout ceiling. Element-density gate (#6) remains
    /// necessary but not sufficient; mass-flux extends coverage to
    /// designs that pass density via small bores.
    /// </summary>
    public const double InjectorMassFluxFloor_kgPerm2s   = 3_000.0;
    public const double InjectorMassFluxCeiling_kgPerm2s = 50_000.0;

    /// <summary>
    /// Sprint 36 / PH-11 (2026-04-24): L\* soft floor as a fraction of
    /// the propellant pair's nominal characteristic length. Below
    /// 95 % of nominal, atomisation + residence time degrade enough
    /// that the (typically constant) η_C\* stops modelling reality —
    /// real engines lose 2-5 % on C\*. Per-pair nominals come from
    /// <see cref="AutoSeeder.CharacteristicLengthFor"/>.
    /// </summary>
    public const double LStarFloorFraction = 0.95;

    /// <summary>
    /// Sprint 34b / PH-8 (2026-04-25): pump specific-speed practical
    /// band. Below 600 → axial-flow regime (centrifugal-pump similarity
    /// math no longer holds + low-η operation); above 9000 → multi-
    /// stage / mixed-flow territory where the single-stage centrifugal
    /// model degrades. Karassik §2.5; Stepanoff §2.7. Pre-Sprint-34b
    /// every design reported the constant 2500 silently — physically
    /// impossible for tiny 0.01 kg/s and large 50 kg/s pumps both to
    /// live at the same N_s. The gate fires only on user-set RPMs that
    /// drift the diagnostic N_s out of band; the legacy auto-derive
    /// path keeps N_s ≈ 2500 by construction.
    /// </summary>
    public const double PumpSpecificSpeedFloor   = 600.0;
    public const double PumpSpecificSpeedCeiling = 9000.0;

    /// <summary>
    /// Sprint 36 / PH-22 (2026-04-24): instrumentation-boss thermal-
    /// bridge advisory threshold. Fires when |k_boss − k_wall| / k_wall
    /// &gt; 0.5 AND the boss sits within a station whose heat flux
    /// exceeds 80 % of peak — the combination creates a localised hot
    /// (or cold) spot beyond what the 1-D wall solver predicts.
    /// </summary>
    public const double InstrumentationBridgeConductivityRatio = 0.5;
    public const double InstrumentationBridgeHighFluxFraction  = 0.80;

    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Z3 #16 / external-audit F-8 (2026-04-28): map a gate's
    /// <c>ConstraintId</c> to its categorical <see cref="GateKind"/>.
    /// Lets UI / report / external analysts filter violations by kind
    /// (PhysicsLimit hard failures vs EmpiricalBand soft warnings vs
    /// ManufacturabilityFloor LPBF concerns vs AdvisoryHeuristic rules
    /// of thumb).
    /// </summary>
    /// <remarks>
    /// Exhaustive over every <c>ConstraintId</c> emitted across:
    /// <list type="bullet">
    ///   <item><see cref="Evaluate"/> + <see cref="PreScreen"/> in this file (regen-cooled bell chamber gates).</item>
    ///   <item><c>RegenChamberOptimization.Evaluate</c> (the optimiser-level <c>VOXEL_RESOLUTION</c> gate).</item>
    ///   <item><c>AerospikeFeasibility</c> (5 aerospike-parallel gates fired for Aerospike / LinearAerospike topologies).</item>
    ///   <item><c>MonolithicFeasibility</c> (2 monolithic-composition gates fired for fused multi-subsystem builds).</item>
    /// </list>
    /// Unknown / future <c>ConstraintId</c>s default to
    /// <see cref="GateKind.AdvisoryHeuristic"/> as the most conservative
    /// classification (won't claim physics-grade rigour for an
    /// unclassified gate) until they're added to the switch.
    ///
    /// The categorisation rubric:
    /// <list type="bullet">
    ///   <item><b>PhysicsLimit</b> — violating it means real hardware fails (yield, burst, melt, cavitation, stall, energy-balance).</item>
    ///   <item><b>EmpiricalBand</b> — violating it means the model's correlation is out of its calibration regime; hardware may or may not work.</item>
    ///   <item><b>ManufacturabilityFloor</b> — violating it means the design is unprintable on LPBF (feature size, overhang, drain path, voxel resolution).</item>
    ///   <item><b>AdvisoryHeuristic</b> — violating it indicates a design-rule-of-thumb concern; experienced designers can override.</item>
    /// </list>
    /// </remarks>
    public static GateKind GetGateKind(string constraintId) => constraintId switch
    {
        // ── PhysicsLimit ─ first-principles failures of real hardware ──
        "WALL_TEMP"                          => GateKind.PhysicsLimit,
        "YIELD_EXCEEDED"                     => GateKind.PhysicsLimit,
        "BURST_MARGIN_INSUFFICIENT"          => GateKind.PhysicsLimit,
        "COOLANT_T_EXCEEDED"                 => GateKind.PhysicsLimit,
        "INJECTOR_FACE_T_EXCEEDED"           => GateKind.PhysicsLimit,
        "FEED_PRESSURE_INSUFFICIENT"         => GateKind.PhysicsLimit,
        "BLOW_DOWN_INSUFFICIENT"             => GateKind.PhysicsLimit,
        "PURGE_FLOW_INSUFFICIENT"            => GateKind.PhysicsLimit,
        "ABLATIVE_BURNTHROUGH"               => GateKind.PhysicsLimit,
        "NPSH_INSUFFICIENT"                  => GateKind.PhysicsLimit,
        "PUMP_PRESSURE_INVERTED"             => GateKind.PhysicsLimit,
        "TURBINE_POWER_DEFICIT"              => GateKind.PhysicsLimit,
        "EXPANDER_TURBINE_ENTHALPY_DEFICIT"  => GateKind.PhysicsLimit,
        "TURBINE_UNCHOKED"                   => GateKind.PhysicsLimit,
        "PREBURNER_WALL_TEMP"                => GateKind.PhysicsLimit,
        "TAPOFF_HOT_GAS_TOO_HOT"             => GateKind.PhysicsLimit,
        "AEROSPIKE_PLUG_WALL_TEMP"           => GateKind.PhysicsLimit,
        "AEROSPIKE_COOLANT_CAVITATION_RISK"  => GateKind.PhysicsLimit,
        "AEROSPIKE_INJECTOR_FACE_TEMP"       => GateKind.PhysicsLimit,
        "COMMON_SHAFT_RPM_INCONSISTENT"      => GateKind.PhysicsLimit,

        // ── EmpiricalBand ─ correlation calibration windows ──
        "ELEMENT_DENSITY_TOO_HIGH"           => GateKind.EmpiricalBand,
        "PINTLE_BLOCKAGE_OUT_OF_BAND"        => GateKind.EmpiricalBand,
        "PINTLE_TMR_OUT_OF_BAND"             => GateKind.EmpiricalBand,
        "STABILITY_FAIL"                     => GateKind.EmpiricalBand,
        "G_INJ_TOO_LOW"                      => GateKind.EmpiricalBand,
        "G_INJ_TOO_HIGH"                     => GateKind.EmpiricalBand,
        "L_STAR_BELOW_PROPELLANT_MIN"        => GateKind.EmpiricalBand,
        "CONTRACTION_RATIO_OUT_OF_BAND"      => GateKind.EmpiricalBand,
        "PUMP_SPECIFIC_SPEED_OFF_BAND"       => GateKind.EmpiricalBand,
        "ORSC_PREBURNER_OXCORROSION"         => GateKind.EmpiricalBand,
        "LINEAR_AEROSPIKE_ASPECT_RATIO"      => GateKind.EmpiricalBand,

        // ── ManufacturabilityFloor ─ LPBF + geometric printability ──
        "FEATURE_TOO_SMALL"                  => GateKind.ManufacturabilityFloor,
        "TPMS_CELL_FEATURE_TOO_SMALL"        => GateKind.ManufacturabilityFloor,
        "VOXEL_RESOLUTION"                   => GateKind.ManufacturabilityFloor,
        "OVERHANG_ANGLE_EXCEEDED"            => GateKind.ManufacturabilityFloor,
        "TRAPPED_POWDER_REGION"              => GateKind.ManufacturabilityFloor,
        "DRAIN_PATH_MISSING"                 => GateKind.ManufacturabilityFloor,
        "CHANNEL_ASPECT_RATIO_EXCEEDED"      => GateKind.ManufacturabilityFloor,
        "AEROSPIKE_ELEMENT_CLEARANCE"        => GateKind.ManufacturabilityFloor,
        "MONOLITHIC_BODY_INTERSECTION"       => GateKind.ManufacturabilityFloor,
        "MONOLITHIC_TUBE_INTERSECTION"       => GateKind.ManufacturabilityFloor,

        // ── AdvisoryHeuristic ─ design rules of thumb, overrideable ──
        "IGNITER_MISSING"                    => GateKind.AdvisoryHeuristic,
        "IGNITER_ENERGY_INSUFFICIENT"        => GateKind.AdvisoryHeuristic,
        "IGNITER_MODALITY_UNSUITABLE"        => GateKind.AdvisoryHeuristic,
        "INSTRUMENTATION_TAP_INTERFERENCE"   => GateKind.AdvisoryHeuristic,
        "INSTRUMENTATION_THERMAL_BRIDGE_RISK"=> GateKind.AdvisoryHeuristic,
        "TPMS_AND_MANIFOLD_OVERLAP"          => GateKind.AdvisoryHeuristic,
        "BIMETALLIC_BOND_ZONE_SHEAR"         => GateKind.AdvisoryHeuristic,
        "CHILLDOWN_BUDGET_EXCEEDED"          => GateKind.AdvisoryHeuristic,
        "HARD_START_RISK"                    => GateKind.AdvisoryHeuristic,
        "SHAFT_WHIRL"                        => GateKind.AdvisoryHeuristic,

        // Unknown / future gates default to AdvisoryHeuristic (most
        // conservative — won't claim physics-grade rigour for an
        // unclassified gate). When adding a new ConstraintId, classify
        // it above per the rubric in the doc-comment.
        _ => GateKind.AdvisoryHeuristic,
    };

    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// T1.5 (2026-04-27) — cheap progressive-fidelity pre-screen.
    ///
    /// Runs a curated subset of feasibility checks that depend ONLY on
    /// (<paramref name="cond"/>, <paramref name="design"/>) — no
    /// thermal-solver, structural, or cycle outputs needed. Returns
    /// <c>null</c> when the design clears every cheap check; returns the
    /// first violating gate when it doesn't.
    ///
    /// Production SA loop short-circuits the ~50-200 ms thermal solver
    /// when this returns non-<c>null</c>, capturing the bulk of
    /// obviously-infeasible candidates at ~10 µs per call. On
    /// convergent SA runs that reject 60-80 % of candidates this
    /// compounds with multi-chain SA's 4-8× speedup for ~2-3× total
    /// throughput improvement on top of multi-chain.
    ///
    /// Gates included (chosen because they fire often on SA-generated
    /// candidates AND need no derived state):
    /// <list type="bullet">
    /// <item>CONTRACTION_RATIO_OUT_OF_BAND — design ε_c outside [2.5, 10.0]</item>
    /// <item>L_STAR_BELOW_PROPELLANT_MIN — design L* below 95 % of pair nominal</item>
    /// <item>TPMS_CELL_FEATURE_TOO_SMALL — TPMS strut below LPBF floor (only when TPMS topology selected)</item>
    /// </list>
    ///
    /// The full <see cref="Evaluate"/> still re-runs these gates against
    /// the post-solver result; the redundancy is intentional and costs
    /// ~µs per pass.
    /// </summary>
    public static FeasibilityViolation? PreScreen(OperatingConditions cond, RegenChamberDesign design)
    {
        if (cond == null || design == null) return null;

        // 1. CONTRACTION_RATIO_OUT_OF_BAND. SA samples [3.0, 10.0] so the
        //    floor (2.5) is unreachable from SA — but legacy / loaded
        //    designs can land below; pre-screen catches both edges.
        if (design.ContractionRatio < ContractionRatioFloor)
        {
            return new FeasibilityViolation(
                ConstraintId: "CONTRACTION_RATIO_OUT_OF_BAND",
                Description:
                    $"[pre-screen] Contraction ratio ε_c = {design.ContractionRatio:F2} below "
                  + $"{ContractionRatioFloor:F1} (Sutton §8.2 / Huzel & Huang §4.1).",
                ActualValue:  design.ContractionRatio,
                Limit:        ContractionRatioFloor);
        }
        if (design.ContractionRatio > ContractionRatioCeiling)
        {
            return new FeasibilityViolation(
                ConstraintId: "CONTRACTION_RATIO_OUT_OF_BAND",
                Description:
                    $"[pre-screen] Contraction ratio ε_c = {design.ContractionRatio:F2} above "
                  + $"{ContractionRatioCeiling:F1} (Sutton §8.2).",
                ActualValue:  design.ContractionRatio,
                Limit:        ContractionRatioCeiling);
        }

        // 2. L_STAR_BELOW_PROPELLANT_MIN. SA bounds [0.7, 1.6]; LOX/CH4
        //    nominal is 1.10 m so values 0.7-1.045 fire. Real driver of
        //    pre-screen-rejected candidate volume on convergent runs.
        double lStarNominal = AutoSeeder.CharacteristicLengthFor(cond.PropellantPair);
        double lStarFloor   = lStarNominal * LStarFloorFraction;
        if (design.CharacteristicLength_m > 0 && design.CharacteristicLength_m < lStarFloor)
        {
            return new FeasibilityViolation(
                ConstraintId: "L_STAR_BELOW_PROPELLANT_MIN",
                Description:
                    $"[pre-screen] L* = {design.CharacteristicLength_m:F2} m below "
                  + $"{LStarFloorFraction * 100:F0} % of {cond.PropellantPair} nominal "
                  + $"{lStarNominal:F2} m.",
                ActualValue:  design.CharacteristicLength_m,
                Limit:        lStarFloor);
        }

        // 3. TPMS_CELL_FEATURE_TOO_SMALL. Skip on non-TPMS topologies.
        bool isTpms = design.ChannelTopology == ChannelTopology.TpmsGyroid
                   || design.ChannelTopology == ChannelTopology.TpmsSchwarzP
                   || design.ChannelTopology == ChannelTopology.TpmsSchwarzD;
        if (isTpms)
        {
            // Z1.1 fix (post-#107): pre-screen MUST call the same helper as
            // the full-eval gate at line ~1174. The previous inline formula
            // (1 − sf) × ce computed the VOID size, not the strut size, so
            // pre-screen rejected the inverse of what full-eval rejected.
            // See RegenChamberDesign.TpmsSolidFraction docstring: sf is the
            // SOLID volume fraction; strut = sf × ce.
            double strut_mm = HeatTransfer.TpmsCorrelations.StrutThickness_mm(
                cellEdge_mm:   design.TpmsCellEdge_mm,
                solidFraction: design.TpmsSolidFraction);
            if (strut_mm < HeatTransfer.TpmsCorrelations.MinStrutThickness_mm)
            {
                return new FeasibilityViolation(
                    ConstraintId: "TPMS_CELL_FEATURE_TOO_SMALL",
                    Description:
                        $"[pre-screen] TPMS strut = {strut_mm:F2} mm < "
                      + $"{HeatTransfer.TpmsCorrelations.MinStrutThickness_mm:F1} mm LPBF floor "
                      + $"(= {design.TpmsSolidFraction:F2} × {design.TpmsCellEdge_mm:F2} mm cell).",
                    ActualValue:  strut_mm,
                    Limit:        HeatTransfer.TpmsCorrelations.MinStrutThickness_mm);
            }
        }

        return null;
    }

    /// <summary>
    /// Evaluate every registered rocket-regen feasibility gate against a
    /// fully-generated design result. All violations are collected (not
    /// fail-fast) so callers can diagnose multi-constraint failures at once.
    /// </summary>
    /// <remarks>
    /// Sprint 0 PR-1 (ADR-019): the original 1,150-line if-chain was
    /// migrated to <see cref="GateRegistry"/> via <see cref="RocketGates"/>.
    /// Per-gate emit logic now lives in static methods on RocketGates;
    /// this entry point is a thin dispatcher over <see cref="GateRegistry.All"/>
    /// filtered by <see cref="EngineFamilyMask.Rocket"/>. Order is pinned
    /// by GateOrderingSnapshotTests + Registry_OrderingMatchesDeclarationSequence.
    /// </remarks>
    public static FeasibilityGateResult Evaluate(RegenGenerationResult gen)
    {
        // Pre-size at 4 — typical feasible designs collect 0-2 violations;
        // pre-sizing avoids the 0 → 4 → 8 growth on the hot path (matches
        // the P9 pattern already applied to the warnings lists across the
        // physics solvers).
        var violations = new System.Collections.Generic.List<FeasibilityViolation>(capacity: 4);

        foreach (var gate in GateRegistry.All)
        {
            if ((gate.Applicability & EngineFamilyMask.Rocket) == 0) continue;
            gate.Emit(gen, violations);
        }

        return new FeasibilityGateResult(
            IsFeasible: violations.Count == 0,
            Violations: violations.ToArray());
    }

    // A3 (deferred from post-Phase-6 correctness bundle): auto-swap
    // SupercriticalPizzarelli for stations where the bulk (T, P) sits in
    // the fluid's pseudocritical band. The detection exists
    // (gen.Thermal.Diagnostics.StationsInPseudocritical) and the
    // correlation is wired into CoolantCorrelations, but no caller selects
    // it. Adding it as a feasibility gate would flip IsFeasible on every
    // high-Pc preset; the proper fix is per-station auto-selection inside
    // RegenCoolingSolver, deferred to its own sprint with bench-baseline
    // rebaselining.
}
