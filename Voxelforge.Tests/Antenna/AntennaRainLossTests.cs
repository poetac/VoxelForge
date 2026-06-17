// AntennaRainLossTests.cs — Sprint ANT.W2 acceptance tests for the
// ITU-R P.838-3 rain attenuation and P.676-12 atmospheric absorption
// models, plus system-loss budget and LinkClosureMargin_dB gate.
//
// Anchors:
//   Rain attenuation — ITU-R P.838-3 (2005) Table 1 k_H / α_H values.
//     Ku-band (12 GHz) 25 mm/hr: γ_R = 0.0188 · 25^1.217 ≈ 2.4 dB/km.
//     Ka-band (30 GHz) 25 mm/hr: γ_R = 0.187 · 25^1.021 ≈ 4.9 dB/km.
//
//   Atmospheric absorption — ITU-R P.676-12 (2019) Fig. 1 standard
//     sea-level atmosphere: zenith ~0.03 dB at 10 GHz; ~0.46 dB at the
//     22.235 GHz H₂O resonance peak; ≫10 dB at 60 GHz O₂ complex.
//
//   Ka-band fixed-satellite link fixture (20 GHz, 1 m Tx + 1 m Rx dish,
//     100 W, 1000 km slant range, η = 0.65). Clear-sky budget:
//       G_tx ≈ G_rx ≈ 30.7 dBi; EIRP ≈ 50.7 dBW;
//       FSPL at 1000 km ≈ 178.4 dB;
//       P_rx ≈ 50.7 + 30.7 − 178.4 = −97 dBm + 30 = −97 dBm.
//     With LDPC R-1/2 (required Eb/N₀ ≈ 1 dB at 1 MHz BW, 3 dB NF):
//       sensitivity ≈ −111 dBm; clear-sky margin ≫ 0 → link closes.

using System;
using Voxelforge.Antenna;
using Xunit;

namespace Voxelforge.Tests.Antenna;

public sealed class AntennaRainLossTests
{
    // ── ItuAtmosphericModels — specific rain attenuation ─────────────────

    [Fact]
    public void SpecificRainAttenuation_IsZero_ForZeroRainRate()
    {
        Assert.Equal(0.0,
            ItuAtmosphericModels.SpecificRainAttenuation_dB_per_km(12e9, 0.0));
        Assert.Equal(0.0,
            ItuAtmosphericModels.SpecificRainAttenuation_dB_per_km(30e9, -1.0));
    }

    [Fact]
    public void SpecificRainAttenuation_KuBand_At25mmPerHr_InPublishedRange()
    {
        // 12 GHz, 25 mm/hr: k_H = 0.0188, α_H = 1.217
        // γ_R = 0.0188 · 25^1.217 ≈ 0.95 dB/km.
        // Acceptance band [0.5, 1.5] dB/km (±50% for table revision spread:
        // ITU-R P.838-1 vs P.838-3 differ ~20–30% at this frequency).
        double gamma = ItuAtmosphericModels.SpecificRainAttenuation_dB_per_km(
            12e9, 25.0);
        Assert.InRange(gamma, 0.5, 1.5);
    }

    [Fact]
    public void SpecificRainAttenuation_KaBand_At25mmPerHr_InPublishedRange()
    {
        // 30 GHz, 25 mm/hr: k_H = 0.187, α_H = 1.021
        // γ_R = 0.187 · 25^1.021 ≈ 4.89 dB/km.
        // Acceptance band [3.0, 8.0] dB/km.
        double gamma = ItuAtmosphericModels.SpecificRainAttenuation_dB_per_km(
            30e9, 25.0);
        Assert.InRange(gamma, 3.0, 8.0);
    }

    [Fact]
    public void SpecificRainAttenuation_KaBandHigherThanKuBand_AtSameRain()
    {
        // Ka-band has higher specific attenuation than Ku-band (both > 10 GHz).
        double ku = ItuAtmosphericModels.SpecificRainAttenuation_dB_per_km(
            12e9, 25.0);
        double ka = ItuAtmosphericModels.SpecificRainAttenuation_dB_per_km(
            30e9, 25.0);
        Assert.True(ka > ku, $"Ka-band ({ka:F2}) should exceed Ku-band ({ku:F2}).");
    }

    [Fact]
    public void SpecificRainAttenuation_IncreasesMonotonicallyWithRainRate()
    {
        // Higher rain rate → more specific attenuation (γ_R = k · R^α, α > 0).
        double lo = ItuAtmosphericModels.SpecificRainAttenuation_dB_per_km(
            20e9, 10.0);
        double hi = ItuAtmosphericModels.SpecificRainAttenuation_dB_per_km(
            20e9, 50.0);
        Assert.True(hi > lo,
            $"Higher rain rate must give higher specific attenuation ({lo:F3} → {hi:F3} dB/km).");
    }

