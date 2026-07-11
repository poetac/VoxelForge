// AntennaSolverTests — pins the ANT.W1–W4 link-budget chain: λ = c/f,
// per-topology gains (dish 10·log10 η + 20·log10(πD/λ); Kraus helix
// 15N(C/λ)²(S/λ); fixed dipole/Yagi/horn/patch anchors), the Friis
// log-form P_rx = EIRP + G_rx − 20·log10(4πR/λ), the ANT.W3 kT·BW·NF
// sensitivity floor + achieved Eb/N₀, the ANT.W2 ITU rain (P.838-3 at
// an exact table frequency) + atmospheric-absorption terms, and the
// closure margin roll-up. Every expected value hand-derived and
// independently recomputed (python3, mirroring the solver's operation
// order) before assertion. Backfills coverage onto the Linux 'core' CI
// leg (ROADMAP → Now §2).

using System;
using Voxelforge.Antenna;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class AntennaSolverTests
{
    // X-band 8 GHz dish-to-dish LEO downlink at zenith: 3 m dishes both
    // ends (η = 0.65), 10 W, 1000 km slant range, elevation 90°, clear
    // sky. 8 GHz sits exactly on both ITU table grid points, so the
    // interpolators return tabulated values with t = 0.
    private static AntennaLinkDesign XBandDishLink()
        => new(AntennaKind.ParabolicDish, AntennaKind.ParabolicDish,
               Frequency_Hz:           8.0e9,
               TransmitPower_W:        10.0,
               LinkDistance_m:         1.0e6,
               TransmitDishDiameter_m: 3.0,
               ReceiveDishDiameter_m:  3.0,
               ElevationAngle_deg:     90.0);

    [Fact]
    public void Solve_XBandAnchor_ReproducesFriisAndSensitivityChain()
    {
        var r = AntennaSolver.Solve(XBandDishLink());

        Assert.Equal(0.03747405725, r.Wavelength_m, 12);
        // G = 10·log10(0.65) + 20·log10(π·3/λ) for both ends.
        Assert.Equal(46.139941795984804, r.TransmitAntennaGain_dBi, 9);
        Assert.Equal(46.139941795984804, r.ReceiveAntennaGain_dBi, 9);
        // EIRP = 10·log10(10) + G_tx = 10 + G_tx dBW.
        Assert.Equal(56.139941795984804, r.EffectiveIsotropicRadiatedPower_dBW, 9);
        Assert.Equal(170.50958296172226, r.FreeSpacePathLoss_dB, 9);
        Assert.Equal(-38.22969936975265, r.ReceivedPower_dBm, 9);
        Assert.Equal(1.5032460211956646e-07, r.ReceivedPower_W, 12);

        // ANT.W3 floor at defaults (QPSK uncoded 9.6 dB, 1 MHz BW, 3 dB
        // NF, 290 K): N = 10·log10(kT·BW) + 30 + NF = −110.975 dBm.
        Assert.Equal(9.6, r.RequiredEbN0_dB, 12);
        Assert.Equal(-101.37518719422812, r.ReceiverSensitivity_dBm, 9);
        Assert.Equal(72.74548782447546, r.AchievedEbN0_dB, 9);

        // ANT.W2 losses at zenith, clear sky: rain 0; atmospheric
        // absorption = tabulated 8 GHz zenith value 0.024 dB / sin 90°;
        // + 0.5 pointing + 0 polarisation + 0.5 cable.
        Assert.Equal(0.0, r.RainAttenuation_dB, 12);
        Assert.Equal(0.024, r.AtmosphericAbsorption_dB, 12);
        Assert.Equal(1.024, r.SystemLoss_dB, 12);
        Assert.Equal(62.121487824475466, r.LinkClosureMargin_dB, 9);
    }

    [Fact]
    public void ComputeAntennaGain_PinsEveryTopologyAnchor()
    {
        double lambda = 0.03747405725;   // 8 GHz
        Assert.Equal(0.0, AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.IdealIsotropic, 0.0, lambda, 0.65), 12);
        Assert.Equal(2.15, AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.HalfWaveDipole, 0.0, lambda, 0.65), 12);
        Assert.Equal(7.0, AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.YagiUda, 0.0, lambda, 0.65), 12);
        Assert.Equal(18.0, AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Horn, 0.0, lambda, 0.65), 12);
        Assert.Equal(7.5, AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Patch, 0.0, lambda, 0.65), 12);
        // Crossed dipole: quadrature feed selects a CP sense, no gain add.
        Assert.Equal(2.15, AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.CrossedDipole, 0.0, lambda, 0.65), 12);
        // Kraus helix at defaults (N=10, C/λ=1, S/λ=0.25):
        // G = 15·10·1²·0.25 = 37.5 → 15.74 dBi.
        Assert.Equal(15.740312677277188, AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Helical, 0.0, lambda, 0.65), 12);
        // Dish requires a positive diameter.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AntennaSolver.ComputeAntennaGain_dBi(
                AntennaKind.ParabolicDish, 0.0, lambda, 0.65));
    }

    [Fact]
    public void Solve_FsplGrowsBySixDbPerDistanceDoubling()
    {
        var near = AntennaSolver.Solve(XBandDishLink());
        var far  = AntennaSolver.Solve(XBandDishLink() with { LinkDistance_m = 2.0e6 });
        Assert.Equal(6.020599913279624,
            far.FreeSpacePathLoss_dB - near.FreeSpacePathLoss_dB, 9);
        // And the closure margin drops by exactly the same amount (the
        // system losses are distance-independent).
        Assert.Equal(6.020599913279624,
            near.LinkClosureMargin_dB - far.LinkClosureMargin_dB, 9);
    }

    [Fact]
    public void RainSlantPathAttenuation_MatchesItuTableAtAnExactGridPoint()
    {
        // 8 GHz row of P.838-3 Table 1: k_H = 4.54e-3, α_H = 1.327.
        // γ = k·R^α at 25 mm/hr; zenith path L_S = 3 km, horizontal
        // projection ~0 → reduction factor 1.
        Assert.Equal(0.32517881520425956,
            ItuAtmosphericModels.SpecificRainAttenuation_dB_per_km(8.0e9, 25.0), 12);
        Assert.Equal(0.9755364456127786,
            ItuAtmosphericModels.RainSlantPathAttenuation_dB(8.0e9, 90.0, 25.0), 12);
        // Clear sky short-circuits to zero.
        Assert.Equal(0.0,
            ItuAtmosphericModels.RainSlantPathAttenuation_dB(8.0e9, 90.0, 0.0), 12);
        // Lower elevation lengthens the slant path → more attenuation.
        double atTen = ItuAtmosphericModels.RainSlantPathAttenuation_dB(8.0e9, 10.0, 25.0);
        Assert.True(atTen > 0.9755364456127786);
    }

    [Fact]
    public void ComputeLinkMargin_PinsTheEbN0Formula()
    {
        // Eb/N0 = P_rx/(R·k·T): 1 pW at 1 Mbps / 290 K → 23.98 dB;
        // minus the 9.6 dB uncoded-QPSK requirement → 14.38 dB margin.
        Assert.Equal(14.375187194228106, AntennaSolver.ComputeLinkMargin_dB(
            receivedPower_W:          1.0e-12,
            dataRate_bps:             1.0e6,
            systemNoiseTemperature_K: 290.0,
            requiredEbN0_dB:          9.6), 9);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AntennaSolver.ComputeLinkMargin_dB(0.0, 1.0e6, 290.0, 9.6));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AntennaSolver.ComputeLinkMargin_dB(1.0e-12, 1.0e6, 0.0, 9.6));
    }

    [Fact]
    public void ModulationSchemeTable_RoundTripsAndPinsTheDeepSpaceOrdering()
    {
        Assert.Equal(20, ModulationSchemeTable.Count);
        for (int i = 0; i < ModulationSchemeTable.Count; i++)
            Assert.Equal(i, ModulationSchemeTable.ToIndex(
                ModulationSchemeTable.FromIndex(i)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ModulationSchemeTable.FromIndex(20));
        // Stronger FEC needs less Eb/N₀: turbo R-1/3 < LDPC R-1/2 <
        // convolutional R-1/2 < uncoded QPSK < 256-QAM.
        Assert.Equal(9.6, ModulationSchemeTable.RequiredEbN0_dB(
            ModulationScheme.QpskUncoded), 12);
        Assert.True(ModulationSchemeTable.RequiredEbN0_dB(ModulationScheme.BpskTurboR13)
                  < ModulationSchemeTable.RequiredEbN0_dB(ModulationScheme.BpskLdpcR12));
        Assert.True(ModulationSchemeTable.RequiredEbN0_dB(ModulationScheme.BpskLdpcR12)
                  < ModulationSchemeTable.RequiredEbN0_dB(ModulationScheme.BpskConvolutionalR12));
        Assert.True(ModulationSchemeTable.RequiredEbN0_dB(ModulationScheme.BpskConvolutionalR12)
                  < ModulationSchemeTable.RequiredEbN0_dB(ModulationScheme.QpskUncoded));
        Assert.True(ModulationSchemeTable.RequiredEbN0_dB(ModulationScheme.QpskUncoded)
                  < ModulationSchemeTable.RequiredEbN0_dB(ModulationScheme.Qam256Uncoded));
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() =>
            AntennaSolver.Solve(XBandDishLink() with
            { TransmitAntennaKind = AntennaKind.None }));
        // Dish endpoint without a diameter is out of range.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AntennaSolver.Solve(XBandDishLink() with { ReceiveDishDiameter_m = 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AntennaSolver.Solve(XBandDishLink() with { Frequency_Hz = 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AntennaSolver.Solve(XBandDishLink() with { ElevationAngle_deg = 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AntennaSolver.Solve(XBandDishLink() with { DishApertureEfficiency = 1.5 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AntennaSolver.Solve(XBandDishLink() with { RainRate_mmPerHr = -1.0 }));
    }
}
