// MuRefFromCeaTests.cs — unit tests for MuRefFromCea (issue #485).
//
// Mirrors SutherlandFromCeaTests with ±10 % tolerance on the per-pair μ_ref
// values (Sutherland S has K-units; μ_ref has Pa·s with much wider absolute
// span across pairs, so percent-tolerance is the natural unit).

using Voxelforge.Cfd.Config;
using Voxelforge.Combustion;
using Xunit;

namespace Voxelforge.Cfd.Tests.Config;

public sealed class MuRefFromCeaTests
{
    private const double FracTol = 0.10; // ±10 % tolerance for placeholder swap

    private static void AssertWithin(double expected, double actual, double fracTol)
    {
        double lo = expected * (1.0 - fracTol);
        double hi = expected * (1.0 + fracTol);
        Assert.InRange(actual, lo, hi);
    }

    // ── Per-pair lookup hits ────────────────────────────────────────────────

    [Fact]
    public void Lookup_LoxCh4_ReturnsCeaSourceCloseToBaselineFormula()
    {
        // Baseline formula at Tc=3450: μ ≈ 1.0e-4 · (3450/3500)^0.7 ≈ 0.99e-4.
        // Per-pair LOX/CH4 should sit close (water + carbon mix dominates).
        var r = MuRefFromCea.Lookup(PropellantPair.LOX_CH4, fallback_PaS: 9.9e-5);
        Assert.Equal(MuRefSource.Cea, r.Source);
        Assert.Equal("LOX/CH4", r.PairLabel);
        AssertWithin(9.5e-5, r.MuRef_PaS, FracTol);
    }

    [Fact]
    public void Lookup_LoxH2_ReturnsLowerMuReflectingLightSpecies()
    {
        // H₂-rich blend has lighter species; H₂ μ ≈ 1/3 of H₂O μ at 3000 K.
        var r = MuRefFromCea.Lookup(PropellantPair.LOX_H2, fallback_PaS: 9.6e-5);
        Assert.Equal(MuRefSource.Cea, r.Source);
        Assert.Equal("LOX/H2", r.PairLabel);
        AssertWithin(8.5e-5, r.MuRef_PaS, FracTol);
    }

    [Fact]
    public void Lookup_LoxRP1_ReturnsHigherMuReflectingCarbonRichBlend()
    {
        // CO/CO₂-dominated, heavier products → μ slightly higher than LOX/CH4.
        var r = MuRefFromCea.Lookup(PropellantPair.LOX_RP1, fallback_PaS: 1.02e-4);
        Assert.Equal(MuRefSource.Cea, r.Source);
        Assert.Equal("LOX/RP-1", r.PairLabel);
        AssertWithin(1.05e-4, r.MuRef_PaS, FracTol);
    }

    [Fact]
    public void Lookup_PerPairValuesAreDistinct()
    {
        // Sanity: the three pairs must produce distinct μ_ref values.
        double mCh4 = MuRefFromCea.Lookup(PropellantPair.LOX_CH4, fallback_PaS: 1e-4).MuRef_PaS;
        double mH2  = MuRefFromCea.Lookup(PropellantPair.LOX_H2,  fallback_PaS: 1e-4).MuRef_PaS;
        double mRP1 = MuRefFromCea.Lookup(PropellantPair.LOX_RP1, fallback_PaS: 1e-4).MuRef_PaS;
        Assert.NotEqual(mCh4, mH2);
        Assert.NotEqual(mCh4, mRP1);
        Assert.NotEqual(mH2,  mRP1);
        // Directional: H₂-rich < CH4 < RP-1 (lighter blend → lower μ).
        Assert.True(mH2 < mCh4);
        Assert.True(mCh4 < mRP1);
    }

    // ── Fallback paths ──────────────────────────────────────────────────────

    [Fact]
    public void Lookup_NullPair_FallsBackToProvidedValue()
    {
        // null Pair → returns the provided fallback unchanged.
        var r = MuRefFromCea.Lookup(pair: null, fallback_PaS: 9.9e-5);
        Assert.Equal(MuRefSource.CeaTableFormula, r.Source);
        Assert.Equal(string.Empty, r.PairLabel);
        Assert.Equal(9.9e-5, r.MuRef_PaS, precision: 12);
    }

    [Fact]
    public void Lookup_UnimplementedPair_FallsBackToProvidedValue()
    {
        var r = MuRefFromCea.Lookup(PropellantPair.N2O4_MMH, fallback_PaS: 8.0e-5);
        Assert.Equal(MuRefSource.CeaTableFormula, r.Source);
        Assert.Equal(string.Empty, r.PairLabel);
        Assert.Equal(8.0e-5, r.MuRef_PaS, precision: 12);
    }

    // ── IsImplemented ───────────────────────────────────────────────────────

    [Fact]
    public void IsImplemented_TrueForThreeRocketPairs_FalseForOthers()
    {
        Assert.True(MuRefFromCea.IsImplemented(PropellantPair.LOX_CH4));
        Assert.True(MuRefFromCea.IsImplemented(PropellantPair.LOX_H2));
        Assert.True(MuRefFromCea.IsImplemented(PropellantPair.LOX_RP1));
        Assert.False(MuRefFromCea.IsImplemented(PropellantPair.N2O4_MMH));
        Assert.False(MuRefFromCea.IsImplemented(PropellantPair.H2O2_RP1));
    }
}
