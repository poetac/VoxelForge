// PlateFinResult.cs — Sprint HX.W1 solver output.

namespace Voxelforge.HeatExchanger;

/// <summary>
/// Solve-time outputs for a counterflow plate-fin heat exchanger
/// (Sprint HX.W1 scaffold).
/// </summary>
/// <param name="CapacityRateMin_WK">C_min = min(ṁ_h·cp_h, ṁ_c·cp_c) [W/K].</param>
/// <param name="CapacityRateRatio">C_r = C_min / C_max [-].</param>
/// <param name="NumberOfTransferUnits">NTU = UA / C_min [-].</param>
/// <param name="Effectiveness">ε [-] from the counterflow ε(NTU, C_r) closed-form.</param>
/// <param name="OverallHeatTransferCoefficient_W_m2K">U = (1/h_hot + 1/h_cold)^-1 [W/(m²·K)].</param>
/// <param name="HotSideHTC_W_m2K">h_hot [W/(m²·K)] from Kays-London j-factor.</param>
/// <param name="ColdSideHTC_W_m2K">h_cold [W/(m²·K)] from Kays-London j-factor.</param>
/// <param name="HeatDuty_W">Q = ε · C_min · (T_hot_in − T_cold_in) [W].</param>
/// <param name="HotOutletTemperature_K">T_hot_out = T_hot_in − Q/C_hot [K].</param>
/// <param name="ColdOutletTemperature_K">T_cold_out = T_cold_in + Q/C_cold [K].</param>
/// <param name="HotPressureDrop_Pa">ΔP_hot [Pa] from Kays-London f-factor.</param>
/// <param name="ColdPressureDrop_Pa">ΔP_cold [Pa].</param>
/// <param name="HotReynolds">Re_hot [-] in the hot-side channel.</param>
/// <param name="ColdReynolds">Re_cold [-] in the cold-side channel.</param>
/// <param name="HotFinEfficiency">
/// Sprint HX.W2. η_fin_hot [-]. 1.0 when fin-efficiency correction is
/// disabled (HX.W1 bit-identical default). Otherwise = tanh(mL)/(mL).
/// </param>
/// <param name="ColdFinEfficiency">
/// Sprint HX.W2. η_fin_cold [-]. Same default + formula.
/// </param>
internal sealed record PlateFinResult(
    double CapacityRateMin_WK,
    double CapacityRateRatio,
    double NumberOfTransferUnits,
    double Effectiveness,
    double OverallHeatTransferCoefficient_W_m2K,
    double HotSideHTC_W_m2K,
    double ColdSideHTC_W_m2K,
    double HeatDuty_W,
    double HotOutletTemperature_K,
    double ColdOutletTemperature_K,
    double HotPressureDrop_Pa,
    double ColdPressureDrop_Pa,
    double HotReynolds,
    double ColdReynolds,
    double HotFinEfficiency  = 1.0,
    double ColdFinEfficiency = 1.0);
