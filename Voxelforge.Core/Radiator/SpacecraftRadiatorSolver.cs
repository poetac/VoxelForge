// SpacecraftRadiatorSolver.cs — Sprint RAD.W1 closed-form spacecraft
// flat-panel radiator performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes the gross
// radiated heat, sink back-radiation, parasitic solar absorption, and
// net heat rejection for a flat-panel radiator at a specified
// (T_panel, T_sink, ε, α, G_solar) operating point.
//
//   Q_emitted    = ε · σ · A · T_panel⁴             [Stefan-Boltzmann]
//   Q_back       = ε · σ · A · T_sink⁴              [back-radiation absorbed by panel]
//   Q_solar_in   = α · A · G_solar                  [parasitic solar load]
//   Q_net        = Q_emitted − Q_back − Q_solar_in  [available rejection capacity]
//
// Convention: Q_net > 0 means the panel is rejecting heat to the
// environment (the usable side of the design space). Q_net < 0 means
// the panel is being net-heated by the sun + sink — typically a sign
// the design is sun-facing without an adequate α/ε coating.
//
// References:
//   Gilmore D.G. (2002). "Spacecraft Thermal Control Handbook," vol 1.
//   Howell J.R., Mengüç M.P., Siegel R. (2015). "Thermal Radiation
//     Heat Transfer," 6th ed.
//   ISS Active Thermal Control System — Park & Cole 2014, AIAA 2014-3414.

using System;

namespace Voxelforge.Radiator;

/// <summary>
/// Closed-form spacecraft flat-panel radiator solver (Sprint RAD.W1).
/// </summary>
internal static class SpacecraftRadiatorSolver
{
    /// <summary>Stefan-Boltzmann constant [W/(m²·K⁴)].</summary>
    internal const double StefanBoltzmann_W_m2K4 = 5.670374419e-8;

    /// <summary>
    /// Solve the radiator performance snapshot at the design operating
    /// point.
    /// </summary>
    /// <param name="design">Validated radiator design.</param>
    /// <returns>Solved performance snapshot.</returns>
    internal static SpacecraftRadiatorResult Solve(SpacecraftRadiatorDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        // Sprint RAD.W2 — TwoSidedDeployable radiates from both faces,
        // doubling the radiative area. Solar absorption stays on a
        // single (sun-facing) face — the caller controls G_solar.
        double radiativeAreaMultiplier = design.Kind == RadiatorKind.TwoSidedDeployable
            ? 2.0
            : 1.0;
        double effectiveRadiativeArea = design.PanelArea_m2 * radiativeAreaMultiplier;
        double sigma_eA  = StefanBoltzmann_W_m2K4 * design.Emissivity * effectiveRadiativeArea;
        double T_pan     = design.OperatingTemperature_K;
        double T_sink    = design.SinkTemperature_K;
        double T_pan4    = T_pan * T_pan * T_pan * T_pan;
        double T_sink4   = T_sink * T_sink * T_sink * T_sink;

        double Q_emitted    = sigma_eA * T_pan4;
        double Q_back       = sigma_eA * T_sink4;
        double Q_solar_in   = design.SolarAbsorptivity * design.PanelArea_m2
                            * design.IncidentSolarFlux_W_m2;
        double Q_net        = Q_emitted - Q_back - Q_solar_in;
        double Q_net_density = design.PanelArea_m2 > 0
            ? Q_net / design.PanelArea_m2
            : 0.0;
        double alphaOverEpsilon = design.SolarAbsorptivity / design.Emissivity;

        return new SpacecraftRadiatorResult(
            GrossRadiatedHeat_W:        Q_emitted,
            SinkBackradiation_W:        Q_back,
            ParasiticSolarHeat_W:       Q_solar_in,
            NetHeatRejectionRate_W:     Q_net,
            HeatRejectionDensity_W_m2:  Q_net_density,
            AlphaOverEpsilonRatio:      alphaOverEpsilon);
    }

    /// <summary>
    /// Solve for the radiator area required to reject a target heat load
    /// at given (T_panel, T_sink, ε, α, G_solar). Public-static helper
    /// for sizing studies.
    /// </summary>
    /// <param name="targetHeatRejection_W">Q_required [W].</param>
    /// <param name="operatingTemperature_K">T_panel [K].</param>
    /// <param name="sinkTemperature_K">T_sink [K].</param>
    /// <param name="emissivity">ε [-].</param>
    /// <param name="solarAbsorptivity">α [-].</param>
    /// <param name="incidentSolarFlux_W_m2">G_solar [W/m²].</param>
    /// <returns>Required area A [m²]. Throws if T_panel ≤ T_sink (or
    /// the parasitic solar load exceeds the radiative-balance capacity).</returns>
    internal static double SolveForRequiredArea(
        double targetHeatRejection_W,
        double operatingTemperature_K,
        double sinkTemperature_K,
        double emissivity,
        double solarAbsorptivity,
        double incidentSolarFlux_W_m2)
    {
        if (targetHeatRejection_W <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetHeatRejection_W),
                "targetHeatRejection_W must be > 0.");
        if (operatingTemperature_K <= sinkTemperature_K)
            throw new ArgumentOutOfRangeException(nameof(operatingTemperature_K),
                $"T_panel ({operatingTemperature_K:F1}) must exceed T_sink "
              + $"({sinkTemperature_K:F1}) for the radiator to reject heat.");
        if (emissivity <= 0 || emissivity > 1.0)
            throw new ArgumentOutOfRangeException(nameof(emissivity),
                "ε must be in (0, 1].");

        // Per-unit-area net rejection:
        //   q_net = ε·σ·(T_p⁴ − T_s⁴) − α·G_solar
        double T_p4 = operatingTemperature_K * operatingTemperature_K
                    * operatingTemperature_K * operatingTemperature_K;
        double T_s4 = sinkTemperature_K * sinkTemperature_K
                    * sinkTemperature_K * sinkTemperature_K;
        double q_per_m2 = emissivity * StefanBoltzmann_W_m2K4
                        * (T_p4 - T_s4)
                        - solarAbsorptivity * incidentSolarFlux_W_m2;

        if (q_per_m2 <= 0)
            throw new InvalidOperationException(
                $"Per-unit-area net heat rejection {q_per_m2:F1} W/m² is non-positive — "
              + "the parasitic solar load exceeds the radiative-balance capacity.");

        return targetHeatRejection_W / q_per_m2;
    }
}
