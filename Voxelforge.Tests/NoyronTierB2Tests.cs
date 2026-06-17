// NoyronTierB2Tests.cs — Tier B2 forcing-function suite. Covers:
//   • EquilibriumCorrection static registry: None = identity;
//     Parameterized respects per-pair coefficients; unsupported pair
//     falls back to identity.
//   • Coefficient lookup coverage + envelope-factor shape (peaks at
//     MR_peak, decays symmetrically).
//   • LogPcDissociationCorrection physics: C* rises with Pc above
//     reference; falls below reference; zero shift at Pc_ref and at
//     MR far from peak; bounded factors.
//   • PropellantTables.UseEquilibrium round-trip through Lookup.
//   • AutoSeeder flag recommendation: true at Pc > 10 MPa; false below;
//     rationale line present.
//   • Cache key includes UseEquilibrium so flipping mid-session does
//     not serve stale entries.
//
// All tests are pure-math — no PicoGK Library required. Each test
// restores the flag in a `try/finally`. Issue #311: classes ALSO join
// the PropellantTablesGlobalStateCollection so xUnit serialises them
// against the other classes that mutate UseEquilibrium /
// EquilibriumCorrectionProvider — without that collection
// membership, parallel xUnit execution could observe a partially-
// mutated flag in a sibling class' cache lookup mid-test.

using Voxelforge.Combustion;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

[Collection(PropellantTablesGlobalStateCollection.Name)]
public class NoyronTierB2Tests
{
    // ══════════════════════ Registry ══════════════════════

