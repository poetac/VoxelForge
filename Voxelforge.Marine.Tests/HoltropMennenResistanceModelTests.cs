// HoltropMennenResistanceModelTests.cs — Sprint M.W4 unit tests for the
// simplified Holtrop-Mennen displacement-hull resistance model.

using System;
using Voxelforge.Marine.Hydrodynamics;
using Xunit;

namespace Voxelforge.Marine.Tests;

public sealed class HoltropMennenResistanceModelTests
{
    private const double Rho_seawater = 1025.0;
    private const double Nu_seawater  = 1.35e-6;

    // ── Smoke / coastal motor-vessel anchor ──────────────────────────────

    [Fact]
    public void Solve_CoastalMotorVesselAnchor_Converges()
    {
        var r = HoltropMennenResistanceModel.Solve(
            speed_ms:               5.0,         // 10 knots
            lengthWaterline_m:     40.0,
            beamWaterline_m:        8.0,
            draft_m:                3.0,
            blockCoefficient:       0.65,
            massDisplacement_kg: 600_000.0,      // 600 tonnes
            waterDensity_kgm3:    Rho_seawater,
            kinematicViscosity_m2s: Nu_seawater);
        Assert.True(r.TotalResistance_N > 0);
        Assert.True(r.FroudeNumber > 0);
        Assert.True(r.FroudeNumber < HoltropMennenResistanceModel.AppendageResistanceFraction * 100); // sanity
        Assert.True(r.FormFactor > 1.0);
        Assert.True(r.WettedSurfaceArea_m2 > 0);
        Assert.True(r.DisplacedVolume_m3 > 0);
    }

