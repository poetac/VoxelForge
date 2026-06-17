// TurbineSizing.cs — Single-stage impulse turbine sizing that closes
// the turbopump shaft-power loop. Earlier work emitted the pump side
// (impeller + inducer + volute) but left the power source implicit —
// the turbopump geometry reported zero mass for the drive turbine and
// the optimizer could pick a design where the preburner enthalpy
// drop was physically insufficient to turn the pump it was wired to.
//
// What this models
// ────────────────
// Given (a) a sized `PumpSizing` result with shaft-power + RPM, and
// (b) the warm-gas state from the matching `PreburnerResult`, this
// sizer runs the Euler turbomachinery equation in reverse:
//
//   Ideal specific work (isentropic expansion across the turbine):
//     w_isen = cp · T_in · (1 − (P_out/P_in)^((γ−1)/γ))
//   Actual specific work:
//     w      = η · w_isen
//   Available shaft power (all preburner flow crosses the turbine):
//     P_avail = ṁ · w
//   Power balance:
//     PowerSufficient ≡ P_avail ≥ P_required (the pump's ShaftPower_W)
//
// Single-stage impulse velocity-ratio optimum (Sutton RPE 9e §10.5,
// Dixon & Hall "Fluid Mechanics & Thermodynamics of Turbomachinery"
// 7e §9.3): peak efficiency at U/C₀ ≈ 0.5 where C₀ = √(2·w_isen) is
// the isentropic spouting velocity. That inversion gives the tip
// speed:
//     U_tip = 0.5 · √(2 · w_isen)
// and the wheel radius at the pump's shaft angular velocity ω:
//     R_wheel = U_tip / ω,   ω = 2π · RPM / 60
// The wheel is on the common shaft with the pump, so RPM is imposed —
// U_tip and R_wheel are outputs, not design variables.
//
// Cp inversion from (γ, MW): cp = γ/(γ−1) · R_specific where
// R_specific = R_univ / MW. Preburner warm-gas γ ≈ 1.25 + MW ≈ 12 g/mol
// for LOX/CH4 fuel-rich yields cp ≈ 3.5 kJ/(kg·K) — the dominant
// sensitivity in the specific-work equation.
//
// Back-pressure assumptions (cycle-dependent)
// ───────────────────────────────────────────
//   • StagedCombustion / FullFlow — exhaust feeds main chamber, so
//     P_out ≈ P_chamber. Pressure ratio P_in/P_out ≈ 1.3–1.5
//     (preburner is sized at 1.5× Pc; small expansion → small w →
//     large wheel to absorb what work is available).
//   • GasGenerator / OpenExpander — exhaust dumps to ambient, so
//     P_out ≈ 0.1 MPa. Pressure ratio is large (≥ 100×) and w is
//     correspondingly large → small, high-speed wheel.
//
// Shaft-power deficit is a hard feasibility gate
// ──────────────────────────────────────────────
// `TurbineSizingResult.PowerBalanceOK` is the seed for
// `FeasibilityGate`'s new `TURBINE_POWER_DEFICIT` violation (Gate 16).
// Deficit means the preburner picked cannot drive the pump specified —
// the optimizer must reject the candidate.
//
// What this does NOT model (deferred)
// ───────────────────────────────────
//   • Multi-stage / re-entry turbines (single-stage impulse covers
//     the pressure-ratio range of interest for the LOX/CH4 + LOX/RP1
//     + LOX/H2 cycle library; re-entry adds ~1 additional efficiency
//     stage with a second rotor and a full-circumference return duct).
//   • Blade-cooling / film cooling — preburner MR is already picked
//     to keep T_in ≤ 1100 K (uncooled wheel safe).
//   • Shaft bending critical speed (flex-shaft rotordynamics) — the
//     pump and turbine share a shaft but the bearing / critical-speed
//     analysis is outside the geometry pipeline. A **disc-rim stress**
//     check (σ = (3+ν)/8 · ρ · U_tip², Timoshenko & Goodier Theory of
//     Elasticity §32) is a necessary but not sufficient condition for
//     rotordynamic feasibility. Emits a
//     warning when rim stress exceeds Inconel-718 yield ÷ safety
//     factor 2 (= 517 MPa). Full rotordynamic sign-off stays out of
//     scope for a later integrated-shaft sprint.
//
// References
//   Sutton & Biblarz "Rocket Propulsion Elements" 9e §10.5
//     (turbine stages + Euler-head inversion for impulse wheels).
//   Dixon & Hall "Fluid Mechanics & Thermodynamics of Turbomachinery"
//     7e §9.3 (velocity-ratio optimum + cp from γ/MW).
//   Huzel & Huang AIAA Vol. 147 §6.5 (representative single-stage
//     blade + stator counts for LRE turbines).

