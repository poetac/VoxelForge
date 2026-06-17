// TurbopumpSizing.cs — Turbopump sizing stub + NPSH gate.
//
// Pushes the tool past the pressure-fed ceiling. The MVP closes
// the feed-system loop for four engine cycles:
//   • PressureFed       — no pump (existing baseline; sizing is a no-op)
//   • GasGenerator      — independent fuel-rich GG drives turbine
//   • ElectricPump      — battery / inverter / electric motor drives pump
//   • OpenExpander      — regen-jacket-heated fuel drives turbine then dumps overboard
//
// Per-pump sizing math:
//   • Required head: Δh_pump = (P_target − P_inlet) / ρ_fluid
//   • Specific energy: w = g · Δh                    [J/kg]
//   • Hydraulic power: P_hyd = ṁ · w                 [W]
//   • Shaft power: P_shaft = P_hyd / η_pump          [W]
//   • RPM via specific-speed (US units): N_s ≈ 2000–3000 for centrifugal
//     impellers, used to back out RPM at the chosen specific speed
//     (returned as a representative number, not a final design value).
//
// NPSH check:
//   • NPSHA = (P_inlet − P_vap) / (ρ · g) + (v_inlet² / 2g)
//   • NPSHR estimated as 1.5 × velocity head at the impeller eye
//     (rule-of-thumb for cryogenic pumps; high inducer NPSHR is much
//     lower but requires inducer design).
//   • NPSH_INSUFFICIENT gate fires when NPSHA < NPSHR.
//
// MVP simplifications (deliberate):
//   • Single representative pump per propellant; no shared-shaft
//     mass-fraction balance for GasGenerator-cycle turbomachinery.
//   • Battery / inverter mass for ElectricPump is a constant scale
//     factor on shaft power (1.5 kg/kW typical for current
//     LiPo/inverter packages).
//   • OpenExpander head budget assumes the expander turbine extracts
//     the full regen-jacket ΔP from the fuel stream — overstates
//     available specific work by ~10 % vs a real expander stage.
//
// References:
//   Sutton & Biblarz "Rocket Propulsion Elements" 9e §10.4
//     (turbopump engine cycles), §10.5 (NPSH and inducers).
//   Huzel & Huang AIAA Vol. 147 §6 (turbopump sizing methodology).
//   Karassik et al. "Pump Handbook" 4e Ch.2 (specific speed,
//     centrifugal-pump similarity).

using Voxelforge.Optimization;

namespace Voxelforge.FeedSystem;

public enum EngineCycle
{
    /// <summary>Pressurised feed. No pumps. Default for legacy designs.</summary>
    PressureFed = 0,
    /// <summary>Gas-generator cycle: independent fuel-rich GG drives turbines.</summary>
    GasGenerator,
    /// <summary>Electric-pump cycle: battery + motor drives pumps directly.</summary>
    ElectricPump,
    /// <summary>Open-expander cycle: regen-heated fuel drives turbine, then dumps overboard.</summary>
    OpenExpander,
    /// <summary>
    /// Staged-combustion cycle (ox-rich or fuel-rich preburner drives
    /// pumps; preburner exhaust feeds main chamber). Requires a
    /// <see cref="Chamber.PreburnerChamber"/> result on
    /// <see cref="Optimization.RegenGenerationResult.Preburner"/>.
    /// </summary>
    StagedCombustion,
    /// <summary>
    /// Full-flow staged combustion — dual preburners (one ox-rich, one
    /// fuel-rich), each drives its own turbine, both exhausts feed the
    /// main chamber. Produces the highest combustion efficiency of any
    /// cycle; characteristic of SpaceX Raptor. The single-preburner
    /// approximation may be used in legacy paths and warns about the
    /// simplification.
    /// </summary>
    FullFlow,
    /// <summary>
    /// Sprint 23 (2026-04-23): closed-expander cycle. Regen-heated fuel
    /// drives turbine; turbine discharge feeds into the main chamber
    /// rather than dumping overboard. Classic for hydrogen engines
    /// (RL10, Vinci, BE-3U — all H2 / O2). Lower specific work than
    /// open-expander at a given inlet state (higher back-pressure), but
    /// no thrust or propellant is lost overboard. Relies on the Sprint
    /// 21 CycleSolver dispatch + the Sprint 23 ExpanderCycleSizing
    /// energy-balance solver.
    /// </summary>
    ClosedExpander,
    /// <summary>
    /// Sprint 24 (2026-04-23): oxygen-rich staged combustion. A single
    /// ox-rich preburner drives the turbine; fuel goes straight to
    /// main-chamber injection (not through the preburner). Russian
    /// heritage — RD-180, RD-191, RD-253; SpaceX Raptor uses this on
    /// the ox side of its FFSC. Distinct from <see cref="FullFlow"/>
    /// (which has BOTH fuel-rich and ox-rich preburners) and from
    /// <see cref="StagedCombustion"/> (which is fuel-rich single).
    /// Requires ox-compatible wall materials in the preburner
    /// (Inconel 718 coated with Cu or NiCrAl; seeded gate
    /// ORSC_PREBURNER_OXCORROSION warns when T &gt; service − 50 K
    /// regardless of the hard WALL_TEMP margin).
    /// </summary>
    ORSC,
    /// <summary>
    /// Sprint 25 (2026-04-23): tap-off cycle. Hot gas tapped from the
    /// main chamber (from a fuel-film-cooled boundary region, not the
    /// core) drives the turbine. No preburner — simpler than
    /// gas-generator / staged, less efficient than expander. Heritage:
    /// Rocketdyne J-2S (Saturn IB upgrade), Blue Origin BE-4 uses a
    /// fuel-rich tap-off on some configurations. Open cycle — turbine
    /// exhaust dumps overboard. The novel feasibility constraint is
    /// the tap-point temperature vs turbine-inlet rating (uncooled
    /// Inconel 718 wheel ~1100 K); the fuel-film-cooled boundary
    /// layer sits at ~30-40 % of chamber Tc, putting the tap point
    /// near the limit at high Pc.
    /// </summary>
    TapOff,
}