    [Fact]
    public void SpecificRainAttenuation_XBand_IsLowerThanKuBand()
    {
        // X-band (8.4 GHz) has lower specific attenuation than Ku-band (12 GHz).
        double x  = ItuAtmosphericModels.SpecificRainAttenuation_dB_per_km(
            8.4e9, 25.0);
        double ku = ItuAtmosphericModels.SpecificRainAttenuation_dB_per_km(
            12e9, 25.0);
        Assert.True(x < ku,
            $"X-band ({x:F3}) should be lower than Ku-band ({ku:F3}) at same rain rate.");
    }

    // ── ItuAtmosphericModels — rain slant path attenuation ───────────────

    [Fact]
    public void RainSlantPath_IsZero_ForClearSky()
    {
        Assert.Equal(0.0,
            ItuAtmosphericModels.RainSlantPathAttenuation_dB(20e9, 30.0, 0.0));
    }

    [Fact]
    public void RainSlantPath_KaBand_25mmPerHr_30deg_InPublishedRange()
    {
        // 20 GHz, 25 mm/hr, 30° elevation.
        // γ_R ≈ 0.0751 · 25^1.099 ≈ 2.46 dB/km.
        // L_S = 3/sin(30°) = 6.0 km; L_G = 5.2 km;
        // d₀ = 35·exp(-0.015·25) = 24.0 km; r₀ = 1/(1+5.2/24) ≈ 0.82.
        // A_rain ≈ 2.46 · 6.0 · 0.82 ≈ 12.1 dB.
        // Band [6, 20] dB (P.618 path and k/α interp scatter).
        double a = ItuAtmosphericModels.RainSlantPathAttenuation_dB(
            20e9, 30.0, 25.0);
        Assert.InRange(a, 6.0, 20.0);
    }

    [Fact]
    public void RainSlantPath_IncreasesAtLowerElevation()
    {
        // Same rain at lower elevation → longer slant path → more attenuation.
        double hi = ItuAtmosphericModels.RainSlantPathAttenuation_dB(
            20e9, 60.0, 25.0);
        double lo = ItuAtmosphericModels.RainSlantPathAttenuation_dB(
            20e9, 20.0, 25.0);
        Assert.True(lo > hi,
            $"Lower elevation ({lo:F2} dB) must give more rain fade than higher ({hi:F2} dB).");
    }

    // ── ItuAtmosphericModels — atmospheric absorption ────────────────────

    [Fact]
    public void AtmosphericAbsorption_XBand_IsSmall()
    {
        // X-band (8-12 GHz) — zenith ≈ 0.03 dB; at 10° elevation ~0.17 dB.
        // Band [0.05, 0.5] dB.
        double a = ItuAtmosphericModels.AtmosphericAbsorption_dB(10e9, 10.0);
        Assert.InRange(a, 0.05, 0.50);
    }

    [Fact]
    public void AtmosphericAbsorption_H2OResonance_IsSignificant()
    {
        // 22.235 GHz H₂O resonance — zenith ≈ 0.46 dB;
        // at 30° elevation: 0.46/sin(30°) = 0.92 dB.
        // Band [0.3, 2.0] dB (significant vs X-band).
        double a = ItuAtmosphericModels.AtmosphericAbsorption_dB(22.235e9, 30.0);
        Assert.InRange(a, 0.3, 2.0);
    }

    [Fact]
    public void AtmosphericAbsorption_H2OResonance_ExceedsXBandByFactor()
    {
        // H₂O resonance absorption must exceed X-band by > 5× at same elevation.
        double xBand  = ItuAtmosphericModels.AtmosphericAbsorption_dB(10e9,     45.0);
        double h2oPeak = ItuAtmosphericModels.AtmosphericAbsorption_dB(22.235e9, 45.0);
        Assert.True(h2oPeak > 5.0 * xBand,
            $"H₂O peak ({h2oPeak:F3} dB) must exceed X-band ({xBand:F3} dB) by > 5×.");
    }

    [Fact]
    public void AtmosphericAbsorption_O2Band_IsVerylarge()
    {
        // 60 GHz O₂ complex — zenith ≈ 17 dB; at 45° ≈ 24 dB.
        // Band [10, 50] dB.
        double a = ItuAtmosphericModels.AtmosphericAbsorption_dB(60e9, 45.0);
        Assert.InRange(a, 10.0, 50.0);
    }

