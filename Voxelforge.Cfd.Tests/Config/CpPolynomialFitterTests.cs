// CpPolynomialFitterTests.cs — Unit tests for CpPolynomialFitter.

using System;
using Voxelforge.Cfd.Config;
using Voxelforge.Combustion;
using Xunit;

namespace Voxelforge.Cfd.Tests.Config;

public sealed class CpPolynomialFitterTests
{
    // Frozen-flow LOX/CH4 test gas: GammaThroat = GammaChamber (standard CEA table output).
    private static readonly PropellantState FrozenGas = new(
        MixtureRatio:       3.5,
        ChamberPressure_Pa: 5_000_000,
        ChamberTemp_K:      3400,
        GammaChamber:       1.20,
        GammaThroat:        1.20,       // same → frozen flow
        MolecularWeight:    21.5,
        SpecificGasConst:   8314.462618 / 21.5,
        Cp_Jkg:             2500,
        Viscosity_PaS:      8.5e-5,
        Prandtl:            0.72,
        CStar_ms:           1800,
        IspVacuum_s:        360,
        PropellantName:     "LOX/CH4");

    // Equilibrium-like gas: GammaThroat < GammaChamber (Cp increases toward throat).
    private static readonly PropellantState EquilGas = FrozenGas with
    {
        GammaThroat = 1.18   // lower than chamber 1.20 → non-trivial Cp(T) curve
    };

    [Fact]
    public void Fit_FrozenGas_ReturnsFlatCpAndChamberGamma()
    {
        var result = CpPolynomialFitter.Fit(FrozenGas);

        Assert.True(result.IsFlatCp);
        Assert.Equal(FrozenGas.GammaChamber, result.GammaEffective, precision: 6);
        Assert.Equal(5, result.Coefficients.Length);
        // b0 ≈ Cp_c; higher terms zero
        Assert.InRange(result.Coefficients[0], FrozenGas.Cp_Jkg * 0.999, FrozenGas.Cp_Jkg * 1.001);
        Assert.Equal(0.0, result.Coefficients[1], precision: 10);
    }

    [Fact]
    public void Fit_EquilibriumLikeGas_GammaEffBetweenChamberAndThroat()
    {
        var result = CpPolynomialFitter.Fit(EquilGas);

        Assert.False(result.IsFlatCp);
        // γ_eff must be strictly between throat and chamber values
        Assert.InRange(result.GammaEffective, EquilGas.GammaThroat, EquilGas.GammaChamber);
    }

    [Fact]
    public void Fit_Coefficients_EvaluateToKnownCpAtEndpoints()
    {
        var result = CpPolynomialFitter.Fit(EquilGas);
        Assert.False(result.IsFlatCp);

        double rGas = 8314.462618 / EquilGas.MolecularWeight;
        double tC   = EquilGas.ChamberTemp_K;
        double tT   = tC * 2.0 / (EquilGas.GammaChamber + 1.0);
        double cpC  = EquilGas.Cp_Jkg;
        double cpT  = EquilGas.GammaThroat / (EquilGas.GammaThroat - 1.0) * rGas;

        double polyAtChamber = CpPolynomialFitter.EvalPoly(result.Coefficients, tC);
        double polyAtThroat  = CpPolynomialFitter.EvalPoly(result.Coefficients, tT);

        // Polynomial should reproduce the anchor values within 0.1%
        Assert.InRange(polyAtChamber, cpC * 0.999, cpC * 1.001);
        Assert.InRange(polyAtThroat,  cpT * 0.999, cpT * 1.001);
    }

    [Fact]
    public void Fit_DegenerateInputs_ReturnsFlatCp()
    {
        // Non-positive T_chamber
        var badTemp = FrozenGas with { ChamberTemp_K = 0.0 };
        Assert.True(CpPolynomialFitter.Fit(badTemp).IsFlatCp);

        // NaN T_chamber
        var nanTemp = FrozenGas with { ChamberTemp_K = double.NaN };
        Assert.True(CpPolynomialFitter.Fit(nanTemp).IsFlatCp);

        // Non-positive Cp
        var badCp = EquilGas with { Cp_Jkg = -1.0 };
        Assert.True(CpPolynomialFitter.Fit(badCp).IsFlatCp);

        // MolecularWeight = 0 → rGas = ∞
        var zeroMw = EquilGas with { MolecularWeight = 0.0 };
        Assert.True(CpPolynomialFitter.Fit(zeroMw).IsFlatCp);
    }

    [Fact]
    public void Fit_GammaEffClampedOutOfRange_ReturnsFlatCp()
    {
        // GammaThroat near 1 makes cpT very large → γ_eff could overshoot 2.0.
        // GammaThroat = 1.001 → Cp_t = 1.001/0.001 * R ≈ 4200 kJ/kg·K (implausibly large).
        // The fitter should detect out-of-range γ_eff and fall back to IsFlatCp.
        var extremeGas = FrozenGas with
        {
            GammaThroat = 1.001,  // contrived to push γ_eff out of [1.05, 2.0]
            GammaChamber = 1.40,
            Cp_Jkg = 1004.0
        };
        var result = CpPolynomialFitter.Fit(extremeGas);
        Assert.True(result.IsFlatCp);
    }
}
