// EngineObjectiveAdapterTests.cs — Sprint A Phase 2 (2026-05-04).
//
// Coverage:
//   a. Round-trip: adapter + RocketEngine produces the same score as
//      RegenObjective.ScoreDesign for the same design + conditions.
//   b. Dimension count and Variables surface are consistent.
//   c. Vector-length mismatch throws ArgumentException.
//   d. Null constructor arguments throw ArgumentNullException.
//   e. Determinism: two calls with the same vector return the same score.
//   f. CancellationToken: cancelled token throws OperationCanceledException.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Voxelforge.Combustion;
using Voxelforge.Engines;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class EngineObjectiveAdapterTests
{
    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N                = 5_000,
        ChamberPressure_Pa      = 6.9e6,
        MixtureRatio            = 3.3,
        PropellantPair          = PropellantPair.LOX_CH4,
        WallMaterialIndex       = 1,
        CoolantInletTemp_K      = 150.0,
        CoolantInletPressure_Pa = 12e6,
    };

    private static RegenChamberDesign DefaultDesign() => new()
    {
        ExpansionRatio    = 6.0,
        ContractionRatio  = 4.0,
    };

    private static (EngineObjectiveAdapter<RegenChamberDesign, OperatingConditions, RocketEngineResult> adapter,
                    DesignVariableInfo[] variables)
        BuildRocketAdapter(OperatingConditions cond, RegenChamberDesign baseline)
    {
        var descriptors = DesignVariableRegistry.DescriptorsForMany(
            typeof(RegenChamberDesign), typeof(Injector.InjectorPattern));
        var variables = descriptors.Select(d => new DesignVariableInfo(d.MemberName, d.Min, d.Max)).ToArray();

        var adapter = new EngineObjectiveAdapter<RegenChamberDesign, OperatingConditions, RocketEngineResult>(
            engine:     RocketEngine.Instance,
            conditions: cond,
            baseline:   baseline,
            variables:  variables,
            unpack:    (vec, bl) => RegenChamberOptimization.Unpack(vec, bl),
            evaluate:   result =>
            {
                // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
                var score = RegenChamberOptimization.Evaluate(result.Generation, RegenChamberOptimization.Profiles[0]);
                return new EvaluationResult(score.TotalScore, result.Violations, score);
            });

        return (adapter, variables);
    }

    // ── a. Round-trip score matches RegenObjective.ScoreDesign ───────────

    [Fact]
    public void RocketAdapter_RoundTrip_ScoreMatchesScoreDesign()
    {
        var cond     = DefaultConditions();
        var baseline = DefaultDesign();

        var (adapter, _) = BuildRocketAdapter(cond, baseline);

        // Pack baseline to get a canonical SA vector.
        var vector = RegenChamberOptimization.Pack(baseline);

        var adapterResult = adapter.Evaluate(vector);
        // #551: ScoreDesign now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
        var directResult  = RegenObjective.ScoreDesign(cond, baseline, RegenChamberOptimization.Profiles[0]);

        // Scores must be identical — both ultimately call GenerateWith on the
        // same (deterministic) design + conditions.
        Assert.Equal(directResult.Score, adapterResult.Score);
    }

    // ── b. DimensionCount and Variables are consistent ───────────────────

    [Fact]
    public void RocketAdapter_DimensionCountMatchesVariables()
    {
        var (adapter, variables) = BuildRocketAdapter(DefaultConditions(), DefaultDesign());
        Assert.Equal(variables.Length, adapter.DimensionCount);
        Assert.Equal(variables.Length, adapter.Variables.Count);
    }

    // ── c. Vector-length mismatch throws ArgumentException ───────────────

    [Fact]
    public void RocketAdapter_VectorLengthMismatch_Throws()
    {
        var (adapter, _) = BuildRocketAdapter(DefaultConditions(), DefaultDesign());
        var wrongLen = new double[adapter.DimensionCount + 1];
        Assert.Throws<ArgumentException>(() => adapter.Evaluate(wrongLen));
    }

    // ── d. Null constructor arguments throw ArgumentNullException ─────────

    [Fact]
    public void RocketAdapter_NullArguments_Throw()
    {
        var cond     = DefaultConditions();
        var baseline = DefaultDesign();
        var vars     = new[] { new DesignVariableInfo("x", 0, 1) };
        Func<double[], RegenChamberDesign, RegenChamberDesign> unpack  = (v, b) => b;
        Func<RocketEngineResult, EvaluationResult>             evalFn  =
            r => new EvaluationResult(0, r.Violations, r);

        Assert.Throws<ArgumentNullException>(() =>
            new EngineObjectiveAdapter<RegenChamberDesign, OperatingConditions, RocketEngineResult>(
                null!, cond, baseline, vars, unpack, evalFn));
        Assert.Throws<ArgumentNullException>(() =>
            new EngineObjectiveAdapter<RegenChamberDesign, OperatingConditions, RocketEngineResult>(
                RocketEngine.Instance, null!, baseline, vars, unpack, evalFn));
        Assert.Throws<ArgumentNullException>(() =>
            new EngineObjectiveAdapter<RegenChamberDesign, OperatingConditions, RocketEngineResult>(
                RocketEngine.Instance, cond, null!, vars, unpack, evalFn));
        Assert.Throws<ArgumentNullException>(() =>
            new EngineObjectiveAdapter<RegenChamberDesign, OperatingConditions, RocketEngineResult>(
                RocketEngine.Instance, cond, baseline, null!, unpack, evalFn));
        Assert.Throws<ArgumentNullException>(() =>
            new EngineObjectiveAdapter<RegenChamberDesign, OperatingConditions, RocketEngineResult>(
                RocketEngine.Instance, cond, baseline, vars, null!, evalFn));
        Assert.Throws<ArgumentNullException>(() =>
            new EngineObjectiveAdapter<RegenChamberDesign, OperatingConditions, RocketEngineResult>(
                RocketEngine.Instance, cond, baseline, vars, unpack, null!));
    }

    // ── e. Determinism ────────────────────────────────────────────────────

    [Fact]
    public void RocketAdapter_Determinism_SameVectorSameScore()
    {
        var cond     = DefaultConditions();
        var baseline = DefaultDesign();
        var (adapter, _) = BuildRocketAdapter(cond, baseline);
        var vector = RegenChamberOptimization.Pack(baseline);

        var r1 = adapter.Evaluate(vector);
        var r2 = adapter.Evaluate(vector);

        Assert.Equal(r1.Score, r2.Score);
        Assert.Equal(r1.Violations.Count, r2.Violations.Count);
    }

    // ── f. CancellationToken propagates ───────────────────────────────────

    [Fact]
    public void RocketAdapter_CancelledToken_Throws()
    {
        var (adapter, _) = BuildRocketAdapter(DefaultConditions(), DefaultDesign());
        var vector = RegenChamberOptimization.Pack(DefaultDesign());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(
            () => adapter.Evaluate(vector, cts.Token));
    }
}