    [Fact]
    public void AtmosphericAbsorption_IncreasesAtLowerElevation()
    {
        // Same frequency — lower elevation → longer atmospheric path → more absorption.
        double high = ItuAtmosphericModels.AtmosphericAbsorption_dB(20e9, 60.0);
        double low  = ItuAtmosphericModels.AtmosphericAbsorption_dB(20e9, 10.0);
        Assert.True(low > high,
            $"Lower elevation ({low:F3} dB) must give higher absorption than 60° ({high:F3} dB).");
    }

    [Fact]
    public void AtmosphericAbsorption_ElevationClamp_At5Degrees()
    {
        // Elevation below 5° is clamped to 5° in the model.
        double at5    = ItuAtmosphericModels.AtmosphericAbsorption_dB(10e9, 5.0);
        double at2    = ItuAtmosphericModels.AtmosphericAbsorption_dB(10e9, 2.0);
        double at0p1  = ItuAtmosphericModels.AtmosphericAbsorption_dB(10e9, 0.1);
        Assert.Equal(at5, at2,   precision: 10);
        Assert.Equal(at5, at0p1, precision: 10);
    }

    // ── AntennaSolver.Solve — new ANT.W2 result fields ───────────────────

    [Fact]
    public void Solve_ClearSky_RainAttenuationIsZero()
    {
        var r = AntennaSolver.Solve(KaBandFixture());
        Assert.Equal(0.0, r.RainAttenuation_dB);
    }

    [Fact]
    public void Solve_ClearSky_AtmosphericAbsorptionIsPositive()
    {
        var r = AntennaSolver.Solve(KaBandFixture());
        Assert.True(r.AtmosphericAbsorption_dB > 0,
            "Atmospheric absorption must be > 0 even in clear sky.");
    }

    [Fact]
    public void Solve_ClearSky_SystemLoss_EqualsComponentSum()
    {
        var d = KaBandFixture();
        var r = AntennaSolver.Solve(d);
        double expected = r.RainAttenuation_dB
                        + r.AtmosphericAbsorption_dB
                        + d.PointingLoss_dB
                        + d.PolarisationMismatch_dB
                        + d.CableConnectorLoss_dB;
        Assert.Equal(expected, r.SystemLoss_dB, precision: 10);
    }

    [Fact]
    public void Solve_LinkClosureMargin_EqualsExpectedFormula()
    {
        var r = AntennaSolver.Solve(KaBandFixture());
        double expected = r.ReceivedPower_dBm
                        - r.SystemLoss_dB
                        - r.ReceiverSensitivity_dBm;
        Assert.Equal(expected, r.LinkClosureMargin_dB, precision: 10);
    }

    [Fact]
    public void Solve_KaBand_ClearSky_ShortRange_LinkCloses()
    {
        // 20 GHz, 100 W, 1 m dishes, 1000 km, η=0.65, LDPC R-1/2,
        // clear sky. G_tx ≈ G_rx ≈ 30.7 dBi; EIRP ≈ 50.7 dBW;
        // FSPL at 1000 km ≈ 178.4 dB; P_rx ≈ +3 dBm → link margin ≫ 0.
        var r = AntennaSolver.Solve(KaBandFixture());
        Assert.True(r.LinkClosureMargin_dB > 0,
            $"Ka-band clear-sky short-range link should close; margin = {r.LinkClosureMargin_dB:F1} dB.");
    }

    [Fact]
    public void Solve_HeavyRain_ReducesLinkClosureMargin()
    {
        // Same fixture but 50 mm/hr rain must reduce the link margin.
        var clearSky = AntennaSolver.Solve(KaBandFixture());
        var rainy    = AntennaSolver.Solve(KaBandFixture() with
        {
            RainRate_mmPerHr = 50.0
        });
        Assert.True(rainy.LinkClosureMargin_dB < clearSky.LinkClosureMargin_dB,
            $"Rain fade must reduce margin ({clearSky.LinkClosureMargin_dB:F1} → {rainy.LinkClosureMargin_dB:F1} dB).");
        Assert.True(rainy.RainAttenuation_dB > 0,
            "RainAttenuation_dB must be positive under rain.");
    }

    [Fact]
    public void Solve_LowerElevation_IncreasesSystemLoss()
    {
        // Lower elevation → longer atmospheric path (and rain path if raining).
        var high = AntennaSolver.Solve(KaBandFixture() with { ElevationAngle_deg = 60.0 });
        var low  = AntennaSolver.Solve(KaBandFixture() with { ElevationAngle_deg = 10.0 });
        Assert.True(low.SystemLoss_dB > high.SystemLoss_dB,
            $"Lower elevation must increase system loss ({high.SystemLoss_dB:F2} → {low.SystemLoss_dB:F2} dB).");
    }

    // ── AntennaLinkDesign.ValidateSelf — ANT.W2 new-field validation ─────

