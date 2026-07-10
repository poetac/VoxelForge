// MultiChainMigratedBreakdownTests — red-team round 3 regression
// (fail-on-old / pass-on-new). Elite migration updated a receiving chain's
// best score/params but left its `BestBreakdown` pointing at the PREVIOUS
// best design (the code comment even claimed it "stays null"). Because
// `MigrateElites` broadcasts the global best to every chain and the final
// tournament breaks ties by lowest chain index, the common winner was a
// RECEIVER — so `Result.BestBreakdown` (the full `EvaluationResult` in the
// IObjective overload: violations + physics record) described a different,
// worse design than `Result.BestParams`/`BestScore`. These tests pin the
// invariant "the reported breakdown belongs to the reported best".

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class MultiChainMigratedBreakdownTests
{
    [Fact]
    public void MigrateFrom_WhenEliteBeatsLocalBest_DoesNotKeepTheStaleBreakdown()
    {
        var sa = new SimulatedAnnealingOptimizer(
            new[] { (0.0, 1.0) }, maxIterations: 10, seed: 1);
        var staleBreakdown = new object();
        sa.ReportScore(new[] { 0.5 }, 7.0, staleBreakdown);   // iter-0 best: 7.0

        sa.MigrateFrom(new[] { 0.9 }, 5.0);                    // elite beats it

        Assert.Equal(5.0, sa.BestScore, 12);
        // The 7.0-design's breakdown must NOT be reported as the 5.0
        // design's breakdown — that pairing is a lie.
        Assert.NotSame(staleBreakdown, sa.BestBreakdown);
    }

    [Fact]
    public void MigrateFrom_ThreeArgOverload_CarriesTheDonorBreakdown()
    {
        var sa = new SimulatedAnnealingOptimizer(
            new[] { (0.0, 1.0) }, maxIterations: 10, seed: 1);
        sa.ReportScore(new[] { 0.5 }, 7.0, new object());

        var donorBreakdown = new object();
        sa.MigrateFrom(new[] { 0.9 }, 5.0, donorBreakdown);

        Assert.Equal(5.0, sa.BestScore, 12);
        Assert.Same(donorBreakdown, sa.BestBreakdown);
        // A WORSE elite must not disturb the local best or its breakdown.
        sa.MigrateFrom(new[] { 0.1 }, 6.0, new object());
        Assert.Equal(5.0, sa.BestScore, 12);
        Assert.Same(donorBreakdown, sa.BestBreakdown);
    }

    /// <summary>
    /// Quantised 1-D bowl: a narrow floor bucket at exactly 0.0 around
    /// x = 0.91, coarse 0.05-steps elsewhere. Chains stop improving once
    /// they hit their bucket, so the post-migration tie at the floor score
    /// survives to the final tournament — the historical mismatch window.
    /// Every evaluation carries the score inside its breakdown, letting the
    /// test check the breakdown↔score pairing on the result.
    /// </summary>
    private sealed class QuantisedBowlObjective : IObjective
    {
        public int DimensionCount => 1;

        public IReadOnlyList<DesignVariableInfo> Variables { get; } =
            new[] { new DesignVariableInfo("x", 0.0, 1.0) };

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            double x = vector[0];
            double d = Math.Abs(x - 0.91);
            double score = d < 0.004 ? 0.0 : Math.Ceiling(d * 20.0) / 20.0;
            return new EvaluationResult(score, Array.Empty<FeasibilityViolation>(), null);
        }
    }

    [Fact]
    public void Run_IObjective_BestBreakdownScoreMatchesBestScore()
    {
        var opt = new MultiChainOptimizer(
            new[] { (0.0, 1.0) }, maxIterations: 150, baseSeed: 11,
            chainCount: 6, migrationCadence: 25);

        var result = opt.Run(new QuantisedBowlObjective());

        var er = Assert.IsType<EvaluationResult>(result.BestBreakdown);
        Assert.Equal(result.BestScore, er.Score, 12);
    }
}
