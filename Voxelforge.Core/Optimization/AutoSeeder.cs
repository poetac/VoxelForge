// AutoSeeder.cs — Produce defensible defaults for (OperatingConditions,
// RegenChamberDesign) from four high-level spec inputs:
//   • propellant pair (enum)
//   • target thrust (N)
//   • chamber pressure (Pa)
//   • expansion ratio ε
//
// Goal: "no human intervention" autonomous generation — given just a
// specification, the tool produces a feasible chamber without
// requiring a human to pick contraction ratio, L*, channel count,
// wall thickness, bell angles, etc. All heuristics are documented
// with textbook references (Sutton 9e; Huzel & Huang; Hill & Peterson).
//
// Non-goals
// ─────────
// This seeder does NOT run SA, the regen solver, or feasibility gates.
// Callers compose:
//   var (cond, design) = AutoSeeder.Seed(spec);
//   var result         = RegenChamberOptimization.GenerateWithAutoCoarsen(
//                            cond, design, voxel_mm);
//   var score          = RegenChamberOptimization.Evaluate(result);
//
// If the user then wants SA polish, they seed the optimizer with the
// same (cond, design) via `Optimizer.SetInitialCandidate`.

using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.Injector;

namespace Voxelforge.Optimization;

/// <summary>
/// Four-input specification bundle — all that's required to hand the
/// tool a rocket engine design problem. Other parameters (coolant inlet
/// T/P, MR, L*, etc.) are synthesized from these and public-domain
/// heuristic rules; the user can override any of the outputs via a
/// subsequent `with` expression before calling `GenerateWith`.
///
/// Optional <see cref="ElementTypeOverride"/> lets callers pin the
/// injector element type. Null (default) uses the AutoSeeder's
/// propellant-pair heuristic.
///
/// Optional <see cref="ChannelTopologyOverride"/> pins the cooling
/// topology. Null (default) leaves the seeder on the Axial baseline.
/// Setting a TPMS value also populates
/// <see cref="HeatTransfer.TpmsCorrelations"/>-calibrated unit-cell
/// size + solid fraction that satisfy the LPBF strut-thickness gate
/// out of the box.
/// </summary>
public sealed record EngineSpec(
    PropellantPair              PropellantPair,
    double                      Thrust_N,
    double                      ChamberPressure_Pa,
    double                      ExpansionRatio,
    string?                     ElementTypeOverride      = null,
    ChannelTopology?            ChannelTopologyOverride  = null,
    /// <summary>
    /// Optional engine-cycle override. Null (default) leaves the
    /// seeder on PressureFed. Setting GasGenerator / StagedCombustion
    /// / FullFlow auto-populates the preburner MR + Pc defaults on
    /// OperatingConditions so a subsequent GenerateWith produces a
    /// populated Preburner result.
    /// </summary>
    FeedSystem.EngineCycle?     EngineCycleOverride      = null);

/// <summary>
/// Bundle returned by <see cref="AutoSeeder.Seed"/> — the two records
/// required by <see cref="RegenChamberOptimization.GenerateWith"/>,
/// plus a narrative trail the CLI / UI can surface so the user can see
/// why each default was picked.
///
/// <see cref="UseEquilibriumRecommended"/> mirrors what the seeder
/// wants <see cref="PropellantTables.UseEquilibrium"/> to be set to
/// before the subsequent
/// <see cref="RegenChamberOptimization.GenerateWith"/> call. True at
/// Pc &gt; 10 MPa (where equilibrium correction meaningfully shifts
/// predictions); false otherwise. Callers apply the flag themselves
/// so they retain full control — AutoSeeder never mutates global
/// state.
/// </summary>
public sealed record AutoSeedResult(
    OperatingConditions   Conditions,
    RegenChamberDesign    Design,
    IReadOnlyList<string> Rationale,
    bool                  UseEquilibriumRecommended = false);

/// <summary>
/// Pure-math default seeder. Deterministic for a given input; safe to
/// call from any thread; no PicoGK / Library dependency.
/// </summary>
public static class AutoSeeder
{
    /// <summary>Minimum accepted thrust (N). Below this, heuristics break down.</summary>
    public const double MinThrust_N = 10.0;

    /// <summary>
    /// Maximum accepted thrust (N). 10 MN covers Saturn V F-1 class (6.77 MN nominal) plus
    /// headroom for aerospike-equivalent design exploration. Earlier 5 MN cap forced fixture
    /// workarounds in <c>PublishedEngineValidationTests.BuildSeed</c> for F-1; the seed-then-
    /// restore pattern remains safe and extends to any heavier published reference.
    /// </summary>
    public const double MaxThrust_N = 10_000_000.0;   // 10 MN / Saturn V F-1-class upper bound

    /// <summary>Minimum accepted chamber pressure (Pa).</summary>
    public const double MinPc_Pa = 0.5e6;

    /// <summary>Maximum accepted chamber pressure (Pa).</summary>
    public const double MaxPc_Pa = 30e6;

    /// <summary>Minimum accepted expansion ratio.</summary>
    public const double MinExpansion = 1.5;

    /// <summary>Maximum accepted expansion ratio.</summary>
    public const double MaxExpansion = 250.0;

