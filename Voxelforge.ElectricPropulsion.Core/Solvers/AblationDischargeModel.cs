// AblationDischargeModel.cs — Wave-2 PPT Solbes-Vondra ablation-discharge
// physics helper.
//
// Stateless, allocation-free, deterministic implementation of the
// Solbes-Vondra ablation-discharge fit for Pulsed Plasma Thrusters per
// Vondra & Thomassen "Flight Qualified Pulsed Plasma Thruster" (J. Spacecraft
// 1974) + Solbes & Vondra "Performance Study of a Solid Fuel Pulsed Electric
// Microthruster" (J. Spacecraft 1973). Aerojet EO-1 PPT cluster anchor.
//
// Physics summary (pulsed parallel-rail PPT with solid PTFE propellant):
//   Δm        = K_m · E_cap                (mass ablated per pulse, linear-in-energy)
//   I_bit     = K_i · √E_cap               (impulse bit, square-root-in-energy)
//   v_exit    = I_bit / Δm = K_i / (K_m · √E_cap)
//                                          (derived; decreases with energy)
//   T_avg     = I_bit · f_pulse            (time-averaged thrust)
//   ṁ_avg     = Δm   · f_pulse             (time-averaged mass flow)
//   P_avg     = E_cap · f_pulse            (average bus power; SA bind clip)
//   Isp_avg   = T_avg / (ṁ_avg · g₀) = v_exit / g₀
//   θ_plume   = arctan(K_plume)            (cluster anchor, geometry-independent)
//
// Aerojet EO-1 PPT calibration anchor (E_cap = 22 J/pulse, f_pulse ≈ 5 Hz,
// average power ≈ 100 W, target I_bit ≈ 860 µN·s, target Isp ≈ 870 s on
// solid PTFE):
//   K_m, K_i fitted so:
//     – Δm    ≈ 1.01e-7 kg ≈ 101 µg per pulse
//     – I_bit ≈ 860 µN·s
//     – v_exit ≈ 8500 m/s ≈ 870 s Isp
//
// The three calibration constants (K_m, K_i, K_plume) plus the reference
// exhaust velocity are exposed as public consts so reviewers can audit the
// calibration trail. Wider validation tolerance (±25 % impulse-bit / ±15 %
// Isp per ADR-029 D4 generalised) absorbs the residual Solbes-Vondra fit
// scatter across the published PPT cluster.

using System;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Output of the Solbes-Vondra PPT ablation-discharge model. Pure data;
/// no reference to PicoGK or any I/O surface.
/// </summary>
public sealed record AblationDischargeResult(
    double ImpulseBit_Ns,
    double MassPerPulse_kg,
    double AverageThrust_N,
    double AverageIsp_s,
    double AverageMassFlow_kgs,
    double AveragePower_W,
    double ExitVelocity_ms,
    double PlumeDivergenceHalfAngle_rad,
    bool   Converged);

/// <summary>
/// Solbes-Vondra ablation-discharge model for solid-PTFE Pulsed Plasma
/// Thrusters. Mirror of <see cref="MaeckerKovityaArcModel"/> for the PPT
/// variant.
/// </summary>
public static class AblationDischargeModel
{
    // ── Physical constants ──────────────────────────────────────────────

    /// <summary>Standard gravity for Isp [m/s²].</summary>
    public const double g0 = 9.80665;

    // ── Calibration constants (Aerojet EO-1 PPT cluster anchor) ─────────

    /// <summary>
    /// Reference exhaust velocity at the cluster anchor [m/s]. Aerojet EO-1
    /// PPT lands ≈ 8500 m/s (≈ 870 s Isp) on solid PTFE per Vondra &amp;
    /// Thomassen (J. Spacecraft 1974). Used (a) as a sanity-check / cluster
    /// anchor and (b) when the design overrides v_exit via
    /// <see cref="ElectricPropulsionEngineDesign.PptIspCalibration"/>
    /// (NaN → use the anchor implicitly via K_i / K_m at the design E_cap).
    /// </summary>
    public const double DefaultExhaustVelocity_ms = 8500.0;

    /// <summary>
    /// Solbes-Vondra mass-per-pulse coefficient K_m [kg/J] in the linear fit
    /// Δm = K_m · E_cap. Calibrated so EO-1 (E_cap = 22 J) lands
    /// Δm ≈ 1.01e-7 kg ≈ 101 µg per pulse.
    /// </summary>
    public const double MassPerPulseCoefficient = 4.6e-9;

    /// <summary>
    /// Solbes-Vondra impulse-bit coefficient K_i [N·s / √J] in the square-
    /// root fit I_bit = K_i · √E_cap. Calibrated alongside <see cref="MassPerPulseCoefficient"/>
    /// so EO-1 (E_cap = 22 J) lands I_bit ≈ 860 µN·s. The pair (K_m, K_i)
    /// implicitly fixes v_exit = K_i / (K_m · √E_cap) ≈ 8500 m/s at E_cap = 22 J.
    /// </summary>
    public const double ImpulseBitCoefficient = 1.834e-4;