using Voxelforge.Chamber;

namespace Voxelforge.FeedSystem;

/// <summary>
/// Sized single-stage impulse turbine driving one pump on a common
/// shaft. Produced by <see cref="TurbineSizing.Size"/>; attached to
/// <see cref="TurbopumpResult.FuelTurbine"/> / <see cref="TurbopumpResult.OxTurbine"/>.
/// </summary>
public sealed record TurbineStage(
    string Label,                       // "fuel" / "ox"
    double MassFlow_kgs,                // preburner exhaust mass flow
    double InletTemperature_K,          // preburner T_c
    double InletPressure_Pa,            // preburner P_c
    double OutletPressure_Pa,           // back-pressure (chamber Pc for staged/FFSC; ambient for GG/OE)
    double Gamma,
    double MolecularWeight_gmol,
    double Cp_Jkg_K,
    double Efficiency,                  // total-to-static isentropic
    double IsentropicSpecificWork_Jkg,
    double ActualSpecificWork_Jkg,
    double SpoutingVelocity_ms,         // C₀ = √(2 · w_isen)
    double TipSpeed_ms,                 // U_tip = 0.5 · C₀
    double WheelRadius_mm,
    double Rpm,                         // shared with pump
    int    BladeCount,
    int    StatorVaneCount,
    double RequiredShaftPower_W,        // pump side
    double AvailableShaftPower_W,       // turbine side
    bool   PowerSufficient,
    string Notes,
    // Rim-stress advisory (see TurbineSizing's "disc-rim stress"
    // doc-block). σ_rim = (3+ν)/8 · ρ · U_tip². StressOk = σ_rim ≤
    // MaterialYield_Pa / SafetyFactor. Optional so earlier callers
    // keep compiling.
    double RimStress_Pa = 0.0,
    double RimStressAllowable_Pa = 0.0,
    bool   RimStressOk = true,
    // Sprint 34 / PH-26 (2026-04-25): turbine-stator choke check.
    // π_crit = (2/(γ+1))^(γ/(γ-1)). Stator throat is sonic when
    // p_out / p_in ≤ π_crit; subsonic flow on a supersonic-stator
    // wheel collapses efficiency to ~0.30. Default IsChoked = true
    // to preserve back-compat for synthetic test fixtures that build
    // TurbineStage directly without a sizer pass.
    double CriticalPressureRatio = 0.0,
    bool   IsChoked = true);

/// <summary>
/// Paired turbine sizing across fuel + ox shafts. Paired with the
/// existing <see cref="TurbopumpResult"/> pumps; each side is null
/// when the corresponding pump / preburner is absent.
/// </summary>
public sealed record TurbineSizingResult(
    TurbineStage? FuelTurbine,
    TurbineStage? OxTurbine,
    double TotalAvailableShaftPower_W,
    double TotalRequiredShaftPower_W,
    bool   PowerBalanceOK,
    string[] Warnings,
    string Notes);

public static class TurbineSizing
{
    /// <summary>
    /// Default single-stage impulse total-to-static efficiency
    /// (Sutton Table 10-3, small rocket turbines). Multi-stage cooled
    /// turbines push this to 0.70–0.75 but are outside Phase 2 scope.
    /// </summary>
    public const double DefaultEfficiency = 0.60;

    /// <summary>
    /// Velocity-ratio optimum for single-stage impulse: U/C₀ ≈ 0.5
    /// gives peak wheel efficiency (Dixon & Hall §9.3).
    /// </summary>
    public const double VelocityRatioOptimum = 0.5;

    /// <summary>Universal gas constant (J/(kmol·K)).</summary>
    public const double R_universal = 8314.5;

    /// <summary>
    /// Standard impulse rotor blade count — Huzel &amp; Huang §6.5
    /// reports 30–60 blades as typical for LRE turbine wheels; 36 is
    /// the median for 50–200 mm wheels.
    /// </summary>
    public const int StandardBladeCount = 36;

