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

        // F(B) = B / (exp(B) - 1) is the Stanton-number REDUCTION ratio St/St0:
        // it tends to 1 as B -> 0 (no blowing, no reduction) and to 0 as B -> inf
        // (strong blowing chokes off gas-side heat transfer). The transpiration
        // TEMPERATURE effectiveness is its COMPLEMENT eta = 1 - F(B): zero cooling
        // at no bleed, approaching full cooling at heavy bleed. (Applying F(B)
        // itself as the effectiveness inverts the physics -- more coolant would
        // give less cooling.) Guard against B ~ 0 where exp(B) - 1 ~ B, F(B) -> 1.
        double stRatio = Math.Abs(B) < 1e-9 ? 1.0 : B / (Math.Exp(B) - 1.0);
        double effectiveness = 1.0 - stRatio;

        return T_aw_K - efficiency * (T_aw_K - T_coolantInlet_K) * effectiveness;
    }
}
