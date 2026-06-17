// ChilldownTransient.cs — Pre-fire chilldown transient for the regen jacket.
//
// First transient solver in the toolchain. Answers the
// question: "with the engine soaking at ambient temperature,
// how long does it take to chill the regen jacket down to
// near-saturation temperature once cryogenic propellant starts
// flowing — and how much propellant gets boiled off in the
// process before steady-state regen cooling begins?"
//
// Model: lumped jacket mass + Chen-style effective two-phase
// HTC.
//
//   Energy balance:
//     m_w · cp_w · dT_w/dt = -h · A · (T_w − T_sat)
//
//   First-order solution (constant h, A, T_sat):
//     T_w(t) = T_sat + (T_w0 − T_sat) · exp(-t / τ)
//     τ = m_w · cp_w / (h · A)
//
//   Chilldown "complete" when (T_w − T_sat) drops below
//   <see cref="ChilldownInputs.DoneDeltaT_K"/>:
//     t_done = -τ · ln(ΔT_done / (T_w0 − T_sat))
//
//   Propellant boiled off during chilldown (worst-case — every
//   kg of inlet propellant fully vaporised against the warm
//   wall):
//     m_propellant ≈ ṁ_coolant · t_done
//
// Thermal-shock peak: assume the radial wall ΔT is bounded by
// the gas-side surface chilling at h · ΔT vs the back side
// staying near initial T. The max thermal-strain stress is
//     σ_shock ≈ E · α · (T_w0 − T_sat) / 2
// — half of the full ΔT because the gradient develops
// progressively across the wall (Timoshenko §13).
//
// MVP simplifications:
//   • Single Chen-style effective HTC (5000 W/m²K typical for
//     boiling LCH4 / LH2 against room-T metal; user-tunable).
//     Real Chen / Shah formulations evolve h through the
//     film → transition → nucleate boiling regimes; the
//     constant-h envelope here is conservative on time and
//     optimistic on propellant burned (good enough for
//     budgeting).
//   • Saturation temperature taken as a per-fluid constant at
//     the inlet pressure (not pressure-dependent through the
//     transient). Acceptable because Δp is small in the
//     few-second chilldown window.
//   • Propellant accounting is hot-side only: every kg of
//     inlet propellant during the chilldown window is treated
//     as "consumed" (worst case). A more refined tally would
//     subtract the regen-jacket-warmed two-phase mixture that
//     leaves the jacket — for an MVP that goes too deep.
//
// References:
//   Chen, J.C. "A Correlation for Boiling Heat Transfer to
//     Saturated Fluids in Convective Flow" I&EC Process Design
//     and Development 5(3), 1966.
//   Shah, M.M. "A New Correlation for Heat Transfer During
//     Boiling Flow Through Pipes" ASHRAE Trans. 82(2), 1976.
//   NASA SP-194 §6 (Liquid Rocket Engine Combustion Stability,
//     start-transient cryogenic chilldown discussion).

namespace Voxelforge.HeatTransfer;

/// <summary>
/// Inputs to the lumped-jacket chilldown integrator. All units
/// are SI; <c>WallMass_kg</c> is the metal-only jacket mass
/// participating in the cool-down (ribs + outer shell + inner
/// liner — not the contained coolant).
/// </summary>
public sealed record ChilldownInputs(
    double WallMass_kg,
    double WallArea_m2,
    double WallSpecificHeat_Jkg,
    double InitialWallTemp_K,
    double CoolantSaturationTemp_K,
    double CoolantMassFlow_kgs,
    double TwoPhaseHTC_Wm2K,
    double DoneDeltaT_K,
    double WallElasticModulus_Pa,
    double WallCTE_perK,
    double MaxTime_s);

/// <summary>
/// Output of <see cref="ChilldownTransient.Run"/>.
/// </summary>
public sealed record ChilldownResult(
    double TimeToChill_s,
    double TimeConstant_s,
    double PropellantMassConsumed_kg,
    double PeakThermalShockStress_MPa,
    bool   ChilldownComplete,
    bool   IsAcceptable,
    string Regime,
    string[] Warnings);

