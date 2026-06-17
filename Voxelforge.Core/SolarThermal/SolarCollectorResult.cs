// SolarCollectorResult.cs — Sprint ST.W1 solver output.

namespace Voxelforge.SolarThermal;

/// <summary>
/// Solve-time outputs for a solar-thermal collector snapshot at the
/// design (G, T_collector, T_ambient) operating point (Sprint ST.W1).
/// </summary>
/// <param name="IncidentSolarPower_W">G · A [W].</param>
/// <param name="AbsorbedSolarPower_W">τα · G · A [W] — power absorbed by the receiver.</param>
/// <param name="ThermalLossPower_W">U_L · A · (T_collector − T_ambient) [W].</param>
/// <param name="UsefulHeatPower_W">Q_useful = F_R · [τα·G − U_L·ΔT] · A [W]
/// (Hottel-Whillier-Bliss). Clamped at 0 (a collector running hotter than
/// its irradiance can sustain is net-losing heat; the absorber side of
/// the loop simply stops contributing).</param>
/// <param name="CollectorEfficiency">η = Q_useful / (G · A) [-]. The figure
/// of merit for sizing studies.</param>
/// <param name="OperatingTemperatureInValidEnvelope">Whether T_collector
/// sits inside the kind's per-kind validity band.</param>
internal sealed record SolarCollectorResult(
    double IncidentSolarPower_W,
    double AbsorbedSolarPower_W,
    double ThermalLossPower_W,
    double UsefulHeatPower_W,
    double CollectorEfficiency,
    bool   OperatingTemperatureInValidEnvelope);
