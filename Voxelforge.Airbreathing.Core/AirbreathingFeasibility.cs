// AirbreathingFeasibility.cs — air-breathing-pillar feasibility
// gate evaluator.
//
// Parallel to AerospikeFeasibility / FeasibilityGate on the rocket
// side. The rocket-side GateRegistry's predicate signature is
// `Action<RegenGenerationResult, List<FeasibilityViolation>>` —
// rocket-shaped. Air-breathing has its own result shape
// (AirbreathingResult) so the registry doesn't unify cleanly today.
// By design the unification waits until rocket +
// air-breathing both exist as concrete pillars (rule of three).
//
// Sprint A5 wires the four ramjet gates from the air-breathing
// build-out's sub-step 1a, plus two follow-ons exposed by the A4 cycle
// solver (NOZZLE_INSUFFICIENT_DRIVE_PRESSURE, T_T4_EXCEEDS_LIMIT).
// A7 inlines the turbojet gates alongside. A8 adds the turbofan
// branch + two turbofan-specific gates (BYPASS_RATIO_OUT_OF_BAND +
// BYPASS_MIXER_ENTHALPY_IMBALANCE).

using System.Collections.Generic;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.Optimization;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;
using Voxelforge.Geometry.LpbfAnalysis;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing;

/// <summary>
/// Evaluator for air-breathing feasibility gates. Returns the
/// engine-family-agnostic <see cref="FeasibilityGateResult"/> so UI /
/// CLI / report consumers can treat it identically to rocket gate
/// results.
/// </summary>
public static class AirbreathingFeasibility
{
    /// <summary>
    /// Lean equivalence-ratio floor. Below this the H2-air mixture is
    /// outside the steady combustion band and the flame will blow off.
    /// Atomized H2-air flammability lower limit ≈ φ = 0.10; we use
    /// 0.20 as the engineering floor with margin for combustor-design
    /// uncertainty.
    /// </summary>
    public const double LeanBlowoutPhi = 0.20;

    /// <summary>
    /// Rich equivalence-ratio ceiling. H2-air is flammable up to ~φ=4
    /// but practical combustors run lean to avoid wall heating + low
    /// completeness. 1.5 is a generous engineering ceiling.
    /// </summary>
    public const double RichBlowoutPhi = 1.5;

    /// <summary>
    /// Hard upper limit on combustor exit stagnation temperature for
    /// uncooled walls. 2200 K is the safe service ceiling for
    /// CMC-coated nickel superalloys. Real combustors run with film
    /// or transpiration cooling and can push higher; this gate is
    /// the uncooled-wall conservative ceiling.
    /// </summary>
    public const double T_T4_MaxUncooled_K = 2200.0;

    /// <summary>
    /// Inlet recovery floor below which the inlet is probably
    /// unstarted. The MIL-STD-5007D curve drops below 0.50 around
    /// M ≈ 4.5; a real unstart event drops π_d to below 0.30.
    /// Fires advisory-style when π_d &lt; 0.50.
    /// </summary>
    public const double InletRecoveryFloor = 0.50;

    /// <summary>
    /// Turbine inlet temperature ceiling for uncooled blade alloys.
    /// Single-crystal nickel superalloys with thermal-barrier coating
    /// service up to ~1700 K; cooled blades push to 1900-2000 K.
    /// 1700 K is the uncooled-blade conservative ceiling. T_T4_MaxUncooled_K
    /// (combustor wall ceiling, 2200 K) is laxer because the combustor
    /// liner is a static shell (radiation cooling) while turbine blades
    /// see the same gas in rotation under bending stress.
    /// </summary>
    public const double TurbineInletT_MaxUncooled_K = 1700.0;

    /// <summary>
    /// Turbine inlet temperature ceiling for cooled blade alloys.
    /// Single-crystal Ni + TBC with compressor-bleed film/transpiration
    /// cooling can service up to ~2200 K. Activated when
    /// <see cref="AirbreathingEngineDesign.TurbineCoolingFraction"/> &gt; 0.
    /// </summary>
    public const double TurbineInletT_MaxCooled_K = 2200.0;

    /// <summary>
    /// Compressor pressure ratio band. Below π_c = 2 the cycle is
    /// barely a turbojet (essentially a ducted ramjet); above π_c = 50
    /// the single-spool stand-in maps don't model the multi-stage
    /// reality (real high-π_c uses two-spool / three-spool architecture).
    /// </summary>
    public const double CompressorRatio_Min = 2.0;

    /// <summary>Companion to <see cref="CompressorRatio_Min"/>.</summary>
    public const double CompressorRatio_Max = 50.0;

    /// <summary>
    /// Industry preliminary-design floor for compressor surge margin.
    /// Below this, advisory gate <c>SURGE_MARGIN_INSUFFICIENT</c> fires
    /// (warning only — does not gate <c>IsFeasible</c>). Real engines
    /// typically run with 15-25 % margin in steady state; below 10 %
    /// the operating point is uncomfortably close to surge.
    /// </summary>
    public const double SurgeMargin_AdvisoryFloor = 0.10;

    /// <summary>
    /// Lower bound on bypass ratio for the Sprint-A8 single-spool
    /// low-bypass turbofan envelope. Below 0.10 the bypass duct
    /// passes negligible mass and the cycle is essentially a turbojet
    /// — switch <see cref="AirbreathingEngineKind.Turbojet"/> instead.
    /// </summary>
    public const double BypassRatio_Min = 0.10;

    /// <summary>
    /// Upper bound on bypass ratio for the Sprint-A8 single-spool
    /// envelope. Above 2.0 the fan loads the single shaft with too
    /// much air for the same shaft to also drive the HPC at typical
    /// π_c — physical reality is two-spool architecture (separate LP
    /// + HP shafts) which Stream B will add. F404 sits at BPR ≈ 0.34;
    /// modern high-bypass commercial engines (CFM56, GE90) are 5+
    /// and out-of-scope for phase 1.
    /// </summary>
    public const double BypassRatio_Max = 2.00;

    /// <summary>
    /// Upper bound on bypass ratio for the two-spool architecture
    /// (activated when <see cref="AirbreathingEngineDesign.PiFan"/>
    /// is set). The two-spool LP/HP shaft split removes the
    /// single-spool overloading constraint; BPR up to 8.0 is
    /// physically representative of modern high-bypass commercial
    /// engines (GE90 ≈ 8.5, CFM56 ≈ 5.9).
    /// </summary>
    public const double BypassRatio_Max_TwoSpool = 8.00;

    /// <summary>
    /// Fan pressure ratio ceiling for stall-free operation at
    /// sea-level static conditions. Above π_fan = 1.9 the fan
    /// operating line at SLS full-power is dangerously close to
    /// the surge boundary for typical fan map geometries. Advisory
    /// gate <c>FAN_STALL</c> fires when this threshold is exceeded.
    /// </summary>
    public const double FanPressureRatio_StallFloor = 1.9;

    /// <summary>
    /// Bypass duct Mach ceiling. Above M = 0.9 the bypass duct
    /// exit (station 16) is in the transonic regime and shock losses
    /// are no longer captured by the constant-Cp model. Hard gate
    /// <c>BYPASS_DUCT_CHOKED</c> fires when this threshold is exceeded.
    /// </summary>
    public const double BypassDuctMach_ChokeFloor = 0.9;

    /// <summary>
    /// Condenser pressure floor for the steam turbine. Below 0.01 bar
    /// the saturation temperature falls below 46 °C and the condenser
    /// requires a vacuum level that is impractical for industrial plant.
    /// Hard gate <c>STEAM_CONDENSE_BELOW_VACUUM</c> fires when exceeded.
    /// </summary>
    public const double SteamCondensePressure_MinBar = 0.01;

    /// <summary>
    /// Mixer enthalpy-imbalance tolerance (fraction of m·cp·T_avg).
    /// The constant-area mass-flow-weighted mixer in Sprint A8 closes
    /// energy balance bit-identical with constant cp, so this gate
    /// never fires today. It's wired in as forward-compatible defence
    /// for the cp(T) tabulation in Stream B — when cp differs between
    /// hot and cold streams, this gate fires if the lumped recovery
    /// model drifts past 0.5%.
    /// </summary>
    public const double MixerEnthalpyImbalanceTolerance = 0.005;