public static class ChilldownTransient
{
    /// <summary>
    /// Approximate saturation temperature (K) at chilldown
    /// pressure for the named regen-coolant fluid. Pressures
    /// during chilldown sit around the inlet ullage; using a
    /// single representative T_sat (1 MPa) is good to ±10 % on
    /// timing.
    /// </summary>
    public static double SaturationTemperature_K(string coolantFluidKey)
        => coolantFluidKey switch
        {
            "CH4"  => 150.0,    // LCH4 @ ~1 MPa
            "H2"   => 28.0,     // LH2  @ ~1 MPa
            "RP-1" => 489.0,    // RP-1 boiling point — chilldown not relevant; used as a sentinel
            _      => 200.0,    // generic fallback
        };

    /// <summary>
    /// True when chilldown is meaningful for the named fluid —
    /// non-cryogenic propellants (RP-1, hypergolics) skip the
    /// transient entirely.
    /// </summary>
    public static bool IsCryogenic(string coolantFluidKey)
        => coolantFluidKey is "CH4" or "H2";

    public static ChilldownResult Run(ChilldownInputs inp)
    {
        var warnings = new System.Collections.Generic.List<string>();

        if (inp.WallMass_kg <= 0 || inp.WallArea_m2 <= 0
         || inp.WallSpecificHeat_Jkg <= 0
         || inp.TwoPhaseHTC_Wm2K <= 0)
        {
            return new ChilldownResult(
                TimeToChill_s:                0,
                TimeConstant_s:               0,
                PropellantMassConsumed_kg:    0,
                PeakThermalShockStress_MPa:   0,
                ChilldownComplete:            true,
                IsAcceptable:                 true,
                Regime:                       "Skipped (degenerate inputs).",
                Warnings:                     new[] { "Chilldown skipped: degenerate inputs (zero mass, area, cp or h)." });
        }

        double dT0 = inp.InitialWallTemp_K - inp.CoolantSaturationTemp_K;
        if (dT0 <= 0)
        {
            return new ChilldownResult(
                TimeToChill_s:                0,
                TimeConstant_s:               0,
                PropellantMassConsumed_kg:    0,
                PeakThermalShockStress_MPa:   0,
                ChilldownComplete:            true,
                IsAcceptable:                 true,
                Regime:                       "Already chilled.",
                Warnings:                     System.Array.Empty<string>());
        }

        // First-order time constant.
        double tau = (inp.WallMass_kg * inp.WallSpecificHeat_Jkg)
                   / (inp.TwoPhaseHTC_Wm2K * inp.WallArea_m2);

        // Time to drop within DoneDeltaT_K of saturation.
        double dTdone = System.Math.Max(inp.DoneDeltaT_K, 0.1);
        double tChill;
        bool complete;
        if (dTdone >= dT0)
        {
            tChill = 0;
            complete = true;
        }
        else
        {
            tChill = -tau * System.Math.Log(dTdone / dT0);
            complete = true;
        }

        // Propellant burned during chilldown (worst case = all flow
        // vaporised). Capped at the user's MaxTime_s so a runaway
        // correlation doesn't report a meaningless 10⁶-kg figure.
        double tBilled = System.Math.Min(tChill, System.Math.Max(inp.MaxTime_s, 0));
        double mProp = inp.CoolantMassFlow_kgs * tBilled;

        // Thermal-shock stress estimate — half of the full ΔT
        // because the gradient develops progressively across the
        // wall thickness.
        double shockMPa =
            inp.WallElasticModulus_Pa * inp.WallCTE_perK * dT0 * 0.5 / 1e6;

        bool acceptable = complete && tChill <= inp.MaxTime_s;
        if (!acceptable && complete)
            warnings.Add($"Chilldown time {tChill:F1} s exceeds budget {inp.MaxTime_s:F1} s — "
                       + "increase coolant flow, raise inlet pressure, or pre-cool the jacket.");

        if (shockMPa > 200)
            warnings.Add($"Thermal-shock stress {shockMPa:F0} MPa is large — verify the wall material's "
                       + "low-cycle-fatigue allowable at the working ΔT.");

        string regime = inp.TwoPhaseHTC_Wm2K < 2_000
            ? "Film-boiling (low h)"
            : inp.TwoPhaseHTC_Wm2K < 8_000
                ? "Transition / nucleate-boiling envelope"
                : "Nucleate-boiling / single-phase liquid";

        return new ChilldownResult(
            TimeToChill_s:                tChill,
            TimeConstant_s:               tau,
            PropellantMassConsumed_kg:    mProp,
            PeakThermalShockStress_MPa:   shockMPa,
            ChilldownComplete:            complete,
            IsAcceptable:                 acceptable,
            Regime:                       regime,
            Warnings:                     warnings.ToArray());
    }
}
