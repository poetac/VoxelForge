// RadiationLossSolverTests.cs — Sprint E.1 acceptance tests for the
// Stefan-Boltzmann radiation solver.

using System;
using Voxelforge.ElectricPropulsion.Solvers;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class RadiationLossSolverTests
{
    [Fact]
    public void ChamberWallRadiation_AtOneSquareMeter_BlackBody1000K_MatchesStefanBoltzmann()
    {
        // q = ε σ A (T_w⁴ − T_∞⁴); for ε=1, A=1 m², T_w=1000 K, T_∞=3 K:
        //   σ T_w⁴ = 5.67e-8 · 1e12 = 5.67e4 W
        //   σ T_∞⁴ = 5.67e-8 · 81 ≈ 4.6e-6 W (negligible)
        double q = RadiationLossSolver.ChamberWallRadiation_W(
            emissivity:    1.0,
            surfaceArea_m2: 1.0,
            T_wall_K:       1000.0,
            T_ambient_K:    RadiationLossSolver.T_CosmicBackground_K);
        Assert.InRange(q, 5.6e4, 5.8e4);
    }

    [Fact]
    public void ChamberWallRadiation_ScalesWithEmissivity()
    {
        double q1 = RadiationLossSolver.ChamberWallRadiation_W(0.30, 0.01, 1500.0, 3.0);
        double q2 = RadiationLossSolver.ChamberWallRadiation_W(0.60, 0.01, 1500.0, 3.0);
        Assert.InRange(q2 / q1, 1.95, 2.05);  // 2× emissivity → 2× q
    }

    [Fact]
    public void ChamberWallRadiation_ScalesAsT4()
    {
        double q500  = RadiationLossSolver.ChamberWallRadiation_W(0.5, 0.01, 500.0,  3.0);
        double q1000 = RadiationLossSolver.ChamberWallRadiation_W(0.5, 0.01, 1000.0, 3.0);
        // Ratio should be ~16 (T⁴ scaling: 1000⁴ / 500⁴ = 16).
        Assert.InRange(q1000 / q500, 15.5, 16.5);
    }

    [Fact]
    public void ChamberWallRadiation_RejectsZeroEmissivity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RadiationLossSolver.ChamberWallRadiation_W(0.0, 0.01, 1000.0, 3.0));
    }

    [Fact]
    public void ChamberWallRadiation_RejectsNegativeArea()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RadiationLossSolver.ChamberWallRadiation_W(0.5, -0.01, 1000.0, 3.0));
    }

    [Fact]
    public void NozzleWallRadiation_ReturnsZeroWhenNotRadiativelyCooled()
    {
        double q = RadiationLossSolver.NozzleWallRadiation_W(
            isRadiativelyCooled: false,
            emissivity:          0.5,
            nozzleSurfaceArea_m2: 0.01,
            T_nozzleWall_K:       1500.0,
            T_ambient_K:          3.0);
        Assert.Equal(0.0, q);
    }

    [Fact]
    public void NozzleWallRadiation_NonZeroWhenRadiativelyCooled()
    {
        double q = RadiationLossSolver.NozzleWallRadiation_W(
            isRadiativelyCooled: true,
            emissivity:          0.5,
            nozzleSurfaceArea_m2: 0.01,
            T_nozzleWall_K:       1500.0,
            T_ambient_K:          3.0);
        Assert.True(q > 0);
    }

    [Fact]
    public void TotalRadiation_SumsBothSurfaces()
    {
        double q_chamber = RadiationLossSolver.ChamberWallRadiation_W(0.3, 0.001, 1200.0, 3.0);
        double q_nozzle  = RadiationLossSolver.NozzleWallRadiation_W(true, 0.5, 0.0005, 1500.0, 3.0);
        double q_total   = RadiationLossSolver.TotalRadiation_W(
            chamberEmissivity:        0.3,
            chamberSurfaceArea_m2:    0.001,
            T_chamberWall_K:          1200.0,
            nozzleRadiativelyCooled:  true,
            nozzleEmissivity:         0.5,
            nozzleSurfaceArea_m2:     0.0005,
            T_nozzleWall_K:           1500.0,
            T_ambient_K:              3.0);
        Assert.InRange(q_total, q_chamber + q_nozzle - 1e-6, q_chamber + q_nozzle + 1e-6);
    }
}
