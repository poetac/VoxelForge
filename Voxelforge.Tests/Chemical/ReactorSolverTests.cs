// ReactorSolverTests.cs — Sprint CHM.W1 unit tests for the closed-form
// ideal first-order reactor solver.

using System;
using Voxelforge.Chemical;
using Xunit;

namespace Voxelforge.Tests.Chemical;

public sealed class ReactorSolverTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneKind()
    {
        var d = MethylAcetateHydrolysis_Cstr() with { Kind = ReactorKind.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroVolume()
    {
        var d = MethylAcetateHydrolysis_Cstr() with { ReactorVolume_m3 = 0.0 };
        // Numeric range failure -> ArgumentOutOfRangeException (#558 PR-F).
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroPreExponential()
    {
        var d = MethylAcetateHydrolysis_Cstr() with { ArrheniusPreExponential_per_s = 0.0 };
        // Numeric range failure -> ArgumentOutOfRangeException (#558 PR-F).
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    // ── Methyl-acetate hydrolysis baseline (Levenspiel textbook) ────────

    [Fact]
    public void MethylAcetateCstr_ConversionInClusterBand()
    {
        // k ≈ 8e-4 /s at 298 K; τ = 600 s; Da = 0.48 → X_CSTR = 0.48/1.48
        // = 0.324. Cluster band [0.28, 0.38].
        var r = ReactorSolver.Solve(MethylAcetateHydrolysis_Cstr());
        Assert.InRange(r.Conversion, 0.28, 0.38);
    }

    [Fact]
    public void MethylAcetatePfr_ConversionInClusterBand()
    {
        // X_PFR = 1 − exp(−0.48) = 0.381. Cluster band [0.34, 0.42].
        var r = ReactorSolver.Solve(MethylAcetateHydrolysis_Cstr()
            with { Kind = ReactorKind.Pfr });
        Assert.InRange(r.Conversion, 0.34, 0.42);
    }

    [Fact]
    public void PfrConversion_ExceedsCstrAtSameDamkohler()
    {
        // For positive-order reactions, PFR always beats CSTR at fixed τ.
        var cstr = ReactorSolver.Solve(MethylAcetateHydrolysis_Cstr()
            with { Kind = ReactorKind.Cstr });
        var pfr  = ReactorSolver.Solve(MethylAcetateHydrolysis_Cstr()
            with { Kind = ReactorKind.Pfr });
        Assert.Equal(cstr.DamkohlerNumber, pfr.DamkohlerNumber, precision: 9);
        Assert.True(pfr.Conversion > cstr.Conversion);
    }

    [Fact]
    public void OutletConcentration_EqualsInletTimesOneMinusX()
    {
        var d = MethylAcetateHydrolysis_Cstr();
        var r = ReactorSolver.Solve(d);
        Assert.Equal(d.InletConcentration_mol_m3 * (1.0 - r.Conversion),
                     r.OutletConcentration_mol_m3, precision: 4);
    }

    [Fact]
    public void ProductFormationRate_EqualsFlowTimesC0X()
    {
        var d = MethylAcetateHydrolysis_Cstr();
        var r = ReactorSolver.Solve(d);
        Assert.Equal(d.VolumetricFlowRate_m3s
                   * d.InletConcentration_mol_m3
                   * r.Conversion,
                     r.ProductFormationRate_mol_s, precision: 6);
    }

    [Fact]
    public void ResidenceTimeEqualsVolumeOverFlow()
    {
        var d = MethylAcetateHydrolysis_Cstr();
        var r = ReactorSolver.Solve(d);
        Assert.Equal(d.ReactorVolume_m3 / d.VolumetricFlowRate_m3s,
                     r.ResidenceTime_s, precision: 6);
    }

    // ── Scaling / limit behaviour ───────────────────────────────────────

    [Fact]
    public void Conversion_ZeroAtDamkohlerZero()
    {
        // Da → 0 (instant flow / zero rate) → X → 0 for both reactor kinds.
        var d = MethylAcetateHydrolysis_Cstr() with
        {
            VolumetricFlowRate_m3s = 1e6,    // huge flow → vanishingly small τ
        };
        var rCstr = ReactorSolver.Solve(d with { Kind = ReactorKind.Cstr });
        var rPfr  = ReactorSolver.Solve(d with { Kind = ReactorKind.Pfr });
        Assert.True(rCstr.Conversion < 0.001);
        Assert.True(rPfr.Conversion  < 0.001);
    }

    [Fact]
    public void Conversion_ApproachesOneAtHighDamkohler()
    {
        // Da → ∞ → X → 1 for both reactor kinds.
        var d = MethylAcetateHydrolysis_Cstr() with
        {
            VolumetricFlowRate_m3s = 1e-9,    // ~ steady-state batch
        };
        var rCstr = ReactorSolver.Solve(d with { Kind = ReactorKind.Cstr });
        var rPfr  = ReactorSolver.Solve(d with { Kind = ReactorKind.Pfr });
        Assert.InRange(rCstr.Conversion, 0.999, 1.0);
        Assert.InRange(rPfr.Conversion,  0.999, 1.0);
    }

    [Fact]
    public void RateConstant_DoublesWhenTRoughlyInvokesArrheniusRise()
    {
        // Methyl acetate E_a = 45 kJ/mol. Raise T from 298 → 308 K:
        // k(308)/k(298) = exp(-Ea/R · (1/308 − 1/298))
        //               = exp(45000/8.314 · (1/298 − 1/308))
        //               = exp(45000/8.314 · 1.09e-4)
        //               = exp(0.589) ≈ 1.80
        var cool = ReactorSolver.Solve(MethylAcetateHydrolysis_Cstr()
            with { OperatingTemperature_K = 298.15 });
        var warm = ReactorSolver.Solve(MethylAcetateHydrolysis_Cstr()
            with { OperatingTemperature_K = 308.15 });
        double ratio = warm.RateConstant_per_s / cool.RateConstant_per_s;
        Assert.InRange(ratio, 1.5, 2.1);
    }

    // ── ComputeArrheniusRateConstant helper ─────────────────────────────

    [Fact]
    public void Arrhenius_AtZeroEa_EqualsPreExponential()
    {
        // E_a = 0 → exp(0) = 1 → k = A regardless of T.
        double k = ReactorSolver.ComputeArrheniusRateConstant(
            preExponential_per_s: 1.0,
            activationEnergy_J_mol: 0.0,
            temperature_K: 500.0);
        Assert.Equal(1.0, k, precision: 9);
    }

    [Fact]
    public void Arrhenius_RejectsNonPositiveInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReactorSolver.ComputeArrheniusRateConstant(0.0, 10000, 298));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReactorSolver.ComputeArrheniusRateConstant(1.0, -1.0, 298));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReactorSolver.ComputeArrheniusRateConstant(1.0, 10000, 0));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Methyl-acetate hydrolysis (Levenspiel textbook example):
    //   T = 25 °C, k ≈ 8e-4 /s, E_a ≈ 45 kJ/mol → A ≈ 6.1e4 /s.
    //   100 L reactor with 10 L/min feed → τ = 10 min.
    private static ReactorDesign MethylAcetateHydrolysis_Cstr() => new(
        Kind:                            ReactorKind.Cstr,
        ReactorVolume_m3:                 0.100,
        VolumetricFlowRate_m3s:           1.667e-4,    // 10 L/min
        InletConcentration_mol_m3:        500.0,       // 0.5 mol/L
        OperatingTemperature_K:           298.15,
        ArrheniusPreExponential_per_s:    6.1e4,
        ActivationEnergy_J_mol:           45_000.0);
}
