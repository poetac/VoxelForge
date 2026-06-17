// SavitskyPlaningModel.cs — Sprint M.W3 Savitsky planing-hull resistance
// physics helper.
//
// Stateless, allocation-free, deterministic implementation of the Savitsky
// 1964 planing-hull resistance model per:
//
//   Savitsky D. (1964). "Hydrodynamic Design of Planing Hulls." Marine
//   Technology 1(1), pp. 71–95.
//
// Physics summary (prismatic hard-chine planing hull, single-step):
//
//   Speed coefficient (beam-based Froude):
//     C_v = V / √(g · b)
//
//   Required deadrise-corrected lift coefficient (lift balance):
//     C_Lβ_req = (2 · Δ · g) / (ρ · V² · b²)
//
//   Inverse deadrise correction (Savitsky 1964):
//     C_L0 = C_Lβ + 0.0065 · β · C_L0^0.60       (fixed-point in C_L0)
//
//   Wetted-length-to-beam (1-D Newton on the dominant √λ term):
//     C_L0 = τ^1.1 · (0.0120 · √λ + 0.0055 · λ^2.5 / C_v²)     [Savitsky empirical fit]
//
//   Equilibrium trim (Savitsky cluster correlation, C_v ∈ [3, 8]):
//     τ_eq ≈ τ_min + (τ_max − τ_min) · (C_v − C_v_min) / (C_v_max − C_v_min)
//          clipped to [τ_min, τ_max] outside the cluster band.
//
//   Skin-friction resistance (ITTC-1957):
//     C_F = 0.075 / (log10(Re_λb) − 2)²
//     R_F = ½ ρ V² · S_w · C_F
//
//   Wetted area (mean):
//     S_w = λ · b² / cos(β)                     (slanted-bottom correction)
//
//   Total resistance (Savitsky for prismatic hull, induced + frictional):
//     R_w = Δ · g · tan(τ)                       (induced — geometric)
//     R_total = R_F · cos(τ) + R_w
//
// Design philosophy: this is a closed-form approximation aimed at the
// planing cluster envelope (C_v ∈ [3, 7], λ ∈ [1.5, 4], β ∈ [10°, 25°]).
// The trim is set from a cluster-anchored linear correlation rather than a
// full LCG moment balance — a real LCG-balanced solve adds an iterative
// 2-variable system that's tractable but not warranted at this fidelity
// (the LongitudinalCgFraction design field shows up in the gates, not
// the equilibrium solve).
//
// Validation tolerance per ADR-029 D4 generalised: ±15 % resistance, ±2°
// trim. Cluster anchor: representative 11 m hard-chine planing yacht
// (LWL = 10 m, B = 3 m, β = 18°, Δ = 5 000 kg) at 25 kt design cruise.

using System;

namespace Voxelforge.Marine.Hydrodynamics;

/// <summary>
/// Output of the Savitsky planing-hull resistance model. Pure data; no
/// reference to PicoGK or any I/O surface.
/// </summary>
/// <param name="TrimAngle_deg">Equilibrium trim angle τ [°] (cluster correlation).</param>
/// <param name="WettedLengthToBeamRatio">Mean wetted length-to-beam ratio λ [-].</param>
/// <param name="WettedSurfaceArea_m2">Mean wetted surface area S_w [m²].</param>
/// <param name="LiftCoefficientCL0">Zero-deadrise lift coefficient C_L0 [-].</param>
/// <param name="LiftCoefficientBeta">Deadrise-corrected lift coefficient C_Lβ [-].</param>
/// <param name="SpeedCoefficient">Beam-based Froude C_v = V / √(gb) [-].</param>
/// <param name="FrictionalResistance_N">R_F (ITTC-1957 skin friction) [N].</param>
/// <param name="ResiduaryResistance_N">R_w (Savitsky induced) [N].</param>
/// <param name="TotalResistance_N">R_F · cos(τ) + R_w [N].</param>
/// <param name="ResistanceCoefficient">R_total / (½ ρ V² S_w) [-].</param>
/// <param name="ReynoldsNumber">Re_λb [-].</param>
/// <param name="Converged">True iff the C_L0-from-C_Lβ fixed point + λ Newton both converged.</param>
public sealed record SavitskyPlaningResult(
    double TrimAngle_deg,
    double WettedLengthToBeamRatio,
    double WettedSurfaceArea_m2,
    double LiftCoefficientCL0,
    double LiftCoefficientBeta,
    double SpeedCoefficient,
    double FrictionalResistance_N,
    double ResiduaryResistance_N,
    double TotalResistance_N,
    double ResistanceCoefficient,
    double ReynoldsNumber,
    bool   Converged);

