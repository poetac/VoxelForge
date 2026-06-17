// FlywheelWave2Tests.cs — Sprint FW.W2 unit tests for the bearing-loss
// + state-of-charge extensions.

using System;
using Voxelforge.Flywheel;
using Xunit;

namespace Voxelforge.Tests.Flywheel;

public sealed class FlywheelWave2Tests
{
    // ── FW.W1 bit-identity invariants ───────────────────────────────────

    [Fact]
    public void DefaultStateOfCharge_IsOne()
    {
        var d = BeaconPowerClass();
        Assert.Equal(1.0, d.StateOfCharge, precision: 9);
    }

    [Fact]
    public void DefaultBearing_IsMechanical()
    {
        var d = BeaconPowerClass();
        Assert.Equal(BearingType.Mechanical, d.Bearing);
    }

    [Fact]
    public void FW_W1_Baseline_StoredEnergyBitIdenticalAtSoC1()
    {
        // The FW.W1 baseline cluster band [1.0, 3.0] kWh must still hold
        // when StateOfCharge defaults to 1.0.
        var r = FlywheelSolver.Solve(BeaconPowerClass());
        Assert.InRange(r.StoredEnergy_kWh, 1.0, 3.0);
    }

    // ── State-of-charge scaling ─────────────────────────────────────────

    [Fact]
    public void StoredEnergy_LinearInStateOfCharge()
    {
        // E(SoC) = E_max · SoC. Half-SoC → half the stored energy.
        var full = FlywheelSolver.Solve(BeaconPowerClass() with { StateOfCharge = 1.0 });
        var half = FlywheelSolver.Solve(BeaconPowerClass() with { StateOfCharge = 0.5 });
        Assert.Equal(0.5, half.StoredEnergy_J / full.StoredEnergy_J, precision: 6);
    }

    [Fact]
    public void AngularVelocity_ScalesWithSqrtSoC()
    {
        // ω = ω_max · √SoC. Quarter-SoC → ω drops by factor of 2.
        var full    = FlywheelSolver.Solve(BeaconPowerClass() with { StateOfCharge = 1.0 });
        var quarter = FlywheelSolver.Solve(BeaconPowerClass() with { StateOfCharge = 0.25 });
        Assert.Equal(0.5,
            quarter.AngularVelocity_rads / full.AngularVelocity_rads, precision: 6);
    }

    [Fact]
    public void StoredEnergyAtZeroSoC_IsZero()
    {
        // SoC = 0 → rotor stopped → zero stored energy.
        var r = FlywheelSolver.Solve(BeaconPowerClass() with { StateOfCharge = 0.0 });
        Assert.Equal(0.0, r.StoredEnergy_J, precision: 9);
        Assert.Equal(0.0, r.AngularVelocity_rads, precision: 9);
    }

    [Fact]
    public void Validate_RejectsSoCOutOfBand()
    {
        // Numeric range failure -> ArgumentOutOfRangeException (#558 PR-F).
        Assert.Throws<ArgumentOutOfRangeException>(
            () => (BeaconPowerClass() with { StateOfCharge = -0.1 }).ValidateSelf());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => (BeaconPowerClass() with { StateOfCharge = 1.1 }).ValidateSelf());
    }

    // ── Burst-speed SF uses DESIGN speed, not SoC-derated ───────────────

    [Fact]
    public void BurstSpeedSafetyFactor_ReferencedToDesignSpeed_NotInstantaneous()
    {
        // The burst-stress envelope is the operating-life worst case;
        // SF must reflect the design max, not the partial-SoC operating
        // point. At SoC = 0.5, SF should equal the SoC = 1.0 SF.
        var full = FlywheelSolver.Solve(BeaconPowerClass() with { StateOfCharge = 1.0 });
        var half = FlywheelSolver.Solve(BeaconPowerClass() with { StateOfCharge = 0.5 });
        Assert.Equal(full.BurstSpeedSafetyFactor, half.BurstSpeedSafetyFactor, precision: 9);
    }

    // ── Bearing parasitic-drag scaling ──────────────────────────────────

    [Fact]
    public void Mechanical_HasHighestDragFraction()
    {
        Assert.True(FlywheelSolver.GetBearingDragFraction(BearingType.Mechanical)
                  > FlywheelSolver.GetBearingDragFraction(BearingType.MagneticLevitation));
        Assert.True(FlywheelSolver.GetBearingDragFraction(BearingType.MagneticLevitation)
                  > FlywheelSolver.GetBearingDragFraction(BearingType.SuperconductingMagneticLevitation));
    }

    [Fact]
    public void MagneticLevitation_HasLongerAutoDischargeTau_ThanMechanical()
    {
        // Lower drag → longer τ_loss.
        var mech = FlywheelSolver.Solve(BeaconPowerClass()
            with { Bearing = BearingType.Mechanical });
        var maglev = FlywheelSolver.Solve(BeaconPowerClass()
            with { Bearing = BearingType.MagneticLevitation });
        Assert.True(maglev.AutoDischargeTimeConstant_s > mech.AutoDischargeTimeConstant_s);
    }

    [Fact]
    public void SuperconductingMaglev_HasLongestAutoDischargeTau()
    {
        var maglev = FlywheelSolver.Solve(BeaconPowerClass()
            with { Bearing = BearingType.MagneticLevitation });
        var hts = FlywheelSolver.Solve(BeaconPowerClass()
            with { Bearing = BearingType.SuperconductingMagneticLevitation });
        Assert.True(hts.AutoDischargeTimeConstant_s > maglev.AutoDischargeTimeConstant_s);
    }

    [Fact]
    public void ParasiticPowerLoss_IsZeroAtSoCZero()
    {
        // At ω = 0, drag-power = τ·ω = 0.
        var r = FlywheelSolver.Solve(BeaconPowerClass()
            with { StateOfCharge = 0.0 });
        Assert.Equal(0.0, r.ParasiticPowerLoss_W, precision: 9);
    }

    [Fact]
    public void ParasiticDragTorque_PositiveAtSoCOne()
    {
        var r = FlywheelSolver.Solve(BeaconPowerClass());
        Assert.True(r.ParasiticDragTorque_Nm > 0);
    }

    [Fact]
    public void AutoDischargeTimeConstant_BeaconPowerCluster_InMinuteToHourBand()
    {
        // Mechanical bearings at ω ≈ 1675 rad/s + E ≈ 1.75 kWh → τ_loss
        // ≈ E / P_drag. Mechanical drag fraction 1 % of (E_max/ω_design).
        // Sanity: τ_loss = E / P_drag. P_drag ∝ ω; for ω = ω_max,
        // τ_drag = 0.01 · E_max / ω_design → P_drag = 0.01 · E_max.
        // → τ_loss = E_max / (0.01 · E_max) = 100 s ≈ 2 min.
        // Cluster band [60 s, 600 s] for mechanical bearings.
        var r = FlywheelSolver.Solve(BeaconPowerClass());
        Assert.InRange(r.AutoDischargeTimeConstant_s, 60.0, 600.0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static FlywheelDesign BeaconPowerClass() => new(
        Shape:              FlywheelShape.SolidDisk,
        Material:           FlywheelMaterial.CarbonFibreComposite,
        OuterRadius_m:      0.30,
        Mass_kg:           100.0,
        RotationSpeed_rpm: 16000.0);
}
