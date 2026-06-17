// IsentropicNozzleSolver.cs — choked-flow isentropic CD-nozzle solver
// for the resistojet diverging section.
//
//   Choked-throat mass flow:
//     ṁ = (P_c · A_t / √(R · T_c)) · √γ · (2/(γ+1))^((γ+1)/(2(γ-1)))
//
//   Area-Mach relation at exit (supersonic root):
//     ε = A_exit/A_throat = (1/M_e) · [(2/(γ+1)) · (1 + ((γ-1)/2) · M_e²)]^((γ+1)/(2(γ-1)))
//
//   Exit-state isentropic relations:
//     T_e/T_c = (1 + ((γ-1)/2) · M_e²)^(-1)
//     P_e/P_c = (T_e/T_c)^(γ/(γ-1))
//     V_e    = M_e · √(γ · R · T_e)
//
//   Thrust + Isp:
//     F      = ṁ · V_e + (P_e − P_∞) · A_e
//     Isp_vac = F / (ṁ · g₀)  with P_∞ = 0
//
// Citations: Sutton/Biblarz §3 (choked nozzle theory); Anderson "Modern
// Compressible Flow" 4e §5 (area-Mach relation, Newton on supersonic root).

using System;
using Voxelforge.ElectricPropulsion.Thermo;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Result of the isentropic-nozzle solve.
/// </summary>
/// <param name="ChamberPressure_Pa">Chamber stagnation pressure derived from the choked-throat continuity equation.</param>
/// <param name="ExitMachNumber">M at nozzle exit (supersonic when feasible).</param>
/// <param name="ExitTemperature_K">Static T at nozzle exit.</param>
/// <param name="ExitPressure_Pa">Static P at nozzle exit.</param>
/// <param name="ExitVelocity_ms">Gas velocity at exit plane.</param>
/// <param name="Thrust_N">Total thrust = ṁ·V_e + (P_e − P_∞)·A_e.</param>
/// <param name="IspVacuum_s">Vacuum specific impulse with P_∞ = 0.</param>
/// <param name="ChokedFlow">True iff <c>P_c / P_∞ ≥ ((γ+1)/2)^(γ/(γ-1))</c>.</param>
/// <param name="Converged">True iff the area-Mach Newton iteration converged.</param>
public sealed record IsentropicNozzleResult(
    double ChamberPressure_Pa,
    double ExitMachNumber,
    double ExitTemperature_K,
    double ExitPressure_Pa,
    double ExitVelocity_ms,
    double Thrust_N,
    double IspVacuum_s,
    bool   ChokedFlow,
    bool   Converged);

/// <summary>
/// Choked-flow isentropic nozzle solver. See class file header for citations.
/// </summary>
public static class IsentropicNozzleSolver
{
    /// <summary>Newton convergence tolerance on M_exit.</summary>
    public const double Tolerance_M = 1e-7;

    /// <summary>Maximum Newton iterations before declaring no-convergence.</summary>
    public const int MaxIterations = 64;