/// <summary>
/// Savitsky 1964 planing-hull resistance model. Mirror of
/// <see cref="HoernerDragSolver"/> for planing surface hulls.
/// </summary>
public static class SavitskyPlaningModel
{
    /// <summary>Standard gravity g [m/s²].</summary>
    public const double g0 = 9.80665;

    // ── Cluster trim correlation constants ──────────────────────────────

    /// <summary>Trim cluster low edge [°] at C_v_low.</summary>
    public const double TrimClusterLow_deg  = 3.5;

    /// <summary>Trim cluster high edge [°] at C_v_high.</summary>
    public const double TrimClusterHigh_deg = 5.5;

    /// <summary>Beam-Froude low anchor for the trim correlation.</summary>
    public const double SpeedCoefficientLow  = 3.0;

    /// <summary>Beam-Froude high anchor for the trim correlation.</summary>
    public const double SpeedCoefficientHigh = 7.0;

    /// <summary>
    /// Solve the Savitsky planing-hull equilibrium + resistance.
    /// </summary>
    /// <param name="speed_ms">Vessel speed V [m/s].</param>
    /// <param name="beamMidship_m">Beam at midship b [m].</param>
    /// <param name="deadriseAngle_deg">Deadrise angle β [°].</param>
    /// <param name="massDisplacement_kg">Mass displacement Δ [kg].</param>
    /// <param name="waterDensity_kgm3">Water density ρ [kg/m³].</param>
    /// <param name="kinematicViscosity_m2s">Kinematic viscosity ν [m²/s].</param>
    /// <returns>Solved planing state.</returns>
    public static SavitskyPlaningResult Solve(
        double speed_ms,
        double beamMidship_m,
        double deadriseAngle_deg,
        double massDisplacement_kg,
        double waterDensity_kgm3,
        double kinematicViscosity_m2s)
    {
        if (speed_ms <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed_ms),
                $"Speed_ms must be positive; got {speed_ms}.");
        if (beamMidship_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(beamMidship_m),
                $"BeamMidship_m must be positive; got {beamMidship_m}.");
        if (deadriseAngle_deg < 0 || deadriseAngle_deg > 45)
            throw new ArgumentOutOfRangeException(nameof(deadriseAngle_deg),
                $"DeadriseAngle_deg must be in [0, 45]; got {deadriseAngle_deg}.");
        if (massDisplacement_kg <= 0)
            throw new ArgumentOutOfRangeException(nameof(massDisplacement_kg),
                $"MassDisplacement_kg must be positive; got {massDisplacement_kg}.");
        if (waterDensity_kgm3 <= 0)
            throw new ArgumentOutOfRangeException(nameof(waterDensity_kgm3),
                $"WaterDensity_kgm3 must be positive; got {waterDensity_kgm3}.");
        if (kinematicViscosity_m2s <= 0)
            throw new ArgumentOutOfRangeException(nameof(kinematicViscosity_m2s),
                $"KinematicViscosity_m2s must be positive; got {kinematicViscosity_m2s}.");

        double rho   = waterDensity_kgm3;
        double V     = speed_ms;
        double b     = beamMidship_m;
        double beta  = deadriseAngle_deg;
        double Delta = massDisplacement_kg;

        // 1. Speed coefficient C_v.
        double C_v = V / Math.Sqrt(g0 * b);

        // 2. Required deadrise-corrected lift coefficient.
        double C_Lbeta_required = (2.0 * Delta * g0) / (rho * V * V * b * b);

        // 3. Recover C_L0 from C_Lβ via the inverse Savitsky correction
        //    (fixed-point iteration on C_L0, converges in ≤ 8 steps).
        bool clConverged = false;
        double C_L0 = C_Lbeta_required;
        for (int i = 0; i < 32; i++)
        {
            double next = C_Lbeta_required + 0.0065 * beta * Math.Pow(Math.Max(0.0, C_L0), 0.60);
            if (Math.Abs(next - C_L0) < 1e-9) { C_L0 = next; clConverged = true; break; }
            C_L0 = next;
        }

        // 4. Equilibrium trim angle from the cluster correlation, clipped to
        //    [τ_low, τ_high] outside the C_v anchor band.
        double tau = TrimFromClusterCorrelation(C_v);

        // 5. λ from inversion of Savitsky's lift fit at (C_L0, τ, C_v).
        (double lambda, bool lambdaConverged) = InvertCL0ForLambda(C_L0, tau, C_v);

