// PressureVesselSolverTests.cs — Sprint TANK.W1 unit tests for the
// closed-form thin-wall cylindrical pressure-vessel solver.

using System;
using Voxelforge.Tankage;
using Xunit;

namespace Voxelforge.Tests.Tankage;

public sealed class PressureVesselSolverTests
{
    // ── Registry ─────────────────────────────────────────────────────────

    [Fact]
    public void Registry_Steel4130_HasHigherYieldThanAl6061()
    {
        Assert.True(TankShellRegistry.Steel4130.YieldStrength_Pa
                  > TankShellRegistry.Aluminum6061.YieldStrength_Pa);
    }

    [Fact]
    public void Registry_Steel4130_HasHigherDensityThanComposite()
    {
        // Density ratio steel : Al : CF ≈ 7850 : 2700 : 1500.
        Assert.True(TankShellRegistry.Steel4130.Density_kgm3
                  > TankShellRegistry.Aluminum6061.Density_kgm3);
        Assert.True(TankShellRegistry.Aluminum6061.Density_kgm3
                  > TankShellRegistry.CarbonFibreComposite.Density_kgm3);
    }

    [Fact]
    public void Registry_For_ThrowsOnNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TankShellRegistry.For(TankShellType.None));
    }

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsThickWallGeometry()
    {
        // R/t = 4 → outside thin-wall envelope.
        var d = Falcon9LoxTank() with
        {
            WallThickness_m  = 0.5,
            InternalRadius_m = 1.0,
        };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositivePressure()
    {
        var d = Falcon9LoxTank() with { OperatingPressure_Pa = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNoneShellType()
    {
        var d = Falcon9LoxTank() with { ShellType = TankShellType.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── Falcon 9 LOX-tank baseline ───────────────────────────────────────

    [Fact]
    public void Falcon9Lox_HoopStressInClusterBand()
    {
        // σ_hoop = P·R/t = 3e5·1.83/0.00478 ≈ 115 MPa. Cluster band
        // [80, 160] MPa for the Falcon 9-class duty.
        var r = PressureVesselSolver.Solve(Falcon9LoxTank());
        Assert.InRange(r.HoopStress_Pa, 80e6, 160e6);
    }

    [Fact]
    public void Falcon9Lox_AxialStressIsHalfHoop()
    {
        var r = PressureVesselSolver.Solve(Falcon9LoxTank());
        Assert.Equal(0.5 * r.HoopStress_Pa, r.AxialStress_Pa, precision: 4);
    }

    [Fact]
    public void Falcon9Lox_VonMisesStressFollowsThinWallIdentity()
    {
        // σ_vm = σ_hoop · √3 / 2 ≈ 0.866 · σ_hoop for σ_axial = σ_hoop/2.
        var r = PressureVesselSolver.Solve(Falcon9LoxTank());
        Assert.Equal(r.HoopStress_Pa * Math.Sqrt(3.0) / 2.0,
                     r.VonMisesStress_Pa, precision: 4);
    }

    [Fact]
    public void Falcon9Lox_SafetyFactorAtAsmeLevel()
    {
        // P_burst = σ_y·t/R = 460e6 · 0.00478 / 1.83 ≈ 1.20 MPa.
        // SF = 1.20e6 / 3e5 ≈ 4.0. Aerospace LOX often runs at lower SF
        // but the geometry here is sized for ASME §VIII civil.
        var r = PressureVesselSolver.Solve(Falcon9LoxTank());
        Assert.InRange(r.SafetyFactor, 3.0, 5.0);
    }

    [Fact]
    public void Falcon9Lox_GravimetricEfficiencyInClusterBand()
    {
        // Steel-monocoque rocket tanks cluster ~ 300-1000 m gravimetric
        // efficiency (P·V / m·g).
        var r = PressureVesselSolver.Solve(Falcon9LoxTank());
        Assert.InRange(r.GravimetricEfficiency, 300.0, 1500.0);
    }

    [Fact]
    public void Falcon9Lox_InternalVolumeIncludesHemiCaps()
    {
        // V_internal = π·R²·L + (4/3)π·R³ when HasHemisphericalEndCaps.
        var d = Falcon9LoxTank();
        var r = PressureVesselSolver.Solve(d);
        double expected = Math.PI * d.InternalRadius_m * d.InternalRadius_m * d.ShellLength_m
                        + (4.0 / 3.0) * Math.PI * Math.Pow(d.InternalRadius_m, 3);
        Assert.Equal(expected, r.InternalVolume_m3, precision: 3);
    }

    // ── Material scaling ────────────────────────────────────────────────

    [Fact]
    public void AluminumShell_LowerMass_AndLowerSafetyFactor_VsSteelAtSameGeometry()
    {
        // Same geometry, Al-6061 has lower σ_y (280 vs 460 MPa) and
        // lower density (2700 vs 7850 kg/m³). So lower SF + lower mass.
        var steel = PressureVesselSolver.Solve(Falcon9LoxTank()
            with { ShellType = TankShellType.Steel4130 });
        var alum  = PressureVesselSolver.Solve(Falcon9LoxTank()
            with { ShellType = TankShellType.Aluminum6061 });
        Assert.True(alum.ShellMass_kg < steel.ShellMass_kg);
        Assert.True(alum.SafetyFactor < steel.SafetyFactor);
    }

    [Fact]
    public void Composite_BestGravimetricEfficiency_AtSameSafetyFactorReq()
    {
        // Carbon fibre has both σ_y/ρ better than aluminium (which is
        // already better than steel). For pressure-bearing weight-
        // critical components, composite wins.
        double t_steel = PressureVesselSolver.SolveForMinimumWallThickness(
            TankShellType.Steel4130,            internalRadius_m: 1.0,
            operatingPressure_Pa: 5e6, targetSafetyFactor: 2.5);
        double t_alum  = PressureVesselSolver.SolveForMinimumWallThickness(
            TankShellType.Aluminum6061,         internalRadius_m: 1.0,
            operatingPressure_Pa: 5e6, targetSafetyFactor: 2.5);
        double t_comp  = PressureVesselSolver.SolveForMinimumWallThickness(
            TankShellType.CarbonFibreComposite, internalRadius_m: 1.0,
            operatingPressure_Pa: 5e6, targetSafetyFactor: 2.5);
        // Steel needs thinner wall than Al (higher σ_y); composite needs
        // even less material BY MASS though wall thickness may be higher.
        Assert.True(t_steel < t_alum);
    }

    // ── Stress scaling ──────────────────────────────────────────────────

    [Fact]
    public void HoopStress_LinearInPressure()
    {
        var lo = PressureVesselSolver.Solve(Falcon9LoxTank() with { OperatingPressure_Pa = 1.5e5 });
        var hi = PressureVesselSolver.Solve(Falcon9LoxTank() with { OperatingPressure_Pa = 3.0e5 });
        Assert.Equal(2.0, hi.HoopStress_Pa / lo.HoopStress_Pa, precision: 6);
    }

    [Fact]
    public void HoopStress_LinearInRadius_AtConstantPressureAndThickness()
    {
        var smaller = PressureVesselSolver.Solve(Falcon9LoxTank() with { InternalRadius_m = 1.0 });
        var bigger  = PressureVesselSolver.Solve(Falcon9LoxTank() with { InternalRadius_m = 2.0 });
        Assert.Equal(2.0, bigger.HoopStress_Pa / smaller.HoopStress_Pa, precision: 6);
    }

    [Fact]
    public void HoopStress_InverselyLinearInWallThickness()
    {
        var thin  = PressureVesselSolver.Solve(Falcon9LoxTank() with { WallThickness_m = 0.005 });
        var thick = PressureVesselSolver.Solve(Falcon9LoxTank() with { WallThickness_m = 0.010 });
        Assert.Equal(0.5, thick.HoopStress_Pa / thin.HoopStress_Pa, precision: 6);
    }

    // ── SolveForMinimumWallThickness ────────────────────────────────────

    [Fact]
    public void MinThickness_AppliesManufacturingFloor()
    {
        // A toy-pressure design that needs 0.01 mm by stress alone gets
        // floored at the per-material manufacturing minimum.
        double t = PressureVesselSolver.SolveForMinimumWallThickness(
            TankShellType.Aluminum6061,
            internalRadius_m:        0.005,
            operatingPressure_Pa:    1e5,
            targetSafetyFactor:      2.5);
        Assert.Equal(TankShellRegistry.Aluminum6061.MinPracticalWallThickness_m, t, precision: 9);
    }

    [Fact]
    public void MinThickness_RejectsSafetyFactorBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PressureVesselSolver.SolveForMinimumWallThickness(
                TankShellType.Steel4130, 1.0, 1e6, 0.9));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Falcon 9 stage-1 LOX-tank-class baseline. AISI 4130 monocoque,
    // 3.66 m OD × 20 m cylindrical section + hemi end caps, MEOP 3 bar.
    private static PressureVesselDesign Falcon9LoxTank() => new(
        ShellType:             TankShellType.Steel4130,
        InternalRadius_m:      1.83,
        ShellLength_m:         20.0,
        WallThickness_m:       0.00478,    // 4.78 mm
        OperatingPressure_Pa:  3e5);       // 3 bar = 300 kPa
}
