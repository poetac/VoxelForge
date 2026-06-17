// ChildLangmuirBeamModelTests.cs — Sprint EP.W2.GIT physics tests for the
// Child-Langmuir gridded-ion beam-extraction model.
//
// Coverage: closed-form perveance scaling, ion exit velocity, mass flow
// + thrust + Isp coupling, Child-Langmuir saturation clamping, NaN-trap
// behaviour, NSTAR-anchor sanity-band.

using System;
using Voxelforge.ElectricPropulsion.Solvers;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class ChildLangmuirBeamModelTests
{
    // ── Smoke / NSTAR anchor band ────────────────────────────────────────

    [Fact]
    public void Solve_NstarAnchor_Converges()
    {
        var r = ChildLangmuirBeamModel.Solve(
            beamVoltage_V:           1100.0,
            beamCurrentRequested_A:     1.76,
            screenGridRadius_mm:      145.0,
            accelGridGap_mm:            0.6,
            neutralizerCurrent_A:       1.76,
            massUtilizationOverride: double.NaN);
        Assert.True(r.Converged);
        Assert.True(r.BeamCurrent_A > 0);
        Assert.True(r.IonExitVelocity_ms > 0);
        Assert.True(r.IspVacuum_s > 0);
        Assert.True(r.Thrust_N > 0);
        Assert.True(r.MassFlow_kgs > 0);
    }

    [Fact]
    public void Solve_NstarAnchor_ThrustWithinFiftyMillinewtonBand()
    {
        var r = ChildLangmuirBeamModel.Solve(
            beamVoltage_V:           1100.0,
            beamCurrentRequested_A:     1.76,
            screenGridRadius_mm:      145.0,
            accelGridGap_mm:            0.6,
            neutralizerCurrent_A:       1.76,
            massUtilizationOverride: double.NaN);
        // Closed-form Child-Langmuir: thrust = J_beam · v_ion · m_Xe / q.
        // For V_b=1100 → v_ion ≈ 40 200 m/s, J_beam=1.76 →
        // thrust ≈ 1.76 · 40 200 · 2.18e-25 / 1.602e-19 ≈ 96 mN.
        Assert.InRange(r.Thrust_N, 0.075, 0.115);
    }

    [Fact]
    public void Solve_NstarAnchor_IspInLowKilometerPerSecondRange()
    {
        var r = ChildLangmuirBeamModel.Solve(
            beamVoltage_V:           1100.0,
            beamCurrentRequested_A:     1.76,
            screenGridRadius_mm:      145.0,
            accelGridGap_mm:            0.6,
            neutralizerCurrent_A:       1.76,
            massUtilizationOverride: double.NaN);
        // NSTAR cluster Isp 2800–3700 s (Goebel & Katz §5 Table 5-1).
        Assert.InRange(r.IspVacuum_s, 2800.0, 3800.0);
    }

    [Fact]
    public void Solve_NstarAnchor_BeamBelowChildLangmuirLimitWithMargin()
    {
        // NSTAR perveance margin is one of the design's load-bearing properties.
        // The CL limit at V_b=1100, r=145mm, gap=0.6mm should be >> 1.76 A.
        var r = ChildLangmuirBeamModel.Solve(
            beamVoltage_V:           1100.0,
            beamCurrentRequested_A:     1.76,
            screenGridRadius_mm:      145.0,
            accelGridGap_mm:            0.6,
            neutralizerCurrent_A:       1.76,
            massUtilizationOverride: double.NaN);
        Assert.True(r.ChildLangmuirLimit_A > r.BeamCurrent_A * 5.0,
            $"NSTAR should sit at least 5x below the CL limit "
          + $"(actual: J_beam={r.BeamCurrent_A:F2} A vs CL={r.ChildLangmuirLimit_A:F2} A).");
    }

    // ── Perveance scaling ────────────────────────────────────────────────

    [Fact]
    public void Perveance_ScalesAsVbeamToThePowerThreeHalves()
    {
        // J_CL = K · V^1.5 / d² → J_CL(2V) / J_CL(V) = 2^1.5 ≈ 2.828.
        var lo = ChildLangmuirBeamModel.Solve(1000.0, 0.5, 100.0, 1.0, 0.5, double.NaN);
        var hi = ChildLangmuirBeamModel.Solve(2000.0, 0.5, 100.0, 1.0, 0.5, double.NaN);
        double ratio = hi.ChildLangmuirLimit_A / lo.ChildLangmuirLimit_A;
        // 2^1.5 = 2.828, expect within 0.5 %.
        Assert.InRange(ratio, 2.814, 2.842);
    }

    [Fact]
    public void Perveance_ScalesAsOneOverGapSquared()
    {
        // J_CL = K · V^1.5 / d² → J_CL(d) / J_CL(2d) = 4.
        var narrow = ChildLangmuirBeamModel.Solve(1000.0, 0.5, 100.0, 1.0, 0.5, double.NaN);
        var wide   = ChildLangmuirBeamModel.Solve(1000.0, 0.5, 100.0, 2.0, 0.5, double.NaN);
        double ratio = narrow.ChildLangmuirLimit_A / wide.ChildLangmuirLimit_A;
        Assert.InRange(ratio, 3.98, 4.02);
    }

    [Fact]
    public void Perveance_ScalesAsScreenGridRadiusSquared()
    {
        // J_CL = K · V^1.5 / d² · A_open with A_open = π · r² → J_CL ∝ r².
        var small = ChildLangmuirBeamModel.Solve(1000.0, 0.5,  50.0, 1.0, 0.5, double.NaN);
        var big   = ChildLangmuirBeamModel.Solve(1000.0, 0.5, 100.0, 1.0, 0.5, double.NaN);
        double ratio = big.ChildLangmuirLimit_A / small.ChildLangmuirLimit_A;
        Assert.InRange(ratio, 3.98, 4.02);
    }

    // ── Saturation clamping ──────────────────────────────────────────────

    [Fact]
    public void Solve_RequestAboveChildLangmuirLimit_ClampsToLimit()
    {
        // Request 100 A through a geometry whose CL limit is much smaller.
        var r = ChildLangmuirBeamModel.Solve(
            beamVoltage_V:           500.0,
            beamCurrentRequested_A: 100.0,
            screenGridRadius_mm:     10.0,    // tiny grid
            accelGridGap_mm:          3.0,    // wide gap
            neutralizerCurrent_A:   100.0,
            massUtilizationOverride: double.NaN);
        Assert.True(r.BeamCurrent_A < 100.0);
        Assert.Equal(r.ChildLangmuirLimit_A, r.BeamCurrent_A, precision: 9);
    }

    // ── Ion velocity from energy conservation ────────────────────────────

    [Fact]
    public void IonExitVelocity_FollowsClassicalEnergyConservation()
    {
        // v_ion = √(2 q V_b / m_Xe). For V_b=1100 → ~40 200 m/s.
        var r = ChildLangmuirBeamModel.Solve(1100.0, 0.5, 100.0, 1.0, 0.5, double.NaN);
        double expected = Math.Sqrt(
            2.0 * ChildLangmuirBeamModel.ElementaryCharge_C * 1100.0
            / ChildLangmuirBeamModel.XenonIonMass_kg);
        Assert.Equal(expected, r.IonExitVelocity_ms, precision: 0);
        Assert.InRange(r.IonExitVelocity_ms, 39_500.0, 41_000.0);
    }

    [Fact]
    public void IonExitVelocity_ScalesAsSqrtVbeam()
    {
        var v1 = ChildLangmuirBeamModel.Solve(500.0, 0.5, 100.0, 1.0, 0.5, double.NaN).IonExitVelocity_ms;
        var v4 = ChildLangmuirBeamModel.Solve(2000.0, 0.5, 100.0, 1.0, 0.5, double.NaN).IonExitVelocity_ms;
        Assert.Equal(2.0, v4 / v1, precision: 3);
    }

    // ── Mass-utilisation override ────────────────────────────────────────

    [Fact]
    public void MassUtilizationOverride_LowersIspAndRaisesMassFlow()
    {
        var hi_eta = ChildLangmuirBeamModel.Solve(1100.0, 1.0, 145.0, 0.6, 1.0, 0.95);
        var lo_eta = ChildLangmuirBeamModel.Solve(1100.0, 1.0, 145.0, 0.6, 1.0, 0.80);
        Assert.True(lo_eta.MassFlow_kgs > hi_eta.MassFlow_kgs,
            "Lower η_m allows more neutral leak → higher total mass flow.");
        Assert.True(lo_eta.IspVacuum_s < hi_eta.IspVacuum_s,
            "Lower η_m dilutes the effective exit velocity → lower Isp.");
        // Ratio: Isp(0.95) / Isp(0.80) = 0.95 / 0.80 = 1.1875.
        Assert.InRange(hi_eta.IspVacuum_s / lo_eta.IspVacuum_s, 1.180, 1.195);
    }

    [Fact]
    public void MassUtilizationOverride_NaNUsesClusterAnchor()
    {
        var nan = ChildLangmuirBeamModel.Solve(1100.0, 1.0, 145.0, 0.6, 1.0, double.NaN);
        var anchor = ChildLangmuirBeamModel.Solve(
            1100.0, 1.0, 145.0, 0.6, 1.0, ChildLangmuirBeamModel.DefaultMassUtilization);
        Assert.Equal(anchor.IspVacuum_s, nan.IspVacuum_s, precision: 6);
        Assert.Equal(anchor.MassFlow_kgs, nan.MassFlow_kgs, precision: 12);
    }

    // ── NaN-trap + bound-check behaviour ────────────────────────────────

    [Fact]
    public void Solve_NonPositiveBeamVoltage_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChildLangmuirBeamModel.Solve(0.0, 1.0, 100.0, 1.0, 1.0, double.NaN));

    [Fact]
    public void Solve_NonPositiveBeamCurrent_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChildLangmuirBeamModel.Solve(1100.0, 0.0, 100.0, 1.0, 1.0, double.NaN));

    [Fact]
    public void Solve_NonPositiveScreenRadius_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChildLangmuirBeamModel.Solve(1100.0, 1.0, 0.0, 1.0, 1.0, double.NaN));

    [Fact]
    public void Solve_NonPositiveAccelGap_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChildLangmuirBeamModel.Solve(1100.0, 1.0, 100.0, 0.0, 1.0, double.NaN));

    [Fact]
    public void Solve_NonPositiveNeutralizerCurrent_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChildLangmuirBeamModel.Solve(1100.0, 1.0, 100.0, 1.0, 0.0, double.NaN));

    [Fact]
    public void Solve_NonPositiveMassUtilization_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChildLangmuirBeamModel.Solve(1100.0, 1.0, 100.0, 1.0, 1.0, 0.0));

    [Fact]
    public void Solve_MassUtilizationAboveOne_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChildLangmuirBeamModel.Solve(1100.0, 1.0, 100.0, 1.0, 1.0, 1.5));

    // ── Beam power = V × J product ───────────────────────────────────────

    [Fact]
    public void BeamPower_EqualsVbeamTimesEffectiveCurrent()
    {
        var r = ChildLangmuirBeamModel.Solve(1100.0, 1.76, 145.0, 0.6, 1.76, double.NaN);
        double expected = 1100.0 * r.BeamCurrent_A;
        Assert.Equal(expected, r.BeamPower_W, precision: 6);
    }
}