    /// <summary>
    /// Plume-divergence calibration constant K_plume in θ = arctan(K_plume).
    /// PPT plume geometry is dominated by the parallel-rail electrode gap +
    /// ablation-vapour expansion — typical published half-angles 15–30°
    /// (Spanjers et al., "PPT Performance" AIAA-2002-3974). K_plume = 0.30
    /// → θ ≈ 16.7°, a centred cluster value. Held constant rather than
    /// scaled with electrode geometry because empirical PPT plume data is
    /// too sparse to support a richer fit at this fidelity.
    /// </summary>
    public const double PptPlumeConstant = 0.30;

    /// <summary>
    /// Solve the Solbes-Vondra PPT ablation-discharge model end-to-end.
    /// </summary>
    /// <param name="capacitorEnergy_J">E_cap [J] — energy stored per pulse.</param>
    /// <param name="pulseFrequency_Hz">f_pulse [Hz] — pulse repetition rate.</param>
    /// <param name="electrodeGap_mm">Inter-electrode gap [mm] (parallel-rail spacing). Used as a NaN-trap and reference for plume model refinements; not currently consumed by the energy-balance fit.</param>
    /// <param name="propellantBarLength_mm">PTFE bar length along discharge axis [mm]. NaN-trap reference.</param>
    /// <param name="electrodeWidth_mm">Rail width [mm]. NaN-trap reference.</param>
    /// <param name="ispOverride_s">Optional Isp override [s]. <see cref="double.NaN"/> uses the cluster-anchor relation; finite values force v_exit = ispOverride_s · g₀.</param>
    /// <returns>Solved performance state.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any required input is NaN or non-positive, or when the
    /// optional <paramref name="ispOverride_s"/> is finite but non-positive.
    /// </exception>
    public static AblationDischargeResult Solve(
        double capacitorEnergy_J,
        double pulseFrequency_Hz,
        double electrodeGap_mm,
        double propellantBarLength_mm,
        double electrodeWidth_mm,
        double ispOverride_s)
    {
        if (double.IsNaN(capacitorEnergy_J) || capacitorEnergy_J <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacitorEnergy_J),
                $"CapacitorEnergy_J must be positive; got E_cap={capacitorEnergy_J:F3} J.");
        if (double.IsNaN(pulseFrequency_Hz) || pulseFrequency_Hz <= 0)
            throw new ArgumentOutOfRangeException(nameof(pulseFrequency_Hz),
                $"PulseFrequency_Hz must be positive; got f={pulseFrequency_Hz:F3} Hz.");
        if (double.IsNaN(electrodeGap_mm) || electrodeGap_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(electrodeGap_mm),
                $"PptElectrodeGap_mm must be positive; got {electrodeGap_mm:F3} mm.");
        if (double.IsNaN(propellantBarLength_mm) || propellantBarLength_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(propellantBarLength_mm),
                $"PptPropellantBarLength_mm must be positive; got {propellantBarLength_mm:F3} mm.");
        if (double.IsNaN(electrodeWidth_mm) || electrodeWidth_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(electrodeWidth_mm),
                $"PptElectrodeWidth_mm must be positive; got {electrodeWidth_mm:F3} mm.");
        // ispOverride_s may be NaN (cluster-anchor mode) or finite-positive;
        // negative values are nonphysical.
        if (!double.IsNaN(ispOverride_s) && ispOverride_s <= 0)
            throw new ArgumentOutOfRangeException(nameof(ispOverride_s),
                $"PptIspCalibration must be NaN or positive; got Isp={ispOverride_s:F1} s.");

        // Reference the geometry inputs so they remain part of the validation
        // surface (NaN propagation + future model refinements that DO use them).
        _ = electrodeGap_mm;
        _ = propellantBarLength_mm;
        _ = electrodeWidth_mm;

        // 1. Solbes-Vondra fits.
        double sqrtE = Math.Sqrt(capacitorEnergy_J);
        double deltaM = MassPerPulseCoefficient * capacitorEnergy_J;
        double impulseBit = double.IsNaN(ispOverride_s)
            // Cluster-anchor mode: I_bit = K_i · √E_cap (Solbes-Vondra).
            ? ImpulseBitCoefficient * sqrtE
            // Override mode: I_bit = Δm · (Isp · g₀) — momentum from the override.
            : deltaM * ispOverride_s * g0;

        // 2. Effective exit velocity. Derived from I_bit / Δm so the override
        //    mode and the cluster-anchor mode share a single bookkeeping path.
        double v_exit = deltaM > 0 ? impulseBit / deltaM : 0.0;

        // 3. Time-averaged quantities.
        double avgThrust = impulseBit * pulseFrequency_Hz;
        double avgMassFlow = deltaM * pulseFrequency_Hz;
        double avgPower = capacitorEnergy_J * pulseFrequency_Hz;

        // 4. Average specific impulse. v_exit / g0 collapses to the Isp
        //    override when finite, else to the Solbes-Vondra-implied Isp.
        double avgIsp = v_exit / g0;

        // 5. Plume divergence half-angle. Cluster anchor; not scaled with
        //    electrode geometry.
        double theta = Math.Atan(PptPlumeConstant);

        return new AblationDischargeResult(
            ImpulseBit_Ns:                impulseBit,
            MassPerPulse_kg:              deltaM,
            AverageThrust_N:              avgThrust,
            AverageIsp_s:                 avgIsp,
            AverageMassFlow_kgs:          avgMassFlow,
            AveragePower_W:               avgPower,
            ExitVelocity_ms:              v_exit,
            PlumeDivergenceHalfAngle_rad: theta,
            Converged:                    true);
    }
}
