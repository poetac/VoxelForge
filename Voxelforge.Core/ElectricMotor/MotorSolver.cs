// MotorSolver.cs — Sprint EM.W1 closed-form electric-motor performance
// snapshot.
//
// Stateless, allocation-free, deterministic. The Wave-1 model treats
// the BLDC / PMSM as an ideal DC machine with linear back-EMF and a
// constant-loss term lumping iron + friction. The closed-form
// equations:
//
//   τ          = K_t · I_a                      [shaft torque]
//   V_emf      = V_bus − I_a · R_a              [Kirchhoff @ steady state]
//   ω          = V_emf / K_e   (= V_emf / K_t in SI)
//   P_mech     = τ · ω
//   P_cu       = I_a² · R_a                     [copper loss]
//   P_in       = V_bus · I_a
//   η          = (P_mech − P_loss_const) / P_in
//
// Per-phase + per-rotor-pole effects (q-axis / d-axis decomposition,
// field weakening, saliency, MTPA control) deferred to EM.W2.
//
// References:
//   Krishnan R. (2010). "Permanent Magnet Synchronous and Brushless
//     DC Motor Drives," chap 4.
//   Hughes A., Drury B. (2019). "Electric Motors and Drives:
//     Fundamentals, Types and Applications," 5th ed.
//   Tesla Model S Plaid Drive Unit teardown — Munro & Associates 2022.

using System;

namespace Voxelforge.ElectricMotor;

/// <summary>
/// Closed-form BLDC / PMSM electric-motor performance snapshot solver
/// (Sprint EM.W1).
/// </summary>
internal static class MotorSolver
{
    /// <summary>
    /// Solve the motor performance snapshot at the design (V_bus, I_a)
    /// operating point.
    /// </summary>
    internal static MotorResult Solve(MotorDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        // 1. Torque + back-EMF.
        double torque  = design.TorqueConstant_NmA * design.ArmatureCurrent_A;
        double V_emf   = design.BusVoltage_V
                       - design.ArmatureCurrent_A * design.ArmatureResistance_Ohm;
        if (V_emf <= 0)
            throw new InvalidOperationException(
                $"Back-EMF would be non-positive (V_bus={design.BusVoltage_V:F2} V, "
              + $"I·R={design.ArmatureCurrent_A * design.ArmatureResistance_Ohm:F2} V) — the "
              + "motor cannot turn; either reduce I_a or raise V_bus.");

        // 2. Speed (SI: K_e = K_t).
        double omega = V_emf / design.TorqueConstant_NmA;
        double rpm   = omega * 60.0 / (2.0 * Math.PI);

        // 3. Mechanical / electrical power + losses.
        double P_mech = torque * omega;
        double P_cu   = design.ArmatureCurrent_A * design.ArmatureCurrent_A
                      * design.ArmatureResistance_Ohm;
        double P_in   = design.BusVoltage_V * design.ArmatureCurrent_A;
        // Net useful mechanical output = shaft P_mech minus the constant
        // iron/friction loss.
        double P_useful = P_mech - design.ConstantPowerLoss_W;
        double eta = P_in > 0 ? P_useful / P_in : 0.0;

        return new MotorResult(
            ShaftTorque_Nm:           torque,
            BackEmf_V:                V_emf,
            AngularVelocity_rads:     omega,
            RotationSpeed_rpm:        rpm,
            MechanicalPower_W:        P_mech,
            CopperLoss_W:             P_cu,
            ElectricalPowerInput_W:   P_in,
            MotorEfficiency:          eta);
    }

    /// <summary>
    /// Compute the no-load (I_a → 0, no torque) angular velocity for a
    /// given bus voltage. Public-static helper for envelope studies.
    /// ω_no-load = V_bus / K_e.
    /// </summary>
    /// <param name="busVoltage_V">V_bus [V].</param>
    /// <param name="torqueConstant_NmA">K_t [N·m/A]. SI K_t = K_e.</param>
    /// <returns>ω_no-load [rad/s].</returns>
    internal static double ComputeNoLoadAngularVelocity(
        double busVoltage_V,
        double torqueConstant_NmA)
    {
        if (busVoltage_V <= 0)
            throw new ArgumentOutOfRangeException(nameof(busVoltage_V),
                "V_bus must be > 0.");
        if (torqueConstant_NmA <= 0)
            throw new ArgumentOutOfRangeException(nameof(torqueConstant_NmA),
                "K_t must be > 0.");
        return busVoltage_V / torqueConstant_NmA;
    }

    /// <summary>
    /// Sprint EM.W2. Sweep the efficiency curve across an array of
    /// armature currents at fixed V_bus. Useful for plotting motor
    /// torque-speed-efficiency contour maps + finding the peak-
    /// efficiency operating point.
    /// </summary>
    /// <param name="design">Validated motor design — the I_a field is
    /// overridden by each sweep sample.</param>
    /// <param name="currents_A">Sorted ascending array of I_a samples
    /// to evaluate. Each must be &gt; 0 and below the stall current
    /// V_bus / R_a.</param>
    /// <returns>One <see cref="MotorResult"/> per input sample.</returns>
    internal static MotorResult[] SolveEfficiencyMap(
        MotorDesign design,
        double[] currents_A)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(currents_A);
        design.ValidateSelf();
        if (currents_A.Length == 0)
            throw new ArgumentException("currents_A must contain at least one sample.",
                nameof(currents_A));

        var results = new MotorResult[currents_A.Length];
        for (int k = 0; k < currents_A.Length; k++)
        {
            double i = currents_A[k];
            if (i <= 0)
                throw new ArgumentException(
                    $"Sample {k} = {i} must be > 0.", nameof(currents_A));
            if (k > 0 && i < currents_A[k - 1])
                throw new ArgumentException(
                    "currents_A must be sorted ascending.", nameof(currents_A));
            results[k] = Solve(design with { ArmatureCurrent_A = i });
        }
        return results;
    }

    /// <summary>
    /// Compute the stall (ω → 0) torque for a given bus voltage. Public-
    /// static helper. τ_stall = K_t · V_bus / R_a.
    /// </summary>
    internal static double ComputeStallTorque(
        double busVoltage_V,
        double torqueConstant_NmA,
        double armatureResistance_Ohm)
    {
        if (busVoltage_V <= 0)
            throw new ArgumentOutOfRangeException(nameof(busVoltage_V),
                "V_bus must be > 0.");
        if (torqueConstant_NmA <= 0)
            throw new ArgumentOutOfRangeException(nameof(torqueConstant_NmA),
                "K_t must be > 0.");
        if (armatureResistance_Ohm <= 0)
            throw new ArgumentOutOfRangeException(nameof(armatureResistance_Ohm),
                "R_a must be > 0.");
        return torqueConstant_NmA * busVoltage_V / armatureResistance_Ohm;
    }
}
