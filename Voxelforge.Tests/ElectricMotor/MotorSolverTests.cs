// MotorSolverTests.cs — Sprint EM.W1 unit tests for the closed-form
// BLDC / PMSM electric-motor performance snapshot.

using System;
using Voxelforge.ElectricMotor;
using Xunit;

namespace Voxelforge.Tests.ElectricMotor;

public sealed class MotorSolverTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneKind()
    {
        var d = TeslaModelSDriveUnit_Cruise() with { Kind = MotorKind.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositiveTorqueConstant()
    {
        var d = TeslaModelSDriveUnit_Cruise() with { TorqueConstant_NmA = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositiveArmatureCurrent()
    {
        var d = TeslaModelSDriveUnit_Cruise() with { ArmatureCurrent_A = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositiveBusVoltage()
    {
        var d = TeslaModelSDriveUnit_Cruise() with { BusVoltage_V = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Solve_ThrowsWhenStalled_ITimesRExceedsBusVoltage()
    {
        // I_a · R_a > V_bus → V_emf < 0 → motor cannot turn (stall
        // condition exceeded).
        var d = TeslaModelSDriveUnit_Cruise() with
        {
            ArmatureCurrent_A = 10_000.0,    // 10 kA on R = 0.05 Ω = 500 V > 400 V V_bus
        };
        Assert.Throws<InvalidOperationException>(() => MotorSolver.Solve(d));
    }

    // ── Tesla Model S Drive Unit cruise baseline ────────────────────────

    [Fact]
    public void TeslaCruise_TorqueMatchesKtTimesCurrent()
    {
        // τ = K_t · I_a = 0.5 · 100 = 50 N·m.
        var r = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise());
        Assert.Equal(50.0, r.ShaftTorque_Nm, precision: 6);
    }

    [Fact]
    public void TeslaCruise_RotationSpeedInClusterBand()
    {
        // V_emf = 400 − 100·0.05 = 395 V; ω = 395/0.5 = 790 rad/s ≈
        // 7544 rpm. Cluster band [5000, 9000] rpm.
        var r = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise());
        Assert.InRange(r.RotationSpeed_rpm, 5000.0, 9000.0);
    }

    [Fact]
    public void TeslaCruise_MechanicalPowerInClusterBand()
    {
        // P_mech ≈ 50·790 = 39.5 kW. Tesla cruise ops cluster [25, 50] kW.
        var r = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise());
        Assert.InRange(r.MechanicalPower_W, 25_000.0, 50_000.0);
    }

    [Fact]
    public void TeslaCruise_EfficiencyInClusterBand()
    {
        // η ≈ (39.5 − 0.5) / 40 = 0.975. Cruise ops cluster [0.95, 0.99].
        var r = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise());
        Assert.InRange(r.MotorEfficiency, 0.94, 0.99);
    }

    [Fact]
    public void TeslaCruise_EnergyBalance_PinEqualsPmechPlusLosses()
    {
        // P_in ≈ P_mech + P_cu + P_loss_const (within rounding).
        var d = TeslaModelSDriveUnit_Cruise();
        var r = MotorSolver.Solve(d);
        double sum = r.MechanicalPower_W + r.CopperLoss_W;
        // P_in = V·I, P_mech includes only τ·ω. The loss-balance check
        // is approximate (we lump bearing/iron via P_loss_const which
        // doesn't appear here). At cruise: 40 kW in; 39.5 kW mech out;
        // 0.5 kW Cu loss. Sum ≈ 40 kW.
        Assert.Equal(r.ElectricalPowerInput_W, sum, precision: 1);
    }

    [Fact]
    public void TeslaCruise_BackEmfBoundedByBusVoltage()
    {
        var r = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise());
        Assert.True(r.BackEmf_V < TeslaModelSDriveUnit_Cruise().BusVoltage_V);
        Assert.True(r.BackEmf_V > 0);
    }

    // ── Tesla peak-power ops ────────────────────────────────────────────

    [Fact]
    public void TeslaPeak_MechanicalPowerInClusterBand()
    {
        // Peak ops at I_a = 700 A → τ ≈ 350 N·m; V_emf = 400 − 35 = 365 V;
        // ω = 730 rad/s; P_mech ≈ 255 kW. Tesla Model S advertised
        // peak 270 kW (single drive unit on dual-motor configurations).
        var d = TeslaModelSDriveUnit_Cruise() with { ArmatureCurrent_A = 700.0 };
        var r = MotorSolver.Solve(d);
        Assert.InRange(r.MechanicalPower_W, 200_000.0, 300_000.0);
    }

    [Fact]
    public void TeslaPeak_EfficiencyDropsVsCruise_DueToCopperLoss()
    {
        // P_cu = I²·R scales quadratically; at 7× cruise current the
        // copper loss is 49× higher → efficiency drops from ~ 97 % to
        // ~ 91 %.
        var cruise = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise());
        var peak   = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise()
            with { ArmatureCurrent_A = 700.0 });
        Assert.True(peak.MotorEfficiency < cruise.MotorEfficiency,
            $"Peak η ({peak.MotorEfficiency:F4}) expected < cruise η "
          + $"({cruise.MotorEfficiency:F4}).");
    }

    // ── Linear-machine identities ────────────────────────────────────────

    [Fact]
    public void Torque_LinearInArmatureCurrent()
    {
        var lo = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise() with { ArmatureCurrent_A = 50.0 });
        var hi = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise() with { ArmatureCurrent_A = 100.0 });
        Assert.Equal(2.0, hi.ShaftTorque_Nm / lo.ShaftTorque_Nm, precision: 6);
    }

    [Fact]
    public void CopperLoss_QuadraticInArmatureCurrent()
    {
        var lo = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise() with { ArmatureCurrent_A = 50.0 });
        var hi = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise() with { ArmatureCurrent_A = 100.0 });
        // P_cu = I²·R → 4× when I doubles.
        Assert.Equal(4.0, hi.CopperLoss_W / lo.CopperLoss_W, precision: 6);
    }

    [Fact]
    public void ElectricalPowerInput_LinearInBusVoltage_AtConstantCurrent()
    {
        var lo = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise() with { BusVoltage_V = 200 });
        var hi = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise() with { BusVoltage_V = 400 });
        Assert.Equal(2.0,
            hi.ElectricalPowerInput_W / lo.ElectricalPowerInput_W,
            precision: 6);
    }

    [Fact]
    public void RotationSpeedIncreasesWithBusVoltage()
    {
        var loV = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise() with { BusVoltage_V = 200 });
        var hiV = MotorSolver.Solve(TeslaModelSDriveUnit_Cruise() with { BusVoltage_V = 400 });
        Assert.True(hiV.RotationSpeed_rpm > loV.RotationSpeed_rpm);
    }

    // ── ComputeNoLoadAngularVelocity / ComputeStallTorque helpers ───────

    [Fact]
    public void NoLoadAngularVelocity_EqualsVbusOverKt()
    {
        // At no load (I_a → 0): ω = V_bus / K_t.
        double omega_nl = MotorSolver.ComputeNoLoadAngularVelocity(
            busVoltage_V: 400.0,
            torqueConstant_NmA: 0.5);
        Assert.Equal(800.0, omega_nl, precision: 6);
    }

    [Fact]
    public void StallTorque_EqualsKtTimesVbusOverRa()
    {
        // At stall (ω → 0): τ_stall = K_t · V_bus / R_a.
        double tau_stall = MotorSolver.ComputeStallTorque(
            busVoltage_V: 400.0,
            torqueConstant_NmA: 0.5,
            armatureResistance_Ohm: 0.05);
        Assert.Equal(0.5 * 400.0 / 0.05, tau_stall, precision: 6);
    }

    [Fact]
    public void Helpers_RejectNonPositiveInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MotorSolver.ComputeNoLoadAngularVelocity(0, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MotorSolver.ComputeStallTorque(400, 0.5, 0.0));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Tesla Model S Drive Unit-class baseline at cruise ops. PMSM, 400 V
    // bus, K_t ≈ 0.5 N·m/A, R_a ≈ 0.05 Ω, 100 A operating current,
    // 500 W constant iron+friction loss. Lands ω ≈ 7544 rpm, P_mech
    // ≈ 39.5 kW, η ≈ 97.5 %.
    private static MotorDesign TeslaModelSDriveUnit_Cruise() => new(
        Kind:                      MotorKind.PermanentMagnetSynchronous,
        TorqueConstant_NmA:        0.5,
        ArmatureResistance_Ohm:    0.05,
        ConstantPowerLoss_W:       500.0,
        BusVoltage_V:              400.0,
        ArmatureCurrent_A:         100.0);
}
