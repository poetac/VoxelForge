// AntennaSolverTests.cs — Sprint ANT.W1 unit tests for the closed-form
// RF-link performance snapshot.

using System;
using Voxelforge.Antenna;
using Xunit;

namespace Voxelforge.Tests.Antenna;

public sealed class AntennaSolverTests
{
    // ── ComputeAntennaGain_dBi ──────────────────────────────────────────

    [Fact]
    public void IsotropicAntenna_Gain_IsZeroDBi()
    {
        double g = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.IdealIsotropic, 0.0, 0.1, 0.65);
        Assert.Equal(0.0, g, precision: 9);
    }

    [Fact]
    public void HalfWaveDipole_Gain_Is215DBi()
    {
        double g = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.HalfWaveDipole, 0.0, 0.1, 0.65);
        Assert.Equal(2.15, g, precision: 6);
    }

    [Fact]
    public void ParabolicDish_4m_AtXBand_HasGainInClusterBand()
    {
        // Cassini HGA: D = 4 m, f = 8.4 GHz → λ = 0.0357 m.
        // G = 0.65 · (π·4/0.0357)² → ~ 49 dBi (advertised 47).
        double lambda = AntennaSolver.SpeedOfLight_ms / 8.4e9;
        double g = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.ParabolicDish, 4.0, lambda, 0.65);
        Assert.InRange(g, 45.0, 55.0);
    }

    [Fact]
    public void ParabolicDish_70m_AtXBand_HasDsnClusterGain()
    {
        // NASA Deep Space Network 70-m at X-band: 74 dBi advertised.
        double lambda = AntennaSolver.SpeedOfLight_ms / 8.4e9;
        double g = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.ParabolicDish, 70.0, lambda, 0.65);
        Assert.InRange(g, 70.0, 80.0);
    }

    [Fact]
    public void ParabolicDish_RejectsZeroDiameter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AntennaSolver.ComputeAntennaGain_dBi(
                AntennaKind.ParabolicDish, 0.0, 0.1, 0.65));
    }

    [Fact]
    public void ComputeAntennaGain_ThrowsOnNoneKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AntennaSolver.ComputeAntennaGain_dBi(
                AntennaKind.None, 1.0, 0.1, 0.65));
    }

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneTxKind()
    {
        var d = CassiniToDsn_XBand() with { TransmitAntennaKind = AntennaKind.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositiveTxPower()
    {
        var d = CassiniToDsn_XBand() with { TransmitPower_W = 0.0 };
        // Numeric range failure -> ArgumentOutOfRangeException (#558 PR-F).
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroDishWithParabolic()
    {
        var d = CassiniToDsn_XBand() with { TransmitDishDiameter_m = 0.0 };
        // Numeric range failure -> ArgumentOutOfRangeException (#558 PR-F).
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    // ── Cassini → DSN deep-space link baseline ──────────────────────────

    [Fact]
    public void CassiniToDsn_XBand_ReceivedPowerInClusterBand()
    {
        // Cassini at Saturn (R ≈ 1.43e12 m) → DSN 70-m antenna. Real
        // Cassini X-band reception cluster: -135 to -120 dBm.
        var r = AntennaSolver.Solve(CassiniToDsn_XBand());
        Assert.InRange(r.ReceivedPower_dBm, -140.0, -115.0);
    }

    [Fact]
    public void CassiniToDsn_XBand_FreeSpacePathLossMassive()
    {
        // FSPL at 1.43e12 m, λ = 0.0357 m → ~ 294 dB.
        var r = AntennaSolver.Solve(CassiniToDsn_XBand());
        Assert.InRange(r.FreeSpacePathLoss_dB, 280.0, 310.0);
    }

    [Fact]
    public void CassiniToDsn_EIRPInClusterBand()
    {
        // P_tx = 20 W = 13 dBW; G_tx ≈ 49 dBi → EIRP ≈ 62 dBW.
        var r = AntennaSolver.Solve(CassiniToDsn_XBand());
        Assert.InRange(r.EffectiveIsotropicRadiatedPower_dBW, 55.0, 70.0);
    }

    [Fact]
    public void CassiniToDsn_ReceivedPowerLinearOnLinearScale_FromDBm()
    {
        // P_rx_W = 10^((P_rx_dBm − 30)/10). Sanity check round-trip.
        var r = AntennaSolver.Solve(CassiniToDsn_XBand());
        double expectedW = Math.Pow(10.0, (r.ReceivedPower_dBm - 30.0) / 10.0);
        Assert.Equal(expectedW, r.ReceivedPower_W, precision: 15);
    }

    [Fact]
    public void Wavelength_EqualsSpeedOfLightOverFrequency()
    {
        var d = CassiniToDsn_XBand();
        var r = AntennaSolver.Solve(d);
        Assert.Equal(AntennaSolver.SpeedOfLight_ms / d.Frequency_Hz,
                     r.Wavelength_m, precision: 12);
    }

    // ── Scaling sanity ──────────────────────────────────────────────────

    [Fact]
    public void FSPL_IncreasesWithDistance()
    {
        // Doubling R adds 6 dB to FSPL (20·log10(2) = 6.02).
        var lo = AntennaSolver.Solve(CassiniToDsn_XBand() with { LinkDistance_m = 1e10 });
        var hi = AntennaSolver.Solve(CassiniToDsn_XBand() with { LinkDistance_m = 2e10 });
        Assert.Equal(20.0 * Math.Log10(2.0),
                     hi.FreeSpacePathLoss_dB - lo.FreeSpacePathLoss_dB, precision: 6);
    }

    [Fact]
    public void ParabolicGain_QuadraticInDishDiameter()
    {
        // G ∝ D² → doubling D adds 20·log10(2) = 6 dB.
        double lambda = 0.0357;
        double g4 = AntennaSolver.ComputeAntennaGain_dBi(AntennaKind.ParabolicDish, 4.0, lambda, 0.65);
        double g8 = AntennaSolver.ComputeAntennaGain_dBi(AntennaKind.ParabolicDish, 8.0, lambda, 0.65);
        Assert.Equal(20.0 * Math.Log10(2.0), g8 - g4, precision: 6);
    }

    [Fact]
    public void ParabolicGain_RisesWithFrequency()
    {
        // G ∝ f² (since G ∝ (1/λ)² = (f/c)²).
        double lambda_x  = AntennaSolver.SpeedOfLight_ms / 8.4e9;    // X-band
        double lambda_ka = AntennaSolver.SpeedOfLight_ms / 32e9;     // Ka-band
        double g_x  = AntennaSolver.ComputeAntennaGain_dBi(AntennaKind.ParabolicDish, 4.0, lambda_x,  0.65);
        double g_ka = AntennaSolver.ComputeAntennaGain_dBi(AntennaKind.ParabolicDish, 4.0, lambda_ka, 0.65);
        Assert.True(g_ka > g_x);
    }

    [Fact]
    public void ReceivedPower_DBmRisesWithTxPower()
    {
        var lo = AntennaSolver.Solve(CassiniToDsn_XBand() with { TransmitPower_W = 10 });
        var hi = AntennaSolver.Solve(CassiniToDsn_XBand() with { TransmitPower_W = 20 });
        // Doubling P_tx adds 10·log10(2) ≈ 3 dB.
        Assert.Equal(10.0 * Math.Log10(2.0),
                     hi.ReceivedPower_dBm - lo.ReceivedPower_dBm, precision: 6);
    }

    [Fact]
    public void DishApertureEfficiencyOfOne_GainHigherThanRealisticEta()
    {
        double lambda = 0.0357;
        double g_real    = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.ParabolicDish, 4.0, lambda, dishApertureEfficiency: 0.65);
        double g_perfect = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.ParabolicDish, 4.0, lambda, dishApertureEfficiency: 1.0);
        Assert.True(g_perfect > g_real);
        // Difference equals 10·log10(1/0.65) = +1.87 dB.
        Assert.Equal(10.0 * Math.Log10(1.0 / 0.65),
                     g_perfect - g_real, precision: 6);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Cassini-to-DSN-70m X-band deep-space link baseline. P_tx = 20 W,
    // D_tx = 4 m (Cassini HGA), D_rx = 70 m (DSN), f = 8.4 GHz,
    // R = 1.43e12 m (Saturn distance). Lands P_rx ≈ -128 dBm.
    private static AntennaLinkDesign CassiniToDsn_XBand() => new(
        TransmitAntennaKind:      AntennaKind.ParabolicDish,
        ReceiveAntennaKind:       AntennaKind.ParabolicDish,
        Frequency_Hz:             8.4e9,
        TransmitPower_W:          20.0,
        LinkDistance_m:           1.43e12,
        TransmitDishDiameter_m:   4.0,
        ReceiveDishDiameter_m:   70.0,
        DishApertureEfficiency:   0.65);
}
