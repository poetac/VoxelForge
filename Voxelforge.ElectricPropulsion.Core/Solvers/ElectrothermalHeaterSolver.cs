// ElectrothermalHeaterSolver.cs — lumped 0-D energy balance for the
// resistojet heater chamber.
//
//   P_in = ṁ · cp · (T_chamber − T_inlet) + q_rad,heater
//
// Newton iteration on T_chamber given (P_in, ṁ, propellant inlet
// composition, geometry, emissivity). The heater coil temperature is
// approximated as T_chamber + ΔT_film via a Dittus-Boelter-style
// gas-side film coefficient.
//
// Citations:
//   Sutton/Biblarz "Rocket Propulsion Elements" 9e §16.5
//   NASA TM-2002-211314 §3 (resistojet thermal-balance regime)
//   Holman "Heat Transfer" 10e §6 (Dittus-Boelter convection)

using System;
using Voxelforge.ElectricPropulsion.Thermo;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Result of the electrothermal heater solve.
/// </summary>
/// <param name="ChamberTemperature_K">Bulk chamber gas temperature [K].</param>
/// <param name="HeaterCoilTemperature_K">Heater coil surface temperature [K] (≈ T_chamber + ΔT_film).</param>
/// <param name="ChamberWallTemperature_K">Chamber outer-wall temperature [K] (steady-state radiation balance).</param>
/// <param name="RadiationLoss_W">q_rad from chamber outer wall [W].</param>
/// <param name="Converged">True iff Newton converged within <see cref="ElectrothermalHeaterSolver.MaxIterations"/>.</param>
/// <param name="IterationsUsed">Newton iterations actually consumed.</param>
public sealed record ElectrothermalHeaterResult(
    double ChamberTemperature_K,
    double HeaterCoilTemperature_K,
    double ChamberWallTemperature_K,
    double RadiationLoss_W,
    bool   Converged,
    int    IterationsUsed);

/// <summary>
/// Lumped 0-D heater solver. See class file header for citations.
/// </summary>
public static class ElectrothermalHeaterSolver
{
    /// <summary>Newton convergence tolerance on T_chamber [K].</summary>
    public const double Tolerance_K = 0.5;

    /// <summary>Maximum Newton iterations before declaring no-convergence.</summary>
    public const int MaxIterations = 64;

    /// <summary>
    /// Initial-guess seed for T_chamber [K]. Picks a value mid-band so
    /// Newton converges from both sides regardless of input scale.
    /// </summary>
    public const double InitialGuess_K = 1500.0;

