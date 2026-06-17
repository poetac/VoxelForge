// BartzHeatFlux.cs — Gas-side convective heat transfer via the Bartz
// correlation, extended with a Mayer-style boundary-layer acceleration
// correction and a combustor-barrel turbulence enhancement.
//
// Baseline — Bartz, D.R. (1957) "A Simple Equation for Rapid Estimation
// of Rocket Nozzle Convective Heat Transfer Coefficients," Jet Propulsion
// Vol. 27 No. 1 pp. 49–51:
//
//   h_g = 0.026/D_t^0.2 · (μ^0.2·C_p / Pr^0.6)_0 · (P_c/C*)^0.8
//       · (D_t/r_c)^0.1 · (A_t/A)^0.9 · σ
//
//   σ = [0.5·(T_wg/T_c)·(1 + (γ-1)/2·M²) + 0.5]^(-0.68)
//     · [1 + (γ-1)/2·M²]^(-0.12)
//
// Known bias of pure Bartz:
//   • Over-predicts h_g at the throat by 10–30 %.
//   • Under-predicts h_g in the combustor barrel by 20–40 %.
//   • Net heat load usually accurate to ±25 % vs measured fires.
//
// Corrections added in this module (both are OPTIONAL — defaults reduce
// exactly to classical Bartz so pre-existing call sites remain unchanged):
//
//   1. Boundary-layer acceleration / laminarisation (throat region)
//      ──────────────────────────────────────────────────────────────
//      At the throat, strong streamwise acceleration (favourable pressure
//      gradient) partially relaminarises the turbulent boundary layer,
//      suppressing heat transfer below the Bartz prediction. The
//      acceleration parameter
//
//         K = ν / U² · dU/dx               (dimensionless)
//
//      is the standard threshold. For K > ≈ 3·10⁻⁶ the BL begins to
//      relaminarise. We apply a Mayer-style Stanton-ratio correction
//
//         f_accel = exp( −C_accel · K )          with C_accel = 80 000
//
//      where C_accel is hand-tuned so f_accel cancels Bartz's own
//      ~20 % throat over-prediction (a self-referential calibration);
//      the relaminarisation trend is consistent with accelerating-flow
//      studies such as NASA TN-D-3328 (Back, Massier & Gier, 1965) but
//      the coefficient is NOT fitted to that report's tabulated data
//      (no TN-D-3328 fixture ships in this repo). This yields
//      f_accel ≈ 0.79 at
//      K = 3e-6 (matching the typical Bartz over-prediction at the
//      throat) and ≈ 0.45 at K = 1e-5 (strongly relaminarised).
//      Caller computes K from local flow state and passes it in; a
//      zero input disables the correction.
//
//   2. Combustor-barrel injector-mixing enhancement
//      ─────────────────────────────────────────────
//      In the barrel, injector-driven turbulence enhances heat transfer
//      above the pure-Bartz prediction. We apply a smooth amplification
//      that decays with axial distance from the injector:
//
//         f_mix = 1 + A_mix · exp(−x / L_mix)
//
//      with A_mix = 0.30 and L_mix = 2 · D_chamber. This adds 30 % at
//      x = 0 and decays to ~10 % by the converging section entrance,
//      reversing the barrel under-prediction observed by Bartz. Caller
//      supplies the nondimensional fraction x / L_mix via
//      `injectorMixingDecay`; a large value (≥ 3) disables the
//      enhancement.
//
// The two corrections are independent and multiply onto the Bartz h_g.
// Heat flux is still q'' = h_g · (T_aw − T_wall_gas).

using Voxelforge.Combustion;

namespace Voxelforge.HeatTransfer;

public static class BartzHeatFlux
{
    /// <summary>Mayer acceleration-correction coefficient. Hand-tuned so the
    /// correction cancels Bartz's own ~20 % throat over-prediction
    /// (self-referential); consistent with the trend in NASA TN-D-3328 but
    /// not fitted to its tabulated data.</summary>
    public const double MayerAccelerationCoefficient = 80_000.0;

    /// <summary>Barrel injector-mixing amplitude at x = 0.</summary>
    public const double BarrelMixingAmplitude = 0.30;

