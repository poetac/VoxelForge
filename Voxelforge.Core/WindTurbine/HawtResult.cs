// HawtResult.cs — Sprint WT.W1 solver output.

namespace Voxelforge.WindTurbine;

/// <summary>
/// Solve-time outputs for a horizontal-axis wind turbine snapshot at a
/// specified wind speed (Sprint WT.W1 scaffold).
/// </summary>
/// <param name="WindSpeed_ms">Free-stream wind speed at hub height V_∞ [m/s].</param>
/// <param name="AvailablePower_W">P_available = 0.5 · ρ · A · V³ [W] — the full kinetic-energy flux through the swept area.</param>
/// <param name="PowerCoefficient">C_p = P_rotor / P_available [-]. Capped at Betz limit 16/27 = 0.5926.</param>
/// <param name="TipSpeedRatio">λ = ωR / V [-].</param>
/// <param name="RotorAngularSpeed_rads">ω [rad/s].</param>
/// <param name="TipSpeed_ms">v_tip = ωR [m/s] — noise + structural limit.</param>
/// <param name="RotorPower_W">P_rotor = C_p · P_available [W] — shaft power off the rotor.</param>
/// <param name="ElectricalPower_W">P_elec = η_drivetrain · P_rotor [W] — net grid-bus output.</param>
/// <param name="RotorThrust_N">T = C_T · 0.5 · ρ · A · V² [N] — axial thrust on the tower / foundation.</param>
/// <param name="ThrustCoefficient">C_T [-] from actuator-disk momentum theory at the C_p-derived axial induction a.</param>
/// <param name="AxialInductionFactor">a [-] — fractional wind-speed slowdown through the disk.</param>
internal sealed record HawtResult(
    double WindSpeed_ms,
    double AvailablePower_W,
    double PowerCoefficient,
    double TipSpeedRatio,
    double RotorAngularSpeed_rads,
    double TipSpeed_ms,
    double RotorPower_W,
    double ElectricalPower_W,
    double RotorThrust_N,
    double ThrustCoefficient,
    double AxialInductionFactor)
{
    /// <summary>N [rpm] = ω · 60 / (2π). Convenience derived from RotorAngularSpeed_rads, matching the MotorResult / WindTurbineComponent integration port convention.</summary>
    public double RotationSpeed_rpm => RotorAngularSpeed_rads * 60.0 / (2.0 * System.Math.PI);
}