/// <summary>
/// Per-propellant pump sizing output.
/// <para>
/// Sprint 3 (2026-04-22) added <see cref="StageCount"/> and
/// <see cref="HeadPerStage_m"/> for multi-stage centrifugal pumps.
/// Single-stage callers (the default for the majority of LRE designs)
/// see <see cref="StageCount"/> = 1 and
/// <see cref="HeadPerStage_m"/> = <see cref="HeadRise_m"/>, preserving
/// the pre-Sprint-3 numerical behaviour bit-identically.
/// </para>
/// </summary>
public sealed record PumpSizing(
    string PropellantLabel,                // "fuel" / "ox"
    double MassFlow_kgs,
    double InletPressure_Pa,
    double DischargePressure_Pa,
    double Density_kgm3,
    double HeadRise_m,                     // TOTAL head rise across all stages
    double HydraulicPower_W,
    double ShaftPower_W,
    double Efficiency,
    double Rpm,
    double NPSHA_m,
    double NPSHR_m,
    bool   NPSHAcceptable,
    // Sprint 3 (2026-04-22) — multi-stage centrifugal pump fields.
    int    StageCount = 1,
    double HeadPerStage_m = 0.0,           // HeadRise_m / StageCount; 0 when StageCount unused
    // Sprint 34b / PH-8 (2026-04-25) — specific speed (US units) computed
    // from the final RPM via N_s = rpm · √Q_gpm / H_ft^0.75 on a per-
    // stage head basis. Defaults to 0 (back-compat for synthetic fixture
    // sites that build PumpSizing directly). Drives the
    // PUMP_SPECIFIC_SPEED_OFF_BAND feasibility gate; values outside the
    // [600, 9000] band fire it. Karassik §2.5; Stepanoff §2.7.
    double SpecificSpeed_US = 0.0);

/// <summary>
/// Output of <see cref="TurbopumpSizing.Size"/>.
/// </summary>
public sealed record TurbopumpResult(
    EngineCycle Cycle,
    PumpSizing? FuelPump,
    PumpSizing? OxPump,
    double TotalShaftPower_W,
    double EstimatedDryMass_kg,            // power-converter + battery mass (electric) or zero (others)
    bool   NPSHFeasible,
    string[] Warnings,
    string Notes,
    // Optional parametric geometry for the fuel-side and ox-side
    // pumps. Populated by
    // `Turbopump.TurbopumpGeometryGenerator.Generate` when
    // `RegenChamberDesign.IncludeTurbopumpGeometry` is true. Null on
    // PressureFed / when generator is skipped / when the underlying
    // `PumpSizing.Rpm` or `.HeadRise_m` is zero.
    Turbopump.TurbopumpGeometry? FuelPumpGeometry = null,
    Turbopump.TurbopumpGeometry? OxPumpGeometry = null,
    // Single-stage impulse turbine sizing that closes the shaft-power
    // loop. Populated by `TurbineSizing.Size` when a preburner is
    // available (StagedCombustion / FullFlow / GasGenerator /
    // OpenExpander). Null on PressureFed / ElectricPump (no turbine)
    // or when no preburner was produced. `Turbine.PowerBalanceOK ==
    // false` seeds the `TURBINE_POWER_DEFICIT` feasibility gate.
    TurbineSizingResult? Turbine = null,
    // Optional parametric geometry for the fuel-side / ox-side
    // turbine wheels. Populated by
    // `Turbopump.TurbineGeometryGenerator.Generate` when
    // `RegenChamberDesign.IncludeTurbopumpGeometry` is true and the
    // matching <see cref="TurbineStage"/> has a positive wheel radius.
    Turbopump.TurbineGeometry? FuelTurbineGeometry = null,
    Turbopump.TurbineGeometry? OxTurbineGeometry = null,
    // First-mode shaft bending critical speed advisory. Populated by
    // <see cref="ShaftCriticalSpeed.Estimate"/> when both a pump
    // geometry and a turbine geometry are available on the matching
    // side. Advisory only — `WhirlOk == false` does NOT seed a
    // feasibility gate, mirroring the rim-stress convention. See
    // <see cref="ShaftCriticalSpeed"/> for model assumptions.
    ShaftCriticalSpeedResult? FuelShaft = null,
    ShaftCriticalSpeedResult? OxShaft = null,
    // PH-47 (issue #192, 2026-04-29): battery energy mass for
    // ElectricPump cycle. 0 when BurnTime_s == 0 (sentinel) or for
    // non-electric cycles. Included in EstimatedDryMass_kg.
    double BatteryMass_kg = 0.0);

public static class TurbopumpSizing
{
    /// <summary>Pump efficiency assumed for centrifugal LRE turbopump impellers (Sutton §10.4).</summary>
    public const double DefaultPumpEfficiency = 0.65;

    /// <summary>Specific-speed (US) for centrifugal LRE pumps used to back out RPM.</summary>
    public const double DefaultSpecificSpeed_US = 2500.0;