    /// <summary>
    /// Solve the lumped 0-D energy balance for steady-state chamber
    /// temperature.
    /// </summary>
    /// <param name="design">Resistojet design (geometry + heater material + emissivity).</param>
    /// <param name="conditions">Operating conditions (inlet composition + temperature).</param>
    /// <returns>
    /// Converged thermal state. If Newton fails to converge within
    /// <see cref="MaxIterations"/>, the result still returns the latest
    /// iterate with <see cref="ElectrothermalHeaterResult.Converged"/>=false.
    /// Callers should treat non-convergence as an infeasible candidate.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="design"/>'s HeaterPower_W or
    /// PropellantMassFlow_kgs is NaN or non-positive.
    /// </exception>
    public static ElectrothermalHeaterResult Solve(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(conditions);
        if (double.IsNaN(design.HeaterPower_W) || design.HeaterPower_W <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"HeaterPower_W must be positive; got P={design.HeaterPower_W:F1} W.");
        if (double.IsNaN(design.PropellantMassFlow_kgs) || design.PropellantMassFlow_kgs <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"PropellantMassFlow_kgs must be positive; got ṁ={design.PropellantMassFlow_kgs:E3} kg/s.");

        double P_in   = design.HeaterPower_W;
        double mDot   = design.PropellantMassFlow_kgs;
        double T_in   = conditions.InletTemperature_K;
        double L_m    = design.HeaterChamberLength_mm * 1e-3;
        double R_m    = design.HeaterChamberRadius_mm * 1e-3;
        double t_w_m  = design.ChamberWallThickness_mm * 1e-3;
        double R_outer_m = R_m + t_w_m;
        double A_chamber_outer_m2 = ComputeChamberOuterSurfaceArea(L_m, R_outer_m);
        double eps    = design.ChamberEmissivity;
        double T_amb  = conditions.AmbientPressure_Pa > 0
            ? 300.0  // Atmospheric T_∞ ground-test; vacuum-of-space otherwise.
            : RadiationLossSolver.T_CosmicBackground_K;

        // Newton iteration on residual:
        //   F(T_c) = P_in − ṁ · cp(T_c) · (T_c − T_in) − q_rad(T_wall(T_c))
        // For the lumped 0-D model, T_wall ≈ T_c (chamber wall in thermal
        // equilibrium with bulk gas). The heater-coil temperature is
        // T_c + ΔT_film where ΔT_film is computed via Dittus-Boelter as a
        // post-step.
        double T_c = InitialGuess_K;
        bool converged = false;
        int iter;

        for (iter = 0; iter < MaxIterations; iter++)
        {
            double cp   = PropellantTables.MixtureCp(conditions.InletComposition, T_c);
            double q_rad = RadiationLossSolver.ChamberWallRadiation_W(eps, A_chamber_outer_m2, T_c, T_amb);
            double F     = P_in - mDot * cp * (T_c - T_in) - q_rad;

            // Numerical derivative dF/dT_c via small perturbation.
            const double dT = 1.0;
            double cp2   = PropellantTables.MixtureCp(conditions.InletComposition, T_c + dT);
            double q2    = RadiationLossSolver.ChamberWallRadiation_W(eps, A_chamber_outer_m2, T_c + dT, T_amb);
            double F2    = P_in - mDot * cp2 * (T_c + dT - T_in) - q2;
            double dF_dT = (F2 - F) / dT;

            if (Math.Abs(dF_dT) < 1e-12)
            {
                // Degenerate; back off to a fixed half-step and try again.
                T_c -= 50.0;
                continue;
            }

            // Newton update: T_{n+1} = T_n - F(T_n) / F'(T_n).
            // (F < 0 ⇒ heat balance has more demand than supply at T_c,
            // so T_c should drop; dF/dT < 0 in this regime, so −F/dF > 0
            // and we step up — but for F > 0 we step down.)
            double step = -F / dF_dT;
            // Damp large steps to avoid Newton overshoot at high
            // temperature where radiation grows like T^4.
            const double maxStep = 200.0;
            if (step > maxStep)        step = maxStep;
            else if (step < -maxStep)  step = -maxStep;

            double T_c_new = T_c + step;
            // Clamp inside table range.
            if (T_c_new < PropellantTables.T_min_K) T_c_new = PropellantTables.T_min_K;
            if (T_c_new > PropellantTables.T_max_K) T_c_new = PropellantTables.T_max_K;

            if (Math.Abs(step) < Tolerance_K)
            {
                T_c = T_c_new;
                converged = true;
                iter++;
                break;
            }
            T_c = T_c_new;
        }

        // Post-step: heater-coil temperature via Dittus-Boelter ΔT_film
        // approximation. h_gas ≈ 0.023 · k/D · Re^0.8 · Pr^0.4. Returns
        // ΔT_film = q_in_to_gas / (h · A_coil), where q_in_to_gas =
        // P_in − q_rad,heater (radiative loss from the coil itself).
        // For the lumped model we approximate ΔT_film ≈ 200 K typical of
        // resistojet hardware (NASA TM-2002-211314 §3 calibration anchor).
        double T_heater = T_c + EstimateFilmDeltaT_K(design, conditions, T_c);

        // Chamber wall temperature: lumped equal to T_c (steady-state
        // radial conduction is fast vs the radiation timescale).
        double T_wall = T_c;
        double q_rad_final = RadiationLossSolver.ChamberWallRadiation_W(eps, A_chamber_outer_m2, T_wall, T_amb);

        return new ElectrothermalHeaterResult(
            ChamberTemperature_K:    T_c,
            HeaterCoilTemperature_K: T_heater,
            ChamberWallTemperature_K: T_wall,
            RadiationLoss_W:         q_rad_final,
            Converged:               converged,
            IterationsUsed:          iter);
    }

    /// <summary>
    /// Compute the chamber outer-wall surface area [m²] = lateral cylinder
    /// + two end caps. Assumes a flat-end chamber (the heater coil is
    /// internal and doesn't change the radiating surface).
    /// </summary>
    internal static double ComputeChamberOuterSurfaceArea(double length_m, double outerRadius_m)
    {
        const double pi = Math.PI;
        double lateral = 2.0 * pi * outerRadius_m * length_m;
        double endCaps = 2.0 * pi * outerRadius_m * outerRadius_m;
        return lateral + endCaps;
    }

    /// <summary>
    /// Estimate the heater-coil-to-gas film ΔT [K]. Wave-1 uses a constant
    /// 200 K offset calibrated from MR-501B operating data; Wave-2 may
    /// upgrade to a real Dittus-Boelter film coefficient when arcjet
    /// physics demands it.
    /// </summary>
    /// <remarks>
    /// Why constant rather than Dittus-Boelter: at sub-mm hydraulic
    /// diameters and sub-Reynolds-1000 flow, Dittus-Boelter is outside
    /// its validated range; the constant offset matches the MR-501B
    /// hardware data better than a poorly-calibrated correlation. The
    /// gate <c>RESISTOJET_HEATER_TEMP_EXCEEDED</c> bounds the heater
    /// material limit using this offset.
    /// </remarks>
    internal static double EstimateFilmDeltaT_K(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions,
        double T_chamber_K)
    {
        // Constant offset per Wave-1 spec. Future fidelity upgrade tracked
        // in the pillar spec's "Conscious omissions" section.
        return 200.0;
    }
}
