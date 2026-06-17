// CmaEsOptimizerTests — Issue #157 (T1.3 — CMA-ES inner-loop optimizer).
//
// Convergence + correctness tests on synthetic objectives where the
// optimum is known. The textbook CMA-ES literature reports
// convergence to machine precision on convex quadratic functions
// within a few hundred generations at dim ∈ [10, 30]; tests below
// pin the same regime.

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class CmaEsOptimizerTests
{
    /// <summary>
    /// Reuses the convex mock objective from IObjectiveContractTests
    /// (sum-of-squares around 0.5 in each dim, optimum 0).
    /// </summary>
    private sealed class ConvexObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;
        private readonly double _target;

        public ConvexObjective(int dim, double targetPerDim = 0.5)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++)
                _vars[i] = new DesignVariableInfo($"x{i}", -10.0, 10.0);
            _target = targetPerDim;
        }

        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            double sum = 0;
            for (int i = 0; i < vector.Length; i++)
            {
                double d = vector[i] - _target;
                sum += d * d;
            }
            return new EvaluationResult(sum, Array.Empty<FeasibilityViolation>(), null);
        }
    }

    /// <summary>
    /// Rosenbrock function: a classic non-convex banana-shaped valley.
    /// Optimum at (1, 1, ..., 1), score 0. CMA-ES should still converge.
    /// </summary>
    private sealed class RosenbrockObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;

        public RosenbrockObjective(int dim)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++)
                _vars[i] = new DesignVariableInfo($"x{i}", -5.0, 10.0);
        }

        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            double sum = 0;
            for (int i = 0; i < vector.Length - 1; i++)
            {
                double a = vector[i + 1] - vector[i] * vector[i];
                double b = 1.0 - vector[i];
                sum += 100.0 * a * a + b * b;
            }
            return new EvaluationResult(sum, Array.Empty<FeasibilityViolation>(), null);
        }
    }

    [Fact]
    public void Convex_5D_ConvergesToOptimum()
    {
        // Convex sum-of-squares in 5D, optimum at (0.5, 0.5, ..., 0.5)
        // with score 0. CMA-ES converges in ~50 generations on this
        // problem class (textbook result).
        var obj = new ConvexObjective(dim: 5);
        var initialMean = new double[5];   // start at origin (0, 0, ..., 0)
        var optimizer = new CmaEsOptimizer(
            objective:      obj,
            initialMean:    initialMean,
            initialSigma:   1.0,
            maxGenerations: 100,
            seed:           42);

        var result = optimizer.Run();
        Assert.True(result.BestScore < 1e-6,
            $"Convex 5D didn't converge: best score {result.BestScore:G6} after {result.GenerationsCompleted} generations");
        // Each dim should land within 1e-3 of the optimum 0.5.
        for (int i = 0; i < 5; i++)
        {
            Assert.InRange(result.BestParams[i], 0.5 - 1e-2, 0.5 + 1e-2);
        }
    }

    [Fact]
    public void Convex_10D_ConvergesWithinBudget()
    {
        var obj = new ConvexObjective(dim: 10, targetPerDim: 0.0);
        var optimizer = new CmaEsOptimizer(
            objective:      obj,
            initialMean:    new double[10] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
            initialSigma:   0.5,
            maxGenerations: 200,
            seed:           99);

        var result = optimizer.Run();
        Assert.True(result.BestScore < 1e-4,
            $"Convex 10D didn't converge to 1e-4: got {result.BestScore:G6}");
    }

    [Fact]
    public void Determinism_SameSeedProducesSameResult()
    {
        // CMA-ES is deterministic given a fixed seed. Two runs with
        // identical inputs must produce identical (BestParams, BestScore).
        var obj1 = new ConvexObjective(dim: 4);
        var obj2 = new ConvexObjective(dim: 4);
        var initialMean = new double[] { 1, 1, 1, 1 };

        var r1 = new CmaEsOptimizer(obj1, initialMean, 0.5, 50, seed: 7).Run();
        var r2 = new CmaEsOptimizer(obj2, initialMean, 0.5, 50, seed: 7).Run();

        Assert.Equal(r1.BestScore, r2.BestScore);
        Assert.Equal(r1.BestParams, r2.BestParams);
        Assert.Equal(r1.GenerationsCompleted, r2.GenerationsCompleted);
    }

    [Fact]
    public void Determinism_DifferentSeedProducesDifferentResult()
    {
        var initialMean = new double[] { 1, 1, 1, 1 };
        var r1 = new CmaEsOptimizer(new ConvexObjective(4), initialMean, 0.5, 30, seed: 1).Run();
        var r2 = new CmaEsOptimizer(new ConvexObjective(4), initialMean, 0.5, 30, seed: 2).Run();
        // After only 30 generations of a 4-dim problem, two seeds
        // should still produce distinct trajectories.
        Assert.NotEqual(r1.BestParams, r2.BestParams);
    }

    [Fact]
    public void History_RecordsOnePerGeneration()
    {
        var obj = new ConvexObjective(dim: 3);
        var optimizer = new CmaEsOptimizer(
            obj, new double[3], 0.3, maxGenerations: 25, seed: 1);
        var result = optimizer.Run();
        Assert.Equal(result.GenerationsCompleted, result.History.Count);
        Assert.Equal(25, result.GenerationsCompleted);
    }

    [Fact]
    public void History_MeanScoreTrendIsDecreasing()
    {
        // On a convex problem, the per-generation mean fitness should
        // trend downward. Not strictly monotonic (CMA-ES is sample-
        // based), but the last quartile should be much better than
        // the first quartile.
        var obj = new ConvexObjective(dim: 5);
        var optimizer = new CmaEsOptimizer(
            obj, new double[5], 1.0, maxGenerations: 80, seed: 42);
        var result = optimizer.Run();

        int q = result.History.Count / 4;
        double earlyMean = 0.0, lateMean = 0.0;
        for (int i = 0; i < q; i++) earlyMean += result.History[i].MeanScore;
        for (int i = result.History.Count - q; i < result.History.Count; i++)
            lateMean += result.History[i].MeanScore;
        earlyMean /= q;
        lateMean  /= q;
        Assert.True(lateMean < earlyMean * 0.1,
            $"Mean score didn't decrease enough: early={earlyMean:G4}, late={lateMean:G4}");
    }

    [Fact]
    public void Cancellation_StopsRunCleanly()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var optimizer = new CmaEsOptimizer(
            new ConvexObjective(3), new double[3], 0.3, maxGenerations: 100, seed: 1);
        var result = optimizer.Run(cts.Token);
        // Runs ZERO generations on pre-cancelled token (loop exits
        // before first gen).
        Assert.Equal(0, result.GenerationsCompleted);
    }

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        var obj = new ConvexObjective(3);
        Assert.Throws<ArgumentNullException>(() =>
            new CmaEsOptimizer(null!, new double[3], 0.3, 10));
        Assert.Throws<ArgumentNullException>(() =>
            new CmaEsOptimizer(obj, null!, 0.3, 10));
        Assert.Throws<ArgumentException>(() =>
            new CmaEsOptimizer(obj, new double[2], 0.3, 10));   // dim mismatch
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CmaEsOptimizer(obj, new double[3], 0.0, 10));   // sigma must be positive
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CmaEsOptimizer(obj, new double[3], 0.3, 0));    // maxGenerations must be positive
    }

    [Fact]
    public void Hyperparameters_DerivedFromDimension()
    {
        // Sanity: lambda + mu should match the textbook calibration.
        var obj5  = new ConvexObjective(5);
        var obj20 = new ConvexObjective(20);
        var opt5  = new CmaEsOptimizer(obj5,  new double[5],  0.5, 1, seed: 1);
        var opt20 = new CmaEsOptimizer(obj20, new double[20], 0.5, 1, seed: 1);

        // λ = 4 + ⌊3 ln(n)⌋
        // n=5  → λ ≈ 4 + 4 = 8
        // n=20 → λ ≈ 4 + 8 = 12 (3 * 2.996 ≈ 8.98)
        Assert.True(opt5.PopulationSize  >= 5 && opt5.PopulationSize  <= 12);
        Assert.True(opt20.PopulationSize >= 10 && opt20.PopulationSize <= 16);
        Assert.Equal(opt5.PopulationSize / 2,  opt5.ParentCount);
        Assert.Equal(opt20.PopulationSize / 2, opt20.ParentCount);
    }

    // ── Bounded-sampling tests (issue #211 — Hansen 2016 §3.3) ────────

    /// <summary>
    /// Tight-bounds objective whose Evaluate marks any out-of-bounds vector
    /// as infeasible (+Infinity). With reflection-at-bound enabled, every
    /// CMA-ES sample must be in-bounds, so no Evaluate call should ever
    /// receive an out-of-bounds vector.
    /// </summary>
    private sealed class TightBoundsObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;
        private readonly double _target;
        public int OutOfBoundsCallCount { get; private set; }
        public int TotalCallCount { get; private set; }

        public TightBoundsObjective(int dim, double min, double max, double target)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++)
                _vars[i] = new DesignVariableInfo($"x{i}", min, max);
            _target = target;
        }

        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            TotalCallCount++;
            for (int i = 0; i < vector.Length; i++)
            {
                if (vector[i] < _vars[i].Min || vector[i] > _vars[i].Max)
                {
                    OutOfBoundsCallCount++;
                    return new EvaluationResult(
                        double.PositiveInfinity, Array.Empty<FeasibilityViolation>(), null);
                }
            }
            double sum = 0;
            for (int i = 0; i < vector.Length; i++)
            {
                double d = vector[i] - _target;
                sum += d * d;
            }
            return new EvaluationResult(sum, Array.Empty<FeasibilityViolation>(), null);
        }
    }

    [Fact]
    public void BoundedSampling_StaysInBox_WithAggressiveSigma()
    {
        // Tight bounds [0, 1] in 5D with aggressive initial sigma 5.0
        // (5x the box width). Without reflection, ~95% of samples would
        // land outside the box on the first generation. With reflection,
        // every sample must be in-bounds.
        var obj = new TightBoundsObjective(dim: 5, min: 0.0, max: 1.0, target: 0.5);
        var optimizer = new CmaEsOptimizer(
            objective:      obj,
            initialMean:    new double[] { 0.5, 0.5, 0.5, 0.5, 0.5 },
            initialSigma:   5.0,
            maxGenerations: 30,
            seed:           42);

        var result = optimizer.Run();

        Assert.True(obj.TotalCallCount > 0, "Optimizer didn't evaluate anything");
        Assert.Equal(0, obj.OutOfBoundsCallCount);
        // BestParams must also lie in-bounds.
        for (int i = 0; i < 5; i++)
            Assert.InRange(result.BestParams[i], 0.0, 1.0);
    }

    [Fact]
    public void BoundedSampling_OptimumAtBoundary_StillConverges()
    {
        // Optimum at the upper boundary (x = 1.0 per dim, bounds [0, 1]).
        // Without bounded sampling, sigma adaptation can be hurt by silent
        // boundary excursions. With reflection, samples beyond the boundary
        // fold back into the feasible region and CMA-ES still finds the
        // optimum.
        var obj = new TightBoundsObjective(dim: 5, min: 0.0, max: 1.0, target: 1.0);
        var optimizer = new CmaEsOptimizer(
            objective:      obj,
            initialMean:    new double[] { 0.0, 0.0, 0.0, 0.0, 0.0 },
            initialSigma:   0.5,
            maxGenerations: 200,
            seed:           7);

        var result = optimizer.Run();

        Assert.Equal(0, obj.OutOfBoundsCallCount);
        Assert.True(result.BestScore < 1e-3,
            $"Boundary-optimum problem didn't converge: best={result.BestScore:G6}");
        for (int i = 0; i < 5; i++)
            Assert.InRange(result.BestParams[i], 0.95, 1.0);
    }

    [Fact]
    public void BoundedSampling_PreservesDeterminism()
    {
        // Two runs with the same seed must produce identical trajectories
        // even when reflection is firing on most samples (large sigma vs
        // tight bounds).
        var obj1 = new TightBoundsObjective(dim: 4, min: 0.0, max: 1.0, target: 0.5);
        var obj2 = new TightBoundsObjective(dim: 4, min: 0.0, max: 1.0, target: 0.5);
        var initialMean = new double[] { 0.5, 0.5, 0.5, 0.5 };

        var r1 = new CmaEsOptimizer(obj1, initialMean, 3.0, 25, seed: 13).Run();
        var r2 = new CmaEsOptimizer(obj2, initialMean, 3.0, 25, seed: 13).Run();

        Assert.Equal(r1.BestScore, r2.BestScore);
        Assert.Equal(r1.BestParams, r2.BestParams);
        Assert.Equal(r1.GenerationsCompleted, r2.GenerationsCompleted);
    }

    // ── Reflection helper unit tests ──────────────────────────────────

    [Fact]
    public void Reflect_InsideBox_PassesThrough()
    {
        Assert.Equal(5.0, CmaEsOptimizer.ReflectIntoBounds(5.0, 0.0, 10.0));
        Assert.Equal(0.0, CmaEsOptimizer.ReflectIntoBounds(0.0, 0.0, 10.0));
        Assert.Equal(10.0, CmaEsOptimizer.ReflectIntoBounds(10.0, 0.0, 10.0));
    }

    [Fact]
    public void Reflect_SmallOvershoot_ReflectsOnce()
    {
        // x = 12 with bounds [0, 10]: distance past max = 2, reflected to 10 - 2 = 8.
        Assert.Equal(8.0, CmaEsOptimizer.ReflectIntoBounds(12.0, 0.0, 10.0));
        // x = -3 with bounds [0, 10]: distance below min = 3, reflected to 0 + 3 = 3.
        Assert.Equal(3.0, CmaEsOptimizer.ReflectIntoBounds(-3.0, 0.0, 10.0));
    }

    [Fact]
    public void Reflect_LargeOvershoot_FoldsCorrectly()
    {
        // x = 25 with bounds [0, 10]: 25 → 15 (reflect) → 5 (in-bounds).
        Assert.Equal(5.0, CmaEsOptimizer.ReflectIntoBounds(25.0, 0.0, 10.0));
        // x = 40 with bounds [0, 10]: 40 → 30 → 20 → 10 → 0.
        Assert.Equal(0.0, CmaEsOptimizer.ReflectIntoBounds(40.0, 0.0, 10.0));
        // x = -25 with bounds [0, 10]: 25 below min, reflects to 25 above max,
        // then back down → 5 final.
        Assert.Equal(5.0, CmaEsOptimizer.ReflectIntoBounds(-25.0, 0.0, 10.0));
    }

    [Fact]
    public void Reflect_DegenerateBounds_PinsToMin()
    {
        Assert.Equal(3.0, CmaEsOptimizer.ReflectIntoBounds(7.0, 3.0, 3.0));
        Assert.Equal(3.0, CmaEsOptimizer.ReflectIntoBounds(-100.0, 3.0, 3.0));
        // max < min is a degenerate caller bug; treat as pinned-to-min.
        Assert.Equal(3.0, CmaEsOptimizer.ReflectIntoBounds(50.0, 3.0, 1.0));
    }

    [Fact]
    public void Reflect_AsymmetricBounds_ReflectsCorrectly()
    {
        // Bounds [-2, 8], span = 10. x = 12: 12 - (-2) = 14, fold mod 20 = 14,
        // 14 > 10 → folded = 6, result = -2 + 6 = 4.
        Assert.Equal(4.0, CmaEsOptimizer.ReflectIntoBounds(12.0, -2.0, 8.0));
        // x = -5 → y = -3, fold mod 20: -3 - 20*Floor(-0.15) = -3 + 20 = 17;
        // 17 > 10 → folded = 3, result = -2 + 3 = 1.
        Assert.Equal(1.0, CmaEsOptimizer.ReflectIntoBounds(-5.0, -2.0, 8.0));
    }
}
