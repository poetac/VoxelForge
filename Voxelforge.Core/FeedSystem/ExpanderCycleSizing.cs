// ExpanderCycleSizing.cs — Sprint 23 (2026-04-23):
// Coolant-driven turbine energy balance for expander cycles.
//
// What this models
// ────────────────
// Expander cycles have no preburner — the regen-jacket-heated fuel (or
// on some historical designs, oxidiser, but fuel is the dominant real-
// world choice) drives the turbine directly. The jacket absorbs ~10-30 %
// of the gas-side heat load; at typical LH2 / LOX regen conditions
// this adds ~150-300 K to the coolant before it enters the turbine at
// jacket-outlet pressure.
//
// Given the jacket-outlet state (T, P) from `RegenCoolingSolver`, the
// coolant mass flow (= fuel mass flow, since 100 % of fuel goes through
// the jacket on an expander), and the required pump shaft power from
// `TurbopumpSizing`, this module answers: is the isentropic expansion
// across the turbine enough to supply the pump?
//
//   Specific heat ratio:
//     cp  = CoolantState.Cp_Jkg        (from the coolant-fluid table)
//     R   = R_universal / MW            (MW from fluid metadata)
//     γ   ≈ cp / (cp - R)               (ideal-gas approximation)
//   Isentropic specific work across (P_in → P_out):
//     w_isen = cp · T_in · (1 − (P_out/P_in)^((γ−1)/γ))
//   Actual specific work (turbine efficiency η):
//     w      = η · w_isen
//   Available shaft power (all coolant crosses the turbine):
//     P_avail = ṁ_coolant · w
//   Power balance:
//     PowerSufficient ≡ P_avail ≥ P_required
//
// Back-pressure (cycle-dependent, dispatched via CycleSolvers):
//   • ClosedExpander — P_out ≈ P_chamber · 1.3 (chamber-injection
//     pressure to push gas into the main chamber). Lower w; requires
//     higher jacket outlet P or ΔT to close the loop.
//   • OpenExpander   — P_out ≈ 0.1 MPa (ambient). High pressure ratio
//     → large w; simpler to close, but turbine exhaust is thrown away
//     (no main-chamber re-use).
//
// Simplifications (deliberate)
// ────────────────────────────
//   • Ideal-gas γ from cp and R — skips real-fluid effects in the
//     supercritical / pseudocritical regions. For H₂ at jacket-outlet
//     conditions (P ≈ 10 MPa, T ≈ 300 K) γ ≈ 1.40 is accurate to
//     ±5 %; for CH₄ at P ≈ 10 MPa, T ≈ 400 K, γ ≈ 1.25 and the
//     approximation is within ±10 %. A future sprint can wire a real-
//     fluid EoS when higher fidelity is needed.
//   • One-shot energy balance, no Picard iteration on ṁ_c. The 2026-04-23
//     physics audit (PH-9) flags this as a circular dependency:
//     "jacket heat absorbed scales with ṁ_c (via h_c); turbine work
//     depends on jacket-outlet T — w(ṁ_c) and ṁ_c(P_avail) form a
//     fixed-point loop". Sprint 34d analysis confirms this circularity
//     is real ONLY under expander-cycle models that treat ṁ_c as a
//     free variable (jacket-bypass throttling). The current voxelforge
//     model pins ṁ_c by mass balance:
//         coolantMassFlow = FuelMassFlow × (1 − FilmCoolingFraction)
//     (RegenChamberOptimization.cs:303-305). All non-film-cooled fuel
//     goes through the jacket regardless of cycle. With ṁ_c pinned,
//     the regen solver is a forward march (no feedback) and the
//     expander balance is genuinely one-shot — power balance becomes
//     a feasibility test (P_avail vs P_required), not a fixed-point.
//     The MassFlowMargin field exposes the (P_avail / P_required − 1)
//     ratio so callers can see the margin / deficit. PH-9's recipe
//     becomes meaningful when a future `JacketBypassFraction` design
//     variable lands; until then, no iteration is needed.
//   • No blade / stator geometry — only specific-work + power balance.
//     TurbineSizing.cs covers the mechanical-wheel geometry for
//     preburner-driven stages; a follow-on sprint can extend that to
//     expander turbines if voxel-level STL output is needed.
//
// Feasibility gate: EXPANDER_TURBINE_ENTHALPY_DEFICIT fires when
// P_avail < P_required. See ADR-009 (gate #22).
//
// References
// ──────────
//   Sutton & Biblarz "Rocket Propulsion Elements" 9e §10.4 (expander
//     cycle topology), §10.5 (turbomachinery specific work).
//   Huzel & Huang AIAA Vol. 147 §6.5 (pressure-ratio regimes).
//   Ponomarenko, "Expander cycle design philosophy" (RL10 lineage).

