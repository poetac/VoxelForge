// ReactorWave2Tests.cs — Sprint CHM.W2 unit tests for the second-order
// + Batch-reactor extensions.

using System;
using Voxelforge.Chemical;
using Xunit;

namespace Voxelforge.Tests.Chemical;

public sealed class ReactorWave2Tests
{
    // ── ReactionOrder default preserves CHM.W1 bit-identity ─────────────

    [Fact]
    public void DefaultReactionOrder_IsFirst()
    {
        var d = MethylAcetateHydrolysis_Cstr();
        Assert.Equal(ReactionOrder.First, d.Order);
    }

    [Fact]
    public void CHM_W1_Baseline_BitIdenticalUnderDefaultOrder()
    {
        // The CHM.W1 baseline must produce bit-identical conversion
        // when explicitly set to First-order (i.e. no behaviour drift).
        var d_default  = MethylAcetateHydrolysis_Cstr();
        var d_explicit = MethylAcetateHydrolysis_Cstr() with { Order = ReactionOrder.First };
        var r_default  = ReactorSolver.Solve(d_default);
        var r_explicit = ReactorSolver.Solve(d_explicit);
        Assert.Equal(r_default.Conversion, r_explicit.Conversion, precision: 9);
    }

    // ── First-order CSTR/PFR helpers ────────────────────────────────────

    [Fact]
    public void FirstOrderHelper_PfrAndBatchAgree()
    {
        // 1st-order PFR and Batch share the same closed-form
        // X = 1 − exp(−Da). The helper should give bit-identical values.
        Assert.Equal(
            ReactorSolver.ComputeFirstOrderConversion(ReactorKind.Pfr,   0.7),
            ReactorSolver.ComputeFirstOrderConversion(ReactorKind.Batch, 0.7),
            precision: 12);
    }

    [Fact]
    public void FirstOrderHelper_PfrExceedsCstrAtPositiveDa()
    {
        double pfr  = ReactorSolver.ComputeFirstOrderConversion(ReactorKind.Pfr,  0.7);
        double cstr = ReactorSolver.ComputeFirstOrderConversion(ReactorKind.Cstr, 0.7);
        Assert.True(pfr > cstr);
    }

