namespace Voxelforge.HeatTransfer;

/// <summary>
/// Eckert-Livingood effusion model for transpiration cooling.
/// Reduces effective adiabatic wall temperature by bleeding coolant
/// through the porous LPBF wall into the boundary layer.
/// Reference: Sutton §4.3; Eckert &amp; Livingood (1954) NACA TN 3010.
/// </summary>
public static class TranspirationCooling
{
    /// <summary>
    /// Computes the effective adiabatic wall temperature after transpiration blowing.
    /// </summary>
    /// <param name="T_aw_K">Baseline adiabatic wall temperature [K] (post-film-cooling).</param>
    /// <param name="T_coolantInlet_K">Coolant inlet temperature [K].</param>
    /// <param name="h_gas_Wm2K">Gas-side convective HTC [W/(m²·K)].</param>
    /// <param name="bleedMassFluxPerArea_kgm2s">Per-area bleed mass flux [kg/(m²·s)] = (BleedFraction × totalCoolantFlow / nStations) / stationWallArea.</param>
    /// <param name="cpGas_JkgK">Gas-side specific heat at constant pressure [J/(kg·K)].</param>
    /// <param name="efficiency">Transpiration efficiency factor η_t (default 0.85, Sutton §4.3).</param>
    /// <returns>Effective adiabatic wall temperature after blowing [K].</returns>
    public static double ComputeEffectiveAdiabaticWallTemp(
        double T_aw_K,
        double T_coolantInlet_K,
        double h_gas_Wm2K,
        double bleedMassFluxPerArea_kgm2s,
        double cpGas_JkgK,
        double efficiency)
    {
        if (bleedMassFluxPerArea_kgm2s <= 0.0) return T_aw_K;

        // Spalding blowing parameter B = m_bleed_per_area * cp_gas / h_gas (dimensionless)
        double B = bleedMassFluxPerArea_kgm2s * cpGas_JkgK / Math.Max(h_gas_Wm2K, 1e-9);

        // Eckert-Livingood effectiveness function F(B) = B / (exp(B) - 1)
        // Guard against B ≈ 0 where exp(B) - 1 ≈ B, giving F(B) → 1
        double F_B = Math.Abs(B) < 1e-9 ? 1.0 : B / (Math.Exp(B) - 1.0);

        return T_aw_K - efficiency * (T_aw_K - T_coolantInlet_K) * F_B;
    }
}
