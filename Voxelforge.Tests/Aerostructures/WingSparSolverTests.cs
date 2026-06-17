// WingSparSolverTests.cs — Sprint AS.W1 unit tests for the closed-form
// Euler-Bernoulli wing-spar performance snapshot.

using System;
using Voxelforge.Aerostructures;
using Xunit;

namespace Voxelforge.Tests.Aerostructures;

public sealed class WingSparSolverTests
{
    // ── Registry ─────────────────────────────────────────────────────────

    [Fact]
    public void Registry_Steel4340_HasHigherYieldThanAl7075()
    {
        Assert.True(SparMaterialRegistry.Steel4340.YieldStrength_Pa
                  > SparMaterialRegistry.Aluminum7075.YieldStrength_Pa);
    }

    [Fact]
    public void Registry_CarbonFibre_LowestDensityAmongMaterials()
    {
        Assert.True(SparMaterialRegistry.CarbonFibreComposite.Density_kgm3
                  < SparMaterialRegistry.Aluminum7075.Density_kgm3);
        Assert.True(SparMaterialRegistry.Aluminum7075.Density_kgm3
                  < SparMaterialRegistry.Steel4340.Density_kgm3);
    }

    [Fact]
    public void Registry_For_ThrowsOnNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SparMaterialRegistry.For(SparMaterial.None));
    }

    // ── Section properties ──────────────────────────────────────────────

    [Fact]
    public void SectionProperties_SolidRectangular_MatchClosedForm()
    {
        var d = Cessna172Spar() with
        {
            SectionType = SparSectionType.SolidRectangular,
            OuterWidth_m  = 0.05,
            OuterHeight_m = 0.20,
        };
        var (A, I, c) = WingSparSolver.ComputeSectionProperties(d);
        Assert.Equal(0.05 * 0.20,                           A, precision: 9);
        Assert.Equal(0.05 * Math.Pow(0.20, 3) / 12.0,       I, precision: 12);
        Assert.Equal(0.10,                                   c, precision: 9);
    }

    [Fact]
    public void SectionProperties_SolidCircular_MatchClosedForm()
    {
        var d = Cessna172Spar() with
        {
            SectionType = SparSectionType.SolidCircular,
            OuterHeight_m = 0.10,    // 2R = 100 mm → R = 50 mm
        };
        var (A, I, c) = WingSparSolver.ComputeSectionProperties(d);
        double R = 0.05;
        Assert.Equal(Math.PI * R * R,                A, precision: 9);
        Assert.Equal(Math.PI * Math.Pow(R, 4) / 4.0, I, precision: 14);
        Assert.Equal(R,                              c, precision: 9);
    }

    [Fact]
    public void SectionProperties_HollowBox_LowerThanSolidEquivalent()
    {
        var solid = Cessna172Spar() with
        {
            SectionType = SparSectionType.SolidRectangular,
            OuterWidth_m  = 0.080,
            OuterHeight_m = 0.20,
        };
        var hollow = Cessna172Spar() with
        {
            SectionType = SparSectionType.HollowRectangularBox,
            OuterWidth_m  = 0.080,
            OuterHeight_m = 0.20,
            WallThickness_m = 0.008,
        };
        var (A_s, I_s, _) = WingSparSolver.ComputeSectionProperties(solid);
        var (A_h, I_h, _) = WingSparSolver.ComputeSectionProperties(hollow);
        // Hollow has less area + less I_xx (some material is removed).
        Assert.True(A_h < A_s);
        Assert.True(I_h < I_s);
    }

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneSection()
    {
        var d = Cessna172Spar() with { SectionType = SparSectionType.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsHollowWithThicknessGreaterThanHalfDimension()
    {
        var d = Cessna172Spar() with { WallThickness_m = 0.060 };  // half of 0.080 width
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroLoadFactor()
    {
        var d = Cessna172Spar() with { LoadFactor = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── Cessna 172-class baseline ───────────────────────────────────────

    [Fact]
    public void Cessna172Spar_BendingStressInClusterBand()
    {
        // 200 mm × 80 mm × 8 mm hollow Al-7075 at 5.5 m span, 981 N/m
        // distributed lift, 3.8 g maneuver → σ_max ≈ 280 MPa.
        var r = WingSparSolver.Solve(Cessna172Spar());
        Assert.InRange(r.MaximumBendingStress_Pa, 200e6, 350e6);
    }

    [Fact]
    public void Cessna172Spar_SafetyFactorInClusterBand()
    {
        // SF = 503 MPa / 280 MPa ≈ 1.79. FAR Part 23 limit-load SF
        // requirement is 1.5 (so ultimate SF = 1.0 at 5.7 g).
        var r = WingSparSolver.Solve(Cessna172Spar());
        Assert.InRange(r.SafetyFactor, 1.4, 2.5);
    }

    [Fact]
    public void Cessna172Spar_TipDeflectionInClusterBand()
    {
        // δ_tip = w·L⁴/(8·E·I) ≈ 0.30 m at 3.8 g. Real Cessna tip
        // deflection at limit maneuver: cluster band [0.20, 0.40] m.
        var r = WingSparSolver.Solve(Cessna172Spar());
        Assert.InRange(r.TipDeflection_m, 0.20, 0.40);
    }

    [Fact]
    public void Cessna172Spar_MassInClusterBand()
    {
        // m = ρ·A·L = 2810·0.00422·5.5 ≈ 65 kg per half-spar. Cluster
        // band [40, 80] kg.
        var r = WingSparSolver.Solve(Cessna172Spar());
        Assert.InRange(r.SparMass_kg, 40.0, 80.0);
    }

    [Fact]
    public void Cessna172Spar_BendingMomentMatchesUDLClosedForm()
    {
        // M_max = n·w·L²/2 at the root.
        var d = Cessna172Spar();
        var r = WingSparSolver.Solve(d);
        double expected = d.LoadFactor * d.DistributedLift_Nm
                        * d.HalfSpan_m * d.HalfSpan_m / 2.0;
        Assert.Equal(expected, r.MaximumBendingMoment_Nm, precision: 4);
    }

    [Fact]
    public void Cessna172Spar_SectionModulusEqualsIxxOverHalfHeight()
    {
        var r = WingSparSolver.Solve(Cessna172Spar());
        // S = I / c where c = h/2 = 0.10.
        Assert.Equal(r.SecondMomentOfArea_m4 / 0.10, r.SectionModulus_m3, precision: 12);
    }

    // ── Scaling sanity ──────────────────────────────────────────────────

    [Fact]
    public void BendingStress_LinearInLoadFactor()
    {
        var lo = WingSparSolver.Solve(Cessna172Spar() with { LoadFactor = 1.0 });
        var hi = WingSparSolver.Solve(Cessna172Spar() with { LoadFactor = 3.8 });
        Assert.Equal(3.8, hi.MaximumBendingStress_Pa / lo.MaximumBendingStress_Pa, precision: 6);
    }

    [Fact]
    public void TipDeflection_QuarticInSpan()
    {
        // δ ∝ L⁴.
        var shorter = WingSparSolver.Solve(Cessna172Spar() with { HalfSpan_m = 4.0 });
        var longer  = WingSparSolver.Solve(Cessna172Spar() with { HalfSpan_m = 5.5 });
        double expectedRatio = Math.Pow(5.5 / 4.0, 4);
        Assert.Equal(expectedRatio,
            longer.TipDeflection_m / shorter.TipDeflection_m, precision: 4);
    }

    [Fact]
    public void TipDeflection_InverseInYoungsModulus()
    {
        // Same geometry, swap to steel-4340 (E ≈ 2.79× Al-7075). Deflection
        // should drop by the inverse ratio.
        var alSpar    = WingSparSolver.Solve(Cessna172Spar() with { Material = SparMaterial.Aluminum7075 });
        var steelSpar = WingSparSolver.Solve(Cessna172Spar() with { Material = SparMaterial.Steel4340 });
        double E_ratio = SparMaterialRegistry.Steel4340.YoungsModulus_Pa
                       / SparMaterialRegistry.Aluminum7075.YoungsModulus_Pa;
        Assert.Equal(1.0 / E_ratio,
            steelSpar.TipDeflection_m / alSpar.TipDeflection_m, precision: 4);
    }

    [Fact]
    public void Composite_LowerMass_ThanSteel_AtSameGeometry()
    {
        var steel    = WingSparSolver.Solve(Cessna172Spar() with { Material = SparMaterial.Steel4340 });
        var compSpar = WingSparSolver.Solve(Cessna172Spar() with { Material = SparMaterial.CarbonFibreComposite });
        Assert.True(compSpar.SparMass_kg < steel.SparMass_kg);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Cessna 172-class single-half-spar baseline. Hollow rectangular box,
    // Al-7075-T6, 5.5 m half-span, 981 N/m distributed lift at 1 g, 3.8 g
    // maneuver-load envelope (FAR Part 23 normal-category limit).
    private static WingSparDesign Cessna172Spar() => new(
        SectionType:          SparSectionType.HollowRectangularBox,
        Material:             SparMaterial.Aluminum7075,
        HalfSpan_m:            5.5,
        OuterHeight_m:         0.20,
        OuterWidth_m:          0.080,
        WallThickness_m:       0.008,
        DistributedLift_Nm:    981.0,
        LoadFactor:            3.8);
}