    /// <summary>
    /// Run the AutoSeeder. Throws <see cref="ArgumentOutOfRangeException"/>
    /// if the spec falls outside the accepted envelope; throws
    /// <see cref="NotSupportedException"/> if the propellant pair is
    /// declared but not yet implemented.
    /// </summary>
    public static AutoSeedResult Seed(EngineSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (spec.Thrust_N < MinThrust_N || spec.Thrust_N > MaxThrust_N)
            throw new ArgumentOutOfRangeException(nameof(spec.Thrust_N),
                $"Thrust {spec.Thrust_N:F0} N out of supported range [{MinThrust_N:F0}, {MaxThrust_N:F0}] N.");
        if (spec.ChamberPressure_Pa < MinPc_Pa || spec.ChamberPressure_Pa > MaxPc_Pa)
            throw new ArgumentOutOfRangeException(nameof(spec.ChamberPressure_Pa),
                $"Pc {spec.ChamberPressure_Pa / 1e6:F1} MPa out of supported range "
              + $"[{MinPc_Pa / 1e6:F1}, {MaxPc_Pa / 1e6:F1}] MPa.");
        if (spec.ExpansionRatio < MinExpansion || spec.ExpansionRatio > MaxExpansion)
            throw new ArgumentOutOfRangeException(nameof(spec.ExpansionRatio),
                $"ε = {spec.ExpansionRatio:F1} out of supported range [{MinExpansion:F1}, {MaxExpansion:F1}].");

        var meta = PropellantPairs.GetMeta(spec.PropellantPair);
        if (!meta.Implemented)
            throw new NotSupportedException(
                $"Propellant pair {spec.PropellantPair} ({meta.Name}) is declared but its CEA table "
              + $"is not populated — AutoSeeder cannot generate defaults for it. Implemented pairs: "
              + $"LOX_CH4, LOX_H2, LOX_RP1.");

        var rationale = new List<string>();

        // ── 1. Propellant-pair-driven defaults ─────────────────────
        double mixtureRatio   = meta.MR_AtPeakCStar;
        double coolantInletT  = CoolantInletTempFor(spec.PropellantPair);
        // Sprint feasibility-audit-F (2026-04-26 night): cycle-aware
        // jacket-inlet pressure. The closed-expander turbine back-
        // pressure is Pc × 1.10 (chamber injection); the jacket outlet
        // must exceed that for forward expansion through the turbine.
        // The non-cycle-aware default `max(Pc × 1.6, 8 MPa)` assumed
        // the only downstream of the jacket is the injector, which is
        // true for pressure-fed and preburner-cycle (gas-generator,
        // ORSC, tap-off) topologies but NOT for expander cycles where
        // the jacket-outlet flow first crosses a turbine. With typical
        // jacket ΔP ≈ 4 MPa (RL10 measured), the prior default left
        // RL10 with a jacket outlet 3.8 MPa < 4.4 MPa back-pressure →
        // ExpanderCycleSizing's no-forward-expansion early-return
        // path → AvailableShaftPower = 0 → EXPANDER_TURBINE_ENTHALPY_-
        // DEFICIT firing at 100 % of SA candidates. Fix: cycle-aware
        // jacket-inlet defaults that match the canonical real engines
        // (RL10 fuel-pump discharge ≈ 4× Pc; J-2 open-expander class
        // ≈ 3× Pc). See CoolantInletPressureFor for the per-cycle
        // formula.
        double coolantInletP  = CoolantInletPressureFor(spec);
        int    wallMaterial   = WallMaterialFor(spec.PropellantPair, spec.ChamberPressure_Pa, spec.Thrust_N);
        double lStar_m        = CharacteristicLengthFor(spec.PropellantPair);

        // ── 2. Thrust-driven contraction + dimensioning ────────────
        // Contraction ratio scales weakly with thrust. Huzel & Huang
        // §2.3 recommends 8–10 for modern LREs; small thrusters
        // (≤ 1 kN) benefit from ~6 to avoid over-large barrels.
        double contraction = ContractionRatioFor(spec.Thrust_N);

        // ── Mode-overlap avoidance (L1/T1 acoustic resonance) ──────────────
        // For mid-size engines with high contraction ratio the chamber can be
        // near-square (D_c ≈ L_c), causing the first longitudinal acoustic
        // mode (L1 = c/2L_c) and first transverse mode (T1 = 1.841c/πD_c)
        // to accidentally coincide. STABILITY_FAIL fires when |L1-T1|/T1
        // < 10%, blocking ALL SA candidates. The resonance is a pure
        // geometry condition: it occurs at D_c/L_c = 2×1.841/π ≈ 1.172,
        // with a danger zone of D_c/L_c ∈ (1.055, 1.289). Fix: increase L*
        // so the barrel elongates past the lower danger boundary. Empirical
        // barrel factor (0.64) fitted to canonical-preset contour geometry.
        // TargetDcLc = 1.00 gives ~15% mode separation — clear of the gate.
        {
            const double cfMin        = 1.5;    // conservative Cf for throat sizing
            const double barrelFactor = 0.64;   // L_barrel ≈ L*/CR × 0.64 (empirical)
            const double targetDcLc   = 1.00;   // well below the 1.055 lower danger bound
            const double dangerLo     = 1.055;
            const double dangerHi     = 1.289;
            double rT_est = Math.Sqrt(spec.Thrust_N / (cfMin * spec.ChamberPressure_Pa * Math.PI));
            double dC_est = 2.0 * rT_est * Math.Sqrt(contraction);
            double lC_est = lStar_m / contraction * barrelFactor;
            double dcLc   = dC_est / lC_est;
            if (dcLc > dangerLo && dcLc < dangerHi)
            {
                double lStarNeeded = dC_est * contraction / (barrelFactor * targetDcLc);
                lStarNeeded = Math.Min(lStarNeeded, 2.5);   // clamp to SA-safe upper bound
                if (lStarNeeded > lStar_m)
                {
                    rationale.Add(
                        $"L* adjusted {lStar_m:F2} → {lStarNeeded:F2} m to avoid L1/T1 mode-overlap "
                      + $"(estimated D_c/L_c = {dcLc:F3}; danger zone {dangerLo:F3}–{dangerHi:F3}; "
                      + $"target {targetDcLc:F3}; Sutton §9.4 + NASA SP-194 §3).");
                    lStar_m = lStarNeeded;
                }
            }
        }

        rationale.Add($"Propellant {meta.Name}: MR={mixtureRatio:F2} at peak C*, "
                    + $"L*={lStar_m:F2} m, coolant inlet {coolantInletT:F0} K / "
                    + $"{coolantInletP / 1e6:F1} MPa.");
        rationale.Add($"Wall material: {WallMaterialLabelFor(wallMaterial)} "
                    + $"(selected for coolant {meta.CoolantFluidKey}).");
        rationale.Add($"Contraction ratio {contraction:F1} "
                    + $"(thrust class {ThrustClassLabel(spec.Thrust_N)}).");

        // Bell contour: θ_n (entrance angle to bell) grows with ε;
        // θ_e (exit angle) shrinks with ε. Typical Rao TOP curves
        // from Sutton 9e Fig. 3-7.
        (double thetaN, double thetaE, double bellLenFrac) =
            BellGeometryFor(spec.ExpansionRatio);
        rationale.Add($"Bell: θ_n={thetaN:F1}°, θ_e={thetaE:F1}°, "
                    + $"length fraction {bellLenFrac:F2}.");

        // ── 3. Channel count + wall thicknesses ────────────────────
        // Wall + rib + jacket: start at the LPBF floor (0.5 mm) with some margin.
        // For high-k liners (GRCop-42 index=0, CuCrZr index=1) the wall thermal
        // resistance is negligible even at 0.6 mm (ΔT_wall < 100 K at 53 MW/m²),
        // so a thinner seed keeps T_wg well below the 1000 K service limit without
        // sacrificing structural margin.  Low-k materials (IN625, bimetallic) use
        // the full 0.8 mm to avoid excessive wall ΔT.
        double gasWall    = wallMaterial is 0 or 1 ? 0.6 : 0.8;
        double ribThick   = 0.8;

        // Sprint A-2 (#167, 2026-04-30): physics-aware wall scheduler.
        // The pre-A-2 jacket default of 2.0 / 2.5 mm was a thrust-class
        // heuristic that ignored chamber pressure × radius — causing
        // YIELD_EXCEEDED + BURST_MARGIN_INSUFFICIENT to fire at the
        // AutoSeeder seed on 4 of 5 canonical bench presets after the
        // Z1.2 bimetallic series-resistance correction. The scheduler
        // sizes the jacket from the cold-yield burst-margin requirement
        //   t_jacket ≥ 2.5 × MEOP × r_max / σ_y_cold − t_inner
        // and per-station inner-liner overrides from the steady-state
        // hoop margin
        //   t_inner ≥ 1.5 × ΔP_station × r_station / σ_y_T_wg
        // both clamped to the SA variable bounds [0.5..8.0 mm liner,
        // 1.0..6.0 mm jacket]. Inner liner stays as thin as feasibility
        // allows so wall ΔT (and hence thermal stress) stay low at the
        // throat.
        var (tCham_mm, tThr_mm, tExit_mm, jacket) =
            WallThicknessSchedule(spec, wallMaterial, gasWall, contraction);
        rationale.Add($"Wall scheduler: chamber {tCham_mm:F2} / throat {tThr_mm:F2} / "
                    + $"exit {tExit_mm:F2} mm liner + {jacket:F2} mm jacket "
                    + $"(σ_y-sized for 2.5× burst margin + 1.5× yield SF, post-Z1.2).");
        rationale.Add($"Initial wall/rib/jacket: {gasWall:F2}/{ribThick:F2}/{jacket:F2} mm "
                    + $"(LPBF-floor + margin; SA will refine).");

        // Rule of thumb: channel count ≈ 40 × √(F/1 kN), clamped
        // to [40, 120] — matches SA variable [6] range. Geometry cap
        // added (Pc-aware) to keep channel width ≥ 0.30 mm (LPBF floor)
        // at the estimated throat so the seed avoids FEATURE_TOO_SMALL.
        int channelCount = ChannelCountFor(spec.Thrust_N, spec.ChamberPressure_Pa, ribThick);
        rationale.Add($"Channel count {channelCount} "
                    + $"(from ≈ 40 × √(F / 1 kN), clamped to SA range and LPBF width floor).");

        // Channel heights: taper from chamber → throat → exit. Throat
        // height is the tightest because q̇ peaks there.
        (double hCham, double hThr, double hExit) = ChannelHeightsFor(spec.Thrust_N);
        rationale.Add($"Channel heights: chamber {hCham:F2} mm, throat {hThr:F2} mm, "
                    + $"exit {hExit:F2} mm (taper set for ≈ 1.5-2.5 mm throat floor).");

        // ── 4. Port / flange / manifold defaults ───────────────────
        // Port diameter scales with mass-flow rate, which scales with
        // thrust. Use 6 mm for ≤ 2 kN, 10 mm for ≤ 20 kN, 20 mm
        // otherwise — gives ≈ 10-20 m/s plenum velocities.
        double portDia = PortDiameterFor(spec.Thrust_N);
        double propPortDia = PropPortDiameterFor(spec.Thrust_N);
        double flangeThk = FlangeThicknessFor(spec.Thrust_N);
        rationale.Add($"Coolant port Ø {portDia:F1} mm, propellant port Ø {propPortDia:F1} mm, "
                    + $"injector flange {flangeThk:F1} mm thick.");

        // ── 4.5. Injector element pattern ──────────────────────────
        string elementType = spec.ElementTypeOverride
                          ?? InjectorElementTypeFor(spec.PropellantPair);
        int    elementCount = ElementCountForInjector(spec.Thrust_N, elementType);
        double filmFraction = OuterRowFilmFractionFor(spec.PropellantPair, spec.ChamberPressure_Pa);
        var layout = InjectorLayoutFor(elementType);
        var injectorPattern = new InjectorPattern
        {
            ElementType          = elementType,
            ElementCount         = elementCount,
            OuterRowFilmFraction = filmFraction,
            DeltaPInjFraction    = 0.20, // standard 20 % ΔP_inj/Pc
            FaceLayout           = layout,
        };
        rationale.Add($"Injector: {elementType} × {elementCount} elements, "
                    + $"{layout} layout, {100 * filmFraction:F0} % outer-row film, "
                    + $"ΔP_inj/Pc = {100 * injectorPattern.DeltaPInjFraction:F0} %.");

        // ── 5. Assemble the records ────────────────────────────────
        // Sprint feasibility-audit-2 (2026-04-26): set a defensible
        // pump-inlet / tank-ullage pressure to provide enough NPSHA on
        // turbopump cycles. Default 300 kPa was inadequate — Merlin /
        // RL10 NPSHA was ~60 m vs NPSHR ~75-150 m even after multi-stage
        // + inducer. Real LRE turbopump-fed engines use 0.5-3 MPa helium
        // / autogenous pressurization. 1.5 MPa default unblocks NPSH on
        // all canonical turbopump-cycle presets.
        //
        // **Sprint feasibility-audit-6 (2026-04-26 evening):** route this
        // through `PumpInletPressure_Pa` for turbopump cycles, NOT
        // `TankUllagePressure_Pa`. Setting TankUllagePressure_Pa triggers
        // the pressure-fed-only PressureStackup model
        // (FEED_PRESSURE_INSUFFICIENT gate) — which interprets a
        // turbopump design with 1.5 MPa tank ullage as a pressure-fed
        // engine that can't reach Pc=7 MPa, firing the gate at 100 %.
        // PumpInletPressure_Pa is the correct knob for the pump's NPSH
        // input pressure on turbopump cycles; TankUllagePressure_Pa is
        // reserved for the pressure-fed stackup gate. PR #77 set the
        // wrong field; this PR routes it correctly.
        bool isTurbopump_forUllage = spec.EngineCycleOverride is not null
                                  && spec.EngineCycleOverride is not FeedSystem.EngineCycle.PressureFed
                                                             and not FeedSystem.EngineCycle.ElectricPump;
        double pumpInletDefault = isTurbopump_forUllage ? 1.5e6 : 0;
        // Sprint feasibility-audit-F (2026-04-26 night): for expander
        // cycles the fuel-pump discharge IS the regen-jacket inlet, so
        // route the bumped CoolantInletPressureFor result into
        // PumpDischargePressure_Pa as well. Otherwise TurbopumpSizing
        // sizes the pump for the much-lower Pc × 1.5 fallback (in
        // ResolvePumpDischarge) → cycle is energetically inconsistent
        // (jacket inlet 16 MPa fed by a 6 MPa pump). For non-expander
        // cycles the existing 0-default → ResolvePumpDischarge fallback
        // is preserved bit-identically. Both pumps share this discharge
        // (the model uses one dischargePressure_Pa for both pump sides);
        // this slightly over-sizes the OX pump on expander cycles vs
        // a real geared/split-turbine layout, but stays conservative
        // for the energy balance.
        bool isExpander = spec.EngineCycleOverride is FeedSystem.EngineCycle.ClosedExpander
                                                  or FeedSystem.EngineCycle.OpenExpander;
        double pumpDischargeDefault = isExpander ? coolantInletP : 0;
        var cond = new OperatingConditions
        {
            Thrust_N                = spec.Thrust_N,
            ChamberPressure_Pa      = spec.ChamberPressure_Pa,
            MixtureRatio            = mixtureRatio,
            CoolantInletTemp_K      = coolantInletT,
            CoolantInletPressure_Pa = coolantInletP,
            WallMaterialIndex       = wallMaterial,
            PropellantPair          = spec.PropellantPair,
            PumpInletPressure_Pa    = pumpInletDefault,
            PumpDischargePressure_Pa = pumpDischargeDefault,
            // Leave efficiencies at the defaults — user overrides if needed.
        };

        // Engine-cycle override. When the user opts into a preburner
        // cycle, set the cycle on cond + populate
        // the preburner MR default (so the subsequent GenerateWith has
        // everything it needs to emit `result.Preburner`). PressureFed
        // (default) leaves everything alone.
        // `preMr` is hoisted so it can also seed the SA design dim 20
        // override (`PreburnerMrRatio`) below — see the dim-20 seeding
        // comment in the `var design = ...` block.
        double preMr = 0.0;
        if (spec.EngineCycleOverride is { } cycleOverride)
        {
            preMr = Chamber.PreburnerChamber.SuggestPreburnerMr(
                cycleOverride, spec.PropellantPair);
            // Sprint 21: pull the Pc multiplier from the cycle solver so
            // adding a new cycle (Expander, ORSC, Tap-off) doesn't require
            // editing this seeder too.
            double prePc = spec.ChamberPressure_Pa
                         * FeedSystem.CycleSolvers.Get(cycleOverride).PreburnerPcMultiplier;
            cond = cond with
            {
                EngineCycle                = cycleOverride,
                PreburnerMrRatio           = preMr,
                PreburnerChamberPressure_Pa = prePc,
            };
            rationale.Add($"Engine cycle: {cycleOverride}"
                        + (preMr > 0
                            ? $" — preburner MR {preMr:F2}, Pc {prePc/1e6:F1} MPa "
                              + $"(turbine-safe T_c expected at or below "
                              + $"{Chamber.PreburnerChamber.TurbineInletTempLimit_K:F0} K)."
                            : " — no preburner."));
        }

        // Channel-topology dispatch. Override wins if supplied;
        // otherwise default to Axial for back-compat. TPMS overrides
        // also pick a unit-cell size that keeps strut thickness above
        // the 2.0 mm LPBF floor at the 0.50 solid fraction default.
        var topology = spec.ChannelTopologyOverride ?? ChannelTopology.Axial;
        double tpmsCellEdge_mm = 3.0;      // only consumed when topology is TPMS
        double tpmsSolidFraction = 0.50;
        double plugLengthRatio = 0.30;     // Aerospike default — truncated plug
        if (spec.ChannelTopologyOverride is { } topologyOverride
            && ChannelTopologyDispatcher.IsTpms(topologyOverride))
        {
            tpmsCellEdge_mm = TpmsCellEdgeFor(spec.Thrust_N);
            rationale.Add($"TPMS cooling: {topology}, "
                        + $"cell edge {tpmsCellEdge_mm:F1} mm, "
                        + $"solid fraction {tpmsSolidFraction:F2} → "
                        + $"strut {tpmsCellEdge_mm * tpmsSolidFraction:F2} mm "
                        + $"(LPBF floor {HeatTransfer.TpmsCorrelations.MinStrutThickness_mm:F1} mm).");
        }
        else if (spec.ChannelTopologyOverride is ChannelTopology.Aerospike)
        {
            // Aerospike override → seed a truncated-plug default (30 %
            // of the full-spike length). Geometry pipeline is
            // standalone (see `AerospikeBuilder`); the
            // RegenChamberDesign carries the topology + ratio so the
            // integration can dispatch from GenerateWith without
            // changing callers. Regen-specific fields (channels,
            // manifolds, ports) stay at their Axial-baseline defaults
            // because AerospikeBuilder doesn't consume them.
            // PlugLengthRatio is wired as an SA variable so the seeded
            // value is tunable.
            plugLengthRatio = 0.30;
            rationale.Add($"Aerospike plug-nozzle: plug length ratio {plugLengthRatio:F2} "
                        + $"(truncated — trades ~1 % C_F at vacuum for ~60 % hardware-length "
                        + $"reduction + printable flat base). Geometry pipeline via "
                        + $"AerospikeBuilder; SA tunes PlugLengthRatio via dim [22]; "
                        + $"full scoring integration deferred to a future sprint.");
        }

        // **Sprint feasibility-audit (2026-04-26):** enable film cooling by
        // default for high-heat-load designs. Prior default
        // (FilmCoolingInputs.Enabled = false) made the AutoSeeder produce
        // unrealistic chambers — every canonical preset (Pc ≥ 0.7 MPa) tripped
        // WALL_TEMP and INJECTOR_FACE_T_EXCEEDED on 100% of SA candidates
        // because the gas-side recovery T (~3,500 K) hit the wall directly,
        // with no boundary-layer film attenuation.
        //
        // Real production engines almost universally divert 5-15% of fuel as
        // boundary-layer film at the injector face. Heuristic for the seed:
        //   • Pc ≥ 3 MPa or Thrust ≥ 10 kN → enable, 8% film fraction.
        //   • Pc < 3 MPa (small thrusters, attitude/RCS class) → disable;
        //     small chambers can run pure regen at low Pc.
        // SA can tune the film fraction within bounds (today FilmCoolingInputs
        // isn't an SA dim — promotion to SA is a follow-up sprint).
        // Enable film for any chamber with non-trivial heat load. The 0.5 MPa
        // / 1 kN threshold catches everything from small attitude thrusters
        // upward — at lower Pc / thrust the seed is for cold-flow / vacuum-
        // chamber experiments that don't need film. Real LRE practice is
        // ~universal film cooling at the injector face for chambers that fire
        // at Pc > 0.5 MPa for more than a few seconds.
        bool enableFilm = spec.ChamberPressure_Pa >= 0.5e6
                       || spec.Thrust_N >= 1_000.0;
        double filmFrac = enableFilm ? 0.08 : 0.0;
        // Film cooling parameters need to scale with thrust for LRE main-
        // chamber applications. The FilmCoolingInputs defaults
        // (slot 0.6 mm, β = 0.15, burnout 200 mm) are calibrated for small
        // wind-tunnel film studies and decay η to ~10⁻³⁰ at LRE chamber
        // distances (273 mm to throat for 100 kN Merlin → η = 0). Real LRE
        // film slots are 5-15 mm wide with slower Stechman decay (β ≈ 0.05).
        //
        // **Sprint feasibility-audit-E (2026-04-27):** further lowered β
        // from 0.05 → 0.03 for production-class designs to match real-
        // engine η profiles. Stechman 1968's empirical β range [0.05, 0.30]
        // came from small-scale wind-tunnel measurements without:
        //   • Combustion at the film/core interface (consumes mixing
        //     turbulence; reduces effective β)
        //   • Severe adverse pressure gradient through the convergent
        //     section (delays mixing; reduces effective β)
        //   • Long confined-flow mixing lengths in real chambers
        // Production-class data (SSME, RL10, J-2 firing-test wall-T
        // probes) shows η ≈ 0.3-0.5 at the throat for designs with
        // 5-10 % film fraction and 5-15 mm slots. Back-solving the
        // Stechman formula against those points gives β ≈ 0.025-0.04.
        // β = 0.03 is the centre of that range and lands within the
        // PREFLIGHT_THERMAL peak_film_eta target band [0.3, 0.5] for
        // RL10 / merlin / pintle / aerospike at the peak heat-flux
        // station post-Sprint-E.
        //
        // **PHYSICS-INTEGRITY DISCLOSURE (2026-04-27):** the η = 0.3-0.5
        // target band cited above is INFERRED from published-engine
        // descriptions, NOT measured against firing-test wall-T probe data
        // available in this repo. The β = 0.03 value was chosen by inverting
        // the Stechman formula against that inferred target — a form of
        // empirical calibration that is only as strong as the cited target.
        // Furthermore, the calibration was done WITH a known upstream bug
        // (FilmCooling.Compute defaults to filmDensity_kgm3 = 10 when
        // RegenCoolingSolver.cs:282 omits the parameter, which is wrong for
        // every implemented propellant pair: real LCH4 ≈ 430, LH2 ≈ 70,
        // RP-1 ≈ 810). The two bugs partially cancel — fixing the density
        // alone would push η to ~0.7 (above target), so β must be
        // re-calibrated jointly. See `docs/physics-integrity-notes.md`
        // for the joint-calibration plan and recommended next-session work.
        //
        // Heuristic seeded values (SA can refine if these become design vars):
        //   • Slot height ≈ √(thrust_kN), clamped to [2.0, 15] mm.
        //     1 kN → 2.0 mm (floor), 100 kN → 10 mm, 1000 kN → 15 mm.
        //     **Sprint feasibility-audit-burnout (2026-04-27):** slot
        //     floor bumped 0.6 mm → 2.0 mm. Pre-fix at 1 kN
        //     pressure-fed-small produced slot = 1.0 mm + Stechman β
        //     = 0.03 → peak η ≈ 0.028 at the peak heat-flux station
        //     (~228 mm from injector face) — well below the [0.3, 0.5]
        //     production-class target. Real pressure-fed thrusters
        //     (Apollo SPS, ATV, LMDE-throttle) all use ≥ 2 mm slots
        //     for the practical reason that smaller slots can't
        //     accommodate the per-element fuel film mass flow without
        //     prohibitively high film velocities. 2 mm floor matches
        //     the small-thruster published-engine envelope and brings
        //     pressure-fed-small peak η into the 0.10-0.20 range
        //     (still below target, but the residual is an effect of
        //     the small-thrust / small-chamber Reynolds regime that
        //     the Stechman formula wasn't calibrated for).
        //   • Decay coefficient = 0.03 (low end of literature range,
        //     calibrated against real-engine η profiles per Sprint E).
        //   • Burnout length scales with thrust to ensure film survives
        //     past the peak-flux station (typically near the throat).
        double thrustKN = spec.Thrust_N / 1000.0;
        double slotHeight_mm = Math.Clamp(Math.Sqrt(thrustKN), 2.0, 15.0);
        // Burnout floor at 500 mm covers small-thrust pressure-fed engines
        // where the high expansion ratio (ε ≥ 25 typical) puts the peak
        // heat-flux station 200-300 mm downstream of the injector face,
        // past the prior 200 mm default. The linear additive scaling above
        // 500 mm catches large engines whose chamber + nozzle is genuinely
        // longer.
        double burnoutLen_mm = Math.Max(500.0, spec.Thrust_N * 7.5e-3);
        var filmInputs = new HeatTransfer.FilmCoolingInputs
        {
            Enabled            = enableFilm,
            FuelFractionAsFilm = filmFrac,
            FilmSlotHeight_mm  = slotHeight_mm,
            DecayCoefficient   = 0.03,
            BurnoutLength_mm   = burnoutLen_mm,
        };
        if (enableFilm)
            rationale.Add($"Film cooling enabled with {filmFrac:P0} fuel fraction, "
                        + $"slot {slotHeight_mm:F1} mm, β=0.05, burnout {burnoutLen_mm:F0} mm "
                        + $"(Pc {spec.ChamberPressure_Pa / 1e6:F1} MPa or thrust "
                        + $"{thrustKN:F0} kN trips the high-heat-load heuristic).");

        // **Sprint feasibility-audit-2 (2026-04-26):** turbopump defaults.
        // Pre-fix RegenChamberDesign.HasInducer = false + PumpStageCount = 1
        // gave NPSHR = 473 m for Merlin (real ≈ 30-60 m). Production LRE
        // turbopumps almost universally use inducers (S_s rises 8500 → 20000
        // → NPSHR drops 3.2×) and stage as needed for the head class.
        // Heuristic: enable inducer for any turbopump-cycle engine; pick
        // stages from propellant pair (LH2 needs ~3-4× more head per kg
        // than denser propellants → multi-stage).
        bool isTurbopump = spec.EngineCycleOverride is not null
                       && spec.EngineCycleOverride is not FeedSystem.EngineCycle.PressureFed
                                                  and not FeedSystem.EngineCycle.ElectricPump;
        bool hasInducer = isTurbopump;
        // Stage count scales with propellant + thrust class. LH2 always
        // multi-stage (low density → high head per kg). Other propellants
        // step up to 2 stages above ~50 kN where the discharge head climbs
        // toward the single-stage RPM/NPSHR limit.
        int pumpStages;
        if (!isTurbopump)
            pumpStages = 1;
        else if (spec.PropellantPair == Combustion.PropellantPair.LOX_H2)
            pumpStages = spec.Thrust_N >= 100_000 ? 3 : 2;
        else
            pumpStages = spec.Thrust_N >= 50_000 ? 2 : 1;
        if (isTurbopump)
            rationale.Add($"Turbopump defaults: inducer = {hasInducer}, stages = {pumpStages} "
                        + $"(LH2 → 2-stage for ΔH per kg; LOX/CH4 / LOX/RP-1 → 1-stage with inducer).");

        var design = new RegenChamberDesign
        {
            ContractionRatio             = contraction,
            ExpansionRatio               = spec.ExpansionRatio,
            CharacteristicLength_m       = lStar_m,
            BellEntranceAngle_deg        = thetaN,
            BellExitAngle_deg            = thetaE,
            BellLengthFraction           = bellLenFrac,
            ChannelCount                 = channelCount,
            ChannelHeightChamber_mm      = hCham,
            ChannelHeightThroat_mm       = hThr,
            ChannelHeightExit_mm         = hExit,
            RibThickness_mm              = ribThick,
            GasSideWallThickness_mm      = gasWall,
            OuterJacketThickness_mm      = jacket,
            PortDiameter_mm              = portDia,
            PropellantPortDiameter_mm    = propPortDia,
            InjectorFlangeThickness_mm   = flangeThk,
            IncludeInjectorFlange        = true,
            IncludeMountingFlange        = spec.Thrust_N >= 5000, // mount flange is useful above ~500 lbf
            IncludeManifolds             = true,
            IncludePorts                 = true,
            ChannelTopology              = topology,
            TpmsCellEdge_mm              = tpmsCellEdge_mm,
            TpmsSolidFraction            = tpmsSolidFraction,
            PlugLengthRatio              = plugLengthRatio,
            InjectorElementPattern       = injectorPattern,
            FilmCooling                  = filmInputs,
            // Dims 24-25: seed the SA film-override dims so Pack(seed) →
            // SetInitialCandidate → clamp → iter=0 evaluates at the same film
            // fraction and slot height that the preflight uses. Without these, both
            // dims are 0.0 (default), which clamps to their SA minimums (0.02/0.5)
            // on the first SA evaluation — changing the film fraction and shrinking
            // the slot from 3+ mm to 0.5 mm, diverging SA iter=0 from the preflight.
            //
            // For film fraction, the effective value is the LAST override that wins
            // inside RegenChamberOptimization.GenerateWith:
            //   1. FilmCooling.FuelFractionAsFilm (= filmFrac)
            //   2. InjectorPattern.OuterRowFilmFraction if > 0 (overrides 1)
            //   3. design.FilmFuelFraction if > 0 (overrides 2)
            // The seed must set dim 24 = step-2 result so SA starts at the same
            // effective fraction the preflight uses.
            FilmFuelFraction             = enableFilm
                                           ? (injectorPattern.OuterRowFilmFraction > 0
                                              ? injectorPattern.OuterRowFilmFraction
                                              : filmFrac)
                                           : 0.0,
            FilmSlotHeightOverride_mm    = enableFilm ? slotHeight_mm : 0.0,
            // Dims 26-27 (2026-04-28): seed pintle override dims so Pack(seed) →
            // SetInitialCandidate → clamp → iter=0 evaluates at the preflight's
            // pintle geometry (post diameter + sleeve hole count) instead of
            // clamping to the SA minimums (6.0 mm / 8 holes). Without these,
            // PINTLE_BLOCKAGE_OUT_OF_BAND fires on ~84-88 % of pintle SA
            // candidates from chain iter=0 — a seeding-bug regression that
            // pre-A1 the bench-pintle cleared. Same pattern as dim 24-25
            // film-cooling fix: capture the InjectorPattern defaults that
            // RegenChamberOptimization.GenerateWith would compute when the
            // override is 0, and write them into the override slots so the
            // seed-and-SA-iter-0 are physics-identical. Override fields are
            // gated to ElementType == "Pintle" inside GenerateWith, so for
            // non-Pintle presets these values are ignored at evaluation time
            // even though they ride along the SA vector. Setting them to 0
            // for non-Pintle preserves the legacy behaviour where SA dims
            // 26-27 stay at SA minimums for non-Pintle (irrelevant noise).
            PintleDiameterOverride_mm    = elementType == "Pintle"
                                           ? injectorPattern.PintleDiameter_mm
                                           : 0.0,
            PintleSleeveHoleCountOverride = elementType == "Pintle"
                                           ? injectorPattern.PintleSleeveHoleCount
                                           : 0.0,
            // Dims 28-30: Track B per-station wall thickness overrides.
            // Pre-Sprint-A-2 these were seeded uniformly to gasWall (0.6 mm
            // for Cu liners) which under post-Z1.2 physics drove
            // YIELD_EXCEEDED + BURST_MARGIN_INSUFFICIENT on every canonical
            // preset's seed (Cu σ_y vs hoop = MEOP × r_max / t requires
            // t ≥ several mm at chamber-class radii × MEOP). Sprint A-2 /
            // #167 sources these from WallThicknessSchedule which
            // physics-sizes each station at 1.5× yield + 2.5× burst margin
            // jointly, with the LPBF/SA bound clamps applied. Inner liner
            // stays as thin as feasibility allows so wall ΔT and thermal
            // stress at the throat stay low; the bulk of the hoop load
            // is carried by the (uniform) outer jacket.
            ChamberWallThicknessOverride_mm = tCham_mm,
            ThroatWallThicknessOverride_mm  = tThr_mm,
            ExitWallThicknessOverride_mm    = tExit_mm,
            // Dim 20 (handoff item #4 audit): PreburnerMrRatio defaults to
            // 0.0 with SA bound [0.30, 1.00]. Production reads
            // `design.PreburnerMrRatio > 0 ? design.PreburnerMrRatio :
            // cond.PreburnerMrRatio : SuggestPreburnerMr(...)` (RegenChamberOptimization
            // ResolveFuelRichMr). Preflight evaluates with design = 0 and
            // falls through to cond.PreburnerMrRatio (= preMr, set above);
            // SA iter=0 reads the SA-clamped 0.30 from design.PreburnerMrRatio
            // → preburner sized at the wrong MR. Same pattern as dims 24-30:
            // seed design from the same value the cond fallback uses so
            // both paths converge. For non-preburner cycles preMr is 0, so
            // SA iter=0 still clamps to 0.30 but production never reads
            // design.PreburnerMrRatio (HasFuelRich/HasOxRichPreburner both
            // false → early return) so the value is irrelevant.
            PreburnerMrRatio             = preMr,
            // Dim 21 (handoff item #4 audit): FlangeRadialProjection_mm
            // defaults to 0.0 with SA bound [8.0, 24.0]. Consumer
            // PumpMountFlange.Size does `radialProjection_mm > 0 ?
            // radialProjection_mm : DefaultRadialProjection_mm` so preflight
            // (design = 0) gets the 12.0 mm default; SA iter=0 reads the
            // SA-clamped 8.0 mm minimum, shrinking the flange radial
            // projection by 33 % from chain iter=0. Hardcode 12.0 (matches
            // PumpMountFlange.DefaultRadialProjection_mm in
            // Voxelforge.Voxels — Core can't reference Voxels so
            // the constant cannot be imported; the value is pinned by
            // AutoSeederSeedingFixTests). Non-turbopump presets don't
            // build pump-mount flanges, so the seeded value is irrelevant
            // there.
            FlangeRadialProjection_mm    = 12.0,
            HasInducer                   = hasInducer,
            PumpStageCount               = pumpStages,
            // Sprint feasibility-audit-B (2026-04-26 evening): seed
            // IgniterType per propellant pair from IgnitionRequirements.
            // Pre-fix the design default IgniterType.None failed the
            // IGNITER_MISSING gate, and downstream callers (CanonicalDesigns
            // .WithDefaultIgniter) hardcoded SparkTorch — which fails
            // IGNITER_MODALITY_UNSUITABLE on LOX/RP-1 (requires
            // AugmentedSpark min). Now AutoSeeder picks the per-pair
            // appropriate igniter directly so the canonical seed clears
            // both gates without downstream patchwork.
            IgniterType                  = DefaultIgniterFor(spec.PropellantPair),
            // Sprint feasibility-audit-8 (2026-04-26 evening): default
            // to polished-channel ε/D = 0.005 for production-class
            // designs. The RegenChamberDesign default 0.02 represents
            // AS-BUILT LPBF (Strauss et al. 2018, centre of band) which
            // is unrealistic for production LRE engines — they all use
            // chemical polish or AFM finishing on the regen channels
            // (real Merlin / RL10 / SSME ε/D ≈ 0.001-0.005). The
            // as-built default was producing unphysical 30+ MPa coolant
            // ΔP on canonical preset seeds, driving negative bulk
            // pressures and 100 % YIELD_EXCEEDED firing. AutoSeeder
            // now explicitly sets the polished value; the 0.02 default
            // remains for direct RegenChamberDesign instantiation
            // (preserves backward-compat with explicit-design tests).
            LpbfRelativeRoughness        = enableFilm ? 0.005 : 0.02,
        };

        rationale.Add($"Mounting flange: {(design.IncludeMountingFlange ? "included" : "omitted")} "
                    + $"(thrust gate: ≥ 5 kN).");

        // ── 6. Equilibrium CEA recommendation ─────────────────────
        // At Pc > 10 MPa, equilibrium-vs-frozen dissociation shifts
        // C* by ≥ 1-2 % — worth routing through the correction.
        // Below 10 MPa the frozen tables' log-linear Pc correction is
        // within engineering tolerance on its own.
        bool recommendEquilibrium = spec.ChamberPressure_Pa > 10e6;
        if (recommendEquilibrium)
            rationale.Add($"Equilibrium CEA recommended: Pc {spec.ChamberPressure_Pa / 1e6:F1} MPa "
                        + $"> 10 MPa threshold. Set PropellantTables.UseEquilibrium = true before "
                        + $"Generate() for calibrated dissociation correction.");
        else
            rationale.Add($"Frozen CEA sufficient: Pc {spec.ChamberPressure_Pa / 1e6:F1} MPa "
                        + $"< 10 MPa threshold (equilibrium shift < 1 %).");

        return new AutoSeedResult(cond, design, rationale, recommendEquilibrium);
    }