    /// <summary>
    /// Standard stator vane count for single-stage impulse — Huzel &amp;
    /// Huang §6.5 reports 15–30 nozzle vanes as typical; 24 is the
    /// median.
    /// </summary>
    public const int StandardStatorVaneCount = 24;

    /// <summary>
    /// Back-pressure (Pa) for cycles that dump turbine exhaust to
    /// ambient (gas-generator, open-expander). 1 atm.
    /// </summary>
    public const double AmbientBackPressure_Pa = 1.01325e5;

    /// <summary>
    /// Back-pressure multiplier on main chamber Pc for cycles that
    /// route turbine exhaust into the main chamber (staged combustion,
    /// full-flow). Accounts for the injector ΔP the exhaust must
    /// clear on its way in. Sprint 32 (PH-25): unified across subsystems
    /// to <see cref="CycleSolvers.ChamberInjectionBackPressureRatio"/>.
    /// </summary>
    public const double ChamberInjectionBackPressureRatio
        = CycleSolvers.ChamberInjectionBackPressureRatio;

    /// <summary>
    /// Rim-stress advisory — disc-material density (kg/m³). Matches
    /// the <c>TurbineGeometryGenerator.RotorMaterialDensity_gcm3</c>
    /// (8.9 g/cm³) used for mass estimation, so the stress check is
    /// consistent with the geometry surface.
    /// </summary>
    public const double RotorMaterialDensity_kgm3 = 8900.0;

    /// <summary>
    /// Rim-stress advisory — Inconel 718 room-temp yield (Pa).
    /// Conservative vs. 1034–1200 MPa in Special Metals Publication
    /// SMC-045; chosen for the minimum yield at the 650 °C service
    /// temperature typical of LRE turbine rims. Source: ASM Metals
    /// Handbook Vol 1, "Inconel 718 aged condition".
    /// </summary>
    public const double MaterialYieldStress_Pa = 1.034e9;

    /// <summary>
    /// Rim-stress advisory — safety factor applied to
    /// <see cref="MaterialYieldStress_Pa"/>. 2.0 is the Sutton RPE 9e
    /// §10.5 recommendation for uncooled impulse wheels.
    /// </summary>
    public const double StressSafetyFactor = 2.0;

    /// <summary>
    /// Rim-stress advisory — Poisson's ratio for the (3+ν)/8
    /// coefficient in the Timoshenko &amp; Goodier solid-disc rim
    /// stress formula. 0.30 is the canonical value for ductile
    /// nickel-based superalloys (ASM Handbook Vol 2, "Inconel 718").
    /// </summary>
    public const double PoissonRatio = 0.30;

