// PressureVesselSolverTests — pins the safety-critical closed-form pressure-
// vessel math (hoop / axial / von-Mises stress, burst pressure, safety factor,
// internal volume, thin-wall sizing, Lamé thick-wall) on the cross-platform
// Linux CI leg. The solver is pure closed form with no calibration constants,
// so every assertion is exact. Backfills coverage that previously existed only
// in Voxelforge.Tests (net9.0-windows → the offline self-hosted runner).

using System;
using Voxelforge.Tankage;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class PressureVesselSolverTests
{
    // Thin-wall AISI-4130 steel cylinder: R = 0.5 m, t = 5 mm (R/t = 100 ✓),
    // L = 2 m, MEOP = 2 MPa, hemispherical end caps. Steel4130: σ_yield = 460 MPa.
    private static PressureVesselDesign SteelCylinder()
        => new(TankShellType.Steel4130,
               InternalRadius_m:     0.5,
               ShellLength_m:        2.0,
               WallThickness_m:      0.005,
               OperatingPressure_Pa: 2.0e6);

    [Fact]
    public void Solve_ThinWallStresses_MatchClosedForm()
    {
        var r = PressureVesselSolver.Solve(SteelCylinder());
        Assert.Equal(2.0e8, r.HoopStress_Pa,  0);                  // σ_h = P·R/t
        Assert.Equal(1.0e8, r.AxialStress_Pa, 0);                  // σ_a = P·R/2t
        Assert.Equal(0.5 * r.HoopStress_Pa, r.AxialStress_Pa, 6);  // σ_a = σ_h / 2
        // σ_vm = σ_h·√3/2 for the thin-wall σ_h = 2·σ_a state.
        Assert.Equal(2.0e8 * Math.Sqrt(3.0) / 2.0, r.VonMisesStress_Pa, 1);
    }

    [Fact]
    public void Solve_BurstPressureAndSafetyFactor_MatchClosedForm()
    {
        var r = PressureVesselSolver.Solve(SteelCylinder());
        // P_burst = σ_yield·t/R = 460e6·0.005/0.5 = 4.6 MPa; SF = P_burst/P = 2.3.
        Assert.Equal(4.6e6, r.BurstPressure_Pa, 0);
        Assert.Equal(2.3,   r.SafetyFactor,     9);
    }

    [Fact]
    public void Solve_InternalVolume_IncludesHemisphericalCaps()
    {
        var r = PressureVesselSolver.Solve(SteelCylinder());
        // π·R²·L (cylinder) + (4/3)·π·R³ (two hemi caps = one sphere).
        double expected = Math.PI * 0.5 * 0.5 * 2.0
                        + (4.0 / 3.0) * Math.PI * 0.5 * 0.5 * 0.5;
        Assert.Equal(expected, r.InternalVolume_m3, 9);
    }

    [Fact]
    public void SolveForMinimumWallThickness_InvertsTheSafetyFactor()
    {
        // t_min = SF·P·R/σ_yield = 2.3·2e6·0.5/460e6 = 5 mm — round-trips the
        // SteelCylinder design (SF 2.3 ↔ t 5 mm), tying the sizing helper to Solve.
        double t = PressureVesselSolver.SolveForMinimumWallThickness(
            TankShellType.Steel4130,
            internalRadius_m:     0.5,
            operatingPressure_Pa: 2.0e6,
            targetSafetyFactor:   2.3);
        Assert.Equal(0.005, t, 9);
    }

    [Fact]
    public void Solve_BelowThinWallEnvelope_Throws()
    {
        // R/t = 0.5/0.1 = 5 < 10 — outside the thin-wall validity envelope.
        var thick = new PressureVesselDesign(
            TankShellType.Steel4130,
            InternalRadius_m:     0.5,
            ShellLength_m:        2.0,
            WallThickness_m:      0.1,
            OperatingPressure_Pa: 2.0e6);
        Assert.Throws<ArgumentException>(() => PressureVesselSolver.Solve(thick));
    }

    [Fact]
    public void ComputeThickWallHoopStress_MatchesLame()
    {
        // σ_hoop_max = P·(R_o²+R_i²)/(R_o²−R_i²); R_i=0.1, t=0.05 → R_o=0.15.
        // 10e6·(0.0225+0.01)/(0.0225−0.01) = 10e6·2.6 = 2.6e7 Pa (inner wall).
        double s = PressureVesselSolver.ComputeThickWallHoopStress(
            internalRadius_m:     0.1,
            wallThickness_m:      0.05,
            operatingPressure_Pa: 10.0e6);
        Assert.Equal(2.6e7, s, 0);
    }
}
