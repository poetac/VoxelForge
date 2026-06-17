// SingleChainSaObjectiveTests.cs — IEngine Phase 2 (ADR-025, 2026-05-06).
//
// Coverage:
//   a. SA BestScore is monotonically non-increasing over 10 iterations when
//      driven via RegenObjective — verifies the IObjective-routed hot path
//      produces scores that SA can accept/reject correctly.
//   b. RegenObjective.Evaluate returns a finite or +∞ score for the default
//      design (not NaN — the path is alive).
//   c. EvaluationResult.EngineSpecificBreakdown is a RegenScoreResult when
//      physics runs to completion — the cast that Program.Sa.cs now relies on
//      is pinned.
//   d. Pre-screen infeasibility path: a design that deliberately violates a
//      pre-screen gate returns +∞ with a non-empty Violations list and a null
//      EngineSpecificBreakdown (pre-screen short-circuit, no physics ran).

using System;
using System.Threading;
using Voxelforge.Combustion;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class SingleChainSaObjectiveTests
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
        ExpansionRatio   = 6.0,
        ContractionRatio = 4.0,
    };

    // ── a. SA best score monotonically non-increasing via RegenObjective ──

    [Fact]
    public void SingleChainSA_Via_RegenObjective_BestScore_MonotonicallyNonIncreasing()
    {
        var cond     = DefaultConditions();
        var baseline = DefaultDesign();

        // #551: RegenObjective now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
        var objective = new RegenObjective(
            conditions:        cond,
            baseline:          baseline,
            profile:           RegenChamberOptimization.Profiles[0],
            skipVoxelGeometry: true,
            skipMfgAnalysis:   true);

        // Use the same Bounds shape that TryStartOpt passes to SA.
        var sa = new SimulatedAnnealingOptimizer(
            RegenChamberOptimization.Bounds,
            maxIterations: 10,
            seed: 42);

        double prevBest = double.PositiveInfinity;
        for (int iter = 0; iter < 10; iter++)
        {
            var vec    = sa.NextCandidate();
            var result = objective.Evaluate(vec);
            sa.ReportScore(vec, result.Score, result.EngineSpecificBreakdown);

            // BestScore must never increase.
            Assert.True(sa.BestScore <= prevBest,
                $"SA BestScore regressed at iter {iter}: {sa.BestScore} > {prevBest}");
            prevBest = sa.BestScore;
        }
    }

    // ── b. Default design produces a non-NaN score ────────────────────────

    [Fact]
    public void RegenObjective_DefaultDesign_NonNanScore()
    {
        var objective = new RegenObjective(
            conditions:        DefaultConditions(),
            baseline:          DefaultDesign(),
            profile:           RegenChamberOptimization.Profiles[0],
            skipVoxelGeometry: true,
            skipMfgAnalysis:   true);

        var vec    = RegenChamberOptimization.Pack(DefaultDesign());
        var result = objective.Evaluate(vec);

        Assert.False(double.IsNaN(result.Score), $"Score was NaN");
    }

    // ── c. EngineSpecificBreakdown is RegenScoreResult when physics ran ───

    [Fact]
    public void RegenObjective_EngineSpecificBreakdown_IsRegenScoreResult_WhenPhysicsRan()
    {
        var objective = new RegenObjective(
            conditions:        DefaultConditions(),
            baseline:          DefaultDesign(),
            profile:           RegenChamberOptimization.Profiles[0],
            skipVoxelGeometry: true,
            skipMfgAnalysis:   true);

        var vec    = RegenChamberOptimization.Pack(DefaultDesign());
        var result = objective.Evaluate(vec);

        // When the pre-screen does NOT short-circuit, EngineSpecificBreakdown
        // must be a RegenScoreResult. Program.Sa.cs's parallel batch loop casts
        // it with `as RegenScoreResult` — this test pins that the cast succeeds.
        if (result.EngineSpecificBreakdown is not null)
        {
            Assert.IsType<RegenScoreResult>(result.EngineSpecificBreakdown);
        }
    }

    // ── d. Pre-screen path returns +∞ with null breakdown ─────────────────

    [Fact]
    public void RegenObjective_PreScreenInfeasible_InfiniteScoreNullBreakdown()
    {
        // For LOX/CH4, L* nominal = 1.10 m.  LStarFloorFraction = 0.95,
        // so the pre-screen fires when CharacteristicLength_m < 1.045 m.
        // SA bounds are [0.7, 1.6] — 0.8 m is within SA bounds but below the
        // pre-screen floor, so Unpack(Pack(design)) preserves the value and
        // RegenObjective.Evaluate's pre-screen short-circuit fires.
        var cond = DefaultConditions();   // LOX/CH4
        var objective = new RegenObjective(
            conditions:        cond,
            baseline:          DefaultDesign(),
            profile:           RegenChamberOptimization.Profiles[0],
            skipVoxelGeometry: true,
            skipMfgAnalysis:   true);

        var triggerDesign = DefaultDesign() with { CharacteristicLength_m = 0.8 };
        var vec           = RegenChamberOptimization.Pack(triggerDesign);

        var result = objective.Evaluate(vec);

        Assert.Equal(double.PositiveInfinity, result.Score);
        // Pre-screen short-circuit: no physics ran, so EngineSpecificBreakdown is null.
        // Program.Sa.cs's parallel batch loop falls through to MakeInfeasibleScore.
        Assert.Null(result.EngineSpecificBreakdown);
        Assert.NotEmpty(result.Violations);
    }
}
