// SteamTurbineCycleSolver.cs — Rankine-cycle steam turbine.
//
// Stationary power-generation engine.
// Analytic steam-property approximations are used throughout to keep the
// solver self-contained (no external steam tables):
//
//   Saturation curve:  Antoine-style log₁₀(P/P0) = 5.526 − 2061.6/T
//                      (P0 = 101 325 Pa; T in K; accurate 373–600 K, ±0.5 %)
//   Latent heat:       Watson correlation Δh_vap(T) = 2.257e6·((647.3−T)/274.15)^0.38
//   Liquid enthalpy:   h_f = cp_water · (T − 273.15), cp_water = 4184 J/kg/K
//   Steam enthalpy:    h_g = h_f + Δh_vap
//   Superheated steam: h = h_g + cp_steam · ΔT, cp_steam = 2090 J/kg/K
//   Entropy (approx):  s_g(T) = 7.354 − 0.00737·(T−373.15)  [kJ/kg/K, linear anchor]
//
// Cycle states (per kg of working fluid):
//   State 1: Condenser exit (saturated liquid at P_cond)
//   State 2: Pump exit (compressed liquid — pressure rise, no temperature rise)
//   State 3: Boiler/superheater exit (superheated steam at P_boil + ΔT_superheat)
//   State 4: Turbine exit (isentropic expansion to P_cond, η_t = 0.85)
//
// Output mapping to standard station slots (proxy usage — not physical):
//   Station 3 = boiler exit (T_t = T3, P_t = P_boil_Pa)
//   Station 4 = turbine entry (T_t = T3, P_t = P_boil_Pa — same as boiler exit)
//   Station 9 = turbine exit (T_t = T4, P_t = P_cond_Pa)
//   ThrustNet_N = shaft power W_net [W] (proxy for output scaling)
//   SpecificImpulse_s = η_th × 1000 (dimensionless proxy, not physical Isp)

using System;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Stations;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Rankine-cycle steam turbine solver. Shaft power output is the primary
/// product; no jet exhaust. Design knobs:
/// <see cref="AirbreathingEngineDesign.SteamBoilerPressure_bar"/>,
/// <see cref="AirbreathingEngineDesign.SteamCondensePressure_bar"/>,
/// <see cref="AirbreathingEngineDesign.SteamSuperheatDeltaT_K"/>.
/// </summary>
public sealed class SteamTurbineCycleSolver : IAirbreathingCycleSolver
{
    /// <summary>Isentropic turbine efficiency η_t.</summary>
    public const double TurbineEfficiency = 0.85;

    /// <summary>Isentropic pump efficiency η_p (approximated as 1.0 — pump work is tiny).</summary>
    public const double PumpEfficiency = 1.0;

    /// <summary>Specific heat of liquid water [J/kg/K].</summary>
    public const double Cp_Water_J_kg_K = 4184.0;

    /// <summary>Specific heat of superheated steam [J/kg/K].</summary>
    public const double Cp_Steam_J_kg_K = 2090.0;

    /// <summary>Specific volume of saturated liquid water [m³/kg].</summary>
    public const double V_Liquid_m3_kg = 0.001;

    /// <summary>Water critical temperature [K].</summary>
    public const double T_Critical_K = 647.3;

    /// <summary>Antoine correlation reference pressure [Pa].</summary>
    private const double P_Ref_Pa = 101_325.0;

    /// <inheritdoc />
    public AirbreathingEngineKind Kind => AirbreathingEngineKind.SteamTurbine;

