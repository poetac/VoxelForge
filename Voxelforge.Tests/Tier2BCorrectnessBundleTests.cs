// Tier2BCorrectnessBundleTests.cs — coverage for the second Tier-2
// micro-fix wave (A5 LH2 viscosity + L3 Sobol off-by-one).

using Voxelforge.Combustion;
using Voxelforge.Injector;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class Tier2BCorrectnessBundleTests
{
    // ─── A5 — InjectionViscosities ────────────────────────────────

    [Fact]
    public void InjectionViscosities_LOX_CH4_HasFluidAccurateValues()
    {
        var (oxMu, fuelMu) = OrificeModel.InjectionViscosities(PropellantPair.LOX_CH4);
        Assert.Equal(OrificeModel.ReferenceViscosity_PaS.LOX, oxMu);
        Assert.Equal(OrificeModel.ReferenceViscosity_PaS.LCH4, fuelMu);
    }

    [Fact]
    public void InjectionViscosities_LOX_H2_FuelMuMatchesLH2_NotLOXFallback()
    {
        // The pre-A5 LineLoss fallback (3e-4 Pa·s) was 25× higher than real
        // LH2 viscosity (1.3e-5). Confirm the new lookup returns the LH2
        // value, not the LOX-default fallback.
        var (oxMu, fuelMu) = OrificeModel.InjectionViscosities(PropellantPair.LOX_H2);
        Assert.Equal(OrificeModel.ReferenceViscosity_PaS.LOX, oxMu);
        Assert.Equal(OrificeModel.ReferenceViscosity_PaS.LH2, fuelMu);
        Assert.True(fuelMu < 1e-4, $"LH2 μ should be << 1e-4 Pa·s; got {fuelMu}");
        Assert.True(fuelMu * 20 < 3e-4,
            "LH2 μ should be at least 20× lower than the pre-A5 LineLoss fallback (3e-4)");
    }

    [Fact]
    public void InjectionViscosities_LOX_RP1_RP1IsHighest()
    {
        var (oxMu, fuelMu) = OrificeModel.InjectionViscosities(PropellantPair.LOX_RP1);
        Assert.Equal(OrificeModel.ReferenceViscosity_PaS.LOX, oxMu);
        Assert.Equal(OrificeModel.ReferenceViscosity_PaS.RP1, fuelMu);
        // Ambient kerosene is much more viscous than cryo LOX.
        Assert.True(fuelMu > oxMu);
    }

    [Theory]
    [InlineData(PropellantPair.LOX_CH4)]
    [InlineData(PropellantPair.LOX_H2)]
    [InlineData(PropellantPair.LOX_RP1)]
    [InlineData(PropellantPair.N2O4_MMH)]
    [InlineData(PropellantPair.H2O2_RP1)]
    public void InjectionViscosities_AllPairs_ReturnPositiveFiniteValues(PropellantPair pair)
    {
        var (oxMu, fuelMu) = OrificeModel.InjectionViscosities(pair);
        Assert.True(oxMu > 0 && double.IsFinite(oxMu));
        Assert.True(fuelMu > 0 && double.IsFinite(fuelMu));
        // Sanity: any liquid propellant μ at injection T sits in (1e-6, 1e-2) Pa·s.
        Assert.InRange(oxMu, 1e-6, 1e-2);
        Assert.InRange(fuelMu, 1e-6, 1e-2);
    }

    // ─── L3 — Sobol chain-slice off-by-one ────────────────────────

    [Fact]
    public void ChainSlice_Slice0_FirstPoint_IsSobolIndex1()
    {
        // Pre-L3 the slice consumed Sobol index 2 first (the "+1" on
        // SkipTo combined with the SkipTo + Next() composition leaves
        // the cursor at index 2 by the time the first point is
        // returned). Post-L3 slice 0 should consume index 1 — the
        // first non-origin Sobol point.
        var ref_ = new SobolSequence(4);
        var index1 = ref_.Next();   // step 1, the first non-origin point

        var slice0 = SobolSequence.ChainSlice(dimensions: 4, count: 1,
                                              sliceIndex: 0, totalSlices: 4);
        Assert.Equal(index1, slice0[0]);
    }

    [Fact]
    public void ChainSlice_SliceK_FirstPoint_IsSobolIndexKPlus1()
    {
        // Slice k starts at Sobol index k+1, then strides by totalSlices.
        for (int k = 0; k < 4; k++)
        {
            var ref_ = new SobolSequence(4);
            for (int i = 0; i < k; i++) ref_.Next();   // skip 0..k-1
            var indexKPlus1 = ref_.Next();             // step k+1

            var sliceK = SobolSequence.ChainSlice(dimensions: 4, count: 1,
                                                  sliceIndex: k, totalSlices: 4);
            Assert.Equal(indexKPlus1, sliceK[0]);
        }
    }

    [Fact]
    public void ChainSlice_PreservesDeterminism_PostL3()
    {
        // L3 only fixed the documented-vs-actual offset; same sliceIndex +
        // totalSlices must still produce identical points across calls.
        var a = SobolSequence.ChainSlice(dimensions: 8, count: 16, sliceIndex: 2, totalSlices: 4);
        var b = SobolSequence.ChainSlice(dimensions: 8, count: 16, sliceIndex: 2, totalSlices: 4);
        for (int i = 0; i < 16; i++) Assert.Equal(a[i], b[i]);
    }

    [Fact]
    public void ChainSlice_DistinctSlices_RemainDisjoint_PostL3()
    {
        // The bug fix doesn't break the disjointness invariant — different
        // slices must still produce different point sequences.
        var s0 = SobolSequence.ChainSlice(dimensions: 8, count: 4, sliceIndex: 0, totalSlices: 4);
        var s1 = SobolSequence.ChainSlice(dimensions: 8, count: 4, sliceIndex: 1, totalSlices: 4);
        var s2 = SobolSequence.ChainSlice(dimensions: 8, count: 4, sliceIndex: 2, totalSlices: 4);
        var s3 = SobolSequence.ChainSlice(dimensions: 8, count: 4, sliceIndex: 3, totalSlices: 4);
        Assert.NotEqual(s0[0], s1[0]);
        Assert.NotEqual(s1[0], s2[0]);
        Assert.NotEqual(s2[0], s3[0]);
        Assert.NotEqual(s0[0], s3[0]);
    }
}
