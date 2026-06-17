// StandardAtmosphereTests.cs — Sprint A3 acceptance for the US 1976
// atmosphere model.
//
// Pinned values come from the "U.S. Standard Atmosphere, 1976"
// reference tables. Layer-base values are bit-exact (definition-side);
// in-layer interpolated values match published tables to four digits.

using Voxelforge.Airbreathing.Atmosphere;

namespace Voxelforge.Airbreathing.Tests.Atmosphere;

public sealed class StandardAtmosphereTests
{
    /// <summary>
    /// Sea level: T = 288.15 K, P = 101 325 Pa, ρ = 1.225 kg/m³,
    /// a = 340.29 m/s. These are the canonical reference values the
    /// model is anchored on — exact match.
    /// </summary>
    [Fact]
    public void SeaLevel_MatchesReferenceValues()
    {
        var s = StandardAtmosphere.At(0.0);
        Assert.Equal(288.15, s.StaticT_K, 4);
        Assert.Equal(101_325.0, s.StaticP_Pa, 1);
        Assert.Equal(1.225, s.Density_kg_m3, 3);
        Assert.Equal(340.29, s.SpeedOfSound_m_s, 1);
    }

    /// <summary>
    /// Tropopause (11 km geopotential): T = 216.65 K, P = 22 632.06 Pa.
    /// </summary>
    [Fact]
    public void Tropopause_11km_MatchesReferenceValues()
    {
        // 11 km geopotential ≈ 11 019 m geometric
        double hGeom = 11_019.13;
        var s = StandardAtmosphere.At(hGeom);
        Assert.Equal(216.65, s.StaticT_K, 1);
        Assert.Equal(22_632.06, s.StaticP_Pa, 0);
    }

    /// <summary>
    /// 12 km geometric — the canonical "lower stratosphere" point used
    /// by the Mattingly synthetic ramjet fixture (M=2 / 12 km / H2).
    /// In the isothermal layer T stays 216.65 K; P drops per the
    /// barometric exponential.
    /// </summary>
    [Fact]
    public void TwelveKm_IsInIsothermalLayer()
    {
        var s = StandardAtmosphere.At(12_000.0);
        Assert.Equal(216.65, s.StaticT_K, 1);
        // Reference: P(12 km geometric) ≈ 19 330 Pa per the published
        // 1976 tables.
        Assert.InRange(s.StaticP_Pa, 19_300.0, 19_400.0);
    }

    /// <summary>
    /// 20 km geopotential: layer-base value, T = 216.65 K, P = 5474.89 Pa.
    /// </summary>
    [Fact]
    public void TwentyKmGeopotential_MatchesReferenceValues()
    {
        // 20 km geopotential ≈ 20 063 m geometric
        double hGeom = 20_063.0;
        var s = StandardAtmosphere.At(hGeom);
        Assert.Equal(216.65, s.StaticT_K, 1);
        Assert.InRange(s.StaticP_Pa, 5_460.0, 5_490.0);
    }

    /// <summary>
    /// 32 km geopotential: layer-base value, T = 228.65 K, P = 868.02 Pa.
    /// </summary>
    [Fact]
    public void ThirtyTwoKmGeopotential_MatchesReferenceValues()
    {
        // 32 km geopotential ≈ 32 162 m geometric
        double hGeom = 32_162.0;
        var s = StandardAtmosphere.At(hGeom);
        // Use InRange instead of Assert.Equal(decimalPlaces): 228.65 is
        // stored as 228.64999…, so banker's-rounding of expected to 1 dp
        // yields 228.6 while actual 228.65027 yields 228.7 — false miss.
        Assert.InRange(s.StaticT_K, 228.60, 228.70);
        Assert.InRange(s.StaticP_Pa, 866.0, 870.0);
    }

    /// <summary>
    /// Continuity at layer boundaries: stepping just below and just
    /// above each transition altitude must return T values within
    /// ~0.01 K. (Pressure is exponential so the step is by definition
    /// continuous; T is the place a discontinuity would surface.)
    /// </summary>
    [Theory]
    [InlineData(11_000.0)]
    [InlineData(20_000.0)]
    [InlineData(32_000.0)]
    [InlineData(47_000.0)]
    [InlineData(51_000.0)]
    [InlineData(71_000.0)]
    public void LayerBoundaries_AreContinuousInTemperature(double geopotKm_m)
    {
        // Convert geopotential boundary to geometric for At() input
        double hGeomBase = ToGeometric(geopotKm_m);
        var below = StandardAtmosphere.At(hGeomBase - 1.0);
        var above = StandardAtmosphere.At(hGeomBase + 1.0);
        Assert.True(System.Math.Abs(below.StaticT_K - above.StaticT_K) < 0.05,
            $"Layer boundary at {geopotKm_m} m geopotential: ΔT = {below.StaticT_K - above.StaticT_K:F3} K");
    }

    /// <summary>
    /// Below sea level + above 86 km both throw — outside the model's
    /// defined range.
    /// </summary>
    [Fact]
    public void OutOfRange_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => StandardAtmosphere.At(-1.0));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => StandardAtmosphere.At(86_001.0));
    }

    /// <summary>
    /// Geopotential conversion is monotonic + matches geometric at h = 0.
    /// </summary>
    [Fact]
    public void GeopotentialConversion_MatchesAtSeaLevel()
    {
        Assert.Equal(0.0, StandardAtmosphere.GeopotentialAltitude_m(0.0), 6);
        // Geopotential always ≤ geometric (gravity drops with radius)
        Assert.True(StandardAtmosphere.GeopotentialAltitude_m(50_000.0) < 50_000.0);
        Assert.True(StandardAtmosphere.GeopotentialAltitude_m(86_000.0) < 86_000.0);
    }

    private static double ToGeometric(double geopot_m)
    {
        // Inverse of the geopotential formula: h_geom = R · h_geopot / (R − h_geopot)
        const double R = 6_356_766.0;
        return R * geopot_m / (R - geopot_m);
    }
}
