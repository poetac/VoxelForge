// StatefulFlywheelTests.cs — Sprint SI.W10 unit tests for the
// StatefulFlywheelComponent.

using System;
using Voxelforge.Flywheel;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class StatefulFlywheelTests
{
    [Fact]
    public void StatefulFlywheel_NoChargeBearingLosses_DrainsSoC()
    {
        // Mechanical bearing at no-charge: SoC drains via parasitic
        // drag. τ_loss ≈ 100 s for Beacon-Power-class composite at
        // 16,000 rpm. Over 50 s the SoC should drop noticeably.
        var net = new ComponentNetwork();
        var fw = new StatefulFlywheelComponent("fw", BeaconPowerClass(),
            initialStateOfCharge: 1.0);
        net.Add(fw);
        net.SetExternalInput("fw", "ChargePower_W", 0.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("fw", fw);
        var hist = integrator.Run(0.0, 50.0, 1.0);

        // SoC should drop from 1.0 by some perceptible margin (~ 30-60 %
        // for Beacon Power mechanical bearings over 50 s).
        double soc_final = hist[^1].PortValues["fw"]["StateOfCharge"];
        Assert.True(soc_final < 1.0);
        Assert.True(soc_final > 0.0);
    }

    [Fact]
    public void StatefulFlywheel_ChargingMatchesDrag_SoCStable()
    {
        // Apply charge power equal to the steady-state drag at full
        // SoC → SoC stays at 1.0.
        var design = BeaconPowerClass();
        var full = FlywheelSolver.Solve(design with { StateOfCharge = 1.0 });
        double dragAtFullSoC = full.ParasiticPowerLoss_W;

        var net = new ComponentNetwork();
        var fw = new StatefulFlywheelComponent("fw", design,
            initialStateOfCharge: 1.0);
        net.Add(fw);
        net.SetExternalInput("fw", "ChargePower_W", dragAtFullSoC);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("fw", fw);
        var hist = integrator.Run(0.0, 10.0, 0.5);

        // SoC at the end should still be ~ 1.0 (within 5 %).
        double soc_final = hist[^1].PortValues["fw"]["StateOfCharge"];
        Assert.InRange(soc_final, 0.95, 1.0);
    }

    [Fact]
    public void StatefulFlywheel_ChargingExceedsLossesAndRaisesSoC()
    {
        // Big charge power → SoC rises from 0.5 to ~ 1.0 (clamped).
        var net = new ComponentNetwork();
        var fw = new StatefulFlywheelComponent("fw", BeaconPowerClass(),
            initialStateOfCharge: 0.5);
        net.Add(fw);
        // Charge at 100 kW.
        net.SetExternalInput("fw", "ChargePower_W", 100_000.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("fw", fw);
        var hist = integrator.Run(0.0, 100.0, 1.0);
        double soc_final = hist[^1].PortValues["fw"]["StateOfCharge"];
        Assert.True(soc_final > 0.5);
    }

    [Fact]
    public void StatefulFlywheel_MagneticBearing_LongerDischargeTimeConstant()
    {
        // Same rotor with magnetic levitation bearings has much lower
        // drag → SoC drops slower than mechanical-bearing variant.
        var net_mech = new ComponentNetwork();
        var fw_mech = new StatefulFlywheelComponent("fw",
            BeaconPowerClass() with { Bearing = BearingType.Mechanical },
            initialStateOfCharge: 1.0);
        net_mech.Add(fw_mech);
        net_mech.SetExternalInput("fw", "ChargePower_W", 0.0);
        var it_mech = new TimeStepIntegrator(net_mech);
        it_mech.RegisterStateful("fw", fw_mech);
        var hist_mech = it_mech.Run(0.0, 20.0, 0.5);

        var net_mag = new ComponentNetwork();
        var fw_mag = new StatefulFlywheelComponent("fw",
            BeaconPowerClass() with { Bearing = BearingType.MagneticLevitation },
            initialStateOfCharge: 1.0);
        net_mag.Add(fw_mag);
        net_mag.SetExternalInput("fw", "ChargePower_W", 0.0);
        var it_mag = new TimeStepIntegrator(net_mag);
        it_mag.RegisterStateful("fw", fw_mag);
        var hist_mag = it_mag.Run(0.0, 20.0, 0.5);

        // Mechanical-bearing flywheel drains faster than maglev.
        Assert.True(hist_mech[^1].PortValues["fw"]["StateOfCharge"]
                  < hist_mag[^1].PortValues["fw"]["StateOfCharge"]);
    }

    [Fact]
    public void StatefulFlywheel_RejectsInvalidInitialSoc()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StatefulFlywheelComponent("f", BeaconPowerClass(), -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StatefulFlywheelComponent("f", BeaconPowerClass(),  1.5));
    }

    private static FlywheelDesign BeaconPowerClass() => new(
        Shape:              FlywheelShape.SolidDisk,
        Material:           FlywheelMaterial.CarbonFibreComposite,
        OuterRadius_m:      0.30,
        Mass_kg:           100.0,
        RotationSpeed_rpm: 16000.0);
}
