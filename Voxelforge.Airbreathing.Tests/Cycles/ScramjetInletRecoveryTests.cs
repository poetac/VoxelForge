// ScramjetInletRecoveryTests.cs — coverage for the hypersonic
// multi-shock inlet stagnation-pressure recovery helper. Audit
// 05-test-gaps.md Section 2 High.
//
// ScramjetInletRecovery is the Mach ∈ [4, 15] complement to InletRecovery
// (which is hard-bounded at M ≤ 5). It is consumed by ScramjetCycleSolver
// + RbccCycleSolver on the hypersonic branch but never named directly in
// tests until this file.

using System;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class ScramjetInletRecoveryTests
{
    [Fact]
    public void MechanicalEfficiency_MatchesDocumentedConstant()
    {
        Assert.Equal(0.90, ScramjetInletRecovery.MechanicalEfficiency, precision: 6);
    }

    [Fact]
    public void DomainBounds_MatchDocumentedRange()
    {
        Assert.Equal(4.0,  ScramjetInletRecovery.MinMach, precision: 6);
        Assert.Equal(15.0, ScramjetInletRecovery.MaxMach, precision: 6);
    }

    [Fact]
    public void Pi_d_AtMach4_RecoversNearMechanicalEfficiency()
    {
        // π_d_max(4) = exp(-0.27 * 0^0.65) = 1.0 → π_d = 0.90.
        Assert.Equal(ScramjetInletRecovery.MechanicalEfficiency,
                     ScramjetInletRecovery.Pi_d(4.0),
                     precision: 6);
    }

    [Fact]
    public void Pi_d_IsMonotoneDecreasing()
    {
        // Recovery drops steeply with Mach (Mattingly §17.2 Table 17.1).
        Assert.True(ScramjetInletRecovery.Pi_d(6.0)  < ScramjetInletRecovery.Pi_d(4.0));
        Assert.True(ScramjetInletRecovery.Pi_d(8.0)  < ScramjetInletRecovery.Pi_d(6.0));
        Assert.True(ScramjetInletRecovery.Pi_d(12.0) < ScramjetInletRecovery.Pi_d(8.0));
    }

    [Fact]
    public void Pi_d_AtMach6_MatchesFormulaEvaluation()
    {
        // π_d(M=6) = 0.90 · exp(-0.27 · 2^0.65) — direct evaluation of the
        // documented closed form so the test pins behaviour to the formula
        // rather than the docstring's approximate mid-band number.
        double expected = 0.90 * Math.Exp(-0.27 * Math.Pow(2.0, 0.65));
        Assert.Equal(expected, ScramjetInletRecovery.Pi_d(6.0), precision: 6);
    }

    [Fact]
    public void Pi_d_AtMach12_MatchesFormulaEvaluation()
    {
        // π_d(M=12) = 0.90 · exp(-0.27 · 8^0.65). Same rationale as Mach 6.
        double expected = 0.90 * Math.Exp(-0.27 * Math.Pow(8.0, 0.65));
        Assert.Equal(expected, ScramjetInletRecovery.Pi_d(12.0), precision: 6);
    }

    [Fact]
    public void Pi_d_BelowMinMach_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScramjetInletRecovery.Pi_d(3.9));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScramjetInletRecovery.Pi_d(0.0));
    }

    [Fact]
    public void Pi_d_AboveMaxMach_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScramjetInletRecovery.Pi_d(15.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScramjetInletRecovery.Pi_d(20.0));
    }

    [Fact]
    public void Pi_d_NaN_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScramjetInletRecovery.Pi_d(double.NaN));
    }

    [Fact]
    public void CombustorInletMach_AtMach4_FloorsAtMinimum()
    {
        // 4.0 * 0.35 = 1.40 → floored at 1.80.
        Assert.Equal(1.80, ScramjetInletRecovery.CombustorInletMach(4.0), precision: 6);
    }

    [Fact]
    public void CombustorInletMach_AtMach10_UsesCompressionFraction()
    {
        // 10.0 * 0.35 = 3.50 (above 1.80 floor).
        Assert.Equal(3.50, ScramjetInletRecovery.CombustorInletMach(10.0), precision: 6);
    }

    [Fact]
    public void CombustorInletMach_IsMonotoneAboveFloor()
    {
        // Above the 1.80 floor, combustor Mach scales with freestream.
        Assert.True(ScramjetInletRecovery.CombustorInletMach(12.0) >
                    ScramjetInletRecovery.CombustorInletMach(8.0));
    }
}
