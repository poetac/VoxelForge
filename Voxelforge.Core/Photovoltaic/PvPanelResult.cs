// PvPanelResult.cs — Sprint PV.W1 solver output.

namespace Voxelforge.Photovoltaic;

/// <summary>
/// Solve-time outputs for a photovoltaic panel snapshot at the design
/// irradiance + cell temperature (Sprint PV.W1 scaffold).
/// </summary>
/// <param name="ShortCircuitCurrent_A">I_sc at the actual operating
/// (G, T) point [A] — scales with irradiance.</param>
/// <param name="OpenCircuitVoltage_V">V_oc at the operating point [V]
/// — drops with temperature.</param>
/// <param name="MaxPowerPointVoltage_V">V_mp at the panel terminals [V]
/// from the cluster fit (V_mp ≈ 0.85 · V_oc · CellsInSeries).</param>
/// <param name="MaxPowerPointCurrent_A">I_mp at the panel terminals [A]
/// from the cluster fit (I_mp ≈ 0.93 · I_sc).</param>
/// <param name="MaxPower_W">P_mp = V_mp · I_mp [W] — the design figure
/// of merit (MPP-tracker target).</param>
/// <param name="ConversionEfficiency">η = P_mp / (G · A_panel) [-].</param>
/// <param name="IncidentSolarPower_W">G · A_panel [W] — the available
/// solar input.</param>
internal sealed record PvPanelResult(
    double ShortCircuitCurrent_A,
    double OpenCircuitVoltage_V,
    double MaxPowerPointVoltage_V,
    double MaxPowerPointCurrent_A,
    double MaxPower_W,
    double ConversionEfficiency,
    double IncidentSolarPower_W);
