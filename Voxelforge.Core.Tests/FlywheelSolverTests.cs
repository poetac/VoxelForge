// FlywheelSolverTests — pins the closed-form flywheel rotor math: I = α·m·R²,
// E = ½·I·ω², thin-rim hoop stress σ = ρ·ω²·R², burst speed √(σ_y/(ρ·R²)),
// the FW.W2 √SoC speed derating, and the bearing-drag auto-discharge time
// constant (exactly 1/dragFraction seconds at SoC = 1). Pure closed form →
// exact assertions. Backfills coverage onto the Linux 'core' CI leg (the
// existing Flywheel tests live in Voxelforge.Tests, net9.0-windows).

using System;
using Voxelforge.Flywheel;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class FlywheelSolverTests
{
    // Thin-rim Steel-4340 rotor: R = 0.5 m, m = 100 kg, N = 10 000 rpm.
    // Steel4340: σ_y = 690 MPa, ρ = 7850 kg/m³. α(ThinRim) = 1 → I = 25 kg·m².
    private static FlywheelDesign SteelRim()
        => new(FlywheelShape.ThinRim, FlywheelMaterial.Steel4340,
               OuterRadius_m: 0.5, Mass_kg: 100.0, RotationSpeed_rpm: 10_000.0);

    private static readonly double OmegaDesign = 2.0 * Math.PI * 10_000.0 / 60.0;

    [Fact]
    public void Solve_KinematicsAndStoredEnergy_MatchClosedForm()
    {
        var r = FlywheelSolver.Solve(SteelRim());
        Assert.Equal(25.0, r.MomentOfInertia_kgm2, 12);            // I = 1·100·0.5²
        Assert.Equal(OmegaDesign, r.AngularVelocity_rads, 9);      // 2π·N/60 ≈ 1047.1976
        Assert.Equal(OmegaDesign * 0.5, r.TipSpeed_ms, 9);         // v = ω·R
        double E = 0.5 * 25.0 * OmegaDesign * OmegaDesign;         // ≈ 13.708 MJ
        Assert.Equal(E, r.StoredEnergy_J, 6);
        Assert.Equal(E / 3.6e6, r.StoredEnergy_kWh, 9);
        Assert.Equal(E / 100.0 / 3600.0, r.SpecificEnergy_Wh_kg, 9);
    }

    [Fact]
    public void Solve_HoopStressAndBurstSpeed_MatchClosedForm()
    {
        var r = FlywheelSolver.Solve(SteelRim());
        // σ = ρ·ω²·R² ≈ 2.152 GPa (above yield — SF < 1 by construction here).
        Assert.Equal(7850.0 * OmegaDesign * OmegaDesign * 0.25, r.MaximumHoopStress_Pa, 4);
        double omegaBurst = Math.Sqrt(690e6 / (7850.0 * 0.25));
        Assert.Equal(omegaBurst * 60.0 / (2.0 * Math.PI), r.BurstSpeed_rpm, 9);
        Assert.Equal(omegaBurst / OmegaDesign, r.BurstSpeedSafetyFactor, 12);
        // Internal consistency: at ω_burst the hoop stress is exactly σ_yield,
        // so σ_hoop·SF² = σ_yield (SoC = 1).
        Assert.Equal(690e6,
            r.MaximumHoopStress_Pa * r.BurstSpeedSafetyFactor * r.BurstSpeedSafetyFactor, 2);
    }

    [Fact]
    public void Solve_MechanicalBearing_AutoDischargeIsInverseDragFraction()
    {
        // τ_drag = f·(E_max/ω_d); P = τ_drag·ω. At SoC = 1 (ω = ω_d):
        // τ_loss = E/P = 1/f = 1/0.01 = 100 s exactly, independent of the rotor.
        var r = FlywheelSolver.Solve(SteelRim());
        Assert.Equal(100.0, r.AutoDischargeTimeConstant_s, 9);
        double E = 0.5 * 25.0 * OmegaDesign * OmegaDesign;
        Assert.Equal(0.01 * (E / OmegaDesign), r.ParasiticDragTorque_Nm, 9);
    }

    [Fact]
    public void Solve_MagneticLevitation_CutsDragTwentyfold()
    {
        // MagLev drag fraction 5e-4 → τ_loss = 1/5e-4 = 2000 s at SoC = 1.
        var r = FlywheelSolver.Solve(SteelRim() with { Bearing = BearingType.MagneticLevitation });
        Assert.Equal(2000.0, r.AutoDischargeTimeConstant_s, 6);
    }

    [Fact]
    public void Solve_StateOfCharge_DeratesSpeedBySqrtAndEnergyLinearly()
    {
        var full = FlywheelSolver.Solve(SteelRim());
        var soc = FlywheelSolver.Solve(SteelRim() with { StateOfCharge = 0.25 });
        Assert.Equal(0.5 * OmegaDesign, soc.AngularVelocity_rads, 9);   // ω ∝ √SoC
        Assert.Equal(0.25 * full.StoredEnergy_J, soc.StoredEnergy_J, 6); // E ∝ SoC
        // Burst SF is an operating-life envelope — pinned to the DESIGN speed.
        Assert.Equal(full.BurstSpeedSafetyFactor, soc.BurstSpeedSafetyFactor, 12);
        // τ_loss = E/(τ_drag·ω) = √SoC/f = 0.5/0.01 = 50 s.
        Assert.Equal(50.0, soc.AutoDischargeTimeConstant_s, 9);
    }

    [Fact]
    public void Solve_SolidDisk_HalvesInertiaVersusThinRim()
    {
        var disk = FlywheelSolver.Solve(SteelRim() with { Shape = FlywheelShape.SolidDisk });
        var rim = FlywheelSolver.Solve(SteelRim());
        Assert.Equal(12.5, disk.MomentOfInertia_kgm2, 12);          // α = 0.5
        Assert.Equal(0.5 * rim.StoredEnergy_J, disk.StoredEnergy_J, 6);
    }

    [Fact]
    public void ComputeMaximumSpecificEnergy_IsShapeFactorTimesStrengthOverDensity()
    {
        // E/m = K·σ_y/ρ [J/kg] → Wh/kg. ThinRim steel: 0.5·690e6/7850/3600 ≈ 12.21.
        Assert.Equal(0.5 * 690e6 / 7850.0 / 3600.0,
            FlywheelSolver.ComputeMaximumSpecificEnergy(
                FlywheelMaterial.Steel4340, FlywheelShape.ThinRim), 9);
        // SolidDisk carbon fibre: 0.606·1000e6/1500/3600 ≈ 112.22 Wh/kg.
        Assert.Equal(0.606 * 1000e6 / 1500.0 / 3600.0,
            FlywheelSolver.ComputeMaximumSpecificEnergy(
                FlywheelMaterial.CarbonFibreComposite, FlywheelShape.SolidDisk), 9);
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FlywheelSolver.Solve(SteelRim() with { StateOfCharge = 1.5 }));
        Assert.Throws<ArgumentException>(() =>
            FlywheelSolver.Solve(SteelRim() with { Shape = FlywheelShape.None }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FlywheelSolver.Solve(SteelRim() with { OuterRadius_m = 0.0 }));
    }
}