using Voxelforge.Coolant;

namespace Voxelforge.FeedSystem;

/// <summary>
/// Sprint 23: Result of <see cref="ExpanderCycleSizing.Size"/>. Attached
/// to <see cref="Optimization.RegenGenerationResult.ExpanderTurbine"/>
/// when the cycle is OpenExpander / ClosedExpander. <see cref="PowerSufficient"/>
/// seeds the <c>EXPANDER_TURBINE_ENTHALPY_DEFICIT</c> feasibility gate.
/// </summary>
public sealed record ExpanderTurbineResult(
    EngineCycle Cycle,
    string      CoolantLabel,            // "CH4" | "H2" | "RP-1"
    double      InletTemperature_K,      // coolant at jacket outlet
    double      InletPressure_Pa,
    double      OutletPressure_Pa,       // ambient for Open; chamber × ratio for Closed
    double      MassFlow_kgs,            // fuel mass flow (= coolant flow)
    double      Cp_Jkg_K,
    double      EffectiveGamma,
    double      IsentropicSpecificWork_Jkg,
    double      ActualSpecificWork_Jkg,
    double      Efficiency,
    double      AvailableShaftPower_W,
    double      RequiredShaftPower_W,
    bool        PowerSufficient,
    string      Notes,
    // Sprint 34 / PH-26 (2026-04-25): turbine-stator choke check.
    // Default IsChoked = true to preserve back-compat for synthetic
    // test fixtures that build ExpanderTurbineResult directly.
    double      CriticalPressureRatio = 0.0,
    bool        IsChoked = true,
    // Sprint 34d / PH-9 (2026-04-25): mass-flow consistency margin.
    // Defined as (P_avail / P_required − 1). Zero indicates exact
    // power balance (the converged state of an ṁ_c iteration); positive
    // indicates surplus margin; negative indicates deficit (= the
    // EXPANDER_TURBINE_ENTHALPY_DEFICIT firing condition). For the
    // current pinned-ṁ_c model this field is purely diagnostic — no
    // iteration is needed because the model has no degree of freedom
    // to shift ṁ_c. See file header for PH-9 model-assumption analysis.
    double      MassFlowMargin = 0.0);

public static class ExpanderCycleSizing
{
    /// <summary>
    /// Default total-to-static efficiency for an expander-cycle turbine.
    /// Lower than the ~0.60 preburner-driven default because expander
    /// fluids run wetter (partial-condensate risk in H₂ at low Pr) and
    /// the single-stage optimum is shifted by the higher specific heat
    /// ratio of fuel-side coolants. Sutton §10.4 Table 10-3 cites
    /// 0.50-0.60 for expander turbines; 0.55 is the median.
    /// </summary>
    public const double DefaultEfficiency = 0.55;

    /// <summary>Ambient back-pressure for open-expander turbine exhaust (Pa).</summary>
    public const double AmbientBackPressure_Pa = 101_325.0;

    /// <summary>
    /// Chamber-injection back-pressure ratio for closed-expander
    /// turbine exhaust. Sprint 32 (PH-25): unified across subsystems
    /// to <see cref="CycleSolvers.ChamberInjectionBackPressureRatio"/>
    /// (was 1.30 here vs 1.10 in <see cref="TurbineSizing"/> — drift
    /// fixed).
    /// </summary>
    public const double ChamberInjectionBackPressureRatio
        = CycleSolvers.ChamberInjectionBackPressureRatio;

    /// <summary>Universal gas constant (J/(kmol·K)).</summary>
    public const double R_universal = 8314.5;

