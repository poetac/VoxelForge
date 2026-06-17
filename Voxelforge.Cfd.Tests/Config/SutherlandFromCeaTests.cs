// SutherlandFromCeaTests.cs — unit tests for SutherlandFromCea (issue #480).
//
// Tolerance philosophy: per-pair S values are placeholder ballparks pending a
// CEA mass-fraction-blended Sutherland fit (acceptance criterion #4). Tests
// use ±5 K tolerance per acceptance criterion #5 so a CEA-derived swap is
// mechanical (no test edits required).

using Voxelforge.Cfd.Config;
using Voxelforge.Combustion;
using Xunit;

namespace Voxelforge.Cfd.Tests.Config;

public sealed class SutherlandFromCeaTests
{
    private const double TolK = 5.0;

    // ── Per-pair lookup hits ────────────────────────────────────────────────

    [Fact]
    public void Lookup_LoxCh4_ReturnsCeaSourceWithPairLabel()
    {
        var r = SutherlandFromCea.Lookup(PropellantPair.LOX_CH4, chamberTemp_K: 3450.0);
        Assert.Equal(SutherlandSource.Cea, r.Source);
        Assert.Equal("LOX/CH4", r.PairLabel);
        // Water-dominated blend with mid-band carbon contribution; expect ~200 K.
        Assert.InRange(r.SutherlandS_K, 197.0 - TolK, 197.0 + TolK);
    }

    [Fact]
    public void Lookup_LoxH2_ReturnsLowerSReflectingHydrogenContribution()
    {
        var r = SutherlandFromCea.Lookup(PropellantPair.LOX_H2, chamberTemp_K: 3300.0);
        Assert.Equal(SutherlandSource.Cea, r.Source);
        Assert.Equal("LOX/H2", r.PairLabel);
        // H₂ has anomalously low S (Svehla 1962); H₂-rich blend lands ~100 K.
        Assert.InRange(r.SutherlandS_K, 97.0 - TolK, 97.0 + TolK);
    }

    [Fact]
    public void Lookup_LoxRP1_ReturnsHigherSReflectingCarbonRichBlend()
    {
        var r = SutherlandFromCea.Lookup(PropellantPair.LOX_RP1, chamberTemp_K: 3600.0);
        Assert.Equal(SutherlandSource.Cea, r.Source);
        Assert.Equal("LOX/RP-1", r.PairLabel);
        // CO/CO₂-dominated blend → S higher than LOX/CH4 at the same T_ref.
        Assert.InRange(r.SutherlandS_K, 240.0 - TolK, 240.0 + TolK);
    }

    [Fact]
    public void Lookup_PerPairValuesAreDistinct()
    {
        // Sanity: the three implemented pairs must produce distinct S values
        // (otherwise the per-pair override delivers nothing over the formula).
        double sCh4 = SutherlandFromCea.Lookup(PropellantPair.LOX_CH4, 3450.0).SutherlandS_K;
        double sH2  = SutherlandFromCea.Lookup(PropellantPair.LOX_H2,  3300.0).SutherlandS_K;
        double sRP1 = SutherlandFromCea.Lookup(PropellantPair.LOX_RP1, 3600.0).SutherlandS_K;
        Assert.NotEqual(sCh4, sH2);
        Assert.NotEqual(sCh4, sRP1);
        Assert.NotEqual(sH2,  sRP1);
        // Directional: H₂-rich < CH4 < RP-1.
        Assert.True(sH2 < sCh4);
        Assert.True(sCh4 < sRP1);
    }

    // ── Fallback paths ──────────────────────────────────────────────────────

    [Fact]
    public void Lookup_NullPair_FallsBackToBartzSlope()
    {
        // null Pair → Sprint C.2 fallback (S = T_c / 9).
        var r = SutherlandFromCea.Lookup(pair: null, chamberTemp_K: 3500.0);
        Assert.Equal(SutherlandSource.BartzSlope, r.Source);
        Assert.Equal(string.Empty, r.PairLabel);
        Assert.Equal(3500.0 / 9.0, r.SutherlandS_K, precision: 6);
    }

    [Fact]
    public void Lookup_UnimplementedPair_FallsBackToBartzSlope()
    {
        // N₂O₄/MMH and H₂O₂/RP-1 are declared but no per-pair entry exists.
        // Expected: same fallback as null Pair.
        var r = SutherlandFromCea.Lookup(PropellantPair.N2O4_MMH, chamberTemp_K: 3000.0);
        Assert.Equal(SutherlandSource.BartzSlope, r.Source);
        Assert.Equal(string.Empty, r.PairLabel);
        Assert.Equal(3000.0 / 9.0, r.SutherlandS_K, precision: 6);
    }

    [Fact]
    public void Lookup_DegenerateChamberTempInFallback_UsesAirAirBaseline()
    {
        // Same degenerate-input behaviour as Su2ConfigWriter.SutherlandConstantFromBartzSlope.
        var r = SutherlandFromCea.Lookup(pair: null, chamberTemp_K: 0.0);
        Assert.Equal(SutherlandSource.BartzSlope, r.Source);
        Assert.Equal(110.4, r.SutherlandS_K);
    }

    // ── IsImplemented ───────────────────────────────────────────────────────

    [Fact]
    public void IsImplemented_TrueForThreeRocketPairs_FalseForOthers()
    {
        Assert.True(SutherlandFromCea.IsImplemented(PropellantPair.LOX_CH4));
        Assert.True(SutherlandFromCea.IsImplemented(PropellantPair.LOX_H2));
        Assert.True(SutherlandFromCea.IsImplemented(PropellantPair.LOX_RP1));
        Assert.False(SutherlandFromCea.IsImplemented(PropellantPair.N2O4_MMH));
        Assert.False(SutherlandFromCea.IsImplemented(PropellantPair.H2O2_RP1));
    }
}
