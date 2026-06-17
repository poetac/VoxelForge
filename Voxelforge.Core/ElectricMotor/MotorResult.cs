// MotorResult.cs — Sprint EM.W1 solver output.

namespace Voxelforge.ElectricMotor;

/// <summary>
/// Solve-time outputs for an electric-motor snapshot at the design
/// (V_bus, I_a) operating point (Sprint EM.W1).
/// </summary>
/// <param name="ShaftTorque_Nm">τ = K_t · I_a [N·m].</param>
/// <param name="BackEmf_V">V_emf = V_bus − I_a · R_a [V] (Kirchhoff).</param>
/// <param name="AngularVelocity_rads">ω = V_emf / K_e [rad/s].</param>
/// <param name="RotationSpeed_rpm">N [rpm] = ω · 60 / (2π).</param>
/// <param name="MechanicalPower_W">P_mech = τ · ω [W].</param>
/// <param name="CopperLoss_W">P_cu = I_a² · R_a [W].</param>
/// <param name="ElectricalPowerInput_W">P_in = V_bus · I_a [W].</param>
/// <param name="MotorEfficiency">η = (P_mech − P_loss_const) / P_in [-].</param>
internal sealed record MotorResult(
    double ShaftTorque_Nm,
    double BackEmf_V,
    double AngularVelocity_rads,
    double RotationSpeed_rpm,
    double MechanicalPower_W,
    double CopperLoss_W,
    double ElectricalPowerInput_W,
    double MotorEfficiency);
