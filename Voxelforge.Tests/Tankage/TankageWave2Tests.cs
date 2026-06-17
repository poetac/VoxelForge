// TankageWave2Tests.cs — Sprint TANK.W2 unit tests for the thick-wall
// Lamé hoop-stress helper.

using System;
using Voxelforge.Tankage;
using Xunit;

namespace Voxelforge.Tests.Tankage;

public sealed class TankageWave2Tests
{
    [Fact]
    public void ThickWall_AmplifiesHoopStress_VsThinWall()
    {
        // R = 0.1 m, t = 0.05 m → R/t = 2 (well within thick-wall).
        //   Thin-wall σ = PR/t = 1e6 · 0.1 / 0.05 = 2 MPa.
        //   Lamé σ = P·(R_o² + R_i²)/(R_o² − R_i²) = 1e6·(0.0225+0.01)/(0.0225−0.01)
        //          = 1e6·0.0325/0.0125 = 2.6 MPa.
        double sigma = PressureVesselSolver.ComputeThickWallHoopStress(
            internalRadius_m:      0.1,
            wallThickness_m:       0.05,
            operatingPressure_Pa:  1e6);
        double thin_wall_approx = 1e6 * 0.1 / 0.05;    // = 2 MPa
        Assert.True(sigma > thin_wall_approx);
        // Lamé / thin-wall ratio at R/t = 2 should be ≈ 1.3.
        double ratio = sigma / thin_wall_approx;
        Assert.InRange(ratio, 1.2, 1.5);
    }

    [Fact]
    public void ThickWall_ApproachesThinWall_AtLargeRoverT()
    {
        // R = 1.0 m, t = 0.005 m → R/t = 200 (squarely thin-wall).
        //   Thin-wall σ = 1e6 · 1.0 / 0.005 = 200 MPa.
        //   Lamé should be very close.
        double sigma = PressureVesselSolver.ComputeThickWallHoopStress(
            internalRadius_m:      1.0,
            wallThickness_m:       0.005,
            operatingPressure_Pa:  1e6);
        double thin_wall_approx = 200e6;
        Assert.InRange(sigma / thin_wall_approx, 0.99, 1.02);
    }

    [Fact]
    public void ThickWall_RejectsNonPositiveInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PressureVesselSolver.ComputeThickWallHoopStress(0.0, 0.05, 1e6));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PressureVesselSolver.ComputeThickWallHoopStress(0.1, 0.0, 1e6));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PressureVesselSolver.ComputeThickWallHoopStress(0.1, 0.05, 0.0));
    }

    [Fact]
    public void ThickWall_HoopStressLinearInPressure()
    {
        double sigma_low  = PressureVesselSolver.ComputeThickWallHoopStress(0.1, 0.05, 1e6);
        double sigma_high = PressureVesselSolver.ComputeThickWallHoopStress(0.1, 0.05, 2e6);
        Assert.Equal(2.0, sigma_high / sigma_low, precision: 9);
    }
}
