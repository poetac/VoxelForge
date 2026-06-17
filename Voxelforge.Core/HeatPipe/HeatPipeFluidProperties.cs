// HeatPipeFluidProperties.cs — Sprint HP.W1 per-fluid property registry.

using System;

namespace Voxelforge.HeatPipe;

/// <summary>
/// Cluster-anchored heat-pipe-fluid properties (Sprint HP.W1).
/// </summary>
/// <param name="OperatingTempMin_K">Lower edge of the cluster validity envelope [K].</param>
/// <param name="OperatingTempMax_K">Upper edge [K].</param>
/// <param name="CapillaryLimitPerArea_W_m2">Q_max / A_cross [W/m²] — the
/// cluster-mid-band capillary heat-flux limit at the working temperature.
/// Drives the gate-worthy "drying-out" envelope on Q_max.</param>
/// <param name="EffectiveAxialConductivity_W_mK">k_eff [W/(m·K)] — much
/// higher than copper (k_Cu = 400) because the latent-heat transport
/// inside the wick + vapour core multiplies the apparent conductivity.</param>
/// <param name="SonicLimitPerArea_W_m2">Sprint HP.W2. q_sonic [W/m²] —
/// vapour-velocity sonic-choke limit. Dominant in the low-T startup
/// regime (water-Cu pipes during cold start; sodium-stainless during
/// initial-power-up). Typically 5-10× lower than the capillary limit.</param>
/// <param name="EntrainmentLimitPerArea_W_m2">Sprint HP.W2. q_entrain
/// [W/m²] — droplet-entrainment limit. Dominant for high-T metal fluids
/// at high vapour velocity (Na, Li). Typically 2-5× lower than capillary
/// for low-T fluids; 0.5-2× for high-T fluids.</param>
internal sealed record HeatPipeFluidPropertiesData(
    double OperatingTempMin_K,
    double OperatingTempMax_K,
    double CapillaryLimitPerArea_W_m2,
    double EffectiveAxialConductivity_W_mK,
    double SonicLimitPerArea_W_m2     = double.MaxValue,
    double EntrainmentLimitPerArea_W_m2 = double.MaxValue);

/// <summary>Static registry of per-fluid heat-pipe properties.</summary>
internal static class HeatPipeFluidRegistry
{
    /// <summary>Cu-water cluster.</summary>
    internal static readonly HeatPipeFluidPropertiesData Water =
        new(OperatingTempMin_K:                283.0,    // 10 °C
            OperatingTempMax_K:                473.0,    // 200 °C
            CapillaryLimitPerArea_W_m2:        1.0e7,    // ~ 10 MW/m² for 6 mm Cu-water
            EffectiveAxialConductivity_W_mK:   50_000.0,
            // Sprint HP.W2 — sonic limit at low T (cold start); entrainment
            // at high vapour velocity. Both ~ 10× higher than capillary
            // for Cu-water (capillary is the dominant constraint).
            SonicLimitPerArea_W_m2:            1.0e8,
            EntrainmentLimitPerArea_W_m2:      5.0e7);

    /// <summary>Na-stainless cluster.</summary>
    internal static readonly HeatPipeFluidPropertiesData Sodium =
        new(OperatingTempMin_K:                673.0,    // 400 °C
            OperatingTempMax_K:                1073.0,   // 800 °C
            CapillaryLimitPerArea_W_m2:        5.0e7,
            // k_eff = 180,000 W/(m·K) is the upper-mid of the Na-stainless
            // cluster (Faghri 2016 §5; NASA TP-3326 §4 reports 150,000–
            // 250,000 W/(m·K) at the 700 K operating point). The Wave-1
            // anchor of 100,000 was too pessimistic — corrected as part of
            // #548-C (the Demo_RTG_HeatPipe_Radiator_SpacecraftThermalLoop
            // test failed because ΔT at 4 kW over a 1 m × 25 mm pipe ran
            // 81 K vs the published-engine sodium HP cluster anchor < 50 K).
            EffectiveAxialConductivity_W_mK:   180_000.0,
            // Sodium: sonic limit is the cold-startup constraint;
            // entrainment is the high-power constraint.
            SonicLimitPerArea_W_m2:            2.0e7,    // dominant at startup
            EntrainmentLimitPerArea_W_m2:      8.0e7);

    /// <summary>Li-tungsten cluster.</summary>
    internal static readonly HeatPipeFluidPropertiesData Lithium =
        new(OperatingTempMin_K:                1273.0,   // 1000 °C
            OperatingTempMax_K:                1773.0,   // 1500 °C
            CapillaryLimitPerArea_W_m2:        2.0e8,
            EffectiveAxialConductivity_W_mK:   200_000.0,
            SonicLimitPerArea_W_m2:            5.0e7,
            EntrainmentLimitPerArea_W_m2:      3.0e8);

    /// <summary>Resolve per-fluid heat-pipe properties.</summary>
    internal static HeatPipeFluidPropertiesData For(HeatPipeFluid fluid) => fluid switch
    {
        HeatPipeFluid.Water   => Water,
        HeatPipeFluid.Sodium  => Sodium,
        HeatPipeFluid.Lithium => Lithium,
        _ => throw new ArgumentOutOfRangeException(nameof(fluid), fluid,
                $"Unknown HeatPipeFluid '{fluid}'."),
    };

    /// <summary>
    /// Sprint HP.W2. Auto-select the best-fit working fluid for a given
    /// operating temperature. Returns the cluster whose validity
    /// envelope contains T; defaults to Water if T sits below the
    /// Water-Cu floor (mismatched but the closest reasonable choice).
    /// </summary>
    /// <param name="temperature_K">Mean operating temperature [K].</param>
    /// <returns>Best-fit <see cref="HeatPipeFluid"/> for the temperature.</returns>
    internal static HeatPipeFluid SelectFluidForTemperature(double temperature_K)
    {
        if (temperature_K <= 0)
            throw new ArgumentOutOfRangeException(nameof(temperature_K),
                "T must be > 0.");
        if (temperature_K >= Lithium.OperatingTempMin_K) return HeatPipeFluid.Lithium;
        if (temperature_K >= Sodium.OperatingTempMin_K)  return HeatPipeFluid.Sodium;
        return HeatPipeFluid.Water;
    }
}