    /// <inheritdoc />
    public CycleSolveResult Solve(AirbreathingEngineDesign design, FlightConditions cond)
    {
        if (design is null)  throw new ArgumentNullException(nameof(design));
        if (cond is null)    throw new ArgumentNullException(nameof(cond));
        if (design.Kind != AirbreathingEngineKind.SteamTurbine)
            throw new ArgumentException(
                $"SteamTurbineCycleSolver invoked with design.Kind = {design.Kind}; expected SteamTurbine.",
                nameof(design));

        double P_boil_bar   = design.SteamBoilerPressure_bar;
        double P_cond_bar   = design.SteamCondensePressure_bar;
        double dT_superheat = design.SteamSuperheatDeltaT_K;

        // Clamp degenerate inputs to avoid NaN propagation.
        if (P_boil_bar <= 0) P_boil_bar = 1.0;
        if (P_cond_bar <= 0) P_cond_bar = 0.04;

        double P_boil_Pa = P_boil_bar * 1e5;
        double P_cond_Pa = P_cond_bar * 1e5;

        // --- Saturation temperatures ---
        double T_boil = T_sat(P_boil_Pa);  // boiler saturation T [K]
        double T_cond = T_sat(P_cond_Pa);  // condenser saturation T [K]

        // --- State 1: condenser exit — saturated liquid ---
        double h1 = H_liquid(T_cond);

        // --- State 2: pump exit — liquid at boiler pressure ---
        double h2 = h1 + V_Liquid_m3_kg * (P_boil_Pa - P_cond_Pa) / PumpEfficiency;

        // --- State 3: boiler/superheater exit ---
        double T3   = T_boil + dT_superheat;
        double h_g3 = H_vapor_sat(T_boil);
        double h3   = h_g3 + Cp_Steam_J_kg_K * dT_superheat;

        // Entropy at state 3 (for isentropic expansion).
        // s_g at saturation estimated from linear anchor at 373.15 K:
        //   s_g(T) ≈ 8.07 − 0.00615·(T − 373.15) kJ/kg/K
        // Superheat correction: +cp_steam · ln(T3/T_boil)
        double s_g_boil = S_vapor_sat(T_boil);
        double s3 = s_g_boil + Cp_Steam_J_kg_K * Math.Log(T3 / T_boil);

        // --- State 4s: isentropic turbine exit ---
        double s_f_cond   = S_liquid_sat(T_cond);
        double s_g_cond   = S_vapor_sat(T_cond);
        double s_fg_cond  = s_g_cond - s_f_cond;
        double x4s        = s_fg_cond > 0.0 ? Math.Clamp((s3 - s_f_cond) / s_fg_cond, 0.0, 1.0) : 1.0;
        double h4s        = H_liquid(T_cond) + x4s * Dh_vap(T_cond);

        // --- State 4: actual turbine exit (with isentropic η_t) ---
        double h4 = h3 - TurbineEfficiency * (h3 - h4s);

        // --- Cycle performance ---
        double W_turbine = h3 - h4;   // specific turbine work [J/kg]
        double W_pump    = h2 - h1;   // specific pump work [J/kg]
        double W_net     = W_turbine - W_pump;

        double Q_boiler  = h3 - h2;   // heat input [J/kg]
        double eta_th    = Q_boiler > 0.0 ? W_net / Q_boiler : 0.0;

        // Scale to a mass-flow proxy.  Use CombustorArea as a proxy for
        // the steam mass flow at unit density (no physical fluid tables).
        double mdot_proxy = design.CombustorArea_m2 * 1.0; // 1 kg/(m²·s) proxy
        double W_net_total = W_net * mdot_proxy;            // W (shaft power)

        // Station map.  Station 3 = boiler exit; station 9 = turbine exit.
        var stations = new StationState[TurbofanCycleSolver.StationArrayLength];
        for (int i = 0; i < stations.Length; i++)
            stations[i] = new StationState(double.NaN, double.NaN, 0.0, double.NaN);

        stations[3] = new StationState(T3,    P_boil_Pa, mdot_proxy, 0.1);
        stations[4] = new StationState(T3,    P_boil_Pa, mdot_proxy, 0.1);
        stations[9] = new StationState(T_cond, P_cond_Pa, mdot_proxy, 0.1);

        return new CycleSolveResult(
            Stations: new StationMap(
                Stations:          stations,
                ThrustNet_N:       W_net_total,
                SpecificImpulse_s: eta_th * 1000.0,
                FuelMassFlow_kg_s: 0.0),
            CompressorDiagnostics: null,
            TurbineDiagnostics:    null)
        {
            ThermalEfficiency = eta_th,
            ShaftPower_W      = W_net_total,
        };
    }

    // ── Steam property helpers ────────────────────────────────────────────

    /// <summary>
    /// Saturation temperature [K] from pressure [Pa].
    /// Antoine-style log₁₀(P/P0) = 5.526 − 2061.6/T → T = 2061.6/(5.526−log₁₀(P/P0)).
    /// Valid roughly 373–600 K.
    /// </summary>
    internal static double T_sat(double P_Pa)
    {
        double logRatio = Math.Log10(P_Pa / P_Ref_Pa);
        double denom = 5.526 - logRatio;
        return denom > 0.0 ? 2061.6 / denom : 373.15;
    }

    /// <summary>
    /// Latent heat of vaporisation [J/kg] via Watson correlation.
    /// Δh_vap(T) = 2.257e6 · ((647.3 − T) / 274.15)^0.38
    /// </summary>
    internal static double Dh_vap(double T_K)
    {
        double theta = (T_Critical_K - T_K) / 274.15;
        if (theta <= 0.0) return 0.0;
        return 2.257e6 * Math.Pow(theta, 0.38);
    }

    /// <summary>Saturated liquid enthalpy [J/kg], relative to 0 °C.</summary>
    internal static double H_liquid(double T_K)
        => Cp_Water_J_kg_K * (T_K - 273.15);

    /// <summary>Saturated vapour enthalpy [J/kg].</summary>
    internal static double H_vapor_sat(double T_K)
        => H_liquid(T_K) + Dh_vap(T_K);

    /// <summary>
    /// Saturated vapour entropy [J/kg/K].
    /// Linear anchor: s_g(373.15 K) ≈ 8070 J/kg/K (Clausius-Clapeyron calibrated).
    /// Slope from Clausius-Clapeyron: ds_g/dT ≈ −6.15 J/kg/K².
    /// </summary>
    internal static double S_vapor_sat(double T_K)
        => 8070.0 - 6.15 * (T_K - 373.15);

    /// <summary>
    /// Saturated liquid entropy [J/kg/K].
    /// s_f(T) = h_f(T) / T (liquid phase simplified approximation).
    /// </summary>
    internal static double S_liquid_sat(double T_K)
        => T_K > 0 ? H_liquid(T_K) / T_K : 0.0;
}
