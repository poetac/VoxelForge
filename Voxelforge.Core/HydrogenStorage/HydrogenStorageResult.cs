// HydrogenStorageResult.cs — Sprint H2T.W1 solver output.

namespace Voxelforge.HydrogenStorage;

/// <summary>
/// Solve-time outputs for a hydrogen storage tank snapshot at the
/// design (P, T, V) operating point (Sprint H2T.W1 scaffold).
/// </summary>
/// <param name="HydrogenDensity_kgm3">ρ_H₂ at the operating point [kg/m³]. Real-gas
/// for CompressedGas (Z correction); liquid density for LiquidCryogenic.</param>
/// <param name="StoredHydrogenMass_kg">m_H₂ = ρ_H₂ · V [kg].</param>
/// <param name="StoredHydrogenEnergy_kWh">m_H₂ · LHV_H₂ [kWh] using H₂
/// LHV = 33.3 kWh/kg.</param>
/// <param name="GravimetricEfficiency">m_H₂ / (m_H₂ + m_dry) [-]. DOE
/// 2025 target ≥ 6.5 %; current Type-IV systems ~ 5-6 %.</param>
/// <param name="VolumetricEnergyDensity_kWh_L">Stored energy / tank
/// volume [kWh / L] — figure of merit for vehicle packaging.</param>
/// <param name="BoilOffRate_kgs">Continuous boil-off rate from heat-leak [kg/s].
/// Zero for compressed-gas; positive for cryogenic. Q_leak / h_fg(LH₂).</param>
internal sealed record HydrogenStorageResult(
    double HydrogenDensity_kgm3,
    double StoredHydrogenMass_kg,
    double StoredHydrogenEnergy_kWh,
    double GravimetricEfficiency,
    double VolumetricEnergyDensity_kWh_L,
    double BoilOffRate_kgs);
