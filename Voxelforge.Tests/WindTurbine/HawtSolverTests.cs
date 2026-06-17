// HawtSolverTests.cs — Sprint WT.W1 unit tests for the closed-form
// horizontal-axis wind turbine performance snapshot.

using System;
using Voxelforge.WindTurbine;
using Xunit;

namespace Voxelforge.Tests.WindTurbine;

public sealed class HawtSolverTests
{
    // ── ComputePowerCoefficient — pure unit tests ────────────────────────

    [Fact]
    public void Cp_AtPeakLambda_EqualsPeakValue()
    {
        // λ = λ_peak → Gaussian exponent = 0 → C_p = C_p_peak.
        double cp = HawtSolver.ComputePowerCoefficient(HawtSolver.TipSpeedRatioAtPeakCp);
        Assert.Equal(HawtSolver.PeakPowerCoefficient, cp, precision: 9);
    }

    [Fact]
    public void Cp_AtZeroLambda_IsZero()
        => Assert.Equal(0.0, HawtSolver.ComputePowerCoefficient(0.0), precision: 9);

    [Fact]
    public void Cp_AlwaysBelowBetzLimit()
    {
        foreach (double lam in new[] { 0.0, 2.0, 5.0, 7.5, 10.0, 15.0, 30.0 })
        {
            double cp = HawtSolver.ComputePowerCoefficient(lam);
            Assert.True(cp <= HawtSolver.BetzLimit + 1e-9,
                $"C_p at λ={lam} ({cp:F4}) exceeded Betz limit {HawtSolver.BetzLimit:F4}.");
        }
    }

    [Fact]
    public void Cp_SymmetricAroundPeakLambda()
    {
        // The Gaussian fit is symmetric in (λ − λ_peak).
        double cpLeft  = HawtSolver.ComputePowerCoefficient(HawtSolver.TipSpeedRatioAtPeakCp - 2.0);
        double cpRight = HawtSolver.ComputePowerCoefficient(HawtSolver.TipSpeedRatioAtPeakCp + 2.0);
        Assert.Equal(cpLeft, cpRight, precision: 6);
    }

