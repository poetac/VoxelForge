// IObjective + EvaluationResult + DesignVariableInfo contract tests.
//
// Slice 1 of the IObjective decoupling work. Pins the basic shape —
// interface members, record-equality,
// bounds-array projection, ReadOnlySpan ergonomics — without exercising
// any production engine-family path. The MockObjective fixture defined
// here is reused by later slices that wire IObjective into
// MultiChainOptimizer.

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public sealed class IObjectiveContractTests
{
    /// <summary>
    /// Convex synthetic objective for testing: sum of squares around a
    /// per-dim minimum. Pure, deterministic, thread-safe, no engine
    /// physics — suitable for IObjective shape testing without any
    /// production code path.
    /// </summary>
    public sealed class ConvexMockObjective : IObjective
    {
        private readonly DesignVariableInfo[] _vars;
        private readonly double[] _targets;

        public ConvexMockObjective(int dim, double minBound = 0.0, double maxBound = 1.0, double targetPerDim = 0.5)
        {
            _vars = new DesignVariableInfo[dim];
            _targets = new double[dim];
            for (int i = 0; i < dim; i++)
            {
                _vars[i] = new DesignVariableInfo($"x{i}", minBound, maxBound);
                _targets[i] = targetPerDim;
            }
        }

        public int DimensionCount => _vars.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _vars;

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            if (vector.Length != _vars.Length)
                throw new ArgumentException(
                    $"vector length {vector.Length} != DimensionCount {_vars.Length}", nameof(vector));

            double sum = 0.0;
            for (int i = 0; i < vector.Length; i++)
            {
                double d = vector[i] - _targets[i];
                sum += d * d;
            }
            return new EvaluationResult(
                Score: sum,
                Violations: Array.Empty<FeasibilityViolation>(),
                EngineSpecificBreakdown: null);
        }
    }

    [Fact]
    public void DesignVariableInfo_BasicShape()
    {
        var v = new DesignVariableInfo("ContractionRatio", 2.5, 10.0);
        Assert.Equal("ContractionRatio", v.Name);
        Assert.Equal(2.5, v.Min);
        Assert.Equal(10.0, v.Max);
    }

    [Fact]
    public void DesignVariableInfo_RecordEquality()
    {
        var a = new DesignVariableInfo("L*", 0.5, 2.0);
        var b = new DesignVariableInfo("L*", 0.5, 2.0);
        var c = new DesignVariableInfo("L*", 0.5, 2.5);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void DesignVariableInfo_ToBoundsArray_PreservesOrder()
    {
        var vars = new[]
        {
            new DesignVariableInfo("a", 0.0, 1.0),
            new DesignVariableInfo("b", -2.0, 5.0),
            new DesignVariableInfo("c", 100.0, 200.0),
        };
        var bounds = DesignVariableInfo.ToBoundsArray(vars);
        Assert.Equal(3, bounds.Length);
        Assert.Equal((0.0, 1.0), bounds[0]);
        Assert.Equal((-2.0, 5.0), bounds[1]);
        Assert.Equal((100.0, 200.0), bounds[2]);
    }

    [Fact]
    public void DesignVariableInfo_ToBoundsArray_RejectsInvertedBounds()
    {
        var vars = new[]
        {
            new DesignVariableInfo("ok", 0.0, 1.0),
            new DesignVariableInfo("inverted", 5.0, 5.0),  // Min == Max — invalid
        };
        var ex = Assert.Throws<ArgumentException>(
            () => DesignVariableInfo.ToBoundsArray(vars));
        Assert.Contains("inverted", ex.Message);
    }

    [Fact]
    public void DesignVariableInfo_ToBoundsArray_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(
            () => DesignVariableInfo.ToBoundsArray(null!));
    }

    [Fact]
    public void EvaluationResult_BasicShape()
    {
        var v = new FeasibilityViolation("WALL_TEMP", "wall T over limit", 1500.0, 1300.0);
        var r = new EvaluationResult(
            Score: 42.0,
            Violations: new[] { v },
            EngineSpecificBreakdown: "engine-specific-thing");
        Assert.Equal(42.0, r.Score);
        Assert.Single(r.Violations);
        Assert.Equal("WALL_TEMP", r.Violations[0].ConstraintId);
        Assert.Equal("engine-specific-thing", r.EngineSpecificBreakdown);
    }

    [Fact]
    public void EvaluationResult_RecordEquality_OnValueFields()
    {
        // Score + Violations + EngineSpecificBreakdown are reference-equal
        // when they're the same instance, so identical-content records
        // with shared sub-objects compare equal.
        var v = new FeasibilityViolation("X", "x", 1.0, 0.5);
        var vios = new[] { v };
        var a = new EvaluationResult(1.0, vios, null);
        var b = new EvaluationResult(1.0, vios, null);
        Assert.Equal(a, b);

        var c = new EvaluationResult(2.0, vios, null);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void IObjective_MockEvaluate_ReturnsExpectedScore()
    {
        var obj = new ConvexMockObjective(dim: 4);
        Span<double> vec = stackalloc double[4] { 0.5, 0.5, 0.5, 0.5 };
        var r = obj.Evaluate(vec);
        Assert.Equal(0.0, r.Score, precision: 12);
        Assert.Empty(r.Violations);
        Assert.Null(r.EngineSpecificBreakdown);
    }

    [Fact]
    public void IObjective_MockEvaluate_ScoreIsConvex()
    {
        var obj = new ConvexMockObjective(dim: 2);
        Span<double> atMin = stackalloc double[2] { 0.5, 0.5 };
        Span<double> offset = stackalloc double[2] { 0.7, 0.5 };
        var rMin = obj.Evaluate(atMin);
        var rOff = obj.Evaluate(offset);
        Assert.True(rOff.Score > rMin.Score,
            $"offset score {rOff.Score} should exceed minimum {rMin.Score}");
    }

    [Fact]
    public void IObjective_MockEvaluate_RejectsWrongVectorLength()
    {
        var obj = new ConvexMockObjective(dim: 4);
        Assert.Throws<ArgumentException>(() =>
        {
            Span<double> wrong = stackalloc double[3] { 0.0, 0.0, 0.0 };
            obj.Evaluate(wrong);
        });
    }

    [Fact]
    public void IObjective_DimensionCount_MatchesVariablesCount()
    {
        var obj = new ConvexMockObjective(dim: 7);
        Assert.Equal(7, obj.DimensionCount);
        Assert.Equal(7, obj.Variables.Count);
    }

    [Fact]
    public void IObjective_Variables_ProjectableToBounds()
    {
        var obj = new ConvexMockObjective(dim: 5, minBound: -1.0, maxBound: 3.0);
        var bounds = DesignVariableInfo.ToBoundsArray(obj.Variables);
        Assert.Equal(5, bounds.Length);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(-1.0, bounds[i].Min);
            Assert.Equal(3.0, bounds[i].Max);
        }
    }

    [Fact]
    public void IObjective_CancellationToken_AcceptedButNotRequired()
    {
        // The contract: Evaluate accepts a CancellationToken but is not
        // required to poll it. The mock here ignores the token entirely;
        // the contract test just confirms the call shape compiles + runs
        // with a token parameter, including a pre-cancelled one.
        var obj = new ConvexMockObjective(dim: 2);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Span<double> vec = stackalloc double[2] { 0.3, 0.4 };
        var r = obj.Evaluate(vec, cts.Token);
        Assert.True(r.Score > 0);
    }
}
