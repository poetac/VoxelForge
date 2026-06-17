// HeliconIcrhMagneticNozzleModelTests.cs — Sprint EP.W4 phase 2 unit
// tests for the parameterized 3-stage VASIMR physics.

using System;
using Voxelforge.ElectricPropulsion.Solvers;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class HeliconIcrhMagneticNozzleModelTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Solve_NonPositiveHeliconPower_Throws() =>
        Assert.Throws<ArgumentException>(() => HeliconIcrhMagneticNozzleModel.Solve(
            0.0, 170000.0, 2.0, 100.0, 1.0e-4));

    [Fact]
    public void Solve_NaNHeliconPower_Throws() =>
        Assert.Throws<ArgumentException>(() => HeliconIcrhMagneticNozzleModel.Solve(
            double.NaN, 170000.0, 2.0, 100.0, 1.0e-4));

    [Fact]
    public void Solve_NonPositiveIcrhPower_Throws() =>
        Assert.Throws<ArgumentException>(() => HeliconIcrhMagneticNozzleModel.Solve(
            30000.0, 0.0, 2.0, 100.0, 1.0e-4));

    [Fact]
    public void Solve_NonPositiveSolenoidField_Throws() =>
        Assert.Throws<ArgumentException>(() => HeliconIcrhMagneticNozzleModel.Solve(
            30000.0, 170000.0, 0.0, 100.0, 1.0e-4));

    [Fact]
    public void Solve_NonPositiveNozzleRadius_Throws() =>
        Assert.Throws<ArgumentException>(() => HeliconIcrhMagneticNozzleModel.Solve(
            30000.0, 170000.0, 2.0, 0.0, 1.0e-4));

    [Fact]
    public void Solve_NonPositiveMassFlow_Throws() =>
        Assert.Throws<ArgumentException>(() => HeliconIcrhMagneticNozzleModel.Solve(
            30000.0, 170000.0, 2.0, 100.0, 0.0));

    // ── VX-200i baseline anchor ──────────────────────────────────────────

    [Fact]
    public void Vx200iBaseline_ThrustAroundFiveNewton()
    {
        // Calibrated cluster anchor: at the VX-200i design point
        // (P_h=30 kW, P_i=170 kW, B=2 T, R=100 mm, ṁ=100 mg/s)
        // the model produces T ≈ 4.63 N (within ±25 % of the 5 N
        // Chang Diaz 2009 target).
        var r = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 170000.0, 2.0, 100.0, 1.0e-4);
        Assert.InRange(r.Thrust_N, 3.75, 6.25);
    }

    [Fact]
    public void Vx200iBaseline_IspAroundFiveThousandSeconds()
    {
        // Isp ≈ 4982 s at the calibrated anchor (within ±15 % of the
        // 5000 s Chang Diaz target).
        var r = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 170000.0, 2.0, 100.0, 1.0e-4);
        Assert.InRange(r.IspVacuum_s, 4250.0, 5750.0);
    }

    [Fact]
    public void Vx200iBaseline_IonisationFractionNearUnity()
    {
        // η_i ≈ 0.95 at the calibrated anchor. Helicon coupling is
        // strong enough at 30 kW to ionise ~ all of the 100 mg/s Ar
        // input.
        var r = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 170000.0, 2.0, 100.0, 1.0e-4);
        Assert.InRange(r.IonisationFraction, 0.85, 1.0);
    }

    [Fact]
    public void Vx200iBaseline_MagneticMirrorAroundThree()
    {
        // M = k_mirror · B_z · R_exit = 0.015 · 2.0 · 100 = 3.0.
        var r = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 170000.0, 2.0, 100.0, 1.0e-4);
        Assert.Equal(3.0, r.MagneticMirrorRatio, precision: 6);
    }

    [Fact]
    public void Vx200iBaseline_NozzleConversionAroundTwoThirds()
    {
        // η_nozzle = 1 - 1/M = 1 - 1/3 = 0.667.
        var r = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 170000.0, 2.0, 100.0, 1.0e-4);
        Assert.InRange(r.NozzleConversionEfficiency, 0.60, 0.75);
    }

    [Fact]
    public void Vx200iBaseline_IonTemperatureInExpectedRange()
    {
        // E_per_ion ≈ 743 eV (high-energy ICRH heating at 170 kW
        // distributed across η_i · 100 mg/s of argon).
        var r = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 170000.0, 2.0, 100.0, 1.0e-4);
        Assert.InRange(r.IonTemperature_eV, 600.0, 900.0);
    }

    [Fact]
    public void Vx200iBaseline_ConvergedTrue()
    {
        var r = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 170000.0, 2.0, 100.0, 1.0e-4);
        Assert.True(r.Converged);
    }

    [Fact]
    public void Vx200iBaseline_ThrustEqualsMassFlowTimesExitVelocity()
    {
        // T = ṁ_ion · v exactly by construction.
        var r = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 170000.0, 2.0, 100.0, 1.0e-4);
        double reconstructed = r.MassFlow_kgs * r.ExitVelocity_ms;
        Assert.Equal(r.Thrust_N, reconstructed, precision: 12);
    }

    // ── Scaling laws ─────────────────────────────────────────────────────

    [Fact]
    public void MirrorRatio_LinearInSolenoidField()
    {
        // M ∝ B_z at fixed R_exit.
        var r1 = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 170000.0, 1.0, 100.0, 1.0e-4);
        var r2 = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 170000.0, 2.0, 100.0, 1.0e-4);
        Assert.Equal(2.0, r2.MagneticMirrorRatio / r1.MagneticMirrorRatio, precision: 6);
    }

    [Fact]
    public void MirrorRatio_LinearInNozzleRadius()
    {
        // M ∝ R_exit at fixed B_z.
        var r1 = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 170000.0, 2.0, 50.0, 1.0e-4);
        var r2 = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 170000.0, 2.0, 100.0, 1.0e-4);
        Assert.Equal(2.0, r2.MagneticMirrorRatio / r1.MagneticMirrorRatio, precision: 6);
    }

    [Fact]
    public void IonTemperature_LinearInIcrhPower()
    {
        // E_per_ion ∝ P_icrh at fixed (η_i · ṁ).
        var r1 = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 100000.0, 2.0, 100.0, 1.0e-4);
        var r2 = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 200000.0, 2.0, 100.0, 1.0e-4);
        Assert.Equal(2.0, r2.IonTemperature_eV / r1.IonTemperature_eV, precision: 6);
    }

    [Fact]
    public void ExitVelocity_ScalesAsSqrtIonTemperature()
    {
        // v ∝ √E_per_ion at fixed η_nozzle.
        var r1 = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 100000.0, 2.0, 100.0, 1.0e-4);
        var r2 = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 200000.0, 2.0, 100.0, 1.0e-4);
        Assert.Equal(Math.Sqrt(2.0), r2.ExitVelocity_ms / r1.ExitVelocity_ms, precision: 6);
    }

    [Fact]
    public void IonisationFraction_LinearInHeliconPower_BeforeSaturation()
    {
        // η_i ∝ P_helicon at fixed ṁ, BEFORE the cap at 1.0. Use very
        // high ṁ so η_i stays well below 1.0 to avoid saturation.
        var r1 = HeliconIcrhMagneticNozzleModel.Solve(10000.0, 170000.0, 2.0, 100.0, 5.0e-4);
        var r2 = HeliconIcrhMagneticNozzleModel.Solve(20000.0, 170000.0, 2.0, 100.0, 5.0e-4);
        Assert.Equal(2.0, r2.IonisationFraction / r1.IonisationFraction, precision: 6);
    }

    [Fact]
    public void IonisationFraction_SaturatesAtUnity_AtHighPower()
    {
        // At very high P_helicon / ṁ the η_i caps at 1.0.
        var r = HeliconIcrhMagneticNozzleModel.Solve(100000.0, 170000.0, 2.0, 100.0, 1.0e-5);
        Assert.Equal(1.0, r.IonisationFraction, precision: 6);
    }

    [Fact]
    public void NozzleConversion_SaturatesAt95Percent_AtHighMirrorRatio()
    {
        // η_nozzle caps at 0.95 to avoid unphysical saturation.
        // M = k · B · R; at B=5, R=300: M = 0.015 · 5 · 300 = 22.5
        // → η_nozzle = 1 - 1/22.5 = 0.956 → clamped to 0.95.
        var r = HeliconIcrhMagneticNozzleModel.Solve(30000.0, 170000.0, 5.0, 300.0, 1.0e-4);
        Assert.Equal(0.95, r.NozzleConversionEfficiency, precision: 6);
    }

    // ── Variable specific impulse (defining VASIMR property) ─────────────

    [Fact]
    public void VariableIsp_HighIcrhFraction_GivesHigherIsp()
    {
        // The VASIMR-defining property: at fixed total power, shifting
        // P_icrh → higher and P_helicon → lower trades thrust for Isp.
        //
        // Low-Isp mode: P_h = 100 kW, P_i = 100 kW (high η_i, low T_perp).
        // High-Isp mode: P_h = 30 kW, P_i = 170 kW (lower η_i, higher T_perp).
        var lowIsp  = HeliconIcrhMagneticNozzleModel.Solve(100000.0, 100000.0, 2.0, 100.0, 1.0e-4);
        var highIsp = HeliconIcrhMagneticNozzleModel.Solve( 30000.0, 170000.0, 2.0, 100.0, 1.0e-4);
        Assert.True(highIsp.IspVacuum_s > lowIsp.IspVacuum_s,
            $"High-ICRH mode Isp ({highIsp.IspVacuum_s:F0} s) should exceed "
          + $"low-ICRH mode Isp ({lowIsp.IspVacuum_s:F0} s).");
    }

    // ── Cycle solver wrapper ─────────────────────────────────────────────

    [Fact]
    public void VasimrCycleSolver_NullDesign_Throws()
    {
        var cond = MakeConds();
        Assert.Throws<ArgumentNullException>(() => VasimrCycleSolver.Solve(null!, cond));
    }

    [Fact]
    public void VasimrCycleSolver_NonVasimrKind_Throws()
    {
        var design = MakeDesign() with { Kind = ElectricPropulsionEngineKind.HallEffect };
        Assert.Throws<ArgumentException>(() => VasimrCycleSolver.Solve(design, MakeConds()));
    }

    [Fact]
    public void VasimrCycleSolver_NaNRequired_Throws()
    {
        var design = MakeDesign() with { VasimrHeliconRfPower_W = double.NaN };
        Assert.Throws<ArgumentException>(() => VasimrCycleSolver.Solve(design, MakeConds()));
    }

    [Fact]
    public void VasimrCycleSolver_PackagesPlasmaState()
    {
        var result = VasimrCycleSolver.Solve(MakeDesign(), MakeConds());
        Assert.NotNull(result.PlasmaState);
        Assert.Equal(result.Helicon.IonTemperature_eV, result.PlasmaState.IonTemperature_eV, precision: 9);
        Assert.Equal(result.Helicon.MagneticMirrorRatio, result.PlasmaState.MagneticMirrorRatio, precision: 9);
        Assert.Equal(result.Helicon.IonisationFraction, result.PlasmaState.IonisationFraction, precision: 9);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static ElectricPropulsionEngineDesign MakeDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.Vasimr,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        VasimrHeliconRfPower_W    = 30000.0,
        VasimrIcrhRfPower_W       = 170000.0,
        VasimrSolenoidField_T     = 2.0,
        VasimrNozzleExitRadius_mm = 100.0,
        VasimrArgonMassFlow_kgs   = 1.0e-4,
    };

    private static ResistojetConditions MakeConds() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:    250000.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.Xenon,
        InletTemperature_K:  300.0,
        InletComposition:    PropellantInletComposition.PureH2);
}
