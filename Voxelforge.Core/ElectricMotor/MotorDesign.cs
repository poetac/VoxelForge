// MotorDesign.cs — Sprint EM.W1 electric-motor design record.
//
// Sized to bracket the Tesla Model S Drive Unit-class PMSM (~ 270 kW
// peak, K_t ≈ 0.5 N·m/A, K_e ≈ 0.5 V/(rad/s), R_armature ≈ 0.05 Ω).

using System;

namespace Voxelforge.ElectricMotor;

/// <summary>
/// Design parameters for a 3-phase brushless / PMSM motor (Sprint EM.W1
/// scaffold). Standalone — does not integrate with the
/// <c>IEngine&lt;,,&gt;</c> stack yet.
/// </summary>
/// <param name="Kind">Motor topology.</param>
/// <param name="TorqueConstant_NmA">K_t [N·m/A] — torque per amp at the
/// shaft. For SI units, K_t = K_e (back-EMF constant).</param>
/// <param name="ArmatureResistance_Ohm">R_a [Ω] — phase-to-phase
/// equivalent winding resistance.</param>
/// <param name="ConstantPowerLoss_W">P_loss [W] — combined iron + bearing
/// friction loss, treated as constant across the operating envelope at
/// scaffold fidelity. Cluster mid-band 50-500 W for kW-class motors.</param>
/// <param name="BusVoltage_V">V_bus [V] — DC-link voltage at the
/// inverter bus.</param>
/// <param name="ArmatureCurrent_A">I_a [A] — operating current. Drives
/// torque + power directly.</param>
internal sealed record MotorDesign(
    MotorKind Kind,
    double TorqueConstant_NmA,
    double ArmatureResistance_Ohm,
    double ConstantPowerLoss_W,
    double BusVoltage_V,
    double ArmatureCurrent_A)
{
    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (Kind == MotorKind.None)
            throw new ArgumentException(
                "Kind must be set (None sentinel is reserved).", nameof(Kind));
        if (TorqueConstant_NmA <= 0)
            throw new ArgumentException("TorqueConstant_NmA must be > 0.",
                nameof(TorqueConstant_NmA));
        if (ArmatureResistance_Ohm <= 0)
            throw new ArgumentException("ArmatureResistance_Ohm must be > 0.",
                nameof(ArmatureResistance_Ohm));
        if (ConstantPowerLoss_W < 0)
            throw new ArgumentException("ConstantPowerLoss_W must be ≥ 0.",
                nameof(ConstantPowerLoss_W));
        if (BusVoltage_V <= 0)
            throw new ArgumentException("BusVoltage_V must be > 0.",
                nameof(BusVoltage_V));
        if (ArmatureCurrent_A <= 0)
            throw new ArgumentException("ArmatureCurrent_A must be > 0.",
                nameof(ArmatureCurrent_A));
    }
}
