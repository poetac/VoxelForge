// GimbalMount.cs — Thrust-vectoring mount configuration + closed-form
// stiffness and bearing-stress estimates for the selected mount.
//
// Four configurations:
//   • FixedFlange       — today's default; nozzle-exit plate only.
//   • PinJointGimbal    — single-axis pin joint; stiffness from bending
//                         of two lugs carrying thrust through a pin.
//   • CardanGimbal      — two-axis Cardan (gimbal) yoke.
//   • FlexureGimbal     — monolithic flex arms (like Merlin-style
//                         "dog-bone" flexures). Highest stiffness-
//                         per-mass but requires fatigue analysis.
//
// Stiffness is Nm/rad about the thrust axis. Bearing stress is the
// Hertzian line-contact stress at the trunnion pin × lug interface
// (or the flexure peak surface stress, for FlexureGimbal).
//
// MVP fidelity:
//   • Beam-theory closed form — not FEA.
//   • Bearing stress uses σ_bearing = F / (d_pin · L_lug) (projected
//     area, simple trunnion rule).
//   • FlexureGimbal uses σ = M·c/I for a thin rectangular arm.
//
// References:
//   Huzel & Huang AIAA Vol. 147 §4.5 (gimbal mounts);
//   Roark's Formulas for Stress &amp; Strain §11 (beams in bending).

using Voxelforge.HeatTransfer;

namespace Voxelforge.Structure;

public enum MountConfiguration
{
    /// <summary>Today's default — solid nozzle-exit plate, no vectoring.</summary>
    FixedFlange = 0,
    /// <summary>Single-axis pin joint gimbal (simplest vectoring mount).</summary>
    PinJointGimbal,
    /// <summary>Two-axis Cardan yoke gimbal (pitch + yaw decoupled).</summary>
    CardanGimbal,
    /// <summary>Monolithic flexure gimbal (no moving parts; limited deflection).</summary>
    FlexureGimbal,
}

public sealed record GimbalMountResult(
    MountConfiguration Configuration,
    double Stiffness_Nm_per_rad,
    double BearingStress_MPa,
    double BearingMargin,               // σ_yield / σ_bearing (at 300 K)
    bool   StressAcceptable,
    string Notes);

public static class GimbalMount
{
    /// <summary>
    /// Bearing-stress margin (σ_yield / σ_bearing) below which we flag
    /// the mount as structurally suspect. 2.0 is a conservative factor-
    /// of-safety target for primary structure on reusable hardware.
    /// </summary>
    public const double MinBearingMargin = 2.0;

    /// <summary>
    /// Pin diameter (mm) assumed for pin-joint / Cardan trunnions.
    /// Calibrated against sub-kN test articles; larger hardware needs
    /// a proportional bump.
    /// </summary>
    public const double PinDiameter_mm = 10.0;

    /// <summary>Lug length (mm) along the pin — projected-area footprint.</summary>
    public const double LugLength_mm = 20.0;

    /// <summary>Flexure-arm thickness (mm) for FlexureGimbal.</summary>
    public const double FlexureThickness_mm = 3.0;

    /// <summary>Flexure-arm length (mm) from the mount to the free end.</summary>
    public const double FlexureLength_mm = 40.0;

