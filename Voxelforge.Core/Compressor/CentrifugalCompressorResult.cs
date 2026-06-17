// CentrifugalCompressorResult.cs — Sprint CMP.W1 solver output.

namespace Voxelforge.Compressor;

/// <summary>
/// Solve-time outputs for a centrifugal compressor stage snapshot
/// (Sprint CMP.W1 scaffold).
/// </summary>
/// <param name="IsentropicExitTemperature_K">T_t2_is [K] = T_t1 ·
/// π^((γ−1)/γ).</param>
/// <param name="ActualExitTemperature_K">T_t2 [K] = T_t1 + ΔT_is / η.</param>
/// <param name="ExitTotalPressure_Pa">P_t2 [Pa] = π · P_t1.</param>
/// <param name="IsentropicTemperatureRise_K">ΔT_is [K].</param>
/// <param name="ActualTemperatureRise_K">ΔT_actual = ΔT_is / η [K].</param>
/// <param name="SpecificWork_J_kg">w = cp · ΔT_actual [J/kg].</param>
/// <param name="ShaftPowerInput_W">P_shaft = ṁ · w [W]. Always positive
/// (compressor consumes work).</param>
/// <param name="DensityRatio">ρ_2 / ρ_1 [-] using ideal-gas relation.</param>
internal sealed record CentrifugalCompressorResult(
    double IsentropicExitTemperature_K,
    double ActualExitTemperature_K,
    double ExitTotalPressure_Pa,
    double IsentropicTemperatureRise_K,
    double ActualTemperatureRise_K,
    double SpecificWork_J_kg,
    double ShaftPowerInput_W,
    double DensityRatio);