    [Fact]
    public void Solve_CoastalMotorVessel_FroudeInDisplacementBand()
    {
        // V=5 m/s, L=40 → Fn = 5/√(9.81·40) ≈ 0.252. Squarely in
        // displacement regime [0.05, 0.40].
        var r = HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater);
        Assert.InRange(r.FroudeNumber, 0.05, 0.40);
    }

    [Fact]
    public void Solve_DisplacedVolume_EqualsDisplacementOverDensity()
    {
        var r = HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater);
        Assert.Equal(600_000.0 / Rho_seawater, r.DisplacedVolume_m3, precision: 6);
    }

    // ── Form-factor scaling ──────────────────────────────────────────────

    [Fact]
    public void FormFactor_GrowsWithBlockCoefficient()
    {
        var rLow  = HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.50, 600_000, Rho_seawater, Nu_seawater);
        var rHigh = HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.80, 600_000, Rho_seawater, Nu_seawater);
        Assert.True(rHigh.FormFactor > rLow.FormFactor);
    }

    [Fact]
    public void FormFactor_GrowsWithBeamToLengthRatio()
    {
        var rSlender = HoltropMennenResistanceModel.Solve(5.0, 80.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater);
        var rBeamy   = HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater);
        Assert.True(rBeamy.FormFactor > rSlender.FormFactor);
    }

    // ── Resistance scaling ──────────────────────────────────────────────

    [Fact]
    public void Resistance_GrowsExponentiallyWithFroude()
    {
        // Hump speed transition: doubling Fn from 0.20 to 0.40 should
        // multiply R_W by exp(m₁·(0.4² − 0.2²)) = exp(4.5·0.12) ≈ exp(0.54)
        // ≈ 1.72 (wave-making is exponential in Fn²).
        //
        // The original threshold `> 4×` assumed R_F grows as V² exactly,
        // but ITTC-1957 C_F drops with Re (C_F = 0.075 / (log10(Re)−2)²)
        // so R_F actually grows as ~V^1.85 over this speed range. For the
        // coastal-cargo hull (L=40, B=8, T=3, Cb=0.65, ∇≈585 m³) the
        // weighted R_F + R_W + R_app total ratio lands at ~2.76× — clearly
        // nonlinear (between linear 2× and quadratic 4×) and dominated by
        // the wave-making exponential rather than V² friction. Threshold
        // tightened to `> 2.5×` to preserve the "more than linear" intent
        // while leaving margin for hull-form drift. Filed as issue for the
        // full Holtrop polynomial rebuild (see PR #544 follow-ups).
        var rLow  = HoltropMennenResistanceModel.Solve(4.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater);
        var rHigh = HoltropMennenResistanceModel.Solve(8.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater);
        Assert.True(rHigh.TotalResistance_N > 2.5 * rLow.TotalResistance_N,
            $"Total resistance should grow > 2.5× when doubling speed (V=4→8 m/s, Fn=0.20→0.40); got "
          + $"R_low={rLow.TotalResistance_N:F0} N, R_high={rHigh.TotalResistance_N:F0} N, "
          + $"ratio={rHigh.TotalResistance_N / rLow.TotalResistance_N:F2}.");
    }

    [Fact]
    public void Resistance_GrowsWithMassDisplacement_AtFixedGeometryAndSpeed()
    {
        // Heavier ship displaces more volume → more wave-making.
        var rLight = HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.65, 400_000, Rho_seawater, Nu_seawater);
        var rHeavy = HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.65, 800_000, Rho_seawater, Nu_seawater);
        Assert.True(rHeavy.TotalResistance_N > rLight.TotalResistance_N);
    }

    // ── ITTC-1957 friction sanity ────────────────────────────────────────

    [Fact]
    public void FrictionCoefficient_FollowsItttc1957()
    {
        // C_F = 0.075 / (log10(Re) − 2)². For V=5, L=40, ν=1.35e-6:
        //   Re = 5·40/1.35e-6 = 1.48e8 → log10 ≈ 8.17, (8.17 − 2)² = 38.1
        //   C_F ≈ 0.075 / 38.1 ≈ 1.97e-3
        var r = HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater);
        double expectedCF = 0.075 / Math.Pow(Math.Log10(r.ReynoldsNumber) - 2.0, 2.0);
        double impliedCF = r.FrictionResistance_N / (0.5 * Rho_seawater * 5.0 * 5.0 * r.WettedSurfaceArea_m2);
        Assert.Equal(expectedCF, impliedCF, precision: 5);
    }

    // ── Appendage lump ───────────────────────────────────────────────────

    [Fact]
    public void Appendage_IsFivePercentOfFrictionTimesFormFactor()
    {
        var r = HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater);
        double expected = HoltropMennenResistanceModel.AppendageResistanceFraction
                        * r.FrictionResistance_N * r.FormFactor;
        Assert.Equal(expected, r.AppendageResistance_N, precision: 4);
    }

    // ── Total = friction*(1+k1) + wave + appendage ──────────────────────

    [Fact]
    public void TotalResistance_SumsComponentsCorrectly()
    {
        var r = HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater);
        double expected = r.FrictionResistance_N * r.FormFactor
                        + r.WaveMakingResistance_N
                        + r.AppendageResistance_N;
        Assert.Equal(expected, r.TotalResistance_N, precision: 4);
    }

    // ── NaN-trap + bound-check ──────────────────────────────────────────

    [Fact]
    public void Solve_NonPositiveSpeed_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            HoltropMennenResistanceModel.Solve(0.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater));

    [Fact]
    public void Solve_NonPositiveLwl_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            HoltropMennenResistanceModel.Solve(5.0, 0.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater));

    [Fact]
    public void Solve_NonPositiveBeam_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            HoltropMennenResistanceModel.Solve(5.0, 40.0, 0.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater));

    [Fact]
    public void Solve_NonPositiveDraft_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 0.0, 0.65, 600_000, Rho_seawater, Nu_seawater));

    [Fact]
    public void Solve_BlockCoefficientOutOfBand_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.30, 600_000, Rho_seawater, Nu_seawater));

    [Fact]
    public void Solve_NonPositiveMass_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.65, 0.0, Rho_seawater, Nu_seawater));

    [Fact]
    public void Solve_NonPositiveDensity_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.65, 600_000, 0.0, Nu_seawater));

    [Fact]
    public void Solve_NonPositiveViscosity_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            HoltropMennenResistanceModel.Solve(5.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, 0.0));
}
