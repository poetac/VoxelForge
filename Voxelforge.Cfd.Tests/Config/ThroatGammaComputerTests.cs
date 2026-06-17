// ThroatGammaComputerTests.cs — Unit tests for ThroatGammaComputer (issue #454).

using System;
using Voxelforge.Cfd.Config;
using Voxelforge.Combustion;
using Xunit;

namespace Voxelforge.Cfd.Tests.Config;

public sealed class ThroatGammaComputerTests
{
    // ── IsentropicThroatPressure formula tests ────────────────────────────────

    [Theory]
    [InlineData(1.4, 0.5283)]   // air: P*/P_c = (2/2.4)^3.5 = 0.52828
    [InlineData(1.2, 0.5645)]   // LOX/CH4: (2/2.2)^6 = 0.56447
    [InlineData(1.3, 0.5457)]   // intermediate: (2/2.3)^(1.3/0.3)
    public void IsentropicThroatPressure_KnownGamma_MatchesAnalytic(double gamma, double expectedRatio)
    {
        double pC = 5_000_000; // 5 MPa — arbitrary
        double pT = ThroatGammaComputer.IsentropicThroatPressure(pC, gamma);
        double ratio = pT / pC;
        Assert.InRange(ratio, expectedRatio - 0.0003, expectedRatio + 0.0003);
    }

    [Fact]
    public void IsentropicThroatPressure_GammaOne_DoesNotThrow()
    {
        // γ = 1 makes denominator (γ-1) = 0 → Math.Pow returns NaN/Inf — handled gracefully
        double result = ThroatGammaComputer.IsentropicThroatPressure(1_000_000, 1.0);
        // We only care it doesn't throw; NaN/Inf is acceptable here (caller guards on that)
        _ = result;
    }

    // ── WithThroatGamma table-lookup tests ───────────────────────────────────

    [Fact]
    public void WithThroatGamma_LoxCh4At7MPa_ProducesDistinctGammaThroat()
    {
        // At Pc=7 MPa, isentropic P* ≈ 3.97 MPa — within the [3, 25] MPa LOX/CH4 table.
        // The table-interpolated γ at P* should differ from the chamber γ at 7 MPa.
        var chamber = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.5, 7_000_000);
        var result  = ThroatGammaComputer.WithThroatGamma(chamber, PropellantPair.LOX_CH4);

        // Only GammaThroat should change; GammaChamber is preserved
        Assert.Equal(chamber.GammaChamber, result.GammaChamber);
        Assert.NotEqual(chamber.GammaChamber, result.GammaThroat);
        Assert.InRange(result.GammaThroat, 1.05, 2.0);
    }

    [Fact]
    public void WithThroatGamma_ActivatesCpPolynomialFitter()
    {
        // End-to-end: WithThroatGamma → CpPolynomialFitter.Fit should give IsFlatCp=false
        // when the propellant table returns distinct γ values at P* vs Pc.
        var chamber = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.5, 7_000_000);
        var gasWithThroat = ThroatGammaComputer.WithThroatGamma(chamber, PropellantPair.LOX_CH4);

        var poly = CpPolynomialFitter.Fit(gasWithThroat);

        if (Math.Abs(gasWithThroat.GammaThroat - gasWithThroat.GammaChamber) > 1e-9)
        {
            // Polynomial path should activate when GammaThroat ≠ GammaChamber
            Assert.False(poly.IsFlatCp);
            Assert.InRange(poly.GammaEffective, gasWithThroat.GammaThroat, gasWithThroat.GammaChamber);
        }
        else
        {
            // If table clamping returned equal values, graceful fallback is correct
            Assert.True(poly.IsFlatCp);
        }
    }

    [Fact]
    public void WithThroatGamma_DegenerateGamma_ReturnsChamberUnchanged()
    {
        // GammaChamber ≤ 1 → P* formula is degenerate → returns original state
        var state = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.5, 5_000_000);
        var bad   = state with { GammaChamber = 0.5 };
        var result = ThroatGammaComputer.WithThroatGamma(bad, PropellantPair.LOX_CH4);
        Assert.Equal(bad, result);
    }
}
