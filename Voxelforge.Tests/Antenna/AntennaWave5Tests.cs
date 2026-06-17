// AntennaWave5Tests.cs — Sprint ANT.W5 unit tests for:
//   ElevationSweepSolver  (orbital period, contact time, data volume)
//   LinkClosureMarginDistribution (rain-margin exceedance probability)
//   AntennaSystemResult   (field population)
//   PrintMaterial / PrintMaterialTable (tabulated properties)
//
// No PicoGK dependency — all tests are pure-physics / pure-algebraic.
// Passes under xUnit in the standard Voxelforge.Tests runner (no
// subprocess or Category=VoxelBuild annotation required).

using System;
using Voxelforge.Antenna;
using Xunit;

namespace Voxelforge.Tests.Antenna;

public sealed class AntennaWave5Tests
{
    // ── Test designs ───────────────────────────────────────────────────

    // DSN 34 m dish at X-band, MRO distance (link that already has a
    // well-tested fixture), with no rain statistics.
    private static AntennaLinkDesign MroDsn34Design() => new(
        TransmitAntennaKind:   AntennaKind.ParabolicDish,
        ReceiveAntennaKind:    AntennaKind.ParabolicDish,
        Frequency_Hz:          8.4e9,
        TransmitPower_W:       100.0,
        LinkDistance_m:        2.0e11,
        TransmitDishDiameter_m:  1.0,
        ReceiveDishDiameter_m:  34.0,
        OrbitalAltitude_km:    550.0,
        RainRate0p01pct_mmPerHr: 0.0);

    // Ku-band DBS with 63 mm/hr 0.01 % rain anchor (ITU zone K/L).
    private static AntennaLinkDesign DbsWithRainStats() => new(
        TransmitAntennaKind:   AntennaKind.ParabolicDish,
        ReceiveAntennaKind:    AntennaKind.ParabolicDish,
        Frequency_Hz:          12.2e9,
        TransmitPower_W:       200.0,
        LinkDistance_m:        3.58e7,
        TransmitDishDiameter_m:  0.6,
        ReceiveDishDiameter_m:   0.6,
        OrbitalAltitude_km:    35_786.0,  // GEO
        RainRate0p01pct_mmPerHr: 63.0,
        ElevationAngle_deg:    45.0);

    // ── ElevationSweepSolver: orbital period ───────────────────────────

    [Fact]
    public void ISS400km_OrbitalPeriod_IsApproximately92Minutes()
    {
        var design = MroDsn34Design() with { OrbitalAltitude_km = 400.0 };
        var result = ElevationSweepSolver.Solve(design);
        // ISS orbital period is well-known: 5 540–5 560 s (≈ 92.3 min).
        Assert.InRange(result.OrbitalPeriod_s, 5_400.0, 5_600.0);
    }

    [Fact]
    public void Leo550km_OrbitalPeriod_IsLongerThan400km()
    {
        var d400 = MroDsn34Design() with { OrbitalAltitude_km = 400.0 };
        var d550 = MroDsn34Design() with { OrbitalAltitude_km = 550.0 };
        double T400 = ElevationSweepSolver.Solve(d400).OrbitalPeriod_s;
        double T550 = ElevationSweepSolver.Solve(d550).OrbitalPeriod_s;
        Assert.True(T550 > T400,
            $"550 km orbital period {T550:F0} s should exceed 400 km period {T400:F0} s.");
    }

    [Fact]
    public void Leo550km_At10degMask_ContactTimePerPass_IsPhysicallyReasonable()
    {
        // 550 km LEO, 10° minimum elevation mask → overhead pass ≈ 5–12 min.
        var design = MroDsn34Design() with
        {
            OrbitalAltitude_km = 550.0,
            ElevationAngle_deg = 10.0
        };
        var result = ElevationSweepSolver.Solve(design);
        Assert.InRange(result.ContactTimePerPass_s, 300.0, 750.0);
    }

    [Fact]
    public void HigherElevationMask_ReducesContactTime()
    {
        var d10 = MroDsn34Design() with { ElevationAngle_deg = 10.0 };
        var d30 = MroDsn34Design() with { ElevationAngle_deg = 30.0 };
        double t10 = ElevationSweepSolver.Solve(d10).ContactTimePerPass_s;
        double t30 = ElevationSweepSolver.Solve(d30).ContactTimePerPass_s;
        Assert.True(t10 > t30,
            $"10° mask contact time {t10:F0} s should exceed 30° mask {t30:F0} s.");
    }