    // ───────────────────────── heuristics ──────────────────────────

    /// <summary>
    /// Coolant inlet temperature in K. Cryogenic fuels enter near
    /// their saturation point; RP-1 enters near ambient.
    /// </summary>
    internal static double CoolantInletTempFor(PropellantPair pair) => pair switch
    {
        PropellantPair.LOX_CH4 => 120.0,   // LCH4 at ~1.5 MPa, slightly subcooled
        PropellantPair.LOX_H2  =>  25.0,   // LH2 near saturation at 1 MPa
        PropellantPair.LOX_RP1 => 290.0,   // ambient RP-1
        _ => 290.0,
    };

    /// <summary>
    /// Sprint feasibility-audit-F (2026-04-26 night): cycle-aware
    /// jacket-inlet (= fuel-pump-discharge) pressure default.
    ///
    /// For non-expander cycles (PressureFed, GasGenerator, ORSC,
    /// Tap-off, Electric, Closed/Staged combustion) the only loads
    /// downstream of the regen jacket are the chamber + injector ΔP,
    /// so the pre-existing `max(Pc × 1.6, 8 MPa)` default sizes the
    /// pump correctly. For expander cycles the jacket-outlet flow
    /// FIRST crosses a turbine (Pc × 1.10 chamber-injection back-
    /// pressure for ClosedExpander; ambient for OpenExpander) THEN
    /// the injector. With typical jacket ΔP ≈ 4 MPa, the prior 8 MPa
    /// default left RL10 with a jacket outlet of 3.8 MPa &lt; 4.4 MPa
    /// closed-expander back-pressure → ExpanderCycleSizing's no-
    /// forward-expansion early-return path → AvailableShaftPower
    /// floored at 0 → EXPANDER_TURBINE_ENTHALPY_DEFICIT firing at
    /// 100 % of SA candidates.
    ///
    /// **Sprint feasibility-audit-F1 (2026-04-27):** multipliers
    /// raised from 4.0×/3.0× → 5.0×/4.0× (floors 14/12 MPa →
    /// 18/16 MPa) after PREFLIGHT_EXPANDER instrumentation revealed
    /// RL10 post-Sprint-F still produced an inadequate turbine
    /// pressure ratio (PR = 0.944, avail 372 kW vs req 2.4 MW). The
    /// jacket ΔP measurement at 16 MPa inlet was 11 MPa (vs the
    /// 4 MPa estimate the original Sprint F multiplier was sized
    /// against), leaving the turbine starved. Bumping inlet to ~20
    /// MPa for RL10 means jacket outlet ~8-9 MPa → turbine PR ~0.5
    /// → ~10× more specific work. Trade-off: the OX pump shaft
    /// power requirement also rises (single shared dischargePressure
    /// in TurbopumpSizing) but the expander has comfortable margin
    /// once turbine PR is healthy.
    ///
    /// Real-engine sizing references:
    ///   • RL10 (Pc 3.4 MPa, ClosedExpander): fuel-pump discharge
    ///     ≈ 14-15 MPa = 4-5× Pc. Drives turbine PR ≈ 2.5-3:1.
    ///   • J-2 (Pc 5.4 MPa, OpenExpander, partial): fuel-pump
    ///     discharge ≈ 16-22 MPa = 3-4× Pc.
    ///   • Vinci (Pc 6 MPa, ClosedExpander): fuel-pump discharge
    ///     ≈ 30 MPa = 5× Pc.
    ///
    /// Per-cycle multipliers + floors (18 / 16 / 8 MPa) chosen to
    /// match these references with margin for typical jacket ΔP.
    /// </summary>
    internal static double CoolantInletPressureFor(EngineSpec spec) => spec.EngineCycleOverride switch
    {
        FeedSystem.EngineCycle.ClosedExpander
            => Math.Max(spec.ChamberPressure_Pa * 5.0, 18e6),
        FeedSystem.EngineCycle.OpenExpander
            => Math.Max(spec.ChamberPressure_Pa * 4.0, 16e6),
        _   => Math.Max(spec.ChamberPressure_Pa * 1.6, 8e6),
    };

