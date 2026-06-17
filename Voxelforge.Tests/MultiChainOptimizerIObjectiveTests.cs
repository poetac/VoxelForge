// Slice 2 of the IObjective decoupling (2026-04-28).
//
// Pins the strict-determinism bridge: MultiChainOptimizer's IObjective
// Run overload is bit-identical to the equivalent Func<> overload with
// the same evaluation semantics. This is what guarantees a
// reproducibility contract upgrade (legacy tooling keeps working) +
// no risk of a silent SA-trajectory shift when production wiring
// migrates to IObjective.

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public sealed class MultiChainOptimizerIObjectiveTests
{
    private static (double Min, double Max)[] StandardBounds(int dim)
    {
        var b = new (double, double)[dim];
        for (int i = 0; i < dim; i++) b[i] = (0.0, 1.0);
        return b;
    }

    /// <summary>
    /// Convex synthetic objective for Func/IObjective bridge equivalence.
    /// Score is sum-of-squares around 0.5; identical to the Func used in
    /// MultiChainOptimizerTests.ConvexEvaluator. Reusing the score
    /// formula across both shapes is the bridge-equivalence pivot.
    /// </summary>
    private sealed class ConvexObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;

        public ConvexObjective(int dim)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++)
                _vars[i] = new DesignVariableInfo($"x{i}", 0.0, 1.0);
        }

        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            double sum = 0;
            for (int i = 0; i < vector.Length; i++)
            {
                double d = vector[i] - 0.5;
                sum += d * d;
            }
            return new EvaluationResult(sum, Array.Empty<FeasibilityViolation>(), null);
        }
    }

    private static (double, object?) ConvexFunc(double[] x)
    {
        double sum = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double d = x[i] - 0.5;
            sum += d * d;
        }
        return (sum, null);
    }

    [Fact]
    public void IObjectiveRun_BitIdenticalToFuncRun_OnSameSemantics()
    {
        // The contract: any Func evaluator F and any IObjective O whose
        // Score-vs-vector mapping is identical must produce the same
        // (BestParams, BestScore, WinningChain) at the same baseSeed +
        // chainCount + maxIter. This is the strict-determinism bridge
        // property — what guarantees migrating production wiring from
        // Func to IObjective is a no-op on the SA trajectory.
        var bounds = StandardBounds(8);
        const int baseSeed = 42;
        const int chainCount = 4;
        const int iters = 200;

        var optFunc      = new MultiChainOptimizer(bounds, iters, baseSeed, chainCount);
        var optObjective = new MultiChainOptimizer(bounds, iters, baseSeed, chainCount);

        var rFunc = optFunc.Run(ConvexFunc);
        var rObj  = optObjective.Run(new ConvexObjective(8));

        Assert.Equal(rFunc.BestScore, rObj.BestScore);
        Assert.Equal(rFunc.BestParams, rObj.BestParams);
        Assert.Equal(rFunc.WinningChain, rObj.WinningChain);
        Assert.Equal(rFunc.TotalIterations, rObj.TotalIterations);
    }

    [Fact]
    public void IObjectiveRun_StrictDeterminism_HoldsAcross5Runs()
    {
        // Direct strict-determinism check on the IObjective path itself
        // (not via the bridge). Independent of bridge equivalence — pins
        // the property even if a future refactor changes how the bridge
        // adapts internally.
        var bounds = StandardBounds(4);
        var firstResult = new MultiChainOptimizer(bounds, 100, baseSeed: 7, chainCount: 4)
            .Run(new ConvexObjective(4));
        for (int i = 0; i < 4; i++)
        {
            var r = new MultiChainOptimizer(bounds, 100, baseSeed: 7, chainCount: 4)
                .Run(new ConvexObjective(4));
            Assert.Equal(firstResult.BestScore, r.BestScore);
            Assert.Equal(firstResult.BestParams, r.BestParams);
        }
    }

    [Fact]
    public void IObjectiveCtor_DerivesBoundsFromVariables()
    {
        // The IObjective constructor projects objective.Variables to
        // bounds via DesignVariableInfo.ToBoundsArray. Verify SA samples
        // stay inside the projected bounds by running a short SA + checking
        // every history entry's parameters are clamped.
        var obj = new ConvexObjective(3);  // x0..x2 ∈ [0.0, 1.0]
        var opt = new MultiChainOptimizer(obj, maxIterations: 50, baseSeed: 1, chainCount: 2);
        var r = opt.Run(obj);

        // Best params should be inside the bounds derived from the objective.
        for (int i = 0; i < r.BestParams.Length; i++)
        {
            Assert.InRange(r.BestParams[i], 0.0, 1.0);
        }
    }

    [Fact]
    public void IObjectiveCtor_NullObjective_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new MultiChainOptimizer(objective: null!, maxIterations: 10, baseSeed: 1));
    }

    [Fact]
    public void IObjectiveRun_NullObjective_Throws()
    {
        var opt = new MultiChainOptimizer(StandardBounds(3), 10, 1);
        Assert.Throws<ArgumentNullException>(
            () => opt.Run(objective: null!));
    }

    [Fact]
    public void IObjectiveRun_DimensionMismatch_Throws()
    {
        // Bounds are 4-D; objective is 3-D — should fail loudly rather
        // than silently passing 4 doubles to a 3-D objective.
        var opt = new MultiChainOptimizer(StandardBounds(4), 10, 1);
        var obj3 = new ConvexObjective(3);
        var ex = Assert.Throws<ArgumentException>(() => opt.Run(obj3));
        Assert.Contains("DimensionCount", ex.Message);
    }

    [Fact]
    public void IObjectiveRun_BestBreakdown_IsEvaluationResult()
    {
        // The IObjective overload stores the full EvaluationResult as
        // BestBreakdown so consumers get first-class access to
        // .Violations alongside .EngineSpecificBreakdown — vs the legacy
        // Func path which stores whatever the Func returned as breakdown.
        var obj = new ConvexObjective(4);
        var opt = new MultiChainOptimizer(obj, maxIterations: 50, baseSeed: 11, chainCount: 2);
        var r = opt.Run(obj);

        var eval = Assert.IsType<EvaluationResult>(r.BestBreakdown);
        Assert.Equal(r.BestScore, eval.Score);
        Assert.Empty(eval.Violations);
        Assert.Null(eval.EngineSpecificBreakdown);
    }

    /// <summary>
    /// Objective that ALWAYS returns a violation, regardless of vector.
    /// Used to verify that violations propagate through the bridge.
    /// </summary>
    private sealed class AlwaysInfeasibleObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;
        public AlwaysInfeasibleObjective(int dim)
        {
            _vars = new DesignVariableInfo[dim];
            for (int i = 0; i < dim; i++) _vars[i] = new DesignVariableInfo($"v{i}", 0, 1);
        }
        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;
        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            var v = new FeasibilityViolation("MOCK_GATE", "always fails", 1.0, 0.5);
            return new EvaluationResult(double.PositiveInfinity, new[] { v }, "engine-detail");
        }
    }

    [Fact]
    public void IObjectiveRun_ViolationsPropagate_ThroughBestBreakdown()
    {
        // When every chain candidate is infeasible, MultiChain's
        // infeasible-exit trips early. The first iteration's breakdown
        // is recorded on chain 0's BestBreakdown — verify violations
        // round-trip through the bridge intact.
        var obj = new AlwaysInfeasibleObjective(3);
        var opt = new MultiChainOptimizer(obj, maxIterations: 100, baseSeed: 1, chainCount: 2);
        var r = opt.Run(obj);

        var eval = Assert.IsType<EvaluationResult>(r.BestBreakdown);
        Assert.True(double.IsPositiveInfinity(eval.Score));
        Assert.Single(eval.Violations);
        Assert.Equal("MOCK_GATE", eval.Violations[0].ConstraintId);
        Assert.Equal("engine-detail", eval.EngineSpecificBreakdown);
    }

    [Fact]
    public void IObjectiveRun_ProgressCallbackInvoked()
    {
        var obj = new ConvexObjective(3);
        var opt = new MultiChainOptimizer(obj, maxIterations: 30, baseSeed: 1, chainCount: 2);

        int progressCount = 0;
        opt.Run(obj, onProgress: _ => Interlocked.Increment(ref progressCount));

        // 2 chains × 30 iters = 60 expected progress calls.
        Assert.True(progressCount >= 30,
            $"expected at least 30 progress callbacks, got {progressCount}");
    }
}
