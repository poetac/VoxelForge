// MotorSolverTests — pins the ideal-DC-machine closed forms: τ = K_t·I,
// back-EMF Kirchhoff balance V_emf = V_bus − I·R, ω = V_emf/K_e, the exact
// electrical bookkeeping P_in = P_mech + P_cu, no-load speed and stall
// torque, and the EM.W2 efficiency-map sweep contract. Pure closed form →
// exact assertions. Backfills coverage onto the Linux 'core' CI leg.

using System;
using Voxelforge.ElectricMotor;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class MotorSolverTests
{
    // EV-traction-class PMSM: K_t = 0.5 N·m/A, R_a = 0.05 Ω,
    // P_loss = 100 W, V_bus = 400 V, I_a = 100 A.
    private static MotorDesign TractionPmsm()
        => new(MotorKind.PermanentMagnetSynchronous,
               TorqueConstant_NmA: 0.5, ArmatureResistance_Ohm: 0.05,
               ConstantPowerLoss_W: 100.0, BusVoltage_V: 400.0,
               ArmatureCurrent_A: 100.0);

    [Fact]
    public void Solve_TorqueSpeedAndPower_MatchClosedForm()
    {
        var r = MotorSolver.Solve(TractionPmsm());
        Assert.Equal(50.0, r.ShaftTorque_Nm, 12);              // K_t·I
        Assert.Equal(395.0, r.BackEmf_V, 12);                  // 400 − 100·0.05
        Assert.Equal(790.0, r.AngularVelocity_rads, 12);       // V_emf/K_e
        Assert.Equal(790.0 * 60.0 / (2.0 * Math.PI), r.RotationSpeed_rpm, 9);
        Assert.Equal(39_500.0, r.MechanicalPower_W, 9);        // τ·ω
        Assert.Equal(500.0, r.CopperLoss_W, 12);               // I²·R
        Assert.Equal(40_000.0, r.ElectricalPowerInput_W, 9);   // V·I
        Assert.Equal(0.985, r.MotorEfficiency, 12);            // (39500−100)/40000
    }

    [Fact]
    public void Solve_ElectricalBookkeeping_InputSplitsExactlyIntoShaftAndCopper()
    {
        // V·I = τ·ω + I²·R is an algebraic identity of the ideal DC machine.
        var r = MotorSolver.Solve(TractionPmsm());
        Assert.Equal(r.ElectricalPowerInput_W, r.MechanicalPower_W + r.CopperLoss_W, 9);
    }

    [Fact]
    public void NoLoadSpeedAndStallTorque_BoundTheOperatingEnvelope()
    {
        Assert.Equal(800.0, MotorSolver.ComputeNoLoadAngularVelocity(400.0, 0.5), 12);
        Assert.Equal(4000.0, MotorSolver.ComputeStallTorque(400.0, 0.5, 0.05), 9);
        // The loaded operating point sits inside both bounds.
        var r = MotorSolver.Solve(TractionPmsm());
        Assert.True(r.AngularVelocity_rads < 800.0);
        Assert.True(r.ShaftTorque_Nm < 4000.0);
    }

    [Fact]
    public void Solve_CurrentBeyondStall_ThrowsInsteadOfNegativeSpeed()
        // I·R ≥ V_bus → non-positive back-EMF: the motor cannot turn.
        => Assert.Throws<InvalidOperationException>(() =>
               MotorSolver.Solve(TractionPmsm() with { ArmatureCurrent_A = 8000.0 }));

    [Fact]
    public void SolveEfficiencyMap_EvaluatesEachSampleLikeSolve()
    {
        var map = MotorSolver.SolveEfficiencyMap(TractionPmsm(), new[] { 50.0, 100.0, 200.0 });
        Assert.Equal(3, map.Length);
        // Middle sample must be bit-identical to a direct Solve at I = 100.
        var direct = MotorSolver.Solve(TractionPmsm());
        Assert.Equal(direct.MotorEfficiency, map[1].MotorEfficiency, 15);
        Assert.Equal(direct.MechanicalPower_W, map[1].MechanicalPower_W, 15);
        // Torque grows with current; speed falls (IR drop).
        Assert.True(map[2].ShaftTorque_Nm > map[0].ShaftTorque_Nm);
        Assert.True(map[2].AngularVelocity_rads < map[0].AngularVelocity_rads);
    }

    [Fact]
    public void SolveEfficiencyMap_RejectsUnsortedOrEmptyOrNonPositiveSamples()
    {
        Assert.Throws<ArgumentException>(() =>
            MotorSolver.SolveEfficiencyMap(TractionPmsm(), Array.Empty<double>()));
        Assert.Throws<ArgumentException>(() =>
            MotorSolver.SolveEfficiencyMap(TractionPmsm(), new[] { 100.0, 50.0 }));
        Assert.Throws<ArgumentException>(() =>
            MotorSolver.SolveEfficiencyMap(TractionPmsm(), new[] { 0.0, 50.0 }));
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() =>
            MotorSolver.Solve(TractionPmsm() with { ArmatureCurrent_A = 0.0 }));
        Assert.Throws<ArgumentException>(() =>
            MotorSolver.Solve(TractionPmsm() with { Kind = MotorKind.None }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MotorSolver.ComputeStallTorque(400.0, 0.5, 0.0));
    }
}
