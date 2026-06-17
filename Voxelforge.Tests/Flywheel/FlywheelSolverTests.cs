// FlywheelSolverTests.cs — Sprint FW.W1 unit tests for the closed-form
// flywheel-rotor performance snapshot.

using System;
using Voxelforge.Flywheel;
using Xunit;

namespace Voxelforge.Tests.Flywheel;

public sealed class FlywheelSolverTests
{
    // ── Registries ───────────────────────────────────────────────────────

    [Fact]
    public void MaterialRegistry_Composite_HigherSigmaPerDensity_ThanSteel()
    {
        var s = FlywheelMaterialRegistry.Steel4340;
        var c = FlywheelMaterialRegistry.CarbonFibreComposite;
        Assert.True(c.YieldStrength_Pa / c.Density_kgm3
                  > s.YieldStrength_Pa / s.Density_kgm3);
    }

    [Fact]
    public void MaterialRegistry_For_ThrowsOnNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FlywheelMaterialRegistry.For(FlywheelMaterial.None));
    }

    [Fact]
    public void ShapeFactors_SolidDiskHigherThanThinRim()
    {
        // Solid disk K = 0.606; thin rim K = 0.5.
        Assert.True(FlywheelShapeFactors.For(FlywheelShape.SolidDisk)
                  > FlywheelShapeFactors.For(FlywheelShape.ThinRim));
    }

    [Fact]
    public void ShapeFactors_MomentOfInertiaCoefficient_ThinRimEqualsOne()
    {
        // Thin rim α = 1 (I = m·R²); solid disk α = 0.5 (I = ½·m·R²).
        Assert.Equal(1.0, FlywheelShapeFactors.MomentOfInertiaCoefficient(FlywheelShape.ThinRim), precision: 6);
        Assert.Equal(0.5, FlywheelShapeFactors.MomentOfInertiaCoefficient(FlywheelShape.SolidDisk), precision: 6);
    }

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneShape()
    {
        var d = BeaconPowerClass() with { Shape = FlywheelShape.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroMass()
    {
        var d = BeaconPowerClass() with { Mass_kg = 0.0 };
        // Numeric range failure -> ArgumentOutOfRangeException (#558 PR-F).
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroSpeed()
    {
        var d = BeaconPowerClass() with { RotationSpeed_rpm = 0.0 };
        // Numeric range failure -> ArgumentOutOfRangeException (#558 PR-F).
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    // ── Beacon Power-class composite rotor baseline ──────────────────────

    [Fact]
    public void BeaconPower_StoredEnergyInClusterBand()
    {
        // Composite, R = 0.3 m, m = 100 kg, 16,000 rpm → E ≈ 1.75 kWh.
        // Cluster band [1.0, 3.0] kWh for this geometry.
        var r = FlywheelSolver.Solve(BeaconPowerClass());
        Assert.InRange(r.StoredEnergy_kWh, 1.0, 3.0);
    }

    [Fact]
    public void BeaconPower_SpecificEnergyInClusterBand()
    {
        // SE ≈ 17.5 Wh/kg — close to real Beacon Power Smart Energy 25
        // cluster (25 kWh / 1130 kg ≈ 22 Wh/kg). Cluster band [10, 50] Wh/kg.
        var r = FlywheelSolver.Solve(BeaconPowerClass());
        Assert.InRange(r.SpecificEnergy_Wh_kg, 10.0, 50.0);
    }

    [Fact]
    public void BeaconPower_TipSpeedBelowCompositeLimit()
    {
        // v_tip_max for CF composite ≈ sqrt(σ_y/ρ) = sqrt(1e9/1500)
        // ≈ 816 m/s. Beacon Power at R=0.3, 16,000 rpm → v_tip ≈ 503 m/s.
        var r = FlywheelSolver.Solve(BeaconPowerClass());
        Assert.True(r.TipSpeed_ms < 816.0);
        Assert.True(r.TipSpeed_ms > 300.0);   // anchor's cluster band
    }

    [Fact]
    public void BeaconPower_BurstSpeedSafetyFactor_AboveCriticalUnity()
    {
        // SF > 1 means design ω < burst ω. Composite at R=0.3 + 16k rpm
        // lands SF ≈ 1.6 (operational margin built in).
        var r = FlywheelSolver.Solve(BeaconPowerClass());
        Assert.True(r.BurstSpeedSafetyFactor > 1.0);
        Assert.True(r.BurstSpeedSafetyFactor < 3.0);
    }

    [Fact]
    public void BeaconPower_StoredEnergyMatchesHalfIOmegaSquared()
    {
        // E = ½·I·ω². Sanity-check the identity.
        var r = FlywheelSolver.Solve(BeaconPowerClass());
        Assert.Equal(0.5 * r.MomentOfInertia_kgm2 * r.AngularVelocity_rads * r.AngularVelocity_rads,
                     r.StoredEnergy_J, precision: 1);
    }

    [Fact]
    public void BeaconPower_MomentOfInertiaMatchesAlphaTimesMRsquared()
    {
        // I = α · m · R² with α = 0.5 for SolidDisk.
        var d = BeaconPowerClass();
        var r = FlywheelSolver.Solve(d);
        double expected = 0.5 * d.Mass_kg * d.OuterRadius_m * d.OuterRadius_m;
        Assert.Equal(expected, r.MomentOfInertia_kgm2, precision: 6);
    }

    // ── Material / shape comparison ─────────────────────────────────────

    [Fact]
    public void Composite_HigherBurstSafetyFactor_ThanSteel_AtSameGeometry()
    {
        var steel    = FlywheelSolver.Solve(BeaconPowerClass()
            with { Material = FlywheelMaterial.Steel4340 });
        var composite = FlywheelSolver.Solve(BeaconPowerClass()
            with { Material = FlywheelMaterial.CarbonFibreComposite });
        // Higher σ_y/ρ → higher burst speed → higher SF.
        Assert.True(composite.BurstSpeedSafetyFactor > steel.BurstSpeedSafetyFactor);
    }

    [Fact]
    public void SolidDisk_LowerMomentOfInertia_ThanThinRimAtSameMassAndRadius()
    {
        // I_rim = m·R² > I_disk = 0.5·m·R².
        var rim  = FlywheelSolver.Solve(BeaconPowerClass() with { Shape = FlywheelShape.ThinRim });
        var disk = FlywheelSolver.Solve(BeaconPowerClass() with { Shape = FlywheelShape.SolidDisk });
        Assert.True(disk.MomentOfInertia_kgm2 < rim.MomentOfInertia_kgm2);
    }

    [Fact]
    public void ThinRim_StoresMoreEnergy_ThanSolidDiskAtSameMassRadiusSpeed()
    {
        // I_rim · ω² > I_disk · ω² since I_rim > I_disk.
        var rim  = FlywheelSolver.Solve(BeaconPowerClass() with { Shape = FlywheelShape.ThinRim });
        var disk = FlywheelSolver.Solve(BeaconPowerClass() with { Shape = FlywheelShape.SolidDisk });
        Assert.True(rim.StoredEnergy_J > disk.StoredEnergy_J);
    }

    // ── Scaling sanity ──────────────────────────────────────────────────

    [Fact]
    public void StoredEnergy_QuadraticInRotationSpeed()
    {
        // E ∝ ω².
        var lo = FlywheelSolver.Solve(BeaconPowerClass() with { RotationSpeed_rpm = 8000  });
        var hi = FlywheelSolver.Solve(BeaconPowerClass() with { RotationSpeed_rpm = 16000 });
        Assert.Equal(4.0, hi.StoredEnergy_J / lo.StoredEnergy_J, precision: 4);
    }

    [Fact]
    public void HoopStress_QuadraticInRotationSpeedAndRadius()
    {
        // σ = ρ·ω²·R². Doubling ω at constant R → 4× σ.
        var lo = FlywheelSolver.Solve(BeaconPowerClass() with { RotationSpeed_rpm = 8000  });
        var hi = FlywheelSolver.Solve(BeaconPowerClass() with { RotationSpeed_rpm = 16000 });
        Assert.Equal(4.0, hi.MaximumHoopStress_Pa / lo.MaximumHoopStress_Pa, precision: 4);
    }

    // ── ComputeMaximumSpecificEnergy ────────────────────────────────────

    [Fact]
    public void MaxSpecificEnergy_CompositeBeatsSteel()
    {
        // Theoretical upper-bound specific energy: composite > steel
        // because (σ/ρ)·K is much higher for CF.
        double se_steel    = FlywheelSolver.ComputeMaximumSpecificEnergy(
            FlywheelMaterial.Steel4340, FlywheelShape.SolidDisk);
        double se_composite = FlywheelSolver.ComputeMaximumSpecificEnergy(
            FlywheelMaterial.CarbonFibreComposite, FlywheelShape.SolidDisk);
        Assert.True(se_composite > se_steel);
        // Composite max SE cluster band [50, 200] Wh/kg.
        Assert.InRange(se_composite, 50.0, 200.0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Beacon Power-class CF-composite rotor baseline. R = 0.3 m, m = 100 kg,
    // 16,000 rpm. Solid disk topology, carbon-fibre composite. Lands E ≈
    // 1.75 kWh, SE ≈ 17.5 Wh/kg, v_tip ≈ 503 m/s, SF ≈ 1.6.
    private static FlywheelDesign BeaconPowerClass() => new(
        Shape:              FlywheelShape.SolidDisk,
        Material:           FlywheelMaterial.CarbonFibreComposite,
        OuterRadius_m:      0.30,
        Mass_kg:           100.0,
        RotationSpeed_rpm: 16000.0);
}