    [Fact]
    public void Leo550km_PassesPerDay_IsApproximately15()
    {
        var design = MroDsn34Design() with { OrbitalAltitude_km = 550.0 };
        var result = ElevationSweepSolver.Solve(design);
        Assert.InRange(result.PassesPerDay, 14.0, 16.0);
    }

    [Fact]
    public void OrbitalAltitude_IsEchoedInResult()
    {
        var design = MroDsn34Design() with { OrbitalAltitude_km = 400.0 };
        var result = ElevationSweepSolver.Solve(design);
        Assert.Equal(400.0, result.OrbitalAltitude_km, precision: 10);
    }

    [Fact]
    public void DataVolumePerPass_EqualsRateTimesContactTime()
    {
        var design = MroDsn34Design() with
        {
            OrbitalAltitude_km    = 550.0,
            BandwidthOccupancy_Hz = 1_000_000.0   // 1 MHz → 1 Mbps baseline
        };
        var result = ElevationSweepSolver.Solve(design);
        double expected = design.BandwidthOccupancy_Hz * result.ContactTimePerPass_s;
        Assert.Equal(expected, result.DataVolumePerPass_bits, precision: 6);
    }

    [Fact]
    public void DataVolumePerDay_EqualsPerPassTimesPassesPerDay()
    {
        var design = MroDsn34Design() with { OrbitalAltitude_km = 550.0 };
        var result = ElevationSweepSolver.Solve(design);
        double expected = result.DataVolumePerPass_bits * result.PassesPerDay;
        Assert.Equal(expected, result.DataVolumePerDay_bits, precision: 6);
    }

    [Fact]
    public void ClearSky_MarginExceedanceProbability_IsZero()
    {
        // RainRate0p01pct_mmPerHr = 0 → no rain statistics → 0 exceedance.
        var design = MroDsn34Design() with { RainRate0p01pct_mmPerHr = 0.0 };
        var result = ElevationSweepSolver.Solve(design);
        Assert.Equal(0.0, result.MarginExceedanceProbability, precision: 15);
    }

    // ── LinkClosureMarginDistribution ─────────────────────────────────

    [Fact]
    public void ClearSky_ExceedanceProbability_IsZero()
    {
        var design = MroDsn34Design() with { RainRate0p01pct_mmPerHr = 0.0 };
        Assert.Equal(0.0,
            LinkClosureMarginDistribution.ComputeExceedanceProbability(design),
            precision: 15);
    }

    [Fact]
    public void HighMarginDesign_WithRainStats_HasVerySmallExceedance()
    {
        // Strong link (34 m dish, 1 GHz, 10 W, 1 km) with rain stats.
        // The link closes easily even in heavy rain → exceedance ≈ 0.
        var design = new AntennaLinkDesign(
            TransmitAntennaKind:    AntennaKind.ParabolicDish,
            ReceiveAntennaKind:     AntennaKind.ParabolicDish,
            Frequency_Hz:          1e9,
            TransmitPower_W:       10.0,
            LinkDistance_m:        1_000.0,
            TransmitDishDiameter_m:  1.0,
            ReceiveDishDiameter_m:  34.0,
            ElevationAngle_deg:    30.0,
            RainRate0p01pct_mmPerHr: 63.0);
        double p = LinkClosureMarginDistribution.ComputeExceedanceProbability(design);
        // Should hold even at 63 mm/hr; exceedance well below 1e-4.
        Assert.InRange(p, 0.0, 1e-4);
    }

    [Fact]
    public void WeakDesign_WithHeavyRain_HasNonTrivialExceedance()
    {
        // Weak link: small dishes, short range, high frequency → rain matters.
        var design = new AntennaLinkDesign(
            TransmitAntennaKind:   AntennaKind.ParabolicDish,
            ReceiveAntennaKind:    AntennaKind.ParabolicDish,
            Frequency_Hz:          30e9,     // Ka-band (heavy rain impact)
            TransmitPower_W:       1.0,
            LinkDistance_m:        5_000e3,  // 5 000 km slant
            TransmitDishDiameter_m: 0.3,
            ReceiveDishDiameter_m:  0.3,
            ElevationAngle_deg:    10.0,
            RainRate0p01pct_mmPerHr: 63.0);
        double p = LinkClosureMarginDistribution.ComputeExceedanceProbability(design);
        // Link fails in the rain; exceedance should be significant.
        Assert.InRange(p, 1e-6, 1.0);
    }