    /// <summary>
    /// Suction specific speed (US) — radial impeller without inducer.
    /// Karassik "Pump Handbook" 4e §2.3, Sutton 9e §10.5. Used by the
    /// Thoma-cavitation NPSHR formula (PH-2 fix, Sprint 30).
    /// </summary>
    public const double SuctionSpecificSpeed_NoInducer_US = 8_500.0;

    /// <summary>
    /// Suction specific speed (US) — radial impeller with a well-designed
    /// inducer. Inducers smear the cavitation across blade leading
    /// edges, raising allowable S_s to ~20 000.
    /// </summary>
    public const double SuctionSpecificSpeed_WithInducer_US = 20_000.0;

    /// <summary>
    /// Battery + inverter + motor specific mass (kg per kW shaft).
    /// </summary>
    /// <remarks>
    /// 0.40 kg/kW recalibrated default — literature midpoint of the
    /// 0.20-0.50 kg/kW band, biased toward the modern aerospace anchor
    /// (Rutherford) rather than the auto-EV anchor (Tesla Plaid).
    /// Anchors:
    /// <list type="bullet">
    /// <item>Tesla Plaid traction inverter ≈ 0.30 kg/kW (650 V, 600 kW
    ///   class; public 2021 teardowns).</item>
    /// <item>Rocket Lab Rutherford BLDC controller ≈ 0.40 kg/kW
    ///   (50-80 kW class; Beck 2018 RL2 talks).</item>
    /// <item>EV / industrial PCUs in the 50-500 kW band: 0.30-0.50
    ///   kg/kW typical, 0.20 kg/kW for SiC-MOSFET cutting-edge.</item>
    /// </list>
    /// Recalibrated 2026-04-29 (issue #273) from the prior 1.5 kg/kW
    /// pessimistic seed, which biased SA scoring against ElectricPump
    /// cycles for moderate-burn engine classes (Rutherford, Photon) by
    /// 3-5× over real flight hardware. Bench fingerprints for
    /// ElectricPump cycles shift by 0.267× the converter component;
    /// refresh tracked in issue #272.
    /// </remarks>
    public const double ElectricPowerConverterMass_kg_per_kW = 0.4;

    /// <summary>Standard gravity (m/s²).</summary>
    public const double g0 = 9.80665;

    /// <summary>
    /// True when the cycle uses a single shaft that drives both the fuel
    /// and ox pumps simultaneously. Full-flow staged combustion (FFSC)
    /// is the only turbine-bearing cycle that uses independent shafts —
    /// each preburner drives its own dedicated turbine. Electric and
    /// pressure-fed cycles have no turbine shaft at all.
    /// </summary>
    public static bool IsCommonShaft(EngineCycle cycle) =>
        cycle is EngineCycle.GasGenerator
              or EngineCycle.StagedCombustion
              or EngineCycle.ORSC
              or EngineCycle.OpenExpander
              or EngineCycle.ClosedExpander
              or EngineCycle.TapOff;