    /// <summary>
    /// Isolator recovery floor for scramjet. Below this value the
    /// pseudo-shock train spans the full isolator length and the inlet
    /// is considered unstarted. Fires the hard <c>ISOLATOR_UNSTART</c>
    /// gate. Matches <see cref="Voxelforge.Airbreathing.Cycles.IsolatorRecovery.IsolatorRecoveryFloor"/>.
    /// </summary>
    public const double ScramjetIsolatorRecoveryFloor = 0.30;

    /// <summary>
    /// Combustor stagnation temperature ratio ceiling for scramjet.
    /// T_t4 / T_t3 above this value indicates near-thermal-choking
    /// in the supersonic combustor (Rayleigh limit). Advisory gate
    /// <c>STATIC_T_T_RATIO_OUT_OF_BAND</c> fires to warn that M_4
    /// is being saturated at 1.001 and net thrust will be overstated.
    /// </summary>
    public const double ScramjetTtRatioCeiling = 6.0;

    /// <summary>
    /// Evaluate gates against a cycle solve. Returns hard-violation
    /// gates (gate optimization) and advisories (warnings only,
    /// surfaced through <see cref="AirbreathingResult.Advisories"/>).
    /// </summary>
    /// <param name="design">Candidate design.</param>
    /// <param name="cond">Flight envelope.</param>
    /// <param name="stations">Cycle-solver station map.</param>
    /// <param name="compressorDiagnostics">
    /// Compressor map's off-design diagnostics, if available. Null for
    /// ramjets and stand-in compressor maps; populated by the J85-class
    /// table-based map.
    /// </param>
    /// <param name="turbineDiagnostics">Turbine equivalent.</param>
    public static (FeasibilityGateResult Result, IReadOnlyList<FeasibilityViolation> Advisories)
        Evaluate(
            AirbreathingEngineDesign design,
            FlightConditions cond,
            StationMap stations,
            MapInfo? compressorDiagnostics = null,
            MapInfo? turbineDiagnostics = null)
    {
        var violations = new List<FeasibilityViolation>();
        var advisories = new List<FeasibilityViolation>();

        // Equivalence ratio band — specified design knob, easy first
        // check before touching the cycle solve. Skipped for LACE and
        // RotatingDetonation: those cycles parameterise stoichiometry
        // differently (LaceAirToFuelRatio / RdePressureGainRatio) and
        // their per-cycle Evaluate*Gates methods own the equivalent
        // band checks (LACE_AIR_TO_FUEL_*, RDE_EQUIVALENCE_RATIO_*).
        if (design.Kind != AirbreathingEngineKind.LiquidAirCycle &&
            design.Kind != AirbreathingEngineKind.RotatingDetonation)
        {
            EvaluateEquivalenceRatioBand(design, violations);
        }

        if (design.Kind == AirbreathingEngineKind.Ramjet)
        {
            EvaluateRamjetGates(design, cond, stations, violations);
        }
        else if (design.Kind == AirbreathingEngineKind.Turbojet)
        {
            EvaluateTurbojetGates(design, cond, stations, violations);
        }
        else if (design.Kind == AirbreathingEngineKind.Turbofan)
        {
            EvaluateTurbofanGates(design, cond, stations, violations, advisories);
        }
        else if (design.Kind == AirbreathingEngineKind.Scramjet)
        {
            EvaluateScramjetGates(design, cond, stations, violations);
        }
        else if (design.Kind == AirbreathingEngineKind.Rbcc)
        {
            EvaluateRbccGates(design, cond, stations, violations, advisories);
        }
        else if (design.Kind == AirbreathingEngineKind.GasTurbine)
        {
            EvaluateGasTurbineGates(design, cond, stations, violations, advisories);
        }
        else if (design.Kind == AirbreathingEngineKind.SteamTurbine)
        {
            EvaluateSteamTurbineGates(design, violations);
        }
        else if (design.Kind == AirbreathingEngineKind.LiquidAirCycle)
        {
            EvaluateLaceGates(design, cond, stations, violations, advisories);
        }
        else if (design.Kind == AirbreathingEngineKind.RotatingDetonation)
        {
            EvaluateRdeGates(design, cond, stations, violations, advisories);
        }

        // Map diagnostics drive surge / choke gates whenever they're
        // populated (J85-class maps; stand-in maps leave them null,
        // skipping these gates entirely).
        EvaluateMapDiagnostics(compressorDiagnostics, turbineDiagnostics, violations, advisories);

        // Registry-based gates (additive overlay, 2026-05-05). Pulsejet adds
        // PULSEJET_BLOWOUT_LEAN (Hard) + PULSEJET_ACOUSTIC_OVERPRESSURE
        // (Advisory) here. Future air-breathing gates that don't already
        // exist as inline if/else branches above ship through this loop.
        // The 22 pre-existing inline gates are NOT lifted in Wave 1 —
        // that's a separate Stream B sprint. The registry loop is empty
        // until pulsejet PR-4; this wiring is forward-compatible.
        var input = new AirbreathingGateInput(
            design, cond, stations, compressorDiagnostics, turbineDiagnostics);
        foreach (var gate in AirbreathingGateRegistry.Instance.All)
        {
            if ((gate.Applicability & EngineFamilyMask.Airbreathing) == 0) continue;
            var sink = gate.Severity == GateSeverity.Hard ? violations : advisories;
            gate.Emit(input, sink);
        }

        return (
            new FeasibilityGateResult(
                IsFeasible: violations.Count == 0,
                Violations: violations.ToArray()),
            advisories.ToArray());
    }