    /// <summary>
    /// Default wall-material index. 0=GRCop42, 1=CuCrZr, 2=Inc625, 3=Inc718.
    /// GRCop-42 for LOX/CH4 and LOX/H2 (highest k, 1000 K service limit);
    /// Inconel 625 for LOX/RP-1 (coking resistance over Cu alloys).
    ///
    /// **A1 physics correction (2026-04-27):** the prior bimetallic upgrade
    /// heuristic (Pc ≥ 5 MPa OR thrust ≥ 50 kN → index 4) is removed.
    /// A1 corrected the composite k_eff from a parallel area-weighted blend
    /// (~264 W/m·K) to the physically-correct series-resistance value
    /// (~13 W/m·K cold). With series k the composite creates a ~620 K
    /// temperature drop across the wall at throat heat-flux levels, driving
    /// thermal stress to ~900 MPa — far beyond GRCop-42's 180 MPa hot
    /// yield. No feasible wall thickness exists that simultaneously satisfies
    /// WALL_TEMP and YIELD_EXCEEDED for the composite in LPBF-printed
    /// integral-channel regen chambers. GRCop-42 mono-material (k=326 W/m·K)
    /// is the correct default: ΔT_wall ~25 K at the same heat flux, thermal
    /// stress ~30 MPa, and MaxServiceTemp 1000 K covers high-Pc designs.
    /// The bimetallic (index 4) remains in WallMaterials.All for users who
    /// explicitly want it (e.g., lower-heat-flux structural experiments).
    /// </summary>
    internal static int WallMaterialFor(PropellantPair pair, double pc_Pa = 0, double thrust_N = 0)
    {
        _ = pc_Pa;    // unused post-A1: bimetallic heuristic removed
        _ = thrust_N;
        return pair switch
        {
            PropellantPair.LOX_CH4 => 0, // GRCop-42: highest k, 1000 K service limit
            PropellantPair.LOX_H2  => 0, // GRCop-42: highest k, lowest density
            PropellantPair.LOX_RP1 => 2, // Inconel 625: coking resistance over Cu
            _ => 1,                       // CuCrZr: general fallback
        };
    }

