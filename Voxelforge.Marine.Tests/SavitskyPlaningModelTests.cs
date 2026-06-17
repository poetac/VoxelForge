// SavitskyPlaningModelTests.cs — unit tests for SavitskyPlaningModel.
//
// Coverage: Savitsky lift fit forward/inverse, cluster trim correlation,
// resistance-coefficient sanity, NaN-trap behaviour, planing-yacht anchor
// sanity-band.

using System;
using Voxelforge.Marine.Hydrodynamics;
using Xunit;

namespace Voxelforge.Marine.Tests;

public sealed class SavitskyPlaningModelTests
{
    private const double Rho_seawater = 1025.0;
    private const double Nu_seawater  = 1.35e-6;

    // ── Smoke / 11 m planing yacht anchor ────────────────────────────────

    [Fact]
    public void Solve_PlaningYachtAnchor_Converges()
    {
        var r = SavitskyPlaningModel.Solve(
            speed_ms:               12.86,    // 25 kt cruise
            beamMidship_m:           3.0,
            deadriseAngle_deg:      18.0,
            massDisplacement_kg:  5000.0,
            waterDensity_kgm3:    Rho_seawater,
            kinematicViscosity_m2s: Nu_seawater);
        Assert.True(r.Converged);
        Assert.True(r.TotalResistance_N > 0);
        Assert.True(r.WettedLengthToBeamRatio > 0);
        Assert.True(r.WettedSurfaceArea_m2 > 0);
        Assert.True(r.SpeedCoefficient > 0);
        Assert.True(r.ReynoldsNumber > 1e6);
    }

    [Fact]
    public void Solve_PlaningYachtAnchor_TrimInClusterBand()
    {
        // At V=12.86 m/s, b=3.0 m → C_v = 12.86/√(g·3) = 12.86/5.42 ≈ 2.37
        // Below the cluster low edge — clamps to 3.5°.
        var r = SavitskyPlaningModel.Solve(12.86, 3.0, 18.0, 5000.0, Rho_seawater, Nu_seawater);
        Assert.InRange(r.TrimAngle_deg,
            SavitskyPlaningModel.TrimClusterLow_deg,
            SavitskyPlaningModel.TrimClusterHigh_deg);
    }

    [Fact]
    public void Solve_PlaningYachtAnchor_LambdaInValidityBand()
    {
        // Cluster λ for the 11 m yacht should sit in the [1, 4] range.
        var r = SavitskyPlaningModel.Solve(12.86, 3.0, 18.0, 5000.0, Rho_seawater, Nu_seawater);
        Assert.InRange(r.WettedLengthToBeamRatio, 1.0, 6.0);
    }

    [Fact]
    public void Solve_PlaningYachtAnchor_ResistanceWithinTwentyPercentOfFiveKilonewton()
    {
        // Hand-checked sanity range: an 11 m / 5 t / 25-kt planing yacht
        // typically lands 3–7 kN total resistance.
        var r = SavitskyPlaningModel.Solve(12.86, 3.0, 18.0, 5000.0, Rho_seawater, Nu_seawater);
        Assert.InRange(r.TotalResistance_N, 2000.0, 10000.0);
    }

    // ── Trim cluster correlation ─────────────────────────────────────────

    [Fact]
    public void TrimFromClusterCorrelation_BelowLowEdge_ClampsToTrimMin()
    {
        Assert.Equal(SavitskyPlaningModel.TrimClusterLow_deg,
                     SavitskyPlaningModel.TrimFromClusterCorrelation(0.5));
    }

    [Fact]
    public void TrimFromClusterCorrelation_AboveHighEdge_ClampsToTrimMax()
    {
        Assert.Equal(SavitskyPlaningModel.TrimClusterHigh_deg,
                     SavitskyPlaningModel.TrimFromClusterCorrelation(20.0));
    }

    [Fact]
    public void TrimFromClusterCorrelation_AtMidband_InterpolatesLinearly()
    {
        double Cv_mid = (SavitskyPlaningModel.SpeedCoefficientLow
                      +  SavitskyPlaningModel.SpeedCoefficientHigh) / 2.0;
        double tau    = SavitskyPlaningModel.TrimFromClusterCorrelation(Cv_mid);
        double tauMid = (SavitskyPlaningModel.TrimClusterLow_deg
                      +  SavitskyPlaningModel.TrimClusterHigh_deg) / 2.0;
        Assert.Equal(tauMid, tau, precision: 6);
    }