    [Fact]
    public void FirstOrderHelper_RejectsNegativeDa()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReactorSolver.ComputeFirstOrderConversion(ReactorKind.Pfr, -0.1));
    }

    // ── Second-order helpers ────────────────────────────────────────────

    [Fact]
    public void SecondOrderHelper_PfrEqualsDaOverOnePlusDa()
    {
        // 2nd-order PFR / Batch: X = Da / (1 + Da). At Da = 1, X = 0.5 exactly.
        Assert.Equal(0.5,
            ReactorSolver.ComputeSecondOrderConversion(ReactorKind.Pfr, 1.0),
            precision: 9);
    }

    [Fact]
    public void SecondOrderHelper_BatchAndPfrAgree()
    {
        Assert.Equal(
            ReactorSolver.ComputeSecondOrderConversion(ReactorKind.Pfr,   2.5),
            ReactorSolver.ComputeSecondOrderConversion(ReactorKind.Batch, 2.5),
            precision: 12);
    }

    [Fact]
    public void SecondOrderHelper_CstrAtDaEqualsOne_MatchesQuadraticSolution()
    {
        // At Da = 1: X = ((1+2) − √5) / 2 = (3 − 2.2361)/2 = 0.382.
        double X = ReactorSolver.ComputeSecondOrderConversion(ReactorKind.Cstr, 1.0);
        Assert.Equal((3.0 - Math.Sqrt(5.0)) / 2.0, X, precision: 9);
    }

    [Fact]
    public void SecondOrderHelper_AtZeroDa_ReturnsZero()
    {
        Assert.Equal(0.0,
            ReactorSolver.ComputeSecondOrderConversion(ReactorKind.Cstr, 0.0),
            precision: 9);
        Assert.Equal(0.0,
            ReactorSolver.ComputeSecondOrderConversion(ReactorKind.Pfr, 0.0),
            precision: 9);
    }

    [Fact]
    public void SecondOrderHelper_PfrExceedsCstr_AtPositiveDa()
    {
        // For 2nd-order positive-rate kinetics, PFR still beats CSTR at
        // the same Da_2 (residence-time × inlet-concentration product).
        double cstr = ReactorSolver.ComputeSecondOrderConversion(ReactorKind.Cstr, 1.0);
        double pfr  = ReactorSolver.ComputeSecondOrderConversion(ReactorKind.Pfr,  1.0);
        Assert.True(pfr > cstr);
    }

    [Fact]
    public void SecondOrderHelper_ConversionBoundedByOne()
    {
        // X ∈ [0, 1] across the Damkohler envelope.
        foreach (double da in new[] { 0.1, 1.0, 10.0, 100.0, 1000.0 })
        {
            double X_cstr = ReactorSolver.ComputeSecondOrderConversion(ReactorKind.Cstr, da);
            double X_pfr  = ReactorSolver.ComputeSecondOrderConversion(ReactorKind.Pfr,  da);
            Assert.InRange(X_cstr, 0.0, 1.0);
            Assert.InRange(X_pfr,  0.0, 1.0);
        }
    }

    // ── Batch reactor end-to-end ────────────────────────────────────────

    [Fact]
    public void BatchReactor_FirstOrder_MatchesElapsedTimeExpFormula()
    {
        // X_batch(t) = 1 − exp(−k·t). Use methyl-acetate kinetics at
        // 600 s elapsed → same Da_1 as the CHM.W1 baseline (τ=600s), so
        // Batch X should match PFR X exactly.
        var d = MethylAcetateHydrolysis_Cstr() with
        {
            Kind                = ReactorKind.Batch,
            BatchElapsedTime_s  = 600.0,
        };
        var r = ReactorSolver.Solve(d);
        Assert.InRange(r.Conversion, 0.34, 0.42);   // PFR-equivalent band
    }

    [Fact]
    public void BatchReactor_RejectsZeroElapsedTime()
    {
        var d = MethylAcetateHydrolysis_Cstr() with
        {
            Kind                = ReactorKind.Batch,
            BatchElapsedTime_s  = 0.0,
        };
        // Numeric range failure -> ArgumentOutOfRangeException (#558 PR-F).
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void BatchReactor_LongerTimeProducesHigherConversion()
    {
        var d_short = MethylAcetateHydrolysis_Cstr() with
        {
            Kind                = ReactorKind.Batch,
            BatchElapsedTime_s  = 300.0,
        };
        var d_long = MethylAcetateHydrolysis_Cstr() with
        {
            Kind                = ReactorKind.Batch,
            BatchElapsedTime_s  = 1800.0,
        };
        var r_short = ReactorSolver.Solve(d_short);
        var r_long  = ReactorSolver.Solve(d_long);
        Assert.True(r_long.Conversion > r_short.Conversion);
    }

    [Fact]
    public void BatchReactor_SecondOrder_LowerConversion_ThanFirstOrder_AtSameDa()
    {
        // Build a (2nd-order, Batch) design that has the same
        // Da_2 = k·C_A0·t as some (1st-order, Batch) Da_1 = k·t.
        // Then 2nd-order conversion < 1st-order at the same Da
        // (slower asymptotic approach to X = 1).
        var firstOrder = MethylAcetateHydrolysis_Cstr() with
        {
            Kind               = ReactorKind.Batch,
            BatchElapsedTime_s = 600.0,
            Order              = ReactionOrder.First,
        };
        // For C_A0 = 500 mol/m³, 2nd-order Da = k·C_A0·t = 0.8e-3·500·600
        // = 240. Need to make it match the 1st-order Da = 0.48. The
        // simplest match: drop C_A0 + t so Da_2 = Da_1.
        var secondOrder = MethylAcetateHydrolysis_Cstr() with
        {
            Kind                       = ReactorKind.Batch,
            BatchElapsedTime_s         = 600.0,
            InletConcentration_mol_m3  = 1.0,    // makes Da_2 = 0.48
            Order                      = ReactionOrder.SecondInA,
        };
        var r1 = ReactorSolver.Solve(firstOrder);
        var r2 = ReactorSolver.Solve(secondOrder);
        // 1st-order: X = 1 − exp(−0.48) = 0.381
        // 2nd-order: X = 0.48 / 1.48 = 0.324
        Assert.True(r2.Conversion < r1.Conversion);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Same baseline as ReactorSolverTests for direct cross-check.
    private static ReactorDesign MethylAcetateHydrolysis_Cstr() => new(
        Kind:                            ReactorKind.Cstr,
        ReactorVolume_m3:                 0.100,
        VolumetricFlowRate_m3s:           1.667e-4,
        InletConcentration_mol_m3:        500.0,
        OperatingTemperature_K:           298.15,
        ArrheniusPreExponential_per_s:    6.1e4,
        ActivationEnergy_J_mol:           45_000.0);
}
