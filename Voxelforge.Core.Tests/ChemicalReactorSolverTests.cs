// ChemicalReactorSolverTests — pins the CHM.W1/W2 ideal-reactor chain:
// Arrhenius k = A·exp(−Ea/RT), τ = V/Q (CSTR/PFR) or elapsed time
// (Batch), Damkohler Da₁ = kτ / Da₂ = k·C_A0·τ, and the Levenspiel
// closed-form conversions — CSTR X = Da/(1+Da), PFR/Batch
// X = 1−exp(−Da) (1st order); CSTR quadratic root, PFR/Batch
// X = Da/(1+Da) (2nd order) — plus the PFR-beats-CSTR ordering
// invariant. Every expected value hand-derived and independently
// recomputed (python3, mirroring the solver's operation order) before
// assertion. Backfills coverage onto the Linux 'core' CI leg
// (ROADMAP → Now §2).

using System;
using Voxelforge.Chemical;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class ChemicalReactorSolverTests
{
    // Levenspiel-scale liquid-phase reactor: V = 1 m³, Q = 0.01 m³/s
    // (τ = 100 s), C_A0 = 1000 mol/m³, T = 350 K, A = 1e10 s⁻¹,
    // Ea = 80 kJ/mol → k ≈ 0.0115 s⁻¹, Da₁ ≈ 1.15.
    private static ReactorDesign CstrBaseline()
        => new(ReactorKind.Cstr,
               ReactorVolume_m3:              1.0,
               VolumetricFlowRate_m3s:        0.01,
               InletConcentration_mol_m3:     1000.0,
               OperatingTemperature_K:        350.0,
               ArrheniusPreExponential_per_s: 1.0e10,
               ActivationEnergy_J_mol:        80000.0);

    [Fact]
    public void Solve_FirstOrderCstr_ReproducesLevenspielChain()
    {
        var r = ReactorSolver.Solve(CstrBaseline());
        // k = 1e10·exp(−80000/(8.31446·350)).
        Assert.Equal(0.011504905878451578, r.RateConstant_per_s, 12);
        Assert.Equal(100.0, r.ResidenceTime_s, 12);
        Assert.Equal(1.150490587845158, r.DamkohlerNumber, 12);
        // CSTR: X = Da/(1+Da).
        Assert.Equal(0.534989827134271, r.Conversion, 12);
        Assert.Equal(465.010172865729, r.OutletConcentration_mol_m3, 9);
        // ṅ_B = Q·C_A0·X.
        Assert.Equal(5.34989827134271, r.ProductFormationRate_mol_s, 12);
    }

    [Fact]
    public void Solve_FirstOrderPfr_BeatsCstrAtTheSameResidenceTime()
    {
        var pfr = ReactorSolver.Solve(CstrBaseline() with { Kind = ReactorKind.Pfr });
        // PFR: X = 1 − exp(−Da).
        Assert.Equal(0.6835185306740603, pfr.Conversion, 12);
        Assert.Equal(316.48146932593966, pfr.OutletConcentration_mol_m3, 9);
        Assert.Equal(6.835185306740604, pfr.ProductFormationRate_mol_s, 12);

        var cstr = ReactorSolver.Solve(CstrBaseline());
        Assert.True(pfr.Conversion > cstr.Conversion,
            "PFR must beat CSTR at equal τ for a positive-order reaction");
    }

    [Fact]
    public void Solve_BatchWithElapsedTime_MatchesPfrAtEqualTau()
    {
        // Batch integrates the same 1st-order ODE as the PFR: X(t = τ)
        // must be identical (both branches share the closed form).
        var pfr = ReactorSolver.Solve(CstrBaseline() with { Kind = ReactorKind.Pfr });
        var batch = ReactorSolver.Solve(CstrBaseline() with
        {
            Kind = ReactorKind.Batch,
            BatchElapsedTime_s = 100.0,
        });
        Assert.Equal(100.0, batch.ResidenceTime_s, 12);
        Assert.Equal(pfr.Conversion, batch.Conversion, 15);
        Assert.Equal(pfr.DamkohlerNumber, batch.DamkohlerNumber, 15);
    }

    [Fact]
    public void Solve_SecondOrder_UsesTheConcentrationScaledDamkohler()
    {
        // Da₂ = k·C_A0·τ ≈ 1150.5. CSTR root of Da·X²−(1+2Da)·X+Da = 0.
        var cstr2 = ReactorSolver.Solve(CstrBaseline() with
        { Order = ReactionOrder.SecondInA });
        Assert.Equal(1150.490587845158, cstr2.DamkohlerNumber, 9);
        Assert.Equal(0.9709492907778922, cstr2.Conversion, 12);
        // The returned X satisfies the CSTR design quadratic.
        double da = cstr2.DamkohlerNumber, x = cstr2.Conversion;
        Assert.Equal(0.0, da * x * x - (1.0 + 2.0 * da) * x + da, 9);

        // PFR 2nd order: X = Da/(1+Da).
        var pfr2 = ReactorSolver.Solve(CstrBaseline() with
        { Kind = ReactorKind.Pfr, Order = ReactionOrder.SecondInA });
        Assert.Equal(0.9991315604221557, pfr2.Conversion, 12);
        Assert.True(pfr2.Conversion > cstr2.Conversion);
    }

    [Fact]
    public void ComputeConversions_RespectPhysicalLimits()
    {
        // X ∈ [0, 1) with X(0) = 0 for every (kind, order) combination;
        // X → 1 monotonically as Da grows.
        foreach (var kind in new[] { ReactorKind.Cstr, ReactorKind.Pfr, ReactorKind.Batch })
        {
            Assert.Equal(0.0, ReactorSolver.ComputeFirstOrderConversion(kind, 0.0), 12);
            Assert.Equal(0.0, ReactorSolver.ComputeSecondOrderConversion(kind, 0.0), 12);
            // Cap at Da ≈ 31.6: beyond Da ≈ 37, 1 − exp(−Da) saturates
            // to exactly 1.0 in double precision and strict growth ends.
            double prev1 = 0.0, prev2 = 0.0;
            for (double daExp = -2.0; daExp <= 1.5; daExp += 0.5)
            {
                double da = Math.Pow(10.0, daExp);
                double x1 = ReactorSolver.ComputeFirstOrderConversion(kind, da);
                double x2 = ReactorSolver.ComputeSecondOrderConversion(kind, da);
                Assert.InRange(x1, 0.0, 1.0);
                Assert.InRange(x2, 0.0, 1.0);
                Assert.True(x1 > prev1 && x2 > prev2, "X must grow with Da");
                (prev1, prev2) = (x1, x2);
            }
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReactorSolver.ComputeFirstOrderConversion(ReactorKind.Pfr, -0.1));
    }

    [Fact]
    public void ComputeArrheniusRateConstant_PinsTheExponentialForm()
    {
        // Ea = 0 → k = A exactly, at any temperature.
        Assert.Equal(1.0e10,
            ReactorSolver.ComputeArrheniusRateConstant(1.0e10, 0.0, 350.0), 3);
        // Hotter runs faster (positive Ea).
        double k350 = ReactorSolver.ComputeArrheniusRateConstant(1.0e10, 80000.0, 350.0);
        double k400 = ReactorSolver.ComputeArrheniusRateConstant(1.0e10, 80000.0, 400.0);
        Assert.True(k400 > k350);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReactorSolver.ComputeArrheniusRateConstant(0.0, 80000.0, 350.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReactorSolver.ComputeArrheniusRateConstant(1.0e10, -1.0, 350.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReactorSolver.ComputeArrheniusRateConstant(1.0e10, 80000.0, 0.0));
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() =>
            ReactorSolver.Solve(CstrBaseline() with { Kind = ReactorKind.None }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReactorSolver.Solve(CstrBaseline() with { ReactorVolume_m3 = 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReactorSolver.Solve(CstrBaseline() with { InletConcentration_mol_m3 = double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReactorSolver.Solve(CstrBaseline() with { OperatingTemperature_K = -1.0 }));
        // Batch requires a positive elapsed time (default 0 is invalid).
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReactorSolver.Solve(CstrBaseline() with { Kind = ReactorKind.Batch }));
    }
}