    private static void EvaluateEquivalenceRatioBand(
        AirbreathingEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        if (design.EquivalenceRatio < LeanBlowoutPhi)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "COMBUSTOR_BLOWOUT_LEAN",
                Description: $"Equivalence ratio φ = {design.EquivalenceRatio:F3} below lean-blowout "
                           + $"floor {LeanBlowoutPhi:F2}. Flame cannot sustain steady combustion "
                           + $"this lean — increase fuel flow or reduce captured air.",
                ActualValue: design.EquivalenceRatio,
                Limit:       LeanBlowoutPhi));
        }
        else if (design.EquivalenceRatio > RichBlowoutPhi)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "COMBUSTOR_BLOWOUT_RICH",
                Description: $"Equivalence ratio φ = {design.EquivalenceRatio:F3} above rich-blowout "
                           + $"ceiling {RichBlowoutPhi:F2}. Combustion completeness collapses + wall "
                           + $"heating becomes unmanageable — reduce fuel flow.",
                ActualValue: design.EquivalenceRatio,
                Limit:       RichBlowoutPhi));
        }
    }

    private static void EvaluateRamjetGates(
        AirbreathingEngineDesign design,
        FlightConditions cond,
        StationMap stations,
        List<FeasibilityViolation> violations)
    {
        // Inlet unstart (proxy: derived π_d below floor).
        var s0 = stations.Station(0);
        var s2 = stations.Station(2);
        if (s0.StagnationP_Pa > 0 && s2.StagnationP_Pa > 0)
        {
            double piD = s2.StagnationP_Pa / s0.StagnationP_Pa;
            if (piD < InletRecoveryFloor)
            {
                violations.Add(new FeasibilityViolation(
                    ConstraintId: "INLET_UNSTART",
                    Description: $"Derived inlet recovery π_d = {piD:F3} below floor "
                               + $"{InletRecoveryFloor:F2} at M_∞ = {cond.MachNumber:F2}. "
                               + $"Inlet probably unstarted; geometry needs ramp-angle redesign "
                               + $"or operating Mach lowered.",
                    ActualValue: piD,
                    Limit:       InletRecoveryFloor));
            }
        }

        // Combustor exit T uncooled-wall ceiling.
        var s4 = stations.Station(4);
        if (s4.StagnationT_K > T_T4_MaxUncooled_K)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "T_T4_EXCEEDS_LIMIT",
                Description: $"Combustor exit stagnation T = {s4.StagnationT_K:F0} K above "
                           + $"uncooled-wall ceiling {T_T4_MaxUncooled_K:F0} K. Add active cooling, "
                           + $"upgrade wall material, or reduce φ.",
                ActualValue: s4.StagnationT_K,
                Limit:       T_T4_MaxUncooled_K));
        }

        // Nozzle insufficient drive pressure (P_t9 ≤ P_∞ → no expansion possible).
        var s9 = stations.Station(9);
        if (double.IsNaN(s9.MachNumber))
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "NOZZLE_INSUFFICIENT_DRIVE_PRESSURE",
                Description: $"Nozzle stagnation pressure P_t9 = {s9.StagnationP_Pa:F0} Pa "
                           + $"≤ ambient — no expansion possible, engine cannot push flow against "
                           + $"atmosphere. Cause is usually combined inlet + combustor + nozzle "
                           + $"recovery losses exceeding ram compression at the operating Mach.",
                ActualValue: s9.StagnationP_Pa,
                Limit:       0.0));
        }

        // Combustor exit Mach floor (Rayleigh-flow thermal-choking
        // proxy). Sprint A4 hard-codes combustor M = 0.2 in the cycle
        // solver, so this gate is silent today; left in place because
        // A5+ may make combustor Mach a derived value where it matters.
        if (s4.MachNumber > 0.7)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "THERMAL_CHOKING",
                Description: $"Combustor exit Mach {s4.MachNumber:F2} approaches 1.0 — "
                           + $"heat-addition Rayleigh-flow choking imminent. Lower φ or "
                           + $"increase combustor area.",
                ActualValue: s4.MachNumber,
                Limit:       0.7));
        }
    }

    /// <summary>
    /// Evaluate LPBF printability gates against a
    /// <see cref="LpbfPrintabilityResult"/> produced by
    /// <c>Voxelforge.Airbreathing.Voxels.RamjetVoxelBuilder.Build</c>.
    /// Emits the same constraint-id strings the rocket side uses
    /// (<c>OVERHANG_ANGLE_EXCEEDED</c>, <c>TRAPPED_POWDER_REGION</c>,
    /// <c>DRAIN_PATH_MISSING</c>) so downstream consumers can string-
    /// match the gates identically across pillars.
    /// <para>
    /// Distinct entry from <see cref="Evaluate"/>: LPBF analysis depends
    /// on the voxel build, which only happens inside a PicoGK.Library
    /// scope. Callers run the cycle solve + <see cref="Evaluate"/> on
    /// the headless path, then run the voxel build + this method on
    /// the PicoGK path. Both violation lists merge in the consumer.
    /// </para>
    /// </summary>
    /// <param name="printability">Result from the LPBF analysis pass.</param>
    /// <param name="violations">Caller-owned violation accumulator. New violations are appended.</param>
    public static void EvaluateLpbfGates(
        LpbfPrintabilityResult printability,
        List<FeasibilityViolation> violations)
    {
        if (printability is null) throw new System.ArgumentNullException(nameof(printability));
        if (violations   is null) throw new System.ArgumentNullException(nameof(violations));

        if (printability.HasOverhangViolation)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "OVERHANG_ANGLE_EXCEEDED",
                Description:  $"LPBF overhang violation: {printability.Overhang.ViolationCount} "
                            + $"surface patch(es) below the {printability.Material.MinUnsupportedOverhangAngle_deg:F0}° "
                            + $"unsupported-angle floor for {printability.Material.DisplayName}. "
                            + $"Worst overhang β = {printability.Overhang.WorstOverhangAngle_deg:F1}° "
                            + $"(total flagged area {printability.Overhang.TotalOverhangArea_mm2:F1} mm²). "
                            + $"Either soften the contour, add support struts, or rotate the build axis.",
                ActualValue:  printability.Overhang.WorstOverhangAngle_deg,
                Limit:        printability.Material.MinUnsupportedOverhangAngle_deg));
        }

        if (printability.HasTrappedPowder)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "TRAPPED_POWDER_REGION",
                Description:  $"LPBF trapped-powder violation: {printability.TrappedPowder!.PocketCount} "
                            + $"pocket(s) totalling {printability.TrappedPowder.TotalTrappedVolume_mm3:F1} mm³ "
                            + $"unreachable from any opening. Add drain ports or split the part for cleaning access.",
                ActualValue:  printability.TrappedPowder.TotalTrappedVolume_mm3,
                Limit:        printability.Material.MinFlaggedPocketVolume_mm3));
        }

        if (printability.HasDrainPathViolation)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "DRAIN_PATH_MISSING",
                Description:  $"LPBF drain-path violation: {printability.DrainPath.ViolationCount} "
                            + $"dead-end(s) or isolated subgraph(s) in the plumbing topology. "
                            + $"Powder cannot evacuate from these regions during post-print cleaning.",
                ActualValue:  printability.DrainPath.ViolationCount,
                Limit:        0.0));
        }
    }

    private static void EvaluateTurbojetGates(
        AirbreathingEngineDesign design,
        FlightConditions cond,
        StationMap stations,
        List<FeasibilityViolation> violations)
    {
        // Compressor pressure ratio band — design knob, easy first
        // check.
        if (design.CompressorPressureRatio < CompressorRatio_Min)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "COMPRESSOR_RATIO_OUT_OF_BAND",
                Description: $"Compressor pressure ratio π_c = {design.CompressorPressureRatio:F2} below "
                           + $"floor {CompressorRatio_Min:F2}. Below this the cycle is essentially a "
                           + $"ducted ramjet — switch Kind = Ramjet.",
                ActualValue: design.CompressorPressureRatio,
                Limit:       CompressorRatio_Min));
        }
        else if (design.CompressorPressureRatio > CompressorRatio_Max)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "COMPRESSOR_RATIO_OUT_OF_BAND",
                Description: $"Compressor pressure ratio π_c = {design.CompressorPressureRatio:F2} above "
                           + $"single-spool ceiling {CompressorRatio_Max:F2}. Multi-spool architecture "
                           + $"required (Sprint A8 follow-on).",
                ActualValue: design.CompressorPressureRatio,
                Limit:       CompressorRatio_Max));
        }

        // Turbine inlet temperature (TIT) — ceiling depends on whether
        // compressor-bleed cooling is active.
        var s4 = stations.Station(4);
        double titLimit_tj = design.TurbineCoolingFraction > 0.0
            ? TurbineInletT_MaxCooled_K
            : TurbineInletT_MaxUncooled_K;
        if (s4.StagnationT_K > titLimit_tj)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "TIT_EXCEEDED",
                Description: $"Turbine inlet stagnation T_t4 = {s4.StagnationT_K:F0} K above "
                           + $"{(design.TurbineCoolingFraction > 0.0 ? "cooled" : "uncooled")}-blade "
                           + $"ceiling {titLimit_tj:F0} K. Increase cooling fraction or reduce φ.",
                ActualValue: s4.StagnationT_K,
                Limit:       titLimit_tj));
        }

        // Inlet unstart — same proxy as ramjet.
        var s0 = stations.Station(0);
        var s2 = stations.Station(2);
        if (s0.StagnationP_Pa > 0 && s2.StagnationP_Pa > 0)
        {
            double piD = s2.StagnationP_Pa / s0.StagnationP_Pa;
            if (piD < InletRecoveryFloor)
            {
                violations.Add(new FeasibilityViolation(
                    ConstraintId: "INLET_UNSTART",
                    Description: $"Derived inlet recovery π_d = {piD:F3} below floor "
                               + $"{InletRecoveryFloor:F2} at M_∞ = {cond.MachNumber:F2}.",
                    ActualValue: piD,
                    Limit:       InletRecoveryFloor));
            }
        }

        // Nozzle insufficient drive pressure (P_t9 ≤ P_∞).
        var s9 = stations.Station(9);
        if (double.IsNaN(s9.MachNumber))
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "NOZZLE_INSUFFICIENT_DRIVE_PRESSURE",
                Description: $"Nozzle stagnation pressure P_t9 = {s9.StagnationP_Pa:F0} Pa "
                           + $"≤ ambient. Likely cause for turbojet: turbine extracted too much "
                           + $"work (high π_c with low T_t4) leaving inadequate pressure for "
                           + $"the nozzle.",
                ActualValue: s9.StagnationP_Pa,
                Limit:       0.0));
        }
    }

    /// <summary>
    /// Evaluate map-based off-design diagnostics. Skipped (no-op) when
    /// both diagnostics are null (e.g. ramjet, or stand-in turbojet
    /// maps that don't model surge/choke). When the J85-class maps
    /// populate diagnostics, fires:
    /// <list type="bullet">
    ///   <item><c>SURGE_MARGIN_INSUFFICIENT</c> (advisory) — fires when
    ///   compressor surge margin &lt; 10 % industry floor. Surface
    ///   warning only; does not gate optimization.</item>
    ///   <item><c>CORRECTED_MASS_FLOW_OUT_OF_MAP</c> (hard) — fires when
    ///   the operating point is past surge (SM &lt; 0) or past choke
    ///   (ChokeMarginRel &gt; 1). Hard infeasibility.</item>
    /// </list>
    /// </summary>
    private static void EvaluateMapDiagnostics(
        MapInfo? compressorDx,
        MapInfo? turbineDx,
        List<FeasibilityViolation> violations,
        List<FeasibilityViolation> advisories)
    {
        if (compressorDx is { } cd)
        {
            // Hard gate: operating point past the surge or choke
            // boundary. SurgeMargin is the *fractional* margin —
            // negative when above the surge line; ChokeMarginRel
            // exceeds 1.0 when past choke.
            if (cd.SurgeMargin < 0.0 || cd.ChokeMarginRel > 1.0)
            {
                violations.Add(new FeasibilityViolation(
                    ConstraintId: "CORRECTED_MASS_FLOW_OUT_OF_MAP",
                    Description: $"Compressor operating point outside the tabulated map "
                               + $"(surge margin = {cd.SurgeMargin:P1}, choke-margin-rel = {cd.ChokeMarginRel:F2}). "
                               + $"Reduce π_c or increase mass flow to bring the point back inside the envelope.",
                    ActualValue: cd.ChokeMarginRel,
                    Limit:       1.0));
            }
            // Advisory: above-floor surge margin (10 % preliminary-
            // design heuristic). Doesn't gate IsFeasible; surfaces
            // through AirbreathingResult.Advisories.
            else if (cd.SurgeMargin < SurgeMargin_AdvisoryFloor)
            {
                advisories.Add(new FeasibilityViolation(
                    ConstraintId: "SURGE_MARGIN_INSUFFICIENT",
                    Description: $"Compressor surge margin {cd.SurgeMargin:P1} below 10 % industry "
                               + $"preliminary-design floor. Operating point is uncomfortably close to "
                               + $"surge — consider lowering π_c or biasing the operating line away from "
                               + $"the surge edge. Advisory only; does not gate feasibility.",
                    ActualValue: cd.SurgeMargin,
                    Limit:       SurgeMargin_AdvisoryFloor));
            }
        }

        if (turbineDx is { } td)
        {
            // Turbine choke: ChokeMarginRel > 1.0 means over-extraction
            // past nominal — fires the same hard gate descriptor.
            if (td.ChokeMarginRel > 1.0)
            {
                violations.Add(new FeasibilityViolation(
                    ConstraintId: "CORRECTED_MASS_FLOW_OUT_OF_MAP",
                    Description: $"Turbine extraction over-nominal "
                               + $"(choke-margin-rel = {td.ChokeMarginRel:F2}). Reduce required shaft "
                               + $"work (lower π_c or higher T_t4) to bring the operating point back "
                               + $"inside the turbine map.",
                    ActualValue: td.ChokeMarginRel,
                    Limit:       1.0));
            }
        }
    }

    // ── Sprint A10 — scramjet gates ───────────────────────────────────────

    private static void EvaluateScramjetGates(
        AirbreathingEngineDesign design,
        FlightConditions cond,
        StationMap stations,
        List<FeasibilityViolation> violations)
    {
        // Isolator unstart (hard). Derived π_iso = P_t3 / P_t2 below floor.
        // Station 2 = oblique-shock inlet exit; station 3 = isolator exit.
        var s2 = stations.Station(2);
        var s3 = stations.Station(3);
        if (s2.StagnationP_Pa > 0 && s3.StagnationP_Pa > 0)
        {
            double piIso = s3.StagnationP_Pa / s2.StagnationP_Pa;
            if (piIso < ScramjetIsolatorRecoveryFloor)
            {
                violations.Add(new FeasibilityViolation(
                    ConstraintId: "ISOLATOR_UNSTART",
                    Description: $"Derived isolator recovery π_iso = {piIso:F3} below unstart "
                               + $"floor {ScramjetIsolatorRecoveryFloor:F2} at M_∞ = {cond.MachNumber:F2}. "
                               + $"Pseudo-shock train has backed up to the inlet — increase isolator "
                               + $"length, reduce combustor back-pressure, or lower φ.",
                    ActualValue: piIso,
                    Limit:       ScramjetIsolatorRecoveryFloor));
            }
        }

        // Combustion efficiency proxy (advisory). Detect near-quench:
        // T_t4 / T_t3 below 1.2 at φ ≥ 0.4 indicates the combustor is
        // not releasing the expected fraction of fuel enthalpy — likely
        // a mixing or ignition failure in this flight condition.
        var s4_sj = stations.Station(4);
        if (design.EquivalenceRatio >= 0.4
            && s3.StagnationT_K > 0
            && s4_sj.StagnationT_K / s3.StagnationT_K < 1.2)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "COMBUSTION_EFFICIENCY_BELOW_FLOOR",
                Description: $"Combustor T_t4/T_t3 = {s4_sj.StagnationT_K / s3.StagnationT_K:F3} "
                           + $"below advisory floor 1.2 at φ = {design.EquivalenceRatio:F2}. "
                           + $"Insufficient heat release — check fuel injection or reduce φ.",
                ActualValue: s4_sj.StagnationT_K / s3.StagnationT_K,
                Limit:       1.2));
        }

        // Near-thermal-choking (advisory). T_t4 / T_t3 above ceiling
        // indicates the heat addition is driving M_4 toward 1 and the
        // Rayleigh solver has saturated M_4 at 1.001.
        if (s3.StagnationT_K > 0
            && s4_sj.StagnationT_K / s3.StagnationT_K > ScramjetTtRatioCeiling)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "STATIC_T_T_RATIO_OUT_OF_BAND",
                Description: $"Combustor T_t4/T_t3 = {s4_sj.StagnationT_K / s3.StagnationT_K:F2} "
                           + $"above ceiling {ScramjetTtRatioCeiling:F1} — Rayleigh-flow thermal "
                           + $"choking imminent (M_4 ≈ 1). Lower φ or increase combustor area.",
                ActualValue: s4_sj.StagnationT_K / s3.StagnationT_K,
                Limit:       ScramjetTtRatioCeiling));
        }

        // NOTE: T_T4_EXCEEDS_LIMIT is intentionally not checked here.
        // At M_∞ ≥ 8 the freestream stagnation temperature T_t0 already
        // exceeds 3000 K, which is above the 2200 K subsonic-combustor
        // uncooled-wall ceiling. Scramjet combustors are regeneratively
        // cooled by the fuel stream; the appropriate thermal limit is the
        // structure fatigue life under combined mechanical + thermal load,
        // which is outside the scope of this preliminary-design model.
        // The STATIC_T_T_RATIO_OUT_OF_BAND gate catches near-thermal-choke
        // conditions instead.

        // Nozzle insufficient drive pressure (same pattern as ramjet).
        var s9_sj = stations.Station(9);
        if (double.IsNaN(s9_sj.MachNumber))
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "NOZZLE_INSUFFICIENT_DRIVE_PRESSURE",
                Description: $"Nozzle stagnation pressure P_t9 = {s9_sj.StagnationP_Pa:F0} Pa "
                           + $"≤ ambient — no expansion possible. Combined inlet + isolator + "
                           + $"combustor + nozzle recovery losses exceed ram compression at "
                           + $"M_∞ = {cond.MachNumber:F2}.",
                ActualValue: s9_sj.StagnationP_Pa,
                Limit:       0.0));
        }
    }

    // ── Sprint A8 — turbofan gates ────────────────────────────────────────

    private static void EvaluateTurbofanGates(
        AirbreathingEngineDesign design,
        FlightConditions cond,
        StationMap stations,
        List<FeasibilityViolation> violations,
        List<FeasibilityViolation> advisories)
    {
        // Bypass ratio band — design knob, easy first check.
        // Two-spool mode (PiFan set) extends the upper bound to 8.0.
        bool isTwoSpool = design.PiFan.HasValue;
        double bprMax = isTwoSpool ? BypassRatio_Max_TwoSpool : BypassRatio_Max;

        if (design.BypassRatio < BypassRatio_Min)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "BYPASS_RATIO_OUT_OF_BAND",
                Description: $"Bypass ratio BPR = {design.BypassRatio:F3} below floor "
                           + $"{BypassRatio_Min:F2}. Below this the bypass duct passes "
                           + $"negligible mass — switch Kind = Turbojet.",
                ActualValue: design.BypassRatio,
                Limit:       BypassRatio_Min));
        }
        else if (design.BypassRatio > bprMax)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "BYPASS_RATIO_OUT_OF_BAND",
                Description: isTwoSpool
                    ? $"Bypass ratio BPR = {design.BypassRatio:F3} above two-spool "
                    + $"ceiling {BypassRatio_Max_TwoSpool:F2}. High-bypass architectures "
                    + $"above BPR 8 require a three-spool layout out of scope for this model."
                    : $"Bypass ratio BPR = {design.BypassRatio:F3} above single-spool "
                    + $"ceiling {BypassRatio_Max:F2}. Above this the fan loads the "
                    + $"single shaft beyond what the same shaft can drive with the HPC — "
                    + $"set PiFan to activate two-spool mode (BPR up to {BypassRatio_Max_TwoSpool:F1}).",
                ActualValue: design.BypassRatio,
                Limit:       bprMax));
        }

        // Compressor pressure ratio band — same as turbojet.
        if (design.CompressorPressureRatio < CompressorRatio_Min)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "COMPRESSOR_RATIO_OUT_OF_BAND",
                Description: $"Compressor pressure ratio π_c = {design.CompressorPressureRatio:F2} below "
                           + $"floor {CompressorRatio_Min:F2}. Below this the cycle is essentially a "
                           + $"ducted ramjet — switch Kind = Ramjet.",
                ActualValue: design.CompressorPressureRatio,
                Limit:       CompressorRatio_Min));
        }
        else if (design.CompressorPressureRatio > CompressorRatio_Max)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "COMPRESSOR_RATIO_OUT_OF_BAND",
                Description: $"Compressor pressure ratio π_c = {design.CompressorPressureRatio:F2} above "
                           + $"single-spool ceiling {CompressorRatio_Max:F2}. Multi-spool architecture "
                           + $"required (Stream B follow-on).",
                ActualValue: design.CompressorPressureRatio,
                Limit:       CompressorRatio_Max));
        }

        // Turbine inlet temperature (TIT) — ceiling depends on whether
        // compressor-bleed cooling is active.
        var s4 = stations.Station(4);
        double titLimit_tf = design.TurbineCoolingFraction > 0.0
            ? TurbineInletT_MaxCooled_K
            : TurbineInletT_MaxUncooled_K;
        if (s4.StagnationT_K > titLimit_tf)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "TIT_EXCEEDED",
                Description: $"Turbine inlet stagnation T_t4 = {s4.StagnationT_K:F0} K above "
                           + $"{(design.TurbineCoolingFraction > 0.0 ? "cooled" : "uncooled")}-blade "
                           + $"ceiling {titLimit_tf:F0} K. Increase cooling fraction or reduce φ.",
                ActualValue: s4.StagnationT_K,
                Limit:       titLimit_tf));
        }

        // Inlet unstart — same proxy as turbojet.
        var s0 = stations.Station(0);
        var s2 = stations.Station(2);
        if (s0.StagnationP_Pa > 0 && s2.StagnationP_Pa > 0)
        {
            double piD = s2.StagnationP_Pa / s0.StagnationP_Pa;
            if (piD < InletRecoveryFloor)
            {
                violations.Add(new FeasibilityViolation(
                    ConstraintId: "INLET_UNSTART",
                    Description: $"Derived inlet recovery π_d = {piD:F3} below floor "
                               + $"{InletRecoveryFloor:F2} at M_∞ = {cond.MachNumber:F2}.",
                    ActualValue: piD,
                    Limit:       InletRecoveryFloor));
            }
        }

        // Nozzle insufficient drive pressure (P_t9 ≤ P_∞).
        var s9 = stations.Station(9);
        if (double.IsNaN(s9.MachNumber))
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "NOZZLE_INSUFFICIENT_DRIVE_PRESSURE",
                Description: $"Nozzle stagnation pressure P_t9 = {s9.StagnationP_Pa:F0} Pa "
                           + $"≤ ambient. For a turbofan this typically means the turbine "
                           + $"extracted too much work to drive both fan + HPC, leaving "
                           + $"inadequate pressure for the mixer + nozzle.",
                ActualValue: s9.StagnationP_Pa,
                Limit:       0.0));
        }

        // Mixer enthalpy balance — defensive, forward-compatible with
        // Stream B cp(T) tabulation. With constant cp, the energy
        // balance closes by construction at the mixer (m_hot·T_t5 +
        // m_cold·T_t16 = m_total·T_t6) so this gate is silent today.
        // It fires if a future cp(T) extension introduces drift past
        // the configured tolerance.
        var s5 = stations.Station(5);
        var s6 = stations.Station(6);
        var s16 = stations.Station(16);
        if (s5.MassFlow_kg_s > 0 && s16.MassFlow_kg_s > 0 && s6.MassFlow_kg_s > 0
            && !double.IsNaN(s5.StagnationT_K) && !double.IsNaN(s6.StagnationT_K)
            && !double.IsNaN(s16.StagnationT_K))
        {
            double h_in = s5.MassFlow_kg_s * s5.StagnationT_K
                        + s16.MassFlow_kg_s * s16.StagnationT_K;
            double h_out = s6.MassFlow_kg_s * s6.StagnationT_K;
            double T_avg = 0.5 * (s5.StagnationT_K + s16.StagnationT_K);
            double residual = System.Math.Abs(h_in - h_out);
            double scale = s6.MassFlow_kg_s * T_avg;
            double fractional = scale > 0 ? residual / scale : 0.0;
            if (fractional > MixerEnthalpyImbalanceTolerance)
            {
                violations.Add(new FeasibilityViolation(
                    ConstraintId: "BYPASS_MIXER_ENTHALPY_IMBALANCE",
                    Description: $"Mixer energy balance residual {fractional:P3} exceeds "
                               + $"tolerance {MixerEnthalpyImbalanceTolerance:P2}. Indicates the "
                               + $"lumped recovery model has drifted from the actual mixed-flow "
                               + $"enthalpy (likely a cp(T) Stream B extension regression).",
                    ActualValue: fractional,
                    Limit:       MixerEnthalpyImbalanceTolerance));
            }
        }

        // FAN_STALL (Advisory) — fires when the effective fan pressure
        // ratio exceeds the SLS stall-free ceiling. Uses the design's
        // explicit PiFan when set (two-spool mode) or the DefaultFanPressureRatio
        // proxy (single-spool √π_c) otherwise.
        double pi_fan_check = design.PiFan
            ?? TurbofanCycleSolver.DefaultFanPressureRatio(
                   design.CompressorPressureRatio, design.BypassRatio);
        if (pi_fan_check > FanPressureRatio_StallFloor)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: "FAN_STALL",
                Description: $"Effective fan pressure ratio π_fan = {pi_fan_check:F2} above "
                           + $"SLS stall-free ceiling {FanPressureRatio_StallFloor:F2}. "
                           + $"Fan operating line at full power is near the surge boundary — "
                           + $"reduce π_fan or operate at partial power. Advisory only; does "
                           + $"not gate feasibility.",
                ActualValue: pi_fan_check,
                Limit:       FanPressureRatio_StallFloor));
        }

        // BYPASS_DUCT_CHOKED (Hard) — fires when the bypass-duct exit
        // Mach (station 16) approaches 1.0. Above M=0.9 shock losses
        // are not captured by the constant-Cp model and net thrust is
        // overstated. Only relevant in two-spool mode where station 16
        // Mach is set by the Mach-equilibrium mixer; in single-spool mode
        // station 16 Mach is the hardcoded CompressorFaceMach = 0.5.
        double m16 = s16.MachNumber;
        if (!double.IsNaN(m16) && m16 > BypassDuctMach_ChokeFloor)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "BYPASS_DUCT_CHOKED",
                Description: $"Bypass duct exit Mach M_16 = {m16:F3} above transonic "
                           + $"ceiling {BypassDuctMach_ChokeFloor:F2}. Shock losses in "
                           + $"the bypass duct are not captured by the constant-Cp model — "
                           + $"increase bypass duct area or reduce BPR to bring M_16 below "
                           + $"{BypassDuctMach_ChokeFloor:F2}.",
                ActualValue: m16,
                Limit:       BypassDuctMach_ChokeFloor));
        }
    }

    // ── Sprint A11 — RBCC gates ───────────────────────────────────────────

    /// <summary>
    /// Upper Mach bound for the ducted-rocket (ejector) mode. Above M 2.5
    /// the constant-ER isobaric-mixing model over-predicts entrained
    /// secondary flow; switch to RbccOperatingMode.Ramjet.
    /// </summary>
    public const double RbccDuctedRocketMachCeiling = 2.5;

    /// <summary>
    /// Lower Mach bound for the scramjet mode. Below M 4.0 the oblique-
    /// shock inlet cannot sustain supersonic combustion at reasonable
    /// equivalence ratios; switch to RbccOperatingMode.Ramjet.
    /// </summary>
    public const double RbccScramjetMachFloor = 4.0;

    /// <summary>
    /// Thermal efficiency floor for an open-Brayton gas turbine. Below
    /// this the cycle is energetically marginal — simple-cycle Brayton
    /// physics (Carnot limit with typical compressor/turbine efficiency
    /// and TIT/T0 ratios) sets a practical lower bound around 0.25.
    /// Fires advisory gate <c>GAS_TURBINE_EFFICIENCY_BELOW_FLOOR</c>.
    /// </summary>
    public const double GasTurbineEfficiencyFloor = 0.25;

    private static void EvaluateRbccGates(
        AirbreathingEngineDesign design,
        FlightConditions cond,
        StationMap stations,
        List<FeasibilityViolation> violations,
        List<FeasibilityViolation> advisories)
    {
        // Gate 1 — RBCC_MODE_OUT_OF_ENVELOPE (Hard).
        // Fires when the chosen operating mode doesn't match the flight
        // Mach band: ducted-rocket is limited to M ≤ 2.5 (constant-ER
        // ejector model validity ceiling); scramjet requires M ≥ 4.0
        // (supersonic combustion ignition threshold).
        if (design.RbccMode == RbccOperatingMode.DuctedRocket
            && cond.MachNumber > RbccDuctedRocketMachCeiling)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "RBCC_MODE_OUT_OF_ENVELOPE",
                Description: $"RBCC DuctedRocket mode selected at M_∞ = {cond.MachNumber:F2}, "
                           + $"above the Phase 1 ejector-model ceiling M = {RbccDuctedRocketMachCeiling:F1}. "
                           + $"Switch to RbccOperatingMode.Ramjet for M > {RbccDuctedRocketMachCeiling:F1}.",
                ActualValue: cond.MachNumber,
                Limit:       RbccDuctedRocketMachCeiling));
        }
        else if (design.RbccMode == RbccOperatingMode.Scramjet
                 && cond.MachNumber < RbccScramjetMachFloor)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "RBCC_MODE_OUT_OF_ENVELOPE",
                Description: $"RBCC Scramjet mode selected at M_∞ = {cond.MachNumber:F2}, "
                           + $"below the supersonic combustion ignition floor M = {RbccScramjetMachFloor:F1}. "
                           + $"Switch to RbccOperatingMode.Ramjet for M < {RbccScramjetMachFloor:F1}.",
                ActualValue: cond.MachNumber,
                Limit:       RbccScramjetMachFloor));
        }

        // Gate 2 — RBCC_TRANSITION_THRUST_GAP (Advisory, always-pass Phase 1).
        // Stream B follow-on: cross-mode thrust continuity check at the
        // DuctedRocket→Ramjet and Ramjet→Scramjet transition Mach numbers.
        // Phase 1 single-design-point model cannot evaluate this without
        // two adjacent-mode evaluations; reserved for the variable-geometry
        // ejector sprint.
        _ = stations; // suppress unused-parameter warning (used by other gate methods)
        _ = advisories;
    }

    // ── Steam turbine (Rankine cycle) gates ──────────────────────────────

    private static void EvaluateSteamTurbineGates(
        AirbreathingEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        // STEAM_CONDENSE_BELOW_VACUUM (Hard) — fires when the condenser
        // pressure is below the practical vacuum floor. At P_cond < 0.01 bar
        // the saturation temperature falls below ~46 °C and the vacuum level
        // required exceeds industrial plant capability.
        if (design.SteamCondensePressure_bar < SteamCondensePressure_MinBar)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "STEAM_CONDENSE_BELOW_VACUUM",
                Description: $"Steam condenser pressure P_cond = {design.SteamCondensePressure_bar:F4} bar "
                           + $"below practical vacuum floor {SteamCondensePressure_MinBar:F2} bar. "
                           + $"Achieving this vacuum level is impractical for industrial plant — "
                           + $"increase SteamCondensePressure_bar to at least "
                           + $"{SteamCondensePressure_MinBar:F2} bar.",
                ActualValue: design.SteamCondensePressure_bar,
                Limit:       SteamCondensePressure_MinBar));
        }
    }

    // ── Sprint A8 — gas-turbine (open Brayton) gates ──────────────────────

    private static void EvaluateGasTurbineGates(
        AirbreathingEngineDesign design,
        FlightConditions cond,
        StationMap stations,
        List<FeasibilityViolation> violations,
        List<FeasibilityViolation> advisories)
    {
        var cp = IdealGasAir.Cp_J_kg_K;
        var s0 = stations.Station(0);
        var s2 = stations.Station(2);
        var s3 = stations.Station(3);
        var s4 = stations.Station(4);

        double mdot_air   = s0.MassFlow_kg_s;
        double mdot_total = s4.MassFlow_kg_s;

        double wComp = mdot_air   * cp * (s2.StagnationT_K - s0.StagnationT_K);
        double wTurb = mdot_total * cp * (s3.StagnationT_K - s4.StagnationT_K);
        double wNet  = wTurb - wComp;

        // Gate 1 — GAS_TURBINE_NET_WORK_NEGATIVE (Hard).
        // Fires when the compressor parasitic load equals or exceeds the
        // turbine output — the engine cannot sustain itself.
        if (wNet <= 0)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "GAS_TURBINE_NET_WORK_NEGATIVE",
                Description: $"Net shaft work W_net = {wNet / 1e3:F1} kW ≤ 0. Compressor "
                           + $"parasitic load (W_comp = {wComp / 1e3:F1} kW) equals or exceeds "
                           + $"turbine output (W_turb = {wTurb / 1e3:F1} kW). Increase π_c, "
                           + $"raise TIT (φ), or verify cycle geometry.",
                ActualValue: wNet,
                Limit:       0.0));
        }

        // Gate 2 — GAS_TURBINE_EFFICIENCY_BELOW_FLOOR (Advisory).
        // Fires when cycle thermal efficiency falls below the simple-cycle
        // Brayton physics floor (≈ 0.25 for realistic η_c/η_t and TIT).
        double mdot_fuel = mdot_total - mdot_air;
        if (mdot_fuel > 0)
        {
            double lhv   = AirbreathingFuelTables.Lookup(cond.Fuel).LowerHeatingValue_J_kg;
            double qFuel = mdot_fuel * lhv;
            double etaTh = qFuel > 0 ? wNet / qFuel : 0.0;
            if (etaTh < GasTurbineEfficiencyFloor)
            {
                advisories.Add(new FeasibilityViolation(
                    ConstraintId: "GAS_TURBINE_EFFICIENCY_BELOW_FLOOR",
                    Description: $"Cycle thermal efficiency η_th = {etaTh:P1} below "
                               + $"simple-cycle Brayton floor {GasTurbineEfficiencyFloor:P0}. "
                               + $"Increase π_c, raise TIT (φ), or add a recuperator "
                               + $"(RecuperatorEffectiveness > 0). Advisory only; does not gate "
                               + $"feasibility.",
                    ActualValue: etaTh,
                    Limit:       GasTurbineEfficiencyFloor));
            }
        }

        // Gate 3 — GAS_TURBINE_RECUPERATOR_OVERTEMPERATURE (Advisory).
        // Fires when the turbine exhaust (hot side) is cooler than the
        // compressor discharge (cold side) — the recuperator would run in
        // reverse, losing heat from the cycle rather than recovering it.
        if (design.RecuperatorEffectiveness > 0.0
            && s4.StagnationT_K < s2.StagnationT_K)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: "GAS_TURBINE_RECUPERATOR_OVERTEMPERATURE",
                Description: $"Recuperator hot-side (turbine exit T_t4 = {s4.StagnationT_K:F0} K) "
                           + $"is cooler than cold-side (compressor exit T_t2 = {s2.StagnationT_K:F0} K). "
                           + $"Heat transfer reverses direction — recuperator becomes a load rather "
                           + $"than a recovery. Lower π_c to reduce T_t2, or raise TIT to raise T_t4.",
                ActualValue: s4.StagnationT_K,
                Limit:       s2.StagnationT_K));
        }
    }

    // ── LACE (Liquid Air Cycle Engine) gates — Sprint A.W3 ────────────────

    /// <summary>Precooler effectiveness hard floor; below this, the cycle cannot liquefy air at design Mach.</summary>
    private const double LacePrecoolerEffectivenessHardMin = 0.70;

    /// <summary>Air-side outlet T must drop below this for liquefaction (saturated-liquid air ≈ 90 K at 1 bar).</summary>
    private const double LaceAirLiquefactionTargetTemp_K = 95.0;

    /// <summary>Air-side outlet T above this risks frost-line fouling (water vapour ice on fins).</summary>
    private const double LaceFrostLineAdvisoryTemp_K = 220.0;

    /// <summary>Air-to-fuel ratio hard band low edge (very rich — most fuel exits unburned).</summary>
    private const double LaceAirToFuelHardMin = 2.0;

    /// <summary>Air-to-fuel ratio hard band high edge (very lean — chamber too cold for stable combustion).</summary>
    private const double LaceAirToFuelHardMax = 50.0;

    /// <summary>Air-to-fuel ratio advisory band (stoichiometric H₂/Air ≈ 34.3; cluster sweet spot 5–15).</summary>
    private const double LaceAirToFuelAdvisoryLow = 5.0;

    /// <summary>Air-to-fuel ratio advisory band high edge.</summary>
    private const double LaceAirToFuelAdvisoryHigh = 35.0;

    /// <summary>Chamber pressure hard floor — below this the chamber doesn't choke the throat properly.</summary>
    private const double LaceChamberPressureHardMin_bar = 20.0;

    /// <summary>Chamber pressure hard ceiling — above this the LH₂ pump becomes a major design driver.</summary>
    private const double LaceChamberPressureHardMax_bar = 250.0;

    private static void EvaluateLaceGates(
        AirbreathingEngineDesign design,
        FlightConditions cond,
        StationMap stations,
        List<FeasibilityViolation> violations,
        List<FeasibilityViolation> advisories)
    {
        // Station 1 — post-ram, pre-precooler — carries the air-side total
        // T that the precooler sees. Station 2 — precooler exit — carries
        // the air-side outlet T.
        var s1 = stations.Stations.Count > 1 ? stations.Stations[1] : default;
        var s2 = stations.Stations.Count > 2 ? stations.Stations[2] : default;

        // LACE_PRECOOLER_EFFECTIVENESS_LOW — hard.
        if (design.PrecoolerEffectiveness < LacePrecoolerEffectivenessHardMin)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "LACE_PRECOOLER_EFFECTIVENESS_LOW",
                Description: $"Precooler effectiveness ε = {design.PrecoolerEffectiveness:F3} below "
                           + $"hard floor {LacePrecoolerEffectivenessHardMin:F2}. Air cannot be "
                           + $"cooled enough to liquefy at design Mach (RB-545 / SABRE class "
                           + $"requires ε ≥ 0.85).",
                ActualValue: design.PrecoolerEffectiveness,
                Limit:       LacePrecoolerEffectivenessHardMin));
        }

        // LACE_AIR_LIQUEFACTION_INSUFFICIENT — hard.
        // Air-side outlet T must drop below ~95 K for liquefaction at ~1 bar.
        if (double.IsFinite(s2.StagnationT_K)
            && s2.StagnationT_K > LaceAirLiquefactionTargetTemp_K)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "LACE_AIR_LIQUEFACTION_INSUFFICIENT",
                Description: $"Precooler air-side outlet T = {s2.StagnationT_K:F1} K exceeds "
                           + $"saturated-liquid-air target {LaceAirLiquefactionTargetTemp_K:F0} K. "
                           + $"Air remains gaseous post-precooler; the liquid-air pump cannot "
                           + $"function. Increase PrecoolerEffectiveness, increase LH2MassFlow_kgs, "
                           + $"or reduce flight Mach.",
                ActualValue: s2.StagnationT_K,
                Limit:       LaceAirLiquefactionTargetTemp_K));
        }

        // LACE_AIR_TO_FUEL_OUT_OF_BAND — hard band.
        if (design.LaceAirToFuelRatio < LaceAirToFuelHardMin
            || design.LaceAirToFuelRatio > LaceAirToFuelHardMax)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "LACE_AIR_TO_FUEL_OUT_OF_BAND",
                Description: $"Air-to-fuel ratio MR_a/f = {design.LaceAirToFuelRatio:F2} outside hard "
                           + $"band [{LaceAirToFuelHardMin:F1}, {LaceAirToFuelHardMax:F1}]. "
                           + $"Stable LH₂/Air combustion requires the mixture to be within this band.",
                ActualValue: design.LaceAirToFuelRatio,
                Limit:       design.LaceAirToFuelRatio < LaceAirToFuelHardMin
                                ? LaceAirToFuelHardMin
                                : LaceAirToFuelHardMax));
        }

        // LACE_CHAMBER_PRESSURE_OUT_OF_BAND — hard.
        if (design.LaceChamberPressure_bar < LaceChamberPressureHardMin_bar
            || design.LaceChamberPressure_bar > LaceChamberPressureHardMax_bar)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "LACE_CHAMBER_PRESSURE_OUT_OF_BAND",
                Description: $"Chamber pressure {design.LaceChamberPressure_bar:F1} bar outside "
                           + $"hard band [{LaceChamberPressureHardMin_bar:F0}, "
                           + $"{LaceChamberPressureHardMax_bar:F0}] bar.",
                ActualValue: design.LaceChamberPressure_bar,
                Limit:       design.LaceChamberPressure_bar < LaceChamberPressureHardMin_bar
                                ? LaceChamberPressureHardMin_bar
                                : LaceChamberPressureHardMax_bar));
        }

        // LACE_AIR_TO_FUEL_OUT_OF_ADVISORY — advisory.
        if (design.LaceAirToFuelRatio >= LaceAirToFuelHardMin
            && design.LaceAirToFuelRatio <= LaceAirToFuelHardMax
            && (design.LaceAirToFuelRatio < LaceAirToFuelAdvisoryLow
                || design.LaceAirToFuelRatio > LaceAirToFuelAdvisoryHigh))
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: "LACE_AIR_TO_FUEL_OUT_OF_ADVISORY",
                Description: $"Air-to-fuel ratio MR_a/f = {design.LaceAirToFuelRatio:F2} outside "
                           + $"cluster sweet spot [{LaceAirToFuelAdvisoryLow:F1}, "
                           + $"{LaceAirToFuelAdvisoryHigh:F1}]. RB-545 / SABRE cluster anchors "
                           + $"6–12; values outside this band trade Isp / chamber-T sub-optimally.",
                ActualValue: design.LaceAirToFuelRatio,
                Limit:       design.LaceAirToFuelRatio < LaceAirToFuelAdvisoryLow
                                ? LaceAirToFuelAdvisoryLow
                                : LaceAirToFuelAdvisoryHigh));
        }

        // LACE_PRECOOLER_FROST_LINE_RISK — advisory.
        // Outlet air T below ~220 K can deposit water-vapour ice on fins
        // and gradually block flow (real RB-545 / SABRE work address this
        // via cyclic frost burnoff or hygroscopic pre-dryer; the gate only
        // flags the regime, doesn't gate feasibility).
        if (double.IsFinite(s2.StagnationT_K)
            && s2.StagnationT_K < LaceFrostLineAdvisoryTemp_K
            && s2.StagnationT_K > LaceAirLiquefactionTargetTemp_K)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: "LACE_PRECOOLER_FROST_LINE_RISK",
                Description: $"Precooler air-side outlet T = {s2.StagnationT_K:F1} K below "
                           + $"frost-line trigger {LaceFrostLineAdvisoryTemp_K:F0} K. "
                           + $"Water-vapour ice will deposit on fins; design must include "
                           + $"a cyclic frost-burnoff or hygroscopic pre-dryer.",
                ActualValue: s2.StagnationT_K,
                Limit:       LaceFrostLineAdvisoryTemp_K));
        }
        _ = s1; _ = cond;  // station-1 + ambient reserved for future air-flow consistency gates.
    }

    // ── RDE (Rotating Detonation Engine) gates — Sprint A.W4 ──────────────

    /// <summary>Pressure-gain ratio hard band low edge (PGR > 1.0 required for actual gain).</summary>
    private const double RdePressureGainHardMin = 1.0;

    /// <summary>Pressure-gain ratio hard band high edge — above this, over-driven detonation.</summary>
    private const double RdePressureGainHardMax = 1.50;

    /// <summary>Wave count hard band low edge (single-wave designs are unstable for sustained operation).</summary>
    private const int RdeWaveCountHardMin = 1;

    /// <summary>Wave count hard band high edge — above this, wave interference dominates.</summary>
    private const int RdeWaveCountHardMax = 10;

    /// <summary>Annular channel-width hard floor [m] — must exceed detonation-cell size (~1 mm cluster anchor).</summary>
    private const double RdeAnnularChannelWidthHardMin_m = 0.001;

    /// <summary>Annular channel-width advisory ceiling [m] — too wide and detonation transitions to slow-burn.</summary>
    private const double RdeAnnularChannelWidthAdvMax_m = 0.020;

    /// <summary>L/D advisory band — annulus axial length to outer diameter ratio.</summary>
    private const double RdeLengthToDiameterAdvMin = 0.20;

    /// <summary>L/D advisory band high edge.</summary>
    private const double RdeLengthToDiameterAdvMax = 4.0;

    private static void EvaluateRdeGates(
        AirbreathingEngineDesign design,
        FlightConditions cond,
        StationMap stations,
        List<FeasibilityViolation> violations,
        List<FeasibilityViolation> advisories)
    {
        // RDE_PRESSURE_GAIN_OUT_OF_BAND — hard. Designs claiming PGR ≤ 1.0
        // or > 1.5 are non-physical / over-driven.
        if (design.RdePressureGainRatio < RdePressureGainHardMin
            || design.RdePressureGainRatio > RdePressureGainHardMax)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "RDE_PRESSURE_GAIN_OUT_OF_BAND",
                Description:  $"Pressure-gain ratio PGR = {design.RdePressureGainRatio:F3} outside "
                            + $"hard band [{RdePressureGainHardMin:F2}, {RdePressureGainHardMax:F2}]. "
                            + "Below 1.0: no pressure gain (use Ramjet kind instead). Above 1.50: "
                            + "over-driven detonation, non-physical for typical fuel-air mixtures "
                            + "(H₂/air CJ → 1.27; CH₄/air CJ → 1.18).",
                ActualValue: design.RdePressureGainRatio,
                Limit:       design.RdePressureGainRatio < RdePressureGainHardMin
                                ? RdePressureGainHardMin
                                : RdePressureGainHardMax));
        }

        // RDE_WAVE_COUNT_OUT_OF_BAND — hard.
        if (design.RdeWaveCount < RdeWaveCountHardMin
            || design.RdeWaveCount > RdeWaveCountHardMax)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "RDE_WAVE_COUNT_OUT_OF_BAND",
                Description:  $"Wave count n = {design.RdeWaveCount} outside hard band "
                            + $"[{RdeWaveCountHardMin}, {RdeWaveCountHardMax}]. Single-wave designs "
                            + "are unstable for sustained operation; >10 waves drive interference-"
                            + "dominated combustion (AFRL Anand & Gutmark 2019 cluster envelope).",
                ActualValue: design.RdeWaveCount,
                Limit:       design.RdeWaveCount < RdeWaveCountHardMin
                                ? RdeWaveCountHardMin
                                : RdeWaveCountHardMax));
        }

        // RDE_CHANNEL_WIDTH_BELOW_CELL_SIZE — hard. Annular channel width
        // = (D_o - D_i)/2 must exceed the detonation-cell size (~1 mm).
        double channelWidth_m = 0.5 * (design.RdeAnnularOuterDiameter_m
                                     - design.RdeAnnularInnerDiameter_m);
        if (channelWidth_m < RdeAnnularChannelWidthHardMin_m)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: "RDE_CHANNEL_WIDTH_BELOW_CELL_SIZE",
                Description:  $"Annular channel width {channelWidth_m * 1000.0:F2} mm below "
                            + $"detonation-cell-size floor {RdeAnnularChannelWidthHardMin_m * 1000.0:F1} mm. "
                            + "Detonation wave cannot propagate stably; combustion regresses to deflagration.",
                ActualValue: channelWidth_m,
                Limit:       RdeAnnularChannelWidthHardMin_m));
        }

        // RDE_CHANNEL_WIDTH_ABOVE_ADVISORY — advisory. Too wide and the
        // detonation wave becomes oblique / inefficient.
        if (channelWidth_m >= RdeAnnularChannelWidthHardMin_m
            && channelWidth_m > RdeAnnularChannelWidthAdvMax_m)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: "RDE_CHANNEL_WIDTH_ABOVE_ADVISORY",
                Description:  $"Annular channel width {channelWidth_m * 1000.0:F2} mm above advisory "
                            + $"ceiling {RdeAnnularChannelWidthAdvMax_m * 1000.0:F1} mm. Detonation "
                            + "becomes oblique / inefficient; consider reducing the radial gap.",
                ActualValue: channelWidth_m,
                Limit:       RdeAnnularChannelWidthAdvMax_m));
        }

        // RDE_LENGTH_TO_DIAMETER_OUT_OF_BAND — advisory.
        double LD = design.RdeAnnularLength_m / Math.Max(1e-6, design.RdeAnnularOuterDiameter_m);
        if (LD < RdeLengthToDiameterAdvMin || LD > RdeLengthToDiameterAdvMax)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: "RDE_LENGTH_TO_DIAMETER_OUT_OF_BAND",
                Description:  $"Annulus L/D = {LD:F2} outside advisory band "
                            + $"[{RdeLengthToDiameterAdvMin:F2}, {RdeLengthToDiameterAdvMax:F2}]. "
                            + "Below: insufficient residence time for full reaction. Above: "
                            + "unnecessary mass + cooling overhead with no detonation benefit.",
                ActualValue: LD,
                Limit:       LD < RdeLengthToDiameterAdvMin
                                ? RdeLengthToDiameterAdvMin
                                : RdeLengthToDiameterAdvMax));
        }
        _ = stations; _ = cond;  // station / conditions data reserved for future RDE plume-stability gates.
    }
}