    [Fact]
    public void Cp_RejectsNegativeLambda()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HawtSolver.ComputePowerCoefficient(-1.0));
    }

    // ── ComputeAxialInductionFactor — pure unit tests ───────────────────

    [Fact]
    public void AxialInduction_AtZeroCp_IsZero()
        => Assert.Equal(0.0, HawtSolver.ComputeAxialInductionFactor(0.0), precision: 9);

    [Fact]
    public void AxialInduction_AtBetzCp_IsOneThird()
    {
        // At C_p = 16/27, a = 1/3 (Betz optimum).
        double a = HawtSolver.ComputeAxialInductionFactor(HawtSolver.BetzLimit);
        Assert.Equal(1.0 / 3.0, a, precision: 4);
    }

    [Fact]
    public void AxialInduction_InverseRoundTrip()
    {
        // a → C_p = 4a(1-a)² → C_p → a' must round-trip.
        foreach (double a in new[] { 0.05, 0.10, 0.20, 0.30 })
        {
            double cp = 4.0 * a * (1.0 - a) * (1.0 - a);
            double aBack = HawtSolver.ComputeAxialInductionFactor(cp);
            Assert.Equal(a, aBack, precision: 4);
        }
    }

    [Fact]
    public void AxialInduction_RejectsCpAboveBetz()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HawtSolver.ComputeAxialInductionFactor(0.65));
    }

    // ── Solver end-to-end (NREL 5 MW baseline) ──────────────────────────

    [Fact]
    public void Nrel5MW_AtRatedWindSpeed_ElectricalPowerInClusterBand()
    {
        // V=11.4, R=63 → A=12469 m². ρ=1.225 → P_avail=11.3 MW.
        // C_p=0.48 → P_rotor=5.43 MW. η=0.944 → P_elec ≈ 5.13 MW.
        // Cluster band [4.5 MW, 5.5 MW] around the 5 MW NREL anchor.
        var r = HawtSolver.Solve(Nrel5MW(), windSpeed_ms: 11.4);
        Assert.InRange(r.ElectricalPower_W, 4_500_000.0, 5_500_000.0);
    }

    [Fact]
    public void Nrel5MW_AtRatedWindSpeed_PowerCoefficientNearPeak()
    {
        // λ = 7.5 = λ_peak → C_p ≈ 0.48.
        var r = HawtSolver.Solve(Nrel5MW(), windSpeed_ms: 11.4);
        Assert.Equal(HawtSolver.PeakPowerCoefficient, r.PowerCoefficient, precision: 4);
    }

    [Fact]
    public void Nrel5MW_AvailablePowerCubicInWindSpeed()
    {
        // P_avail ∝ V³ at fixed ρ + A.
        var lo = HawtSolver.Solve(Nrel5MW(), windSpeed_ms:  5.7);  // half rated
        var hi = HawtSolver.Solve(Nrel5MW(), windSpeed_ms: 11.4);
        // P_hi / P_lo should equal (V_hi / V_lo)³ = 8.0.
        Assert.Equal(8.0, hi.AvailablePower_W / lo.AvailablePower_W, precision: 4);
    }

    [Fact]
    public void Nrel5MW_TipSpeed_BelowSonicAndNoiseLimit()
    {
        // v_tip = λ · V = 7.5 · 11.4 = 85.5 m/s. Within practical
        // structural / noise band for utility-scale rotors (< 90 m/s).
        var r = HawtSolver.Solve(Nrel5MW(), windSpeed_ms: 11.4);
        Assert.InRange(r.TipSpeed_ms, 50.0, 95.0);
    }

    [Fact]
    public void Nrel5MW_AxialInduction_LessThanOneThird()
    {
        // C_p < Betz → a < 1/3 (lower-induction root).
        var r = HawtSolver.Solve(Nrel5MW(), windSpeed_ms: 11.4);
        Assert.True(r.AxialInductionFactor < 1.0 / 3.0);
        Assert.True(r.AxialInductionFactor > 0);
    }

    [Fact]
    public void Nrel5MW_ThrustPositive()
    {
        var r = HawtSolver.Solve(Nrel5MW(), windSpeed_ms: 11.4);
        Assert.True(r.RotorThrust_N > 0);
        // NREL 5MW rated thrust cluster ≈ 600-800 kN.
        Assert.InRange(r.RotorThrust_N, 300_000.0, 900_000.0);
    }

    [Fact]
    public void ParkedEnvelope_BelowCutIn_ProducesZeroElectricalPower()
    {
        // Wave-1 cut-in = 3.0 m/s → V=2.0 m/s parked.
        var r = HawtSolver.Solve(Nrel5MW(), windSpeed_ms: 2.0);
        Assert.Equal(0.0, r.ElectricalPower_W, precision: 9);
        Assert.Equal(0.0, r.RotorPower_W,     precision: 9);
        Assert.Equal(0.0, r.PowerCoefficient, precision: 9);
        Assert.Equal(0.0, r.RotorThrust_N,    precision: 9);
        // But the available kinetic-energy flux is still reported (so a
        // dashboard can plot "what if it were running").
        Assert.True(r.AvailablePower_W > 0);
    }

    [Fact]
    public void ParkedEnvelope_AboveCutOut_ProducesZeroElectricalPower()
    {
        // Cut-out = 25 m/s → V=30 m/s parked.
        var r = HawtSolver.Solve(Nrel5MW(), windSpeed_ms: 30.0);
        Assert.Equal(0.0, r.ElectricalPower_W, precision: 9);
        Assert.Equal(0.0, r.RotorPower_W,     precision: 9);
    }

    // ── Drivetrain scaling ───────────────────────────────────────────────

    [Fact]
    public void ElectricalPower_LinearInGearboxAndGeneratorEfficiency()
    {
        var idealDrivetrain = HawtSolver.Solve(
            Nrel5MW() with { GearboxAndGeneratorEfficiency = 1.0 }, 11.4);
        var realDrivetrain  = HawtSolver.Solve(
            Nrel5MW() with { GearboxAndGeneratorEfficiency = 0.5 }, 11.4);
        // Same C_p · P_avail → ratio = η_real / η_ideal = 0.5.
        Assert.Equal(0.5,
            realDrivetrain.ElectricalPower_W / idealDrivetrain.ElectricalPower_W,
            precision: 6);
    }

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsHubHeightBelowRotorRadius()
    {
        var d = Nrel5MW() with { HubHeight_m = 50.0 };  // less than R = 63
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsCutOutBelowCutIn()
    {
        var d = Nrel5MW() with { CutInWindSpeed_ms = 10.0, CutOutWindSpeed_ms = 5.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsDesignWindSpeedOutsideCutInCutOutBand()
    {
        var d = Nrel5MW() with { DesignWindSpeed_ms = 50.0 };   // above cut-out
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsBladeCountOutOfBand()
    {
        var d = Nrel5MW() with { BladeCount = 0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
        var d2 = Nrel5MW() with { BladeCount = 7 };
        Assert.Throws<ArgumentException>(() => d2.ValidateSelf());
    }

    [Fact]
    public void Solve_RejectsNegativeWindSpeed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HawtSolver.Solve(Nrel5MW(), windSpeed_ms: -1.0));
    }

    [Fact]
    public void Solve_RejectsNonPositiveAirDensity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HawtSolver.Solve(Nrel5MW(), windSpeed_ms: 11.4, airDensity_kgm3: 0.0));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // NREL 5 MW reference baseline (Jonkman et al. 2009 NREL/TP-500-38060).
    private static HawtDesign Nrel5MW() => new(
        Kind:                            WindTurbineKind.HorizontalAxis,
        RotorRadius_m:                   63.0,
        BladeCount:                      3,
        HubHeight_m:                     90.0,
        DesignWindSpeed_ms:              11.4,
        DesignTipSpeedRatio:             7.5,
        GearboxAndGeneratorEfficiency:   0.944,
        CutInWindSpeed_ms:               3.0,
        CutOutWindSpeed_ms:              25.0);
}