    internal static string WallMaterialLabelFor(int idx) => idx switch
    {
        0 => "GRCop-42",
        1 => "CuCrZr",
        2 => "Inconel 625",
        3 => "Inconel 718",
        4 => "GRCop-42 / IN625 bimetallic",
        _ => "unknown",
    };

    /// <summary>
    /// **Sprint feasibility-audit-B (2026-04-26 evening):** per-pair default
    /// igniter selection. Routes through the Sprint 29 IgnitionRequirements
    /// table to pick the LOWEST modality that clears BOTH the
    /// IGNITER_MODALITY_UNSUITABLE gate (modality ≥ MinModality) AND the
    /// IGNITER_ENERGY_INSUFFICIENT gate (rated energy ≥ MinEnergy_J).
    ///
    /// For LOX/RP-1, MinModality is AugmentedSpark but MinEnergy is 500 J —
    /// AugmentedSpark only rates 5 J of capacitor energy, so PyrotechnicCartridge
    /// (1000 J chemical authority) is required to clear both gates. The
    /// two-criterion search captures this without hardcoding propellant pairs
    /// here. (PH-12 2026-04-29: units migrated mJ → J.)
    /// </summary>
    internal static Geometry.IgniterType DefaultIgniterFor(PropellantPair pair)
    {
        var req = Combustion.IgnitionRequirements.For(pair);
        if (req.IsHypergolic) return Geometry.IgniterType.None;
        // Walk modalities in ordinal order; first that clears both gates wins.
        Geometry.IgniterType[] candidates =
        {
            Geometry.IgniterType.SparkTorch,
            Geometry.IgniterType.AugmentedSpark,
            Geometry.IgniterType.PyrotechnicCartridge,
        };
        int minOrdinal = Combustion.IgnitionRequirements.ModalityOrdinal(req.MinModality);
        foreach (var t in candidates)
        {
            if (Combustion.IgnitionRequirements.ModalityOrdinal(t) < minOrdinal) continue;
            double rated = Geometry.IgniterPresets.All[t].IgnitionEnergy_J;
            if (rated >= req.MinEnergy_J) return t;
        }
        // No modality satisfies both — return the strongest (PyrotechnicCartridge).
        return Geometry.IgniterType.PyrotechnicCartridge;
    }

