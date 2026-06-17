// CmaEsOptimizerDeterminismTests.cs — Issue #552 / audit C4 regression.
//
// Pins the strict-determinism contract for CmaEsOptimizer when the
// objective produces tied fitness values. Before the fix at
// CmaEsOptimizer.cs:267, Array.Sort on the fitness-comparing comparer
// was unstable: ties (common with +Inf-clamped infeasible candidates)
// left idx ordering undefined. The top-mu recombination weights at
// line 274 then assigned different weights to the same fitness,
// producing different new means -> different sigma updates -> different
// subsequent generations. A tie-break by original index makes the sort
// order a deterministic function of (seed, objective), restoring
// bit-identical (BestParams, BestScore) across fresh instances.
//
// Pattern adapted from MultiChainOptimizerTests.StrictDeterminism_
// HoldsAcross10Runs (audit H5 — the canonical N-fresh-instances shape).

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Optimization;

public sealed class CmaEsOptimizerDeterminismTests
{
    /// <summary>
    /// Returns <c>+Infinity</c> for every input. Exercises the worst-case
    /// tie scenario: every fitness in every generation compares equal,
    /// so every <c>Array.Sort</c> comparer call returns 0 on the fitness
    /// term. Without the tie-break by original index, the sort order is
    /// undefined and the recombination-weight assignment drifts across
    /// runs, breaking strict determinism.
    /// </summary>
    private sealed class InfeasibleEverywhereObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;

        public InfeasibleEverywhereObjective(int dim)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++)
                _vars[i] = new DesignVariableInfo($"x{i}", -1.0, 1.0);
        }

        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
            => new(double.PositiveInfinity, Array.Empty<FeasibilityViolation>(), null);
    }

    /// <summary>
    /// Returns a fixed finite score for every input. Same tie regime as
    /// the +Inf variant but exercises the finite-fitness branch in
    /// <see cref="CmaEsOptimizer.Run"/> (the best-tracking path filters
    /// out NaN but accepts finite scores, while +Inf is silently rejected
    /// because <c>+Inf &lt; +Inf</c> is false).
    /// </summary>
    private sealed class FixedFiniteScoreObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;
        private readonly double _score;

        public FixedFiniteScoreObjective(int dim, double score)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++)
                _vars[i] = new DesignVariableInfo($"x{i}", -1.0, 1.0);
            _score = score;
        }

        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
            => new(_score, Array.Empty<FeasibilityViolation>(), null);
    }

    [Fact]
    public void TiedFitness_Infeasible_StrictDeterminismAcross10Runs()
    {
        // Every Evaluate call returns +Inf, so every per-generation
        // Array.Sort comparer call returns 0 on the fitness term. Without
        // the tie-break by original index, idx ordering is undefined and
        // the recombination-weight assignment drifts run-to-run. With the
        // fix, ten fresh CmaEsOptimizer instances on identical inputs
        // must produce bit-identical (BestParams, BestScore).
        const int dim = 4;
        var initialMean = new double[] { 0.1, -0.2, 0.3, -0.4 };

        double[]? firstBestParams = null;
        double firstBestScore = 0.0;
        for (int run = 0; run < 10; run++)
        {
            var obj = new InfeasibleEverywhereObjective(dim);
            var result = new CmaEsOptimizer(
                objective:      obj,
                initialMean:    initialMean,
                initialSigma:   0.3,
                maxGenerations: 15,
                seed:           42).Run();

            if (run == 0)
            {
                firstBestParams = result.BestParams;
                firstBestScore = result.BestScore;
            }
            else
            {
                Assert.Equal(firstBestScore, result.BestScore);
                Assert.Equal(firstBestParams!, result.BestParams);
            }
        }
    }

    [Fact]
    public void TiedFitness_FixedFiniteScore_StrictDeterminismAcross10Runs()
    {
        // Same shape as the +Inf test but with a finite tied score. Every
        // generation's full population produces the same fitness; the
        // top-mu selection has no signal beyond sort order. The tie-break
        // by original index keeps the recombination mean — and therefore
        // the C / sigma / p_sigma / p_c trajectory — bit-identical across
        // fresh instances.
        const int dim = 4;
        var initialMean = new double[] { 0.0, 0.0, 0.0, 0.0 };

        double[]? firstBestParams = null;
        double firstBestScore = 0.0;
        for (int run = 0; run < 10; run++)
        {
            var obj = new FixedFiniteScoreObjective(dim, score: 1.0);
            var result = new CmaEsOptimizer(
                objective:      obj,
                initialMean:    initialMean,
                initialSigma:   0.4,
                maxGenerations: 20,
                seed:           123).Run();

            if (run == 0)
            {
                firstBestParams = result.BestParams;
                firstBestScore = result.BestScore;
            }
            else
            {
                Assert.Equal(firstBestScore, result.BestScore);
                Assert.Equal(firstBestParams!, result.BestParams);
            }
        }
    }

    [Fact]
    public void TiedFitness_HistoryIsBitIdenticalAcrossRuns()
    {
        // Per-generation diagnostics (Sigma, MeanScore, BestScore,
        // WorstScore) must also be bit-identical when ties dominate. The
        // sigma trajectory is the most sensitive observable: a single
        // mis-ordered recombination cascades through the C/p_sigma update
        // and the sigma trace diverges within a few generations.
        const int dim = 3;
        var initialMean = new double[] { 0.0, 0.0, 0.0 };

        var firstHistory = new CmaEsOptimizer(
            objective:      new FixedFiniteScoreObjective(dim, score: 0.5),
            initialMean:    initialMean,
            initialSigma:   0.25,
            maxGenerations: 12,
            seed:           7).Run().History;

        for (int run = 0; run < 5; run++)
        {
            var history = new CmaEsOptimizer(
                objective:      new FixedFiniteScoreObjective(dim, score: 0.5),
                initialMean:    initialMean,
                initialSigma:   0.25,
                maxGenerations: 12,
                seed:           7).Run().History;

            Assert.Equal(firstHistory.Count, history.Count);
            for (int g = 0; g < firstHistory.Count; g++)
            {
                Assert.Equal(firstHistory[g].Sigma,      history[g].Sigma);
                Assert.Equal(firstHistory[g].MeanScore,  history[g].MeanScore);
                Assert.Equal(firstHistory[g].BestScore,  history[g].BestScore);
                Assert.Equal(firstHistory[g].WorstScore, history[g].WorstScore);
            }
        }
    }
}
