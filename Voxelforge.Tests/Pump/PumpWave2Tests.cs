// PumpWave2Tests.cs — Sprint PMP.W2 unit tests for the positive-
// displacement extension.

using System;
using Voxelforge.Pump;
using Xunit;

namespace Voxelforge.Tests.Pump;

public sealed class PumpWave2Tests
{
    [Fact]
    public void PositiveDisplacement_FlowEqualsDisplacementTimesSpeedTimesEta()
    {
        // 100 mL/rev = 1e-4 m³/rev, 1800 rpm, η_vol = 0.95.
        // Q = 1e-4 · 1800/60 · 0.95 = 1e-4 · 30 · 0.95 = 0.00285 m³/s.
        double Q = CentrifugalPumpSolver.ComputePositiveDisplacementFlow(
            displacementPerRevolution_m3: 1e-4,
            rotationSpeed_rpm:            1800.0,
            volumetricEfficiency:         0.95);
        Assert.Equal(0.00285, Q, precision: 9);
    }

    [Fact]
    public void PositiveDisplacement_FlowLinearInSpeed()
    {
        double Q_lo = CentrifugalPumpSolver.ComputePositiveDisplacementFlow(1e-4, 1800);
        double Q_hi = CentrifugalPumpSolver.ComputePositiveDisplacementFlow(1e-4, 3600);
        Assert.Equal(2.0, Q_hi / Q_lo, precision: 6);
    }

    [Fact]
    public void PositiveDisplacement_FlowLinearInDisplacement()
    {
        double Q_small = CentrifugalPumpSolver.ComputePositiveDisplacementFlow(1e-4, 1800);
        double Q_big   = CentrifugalPumpSolver.ComputePositiveDisplacementFlow(2e-4, 1800);
        Assert.Equal(2.0, Q_big / Q_small, precision: 6);
    }

    [Fact]
    public void PositiveDisplacement_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CentrifugalPumpSolver.ComputePositiveDisplacementFlow(0.0, 1800));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CentrifugalPumpSolver.ComputePositiveDisplacementFlow(1e-4, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CentrifugalPumpSolver.ComputePositiveDisplacementFlow(1e-4, 1800, 1.5));
    }

    [Fact]
    public void Validate_AcceptsPositiveDisplacement()
    {
        var d = new CentrifugalPumpDesign(
            Kind:                    PumpKind.PositiveDisplacement,
            VolumetricFlowRate_m3s:  0.00285,
            HeadRise_m:              30.0,
            RotationSpeed_rpm:       1800.0,
            OverallEfficiency:       0.80);
        d.ValidateSelf();    // must not throw
    }
}
