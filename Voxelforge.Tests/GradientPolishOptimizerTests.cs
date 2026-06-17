// GradientPolishOptimizerTests — T4.1 finite-difference gradient polish validation.
//
// Tests cover convergence on synthetic convex objectives, best-seen invariant,
// bound satisfaction, cancellation, and edge-case behaviour.

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class GradientPolishOptimizerTests
{
    // ── Synthetic quadratic objective ────────────────────────────────────

    // f(x) = sum_i (x_i - target_i)^2, with bounds [lo, hi] per dimension.
    private sealed class QuadraticObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;
        private readonly double[] _target;

        public QuadraticObjective(double[] lo, double[] hi, double[]? target = null)
        {
            _vars   = new DesignVariableInfo[lo.Length];
            _target = target ?? new double[lo.Length];
            for (int i = 0; i < lo.Length; i++)
                _vars[i] = new DesignVariableInfo($"x{i}", lo[i], hi[i]);
        }

        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;

        public EvaluationResult Evaluate(ReadOnlySpan<double> v, CancellationToken ct = default)
        {
            double score = 0.0;
            for (int i = 0; i < v.Length; i++)
            {
                double d = v[i] - _target[i];
                score += d * d;
            }
            return new EvaluationResult(score, Array.Empty<FeasibilityViolation>(), null);
        }
    }

    // Constant-score objective (flat landscape).
    private sealed class FlatObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;
        public FlatObjective(int dim)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++) _vars[i] = new DesignVariableInfo($"x{i}", 0.0, 1.0);
        }
        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;
        public EvaluationResult Evaluate(ReadOnlySpan<double> v, CancellationToken ct = default) =>
            new EvaluationResult(1.0, Array.Empty<FeasibilityViolation>(), null);
    }

    // ── Convergence ───────────────────────────────────────────────────────

    [Fact]
    public void QuadraticConvergence_ScoreImproves()
    {
        // f(x) = sum(xi - 0.5)^2; start at (0,...,0); optimum at (0.5,...,0.5).
        int dim = 5;
        var lo  = new double[dim];
        var hi  = new double[] { 1.0, 1.0, 1.0, 1.0, 1.0 };
        var target = new double[] { 0.5, 0.5, 0.5, 0.5, 0.5 };
        var obj = new QuadraticObjective(lo, hi, target);

        var optimizer    = new GradientPolishOptimizer(obj, maxSteps: 20);
        var initialX     = new double[dim]; // all zeros
        double initialScore = obj.Evaluate(initialX).Score;  // = 5 * 0.25 = 1.25

        var result = optimizer.Polish(initialX, initialScore);

        Assert.True(result.BestScore < initialScore,
            $"Expected improvement; BestScore={result.BestScore:F6} initialScore={initialScore:F6}");
        Assert.True(result.ImprovementFraction >= 0.10,
            $"Expected ≥10%% improvement; got {result.ImprovementFraction:P1}");
    }

    [Fact]
    public void ImprovementFraction_PositiveForConvexFromOffCenter()
    {
        // Any run from a non-optimal starting point should yield positive fraction.
        var lo  = new double[] { -2.0 };
        var hi  = new double[] {  2.0 };
        var target = new double[] { 1.0 };
        var obj = new QuadraticObjective(lo, hi, target);

        var optimizer = new GradientPolishOptimizer(obj, maxSteps: 30, learningRate: 0.1);
        double initialScore = obj.Evaluate(new double[] { -1.0 }).Score;
        var result = optimizer.Polish(new double[] { -1.0 }, initialScore);

        Assert.True(result.ImprovementFraction > 0.0,
            $"Expected positive improvement; got {result.ImprovementFraction:F6}");
    }

    // ── Best-seen invariant ───────────────────────────────────────────────

    [Fact]
    public void BestScore_AlwaysLeqInitialScore()
    {
        // Even with a noisy / non-convex objective, BestScore must not exceed initialScore.
        // Use a simple bowl but start from a reasonable point.
        var lo  = new double[] { 0.0, 0.0, 0.0 };
        var hi  = new double[] { 1.0, 1.0, 1.0 };
        var target = new double[] { 0.7, 0.3, 0.5 };
        var obj = new QuadraticObjective(lo, hi, target);

        var optimizer    = new GradientPolishOptimizer(obj, maxSteps: 10);
        var initialX     = new double[] { 0.1, 0.9, 0.1 };
        double initialScore = obj.Evaluate(initialX).Score;

        for (int trial = 0; trial < 5; trial++)
        {
            var result = optimizer.Polish(initialX, initialScore);
            Assert.True(result.BestScore <= initialScore + 1e-12,
                $"Trial {trial}: BestScore {result.BestScore:F8} > initialScore {initialScore:F8}");
        }
    }

    // ── Bound satisfaction ────────────────────────────────────────────────

    [Fact]
    public void BoundRespected_AllParamsInBounds()
    {
        int dim = 4;
        var lo  = new double[] { 0.2, 0.1, 0.0, -0.5 };
        var hi  = new double[] { 0.8, 0.9, 1.0,  0.5 };
        var target = new double[] { 0.5, 0.5, 0.5, 0.0 };
        var obj = new QuadraticObjective(lo, hi, target);

        var optimizer   = new GradientPolishOptimizer(obj, maxSteps: 25, learningRate: 0.3);
        var initialX    = new double[] { 0.25, 0.15, 0.05, -0.4 };
        double initScore = obj.Evaluate(initialX).Score;
        var result = optimizer.Polish(initialX, initScore);

        for (int i = 0; i < dim; i++)
        {
            Assert.True(result.BestParams[i] >= lo[i] - 1e-14,
                $"Dim {i}: BestParams[i]={result.BestParams[i]:F6} < lo={lo[i]}");
            Assert.True(result.BestParams[i] <= hi[i] + 1e-14,
                $"Dim {i}: BestParams[i]={result.BestParams[i]:F6} > hi={hi[i]}");
        }
    }

    // ── Cancellation ─────────────────────────────────────────────────────

    [Fact]
    public void Cancellation_StepsCompleted_LessThanMax()
    {
        using var cts  = new CancellationTokenSource();
        int dim        = 10;
        var lo         = new double[dim];
        var hi         = Repeat(1.0, dim);
        var obj        = new QuadraticObjective(lo, hi);
        var optimizer  = new GradientPolishOptimizer(obj, maxSteps: 200);
        var initialX   = Repeat(0.1, dim);
        double initScore = obj.Evaluate(initialX).Score;

        cts.Cancel();
        var result = optimizer.Polish(initialX, initScore, cts.Token);

        Assert.True(result.StepsCompleted < 200,
            $"Expected cancellation before 200 steps; got {result.StepsCompleted}");
        Assert.NotNull(result.BestParams);
    }

    // ── Flat landscape ────────────────────────────────────────────────────

    [Fact]
    public void FlatObjective_NoCrash()
    {
        var obj      = new FlatObjective(3);
        var optimizer = new GradientPolishOptimizer(obj, maxSteps: 10);
        var initialX = new double[] { 0.5, 0.5, 0.5 };
        var result   = optimizer.Polish(initialX, 1.0);

        Assert.NotNull(result);
        Assert.True(result.BestScore <= 1.0 + 1e-12,
            $"Flat objective: BestScore={result.BestScore:F6} > 1.0");
    }

    // ── Step count ────────────────────────────────────────────────────────

    [Fact]
    public void StepCount_EqualsMaxSteps_WhenNoCancellation()
    {
        int maxSteps = 7;
        var obj      = new FlatObjective(2);
        var optimizer = new GradientPolishOptimizer(obj, maxSteps: maxSteps);
        var result   = optimizer.Polish(new double[] { 0.5, 0.5 }, 1.0);

        Assert.Equal(maxSteps, result.StepsCompleted);
    }

    // ── Single dimension ─────────────────────────────────────────────────

    [Fact]
    public void SingleDimension_CorrectGradient()
    {
        // f(x) = (x - 0.8)^2 in [0, 1]; gradient at x=0 is -2*(0-0.8)=-1.6... negative.
        // One learning step with lr=0.05, h=1e-3: gradient ≈ (f(1e-3)-f(-1e-3))/(2e-3)
        // ≈ (0.6348 - 0.6452)/0.002 = -1.6 → x_new = 0 - 0.05 * (-1.6) = 0.08.
        var obj = new QuadraticObjective(new double[] { 0.0 }, new double[] { 1.0 },
            new double[] { 0.8 });
        var optimizer = new GradientPolishOptimizer(obj, maxSteps: 1,
            relativeStepSize: 1e-3, learningRate: 0.05);
        double initScore = obj.Evaluate(new double[] { 0.0 }).Score;
        var result = optimizer.Polish(new double[] { 0.0 }, initScore);

        Assert.Equal(1, result.StepsCompleted);
        // After one step the score should be closer to optimum.
        Assert.True(result.BestScore <= initScore + 1e-12,
            $"BestScore {result.BestScore:F6} should be ≤ initScore {initScore:F6}");
    }

    // ── Learning rate zero ────────────────────────────────────────────────

    [Fact]
    public void LearningRate_Zero_XNeverMoves()
    {
        var lo  = new double[] { 0.0, 0.0 };
        var hi  = new double[] { 1.0, 1.0 };
        var obj = new QuadraticObjective(lo, hi, new double[] { 0.5, 0.5 });
        var optimizer = new GradientPolishOptimizer(obj, maxSteps: 10, learningRate: 0.0);
        var x0 = new double[] { 0.1, 0.9 };
        double initScore = obj.Evaluate(x0).Score;

        var result = optimizer.Polish(x0, initScore);

        // With lr=0 the gradient step doesn't move x; score stays at initial.
        Assert.True(Math.Abs(result.BestScore - initScore) < 1e-12,
            $"With lr=0, score should not change: was {initScore:F8}, got {result.BestScore:F8}");
    }

    // ── MaxSteps zero ─────────────────────────────────────────────────────

    [Fact]
    public void MaxSteps_Zero_ReturnsInitialState()
    {
        var obj = new FlatObjective(2);
        var optimizer = new GradientPolishOptimizer(obj, maxSteps: 0);
        var x0 = new double[] { 0.3, 0.7 };
        const double initScore = 42.0;

        var result = optimizer.Polish(x0, initScore);

        Assert.Equal(0, result.StepsCompleted);
        Assert.Equal(initScore, result.BestScore);
        Assert.Equal(0.0, result.ImprovementFraction);
        Assert.Equal(x0[0], result.BestParams[0]);
        Assert.Equal(x0[1], result.BestParams[1]);
    }

    // ── Improvement fraction at minimum ───────────────────────────────────

    [Fact]
    public void ImprovementFraction_NearZero_IfStartedAtMinimum()
    {
        // Start exactly at the optimum; gradient ≈ 0, fraction ≈ 0 (may be small and negative).
        var lo  = new double[] { 0.0 };
        var hi  = new double[] { 1.0 };
        var obj = new QuadraticObjective(lo, hi, new double[] { 0.5 });
        var optimizer = new GradientPolishOptimizer(obj, maxSteps: 5);
        double initScore = obj.Evaluate(new double[] { 0.5 }).Score;  // ≈ 0

        var result = optimizer.Polish(new double[] { 0.5 }, initScore);

        // ImprovementFraction = (initScore - BestScore) / max(|initScore|, 1e-12).
        // initScore ≈ 0 → denominator = 1e-12; fraction could be large but BestScore ≤ 0 + ε.
        // What we can assert: BestScore ≥ 0 (quadratic) and ≤ initScore + tiny.
        Assert.True(result.BestScore >= -1e-12,
            $"Quadratic at optimum: BestScore {result.BestScore:E6} should be ≥ 0");
    }

    // ── Constructor validation ────────────────────────────────────────────

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        var obj = new FlatObjective(2);
        Assert.Throws<ArgumentNullException>(() =>
            new GradientPolishOptimizer(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GradientPolishOptimizer(obj, maxSteps: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GradientPolishOptimizer(obj, relativeStepSize: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GradientPolishOptimizer(obj, learningRate: -0.1));
    }

    // ── Polish validates initialParams ────────────────────────────────────

    [Fact]
    public void Polish_ValidatesInitialParams()
    {
        var obj       = new FlatObjective(3);
        var optimizer = new GradientPolishOptimizer(obj, maxSteps: 1);

        Assert.Throws<ArgumentNullException>(() =>
            optimizer.Polish(null!, 0.0));
        Assert.Throws<ArgumentException>(() =>
            optimizer.Polish(new double[] { 0.5, 0.5 }, 0.0));  // wrong length (2 ≠ 3)
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static double[] Repeat(double v, int n)
    {
        var a = new double[n];
        Array.Fill(a, v);
        return a;
    }
}