    /// <summary>
    /// Size the turbopump (or report the pressure-fed null result) given
    /// the current operating point and feed-line state. <paramref name="cycle"/>
    /// = PressureFed returns a null-ish result that downstream consumers
    /// can ignore.
    /// </summary>
    public static TurbopumpResult Size(
        EngineCycle cycle,
        OperatingConditions cond,
        double fuelFlow_kgs,
        double oxFlow_kgs,
        double fuelDensity_kgm3,
        double oxDensity_kgm3,
        double fuelInletPressure_Pa,
        double oxInletPressure_Pa,
        double dischargePressure_Pa,
        double pumpEfficiency = DefaultPumpEfficiency,
        // Sprint 3 (2026-04-22) — number of serial centrifugal stages
        // on each pump. Default 1 preserves pre-Sprint-3 single-stage
        // sizing bit-identically. Valid range [1, 4]; values outside
        // clamp inside SizeOnePump.
        int stageCount = 1,
        // Sprint 30 (2026-04-24, PH-2) — inducer presence flag. Drives
        // the Thoma-cavitation NPSHR computation: S_s = 8 500 (no
        // inducer) vs 20 000 (with). Default false preserves the
        // pre-Sprint-30 conservative-NPSHR behaviour for callers that
        // haven't been updated.
        bool hasInducer = false,
        // Sprint 34b / PH-8 (2026-04-25) — user-overrideable shaft RPM.
        // Default 0 = auto-derive from N_s = 2500 (pre-Sprint-34b path).
        // > 0 treats RPM as mechanical constraint and reports N_s as
        // diagnostic that drives PUMP_SPECIFIC_SPEED_OFF_BAND gate.
        double pumpRpm_rpm = 0,
        // Sprint feasibility-audit-integrity-bundle-2 (2026-04-27, ID-3) —
        // separate ox-pump discharge pressure. Default 0 = legacy
        // behavior (use the same `dischargePressure_Pa` for both pumps).
        // Real engines have substantially different fuel and ox pump
        // discharges:
        //   • RL10: fuel ~14 MPa, ox ~5 MPa, ratio 2.8×
        //   • Merlin: fuel ~15 MPa, ox ~12 MPa, ratio 1.25×
        //   • F-1: fuel ~9 MPa, ox ~7 MPa, ratio 1.3×
        // The shared-discharge bug was particularly visible on expander
        // cycles where Sprint F1 bumped fuel-pump discharge to 5× Pc to
        // satisfy turbine pressure ratio — that over-spec'd the OX pump
        // (which only needs to push past injector ΔP ≈ 1.2× Pc) by 4-5×
        // on the modeled shaft-power side. The expander still had
        // comfortable margin so feasibility wasn't affected, but the
        // RequiredShaftPower number was misleading. This fix routes ox
        // pump correctly so total shaft power reflects actual physical
        // requirements per pump.
        double oxDischargePressure_Pa = 0.0)
    {
        // Sprint 21: cycle-balance dispatch via CycleSolvers.
        var cycleSolver = CycleSolvers.Get(cycle);
        if (!cycleSolver.HasTurbopump)
        {
            return new TurbopumpResult(
                Cycle:               cycle,
                FuelPump:            null,
                OxPump:              null,
                TotalShaftPower_W:   0,
                EstimatedDryMass_kg: 0,
                NPSHFeasible:        true,
                Warnings:            System.Array.Empty<string>(),
                Notes:               $"{cycle} — no turbomachinery sized.");
        }

        var warnings = new System.Collections.Generic.List<string>();

        var fuelPump = SizeOnePump(
            label:        "fuel",
            massFlow:     fuelFlow_kgs,
            density:      fuelDensity_kgm3,
            inletP:       fuelInletPressure_Pa,
            dischargeP:   dischargePressure_Pa,
            vapourP_Pa:   VapourPressure_Pa(cond, isFuel: true),
            efficiency:   pumpEfficiency,
            stageCount:   stageCount,
            hasInducer:   hasInducer,
            userRpm:      pumpRpm_rpm);
        // Sprint feasibility-audit-integrity-bundle-2 (2026-04-27, ID-3):
        // ox pump uses oxDischargePressure_Pa when explicitly supplied;
        // falls back to fuel discharge for back-compat with all pre-
        // bundle-2 callers.
        double effectiveOxDischarge = oxDischargePressure_Pa > 0
            ? oxDischargePressure_Pa
            : dischargePressure_Pa;
        var oxPump = SizeOnePump(
            label:        "ox",
            massFlow:     oxFlow_kgs,
            density:      oxDensity_kgm3,
            inletP:       oxInletPressure_Pa,
            dischargeP:   effectiveOxDischarge,
            vapourP_Pa:   VapourPressure_Pa(cond, isFuel: false),
            efficiency:   pumpEfficiency,
            stageCount:   stageCount,
            hasInducer:   hasInducer,
            userRpm:      pumpRpm_rpm);

        // PH-48 (issue #193, 2026-04-29): enforce shared shaft speed for
        // common-shaft cycles. When RPM is auto-derived (pumpRpm_rpm == 0),
        // each pump independently targets N_s = 2500, which yields different
        // RPMs for fuel and ox — physically impossible on a single shaft.
        //
        // PH-48 follow-up history:
        //   #274 (PR #309, 2026-04-29) replaced PH-48's conservative
        //     min(fuel_RPM, ox_RPM) with the geometric mean — closed-form,
        //     deterministic, captured 5.79 % of the 6.66 % theoretical
        //     maximum on Merlin-class LOX/CH4 GG (within 0.93 % of
        //     golden-section search).
        //   #310 (this PR) replaced GMEAN with golden-section search on
        //     [min(fuel_RPM, ox_RPM), max(fuel_RPM, ox_RPM)] minimizing
        //     total shaft power. On RL10-class LOX/LH2 closed-expander
        //     (16× density gap between LH2 and LOX), GMEAN landed deep
        //     in the asymmetric region of the log-interpolated Stepanoff
        //     curve and only beat MIN by 4.33 %; the search picks an RPM
        //     near the dominant pump's η peak and beats GMEAN by 28.6 %.
        //     Trade-off: the non-dominant pump's diagnostic N_s lands
        //     outside [600, 9000] → the existing PUMP_SPECIFIC_SPEED_OFF_BAND
        //     gate fires on it (advisory). That's acceptable: the sizing
        //     function picks the most efficient compromise; gates flag
        //     concerns. Two separate jobs.
        //
        // Search is golden-section, deterministic, ~20 iterations × 2
        // SizeOnePump evaluations per call → ~400-800 μs overhead per
        // common-shaft Size() call. No DateTime/Guid calls so the
        // [Deterministic] analyzer (ADR-020) stays green.
        //
        // Only applies to auto-derive mode (pumpRpm_rpm == 0); when the
        // user overrides RPM explicitly both pumps already use the same
        // value.
        if (IsCommonShaft(cycle) && pumpRpm_rpm == 0
            && fuelPump.Rpm > 0 && oxPump.Rpm > 0
            && System.Math.Abs(fuelPump.Rpm - oxPump.Rpm) > 1.0)
        {
            // Independent N_s-derived RPMs become the search bracket
            // endpoints. Capture before overwriting fuelPump/oxPump.
            double fuelRpmIndep = fuelPump.Rpm;
            double oxRpmIndep   = oxPump.Rpm;

            double EvalTotalShaftPowerAt(double rpm)
            {
                var fp = SizeOnePump(
                    label:      "fuel",
                    massFlow:   fuelFlow_kgs,
                    density:    fuelDensity_kgm3,
                    inletP:     fuelInletPressure_Pa,
                    dischargeP: dischargePressure_Pa,
                    vapourP_Pa: VapourPressure_Pa(cond, isFuel: true),
                    efficiency: pumpEfficiency,
                    stageCount: stageCount,
                    hasInducer: hasInducer,
                    userRpm:    rpm);
                var op = SizeOnePump(
                    label:      "ox",
                    massFlow:   oxFlow_kgs,
                    density:    oxDensity_kgm3,
                    inletP:     oxInletPressure_Pa,
                    dischargeP: effectiveOxDischarge,
                    vapourP_Pa: VapourPressure_Pa(cond, isFuel: false),
                    efficiency: pumpEfficiency,
                    stageCount: stageCount,
                    hasInducer: hasInducer,
                    userRpm:    rpm);
                double power = fp.ShaftPower_W + op.ShaftPower_W;
                // NPSH-aware soft penalty: golden-section steers away
                // from RPMs where either pump's NPSHA < NPSHR. Thoma
                // NPSHR scales with rpm·√Q so this naturally penalises
                // the high-RPM end of the bracket (where the dominant
                // pump's η is best). 1e15 W dwarfs any real shaft
                // power (~MW scale) so the penalty acts like a
                // constraint without breaking golden-section's
                // smoothness assumption.
                if (!fp.NPSHAcceptable || !op.NPSHAcceptable)
                    power += OptimizeCommonShaftNpshPenalty_W;
                return power;
            }

            double commonRpm = OptimizeCommonShaftRpm(
                rpmLo: System.Math.Min(fuelRpmIndep, oxRpmIndep),
                rpmHi: System.Math.Max(fuelRpmIndep, oxRpmIndep),
                evalTotalShaftPowerAt: EvalTotalShaftPowerAt);

            fuelPump = SizeOnePump(
                label:      "fuel",
                massFlow:   fuelFlow_kgs,
                density:    fuelDensity_kgm3,
                inletP:     fuelInletPressure_Pa,
                dischargeP: dischargePressure_Pa,
                vapourP_Pa: VapourPressure_Pa(cond, isFuel: true),
                efficiency: pumpEfficiency,
                stageCount: stageCount,
                hasInducer: hasInducer,
                userRpm:    commonRpm);
            oxPump = SizeOnePump(
                label:      "ox",
                massFlow:   oxFlow_kgs,
                density:    oxDensity_kgm3,
                inletP:     oxInletPressure_Pa,
                dischargeP: effectiveOxDischarge,
                vapourP_Pa: VapourPressure_Pa(cond, isFuel: false),
                efficiency: pumpEfficiency,
                stageCount: stageCount,
                hasInducer: hasInducer,
                userRpm:    commonRpm);

            // Final NPSH check: if the converged optimum still lands
            // in NPSH-infeasible territory (every rpm in the bracket
            // tripped NPSHR), retreat to the lowest-RPM endpoint where
            // Thoma NPSHR is minimised. Still-infeasible designs flow
            // through the existing NPSHFeasible = false flag and the
            // warnings list below.
            if (!fuelPump.NPSHAcceptable || !oxPump.NPSHAcceptable)
            {
                double fallbackRpm = System.Math.Min(fuelRpmIndep, oxRpmIndep);
                if (System.Math.Abs(fallbackRpm - commonRpm) > 1.0)
                {
                    fuelPump = SizeOnePump(
                        label:      "fuel",
                        massFlow:   fuelFlow_kgs,
                        density:    fuelDensity_kgm3,
                        inletP:     fuelInletPressure_Pa,
                        dischargeP: dischargePressure_Pa,
                        vapourP_Pa: VapourPressure_Pa(cond, isFuel: true),
                        efficiency: pumpEfficiency,
                        stageCount: stageCount,
                        hasInducer: hasInducer,
                        userRpm:    fallbackRpm);
                    oxPump = SizeOnePump(
                        label:      "ox",
                        massFlow:   oxFlow_kgs,
                        density:    oxDensity_kgm3,
                        inletP:     oxInletPressure_Pa,
                        dischargeP: effectiveOxDischarge,
                        vapourP_Pa: VapourPressure_Pa(cond, isFuel: false),
                        efficiency: pumpEfficiency,
                        stageCount: stageCount,
                        hasInducer: hasInducer,
                        userRpm:    fallbackRpm);
                }
            }
        }

        if (!fuelPump.NPSHAcceptable)
            warnings.Add($"Fuel pump NPSHA {fuelPump.NPSHA_m:F2} m < NPSHR {fuelPump.NPSHR_m:F2} m. "
                       + "Raise tank ullage, lower inlet velocity, or add an inducer.");
        if (!oxPump.NPSHAcceptable)
            warnings.Add($"Ox pump NPSHA {oxPump.NPSHA_m:F2} m < NPSHR {oxPump.NPSHR_m:F2} m. "
                       + "Raise tank ullage, lower inlet velocity, or add an inducer.");

        double totalShaft = fuelPump.ShaftPower_W + oxPump.ShaftPower_W;

        double converterMass = cycleSolver.HasElectricPowerConverter
            ? ElectricPowerConverterMass_kg_per_kW * (totalShaft / 1000.0)
            : 0.0;

        // PH-47 (issue #192): battery energy mass for ElectricPump cycle.
        // Battery energy (MJ) = TotalShaft_W × BurnTime_s / 1e6.
        // Default BurnTime_s = 0 → zero battery mass (backward-compatible).
        double batteryMass = (cycleSolver.HasElectricPowerConverter && cond.BurnTime_s > 0)
            ? (totalShaft / 1e6) * cond.BurnTime_s * cond.BatteryEnergyDensity_kg_per_MJ
            : 0.0;

        double dryMass = converterMass + batteryMass;

        string notes = cycle switch
        {
            EngineCycle.GasGenerator =>
                "Gas-generator cycle — assumes independent fuel-rich GG; turbine sizing not modelled in MVP.",
            EngineCycle.ElectricPump when batteryMass > 0 =>
                $"Electric-pump cycle — power-converter {converterMass:F1} kg "
                + $"+ battery {batteryMass:F1} kg ({cond.BurnTime_s:F0} s burn, "
                + $"{cond.BatteryEnergyDensity_kg_per_MJ:F2} kg/MJ) = {dryMass:F1} kg total.",
            EngineCycle.ElectricPump =>
                $"Electric-pump cycle — power-converter mass estimate {converterMass:F1} kg "
                + $"at {ElectricPowerConverterMass_kg_per_kW:F1} kg/kW (set BurnTime_s to include battery).",
            EngineCycle.OpenExpander =>
                "Open-expander cycle — turbine driven by regen-heated fuel; "
                + "expander turbine sizing not modelled in MVP.",
            EngineCycle.StagedCombustion =>
                "Staged-combustion cycle — preburner drives turbines; "
                + "preburner exhaust feeds main chamber. See result.Preburner for sizing.",
            EngineCycle.FullFlow =>
                "Full-flow staged combustion — single-preburner approximation on this path; "
                + "for dual-preburner (ox-rich + fuel-rich) modelling use SizeFfscDual.",
            _ => "",
        };

        return new TurbopumpResult(
            Cycle:               cycle,
            FuelPump:            fuelPump,
            OxPump:              oxPump,
            TotalShaftPower_W:   totalShaft,
            EstimatedDryMass_kg: dryMass,
            NPSHFeasible:        fuelPump.NPSHAcceptable && oxPump.NPSHAcceptable,
            Warnings:            warnings.ToArray(),
            Notes:               notes,
            BatteryMass_kg:      batteryMass);
    }

