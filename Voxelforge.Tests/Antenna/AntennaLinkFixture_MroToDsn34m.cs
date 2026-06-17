// AntennaLinkFixture_MroToDsn34m.cs — published-product validation
// fixture for the deep-space X-band link path through the Antenna
// pillar.
//
// Anchors the model to the **Mars Reconnaissance Orbiter (MRO)** X-band
// downlink to a **NASA Deep Space Network (DSN) 34-m BWG antenna** at
// 1 AU Earth-Mars separation. Public anchors:
//   - DSN Telecommunications Link Design Handbook 810-005 (DESCANSO
//     public, https://deepspace.jpl.nasa.gov/dsndocs/810-005/) — 34-m
//     BWG aperture efficiency ~ 0.65 at X-band
//   - MRO Telecommunications System (JPL public release) — 3 m HGA,
//     100 W TWTA at saturation, X-band 8.4 GHz downlink
//   - Geometry: 1 AU = 1.496e11 m (mean Earth-Mars separation as a
//     canonical reference; actual range varies 0.5-2.5 AU)
//
// Cluster-anchored bands accommodate the scatter inherent in the
// 1 AU canonical reference; actual MRO downlinks at Mars perihelion
// / aphelion span ~ 10 dB more variation than the FSPL term alone
// captures. First fixture to exercise the ParabolicDish kind in the
// Antenna pillar (Wave-1 anchor cluster is half-wave-dipole +
// isotropic). Pure-additive: zero pillar code touched.

using Voxelforge.Antenna;
using Xunit;

namespace Voxelforge.Tests.Antenna;

public sealed class AntennaLinkFixture_MroToDsn34m
{
    // ── Friis transmission equation outputs ───────────────────────────

    [Fact]
    public void MroToDsn_AtOneAu_WavelengthMatchesXBand()
    {
        // λ = c / f at 8.4 GHz X-band = 0.0357 m.
        var r = AntennaSolver.Solve(MroToDsnLink());
        Assert.InRange(r.Wavelength_m, 0.0354, 0.0360);
    }

    [Fact]
    public void MroToDsn_AtOneAu_TransmitGainInDishBand()
    {
        // MRO 3 m HGA gain at X-band: G = η × (πD/λ)²
        // = 0.65 × (π × 3 / 0.0357)² = 0.65 × 264.2² = 45 357 = 46.6 dBi.
        // Cluster band [42, 50] dBi for 3 m X-band dishes.
        var r = AntennaSolver.Solve(MroToDsnLink());
        Assert.InRange(r.TransmitAntennaGain_dBi, 42.0, 50.0);
    }

    [Fact]
    public void MroToDsn_AtOneAu_ReceiveGainInDsnBand()
    {
        // DSN 34 m BWG gain at X-band:
        // G = 0.65 × (π × 34 / 0.0357)² = 0.65 × 2991² = 5.82e6 = 67.65 dBi.
        // Published DSN BWG nominal G/T = 51.5 dB/K → G_rx ≈ 67-68 dBi.
        var r = AntennaSolver.Solve(MroToDsnLink());
        Assert.InRange(r.ReceiveAntennaGain_dBi, 65.0, 70.0);
    }

    [Fact]
    public void MroToDsn_AtOneAu_EirpMatchesTxPowerPlusTxGain()
    {
        // EIRP = P_tx_dBW + G_tx_dBi exactly.
        var r = AntennaSolver.Solve(MroToDsnLink());
        const double P_tx_dBW = 20.0; // 10 × log10(100 W)
        Assert.Equal(P_tx_dBW + r.TransmitAntennaGain_dBi,
                     r.EffectiveIsotropicRadiatedPower_dBW, precision: 6);
    }

    [Fact]
    public void MroToDsn_AtOneAu_FreeSpacePathLossInDeepSpaceBand()
    {
        // FSPL at 1 AU X-band: 20 × log10(4π × 1.496e11 / 0.0357)
        // = 20 × log10(5.27e13) = 274.4 dB. Cluster band [273, 277] dB
        // accommodates λ + R fractional scatter.
        var r = AntennaSolver.Solve(MroToDsnLink());
        Assert.InRange(r.FreeSpacePathLoss_dB, 273.0, 277.0);
    }

    [Fact]
    public void MroToDsn_AtOneAu_ReceivedPowerInDeepSpaceBand()
    {
        // P_rx = EIRP + G_rx - FSPL = 66.6 + 67.6 - 274.4 = -140.2 dBW
        //                                                  = -110.2 dBm.
        // Real Mars-distance MRO downlink at DSN reads -120 to -110 dBm
        // depending on Mars range. Cluster band [-115, -105] dBm.
        var r = AntennaSolver.Solve(MroToDsnLink());
        Assert.InRange(r.ReceivedPower_dBm, -115.0, -105.0);
    }

    [Fact]
    public void MroToDsn_AtOneAu_ReceivedPowerLinearAndDbmConsistent()
    {
        // P_rx_W = 10^((P_rx_dBm - 30) / 10) — round-trip relative-
        // tolerance test (FP log/pow incurs ~ 1e-9 relative error).
        var r = AntennaSolver.Solve(MroToDsnLink());
        double linearFromDbm = System.Math.Pow(10.0,
            (r.ReceivedPower_dBm - 30.0) / 10.0);
        double relativeError = System.Math.Abs(linearFromDbm - r.ReceivedPower_W)
                             / r.ReceivedPower_W;
        Assert.True(relativeError < 1e-6,
            $"dBm → W round-trip relative error {relativeError:E3} should be < 1e-6.");
    }

