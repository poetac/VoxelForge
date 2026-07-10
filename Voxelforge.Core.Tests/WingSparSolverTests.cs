// WingSparSolverTests — pins the closed-form Euler-Bernoulli cantilever
// wing-spar math: section properties (rect / hollow box / circular),
// root moment M = n·w·L²/2, UDL tip deflection w·L⁴/(8EI), σ = M/S,
// SF = σ_y/σ, mass = ρ·A·L, and the AS.W2 elliptical-lift 0.75/0.65
// closed-form factors. Pure closed form → exact assertions. Backfills
// coverage onto the Linux 'core' CI leg.

using System;
using Voxelforge.Aerostructures;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class WingSparSolverTests
{
    // Solid rectangular Al-7075 spar: L = 5 m, h = 0.1 m, b = 0.05 m,
    // w = 1000 N/m at n = 1. Al7075: σ_y = 503 MPa, E = 71.7 GPa, ρ = 2810.
    private static WingSparDesign RectSpar()
        => new(SparSectionType.SolidRectangular, SparMaterial.Aluminum7075,
               HalfSpan_m: 5.0, OuterHeight_m: 0.1, OuterWidth_m: 0.05,
               WallThickness_m: 0.0, DistributedLift_Nm: 1000.0, LoadFactor: 1.0);

    [Fact]
    public void Solve_SolidRectangular_MatchesClosedForm()
    {
        var r = WingSparSolver.Solve(RectSpar());
        Assert.Equal(0.05 * 0.1, r.SectionArea_m2, 12);                       // A = b·h
        Assert.Equal(0.05 * 0.1 * 0.1 * 0.1 / 12.0, r.SecondMomentOfArea_m4, 15); // b·h³/12
        Assert.Equal(r.SecondMomentOfArea_m4 / 0.05, r.SectionModulus_m3, 15);    // S = I/c
        Assert.Equal(12_500.0, r.MaximumBendingMoment_Nm, 9);                 // w·L²/2
        Assert.Equal(1.5e8, r.MaximumBendingStress_Pa, 5);                    // M·6/(b·h²)
        Assert.Equal(0.26150627615062755, r.TipDeflection_m, 12);             // wL⁴/(8EI)
        Assert.Equal(503e6 / 1.5e8, r.SafetyFactor, 9);                       // ≈ 3.353
        Assert.Equal(70.25, r.SparMass_kg, 9);                                // ρ·A·L
    }

    [Fact]
    public void Solve_LoadFactor_ScalesMomentStressDeflectionLinearly()
    {
        var oneG = WingSparSolver.Solve(RectSpar());
        var pullUp = WingSparSolver.Solve(RectSpar() with { LoadFactor = 3.8 });
        Assert.Equal(3.8 * 12_500.0, pullUp.MaximumBendingMoment_Nm, 9);
        Assert.Equal(3.8, pullUp.MaximumBendingStress_Pa / oneG.MaximumBendingStress_Pa, 9);
        Assert.Equal(3.8, pullUp.TipDeflection_m / oneG.TipDeflection_m, 9);
        Assert.Equal(3.8, oneG.SafetyFactor / pullUp.SafetyFactor, 9);        // SF ∝ 1/n
    }

    [Fact]
    public void Solve_EllipticalLift_AppliesRootMomentAndDeflectionFactors()
    {
        // Same total lift redistributed inboard: M ×0.75, δ_tip ×0.65 (AS.W2).
        var udl = WingSparSolver.Solve(RectSpar());
        var ell = WingSparSolver.Solve(RectSpar() with { UseEllipticalLift = true });
        Assert.Equal(0.75 * 12_500.0, ell.MaximumBendingMoment_Nm, 9);
        Assert.Equal(0.65 * udl.TipDeflection_m, ell.TipDeflection_m, 12);
    }

    [Fact]
    public void ComputeSectionProperties_SolidCircular_UsesHeightAsDiameter()
    {
        // h reinterpreted as 2R → R = 0.05; b is ignored (0 is accepted).
        var circ = RectSpar() with { SectionType = SparSectionType.SolidCircular, OuterWidth_m = 0.0 };
        var (a, i, c) = WingSparSolver.ComputeSectionProperties(circ);
        Assert.Equal(Math.PI * 0.05 * 0.05, a, 15);                 // π·R²
        Assert.Equal(Math.PI * 0.05 * 0.05 * 0.05 * 0.05 / 4.0, i, 15); // π·R⁴/4
        Assert.Equal(0.05, c, 15);
    }

    [Fact]
    public void ComputeSectionProperties_HollowBox_SubtractsInnerRectangle()
    {
        var box = RectSpar() with
        {
            SectionType = SparSectionType.HollowRectangularBox,
            WallThickness_m = 0.01,
        };
        var (a, i, _) = WingSparSolver.ComputeSectionProperties(box);
        // Inner: (b−2t)·(h−2t) = 0.03 × 0.08.
        Assert.Equal(0.0026, a, 12);
        Assert.Equal(2.8866666666666677e-06, i, 12);
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        // Hollow wall ≥ half the smaller outer dimension is degenerate.
        Assert.Throws<ArgumentException>(() => WingSparSolver.Solve(RectSpar() with
        {
            SectionType = SparSectionType.HollowRectangularBox,
            WallThickness_m = 0.025,
        }));
        Assert.Throws<ArgumentException>(() =>
            WingSparSolver.Solve(RectSpar() with { Material = SparMaterial.None }));
        Assert.Throws<ArgumentException>(() =>
            WingSparSolver.Solve(RectSpar() with { DistributedLift_Nm = 0.0 }));
    }
}