    /// <summary>
    /// Minimum pump stage count. Values below clamp to 1 (single stage).
    /// </summary>
    public const int MinStageCount = 1;

    /// <summary>
    /// Maximum pump stage count. LRE-class centrifugal pumps rarely
    /// exceed 4 stages — beyond that axial layouts dominate and the
    /// centrifugal-similarity math in <see cref="SizeOnePump"/> no
    /// longer applies. Values above clamp to 4.
    /// </summary>
    public const int MaxStageCount = 4;

    // PH-48 follow-up #310 (2026-04-29): golden-section search bounds
    // for the common-shaft RPM compromise. The search runs over
    // [min(fuel_RPM_indep, ox_RPM_indep), max(fuel_RPM_indep, ox_RPM_indep)]
    // — the two endpoints are the per-pump independent N_s = 2500 target
    // RPMs. Inside the bracket the dominant pump (the one with larger
    // hydraulic power) dictates the optimum. Tolerance is relative to
    // the bracket midpoint; 1e-3 converges in ~20 iterations across
    // bracket ratios up to ~15× (the LOX/LH2 worst case).
    internal const int OptimizeCommonShaftMaxIterations = 30;
    internal const double OptimizeCommonShaftRelTolerance = 1.0e-3;

    /// <summary>
    /// Soft-penalty value (W) added to the golden-section objective
    /// when either pump's NPSHA &lt; NPSHR at a candidate shaft RPM.
    /// 1e15 W dwarfs any real combined shaft power (typical LRE
    /// turbopumps run at MW scale, six orders of magnitude smaller),
    /// so the penalty acts as a hard constraint while preserving
    /// golden-section's monotone-bracket assumption. If every RPM in
    /// the bracket trips NPSH, the function still returns its lowest
    /// penalty value; the caller falls back to the min-RPM endpoint
    /// (lowest Thoma NPSHR) and propagates NPSHFeasible = false.
    /// </summary>
    internal const double OptimizeCommonShaftNpshPenalty_W = 1.0e15;

