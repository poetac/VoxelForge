// EmWave2Tests.cs — Sprint EM.W2 unit tests for the efficiency-map
// sweep helper.

using System;
using System.Linq;
using Voxelforge.ElectricMotor;
using Xunit;

namespace Voxelforge.Tests.ElectricMotor;

public sealed class EmWave2Tests
{
    [Fact]
    public void Sweep_OutputLengthEqualsInputLength()
    {
        var samples = new[] { 50.0, 100.0, 200.0, 400.0, 700.0 };
        var map = MotorSolver.SolveEfficiencyMap(TeslaModelSDriveUnit_Cruise(), samples);
        Assert.Equal(samples.Length, map.Length);
    }

    [Fact]
    public void Sweep_TorqueLinearInI_AcrossMap()
    {
        var map = MotorSolver.SolveEfficiencyMap(
            TeslaModelSDriveUnit_Cruise(),
            new[] { 50.0, 100.0, 200.0 });
        // τ ∝ I, so map[1].τ / map[0].τ = 2 and map[2].τ / map[0].τ = 4.
        Assert.Equal(2.0, map[1].ShaftTorque_Nm / map[0].ShaftTorque_Nm, precision: 6);
        Assert.Equal(4.0, map[2].ShaftTorque_Nm / map[0].ShaftTorque_Nm, precision: 6);
    }

    [Fact]
    public void Sweep_EfficiencyHasInteriorPeak()
    {
        // η has a peak somewhere — at low I, the constant-loss term
        // dominates; at high I, copper-loss-squared dominates.
        var map = MotorSolver.SolveEfficiencyMap(
            TeslaModelSDriveUnit_Cruise(),
            new[] { 10.0, 50.0, 100.0, 200.0, 400.0, 700.0 });
        int peakIdx = 0;
        for (int k = 1; k < map.Length; k++)
            if (map[k].MotorEfficiency > map[peakIdx].MotorEfficiency)
                peakIdx = k;
        // The peak shouldn't be at the lowest or highest current.
        Assert.InRange(peakIdx, 1, map.Length - 2);
    }

    [Fact]
    public void Sweep_RejectsEmptyArray()
    {
        Assert.Throws<ArgumentException>(
            () => MotorSolver.SolveEfficiencyMap(
                TeslaModelSDriveUnit_Cruise(), Array.Empty<double>()));
    }

    [Fact]
    public void Sweep_RejectsUnsortedArray()
    {
        Assert.Throws<ArgumentException>(
            () => MotorSolver.SolveEfficiencyMap(
                TeslaModelSDriveUnit_Cruise(),
                new[] { 50.0, 200.0, 100.0 }));
    }

    [Fact]
    public void Sweep_RejectsNonPositiveSample()
    {
        Assert.Throws<ArgumentException>(
            () => MotorSolver.SolveEfficiencyMap(
                TeslaModelSDriveUnit_Cruise(),
                new[] { 0.0, 100.0 }));
    }

    private static MotorDesign TeslaModelSDriveUnit_Cruise() => new(
        Kind:                      MotorKind.PermanentMagnetSynchronous,
        TorqueConstant_NmA:        0.5,
        ArmatureResistance_Ohm:    0.05,
        ConstantPowerLoss_W:       500.0,
        BusVoltage_V:              400.0,
        ArmatureCurrent_A:         100.0);
}
