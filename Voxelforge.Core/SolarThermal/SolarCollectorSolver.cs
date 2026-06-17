// SolarCollectorSolver.cs — Sprint ST.W1 closed-form solar-thermal
// collector performance snapshot.
//
// Stateless, allocation-free, deterministic. Implements the canonical
// Hottel-Whillier-Bliss closed-form fit:
//
//   Q_useful = F_R · A · [(τα) · G − U_L · (T_collector − T_ambient)]
//   η        = Q_useful / (G · A)
//             = F_R · (τα) − F_R · U_L · (T_collector − T_ambient) / G
//
// The linear-in-(ΔT/G) form is the standard engineering fit used in
// ASHRAE 93 + ISO 9806 collector test reports. F_R, τα, and U_L are
// pulled from the per-kind registry.
//
// Q_useful is clamped at 0 (a collector running hotter than its
// irradiance can sustain produces no useful heat; the absorber-side
// of the loop simply stops contributing — the loss component
// continues but cannot drive negative Q_useful by convention).
//
// References:
//   Duffie J.A., Beckman W.A. (2013). "Solar Engineering of Thermal
//     Processes," 4th ed., chap 6 (Hottel-Whillier-Bliss model).
//   ASHRAE 93-2010. "Methods of Testing to Determine the Thermal
//     Performance of Solar Collectors."
//   ISO 9806:2017. "Solar energy — Solar thermal collectors — Test
//     methods."
//   Solar Millennium AG (2008). "Andasol Power Plants" technical
//     description.

using System;

namespace Voxelforge.SolarThermal;

/// <summary>
/// Closed-form solar-thermal collector performance snapshot solver
/// (Sprint ST.W1).
/// </summary>
internal static class SolarCollectorSolver
{
    /// <summary>
    /// Solve the solar-thermal collector performance snapshot at the
    /// design operating point.
    /// </summary>
    internal static SolarCollectorResult Solve(SolarCollectorDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var props = SolarCollectorRegistry.For(design.Kind);

        double deltaT_K = design.CollectorTemperature_C - design.AmbientTemperature_C;

        // 1. Energy-flux components.
        double G_times_A = design.DirectNormalIrradiance_W_m2 * design.ApertureArea_m2;
        double Q_incident = G_times_A;
        double Q_absorbed = props.TransmittanceAbsorptanceProduct * G_times_A;
        double Q_loss     = props.OverallLossCoefficient_W_m2K
                          * design.ApertureArea_m2 * deltaT_K;

        // 2. Hottel-Whillier-Bliss useful heat. Clamp at zero — a
        //    collector running hotter than its current irradiance can
        //    sustain produces no useful heat.
        double Q_useful_raw = props.HeatRemovalFactor * (Q_absorbed - Q_loss);
        double Q_useful = Math.Max(0.0, Q_useful_raw);

        // 3. Efficiency.
        double efficiency = Q_incident > 0 ? Q_useful / Q_incident : 0.0;

        // 4. Envelope check on operating temperature.
        bool inEnvelope = design.CollectorTemperature_C >= design.AmbientTemperature_C
                       && design.CollectorTemperature_C <= props.MaxOperatingTemperature_C;

        return new SolarCollectorResult(
            IncidentSolarPower_W:                Q_incident,
            AbsorbedSolarPower_W:                Q_absorbed,
            ThermalLossPower_W:                  Q_loss,
            UsefulHeatPower_W:                   Q_useful,
            CollectorEfficiency:                 efficiency,
            OperatingTemperatureInValidEnvelope: inEnvelope);
    }

    /// <summary>
    /// Compute the stagnation temperature [°C] — the T_collector at
    /// which Q_useful drops to zero. Public-static helper for sizing
    /// studies. Solve T_collector from η = 0:
    ///   τα · G − U_L · (T_stag − T_ambient) = 0
    ///   → T_stag = T_ambient + (τα · G) / U_L
    /// </summary>
    /// <param name="kind">Collector kind (drives τα + U_L).</param>
    /// <param name="irradiance_W_m2">G [W/m²].</param>
    /// <param name="ambientTemperature_C">T_ambient [°C].</param>
    /// <returns>Stagnation temperature [°C].</returns>
    internal static double ComputeStagnationTemperature(
        SolarCollectorKind kind,
        double irradiance_W_m2,
        double ambientTemperature_C)
    {
        if (irradiance_W_m2 < 0)
            throw new ArgumentOutOfRangeException(nameof(irradiance_W_m2),
                "G must be ≥ 0.");
        var props = SolarCollectorRegistry.For(kind);
        return ambientTemperature_C
             + props.TransmittanceAbsorptanceProduct * irradiance_W_m2
                 / props.OverallLossCoefficient_W_m2K;
    }
}