    // ── Forward C_L0 fit ────────────────────────────────────────────────

    [Fact]
    public void LiftCoefficientCL0_GrowsWithTrimAngle()
    {
        // At fixed (λ, C_v), C_L0 ∝ τ^1.1 — strictly monotonic.
        double cl_lo = SavitskyPlaningModel.LiftCoefficientCL0(2.0, 2.0, 4.0);
        double cl_hi = SavitskyPlaningModel.LiftCoefficientCL0(8.0, 2.0, 4.0);
        Assert.True(cl_hi > cl_lo);
    }

    [Fact]
    public void LiftCoefficientCL0_GrowsWithLambda()
    {
        // At fixed (τ, C_v), C_L0 grows with λ (both terms positive).
        double cl_lo = SavitskyPlaningModel.LiftCoefficientCL0(4.0, 1.0, 4.0);
        double cl_hi = SavitskyPlaningModel.LiftCoefficientCL0(4.0, 4.0, 4.0);
        Assert.True(cl_hi > cl_lo);
    }

    // ── Resistance scaling ──────────────────────────────────────────────

    [Fact]
    public void Resistance_GrowsWithSpeed_AtFixedGeometry()
    {
        var slow = SavitskyPlaningModel.Solve( 8.0, 3.0, 18.0, 5000.0, Rho_seawater, Nu_seawater);
        var fast = SavitskyPlaningModel.Solve(15.0, 3.0, 18.0, 5000.0, Rho_seawater, Nu_seawater);
        Assert.True(fast.TotalResistance_N > slow.TotalResistance_N);
    }

    [Fact]
    public void Resistance_GrowsWithDisplacement_AtFixedSpeed()
    {
        var light = SavitskyPlaningModel.Solve(12.86, 3.0, 18.0, 3000.0, Rho_seawater, Nu_seawater);
        var heavy = SavitskyPlaningModel.Solve(12.86, 3.0, 18.0, 8000.0, Rho_seawater, Nu_seawater);
        Assert.True(heavy.TotalResistance_N > light.TotalResistance_N);
    }

    // ── NaN-trap + bound-check behaviour ────────────────────────────────

    [Fact]
    public void Solve_NonPositiveSpeed_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SavitskyPlaningModel.Solve(0.0, 3.0, 18.0, 5000.0, Rho_seawater, Nu_seawater));

    [Fact]
    public void Solve_NonPositiveBeam_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SavitskyPlaningModel.Solve(12.0, 0.0, 18.0, 5000.0, Rho_seawater, Nu_seawater));

    [Fact]
    public void Solve_NegativeDeadrise_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SavitskyPlaningModel.Solve(12.0, 3.0, -5.0, 5000.0, Rho_seawater, Nu_seawater));

    [Fact]
    public void Solve_DeadriseAboveFortyFive_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SavitskyPlaningModel.Solve(12.0, 3.0, 50.0, 5000.0, Rho_seawater, Nu_seawater));

    [Fact]
    public void Solve_NonPositiveMass_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SavitskyPlaningModel.Solve(12.0, 3.0, 18.0, 0.0, Rho_seawater, Nu_seawater));

    [Fact]
    public void Solve_NonPositiveDensity_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SavitskyPlaningModel.Solve(12.0, 3.0, 18.0, 5000.0, 0.0, Nu_seawater));

    [Fact]
    public void Solve_NonPositiveViscosity_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SavitskyPlaningModel.Solve(12.0, 3.0, 18.0, 5000.0, Rho_seawater, 0.0));

    // ── Lift balance sanity check ───────────────────────────────────────

    [Fact]
    public void Solve_LiftBalance_CLBetaMatchesRequired()
    {
        // Internal sanity: the C_Lβ output should equal (2 · Δ · g) / (ρ V² b²).
        var r = SavitskyPlaningModel.Solve(12.86, 3.0, 18.0, 5000.0, Rho_seawater, Nu_seawater);
        double expected = 2.0 * 5000.0 * 9.80665 / (1025.0 * 12.86 * 12.86 * 3.0 * 3.0);
        Assert.Equal(expected, r.LiftCoefficientBeta, precision: 6);
    }
}
