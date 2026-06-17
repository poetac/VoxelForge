// RegenObjectiveTests.cs — Slice 3 of the IObjective decoupling
// (2026-04-28). Pins the score-parity contract between the
// new IObjective-shaped wrapper (RegenObjective) and the legacy
// (RegenChamberOptimization.Unpack → GenerateWith → Evaluate) path
// it wraps. Score-parity is the bench-fingerprint-equivalence
// invariant: production wiring migrating to RegenObjective must not
// shift any SA trajectory.

using System;
using Voxelforge.Combustion;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class RegenObjectiveTests
{
    // Shared baseline for score-parity tests. Same shape as
    // A1FollowOnGateFixTests.BaseResult — small LOX/CH4 design that's
    // partially gate-clean by default. Uses xUnit-safe physics-only path
    // (skipVoxelGeometry = true), so no PicoGK Library is required.
    private static readonly OperatingConditions BaselineConditions = new()
    {
        Thrust_N                = 2224.0,
        ChamberPressure_Pa      = 6.9e6,
        MixtureRatio            = 3.3,
        CoolantInletTemp_K      = 150.0,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex       = 0,   // GRCop-42
        PropellantPair          = PropellantPair.LOX_CH4,
    };

    private static readonly RegenChamberDesign BaselineDesign = new()
    {
        IncludeManifolds      = false,
        IncludePorts          = false,
        IncludeInjectorFlange = false,
        ContourStationCount   = 60,
    };

    [Fact]
    public void Constructor_DerivesVariables_From_Registry()
    {
        // #551: RegenObjective + ScoreDesign now take explicit profile; default Profiles[0] preserves prior static-state behavior.
        var obj = new RegenObjective(BaselineConditions, BaselineDesign, RegenChamberOptimization.Profiles[0]);
        // Number of dims must match the registry-derived bounds the SA
        // optimizer uses today. RegenChamberOptimization.Bounds is the
        // canonical surface for that count.
        Assert.Equal(RegenChamberOptimization.Bounds.Length, obj.DimensionCount);
        Assert.Equal(RegenChamberOptimization.Bounds.Length, obj.Variables.Count);

        // Bounds should match dim-by-dim with the registry's bounds.
        var bounds = DesignVariableInfo.ToBoundsArray(obj.Variables);
        Assert.Equal(RegenChamberOptimization.Bounds.Length, bounds.Length);
        for (int i = 0; i < bounds.Length; i++)
        {
            Assert.Equal(RegenChamberOptimization.Bounds[i].Min, bounds[i].Min);
            Assert.Equal(RegenChamberOptimization.Bounds[i].Max, bounds[i].Max);
        }
    }

    [Fact]
    public void Constructor_NullArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new RegenObjective(null!, BaselineDesign, RegenChamberOptimization.Profiles[0]));
        Assert.Throws<ArgumentNullException>(
            () => new RegenObjective(BaselineConditions, null!, RegenChamberOptimization.Profiles[0]));
    }

    [Fact]
    public void Evaluate_RejectsWrongVectorLength()
    {
        var obj = new RegenObjective(BaselineConditions, BaselineDesign, RegenChamberOptimization.Profiles[0]);
        Assert.Throws<ArgumentException>(() =>
        {
            // One short.
            ReadOnlySpan<double> v = new double[obj.DimensionCount - 1];
            obj.Evaluate(v);
        });
    }

    [Fact]
    public void Evaluate_AtPackedBaseline_MatchesLegacyPathExactly()
    {
        // Score-parity: RegenObjective.Evaluate(packed) must produce
        // the same result as the round-trip-equivalent legacy path:
        //   Unpack(packed, baseline) → GenerateWith → Evaluate.
        //
        // Note Pack/Unpack are not literally round-trip-identity for
        // baselines that have raw record defaults (e.g. ChamberWall-
        // ThicknessOverride_mm = 0.0): Unpack clamps each value to its
        // SA bounds, which can shift override-style dims from 0.0 up
        // to the SA min. So the right comparison is RegenObjective vs
        // the same Unpack-clamped path that SA's Pack→Unpack
        // produces — that's the bench-fingerprint-equivalence
        // invariant for production SA wiring migrating to RegenObjective.
        var packed = RegenChamberOptimization.Pack(BaselineDesign);

        // Reference (Unpack-aware legacy) path.
        var roundtripDesign = RegenChamberOptimization.Unpack(packed, BaselineDesign);
        var legacyGen = RegenChamberOptimization.GenerateWith(
            BaselineConditions, roundtripDesign,
            voxelSize_mm: 0.0,
            skipVoxelGeometry: true,
            skipMfgAnalysis: true);
        // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
        var legacyScore = RegenChamberOptimization.Evaluate(legacyGen, RegenChamberOptimization.Profiles[0]);

        // Through RegenObjective.
        var obj = new RegenObjective(BaselineConditions, BaselineDesign, RegenChamberOptimization.Profiles[0]);
        var result = obj.Evaluate(packed);

        Assert.Equal(legacyScore.TotalScore, result.Score);
        Assert.Equal(legacyScore.FeasibilityViolations.Length, result.Violations.Count);
        // EngineSpecificBreakdown carries the RegenScoreResult forward.
        var carriedScore = Assert.IsType<RegenScoreResult>(result.EngineSpecificBreakdown);
        Assert.Equal(legacyScore.TotalScore, carriedScore.TotalScore);
        Assert.Equal(legacyScore.PeakWallT_K, carriedScore.PeakWallT_K);
    }

    [Fact]
    public void Evaluate_AtPerturbedVector_MatchesLegacyPathExactly()
    {
        // Same parity invariant, this time at a non-baseline vector.
        // Catches drift in the Unpack adapter — if RegenObjective's
        // Evaluate ever diverges from
        // (Unpack → GenerateWith → Evaluate) it's a bug.
        var packed = RegenChamberOptimization.Pack(BaselineDesign);
        var perturbed = (double[])packed.Clone();
        // Nudge dim 0 toward its midpoint.
        var (lo, hi) = RegenChamberOptimization.Bounds[0];
        perturbed[0] = 0.5 * (lo + hi);

        var perturbedDesign = RegenChamberOptimization.Unpack(perturbed, BaselineDesign);
        var legacyGen = RegenChamberOptimization.GenerateWith(
            BaselineConditions, perturbedDesign,
            voxelSize_mm: 0.0,
            skipVoxelGeometry: true,
            skipMfgAnalysis: true);
        var legacyScore = RegenChamberOptimization.Evaluate(legacyGen, RegenChamberOptimization.Profiles[0]);

        var obj = new RegenObjective(BaselineConditions, BaselineDesign, RegenChamberOptimization.Profiles[0]);
        var result = obj.Evaluate(perturbed);

        Assert.Equal(legacyScore.TotalScore, result.Score);
    }

    [Fact]
    public void Evaluate_PreservesViolationsList_OnInfeasibleCandidate()
    {
        // Force infeasibility by perturbing the WallMaterialIndex via a
        // baseline that should produce a wall-T or yield gate failure
        // at the seed. The cleanest forced-infeasible is to start with
        // an extreme thrust value off the bounds-sane band.
        var hardCondition = BaselineConditions with
        {
            // 50× nominal thrust — virtually guaranteed to trip
            // structural / wall-T gates. The exact gate doesn't matter
            // for the test; we only need at least one violation.
            Thrust_N = 100_000.0,
        };

        var obj = new RegenObjective(hardCondition, BaselineDesign, RegenChamberOptimization.Profiles[0]);
        var packed = RegenChamberOptimization.Pack(BaselineDesign);
        var result = obj.Evaluate(packed);

        // Either the score is +Infinity (gate fired) or the result has
        // violations populated. Both indicate the violations surface
        // round-trips correctly. We don't assert which gate specifically
        // because the cascade of failures depends on which one fires
        // first in the gate pipeline.
        Assert.True(
            double.IsPositiveInfinity(result.Score) || result.Violations.Count > 0,
            "expected an infeasible candidate to surface violations or +Inf score");
    }

    [Fact]
    public void Evaluate_BaselineProperties_AreImmutableAcrossCalls()
    {
        // Construct once + evaluate twice; the baseline reference must
        // not be mutated. Stability invariant — multi-chain SA shares
        // one IObjective across N concurrent threads, any baseline
        // mutation would race.
        var obj = new RegenObjective(BaselineConditions, BaselineDesign, RegenChamberOptimization.Profiles[0]);
        var origBaseline = obj.Baseline;

        var packed = RegenChamberOptimization.Pack(BaselineDesign);
        obj.Evaluate(packed);
        obj.Evaluate(packed);

        // Reference equality preserved; original record not swapped.
        Assert.Same(origBaseline, obj.Baseline);
        // Equality preserved (records are immutable).
        Assert.Equal(BaselineDesign, obj.Baseline);
    }

    [Fact]
    public void ScoreDesign_MatchesLegacyDirectPath_BitIdentically()
    {
        // ScoreDesign is the no-Pack/Unpack helper voxelforge-eval uses
        // to preserve "evaluate this design exactly as given" semantics
        // while still routing through the IObjective abstraction's
        // EvaluationResult shape. Must be bit-identical to the legacy
        // (GenerateWith → Evaluate) path.
        var legacyGen = RegenChamberOptimization.GenerateWith(
            BaselineConditions, BaselineDesign,
            voxelSize_mm: 0.0,
            skipVoxelGeometry: true,
            skipMfgAnalysis: true);
        var legacyScore = RegenChamberOptimization.Evaluate(legacyGen, RegenChamberOptimization.Profiles[0]);

        var result = RegenObjective.ScoreDesign(BaselineConditions, BaselineDesign, RegenChamberOptimization.Profiles[0]);

        Assert.Equal(legacyScore.TotalScore, result.Score);
        Assert.Equal(legacyScore.FeasibilityViolations.Length, result.Violations.Count);
        var carried = Assert.IsType<RegenScoreResult>(result.EngineSpecificBreakdown);
        Assert.Equal(legacyScore.TotalScore, carried.TotalScore);
        Assert.Equal(legacyScore.PeakWallT_K, carried.PeakWallT_K);
        Assert.Equal(legacyScore.CoolantDP_Pa, carried.CoolantDP_Pa);
    }

    [Fact]
    public void ScoreDesign_NullArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => RegenObjective.ScoreDesign(null!, BaselineDesign, RegenChamberOptimization.Profiles[0]));
        Assert.Throws<ArgumentNullException>(
            () => RegenObjective.ScoreDesign(BaselineConditions, null!, RegenChamberOptimization.Profiles[0]));
    }

    [Fact]
    public void EndToEnd_MultiChainOptimizer_ConsumesRegenObjective()
    {
        // Integration check: MultiChainOptimizer + RegenObjective compose
        // correctly. Tiny iteration budget (10 iters × 2 chains) keeps
        // the test fast — we only need to confirm the wiring runs
        // without throwing and produces a finite-or-Infinity result with
        // an EvaluationResult breakdown.
        var obj = new RegenObjective(BaselineConditions, BaselineDesign, RegenChamberOptimization.Profiles[0]);
        var opt = new MultiChainOptimizer(
            obj,
            maxIterations: 10,
            baseSeed: 1,
            chainCount: 2,
            useSobolSeeding: false);  // skip Sobol warmup for short budget

        var initial = RegenChamberOptimization.Pack(BaselineDesign);
        var result = opt.Run(obj, initialCandidate: initial);

        Assert.Equal(2, result.ChainCount);
        Assert.Equal(obj.DimensionCount, result.BestParams.Length);
        // Best breakdown must be an EvaluationResult (per the IObjective
        // bridge contract — Slice 2 invariant) carrying a RegenScoreResult.
        var eval = Assert.IsType<EvaluationResult>(result.BestBreakdown);
        Assert.IsType<RegenScoreResult>(eval.EngineSpecificBreakdown);
    }

    [Fact]
    public void Evaluate_DifferentVectors_ProduceDistinctBreakdowns()
    {
        // Sanity check that the wrapper isn't accidentally hashing all
        // candidates to the same EvaluationResult. We compare the
        // EngineSpecificBreakdown's PeakWallT_K rather than Score
        // because both candidates may be infeasible (Score = +Inf) on
        // a small minimal baseline — but even infeasible candidates
        // produce distinct PeakWallT_K when the design changes.
        var obj = new RegenObjective(BaselineConditions, BaselineDesign, RegenChamberOptimization.Profiles[0]);
        var packed = RegenChamberOptimization.Pack(BaselineDesign);
        var perturbed = (double[])packed.Clone();
        // Move dim 0 (ContractionRatio) far from the baseline within bounds.
        var (lo, hi) = RegenChamberOptimization.Bounds[0];
        perturbed[0] = lo + 0.9 * (hi - lo);

        var r1 = obj.Evaluate(packed);
        var r2 = obj.Evaluate(perturbed);

        var s1 = Assert.IsType<RegenScoreResult>(r1.EngineSpecificBreakdown);
        var s2 = Assert.IsType<RegenScoreResult>(r2.EngineSpecificBreakdown);
        Assert.NotEqual(s1.PeakWallT_K, s2.PeakWallT_K);
    }
}