    /// <summary>
    /// Solve the choked-throat isentropic CD-nozzle for the given chamber
    /// state + nozzle geometry.
    /// </summary>
    /// <param name="design">Resistojet design — uses NozzleThroatRadius_mm + NozzleAreaRatio.</param>
    /// <param name="conditions">Operating conditions — uses AmbientPressure_Pa + InletComposition.</param>
    /// <param name="chamberTemperature_K">Stagnation temperature at the throat (from the heater solve).</param>
    /// <param name="propellantMassFlow_kgs">Mass flow rate (a design knob, not a derived quantity here).</param>
    /// <returns>Full nozzle state + thrust + Isp.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="chamberTemperature_K"/> or
    /// <paramref name="propellantMassFlow_kgs"/> is NaN or non-positive, or
    /// when <paramref name="design"/>'s NozzleThroatRadius_mm is NaN /
    /// non-positive or NozzleAreaRatio is NaN / ≤ 1.
    /// </exception>
    public static IsentropicNozzleResult Solve(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions,
        double chamberTemperature_K,
        double propellantMassFlow_kgs)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(conditions);
        if (double.IsNaN(chamberTemperature_K) || chamberTemperature_K <= 0)
            throw new ArgumentOutOfRangeException(nameof(chamberTemperature_K),
                $"Chamber temperature must be positive; got T_c={chamberTemperature_K:F1} K.");
        if (double.IsNaN(propellantMassFlow_kgs) || propellantMassFlow_kgs <= 0)
            throw new ArgumentOutOfRangeException(nameof(propellantMassFlow_kgs),
                $"Mass flow must be positive; got ṁ={propellantMassFlow_kgs:E3} kg/s.");
        if (double.IsNaN(design.NozzleThroatRadius_mm) || design.NozzleThroatRadius_mm <= 0
            || double.IsNaN(design.NozzleAreaRatio) || design.NozzleAreaRatio <= 1)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"Throat radius must be positive and area ratio > 1; "
              + $"got R_t={design.NozzleThroatRadius_mm:F3} mm, ε={design.NozzleAreaRatio:F3}.");

        double R_t_m   = design.NozzleThroatRadius_mm * 1e-3;
        double A_t     = Math.PI * R_t_m * R_t_m;
        double epsilon = design.NozzleAreaRatio;
        double A_e     = epsilon * A_t;

        // γ at chamber temperature (composition-mass-averaged).
        double gamma = PropellantTables.MixtureGamma(conditions.InletComposition, chamberTemperature_K);
        double R_spec = PropellantTables.R_universal / PropellantTables.MixtureMW(conditions.InletComposition);

        // Derive chamber pressure from the choked-throat continuity equation.
        // ṁ = (P_c · A_t / √(R · T_c)) · √γ · (2/(γ+1))^((γ+1)/(2(γ-1)))
        // ⇒ P_c = ṁ · √(R · T_c / γ) / A_t · (2/(γ+1))^(-(γ+1)/(2(γ-1)))
        double choke_factor = Math.Pow(2.0 / (gamma + 1.0), (gamma + 1.0) / (2.0 * (gamma - 1.0)));
        double P_chamber = propellantMassFlow_kgs * Math.Sqrt(R_spec * chamberTemperature_K / gamma)
                         / A_t / choke_factor;

        // Choking criterion. For vacuum (P_∞ = 0), ratio is infinite, always choked.
        // Critical pressure ratio: ((γ+1)/2)^(γ/(γ-1))
        double criticalRatio = Math.Pow((gamma + 1.0) / 2.0, gamma / (gamma - 1.0));
        bool choked = conditions.AmbientPressure_Pa <= 0
            ? true
            : (P_chamber / conditions.AmbientPressure_Pa) >= criticalRatio;

        // Solve area-Mach relation for supersonic root.
        // ε = (1/M) · [(2/(γ+1)) · (1 + ((γ-1)/2) · M²)]^((γ+1)/(2(γ-1)))
        // Newton iteration starting from M = 3 (typical resistojet supersonic exit).
        double M_e = 3.0;
        bool converged = false;
        for (int iter = 0; iter < MaxIterations; iter++)
        {
            double F     = AreaMachResidual(epsilon, M_e, gamma);
            double F2    = AreaMachResidual(epsilon, M_e + 1e-6, gamma);
            double dF    = (F2 - F) / 1e-6;
            if (Math.Abs(dF) < 1e-14) break;
            double step  = F / dF;
            // Damp large steps + clamp to supersonic regime.
            if (step > 0.5)        step = 0.5;
            else if (step < -0.5)  step = -0.5;
            double M_new = M_e - step;
            if (M_new < 1.01) M_new = 1.01;
            if (M_new > 50.0) M_new = 50.0;
            if (Math.Abs(M_new - M_e) < Tolerance_M)
            {
                M_e = M_new;
                converged = true;
                break;
            }
            M_e = M_new;
        }

        // Exit isentropic state.
        double T_e_over_T_c = 1.0 / (1.0 + ((gamma - 1.0) / 2.0) * M_e * M_e);
        double T_e = chamberTemperature_K * T_e_over_T_c;
        double P_e = P_chamber * Math.Pow(T_e_over_T_c, gamma / (gamma - 1.0));
        double V_e = M_e * Math.Sqrt(gamma * R_spec * T_e);

        // Thrust (vacuum: P_∞ = 0; ground-test would add (P_e − P_amb) · A_e).
        double thrust = propellantMassFlow_kgs * V_e + (P_e - conditions.AmbientPressure_Pa) * A_e;
        double isp_vac = (propellantMassFlow_kgs * V_e + P_e * A_e) / (propellantMassFlow_kgs * PropellantTables.g0);

        return new IsentropicNozzleResult(
            ChamberPressure_Pa: P_chamber,
            ExitMachNumber:     M_e,
            ExitTemperature_K:  T_e,
            ExitPressure_Pa:    P_e,
            ExitVelocity_ms:    V_e,
            Thrust_N:           thrust,
            IspVacuum_s:        isp_vac,
            ChokedFlow:         choked,
            Converged:          converged);
    }

    /// <summary>
    /// Residual: ε(M, γ) − ε_target. Zero at the supersonic root
    /// satisfying the area-Mach relation.
    /// </summary>
    internal static double AreaMachResidual(double epsilonTarget, double M, double gamma)
    {
        double bracket = (2.0 / (gamma + 1.0)) * (1.0 + ((gamma - 1.0) / 2.0) * M * M);
        double exp     = (gamma + 1.0) / (2.0 * (gamma - 1.0));
        double epsilonOfM = (1.0 / M) * Math.Pow(bracket, exp);
        return epsilonOfM - epsilonTarget;
    }
}
