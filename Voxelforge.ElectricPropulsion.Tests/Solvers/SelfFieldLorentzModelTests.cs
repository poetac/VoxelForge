// SelfFieldLorentzModelTests.cs — Sprint EP.W2.MPD physics tests for the
// self-field Maecker Lorentz-acceleration model.
//
// Coverage: closed-form Maecker scaling, J²-thrust dependence, geometry-
// coefficient ln(r_a/r_c) dependence, magnetic-pressure scaling, cathode
// temperature lumped balance, NaN-trap behaviour, NASA-Lewis-anchor
// sanity-band.

using System;
using Voxelforge.ElectricPropulsion.Solvers;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class SelfFieldLorentzModelTests
{
    // ── Smoke / NASA-Lewis 200 kW SF-MPD anchor band ─────────────────────

    [Fact]
    public void Solve_NasaLewisAnchor_Converges()
    {
        var r = SelfFieldLorentzModel.Solve(
            arcCurrent_A:           4000.0,
            propellantMassFlow_kgs:    2.0e-4,
            cathodeRadius_mm:         10.0,
            anodeRadius_mm:          100.0,
            chamberLength_mm:        150.0);
        Assert.True(r.Converged);
        Assert.True(r.Thrust_N > 0);
        Assert.True(r.IspVacuum_s > 0);
        Assert.True(r.ExitVelocity_ms > 0);
        Assert.True(r.MagneticPressure_Pa > 0);
        Assert.True(r.CathodeWallTemp_K > 0);
    }

    [Fact]
    public void Solve_NasaLewisAnchor_ThrustAroundFiveNewtons()
    {
        var r = SelfFieldLorentzModel.Solve(4000.0, 2.0e-4, 10.0, 100.0, 150.0);
        // b = (μ₀/4π) · (ln(10) + 0.75) ≈ 1e-7 · 3.05 ≈ 3.05e-7
        // T = b · J² ≈ 3.05e-7 · 1.6e7 ≈ 4.88 N
        Assert.InRange(r.Thrust_N, 4.0, 6.0);
    }

    [Fact]
    public void Solve_NasaLewisAnchor_IspInLowKilometerPerSecondRange()
    {
        var r = SelfFieldLorentzModel.Solve(4000.0, 2.0e-4, 10.0, 100.0, 150.0);
        // v_exit = T / ṁ ≈ 4.88 / 2e-4 ≈ 24 400 m/s → Isp ≈ 2487 s.
        // SF-MPD cluster (Sovey 1990) sits in the 1.5–3.5 km Isp range.
        Assert.InRange(r.IspVacuum_s, 1500.0, 3500.0);
    }

    // ── Maecker scaling ──────────────────────────────────────────────────

    [Fact]
    public void Thrust_ScalesAsArcCurrentSquared()
    {
        // T = b · J². Doubling J quadruples T at fixed geometry.
        var lo = SelfFieldLorentzModel.Solve(2000.0, 1e-4, 10.0, 100.0, 150.0);
        var hi = SelfFieldLorentzModel.Solve(4000.0, 1e-4, 10.0, 100.0, 150.0);
        Assert.InRange(hi.Thrust_N / lo.Thrust_N, 3.98, 4.02);
    }

    [Fact]
    public void ThrustCoefficient_DependsOnGeometryRatioOnly()
    {
        // b is a function only of r_a/r_c — ṁ, J, L don't enter.
        var a = SelfFieldLorentzModel.Solve(2000.0, 1e-4, 10.0, 100.0, 150.0);
        var b = SelfFieldLorentzModel.Solve(5000.0, 5e-4, 10.0, 100.0, 250.0);
        Assert.Equal(a.ThrustCoefficient_NperA2, b.ThrustCoefficient_NperA2, precision: 12);
    }

    [Fact]
    public void ThrustCoefficient_GrowsWithLogOfRadiusRatio()
    {
        // b = (μ₀/4π) · (ln(r_a/r_c) + 3/4). Doubling r_a/r_c adds ln(2)·1e-7
        // ≈ 6.93e-8 to b.
        var narrow = SelfFieldLorentzModel.Solve(2000.0, 1e-4, 10.0,  50.0, 150.0);
        var wide   = SelfFieldLorentzModel.Solve(2000.0, 1e-4, 10.0, 100.0, 150.0);
        double delta = wide.ThrustCoefficient_NperA2 - narrow.ThrustCoefficient_NperA2;
        Assert.InRange(delta, 6.5e-8, 7.5e-8);
    }

    // ── Magnetic-pressure scaling ────────────────────────────────────────

    [Fact]
    public void MagneticPressure_ScalesAsCurrentSquared()
    {
        // B = μ₀J/(2π r_c) → p_mag = B²/(2μ₀) ∝ J².
        var lo = SelfFieldLorentzModel.Solve(1000.0, 1e-4, 10.0, 100.0, 150.0);
        var hi = SelfFieldLorentzModel.Solve(2000.0, 1e-4, 10.0, 100.0, 150.0);
        Assert.InRange(hi.MagneticPressure_Pa / lo.MagneticPressure_Pa, 3.98, 4.02);
    }

    [Fact]
    public void MagneticPressure_ScalesAsOneOverCathodeRadiusSquared()
    {
        // p_mag ∝ 1/r_c² (B ∝ 1/r_c).
        var thin = SelfFieldLorentzModel.Solve(2000.0, 1e-4,  5.0, 100.0, 150.0);
        var fat  = SelfFieldLorentzModel.Solve(2000.0, 1e-4, 10.0, 100.0, 150.0);
        Assert.InRange(thin.MagneticPressure_Pa / fat.MagneticPressure_Pa, 3.98, 4.02);
    }

    // ── Discharge voltage / power ────────────────────────────────────────

    [Fact]
    public void DischargeVoltage_LinearInChamberLengthOverAnodeRadius()
    {
        // V_arc = V_anode + V_col · (L / r_a). At fixed r_a, V scales linearly with L.
        var shortChamber = SelfFieldLorentzModel.Solve(2000.0, 1e-4, 10.0, 100.0,  50.0);
        var longChamber  = SelfFieldLorentzModel.Solve(2000.0, 1e-4, 10.0, 100.0, 150.0);
        // ΔV = V_col · (Δ(L/r_a)) = 8 · ((150-50)/100) = 8 V
        Assert.InRange(longChamber.DischargeVoltage_V - shortChamber.DischargeVoltage_V, 7.9, 8.1);
    }

    [Fact]
    public void DischargePower_EqualsVarcTimesJarc()
    {
        var r = SelfFieldLorentzModel.Solve(2000.0, 1e-4, 10.0, 100.0, 150.0);
        Assert.Equal(r.DischargeVoltage_V * 2000.0, r.DischargePower_W, precision: 6);
    }

    // ── Cathode temperature ──────────────────────────────────────────────

    [Fact]
    public void CathodeWallTemp_GrowsAsOneOverFourthRootOfArea()
    {
        // T_cathode = (Q_in / (ε σ A_tip))^0.25 with Q_in = V_cathode·J.
        // A larger r_c (bigger A_tip) at fixed J should reduce temperature.
        var thin = SelfFieldLorentzModel.Solve(2000.0, 1e-4,  5.0, 100.0, 150.0);
        var fat  = SelfFieldLorentzModel.Solve(2000.0, 1e-4, 10.0, 100.0, 150.0);
        Assert.True(thin.CathodeWallTemp_K > fat.CathodeWallTemp_K);
    }

    [Fact]
    public void CathodeWallTemp_ScalesAsOneFourthPowerOfArcCurrent()
    {
        // T ∝ J^0.25 at fixed geometry (Q_in ∝ J, T ∝ Q^0.25).
        var lo = SelfFieldLorentzModel.Solve(1000.0, 1e-4, 10.0, 100.0, 150.0);
        var hi = SelfFieldLorentzModel.Solve(2000.0, 1e-4, 10.0, 100.0, 150.0);
        // T(2J)/T(J) = 2^0.25 ≈ 1.189
        double ratio = hi.CathodeWallTemp_K / lo.CathodeWallTemp_K;
        Assert.InRange(ratio, 1.180, 1.198);
    }

    // ── Thrust efficiency ────────────────────────────────────────────────

    [Fact]
    public void ThrustEfficiency_BoundedZeroToOne()
    {
        var r = SelfFieldLorentzModel.Solve(4000.0, 2e-4, 10.0, 100.0, 150.0);
        Assert.InRange(r.ThrustEfficiency_Maecker, 0.0, 1.0);
    }

    // ── NaN-trap + bound-check behaviour ────────────────────────────────

    [Fact]
    public void Solve_NonPositiveArcCurrent_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SelfFieldLorentzModel.Solve(0.0, 1e-4, 10.0, 100.0, 150.0));

    [Fact]
    public void Solve_NonPositiveMassFlow_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SelfFieldLorentzModel.Solve(2000.0, 0.0, 10.0, 100.0, 150.0));

    [Fact]
    public void Solve_NonPositiveCathodeRadius_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SelfFieldLorentzModel.Solve(2000.0, 1e-4, 0.0, 100.0, 150.0));

    [Fact]
    public void Solve_NonPositiveAnodeRadius_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SelfFieldLorentzModel.Solve(2000.0, 1e-4, 10.0, 0.0, 150.0));

    [Fact]
    public void Solve_AnodeNotLargerThanCathode_Throws()
        // Maecker formula requires r_a > r_c; equality would make ln(r_a/r_c)=0.
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SelfFieldLorentzModel.Solve(2000.0, 1e-4, 50.0, 50.0, 150.0));

    [Fact]
    public void Solve_AnodeSmallerThanCathode_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SelfFieldLorentzModel.Solve(2000.0, 1e-4, 50.0, 30.0, 150.0));

    [Fact]
    public void Solve_NonPositiveChamberLength_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SelfFieldLorentzModel.Solve(2000.0, 1e-4, 10.0, 100.0, 0.0));
}