    [Fact]
    public void HigherRain0p01pct_IncreasesExceedanceProbability()
    {
        // Same weak link but comparing two rain-zone anchors.
        var baseDesign = new AntennaLinkDesign(
            TransmitAntennaKind:   AntennaKind.ParabolicDish,
            ReceiveAntennaKind:    AntennaKind.ParabolicDish,
            Frequency_Hz:          20e9,
            TransmitPower_W:       1.0,
            LinkDistance_m:        2_000e3,
            TransmitDishDiameter_m: 0.3,
            ReceiveDishDiameter_m:  0.3,
            ElevationAngle_deg:    20.0);
        double p_z63  = LinkClosureMarginDistribution.ComputeExceedanceProbability(
            baseDesign with { RainRate0p01pct_mmPerHr = 63.0 });
        double p_z145 = LinkClosureMarginDistribution.ComputeExceedanceProbability(
            baseDesign with { RainRate0p01pct_mmPerHr = 145.0 });
        // Higher rain anchor → higher exceedance (power-law model).
        Assert.True(p_z145 >= p_z63,
            $"Tropical zone exceedance {p_z145:E2} should be ≥ mid-lat {p_z63:E2}.");
    }

    // ── PrintMaterialTable ────────────────────────────────────────────

    // PrintMaterial is internal; [Theory] parameter must be a publicly
    // accessible type (CS0051). Pass the enum's int ordinal and cast in body.
    [Theory]
    [InlineData((int)PrintMaterial.Lpbf316L,         0.3)]
    [InlineData((int)PrintMaterial.ConductiveFdmPla,  0.4)]
    [InlineData((int)PrintMaterial.SlaResinStandard,  0.1)]
    [InlineData((int)PrintMaterial.SlaResinRogers,    0.1)]
    public void MinFeatureDiameter_MatchesCatalogueValues(int materialOrdinal, double expected)
        => Assert.Equal(expected,
                        PrintMaterialTable.MinFeatureDiameter_mm((PrintMaterial)materialOrdinal),
                        precision: 10);

    [Theory]
    [InlineData((int)PrintMaterial.Lpbf316L,         45.0)]
    [InlineData((int)PrintMaterial.ConductiveFdmPla,  45.0)]
    [InlineData((int)PrintMaterial.SlaResinStandard,  90.0)]
    [InlineData((int)PrintMaterial.SlaResinRogers,    90.0)]
    public void MaxOverhangAngle_MatchesCatalogueValues(int materialOrdinal, double expected)
        => Assert.Equal(expected,
                        PrintMaterialTable.MaxOverhangAngle_deg((PrintMaterial)materialOrdinal),
                        precision: 10);

    [Theory]
    [InlineData((int)PrintMaterial.Lpbf316L,         1.0)]
    [InlineData((int)PrintMaterial.ConductiveFdmPla,  5.0)]
    [InlineData((int)PrintMaterial.SlaResinStandard,  3.2)]
    [InlineData((int)PrintMaterial.SlaResinRogers,    3.55)]
    public void RelativePermittivity_MatchesCatalogueValues(int materialOrdinal, double expected)
        => Assert.Equal(expected,
                        PrintMaterialTable.RelativePermittivity((PrintMaterial)materialOrdinal),
                        precision: 10);

    [Fact]
    public void PrintMaterialTable_ThrowsForUnknownMaterial()
    {
        var unknown = (PrintMaterial)99;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PrintMaterialTable.MinFeatureDiameter_mm(unknown));
    }

    // ── AntennaLinkDesign new-field validation ────────────────────────

    [Fact]
    public void ValidateSelf_ThrowsOnNegativeOrbitalAltitude()
    {
        var design = MroDsn34Design() with { OrbitalAltitude_km = -1.0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => design.ValidateSelf());
    }

    [Fact]
    public void ValidateSelf_ThrowsOnNegativeRainRate0p01()
    {
        var design = MroDsn34Design() with { RainRate0p01pct_mmPerHr = -1.0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => design.ValidateSelf());
    }

    [Fact]
    public void ValidateSelf_ThrowsOnNegativeSubstrateThickness()
    {
        var design = MroDsn34Design() with { SubstrateThickness_mm = -0.1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => design.ValidateSelf());
    }

    [Fact]
    public void ValidateSelf_ThrowsOnNegativeHelicalCoilDiameter()
    {
        var design = MroDsn34Design() with { HelicalCoilDiameter_mm = -1.0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => design.ValidateSelf());
    }

    // ── GEO contact window (longer orbital period) ────────────────────

    [Fact]
    public void GeoSatellite_OrbitalPeriod_IsApproximately24Hours()
    {
        var design = MroDsn34Design() with { OrbitalAltitude_km = 35_786.0 };
        var result = ElevationSweepSolver.Solve(design);
        // GEO period is 23 h 56 min (sidereal day) = 86 164 s ≈ 24 h.
        Assert.InRange(result.OrbitalPeriod_s, 85_000.0, 87_000.0);
    }
}