    public static GimbalMountResult Evaluate(
        MountConfiguration config,
        double thrust_N,
        WallMaterial material)
    {
        if (config == MountConfiguration.FixedFlange || thrust_N <= 0)
        {
            return new GimbalMountResult(
                Configuration: config,
                Stiffness_Nm_per_rad: double.PositiveInfinity,
                BearingStress_MPa: 0,
                BearingMargin: double.PositiveInfinity,
                StressAcceptable: true,
                Notes: "Fixed flange — no vectoring, infinite notional stiffness.");
        }

        double yield = material.YieldStrengthCold_MPa;

        switch (config)
        {
            case MountConfiguration.PinJointGimbal:
            {
                double d_m   = PinDiameter_mm * 1e-3;
                double L_m   = LugLength_mm   * 1e-3;
                // Bearing stress via projected-area rule.
                double bearing_Pa = thrust_N / (d_m * L_m);
                double bearing_MPa = bearing_Pa * 1e-6;
                // Stiffness: two lugs in parallel, each a cantilever of
                // length L/2 with the pin as a simple support.
                double E = material.ElasticModulusCold_GPa * 1e9;
                double I_pin = System.Math.PI * d_m * d_m * d_m * d_m / 64.0;
                double k = 12 * E * I_pin / (L_m * L_m * L_m);
                double margin = yield / System.Math.Max(bearing_MPa, 1e-3);
                return new GimbalMountResult(
                    Configuration: config,
                    Stiffness_Nm_per_rad: k,
                    BearingStress_MPa: bearing_MPa,
                    BearingMargin: margin,
                    StressAcceptable: margin >= MinBearingMargin,
                    Notes: $"Single-axis pin joint, Ø{PinDiameter_mm:F1} mm pin × {LugLength_mm:F1} mm lug. "
                         + $"σ_bearing = {bearing_MPa:F0} MPa vs σ_y = {yield:F0} MPa ({margin:F2}× margin).");
            }

            case MountConfiguration.CardanGimbal:
            {
                // Cardan = two orthogonal pin joints; each pin carries
                // the full thrust in its active axis. Use the same
                // bearing-stress formula but note that the user gets
                // two-axis decoupling at a small mass penalty.
                double d_m = PinDiameter_mm * 1e-3;
                double L_m = LugLength_mm   * 1e-3;
                double bearing_Pa = thrust_N / (d_m * L_m);
                double bearing_MPa = bearing_Pa * 1e-6;
                double E = material.ElasticModulusCold_GPa * 1e9;
                double I = System.Math.PI * d_m * d_m * d_m * d_m / 64.0;
                // Cardan yoke has two stages in series → half the stiffness
                // of a single pin joint with the same pin.
                double k = 6 * E * I / (L_m * L_m * L_m);
                double margin = yield / System.Math.Max(bearing_MPa, 1e-3);
                return new GimbalMountResult(
                    Configuration: config,
                    Stiffness_Nm_per_rad: k,
                    BearingStress_MPa: bearing_MPa,
                    BearingMargin: margin,
                    StressAcceptable: margin >= MinBearingMargin,
                    Notes: $"Cardan yoke (2-axis), Ø{PinDiameter_mm:F1} mm pins × {LugLength_mm:F1} mm lugs. "
                         + $"σ_bearing = {bearing_MPa:F0} MPa vs σ_y = {yield:F0} MPa ({margin:F2}× margin). "
                         + $"Series-stage stiffness = half of a single pin.");
            }

            case MountConfiguration.FlexureGimbal:
            {
                // Four flexure arms in a cruciform; each is a slender
                // rectangular beam. σ_peak = M·c / I at the root.
                double t_m = FlexureThickness_mm * 1e-3;
                double L_m = FlexureLength_mm    * 1e-3;
                // Assume arm width equals chamber-scale — 25 mm nominal.
                double w_m = 0.025;
                double I = w_m * t_m * t_m * t_m / 12.0;
                double c = t_m / 2.0;
                // Per arm: M = F · L / 4 (four arms sharing thrust).
                double M = thrust_N * L_m / 4.0;
                double peak_Pa = M * c / I;
                double peak_MPa = peak_Pa * 1e-6;
                double E = material.ElasticModulusCold_GPa * 1e9;
                // Torsional stiffness: four arms, each k_i = 3EI/L in
                // bending, acting at a moment arm of the chamber radius.
                // MVP — use the single-arm rotational stiffness × 4.
                double k_per = 3 * E * I / (L_m * L_m * L_m);
                double k = 4 * k_per * w_m * w_m;
                double margin = yield / System.Math.Max(peak_MPa, 1e-3);
                return new GimbalMountResult(
                    Configuration: config,
                    Stiffness_Nm_per_rad: k,
                    BearingStress_MPa: peak_MPa,
                    BearingMargin: margin,
                    StressAcceptable: margin >= MinBearingMargin,
                    Notes: $"Flexure gimbal: 4 arms, {FlexureThickness_mm:F1} × {FlexureLength_mm:F0} mm each. "
                         + $"σ_peak = {peak_MPa:F0} MPa vs σ_y = {yield:F0} MPa ({margin:F2}× margin). "
                         + $"Limited deflection — add fatigue analysis before flight.");
            }

            default:
                return new GimbalMountResult(
                    Configuration: config,
                    Stiffness_Nm_per_rad: 0,
                    BearingStress_MPa: 0,
                    BearingMargin: 0,
                    StressAcceptable: false,
                    Notes: "Unknown mount configuration.");
        }
    }
}
