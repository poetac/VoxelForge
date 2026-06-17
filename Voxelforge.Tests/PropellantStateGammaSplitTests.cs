// PH-24 + PH-30 (2026-04-25): cover the GammaChamber/GammaThroat split
// and the IsFrozen idempotency guard on EquilibriumCorrection.Correct.

using Voxelforge.Combustion;

namespace Voxelforge.Tests;

public class PropellantStateGammaSplitTests
{
    private static PropellantState Sample(bool isFrozen = true) => new(
        MixtureRatio: 3.5,
        ChamberPressure_Pa: 7.0e6,
        ChamberTemp_K: 3500.0,
        GammaChamber: 1.18,
        GammaThroat:  1.18,
        MolecularWeight: 22.0,
        SpecificGasConst: 8314.462618 / 22.0,
        Cp_Jkg: 3000.0,
        Viscosity_PaS: 1e-4,
        Prandtl: 0.6,
        CStar_ms: 1800.0,
        IspVacuum_s: 360.0,
        PropellantName: "synthetic",
        IsFrozen: isFrozen);

    [Fact]
    public void FrozenTable_HasEqualGammaChamberAndThroat()
    {
        // Frozen-flow tables (current state of all 3 implemented pairs)
        // produce GammaThroat == GammaChamber by definition — no
        // composition change between chamber and throat.
        var s = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.5, 7.0e6);
        Assert.Equal(s.GammaChamber, s.GammaThroat);
        Assert.True(s.IsFrozen, "PropellantTables.Lookup should produce frozen states by default.");
    }

    [Fact]
    public void GammaProperty_IsBackCompatAliasForGammaChamber()
    {
        var s = Sample() with { GammaChamber = 1.22, GammaThroat = 1.18 };
        // Back-compat property must return chamber-side value, not throat.
        Assert.Equal(1.22, s.Gamma);
        Assert.Equal(1.22, s.GammaChamber);
        Assert.Equal(1.18, s.GammaThroat);
    }

    [Fact]
    public void EquilibriumCorrection_OnFrozenState_FlipsIsFrozenToFalse()
    {
        var frozen = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.5, 20.0e6);
        Assert.True(frozen.IsFrozen);

        var corrected = EquilibriumCorrection.Parameterized.Correct(
            frozen, PropellantPair.LOX_CH4);
        Assert.False(corrected.IsFrozen,
            "Correct() must mark its output as no-longer-frozen.");
    }

    [Fact]
    public void EquilibriumCorrection_OnAlreadyCorrectedState_IsIdempotent()
    {
        // PH-30 contract: Correct() noops when !IsFrozen. Re-applying the
        // correction must not double-apply the dissociation factor.
        var frozen = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.5, 20.0e6);
        var once   = EquilibriumCorrection.Parameterized.Correct(frozen, PropellantPair.LOX_CH4);
        var twice  = EquilibriumCorrection.Parameterized.Correct(once,   PropellantPair.LOX_CH4);

        // Strict bit-for-bit equality after the noop.
        Assert.Equal(once, twice);
    }

    [Fact]
    public void EquilibriumCorrection_AppliesGammaShiftToBothFields()
    {
        // PH-24 + parameterized correction: both GammaChamber and
        // GammaThroat get the same dissociation scaling (until a future
        // 2-D table or Gordon-McBride solver gives them distinct shifts).
        var frozen = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.5, 20.0e6);
        var corrected = EquilibriumCorrection.Parameterized.Correct(
            frozen, PropellantPair.LOX_CH4);

        // Both should have moved off the frozen value and equal each other.
        Assert.NotEqual(frozen.GammaChamber, corrected.GammaChamber);
        Assert.Equal(corrected.GammaChamber, corrected.GammaThroat);
    }

    [Fact]
    public void EquilibriumCorrection_OnUnsupportedPair_StaysIdentity_AndStaysFrozen()
    {
        // Unsupported pair returns identity + leaves IsFrozen unchanged
        // (the all-zero-coefficient path returns `s` directly without
        // resetting IsFrozen). Defensible: if no correction was actually
        // applied, the state is still in its original frozen-table form.
        var frozen = Sample(isFrozen: true);
        var corrected = EquilibriumCorrection.Parameterized.Correct(
            frozen, PropellantPair.N2O4_MMH);
        Assert.True(corrected.IsFrozen);
        Assert.Equal(frozen, corrected);
    }
}