    [Fact]
    public void MroToDsn_AtOneAu_ReceivedPowerIsFemtowattScale()
    {
        // P_rx ~ 1e-14 W (10 femtowatts) at 1 AU. Cluster band
        // [1e-15, 1e-13] W to allow Mars-range scatter.
        var r = AntennaSolver.Solve(MroToDsnLink());
        Assert.InRange(r.ReceivedPower_W, 1.0e-15, 1.0e-13);
    }

    // ── Antenna-kind validation ───────────────────────────────────────

    [Fact]
    public void MroToDsn_BothEndsAreParabolicDishes()
    {
        // Both MRO HGA and DSN 34-m are parabolic dishes. Wave-1 anchor
        // cluster (in `AntennaSolverTests`) covers isotropic + dipole +
        // Yagi + horn; B.17 specifically exercises the parabolic-dish
        // kind on both ends.
        Assert.Equal(AntennaKind.ParabolicDish, MroToDsnLink().TransmitAntennaKind);
        Assert.Equal(AntennaKind.ParabolicDish, MroToDsnLink().ReceiveAntennaKind);
    }

    [Fact]
    public void MroToDsn_DishGainScalesQuadraticallyWithDiameter()
    {
        // G ∝ (πD/λ)² → doubling D quadruples G (linear) = exactly
        // 20·log10(2) ≈ 6.0206 dB. The "6 dB" rule of thumb in RF
        // engineering rounds this; the assertion uses the exact
        // mathematical value so a 4-decimal-place check is meaningful.
        const double lambda_m = 0.03569;
        const double eta      = 0.65;
        double gain3m  = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.ParabolicDish, dishDiameter_m: 3.0,
            wavelength_m: lambda_m, dishApertureEfficiency: eta);
        double gain6m  = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.ParabolicDish, dishDiameter_m: 6.0,
            wavelength_m: lambda_m, dishApertureEfficiency: eta);
        Assert.Equal(20.0 * System.Math.Log10(2.0), gain6m - gain3m, precision: 4);
    }

    [Fact]
    public void MroToDsn_DishGainScalesQuadraticallyWithFrequency()
    {
        // G ∝ (π·D/λ)² and λ = c/f, so G ∝ f² → halving λ (doubling f)
        // adds exactly 20·log10(2) ≈ 6.0206 dB at fixed D.
        const double eta = 0.65;
        double gain_xband = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.ParabolicDish, dishDiameter_m: 34.0,
            wavelength_m: 0.03569 /* X-band 8.4 GHz */,
            dishApertureEfficiency: eta);
        double gain_kuband = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.ParabolicDish, dishDiameter_m: 34.0,
            wavelength_m: 0.017845 /* halved → 16.8 GHz */,
            dishApertureEfficiency: eta);
        Assert.Equal(20.0 * System.Math.Log10(2.0), gain_kuband - gain_xband, precision: 4);
    }

    // ── Link-margin physics (Sprint ANT.W2) ───────────────────────────

    [Fact]
    public void MroToDsn_LinkMargin_StrongLinkAtDeepSpaceTsys()
    {
        // DSN cryogenically-cooled HEMT LNA achieves T_sys ≈ 25 K.
        // At LDPC R_data = 100 kbps and required Eb/N0 = 0.5 dB
        // (modern deep-space LDPC), the link margin should be ample
        // (10-30 dB) at 1 AU.
        var r = AntennaSolver.Solve(MroToDsnLink());
        double margin_dB = AntennaSolver.ComputeLinkMargin_dB(
            receivedPower_W:          r.ReceivedPower_W,
            dataRate_bps:             100_000.0,
            systemNoiseTemperature_K: 25.0,
            requiredEbN0_dB:          0.5);
        Assert.InRange(margin_dB, 10.0, 30.0);
    }

    [Fact]
    public void MroToDsn_LinkMargin_DegradesWhenNoiseTemperatureRises()
    {
        // Same link with un-cooled commercial-ground-station T_sys
        // (~ 200 K vs 25 K) should drop margin by ~ 10 × log10(200/25)
        // = 9 dB. Confirm directional sign.
        var r = AntennaSolver.Solve(MroToDsnLink());
        double marginCold = AntennaSolver.ComputeLinkMargin_dB(
            r.ReceivedPower_W, 100_000.0, 25.0,  0.5);
        double marginWarm = AntennaSolver.ComputeLinkMargin_dB(
            r.ReceivedPower_W, 100_000.0, 200.0, 0.5);
        Assert.True(marginWarm < marginCold,
            "Higher T_sys must reduce link margin (more thermal noise floor).");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    // MRO X-band downlink to DSN 34-m BWG antenna at 1 AU mean Earth-
    // Mars separation. Public anchors:
    //   - MRO HGA: 3 m parabolic, X-band feed
    //   - MRO Tx Power: 100 W TWTA
    //   - DSN 34-m BWG: 34 m parabolic, η ≈ 0.65
    //   - 1 AU = 1.496e11 m (mean Earth-Mars distance, canonical reference)
    //   - X-band carrier: 8.4 GHz (NASA-JPL deep-space allocation)
    private static AntennaLinkDesign MroToDsnLink() => new(
        TransmitAntennaKind:     AntennaKind.ParabolicDish,
        ReceiveAntennaKind:      AntennaKind.ParabolicDish,
        Frequency_Hz:            8.4e9,
        TransmitPower_W:         100.0,
        LinkDistance_m:          1.496e11,
        TransmitDishDiameter_m:  3.0,
        ReceiveDishDiameter_m:   34.0,
        DishApertureEfficiency:  0.65);
}
