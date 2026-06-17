// PropellantTablesTests.cs — Sprint E.1 acceptance tests for the
// per-species + mixture thermodynamic property lookup.
//
// Validates monotonic behaviour, anchor values from NIST WebBook,
// log-T interpolation, mixture-rule composition for the canonical
// hydrazine-Shell-405 catalyst products.

using System;
using Voxelforge.ElectricPropulsion.Solvers;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class PropellantTablesTests
{
    [Fact]
    public void Gamma_OfPureNH3_DecreasesWithTemperature()
    {
        // γ(NH3) drops as vibrational modes activate. RealGasGammaSolver
        // computes mixture γ via cp/cv = cp/(cp − R/MW) — Mayer's relation
        // — not a direct γ-table lookup. This makes the value depend on
        // both the cp table values and the mixture MW. Real-gas effects
        // make γ slightly different from a tabulated single-species γ.
        double gammaCold = RealGasGammaSolver.Gamma(PropellantInletComposition.PureNH3, 300.0);
        double gammaHot  = RealGasGammaSolver.Gamma(PropellantInletComposition.PureNH3, 2500.0);
        Assert.True(gammaCold > gammaHot, $"NH3 γ should decrease with T: cold {gammaCold}, hot {gammaHot}");
        Assert.InRange(gammaCold, 1.25, 1.35);  // ~1.31 at 300 K from cp/cv
        Assert.InRange(gammaHot,  1.15, 1.22);  // ~1.17 at 2500 K from cp/cv (real-gas, real-cp)
    }

    [Fact]
    public void Gamma_OfPureH2_IsHigherAtColdTemperatures()
    {
        // γ(H2) cold ≈ 1.41 (rotation dominant); hot ≈ 1.30 (vibration excited).
        // Mayer-relation γ from cp/cv may differ slightly from the
        // single-species table value due to real-gas cp curvature.
        double gammaCold = RealGasGammaSolver.Gamma(PropellantInletComposition.PureH2, 300.0);
        double gammaHot  = RealGasGammaSolver.Gamma(PropellantInletComposition.PureH2, 3000.0);
        Assert.InRange(gammaCold, 1.38, 1.42);
        Assert.InRange(gammaHot,  1.28, 1.36);
        Assert.True(gammaCold > gammaHot);
    }

    [Fact]
    public void MolarMass_OfPureH2_IsTwoGramPerMol()
    {
        double mw = RealGasGammaSolver.MolarMass(PropellantInletComposition.PureH2);
        Assert.InRange(mw, 0.00200, 0.00203);  // 2.016 g/mol
    }

    [Fact]
    public void MolarMass_OfHydrazineCatalystProducts_IsLowerThanNH3Alone()
    {
        // Hydrazine products (NH3 + N2 + H2 mix) include a lot of
        // light H2; effective MW should be lower than pure NH3.
        double mw_NH3   = RealGasGammaSolver.MolarMass(PropellantInletComposition.PureNH3);
        double mw_mixed = RealGasGammaSolver.MolarMass(PropellantInletComposition.Hydrazine_Shell405);
        Assert.True(mw_mixed < mw_NH3,
            $"Hydrazine products MW {mw_mixed} should be < NH3 MW {mw_NH3}");
        // 0.32·NH3 + 0.24·N2 + 0.44·H2 ≈ 0.32·17.03 + 0.24·28.01 + 0.44·2.02
        //                            ≈ 13.06 g/mol
        Assert.InRange(mw_mixed, 0.0125, 0.0135);
    }

    [Fact]
    public void Cp_OfH2_IsMuchHigherThanNH3()
    {
        // H2's low MW makes its mass-specific cp very high (~14300 J/(kg·K)).
        double cp_H2  = RealGasGammaSolver.Cp(PropellantInletComposition.PureH2,  500.0);
        double cp_NH3 = RealGasGammaSolver.Cp(PropellantInletComposition.PureNH3, 500.0);
        Assert.True(cp_H2 > cp_NH3 * 5);  // H2 cp is ~7× NH3 cp by mass
    }

    [Fact]
    public void Cp_GrowsMonotonicallyWithT_ForNH3()
    {
        // cp_NH3 grows from ~2080 (cold) to ~3300 (hot) as vibrational
        // modes activate.
        double cp200  = RealGasGammaSolver.Cp(PropellantInletComposition.PureNH3,  200.0);
        double cp1000 = RealGasGammaSolver.Cp(PropellantInletComposition.PureNH3, 1000.0);
        double cp3000 = RealGasGammaSolver.Cp(PropellantInletComposition.PureNH3, 3000.0);
        Assert.True(cp200 < cp1000);
        Assert.True(cp1000 < cp3000);
    }

    [Fact]
    public void Mu_GrowsMonotonicallyWithT_ForN2()
    {
        // μ(N2) follows roughly Sutherland's law — monotonic increase.
        double mu300  = RealGasGammaSolver.Mu(PropellantInletComposition.PureNH3, 300.0);
        // PureNH3 uses NH3 species; for a pure-N2-stream test we need to
        // hand-construct: 0 NH3, 1 N2, 0 H2, 0 H2O.
        var pureN2 = new PropellantInletComposition(0.0, 1.0, 0.0, 0.0);
        double mu300n2  = RealGasGammaSolver.Mu(pureN2, 300.0);
        double mu1500n2 = RealGasGammaSolver.Mu(pureN2, 1500.0);
        Assert.True(mu1500n2 > mu300n2,
            $"N2 viscosity should increase with T: 300K {mu300n2}, 1500K {mu1500n2}");
    }

    [Fact]
    public void DecompositionLimit_OfHydrazineProducts_IsBoundedByNH3()
    {
        // The mixture limit is the lowest non-trivial-fraction species
        // limit. For hydrazine Shell-405 products (32% NH3 / 24% N2 /
        // 44% H2), NH3 dominates the limit at 1100 K.
        double limit = RealGasGammaSolver.DecompositionLimit_K(
            PropellantInletComposition.Hydrazine_Shell405);
        Assert.InRange(limit, 1099, 1101);
    }

    [Fact]
    public void DecompositionLimit_OfPureH2_IsHigh()
    {
        double limit = RealGasGammaSolver.DecompositionLimit_K(PropellantInletComposition.PureH2);
        Assert.InRange(limit, 3499, 3501);
    }

    [Fact]
    public void Gamma_AtMiddleAnchor_InterpolatesCleanly()
    {
        // Pick a mid-range T (1000 K) and verify γ is between cold and hot.
        double gamma300  = RealGasGammaSolver.Gamma(PropellantInletComposition.PureNH3, 300.0);
        double gamma1000 = RealGasGammaSolver.Gamma(PropellantInletComposition.PureNH3, 1000.0);
        double gamma3000 = RealGasGammaSolver.Gamma(PropellantInletComposition.PureNH3, 3000.0);
        Assert.True(gamma300 >= gamma1000);
        Assert.True(gamma1000 >= gamma3000);
    }

    [Fact]
    public void RSpecific_OfPureH2_IsAroundFourThousand()
    {
        // R / MW = 8.314 / 0.00202 ≈ 4115 J/(kg·K).
        double R = RealGasGammaSolver.R_specific(PropellantInletComposition.PureH2);
        Assert.InRange(R, 4090, 4140);
    }
}