    /// <summary>
    /// Characteristic length L* in metres. Rule-of-thumb from Huzel
    /// &amp; Huang Table 4-1 and Sutton 9e Table 8-1:
    ///   LOX/CH4 ≈ 1.0-1.2 m (we use 1.1)
    ///   LOX/H2  ≈ 0.76-1.0 m (we use 0.9)
    ///   LOX/RP-1≈ 1.0-1.3 m (we use 1.2)
    /// </summary>
    internal static double CharacteristicLengthFor(PropellantPair pair) => pair switch
    {
        PropellantPair.LOX_CH4 => 1.1,
        PropellantPair.LOX_H2  => 0.9,
        PropellantPair.LOX_RP1 => 1.2,
        _ => 1.1,
    };

    /// <summary>
    /// TPMS unit-cell edge length (mm) by thrust class. Tuned so
    /// solid_fraction × cell_edge ≥ 2.0 mm LPBF floor at the 0.50
    /// solid fraction default — large thrusters benefit from bigger
    /// cells (more coolant mass through a sparser lattice), small
    /// thrusters use the 4.0 mm floor to stay manufacturable at all.
    /// </summary>
    internal static double TpmsCellEdgeFor(double thrustN)
    {
        if (thrustN <= 2000)   return 4.0;     // small — print-margin wins over density
        if (thrustN <= 20000)  return 4.5;
        if (thrustN <= 200000) return 5.0;
        return 6.0;                            // large — coolant mass flow dominates
    }

    /// <summary>
    /// Contraction ratio A_c/A_t. Huzel &amp; Huang §2.3:
    ///   F ≤ 2 kN  → 6 (compact)
    ///   F ≤ 20 kN → 8
    ///   F > 20 kN → 9
    /// </summary>
    internal static double ContractionRatioFor(double thrustN)
    {
        if (thrustN <= 2000)   return 6.0;
        if (thrustN <= 20000)  return 8.0;
        return 9.0;
    }

    /// <summary>
    /// Rao bell geometry presets by expansion ratio. PH-16 (2026-04-25):
    /// migrated from a 5-band step function to a bilinear interpolation
    /// over (ε, L%) via <see cref="Chamber.RaoBellTable"/>. Anchor values
    /// at the legacy band breakpoints (ε ∈ {≤5, ≤10, ≤25, ≤50, &gt;50})
    /// are preserved bit-for-bit at L%=0.80; off-band ε values now get
    /// a smooth interpolation instead of a step jump.
    /// </summary>
    /// <remarks>
    /// Bell length fraction (L%) is selected per ε band using the same
    /// heuristic the legacy step function implied — small expansion
    /// ratios get shorter bells (better tradeoff for low-ε engines).
    /// Future work could expose L% as a separate SA dimension or as a
    /// user-facing knob; for now AutoSeeder picks it.
    /// </remarks>
    internal static (double thetaN, double thetaE, double bellLengthFraction)
        BellGeometryFor(double expansionRatio)
    {
        double L =
            expansionRatio <= 5  ? 0.70 :
            expansionRatio <= 10 ? 0.80 :
            expansionRatio <= 25 ? 0.80 :
            expansionRatio <= 50 ? 0.82 :
                                   0.85;
        var (thetaN, thetaE) = Chamber.RaoBellTable.Lookup(expansionRatio, L);
        return (thetaN, thetaE, L);
    }

