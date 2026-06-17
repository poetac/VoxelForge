// AntennaWave2Tests.cs — Sprint ANT.W2 unit tests for the Yagi-Uda +
// horn-antenna extensions + Eb/N0 link-margin helper.

using System;
using Voxelforge.Antenna;
using Xunit;

namespace Voxelforge.Tests.Antenna;

public sealed class AntennaWave2Tests
{
    // ── New antenna gain values ─────────────────────────────────────────

    [Fact]
    public void YagiUda_GainEqualsSevenDBi()
    {
        double g = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.YagiUda, dishDiameter_m: 0.0, wavelength_m: 0.1,
            dishApertureEfficiency: 0.65);
        Assert.Equal(AntennaSolver.YagiUdaGain_dBi, g, precision: 6);
    }

    [Fact]
    public void Horn_GainEqualsEighteenDBi()
    {
        double g = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Horn, dishDiameter_m: 0.0, wavelength_m: 0.1,
            dishApertureEfficiency: 0.65);
        Assert.Equal(AntennaSolver.HornGain_dBi, g, precision: 6);
    }

    [Fact]
    public void Yagi_HasHigherGainThanHalfWaveDipole()
    {
        Assert.True(AntennaSolver.YagiUdaGain_dBi
                  > AntennaSolver.HalfWaveDipoleGain_dBi);
    }

    [Fact]
    public void Horn_HasHigherGainThanYagi()
    {
        Assert.True(AntennaSolver.HornGain_dBi > AntennaSolver.YagiUdaGain_dBi);
    }

    [Fact]
    public void Horn_AndYagiAreUsableAsTxOrRx()
    {
        // Round-trip through the full Solve() for both new topologies.
        var d = new AntennaLinkDesign(
            TransmitAntennaKind:    AntennaKind.YagiUda,
            ReceiveAntennaKind:     AntennaKind.Horn,
            Frequency_Hz:           2.4e9,
            TransmitPower_W:        1.0,
            LinkDistance_m:         1000.0);
        var r = AntennaSolver.Solve(d);
        Assert.Equal(AntennaSolver.YagiUdaGain_dBi, r.TransmitAntennaGain_dBi, precision: 6);
        Assert.Equal(AntennaSolver.HornGain_dBi,    r.ReceiveAntennaGain_dBi,  precision: 6);
    }

    // ── Eb/N0 link-margin helper ────────────────────────────────────────

    [Fact]
    public void LinkMargin_AtTypicalGsLink_IsPositive()
    {
        // A reasonable commercial-satellite downlink:
        //   P_rx = -100 dBm = 1e-13 W, R = 1 Mbit/s, T_sys = 100 K,
        //   required Eb/N0 = 5 dB (QPSK with turbo coding).
        // Eb/N0 = 1e-13 / (1e6 · 1.38e-23 · 100) = 1e-13/1.38e-15 = 72.5
        //       → 18.6 dB. Margin = 18.6 − 5 = 13.6 dB. Healthy link.
        double margin = AntennaSolver.ComputeLinkMargin_dB(
            receivedPower_W:           1e-13,
            dataRate_bps:              1e6,
            systemNoiseTemperature_K:  100.0,
            requiredEbN0_dB:           5.0);
        Assert.InRange(margin, 10.0, 20.0);
    }

    [Fact]
    public void LinkMargin_AtMarginalLink_IsNearZero()
    {
        // P_rx very low → margin → 0 or negative. Tuned so that
        // Eb/N0 ≈ required.
        // Eb/N0 = required → P_rx = required · k · T · R
        // = 10^(5/10) · 1.38e-23 · 100 · 1e6 = 3.16 · 1.38e-15 = 4.36e-15 W.
        double margin = AntennaSolver.ComputeLinkMargin_dB(
            receivedPower_W:           4.36e-15,
            dataRate_bps:              1e6,
            systemNoiseTemperature_K:  100.0,
            requiredEbN0_dB:           5.0);
        Assert.InRange(margin, -1.0, 1.0);
    }

    [Fact]
    public void LinkMargin_RejectsNonPositiveInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AntennaSolver.ComputeLinkMargin_dB(0.0, 1e6, 100.0, 5.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AntennaSolver.ComputeLinkMargin_dB(1e-13, 0.0, 100.0, 5.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AntennaSolver.ComputeLinkMargin_dB(1e-13, 1e6, 0.0, 5.0));
    }

    [Fact]
    public void LinkMargin_DecreasesWithDataRate()
    {
        // Higher R → less Eb → less Eb/N0 → smaller margin. Doubling R
        // halves Eb → reduces margin by 10·log10(2) = 3 dB.
        double m_lo = AntennaSolver.ComputeLinkMargin_dB(
            1e-13, 1e6, 100.0, 5.0);
        double m_hi = AntennaSolver.ComputeLinkMargin_dB(
            1e-13, 2e6, 100.0, 5.0);
        Assert.Equal(10.0 * Math.Log10(2.0), m_lo - m_hi, precision: 4);
    }

    [Fact]
    public void LinkMargin_DecreasesWithNoiseTemperature()
    {
        // Higher T_sys → more noise → smaller margin. Doubling T
        // halves Eb/N0 → reduces margin by 3 dB.
        double m_quiet = AntennaSolver.ComputeLinkMargin_dB(
            1e-13, 1e6,  50.0, 5.0);
        double m_noisy = AntennaSolver.ComputeLinkMargin_dB(
            1e-13, 1e6, 100.0, 5.0);
        Assert.Equal(10.0 * Math.Log10(2.0), m_quiet - m_noisy, precision: 4);
    }

    [Fact]
    public void LinkMargin_IncreasesWithReceivedPower()
    {
        // Doubling P_rx adds 3 dB to margin.
        double m_lo = AntennaSolver.ComputeLinkMargin_dB(
            1e-13, 1e6, 100.0, 5.0);
        double m_hi = AntennaSolver.ComputeLinkMargin_dB(
            2e-13, 1e6, 100.0, 5.0);
        Assert.Equal(10.0 * Math.Log10(2.0), m_hi - m_lo, precision: 4);
    }
}