        // 6. Wetted area + Reynolds number + skin friction.
        double cosBeta = Math.Cos(beta * Math.PI / 180.0);
        double S_w     = lambda * b * b / Math.Max(0.1, cosBeta);
        double Re      = V * lambda * b / kinematicViscosity_m2s;
        double C_F     = Re > 1e3
            ? 0.075 / Math.Pow(Math.Log10(Re) - 2.0, 2.0)
            : 0.075;  // degenerate-Re guard

        // 7. Resistance breakdown.
        double tauRad  = tau * Math.PI / 180.0;
        double R_F     = 0.5 * rho * V * V * S_w * C_F;
        double R_w     = Delta * g0 * Math.Tan(tauRad);
        double R_total = R_F * Math.Cos(tauRad) + R_w;
        double C_total = R_total / (0.5 * rho * V * V * S_w);

        return new SavitskyPlaningResult(
            TrimAngle_deg:           tau,
            WettedLengthToBeamRatio: lambda,
            WettedSurfaceArea_m2:    S_w,
            LiftCoefficientCL0:      C_L0,
            LiftCoefficientBeta:     C_Lbeta_required,
            SpeedCoefficient:        C_v,
            FrictionalResistance_N:  R_F,
            ResiduaryResistance_N:   R_w,
            TotalResistance_N:       R_total,
            ResistanceCoefficient:   C_total,
            ReynoldsNumber:          Re,
            Converged:               clConverged && lambdaConverged);
    }

    /// <summary>
    /// Equilibrium trim angle from the Savitsky cluster correlation,
    /// linear in C_v ∈ [<see cref="SpeedCoefficientLow"/>,
    /// <see cref="SpeedCoefficientHigh"/>] and clipped outside.
    /// </summary>
    public static double TrimFromClusterCorrelation(double speedCoefficient)
    {
        if (speedCoefficient <= SpeedCoefficientLow)  return TrimClusterLow_deg;
        if (speedCoefficient >= SpeedCoefficientHigh) return TrimClusterHigh_deg;
        double frac = (speedCoefficient - SpeedCoefficientLow)
                    / (SpeedCoefficientHigh - SpeedCoefficientLow);
        return TrimClusterLow_deg + frac * (TrimClusterHigh_deg - TrimClusterLow_deg);
    }

    /// <summary>
    /// Forward Savitsky lift fit C_L0(τ, λ, C_v).
    /// </summary>
    public static double LiftCoefficientCL0(double tau_deg, double lambda, double C_v)
    {
        double tauPow = Math.Pow(Math.Max(0.0, tau_deg), 1.1);
        return tauPow * (0.0120 * Math.Sqrt(Math.Max(0.0, lambda))
                       + 0.0055 * Math.Pow(Math.Max(0.0, lambda), 2.5) / Math.Max(1e-6, C_v * C_v));
    }

    /// <summary>
    /// Invert Savitsky's C_L0 fit for λ given (C_L0, τ, C_v). 1-D Newton
    /// iteration; converges in ≤ 12 steps for the cluster envelope.
    /// </summary>
    private static (double lambda, bool converged) InvertCL0ForLambda(
        double C_L0_target, double tau_deg, double C_v)
    {
        // Initial guess from the dominant √λ term:
        //   C_L0 ≈ τ^1.1 · 0.0120 · √λ  →  λ ≈ (C_L0 / (0.0120 · τ^1.1))²
        double tauPow = Math.Pow(Math.Max(0.5, tau_deg), 1.1);
        double lambda = Math.Pow(Math.Max(1e-9, C_L0_target) / Math.Max(1e-9, 0.0120 * tauPow), 2.0);
        if (!double.IsFinite(lambda) || lambda <= 0) lambda = 1.0;
        if (lambda > 50) lambda = 50;

        bool converged = false;
        for (int i = 0; i < 32; i++)
        {
            double f = LiftCoefficientCL0(tau_deg, lambda, C_v) - C_L0_target;
            double df = tauPow * (
                0.0120 * 0.5 / Math.Sqrt(Math.Max(1e-6, lambda))
              + 0.0055 * 2.5 * Math.Pow(Math.Max(0.0, lambda), 1.5) / Math.Max(1e-6, C_v * C_v));
            if (Math.Abs(df) < 1e-12) break;
            double step = f / df;
            // Damp + clamp to keep λ in a sensible band.
            step = Math.Max(-2.0, Math.Min(2.0, step));
            lambda -= step;
            if (lambda <= 0.05) lambda = 0.05;
            if (lambda > 50)    lambda = 50;
            if (Math.Abs(step) < 1e-7) { converged = true; break; }
        }
        return (lambda, converged);
    }
}