    /// <summary>
    /// Bartz gas-side heat transfer coefficient h_g [W/(m²·K)] with optional
    /// Mayer acceleration correction and injector-mixing enhancement.
    /// </summary>
    /// <param name="gas">Combustion gas state at chamber stagnation.</param>
    /// <param name="throatDiameter_m">Throat diameter D_t [m].</param>
    /// <param name="throatCurvature_m">Throat wall curvature radius r_c [m].</param>
    /// <param name="areaRatioToThroat">A_t / A_local (≤ 1 except at throat where = 1).</param>
    /// <param name="localMach">Local Mach number at this station.</param>
    /// <param name="wallTempGas_K">Gas-side wall temperature T_wg [K] (guess, then iterate).</param>
    /// <param name="scalingFactor">
    ///   Empirical multiplier to compensate for Bartz bias (typical 0.8–1.2).
    ///   Defaults to 1.0 = pure Bartz.
    /// </param>
    /// <param name="accelerationParameterK">
    ///   BL acceleration parameter K = ν·(dU/dx)/U². Zero disables the
    ///   acceleration correction (backward-compatible with the original
    ///   signature).
    /// </param>
    /// <param name="injectorMixingDecay">
    ///   Dimensionless x / L_mix for barrel mixing enhancement. 0 = at
    ///   injector (max enhancement); large values (≥ 3) effectively
    ///   disable. Negative values also disable.
    /// </param>
    public static double HeatTransferCoefficient(
        in PropellantState gas,
        double throatDiameter_m,
        double throatCurvature_m,
        double areaRatioToThroat,
        double localMach,
        double wallTempGas_K,
        double scalingFactor = 1.0,
        double accelerationParameterK = 0.0,
        double injectorMixingDecay = 1e9)
    {
        if (throatDiameter_m <= 0) return 0;
        if (throatCurvature_m <= 0) throatCurvature_m = 1.5 * (throatDiameter_m * 0.5);
        if (areaRatioToThroat > 1.0) areaRatioToThroat = 1.0;
        if (areaRatioToThroat <= 0) return 0;

        double M = Math.Max(localMach, 0.02);
        double Tc = gas.ChamberTemp_K;
        double Pc = gas.ChamberPressure_Pa;
        double Cp = gas.Cp_Jkg;
        double mu = gas.Viscosity_PaS;
        double Pr = gas.Prandtl;
        double g  = gas.Gamma;
        double Cstar = gas.CStar_ms;
        if (Cstar <= 0) return 0;

        // PH-44 (2026-04-29): Bartz σ wall-T floor lowered 400 K → 200 K to
        // unblock chilldown / start-transient correctness. Hot-fire item 4
        // (`ChilldownTransient.Run` + `StartTransientSim.Run` shipped
        // 2026-04-28) calls Bartz at T_wg < 400 K — cold-side cryogen
        // conditions where the original 400 K floor biased σ low (and
        // therefore h_g low), making the chilldown integrator predict
        // unrealistically gentle gas-side flux during the cold phase. The
        // Bartz σ form is well-defined at any positive T; the 400 K floor
        // was a steady-state-only safety against dividing by tiny T at
        // initialisation. 200 K covers cryogen wall states (LH2 ≈ 20 K,
        // LCH4 ≈ 112 K) without sacrificing the divide-by-zero guard.
        double Twg = Math.Max(wallTempGas_K, 200.0);
        double machTerm = 1.0 + 0.5 * (g - 1.0) * M * M;
        double sigma = 1.0 / (Math.Pow(0.5 * (Twg / Tc) * machTerm + 0.5, 0.68)
                            * Math.Pow(machTerm, 0.12));

        double term1 = 0.026 / Math.Pow(throatDiameter_m, 0.2);
        double term2 = (Math.Pow(mu, 0.2) * Cp) / Math.Pow(Pr, 0.6);
        double term3 = Math.Pow(Pc / Cstar, 0.8);
        double term4 = Math.Pow(throatDiameter_m / throatCurvature_m, 0.1);
        double term5 = Math.Pow(areaRatioToThroat, 0.9);

        double h_g = term1 * term2 * term3 * term4 * term5 * sigma * scalingFactor;

        // ── Mayer acceleration correction ───────────────────────
        double K = Math.Max(accelerationParameterK, 0.0);
        double f_accel = Math.Exp(-MayerAccelerationCoefficient * K);

        // ── Barrel injector-mixing enhancement ──────────────────
        double decay = injectorMixingDecay;
        double f_mix = decay >= 0
            ? 1.0 + BarrelMixingAmplitude * Math.Exp(-decay)
            : 1.0;

        return h_g * f_accel * f_mix;
    }

    /// <summary>
    /// Compute the boundary-layer acceleration parameter K = ν·(dU/dx)/U²
    /// from local flow state. Positive K = favourable gradient (accelerating
    /// flow); negative K = adverse. Only positive values affect h_g.
    /// </summary>
    public static double AccelerationParameter(
        in PropellantState gas, double localMach, double T_static_K,
        double velocityGradient_1ps)
    {
        double M = Math.Max(localMach, 0.02);
        double rho_static = gas.ChamberPressure_Pa
                          / (gas.SpecificGasConst * Math.Max(T_static_K, 1))
                          * Math.Pow(1.0 + 0.5 * (gas.Gamma - 1.0) * M * M,
                                     -1.0 / (gas.Gamma - 1.0));
        double nu = gas.Viscosity_PaS / Math.Max(rho_static, 1e-9);
        double U = M * Math.Sqrt(gas.Gamma * gas.SpecificGasConst * Math.Max(T_static_K, 1));
        if (U < 1.0) return 0;
        return nu * velocityGradient_1ps / (U * U);
    }

    /// <summary>
    /// Gas-side heat flux q" [W/m²] given h_g, adiabatic wall T, and gas-side wall T.
    /// </summary>
    public static double HeatFlux(double h_g, double T_aw_K, double T_wg_K)
        => h_g * (T_aw_K - T_wg_K);
}
