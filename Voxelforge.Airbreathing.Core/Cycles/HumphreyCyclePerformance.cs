// HumphreyCyclePerformance.cs — pulsejet constant-volume combustion +
// peak-pressure helpers (Wave 1 PR-4, sub-step 1a.5).
//
// Distinct from ramjet/turbojet's constant-pressure Brayton: a pulsejet
// burns at near-constant volume (Humphrey cycle approximation, Foa 1960
// §11.4), raising both T and P during the combustion phase before the
// charge accelerates out the tailpipe. This helper supplies:
//
//   • CombustorExitT_K(...)      — energy-balance T_t4 from T_t2 + f·LHV
//                                  (same shape as ramjet, parametrically
//                                  swappable; included here for
//                                  cycle-completeness)
//   • PeakChamberPressureRatio(.) — P_peak / P_steady estimate driving the
//                                  PULSEJET_ACOUSTIC_OVERPRESSURE advisory
//                                  gate
//
// References:
//   Foa, J.V. 1960 Elements of Flight Propulsion, Wiley §11.4
//   NACA RM E50A04 (Cleveland-instrumented V-1 buzz-bomb static-thrust tests)
//   Glassman, I. 1996 Combustion §3 (lower flammability limits)

using System;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Closed-form Humphrey-cycle helpers for valveless pulsejet performance.
/// Static; no state.
/// </summary>
public static class HumphreyCyclePerformance
{
    /// <summary>
    /// Constant-property gas <c>cp</c> for cold-side air (J/(kg·K)).
    /// Same value <see cref="RamjetCycleSolver"/> uses for the H₂-fuel
    /// constant-cp branch.
    /// </summary>
    public const double CpAir_JkgK = 1004.7;

    /// <summary>
    /// Combustion efficiency η_b for the deflagration phase. Same value as
    /// the ramjet cycle (Foa §5.3 — modern combustors cluster around 0.99).
    /// Pulsejet η_b is slightly lower in reality due to incomplete burn
    /// during the rapid blow-down half-cycle, but the steady-state
    /// thermodynamic balance preserves this approximation for first-order
    /// analysis.
    /// </summary>
    public const double CombustionEfficiency = 0.99;

    /// <summary>
    /// Constant-volume combustion exit-temperature energy balance:
    /// <c>T_t4 = T_t2 + (η_b · f · LHV) / cp</c>
    /// where f is the actual fuel-air mass fraction and LHV is the fuel's
    /// lower heating value (J/kg). Returns <c>T_in_K</c> unchanged when
    /// f ≤ 0 or LHV ≤ 0.
    /// </summary>
    public static double CombustorExitT_K(double T_in_K, double fuelAirMassFraction, double LHV_Jkg)
    {
        if (T_in_K <= 0.0 || double.IsNaN(T_in_K)) return double.NaN;
        if (fuelAirMassFraction <= 0.0 || LHV_Jkg <= 0.0) return T_in_K;
        return T_in_K + (CombustionEfficiency * fuelAirMassFraction * LHV_Jkg) / CpAir_JkgK;
    }

    /// <summary>
    /// Engineering estimate of peak-to-steady chamber pressure ratio
    /// <c>P_peak / P_steady</c> for the cyclic Humphrey combustion phase.
    /// Drives the <c>PULSEJET_ACOUSTIC_OVERPRESSURE</c> advisory gate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ideal constant-volume combustion gives <c>P_peak / P_initial = T_t4 / T_t2</c>
    /// (ideal-gas law at constant V). Real valveless pulsejets vent through
    /// the tailpipe during combustion, so the measured peak is much smaller
    /// than the ideal prediction. NACA RM E50A04 V-1 instrumented data
    /// shows peak-to-steady excursions in the 1.05–1.30× range during
    /// stable buzz operation; transitions to mode-jump or detonation begin
    /// above ~1.30×.
    /// </para>
    /// <para>
    /// The closed-form fit used here:
    /// <c>P_peak / P_steady ≈ 1 + 0.05 · max(0, T_t4/T_t2 − 1)</c>
    /// — gives ~1.22 at V-1 nominal (<c>T_ratio ≈ 5.4</c>), well below the
    /// 1.30× advisory threshold; rises to 1.30× near <c>T_ratio = 7</c>;
    /// fires the gate above. The 0.05 coefficient is calibrated to
    /// NACA RM E50A04 fig 4 V-1 data; documented as engineering-grade
    /// per Foa §11.4 (the underlying cycle is highly non-linear and a
    /// closed-form fit is approximate).
    /// </para>
    /// </remarks>
    public static double PeakChamberPressureRatio(double T_in_K, double T_out_K)
    {
        if (T_in_K <= 0.0 || double.IsNaN(T_in_K) || double.IsNaN(T_out_K))
            return double.NaN;
        double tempRatio = T_out_K / T_in_K;
        return 1.0 + 0.05 * Math.Max(0.0, tempRatio - 1.0);
    }
}