    /// <summary>
    /// Golden-section search for the common-shaft RPM that minimises
    /// the sum of fuel + ox shaft power. Closed-form alternative is
    /// the geometric mean of <paramref name="rpmLo"/> and
    /// <paramref name="rpmHi"/>; this routine improves on geometric
    /// mean when the η-vs-N_s curve is steeply asymmetric (e.g.
    /// LOX/LH2 with a 16× density gap pushing the dominant pump's
    /// preferred RPM far above the geometric mean — see issue #310).
    /// Pure-numeric implementation; deterministic.
    /// </summary>
    private static double OptimizeCommonShaftRpm(
        double rpmLo,
        double rpmHi,
        System.Func<double, double> evalTotalShaftPowerAt)
    {
        // Golden ratio reciprocal: φ = 2 / (1 + √5) ≈ 0.6180339887...
        const double phi = 0.6180339887498949;

        double a = rpmLo;
        double b = rpmHi;
        double tol = OptimizeCommonShaftRelTolerance * (rpmLo + rpmHi) * 0.5;

        double x1 = b - phi * (b - a);
        double x2 = a + phi * (b - a);
        double f1 = evalTotalShaftPowerAt(x1);
        double f2 = evalTotalShaftPowerAt(x2);

        for (int iter = 0; iter < OptimizeCommonShaftMaxIterations; iter++)
        {
            if (b - a < tol) break;
            if (f1 < f2)
            {
                b = x2;
                x2 = x1;
                f2 = f1;
                x1 = b - phi * (b - a);
                f1 = evalTotalShaftPowerAt(x1);
            }
            else
            {
                a = x1;
                x1 = x2;
                f1 = f2;
                x2 = a + phi * (b - a);
                f2 = evalTotalShaftPowerAt(x2);
            }
        }

        return (a + b) * 0.5;
    }