    /// <summary>
    /// Size both turbines on the engine. Returns a null-ish result
    /// (empty, PowerBalanceOK = true) on PressureFed / ElectricPump —
    /// those cycles don't have turbines.
    /// </summary>
    /// <param name="cycle">Engine cycle. PressureFed / ElectricPump → no-op.</param>
    /// <param name="mainChamberPressure_Pa">
    /// Main chamber Pc. Sets the back-pressure for staged / FFSC cycles.
    /// </param>
    /// <param name="fuelPump">
    /// Sized fuel pump (from <see cref="TurbopumpSizing"/>); null if
    /// pump sizing was degenerate.
    /// </param>
    /// <param name="oxPump">Sized ox pump; null if degenerate.</param>
    /// <param name="fuelPreburner">
    /// Fuel-rich preburner driving the fuel-side turbine. On non-FFSC
    /// cycles with a single preburner this same record drives both
    /// pumps (common-shaft), so pass it as both <paramref name="fuelPreburner"/>
    /// and <paramref name="oxPreburner"/> — the sizer splits its mass
    /// flow proportionally to pump shaft-power demand.
    /// </param>
    /// <param name="oxPreburner">
    /// Ox-rich preburner driving the ox-side turbine (FFSC only). Null
    /// on non-FFSC cycles; the sizer falls back to the fuel preburner
    /// for the ox side with a proportional mass-flow split.
    /// </param>
    /// <param name="efficiency">
    /// Turbine total-to-static efficiency. Default
    /// <see cref="DefaultEfficiency"/>.
    /// </param>
    public static TurbineSizingResult? Size(
        EngineCycle cycle,
        double mainChamberPressure_Pa,
        PumpSizing? fuelPump,
        PumpSizing? oxPump,
        PreburnerResult? fuelPreburner,
        PreburnerResult? oxPreburner,
        double efficiency = DefaultEfficiency)
    {
        // Sprint 21: cycle dispatch via CycleSolvers — adding a new cycle
        // (Expander, ORSC, Tap-off) doesn't require editing this switch.
        var cycleSolver = CycleSolvers.Get(cycle);
        if (!cycleSolver.HasTurbine)
            return null;   // PressureFed / ElectricPump short-circuit

        // Sprint 24: ORSC has fuelPreburner=null (fuel goes direct to
        // main injection; only ox-rich preburner exists and drives
        // both pumps). Accept EITHER preburner as the drive-gas source.
        var drivePreburner = fuelPreburner ?? oxPreburner;
        if (drivePreburner is null)
            return null;   // neither preburner → no gas-turbine drive

        double backPressure = cycleSolver.TurbineDischargeFeedsMainChamber
            ? mainChamberPressure_Pa * ChamberInjectionBackPressureRatio
            : AmbientBackPressure_Pa;

        var warnings = new System.Collections.Generic.List<string>();

        // Non-FFSC: single preburner drives both pumps on a common
        // shaft. Split its mass flow by shaft-power demand so each
        // side gets a proportional slice of the available drive gas.
        // In StagedCombustion / GasGenerator / OpenExpander that drive
        // gas is `fuelPreburner`; in Sprint 24's ORSC it's `oxPreburner`.
        // FFSC: each preburner drives its own turbine, full flow.
        bool isFfsc = cycle == EngineCycle.FullFlow
                   && fuelPreburner is not null && oxPreburner is not null;

        TurbineStage? fuelStage = null, oxStage = null;

        if (fuelPump is not null && fuelPump.ShaftPower_W > 0)
        {
            double fuelMdot = isFfsc
                ? fuelPreburner!.MassFlow_kgs
                : SplitPreburnerFlowForPump(drivePreburner.MassFlow_kgs,
                    fuelPump.ShaftPower_W, oxPump?.ShaftPower_W ?? 0.0, preferSide: true);
            var fuelDrive = isFfsc ? fuelPreburner! : drivePreburner;
            fuelStage = SizeOneStage(
                label:              "fuel",
                pump:               fuelPump,
                preburner:          fuelDrive,
                turbineMassFlow:    fuelMdot,
                backPressure:       backPressure,
                efficiency:         efficiency);
        }

        if (oxPump is not null && oxPump.ShaftPower_W > 0)
        {
            var oxPre = isFfsc ? oxPreburner! : drivePreburner;
            double oxMdot = isFfsc
                ? oxPreburner!.MassFlow_kgs
                : SplitPreburnerFlowForPump(drivePreburner.MassFlow_kgs,
                    fuelPump?.ShaftPower_W ?? 0.0, oxPump.ShaftPower_W, preferSide: false);
            oxStage = SizeOneStage(
                label:              "ox",
                pump:               oxPump,
                preburner:          oxPre,
                turbineMassFlow:    oxMdot,
                backPressure:       backPressure,
                efficiency:         efficiency);
        }

        if (fuelStage is { PowerSufficient: false })
            warnings.Add($"Fuel turbine power deficit: {fuelStage.AvailableShaftPower_W / 1e3:F1} kW available "
                       + $"vs {fuelStage.RequiredShaftPower_W / 1e3:F1} kW required. "
                       + $"Raise preburner Pc, increase preburner mass flow, or lower pump head.");
        if (oxStage is { PowerSufficient: false })
            warnings.Add($"Ox turbine power deficit: {oxStage.AvailableShaftPower_W / 1e3:F1} kW available "
                       + $"vs {oxStage.RequiredShaftPower_W / 1e3:F1} kW required. "
                       + $"Raise preburner Pc, increase preburner mass flow, or lower pump head.");

        // Rim-stress advisory warnings (Timoshenko & Goodier solid-disc
        // peak stress). Advisory only — does NOT seed a feasibility
        // gate, consistent with the rotordynamic-warning scope. Shaft
        // bending critical speed is still outside scope; rim stress is
        // a necessary but not sufficient condition.
        if (fuelStage is { RimStressOk: false })
            warnings.Add($"Fuel turbine rim stress: {fuelStage.RimStress_Pa / 1e6:F0} MPa at "
                       + $"U_tip={fuelStage.TipSpeed_ms:F0} m/s exceeds allowable "
                       + $"{fuelStage.RimStressAllowable_Pa / 1e6:F0} MPa (Inconel-718 yield ÷ safety factor {StressSafetyFactor}). "
                       + $"Reduce shaft RPM, raise pump specific-speed target, or split turbine into two stages.");
        if (oxStage is { RimStressOk: false })
            warnings.Add($"Ox turbine rim stress: {oxStage.RimStress_Pa / 1e6:F0} MPa at "
                       + $"U_tip={oxStage.TipSpeed_ms:F0} m/s exceeds allowable "
                       + $"{oxStage.RimStressAllowable_Pa / 1e6:F0} MPa (Inconel-718 yield ÷ safety factor {StressSafetyFactor}). "
                       + $"Reduce shaft RPM, raise pump specific-speed target, or split turbine into two stages.");

        double totalAvail = (fuelStage?.AvailableShaftPower_W ?? 0) + (oxStage?.AvailableShaftPower_W ?? 0);
        double totalReq   = (fuelStage?.RequiredShaftPower_W   ?? 0) + (oxStage?.RequiredShaftPower_W   ?? 0);
        bool bothOk = (fuelStage?.PowerSufficient ?? true) && (oxStage?.PowerSufficient ?? true);

        string notes = cycle switch
        {
            EngineCycle.StagedCombustion =>
                "Staged combustion — single preburner drives both pumps on common shaft (flow split by shaft-power demand).",
            EngineCycle.FullFlow =>
                "FFSC — fuel-rich preburner drives fuel turbine, ox-rich preburner drives ox turbine (independent shafts).",
            EngineCycle.GasGenerator =>
                "Gas-generator — single preburner drives both pumps; exhaust dumps to ambient (full expansion).",
            EngineCycle.OpenExpander =>
                "Open-expander — regen-heated fuel drives turbine; preburner record used as drive-gas proxy.",
            EngineCycle.ORSC =>
                "ORSC — single ox-rich preburner drives both pumps on common shaft; fuel goes direct to main injection.",
            _ => "",
        };

        return new TurbineSizingResult(
            FuelTurbine:                 fuelStage,
            OxTurbine:                   oxStage,
            TotalAvailableShaftPower_W:  totalAvail,
            TotalRequiredShaftPower_W:   totalReq,
            PowerBalanceOK:              bothOk,
            Warnings:                    warnings.ToArray(),
            Notes:                       notes);
    }