    [Fact]
    public void EquilibriumCorrection_NoneIsIdentity()
    {
        var s = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 7e6);
        var corrected = EquilibriumCorrection.None.Correct(s, PropellantPair.LOX_CH4);
        Assert.Equal(s, corrected);
        Assert.Equal("None", EquilibriumCorrection.None.Name);
    }

    [Fact]
    public void EquilibriumCorrection_ParameterizedIsNonIdentity()
    {
        // At Pc far from reference, correction shifts C*.
        var s = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 20e6);
        var corrected = EquilibriumCorrection.Parameterized.Correct(s, PropellantPair.LOX_CH4);
        Assert.NotEqual(s.CStar_ms, corrected.CStar_ms);
        Assert.Contains("Parameterized", EquilibriumCorrection.Parameterized.Name);
    }

    [Theory]
    [InlineData(PropellantPair.LOX_CH4)]
    [InlineData(PropellantPair.LOX_H2)]
    [InlineData(PropellantPair.LOX_RP1)]
    public void EquilibriumCorrection_CoefficientsPopulatedForImplementedPairs(PropellantPair pair)
    {
        var c = EquilibriumCorrection.For(pair);
        Assert.True(c.Kappa_C > 0);
        Assert.True(c.Kappa_T > 0);
        Assert.True(c.MR_peak > 0);
        Assert.True(c.MR_sigma > 0);
    }

    [Fact]
    public void EquilibriumCorrection_UnsupportedPairReturnsZeroCoefficients()
    {
        var c = EquilibriumCorrection.For(PropellantPair.N2O4_MMH);
        Assert.Equal(0.0, c.Kappa_C);
        Assert.Equal(0.0, c.Kappa_T);
    }

    // ══════════════════════ Envelope factor ══════════════════════

    [Fact]
    public void EnvelopeFactor_PeaksAtMrPeak()
    {
        var c = EquilibriumCorrection.For(PropellantPair.LOX_CH4);
        double atPeak = EquilibriumCorrection.EnvelopeFactor(c.MR_peak, c);
        Assert.InRange(atPeak, 0.999, 1.001);
    }

    [Fact]
    public void EnvelopeFactor_DecaysSymmetrically()
    {
        var c = EquilibriumCorrection.For(PropellantPair.LOX_CH4);
        double above = EquilibriumCorrection.EnvelopeFactor(c.MR_peak + c.MR_sigma, c);
        double below = EquilibriumCorrection.EnvelopeFactor(c.MR_peak - c.MR_sigma, c);
        Assert.Equal(above, below, 6);
        Assert.InRange(above, 0.55, 0.65);   // exp(-0.5) ≈ 0.607
    }

    [Fact]
    public void EnvelopeFactor_VanishesFarFromPeak()
    {
        var c = EquilibriumCorrection.For(PropellantPair.LOX_CH4);
        double far = EquilibriumCorrection.EnvelopeFactor(c.MR_peak + 4 * c.MR_sigma, c);
        Assert.True(far < 0.01);
    }

    // ══════════════════════ Physics direction ══════════════════════

    [Fact]
    public void Correction_RaisesCStarAbovePcReference()
    {
        // Le Chatelier: higher Pc ⇒ less dissociation ⇒ higher
        // equilibrium C* than frozen. LOX/CH4 at MR_peak = 3.2,
        // Pc = 20 MPa > Pc_ref (7 MPa).
        var frozen    = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.2, 20e6);
        var corrected = EquilibriumCorrection.Parameterized.Correct(
            frozen, PropellantPair.LOX_CH4);
        Assert.True(corrected.CStar_ms > frozen.CStar_ms,
            $"C*_eq ({corrected.CStar_ms:F1}) should exceed C*_frozen ({frozen.CStar_ms:F1}) at Pc > Pc_ref.");
    }

    [Fact]
    public void Correction_LowersCStarBelowPcReference()
    {
        var frozen    = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.2, 2e6);
        var corrected = EquilibriumCorrection.Parameterized.Correct(
            frozen, PropellantPair.LOX_CH4);
        Assert.True(corrected.CStar_ms < frozen.CStar_ms,
            $"C*_eq ({corrected.CStar_ms:F1}) should fall below C*_frozen ({frozen.CStar_ms:F1}) at Pc < Pc_ref.");
    }

    [Fact]
    public void Correction_IdentityAtReferencePressure()
    {
        var frozen    = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.2,
            EquilibriumCorrection.ReferencePc_Pa);
        var corrected = EquilibriumCorrection.Parameterized.Correct(
            frozen, PropellantPair.LOX_CH4);
        Assert.InRange(corrected.CStar_ms / frozen.CStar_ms, 0.9999, 1.0001);
    }

    [Fact]
    public void Correction_TinyFarFromPeakMR()
    {
        var c = EquilibriumCorrection.For(PropellantPair.LOX_CH4);
        double mrFar = c.MR_peak + 4 * c.MR_sigma;
        var frozen    = PropellantTables.Lookup(PropellantPair.LOX_CH4, mrFar, 20e6);
        var corrected = EquilibriumCorrection.Parameterized.Correct(
            frozen, PropellantPair.LOX_CH4);
        // Far from peak, the envelope kills the correction → ~identity.
        double ratio = corrected.CStar_ms / frozen.CStar_ms;
        Assert.InRange(ratio, 0.999, 1.001);
    }

    [Fact]
    public void Correction_FactorsAreBoundedOnExtremePressure()
    {
        // Drive Pc to 100 MPa (insane) — correction must not blow up.
        var frozen    = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.2, 100e6);
        var corrected = EquilibriumCorrection.Parameterized.Correct(
            frozen, PropellantPair.LOX_CH4);
        double tcRatio    = corrected.ChamberTemp_K / frozen.ChamberTemp_K;
        double cStarRatio = corrected.CStar_ms      / frozen.CStar_ms;
        Assert.InRange(tcRatio,    0.85, 1.15);
        Assert.InRange(cStarRatio, 0.92, 1.08);
    }

    [Fact]
    public void Correction_UnsupportedPairIsIdentity()
    {
        var s = new PropellantState(
            MixtureRatio: 2.0, ChamberPressure_Pa: 20e6,
            ChamberTemp_K: 3000, GammaChamber: 1.2, GammaThroat: 1.2,
            MolecularWeight: 20,
            SpecificGasConst: 416, Cp_Jkg: 2500, Viscosity_PaS: 1e-4,
            Prandtl: 0.7, CStar_ms: 1700, IspVacuum_s: 320,
            PropellantName: "synthetic");
        var corrected = EquilibriumCorrection.Parameterized.Correct(
            s, PropellantPair.N2O4_MMH);
        Assert.Equal(s, corrected);
    }

    // ══════════════════════ PropellantTables integration ══════════════════════

    [Fact]
    public void PropellantTables_UseEquilibriumDefaultsFalse()
    {
        Assert.False(PropellantTables.UseEquilibrium);
    }

    [Fact]
    public void PropellantTables_LookupRespectsUseEquilibrium()
    {
        bool prior = PropellantTables.UseEquilibrium;
        var  priorProvider = PropellantTables.EquilibriumCorrectionProvider;
        try
        {
            PropellantTables.ClearLookupCacheForTests();
            PropellantTables.EquilibriumCorrectionProvider = EquilibriumCorrection.Parameterized;
            PropellantTables.UseEquilibrium = false;
            var frozen = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.2, 20e6);
            PropellantTables.UseEquilibrium = true;
            var eq = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.2, 20e6);

            Assert.NotEqual(frozen.CStar_ms, eq.CStar_ms);
            Assert.True(eq.CStar_ms > frozen.CStar_ms);
        }
        finally
        {
            PropellantTables.UseEquilibrium = prior;
            PropellantTables.EquilibriumCorrectionProvider = priorProvider;
            PropellantTables.ClearLookupCacheForTests();
        }
    }

    [Fact]
    public void PropellantTables_CacheKeyIncludesUseEquilibrium()
    {
        // If the cache didn't key on UseEquilibrium, flipping the
        // flag would return the cached (wrong) value on the second
        // call. Test that both sides are stable under re-queries.
        bool prior = PropellantTables.UseEquilibrium;
        var  priorProvider = PropellantTables.EquilibriumCorrectionProvider;
        try
        {
            PropellantTables.ClearLookupCacheForTests();
            PropellantTables.EquilibriumCorrectionProvider = EquilibriumCorrection.Parameterized;
            PropellantTables.UseEquilibrium = false;
            var a1 = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.2, 15e6);
            PropellantTables.UseEquilibrium = true;
            var b1 = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.2, 15e6);
            PropellantTables.UseEquilibrium = false;
            var a2 = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.2, 15e6);
            Assert.Equal(a1, a2);
            Assert.NotEqual(a1.CStar_ms, b1.CStar_ms);
        }
        finally
        {
            PropellantTables.UseEquilibrium = prior;
            PropellantTables.EquilibriumCorrectionProvider = priorProvider;
            PropellantTables.ClearLookupCacheForTests();
        }
    }

    [Fact]
    public void PropellantTables_ProviderIsReplaceable()
    {
        bool priorFlag = PropellantTables.UseEquilibrium;
        var  priorProvider = PropellantTables.EquilibriumCorrectionProvider;
        try
        {
            PropellantTables.ClearLookupCacheForTests();
            PropellantTables.UseEquilibrium = true;
            PropellantTables.EquilibriumCorrectionProvider = EquilibriumCorrection.None;
            var s = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.2, 20e6);

            PropellantTables.ClearLookupCacheForTests();
            PropellantTables.EquilibriumCorrectionProvider = EquilibriumCorrection.Parameterized;
            var t = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.2, 20e6);
            Assert.NotEqual(s.CStar_ms, t.CStar_ms);
        }
        finally
        {
            PropellantTables.UseEquilibrium = priorFlag;
            PropellantTables.EquilibriumCorrectionProvider = priorProvider;
            PropellantTables.ClearLookupCacheForTests();
        }
    }

    // ══════════════════════ AutoSeeder integration ══════════════════════

    [Theory]
    [InlineData(5e6,  false)]   // 5 MPa below threshold
    [InlineData(10e6, false)]   // exactly at threshold (> 10 MPa trips)
    [InlineData(12e6, true)]    // above
    [InlineData(25e6, true)]    // well above
    public void AutoSeeder_RecommendsEquilibriumAbove10MPa(double pc, bool expected)
    {
        var r = AutoSeeder.Seed(new EngineSpec(
            PropellantPair.LOX_CH4, 20_000, pc, 10.0));
        Assert.Equal(expected, r.UseEquilibriumRecommended);
    }

    [Fact]
    public void AutoSeeder_RationaleMentionsEquilibrium()
    {
        var r1 = AutoSeeder.Seed(new EngineSpec(
            PropellantPair.LOX_CH4, 20_000, 15e6, 10.0));
        Assert.Contains(r1.Rationale, line => line.Contains("Equilibrium CEA"));

        var r2 = AutoSeeder.Seed(new EngineSpec(
            PropellantPair.LOX_CH4, 20_000, 5e6, 10.0));
        Assert.Contains(r2.Rationale, line => line.Contains("Frozen CEA"));
    }

    [Fact]
    public void AutoSeeder_DoesNotMutateGlobalState()
    {
        // Forcing function: AutoSeeder must never set
        // PropellantTables.UseEquilibrium itself — that's the caller's
        // responsibility so tests + autonomous CLI retain full control.
        bool prior = PropellantTables.UseEquilibrium;
        try
        {
            PropellantTables.UseEquilibrium = false;
            _ = AutoSeeder.Seed(new EngineSpec(
                PropellantPair.LOX_CH4, 20_000, 20e6, 10.0));
            Assert.False(PropellantTables.UseEquilibrium,
                "AutoSeeder mutated PropellantTables.UseEquilibrium — it must not.");
        }
        finally
        {
            PropellantTables.UseEquilibrium = prior;
        }
    }
}
