// SpacecraftRadiatorResult.cs — Sprint RAD.W1 solver output.

namespace Voxelforge.Radiator;

/// <summary>
/// Solve-time outputs for a spacecraft radiator snapshot at the design
/// (T_panel, T_sink, ε, α, G_solar) operating point (Sprint RAD.W1).
/// </summary>
/// <param name="GrossRadiatedHeat_W">Q_emitted = ε · σ · A · T_panel⁴ [W].</param>
/// <param name="SinkBackradiation_W">Q_back = ε · σ · A · T_sink⁴ [W] —
/// back-radiation from the sink absorbed by the panel.</param>
/// <param name="ParasiticSolarHeat_W">Q_solar_in = α · A · G_solar [W] —
/// parasitic solar heating that must be radiated alongside the design heat.</param>
/// <param name="NetHeatRejectionRate_W">Q_net = Q_emitted − Q_back −
/// Q_solar_in [W] — the actual heat-rejection capacity available.</param>
/// <param name="HeatRejectionDensity_W_m2">Q_net / A_panel [W/m²].</param>
/// <param name="AlphaOverEpsilonRatio">α/ε [-] — the canonical figure of
/// merit for radiator coatings (lower = better; OSR ≈ 0.10).</param>
internal sealed record SpacecraftRadiatorResult(
    double GrossRadiatedHeat_W,
    double SinkBackradiation_W,
    double ParasiticSolarHeat_W,
    double NetHeatRejectionRate_W,
    double HeatRejectionDensity_W_m2,
    double AlphaOverEpsilonRatio);