    /// <summary>
    /// Initial channel count. Approximately 40 × √(thrust / 1 kN),
    /// clamped to the SA variable [6] band of [40, 120]. At 500 N
    /// → 40; at 10 kN → ~126 (clamped to 120); at ≥ 9 kN → capped at
    /// 120. Upper bound was 180 pre-pattern-SDF; reduced to 120 once
    /// channel-cooling saturation made the higher count cosmetic.
    ///
    /// When <paramref name="pc_Pa"/> is provided the count is also capped by
    /// the LPBF min-width constraint (0.30 mm channel width) at the estimated
    /// throat, using C_F = 1.5 (conservative) + 1 mm nominal gas-side wall.
    /// This prevents the thermal solver from silently clamping geometric widths
    /// below the LPBF floor, which would trigger FEATURE_TOO_SMALL and make
    /// the seed infeasible before SA begins.
    /// </summary>
    internal static int ChannelCountFor(double thrustN, double pc_Pa = 0, double ribThickness_mm = 0.8)
    {
        double raw = 40.0 * Math.Sqrt(thrustN / 1000.0);
        int n = (int)Math.Round(raw);
        n = Math.Clamp(n, 40, 120);

        // Geometry cap: keep channel width ≥ 0.30 mm (LPBF floor) at the
        // estimated throat so the thermal solver never needs to clamp.
        if (pc_Pa > 0)
        {
            const double cfMin = 1.5; // conservative thrust coefficient for geometry sizing
            double rT_mm = 1000.0 * Math.Sqrt(thrustN / (cfMin * pc_Pa * Math.PI));
            double rOuter_mm = rT_mm + 1.0; // nominal 1 mm gas-side wall
            const double minWidth_mm = 0.30;
            int nGeo = (int)Math.Floor(2.0 * Math.PI * rOuter_mm / (ribThickness_mm + minWidth_mm));
            n = Math.Clamp(Math.Min(n, nGeo), 40, 120);
        }
        return n;
    }

    /// <summary>
    /// Channel-height taper (chamber, throat, exit) in mm. Throat is
    /// always the narrowest because q̇ peaks there. Scales loosely
    /// with chamber size (larger chambers afford larger channels).
    /// </summary>
    internal static (double chamber, double throat, double exit) ChannelHeightsFor(double thrustN)
    {
        // Small end (≤ 2 kN):  2.0 / 1.2 / 1.8
        // Mid   end (≤ 20 kN): 2.5 / 0.8 / 2.0 — throat tightened 1.5→0.8 mm
        //   so that GRCop-42-lined mid-class engines (MaxServiceTemp=1000 K) start
        //   with h_c high enough to hold T_wg below the material limit; the
        //   prior 1.5 mm seed produced T_wg≈1300 K and 0 SA feasible on pintle.
        //   0.8 mm is the SA variable [8] lower bound; SA can explore upward from here.
        // Large end (> 20 kN): 3.0 / 2.0 / 2.5
        if (thrustN <= 2000)   return (2.0, 1.2, 1.8);
        if (thrustN <= 20000)  return (2.5, 0.8, 2.0);
        return (3.0, 2.0, 2.5);
    }

    internal static double PortDiameterFor(double thrustN)
    {
        if (thrustN <= 2000)   return 6.0;
        if (thrustN <= 20000)  return 10.0;
        return 20.0;
    }

    internal static double PropPortDiameterFor(double thrustN)
    {
        if (thrustN <= 2000)   return 4.0;
        if (thrustN <= 20000)  return 6.0;
        return 12.0;
    }

    internal static double FlangeThicknessFor(double thrustN)
    {
        if (thrustN <= 2000)   return 6.0;
        if (thrustN <= 20000)  return 8.0;
        if (thrustN <= 100000) return 12.0;
        return 18.0;
    }

    internal static string ThrustClassLabel(double thrustN)
    {
        if (thrustN <= 2000)   return "small";
        if (thrustN <= 20000)  return "medium";
        if (thrustN <= 100000) return "large";
        return "xlarge";
    }

    // Injector element default per propellant pair.
    //
    //   LOX/CH4  → Coax shear (SpaceX Raptor heritage; widespread in methalox).
    //   LOX/H2   → Coax shear (RS-25 / Vulcain heritage; best for cryogenic H2).
    //   LOX/RP1  → Pintle (Merlin / Draco heritage; deep-throttling + throttle
    //              response; only one pintle per chamber so element count = 1).
    //
    // Users override via EngineSpec.ElementTypeOverride when they want
    // impinging doublets (Atlas / Delta heritage) or swirl (Russian
    // staged-combustion heritage) instead.
    internal static string InjectorElementTypeFor(PropellantPair pair) => pair switch
    {
        PropellantPair.LOX_CH4 => "Coax",
        PropellantPair.LOX_H2  => "Coax",
        PropellantPair.LOX_RP1 => "Pintle",
        _                      => "Coax",
    };

    /// <summary>
    /// Number of injector elements. Pintle is a special case: always 1
    /// element per chamber regardless of thrust (one pintle boss).
    /// Shear coax / doublets / swirl scale with √(thrust/1 kN) × 10 so
    /// small engines land ~20 elements and 100 kN engines land ~100.
    /// Clamped into the SA variable [13] range [8, 48] per
    /// Optimizer.Unpack (non-pintle types).
    /// </summary>
    internal static int ElementCountForInjector(double thrustN, string elementType)
    {
        if (elementType == "Pintle") return 1;
        double raw = 10.0 * Math.Sqrt(thrustN / 1000.0);
        return Math.Clamp((int)Math.Round(raw), 8, 48);
    }

    /// <summary>
    /// Outer-row film-cooling fuel fraction for the seed. Kerosene
    /// engines need more film because of coking; hydrogen engines can
    /// run leaner films. Range [0, 0.15] matches SA variable [15].
    /// </summary>
    internal static double OuterRowFilmFractionFor(PropellantPair pair, double pc_Pa = 0)
    {
        // pc_Pa parameter is currently unused but reserved for future
        // Pc-aware scaling. An earlier draft of this Sprint A-2 (#167)
        // change scaled film with Pc; that was reverted because it
        // shifted total mass flow on OOB-3 published-engine fixtures
        // (SSME, RD-180, NK-33, RS-68A, Vinci, Merlin-1D Vacuum) outside
        // their tolerance bands. The canonical preset feasibility wins
        // came from the wall-thickness scheduler + per-preset Pc/ε
        // downgrades, not the film fraction bump.
        _ = pc_Pa;
        return pair switch
        {
            PropellantPair.LOX_CH4 => 0.05,
            PropellantPair.LOX_H2  => 0.03,
            PropellantPair.LOX_RP1 => 0.07,
            _                      => 0.05,
        };
    }

    /// <summary>
    /// Default face layout for a given element type. Coax / Showerhead
    /// pack naturally on a hex grid (F-1 / RS-25 heritage); Swirl uses
    /// annular rows (Russian staged-combustion heritage); Pintle is a
    /// single central element; ImpingingDoublet uses the legacy single
    /// pitch-circle arrangement (Atlas / Delta heritage).
    /// </summary>
    internal static InjectorFaceLayout InjectorLayoutFor(string elementType) => elementType switch
    {
        "Pintle"     => InjectorFaceLayout.Central,
        "Swirl"      => InjectorFaceLayout.AnnularRows,
        "Coax"       => InjectorFaceLayout.Hexagonal,
        "Showerhead" => InjectorFaceLayout.Hexagonal,
        _            => InjectorFaceLayout.Circular,
    };

    /// <summary>
    /// SA bound clamps for the wall thickness scheduler. Mirror the
    /// <c>[SaDesignVariable]</c> attributes on
    /// <c>RegenChamberDesign.{Chamber,Throat,Exit}WallThicknessOverride_mm</c>
    /// (0.5..8.0 mm liner) and <c>OuterJacketThickness_mm</c> (1.0..6.0 mm).
    /// Out-of-band schedule values are clamped here rather than at the SA
    /// boundary so the seed and SA iter=0 remain physics-identical.
    /// </summary>
    internal const double LinerMin_mm    = 0.5;
    internal const double LinerMax_mm    = 8.0;
    internal const double JacketMin_mm   = 1.0;
    internal const double JacketMax_mm   = 6.0;