    [Theory]
    [InlineData(0.0)]      // zero elevation
    [InlineData(-10.0)]    // negative elevation
    [InlineData(91.0)]     // above 90°
    [InlineData(double.NaN)]
    public void ValidateSelf_RejectsInvalidElevationAngle(double el_deg)
    {
        var d = BaseDesign() with { ElevationAngle_deg = el_deg };
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void ValidateSelf_RejectsNegativeRainRate()
    {
        var d = BaseDesign() with { RainRate_mmPerHr = -1.0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void ValidateSelf_AcceptsZeroRainRate()
    {
        var d = BaseDesign() with { RainRate_mmPerHr = 0.0 };
        d.ValidateSelf(); // must not throw
    }

    [Fact]
    public void ValidateSelf_RejectsNegativePointingLoss()
    {
        var d = BaseDesign() with { PointingLoss_dB = -0.1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void ValidateSelf_RejectsNegativePolarisationMismatch()
    {
        var d = BaseDesign() with { PolarisationMismatch_dB = -0.1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void ValidateSelf_RejectsNegativeCableLoss()
    {
        var d = BaseDesign() with { CableConnectorLoss_dB = -0.1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void ValidateSelf_AcceptsZeroOptionalLosses()
    {
        // All loss fields at 0 is physically valid (ideal, no-loss system).
        var d = BaseDesign() with
        {
            PointingLoss_dB         = 0.0,
            PolarisationMismatch_dB = 0.0,
            CableConnectorLoss_dB   = 0.0,
        };
        d.ValidateSelf(); // must not throw
    }

    // ── Backwards-compatibility guard ─────────────────────────────────────

    [Fact]
    public void MroDsnLink_Wave1Outputs_UnchangedByAnt2Extension()
    {
        // The pre-ANT.W2 MRO-to-DSN test baseline (8.4 GHz, 100 W, 1 AU,
        // ParabolicDish both ends) must still land in the same output bands
        // as before — the new fields use defaults and ReceivedPower_dBm
        // is the unchanged Friis-only result.
        var d = new AntennaLinkDesign(
            TransmitAntennaKind:    AntennaKind.ParabolicDish,
            ReceiveAntennaKind:     AntennaKind.ParabolicDish,
            Frequency_Hz:           8.4e9,
            TransmitPower_W:        100.0,
            LinkDistance_m:         1.496e11,
            TransmitDishDiameter_m: 3.0,
            ReceiveDishDiameter_m:  34.0,
            DishApertureEfficiency: 0.65);
        var r = AntennaSolver.Solve(d);
        // Wave-1 Friis result — same bands as AntennaLinkFixture_MroToDsn34m.
        Assert.InRange(r.ReceivedPower_dBm, -115.0, -105.0);
        Assert.InRange(r.FreeSpacePathLoss_dB, 273.0, 277.0);
        // ANT.W2 fields are present, finite, and non-negative.
        Assert.True(double.IsFinite(r.RainAttenuation_dB));
        Assert.True(double.IsFinite(r.AtmosphericAbsorption_dB));
        Assert.Equal(0.0, r.RainAttenuation_dB);           // clear sky default
        Assert.True(r.AtmosphericAbsorption_dB > 0.0);     // always some absorption
        Assert.True(r.SystemLoss_dB > 0.0);
        Assert.True(double.IsFinite(r.LinkClosureMargin_dB));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // Ka-band fixed-satellite link: 20 GHz, 100 W, 1 m dishes (η=0.65),
    // 1000 km slant range, 30° elevation, LDPC R-1/2, clear sky.
    // This provides a positive-margin baseline for the ANT.W2 gate tests.
    private static AntennaLinkDesign KaBandFixture() => new(
        TransmitAntennaKind:    AntennaKind.ParabolicDish,
        ReceiveAntennaKind:     AntennaKind.ParabolicDish,
        Frequency_Hz:           20e9,
        TransmitPower_W:        100.0,
        LinkDistance_m:         1.0e6,
        TransmitDishDiameter_m: 1.0,
        ReceiveDishDiameter_m:  1.0,
        DishApertureEfficiency: 0.65,
        Modulation:             ModulationScheme.BpskLdpcR12,
        BandwidthOccupancy_Hz:  1.0e6,
        ElevationAngle_deg:     30.0,
        RainRate_mmPerHr:       0.0);

    // Minimal design used for ValidateSelf tests.
    private static AntennaLinkDesign BaseDesign() => new(
        TransmitAntennaKind: AntennaKind.IdealIsotropic,
        ReceiveAntennaKind:  AntennaKind.IdealIsotropic,
        Frequency_Hz:        10e9,
        TransmitPower_W:     1.0,
        LinkDistance_m:      1.0e5);
}