    /// <summary>
    /// Size one turbine stage given its drive gas + target pump. Pure
    /// math; no PicoGK / filesystem dependency. Thread-safe.
    /// </summary>
    public static TurbineStage SizeOneStage(
        string label,
        PumpSizing pump,
        PreburnerResult preburner,
        double turbineMassFlow,
        double backPressure,
        double efficiency = DefaultEfficiency)
    {
        if (pump is null) throw new System.ArgumentNullException(nameof(pump));
        if (preburner is null) throw new System.ArgumentNullException(nameof(preburner));
        if (turbineMassFlow <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(turbineMassFlow),
                "turbine mass flow must be positive");
        if (backPressure <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(backPressure),
                "back-pressure must be positive");
        if (efficiency is <= 0 or > 1)
            throw new System.ArgumentOutOfRangeException(nameof(efficiency),
                "efficiency must lie in (0, 1]");

        double gamma = preburner.WarmGasGamma;
        double mw = preburner.WarmGasMolecularWeight;
        double rSpecific = R_universal / System.Math.Max(mw, 1e-3);
        double cp = gamma / (gamma - 1.0) * rSpecific;

        double pIn = preburner.ChamberPressure_Pa;
        double pOut = System.Math.Min(backPressure, pIn * 0.999);  // clamp to prevent nonsense expansion
        double tIn = preburner.WarmGasTemperature_K;

        double piRatio = pOut / pIn;
        double exponent = (gamma - 1.0) / gamma;
        double wIsentropic = cp * tIn * (1.0 - System.Math.Pow(piRatio, exponent));

        // Sprint 34 / PH-26 (2026-04-25): stator-throat choke check.
        // π_crit = (2/(γ+1))^(γ/(γ-1)) is the critical pressure ratio
        // at which the stator throat becomes sonic. piRatio = p_out/p_in;
        // flow is choked iff piRatio ≤ π_crit. Subsonic flow on a
        // supersonic-stator wheel collapses η — the assumed 0.55-0.60
        // is no longer defensible. The TURBINE_UNCHOKED feasibility gate
        // fires on the unchoked branch.
        double piCrit = System.Math.Pow(
            2.0 / (gamma + 1.0), gamma / (gamma - 1.0));
        bool isChoked = piRatio <= piCrit;
        double wActual = efficiency * wIsentropic;
        double pAvail = turbineMassFlow * wActual;

        double c0 = System.Math.Sqrt(2.0 * System.Math.Max(wIsentropic, 0));
        double uTip = VelocityRatioOptimum * c0;
        double omega = 2.0 * System.Math.PI * pump.Rpm / 60.0;
        double rWheel_m = omega > 0 ? uTip / omega : 0;
        double rWheel_mm = rWheel_m * 1000.0;

        double pReq = pump.ShaftPower_W;
        bool powerOk = pAvail >= pReq;

        // Rim-stress advisory — solid-disc peak stress at the hub for
        // a disc spinning at ω with tip velocity U_tip.
        //   σ_max = (3 + ν)/8 · ρ · U_tip²   (Timoshenko & Goodier §32)
        double rimStress = (3.0 + PoissonRatio) / 8.0
                         * RotorMaterialDensity_kgm3 * uTip * uTip;
        double rimAllowable = MaterialYieldStress_Pa / StressSafetyFactor;
        bool   rimOk = rimStress <= rimAllowable;

        string notes = $"{label} turbine: π={1.0 / piRatio:F2}, w_isen={wIsentropic / 1e3:F0} kJ/kg, "
                     + $"U_tip={uTip:F0} m/s, R={rWheel_mm:F1} mm, RPM={pump.Rpm:F0}, "
                     + $"P_avail={pAvail / 1e3:F1} kW, P_req={pReq / 1e3:F1} kW, "
                     + $"σ_rim={rimStress / 1e6:F0} MPa vs. allowable {rimAllowable / 1e6:F0} MPa"
                     + (rimOk ? "" : " [OVERSPEED]") + ".";

        return new TurbineStage(
            Label:                        label,
            MassFlow_kgs:                 turbineMassFlow,
            InletTemperature_K:           tIn,
            InletPressure_Pa:             pIn,
            OutletPressure_Pa:            pOut,
            Gamma:                        gamma,
            MolecularWeight_gmol:         mw,
            Cp_Jkg_K:                     cp,
            Efficiency:                   efficiency,
            IsentropicSpecificWork_Jkg:   wIsentropic,
            ActualSpecificWork_Jkg:       wActual,
            SpoutingVelocity_ms:          c0,
            TipSpeed_ms:                  uTip,
            WheelRadius_mm:               rWheel_mm,
            Rpm:                          pump.Rpm,
            BladeCount:                   StandardBladeCount,
            StatorVaneCount:              StandardStatorVaneCount,
            RequiredShaftPower_W:         pReq,
            AvailableShaftPower_W:        pAvail,
            PowerSufficient:              powerOk,
            Notes:                        notes,
            RimStress_Pa:                 rimStress,
            RimStressAllowable_Pa:        rimAllowable,
            RimStressOk:                  rimOk,
            CriticalPressureRatio:        piCrit,
            IsChoked:                     isChoked);
    }

    /// <summary>
    /// Common-shaft split: partition the single-preburner mass flow
    /// between the two pumps by shaft-power demand so each side gets a
    /// proportional slice of the drive gas. When either side's demand
    /// is zero, the other side gets the full flow.
    /// </summary>
    private static double SplitPreburnerFlowForPump(
        double totalPreburnerMdot,
        double fuelShaftPower, double oxShaftPower,
        bool preferSide)
    {
        double sum = fuelShaftPower + oxShaftPower;
        if (sum <= 0) return totalPreburnerMdot * 0.5;
        double frac = preferSide
            ? fuelShaftPower / sum
            : oxShaftPower / sum;
        return totalPreburnerMdot * frac;
    }
}