    /// <summary>
    /// Sprint A-2 (#167, 2026-04-30): physics-aware wall thickness
    /// scheduler. Returns the AutoSeeder's seeded inner-liner thickness
    /// at chamber / throat / exit anchors plus the uniform outer jacket
    /// thickness. The values are sized to clear two structural gates at
    /// the seed:
    ///
    ///   1. <b>Burst margin</b> (cold proof test, Pc only on inner side):
    ///      <c>P_burst = (σ_y_cold_inner · t_inner + σ_y_cold_jacket · t_jacket) / r_max</c>
    ///      ≥ <c>2.5 · MEOP</c> (ASME BPVC §VIII Div 1, see
    ///      <see cref="ProofTestAnalysis.MinBurstMarginFactor"/>).
    ///
    ///   2. <b>Steady-state hoop yield</b> (post-Sprint-G' per-station
    ///      |P_coolant − P_gas|): <c>σ_hoop = ΔP · r / (t_inner + t_jacket)</c>
    ///      &lt; <c>σ_y(T_wg) / 1.5</c> (50 % yield safety factor).
    ///
    /// The jacket carries the bulk of the hoop load — keeping the inner
    /// liner thin minimises wall ΔT and hence thermal stress at the
    /// throat. The exit-station inner liner is bumped only when the
    /// jacket alone (clamped at the SA upper bound 6 mm) cannot satisfy
    /// burst at the largest-r station.
    ///
    /// This routine is pure math; it doesn't run the cooling solver, so
    /// the per-station T_wg values are conservatively approximated from
    /// material max-service-T (chamber/throat) and 700 K (exit). Test
    /// callers can still override the result via
    /// <c>RegenChamberDesign with { ChamberWallThicknessOverride_mm = ... }</c>.
    /// </summary>
    internal static (double chamber, double throat, double exit, double jacket)
        WallThicknessSchedule(EngineSpec spec, int wallMaterial, double gasWall_mm, double contraction)
    {
        // Estimate station radii from thrust + Pc + ε. Match the
        // ChannelCountFor convention (cfMin = 1.5) so all geometry
        // assumptions in the seeder are consistent.
        const double cfMin = 1.5;
        double r_throat_m  = Math.Sqrt(spec.Thrust_N / (cfMin * spec.ChamberPressure_Pa * Math.PI));
        double r_chamber_m = r_throat_m * Math.Sqrt(contraction);
        double r_exit_m    = r_throat_m * Math.Sqrt(spec.ExpansionRatio);

        // Material yield strengths, evaluated cold (300 K) for burst and
        // at expected gas-side T for yield. For bimetallic (index 4) we
        // approximate with the inner liner (GRCop-42) — composite-yield
        // accounting in StructuralCheck handles the actual evaluation.
        var liner = wallMaterial switch
        {
            0 => HeatTransfer.WallMaterials.GRCop42,
            1 => HeatTransfer.WallMaterials.CuCrZr,
            2 => HeatTransfer.WallMaterials.Inconel625,
            3 => HeatTransfer.WallMaterials.Inconel718,
            4 => HeatTransfer.WallMaterials.GRCop42,
            _ => HeatTransfer.WallMaterials.GRCop42,
        };
        // T_wg estimates: throat hits the max-service-T ceiling under
        // post-Z1.2 physics; chamber slightly cooler; exit ~700 K (gas
        // is rapidly expanding + film cooling still effective).
        double T_wg_throat_K  = liner.MaxServiceTemp_K - 200; // ~50 K below ceiling
        double T_wg_chamber_K = T_wg_throat_K - 100;
        double T_wg_exit_K    = 700.0;
        double sigmaY_cold_Pa  = liner.YieldStrengthAt_MPa(293) * 1e6;
        double sigmaY_throatPa = liner.YieldStrengthAt_MPa(T_wg_throat_K)  * 1e6;
        double sigmaY_chamberP = liner.YieldStrengthAt_MPa(T_wg_chamber_K) * 1e6;
        double sigmaY_exitPa   = liner.YieldStrengthAt_MPa(T_wg_exit_K)    * 1e6;

        // ΔP per station (steady state, post-Sprint-G' isentropic gas P):
        //   chamber:  |P_coolant − P_chamber| ≈ 0.4 × Pc
        //   throat:   |P_coolant − 0.55 × Pc| ≈ 0.85 × Pc
        //   exit:     |P_coolant − 0|         ≈ P_coolant_outlet
        // P_coolant per cycle (matches CoolantInletPressureFor's outlet
        // approximation: pump discharge → injector ΔP ≈ 1.4× Pc, or
        // turbine inlet for closed expander).
        double pCoolantOutlet_Pa = spec.EngineCycleOverride switch
        {
            FeedSystem.EngineCycle.ClosedExpander => spec.ChamberPressure_Pa * 1.10,
            FeedSystem.EngineCycle.OpenExpander   => 0.5e6,
            _                                     => spec.ChamberPressure_Pa * 1.40,
        };
        double pCoolantInlet_Pa = CoolantInletPressureFor(spec);
        // For steady-state hoop, the jacket pressure varies along the
        // length. Use the worst-case (largest) coolant pressure as a
        // conservative reference at the deep-station (exit/skirt).
        double pCoolantWorst_Pa = Math.Max(pCoolantInlet_Pa, pCoolantOutlet_Pa);
        double dpChamber_Pa = 0.40 * spec.ChamberPressure_Pa;
        double dpThroat_Pa  = 0.85 * spec.ChamberPressure_Pa;
        double dpExit_Pa    = pCoolantWorst_Pa;

        // Burst margin sizing: sized on cold material at the proof test.
        // MEOP is the chamber pressure (the proof test pressurises the
        // gas side; the coolant side is empty). The structural check's
        // ComputeBurstMarginFactor uses MAX-r station — typically exit
        // for ε > contraction. Solve for total t_eff that satisfies
        //   P_burst = σ_y_cold · t_eff / r_max ≥ 2.5 · Pc
        // Using inner + jacket sum (composite hoop credit, Sprint G' /
        // Z2.10).
        double r_max_m = Math.Max(r_chamber_m, r_exit_m);
        double tEffBurst_m = 2.5 * spec.ChamberPressure_Pa * r_max_m / Math.Max(sigmaY_cold_Pa, 1);

        // Steady-state hoop sizing per station (50 % SF over yield):
        const double SF = 1.5;
        double tEffChamber_m = SF * dpChamber_Pa * r_chamber_m / Math.Max(sigmaY_chamberP, 1);
        double tEffThroat_m  = SF * dpThroat_Pa  * r_throat_m  / Math.Max(sigmaY_throatPa, 1);
        double tEffExit_m    = SF * dpExit_Pa    * r_exit_m    / Math.Max(sigmaY_exitPa,   1);

        // Jacket sizing: take the strictest of all anchors (cold burst
        // dominates for Pc-driven designs; steady-state may dominate for
        // expander-class coolant pressures). Inner liner contribution
        // is at gasWall; jacket carries the rest.
        double tInner_m = gasWall_mm * 1e-3;
        double tEffPeak_m = Math.Max(
            Math.Max(tEffBurst_m, tEffChamber_m),
            Math.Max(tEffThroat_m, tEffExit_m));
        double tJacketReq_mm = 1000.0 * Math.Max(0, tEffPeak_m - tInner_m);
        // Apply the +20 % conservatism above the gate edge so SA iter=0
        // doesn't sit right on the line.
        tJacketReq_mm *= 1.20;
        double jacket_mm = Math.Clamp(tJacketReq_mm, JacketMin_mm, JacketMax_mm);

        // Per-station inner liner: stay at gasWall (thin, low ΔT) for
        // chamber + throat. For the exit, if the jacket alone (capped at
        // SA max 6 mm) cannot meet the steady-state OR burst requirement,
        // bump the exit liner to share the load.
        double t_jacket_m = jacket_mm * 1e-3;
        double tExitInner_m = tInner_m;
        double tExitRequired_m = Math.Max(tEffExit_m, tEffBurst_m);
        if (tExitRequired_m > tInner_m + t_jacket_m)
            tExitInner_m = Math.Min(tExitRequired_m - t_jacket_m, LinerMax_mm * 1e-3);
        double tChamberInner_m = tInner_m;
        if (tEffChamber_m > tInner_m + t_jacket_m)
            tChamberInner_m = Math.Min(tEffChamber_m - t_jacket_m, LinerMax_mm * 1e-3);
        double tThroatInner_m = tInner_m;
        if (tEffThroat_m > tInner_m + t_jacket_m)
            tThroatInner_m = Math.Min(tEffThroat_m - t_jacket_m, LinerMax_mm * 1e-3);

        // Convert to mm + clamp to SA bounds. Liner floor is gasWall (the
        // pre-A-2 thinnest seed); we never go thinner than that, since it
        // would compromise heat transfer + LPBF margin.
        double linerFloor_mm = Math.Max(LinerMin_mm, gasWall_mm);
        double tCham_mm = Math.Clamp(tChamberInner_m * 1000.0, linerFloor_mm, LinerMax_mm);
        double tThr_mm  = Math.Clamp(tThroatInner_m  * 1000.0, linerFloor_mm, LinerMax_mm);
        double tExit_mm = Math.Clamp(tExitInner_m    * 1000.0, linerFloor_mm, LinerMax_mm);
        return (tCham_mm, tThr_mm, tExit_mm, jacket_mm);
    }
}