    private static PumpSizing SizeOnePump(
        string label, double massFlow, double density,
        double inletP, double dischargeP,
        double vapourP_Pa, double efficiency,
        int stageCount = 1,
        bool hasInducer = false,
        // Sprint 34b / PH-8 (2026-04-25) — user-overrideable shaft RPM.
        // Default 0 = auto-derive from N_s = 2500 (pre-Sprint-34b
        // behaviour). When > 0, treats RPM as the mechanical constraint
        // and lets N_s fall out of the design — matching real LRE
        // workflow where bearing / shaft / seal limits set RPM and N_s
        // is a diagnostic. Values that drift N_s outside [600, 9000]
        // fire the PUMP_SPECIFIC_SPEED_OFF_BAND feasibility gate.
        double userRpm = 0)
    {
        int N = System.Math.Clamp(stageCount, MinStageCount, MaxStageCount);

        if (massFlow <= 0 || density <= 0)
        {
            return new PumpSizing(
                PropellantLabel:     label,
                MassFlow_kgs:        massFlow, InletPressure_Pa: inletP,
                DischargePressure_Pa:dischargeP, Density_kgm3: density,
                HeadRise_m:          0, HydraulicPower_W: 0,
                ShaftPower_W:        0, Efficiency: efficiency,
                Rpm:                 0,
                NPSHA_m:             0, NPSHR_m: 0,
                NPSHAcceptable:      true,
                StageCount:          N,
                HeadPerStage_m:      0);
        }

        // Z2.7 hot-fix follow-on (2026-04-28): inverted-feed early exit.
        // External-audit F-2: when `dischargeP ≤ inletP` (the pump can't
        // raise pressure because the feed already exceeds the discharge
        // target — physically impossible, indicates upstream cycle-balance
        // bug or SA candidate exploring outside the feasible region), the
        // legacy `Math.Max(dP, 0)` clamp silently zeroed the head rise.
        // Cascading effects: pHyd = 0, pShaft = 0, rpm = 0, NPSHR_m = 0,
        // NPSHA_m ≥ 0 → NPSHAcceptable always true. Cycle balance saw
        // fake-zero ShaftPower_W and TURBINE_POWER_DEFICIT spuriously
        // passed. The PUMP_PRESSURE_INVERTED gate (gate 14b, post-Phase-6
        // Tier-1 bundle) catches the inversion post-hoc but only AFTER
        // downstream sees the misleading numbers.
        //
        // Fix: return a sentinel with ShaftPower_W = +Infinity (cycle-
        // balance will report TURBINE_POWER_DEFICIT consistently) and
        // NPSHAcceptable = false (NPSH gate fires too) and Efficiency = NaN
        // (downstream consumers treating efficiency as a valid number will
        // immediately flag the result via NaN propagation rather than
        // computing a plausible-looking zero).
        if (dischargeP <= inletP)
        {
            return new PumpSizing(
                PropellantLabel:     label,
                MassFlow_kgs:        massFlow,
                InletPressure_Pa:    inletP,
                DischargePressure_Pa:dischargeP,
                Density_kgm3:        density,
                HeadRise_m:          0,
                HydraulicPower_W:    0,
                ShaftPower_W:        double.PositiveInfinity,
                Efficiency:          double.NaN,
                Rpm:                 0,
                NPSHA_m:             0, NPSHR_m: 0,
                NPSHAcceptable:      false,
                StageCount:          N,
                HeadPerStage_m:      0);
        }

        double dP = dischargeP - inletP;
        // (Math.Max(..., 0) clamp removed by Z2.7 — the inverted-feed case
        // is now handled by the early exit above; non-inverted cases have
        // dP > 0 by construction.)
        double headRise_m = dP / (density * g0);
        double w_Jkg = g0 * headRise_m;
        double pHyd = massFlow * w_Jkg;
        // pShaft computed below after specificSpeed_US is available so
        // the Stepanoff η correlation (PH-8 companion, Sprint 34c) can
        // size shaft power against the actual N_s rather than a constant.

        // Sprint 3 (2026-04-22) — multi-stage centrifugal pump:
        // serial stages each add H_stage = H_total / N of head. RPM is
        // set by the single-stage specific-speed relation on the
        // per-stage head (Karassik "Pump Handbook" 4e §2.5). Lower
        // per-stage head → lower RPM, which lowers both rim stress and
        // shaft-whirl risk — this is the primary reason a designer
        // reaches for a second stage. Hydraulic and shaft power are
        // unaffected (conservation: ΣH_stage × ṁ × g = H_total × ṁ × g).
        double headPerStage_m = headRise_m / N;

        // RPM via centrifugal-pump specific speed (US):
        //   N_s = N · √Q / H^(3/4),  Q in gpm, H in ft, N in rpm.
        // Convert SI: Q_gpm = m_dot/ρ × 15850.3, H_ft = headPerStage × 3.2808.
        double Q_gpm = (massFlow / density) * 15850.3;
        double Hstage_ft = headPerStage_m * 3.28084;
        double rpm;
        if (userRpm > 0)
        {
            // Sprint 34b / PH-8: user-specified RPM treated as the
            // mechanical constraint (bearings / shaft strength / seal
            // limits). N_s is computed from this RPM and reported as a
            // diagnostic; out-of-band N_s fires the gate.
            rpm = userRpm;
        }
        else if (Q_gpm > 0 && Hstage_ft > 0)
        {
            // Pre-Sprint-34b path: derive RPM from the legacy 2500 N_s
            // assumption. By construction, N_s computed back from this
            // RPM will round-trip to ~2500 (well inside the gate band).
            rpm = DefaultSpecificSpeed_US * System.Math.Pow(Hstage_ft, 0.75)
                / System.Math.Sqrt(Q_gpm);
        }
        else
        {
            rpm = 0;
        }

        // Sprint 34b / PH-8 (2026-04-25): always compute N_s from the
        // final RPM as a diagnostic. Pre-Sprint-34b every design
        // reported the constant 2500 silently — physically impossible
        // for tiny 0.01 kg/s and large 50 kg/s pumps both to live at
        // the same N_s. Real workflow: RPM is the input, N_s falls out.
        double specificSpeed_US =
            (rpm > 0 && Q_gpm > 0 && Hstage_ft > 0)
                ? rpm * System.Math.Sqrt(Q_gpm) / System.Math.Pow(Hstage_ft, 0.75)
                : 0;

        // Sprint 34c / PH-8 companion (2026-04-25): η from Stepanoff
        // correlation (N_s, Q) replaces the pre-Sprint-34c constant 0.65.
        // Real centrifugal LRE η = 0.40-0.85 depending on N_s and Q;
        // the constant flattered shaft power numbers on tiny/large pumps
        // by ±20 %. The `efficiency` parameter survives in the API as a
        // shipped-API back-compat surface but is ignored for compute —
        // explicit overrides should now flow through OperatingConditions.
        double effectiveEfficiency = PumpEfficiencyCorrelation.Efficiency(specificSpeed_US, Q_gpm);
        double pShaft = pHyd / System.Math.Max(effectiveEfficiency, 1e-3);

        // NPSHA: ((P_inlet − P_vap) / (ρ g)) + v_inlet²/2g.
        // Assume inlet velocity = 5 m/s (typical pump-suction line) for v² head.
        const double v_suction_ms = 5.0;
        double NPSHA_m = (inletP - vapourP_Pa) / (density * g0)
                       + (v_suction_ms * v_suction_ms) / (2.0 * g0);

        // NPSHR via Thoma cavitation form (PH-2 fix, Sprint 30).
        //   Suction specific speed S_s = N · √Q / NPSHR^(3/4)  [US units: rpm, gpm, ft]
        //   → NPSHR_ft = (N · √Q / S_s)^(4/3)
        //   → NPSHR_m  = NPSHR_ft × 0.3048
        // S_s defaults to 8 500 (radial impeller, no inducer), Karassik
        // "Pump Handbook" 4e §2.3. With a well-designed inducer
        // (RegenChamberDesign.HasInducer = true) S_s rises to ~20 000,
        // roughly halving NPSHR.
        //
        // Pre-Sprint-30 the NPSHR was a constant velocity-head
        // estimate (~1.91 m for v_eye = 5 m/s), independent of RPM,
        // flow, and inducer presence. That under-predicted real
        // cavitation risk and silently passed cavitating designs
        // through the NPSH_INSUFFICIENT gate.
        double S_s_us = hasInducer
            ? SuctionSpecificSpeed_WithInducer_US
            : SuctionSpecificSpeed_NoInducer_US;
        double NPSHR_m;
        if (rpm > 0 && Q_gpm > 0 && S_s_us > 0)
        {
            double NPSHR_ft = System.Math.Pow(rpm * System.Math.Sqrt(Q_gpm) / S_s_us, 4.0 / 3.0);
            NPSHR_m = NPSHR_ft * 0.3048;
        }
        else
        {
            NPSHR_m = 0;
        }

        return new PumpSizing(
            PropellantLabel:     label,
            MassFlow_kgs:        massFlow,
            InletPressure_Pa:    inletP,
            DischargePressure_Pa:dischargeP,
            Density_kgm3:        density,
            HeadRise_m:          headRise_m,
            HydraulicPower_W:    pHyd,
            ShaftPower_W:        pShaft,
            Efficiency:          effectiveEfficiency,
            Rpm:                 rpm,
            NPSHA_m:             NPSHA_m,
            NPSHR_m:             NPSHR_m,
            NPSHAcceptable:      NPSHA_m >= NPSHR_m,
            StageCount:          N,
            HeadPerStage_m:      headPerStage_m,
            SpecificSpeed_US:    specificSpeed_US);
    }

