// WindTurbineSolverTests — pins the WT.W1/W2 closed-form HAWT/VAWT
// snapshot: P_avail = ½ρAV³, the Gaussian-in-λ C_p cluster fit (peak
// 0.48 @ λ=7.5 HAWT, 0.40 @ λ=5 VAWT), the actuator-disk induction
// inversion 4a(1−a)² = C_p (bisection), C_T = 4a(1−a), thrust, and the
// rotor→drivetrain power roll-up. Every expected value hand-derived and
// independently recomputed (python3, mirroring the solver's operation
// order) before assertion. Backfills coverage onto the Linux 'core' CI
// leg (ROADMAP → Now §2).

using System;
using Voxelforge.WindTurbine;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class WindTurbineSolverTests
{
    // NREL-5MW-class reference rotor: R = 63 m, hub 90 m, rated wind
    // 11.4 m/s, λ = 7.5 (the C_p-peak anchor), η_drivetrain = 0.90.
    private static HawtDesign Nrel5MwClass()
        => new(WindTurbineKind.HorizontalAxis,
               RotorRadius_m:                 63.0,
               BladeCount:                    3,
               HubHeight_m:                   90.0,
               DesignWindSpeed_ms:            11.4,
               DesignTipSpeedRatio:           7.5,
               GearboxAndGeneratorEfficiency: 0.90,
               CutInWindSpeed_ms:             3.0,
               CutOutWindSpeed_ms:            25.0);

    [Fact]
    public void Solve_AtRatedWind_ReproducesActuatorDiskChain()
    {
        var r = HawtSolver.Solve(Nrel5MwClass(), windSpeed_ms: 11.4);

        // A = π·63² = 12 468.98 m²; P_avail = ½·1.225·A·11.4³.
        Assert.Equal(12468.981242097889, Nrel5MwClass().SweptArea_m2, 6);
        Assert.Equal(11314923.41152239, r.AvailablePower_W, 4);

        // λ at the Gaussian peak → C_p = 0.48 exactly (exp(0) = 1).
        Assert.Equal(0.48, r.PowerCoefficient, 12);
        Assert.Equal(7.5, r.TipSpeedRatio, 12);

        // ω = λV/R; v_tip = ωR = λV = 85.5 m/s exactly.
        Assert.Equal(1.3571428571428572, r.RotorAngularSpeed_rads, 12);
        Assert.Equal(85.5, r.TipSpeed_ms, 9);
        Assert.Equal(12.959759651768621, r.RotationSpeed_rpm, 9);

        // Induction from 4a(1−a)² = 0.48 (bisection, lower root);
        // C_T = 4a(1−a); T = C_T·½ρAV².
        Assert.Equal(0.17729247782426683, r.AxialInductionFactor, 10);
        Assert.Equal(0.5834394205247947, r.ThrustCoefficient, 10);
        Assert.Equal(579085.2946053558, r.RotorThrust_N, 4);

        // Power roll-up: P_rotor = C_p·P_avail; P_elec = 0.90·P_rotor.
        Assert.Equal(5431163.237530747, r.RotorPower_W, 4);
        Assert.Equal(4888046.913777673, r.ElectricalPower_W, 4);
    }

    [Fact]
    public void ComputePowerCoefficient_OffPeak_FollowsGaussianFit()
    {
        // HAWT λ = 4.5: z = (4.5−7.5)/3 = −1 → C_p = 0.48·e⁻¹.
        Assert.Equal(0.17658213176229232,
            HawtSolver.ComputePowerCoefficient(4.5), 12);
        // VAWT peak: λ = 5 → 0.40 exactly; λ = 3: z = −1 → 0.40·e⁻¹.
        Assert.Equal(0.40,
            HawtSolver.ComputePowerCoefficient(5.0, WindTurbineKind.VerticalAxis), 12);
        Assert.Equal(0.14715177646857694,
            HawtSolver.ComputePowerCoefficient(3.0, WindTurbineKind.VerticalAxis), 12);
        // λ = 0 short-circuits to 0 (no log/exp evaluated).
        Assert.Equal(0.0, HawtSolver.ComputePowerCoefficient(0.0), 12);
    }

    [Fact]
    public void ComputePowerCoefficient_NeverExceedsBetzLimit()
    {
        // Invariant robust to future anchor recalibration: the fit plus
        // its defensive clamp must respect Betz for any λ, both kinds.
        for (double lambda = 0.0; lambda <= 20.0; lambda += 0.25)
        {
            Assert.InRange(HawtSolver.ComputePowerCoefficient(lambda),
                0.0, HawtSolver.BetzLimit);
            Assert.InRange(
                HawtSolver.ComputePowerCoefficient(lambda, WindTurbineKind.VerticalAxis),
                0.0, HawtSolver.BetzLimit);
        }
    }

    [Fact]
    public void ComputeAxialInductionFactor_StaysOnLowerRootBranch()
    {
        // a ∈ [0, 1/3] for C_p ∈ [0, Betz]; a(Betz) → 1/3; a(0) → 0.
        Assert.Equal(0.0, HawtSolver.ComputeAxialInductionFactor(0.0), 12);
        double aBetz = HawtSolver.ComputeAxialInductionFactor(HawtSolver.BetzLimit);
        Assert.Equal(1.0 / 3.0, aBetz, 6);
        for (double cp = 0.05; cp < HawtSolver.BetzLimit; cp += 0.05)
        {
            double a = HawtSolver.ComputeAxialInductionFactor(cp);
            Assert.InRange(a, 0.0, 1.0 / 3.0 + 1e-12);
            // Round-trip: 4a(1−a)² recovers C_p to bisection tolerance.
            Assert.Equal(cp, 4.0 * a * (1.0 - a) * (1.0 - a), 9);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HawtSolver.ComputeAxialInductionFactor(0.65));   // > Betz
    }

    [Fact]
    public void Solve_OutsideCutInCutOutBand_ReturnsParkedSnapshot()
    {
        var below = HawtSolver.Solve(Nrel5MwClass(), windSpeed_ms: 2.0);
        Assert.Equal(0.0, below.RotorPower_W, 12);
        Assert.Equal(0.0, below.PowerCoefficient, 12);
        Assert.Equal(0.0, below.RotorThrust_N, 12);
        Assert.True(below.AvailablePower_W > 0.0,
            "parked snapshot still reports the kinetic-energy flux");

        var above = HawtSolver.Solve(Nrel5MwClass(), windSpeed_ms: 30.0);
        Assert.Equal(0.0, above.ElectricalPower_W, 12);
        Assert.Equal(0.0, above.TipSpeed_ms, 12);
    }

    [Fact]
    public void Solve_PowerRollUp_NeverCreatesEnergy()
    {
        // P_elec ≤ P_rotor ≤ C_p-clamped share of P_avail, at any wind
        // speed inside the operating band.
        for (double v = 3.0; v <= 25.0; v += 2.0)
        {
            var r = HawtSolver.Solve(Nrel5MwClass(), v);
            Assert.True(r.ElectricalPower_W <= r.RotorPower_W + 1e-9);
            Assert.True(r.RotorPower_W <= HawtSolver.BetzLimit * r.AvailablePower_W + 1e-9);
        }
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        // Blade tips must clear the ground: hub ≤ R is malformed.
        Assert.Throws<ArgumentException>(() =>
            HawtSolver.Solve(Nrel5MwClass() with { HubHeight_m = 60.0 }, 11.4));
        Assert.Throws<ArgumentException>(() =>
            HawtSolver.Solve(Nrel5MwClass() with { Kind = WindTurbineKind.None }, 11.4));
        Assert.Throws<ArgumentException>(() =>
            HawtSolver.Solve(Nrel5MwClass() with { BladeCount = 7 }, 11.4));
        Assert.Throws<ArgumentException>(() =>
            HawtSolver.Solve(Nrel5MwClass() with { DesignWindSpeed_ms = 2.0 }, 11.4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HawtSolver.Solve(Nrel5MwClass(), windSpeed_ms: -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HawtSolver.Solve(Nrel5MwClass(), 11.4, airDensity_kgm3: 0.0));
    }
}