    /// <summary>
    /// Size the expander-cycle turbine energy balance. Returns <c>null</c>
    /// on any non-expander cycle (dispatch via <see cref="CycleSolvers"/>)
    /// or when the coolant outlet temperature didn't rise above the
    /// inlet (no enthalpy to extract — jacket didn't do any work).
    /// </summary>
    /// <param name="cycle">Engine cycle — must be OpenExpander or ClosedExpander to return non-null.</param>
    /// <param name="coolant">Regen-jacket fluid (from <see cref="CoolantRegistry"/>).</param>
    /// <param name="coolantOutletT_K">Coolant bulk T at jacket outlet (from <see cref="HeatTransfer.RegenSolverOutputs.CoolantOutletT_K"/>).</param>
    /// <param name="coolantOutletP_Pa">Coolant P at jacket outlet (from <see cref="HeatTransfer.RegenSolverOutputs.CoolantOutletP_Pa"/>).</param>
    /// <param name="coolantInletT_K">Coolant bulk T at jacket inlet (for jacket-did-no-work early-return check).</param>
    /// <param name="coolantMassFlow_kgs">Fuel mass flow — the entire fuel side drives the turbine on an expander.</param>
    /// <param name="mainChamberPressure_Pa">Main chamber Pc, for closed-expander back-pressure.</param>
    /// <param name="requiredPumpShaftPower_W">Total pump shaft power required, from <see cref="TurbopumpResult.TotalShaftPower_W"/>.</param>
    /// <param name="efficiency">Turbine total-to-static efficiency; defaults to <see cref="DefaultEfficiency"/>.</param>
    public static ExpanderTurbineResult? Size(
        EngineCycle   cycle,
        ICoolantFluid coolant,
        double        coolantOutletT_K,
        double        coolantOutletP_Pa,
        double        coolantInletT_K,
        double        coolantMassFlow_kgs,
        double        mainChamberPressure_Pa,
        double        requiredPumpShaftPower_W,
        double        efficiency = DefaultEfficiency)
    {
        if (cycle is not EngineCycle.OpenExpander and not EngineCycle.ClosedExpander)
            return null;
        if (coolantMassFlow_kgs <= 0 || coolantOutletP_Pa <= 0)
            return null;
        // Jacket did no work — expander has no energy to extract.
        if (coolantOutletT_K <= coolantInletT_K)
            return null;

        double P_out = cycle == EngineCycle.ClosedExpander
            ? mainChamberPressure_Pa * ChamberInjectionBackPressureRatio
            : AmbientBackPressure_Pa;

        // Ensure forward expansion (P_out < P_in). If the jacket-outlet
        // P has already dropped below the target back-pressure (failed
        // pump discharge), flag as insufficient rather than produce
        // negative specific work.
        if (P_out >= coolantOutletP_Pa)
        {
            return new ExpanderTurbineResult(
                Cycle: cycle,
                CoolantLabel: coolant.Metadata.Key,
                InletTemperature_K: coolantOutletT_K,
                InletPressure_Pa: coolantOutletP_Pa,
                OutletPressure_Pa: P_out,
                MassFlow_kgs: coolantMassFlow_kgs,
                Cp_Jkg_K: 0,
                EffectiveGamma: 0,
                IsentropicSpecificWork_Jkg: 0,
                ActualSpecificWork_Jkg: 0,
                Efficiency: efficiency,
                AvailableShaftPower_W: 0,
                RequiredShaftPower_W: requiredPumpShaftPower_W,
                PowerSufficient: false,
                Notes: $"{cycle} back-pressure {P_out / 1e6:F2} MPa ≥ jacket outlet "
                     + $"{coolantOutletP_Pa / 1e6:F2} MPa — no forward expansion. "
                     + "Raise pump discharge pressure or reduce jacket ΔP.");
        }

        var state = coolant.GetState(coolantOutletT_K, coolantOutletP_Pa);
        double cp = state.Cp_Jkg;
        double R_specific = R_universal / System.Math.Max(coolant.Metadata.MW_gmol, 0.1);
        // γ = cp / cv, cv = cp − R (ideal-gas). Guard against cp very close to R.
        //
        // PH-28 (2026-04-30): this ideal-gas reduction breaks for the
        // supercritical jacket-outlet conditions every flight expander
        // cycle actually runs at — H2 at 10 MPa / 80-200 K, CH4 at
        // 10 MPa / 400 K. (cp − R) can be 2× the ideal value in the
        // pseudocritical band, yielding γ errors of ±15-25 %. The
        // proper fix requires real-fluid γ from the coolant state
        // (REFPROP-class table), which would touch all 4 fluid
        // implementations + bench fingerprints. Until that upgrade
        // ships, this routine flags the supercritical regime in
        // `ExpanderTurbineResult.Notes` so consumers know to treat the
        // resulting EXPANDER_TURBINE_ENTHALPY_DEFICIT gate output as
        // approximate. Tracked as PH-28.
        double gamma = cp / System.Math.Max(cp - R_specific, 1.0);

        double pr = P_out / coolantOutletP_Pa;
        double w_isen = cp * coolantOutletT_K * (1.0 - System.Math.Pow(pr, (gamma - 1.0) / gamma));
        double w = efficiency * System.Math.Max(w_isen, 0.0);
        double P_avail = coolantMassFlow_kgs * w;
        bool   ok = P_avail >= requiredPumpShaftPower_W;

        // Sprint 34 / PH-26 (2026-04-25): stator-throat choke check.
        // Expander cycles often run π closer to 1 because the coolant
        // jacket only develops modest pressure drops; closed-expander
        // cycles in particular are at high risk of running unchoked
        // through their turbine. Same π_crit form as TurbineSizing.
        double piCrit = System.Math.Pow(
            2.0 / (gamma + 1.0), gamma / (gamma - 1.0));
        bool isChoked = pr <= piCrit;

        string notes = cycle == EngineCycle.OpenExpander
            ? $"Open-expander: {coolant.Metadata.DisplayName} at {coolantOutletT_K:F0} K / "
            + $"{coolantOutletP_Pa / 1e6:F1} MPa expands to ambient; turbine exhaust dumped overboard."
            : $"Closed-expander: {coolant.Metadata.DisplayName} at {coolantOutletT_K:F0} K / "
            + $"{coolantOutletP_Pa / 1e6:F1} MPa expands to {P_out / 1e6:F2} MPa; "
            + "turbine exhaust feeds main chamber.";

        // PH-28 disclosure: append a γ-uncertainty note when the
        // jacket-outlet state is supercritical (T ≥ Tc OR P ≥ Pc).
        // Real flight expander cycles essentially always run
        // supercritical because that's where the cp boost lives, so
        // this fires on every realistic design — it's a yellow-flag
        // disclosure on the underlying ideal-gas γ approximation, not
        // a feasibility-blocking signal.
        bool isSupercritical = coolantOutletT_K >= coolant.Metadata.CriticalT_K
                            || coolantOutletP_Pa >= coolant.Metadata.CriticalP_Pa;
        if (isSupercritical)
        {
            notes += $" [PH-28 disclosure: γ={gamma:F3} computed from ideal-gas (cp − R); "
                   + $"{coolant.Metadata.DisplayName} at T={coolantOutletT_K:F0} K / "
                   + $"P={coolantOutletP_Pa / 1e6:F1} MPa is supercritical "
                   + $"(Tc={coolant.Metadata.CriticalT_K:F0} K, Pc={coolant.Metadata.CriticalP_Pa / 1e6:F2} MPa) — "
                   + "γ uncertainty ±15-25 %; EXPANDER_TURBINE_ENTHALPY_DEFICIT margin inherits this. "
                   + "REFPROP-class real-fluid γ upgrade tracked.]";
        }

        // Sprint 34d / PH-9 (2026-04-25): power-balance consistency margin.
        // Under voxelforge's pinned-ṁ_c model this is diagnostic only —
        // negative values mirror the !PowerSufficient gate firing.
        double massFlowMargin = requiredPumpShaftPower_W > 0
            ? (P_avail / requiredPumpShaftPower_W) - 1.0
            : 0.0;

        return new ExpanderTurbineResult(
            Cycle: cycle,
            CoolantLabel: coolant.Metadata.Key,
            InletTemperature_K: coolantOutletT_K,
            InletPressure_Pa: coolantOutletP_Pa,
            OutletPressure_Pa: P_out,
            MassFlow_kgs: coolantMassFlow_kgs,
            Cp_Jkg_K: cp,
            EffectiveGamma: gamma,
            IsentropicSpecificWork_Jkg: w_isen,
            ActualSpecificWork_Jkg: w,
            Efficiency: efficiency,
            AvailableShaftPower_W: P_avail,
            RequiredShaftPower_W: requiredPumpShaftPower_W,
            PowerSufficient: ok,
            Notes: notes,
            CriticalPressureRatio: piCrit,
            IsChoked: isChoked,
            MassFlowMargin: massFlowMargin);
    }
}