    /// <summary>
    /// Vapour pressure (Pa) at pump inlet temperature for the active
    /// propellant. Cryogenic propellants near boiling sit close to the
    /// inlet pressure; storables stay far below.
    /// </summary>
    private static double VapourPressure_Pa(OperatingConditions cond, bool isFuel)
    {
        var meta = Combustion.PropellantPairs.GetMeta(cond.PropellantPair);
        string key = isFuel ? meta.CoolantFluidKey : meta.OxidiserSymbol;

        // Issue #158 (A6): when the oxidizer-side tank-inlet temperature
        // is set on OperatingConditions, compute P_vap from the Antoine
        // equation rather than the legacy per-fluid constant. Only the
        // ox side is wired (per the issue's stated scope). The fuel
        // side falls through to the legacy constant table below; the
        // analogous fuel-side wiring is gated on a separate change to
        // CoolantInletTemp_K's semantics (today it's the *coolant*
        // entry temperature for the regen jacket, not necessarily the
        // pump-suction temperature on staged-combustion / GG cycles).
        if (!isFuel && cond.OxidizerInletTemp_K > 0.0)
        {
            var antoine = Coolant.Antoine.VaporPressureForFluid_Pa(key, cond.OxidizerInletTemp_K);
            if (antoine.HasValue) return antoine.Value;
        }

        // Legacy per-fluid constants. Conservative bounds keyed off
        // coolant-fluid identity. Real pump-inlet T comes from tank
        // ullage / chilldown — out of MVP. Default `OxidizerInletTemp_K = 0`
        // (sentinel) on OperatingConditions makes this path bit-identical
        // to pre-A6 callers that don't opt in.
        return key switch
        {
            "CH4"  => 0.5e5,    // saturation @ ~150 K
            "H2"   => 0.4e5,    // saturation @ ~25 K
            "RP-1" => 100.0,    // tiny — kerosene at room T
            "LOX"  => 0.5e5,    // saturation @ ~95 K
            _      => 1e3,
        };
    }
}
