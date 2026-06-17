// DisplacementHullObjectiveTests.cs — unit tests for DisplacementHullObjective.
//
// Verifies:
//   (a) Pack / Unpack round-trip is bit-identical for feasible designs.
//   (b) Infeasible candidate (hard gate violation) → +∞ score.
//   (c) Feasible candidate → finite positive score (DragForce_N).
//   (d) MaterialIndex categorical slot rounds to nearest integer.
//   (e) DimensionCount + Variables count match.

using System;
using System.Threading;
using Voxelforge.Marine;
using Voxelforge.Marine.Optimization;
using Xunit;
using static Voxelforge.Marine.Tests.ScaffoldingSmokeTests;

namespace Voxelforge.Marine.Tests;

public sealed class DisplacementHullObjectiveTests
{
    [Fact]
    public void DimensionCount_IsSevenAndVariablesMatch()
    {
        var obj = DisplacementHullObjective.WithDefaultBounds(MakeRemus100Conditions());
        Assert.Equal(7, obj.DimensionCount);
        Assert.Equal(7, obj.Variables.Count);
    }

    [Fact]
    public void Pack_Unpack_RoundTrip_IsIdentical()
    {
        var design = MakeRemus100Design();
        var vec    = DisplacementHullObjective.Pack(design);
        var round  = DisplacementHullObjective.Unpack(vec);

        Assert.Equal(design.Length_m,            round.Length_m,            precision: 15);
        Assert.Equal(design.Diameter_m,          round.Diameter_m,          precision: 15);
        Assert.Equal(design.NoseFairingFraction, round.NoseFairingFraction, precision: 15);
        Assert.Equal(design.TailFairingFraction, round.TailFairingFraction, precision: 15);
        Assert.Equal(design.WallThickness_m,     round.WallThickness_m,     precision: 15);
        Assert.Equal(design.MaterialIndex,       round.MaterialIndex);
        Assert.Equal(design.DepthRating_m,       round.DepthRating_m,       precision: 15);
    }

    [Fact]
    public void FeasibleCandidate_ReturnsFiniteScore()
    {
        var obj = DisplacementHullObjective.WithDefaultBounds(MakeRemus100Conditions());
        var vec = DisplacementHullObjective.Pack(MakeRemus100Design());
        var er  = obj.Evaluate(vec.AsSpan(), CancellationToken.None);
        Assert.True(double.IsFinite(er.Score),
            $"Expected finite score, got {er.Score}");
        Assert.True(er.Score > 0, "Score (drag) should be positive");
    }

    [Fact]
    public void InfeasibleCandidate_ReturnsPlusInfinity()
    {
        // Very thin wall → HULL_WATERTIGHT_INTEGRITY + HULL_BUCKLING_INSUFFICIENT → +∞
        var obj    = DisplacementHullObjective.WithDefaultBounds(MakeRemus100Conditions());
        var design = MakeRemus100Design() with { WallThickness_m = 0.0001 }; // 0.1 mm
        var vec    = DisplacementHullObjective.Pack(design);
        var er     = obj.Evaluate(vec.AsSpan(), CancellationToken.None);
        Assert.Equal(double.PositiveInfinity, er.Score);
    }

    [Fact]
    public void MaterialIndex_CategoricalSlot_RoundsCorrectly()
    {
        // Slot value 1.4 should round to 1 (Al-6061); 1.6 should round to 2 (SS).
        var vec1 = new double[] { 1.595, 0.190, 0.18, 0.22, 0.004, 1.4, 100.0 };
        var vec2 = new double[] { 1.595, 0.190, 0.18, 0.22, 0.004, 1.6, 100.0 };
        var d1 = DisplacementHullObjective.Unpack(vec1.AsSpan());
        var d2 = DisplacementHullObjective.Unpack(vec2.AsSpan());
        Assert.Equal(1, d1.MaterialIndex);
        Assert.Equal(2, d2.MaterialIndex);
    }

    [Fact]
    public void WrongVectorLength_Throws()
    {
        var obj = DisplacementHullObjective.WithDefaultBounds(MakeRemus100Conditions());
        var shortVec = new double[] { 1.0, 0.2 }; // 2 elements, needs 7
        Assert.Throws<ArgumentException>(() => obj.Evaluate(shortVec.AsSpan()));
    }

    [Fact]
    public void ScoreMonotonicity_SmallVelocity_LowerDrag()
    {
        // Lower cruise speed → lower drag → lower score (objective is drag minimisation).
        var lowSpeed  = new MarineConditions(CruiseSpeed_ms: 1.0, MaxDepth_m: 100.0);
        var highSpeed = new MarineConditions(CruiseSpeed_ms: 2.0, MaxDepth_m: 100.0);
        var vec = DisplacementHullObjective.Pack(MakeRemus100Design());
        var scoreLow  = DisplacementHullObjective.WithDefaultBounds(lowSpeed)
                            .Evaluate(vec.AsSpan()).Score;
        var scoreHigh = DisplacementHullObjective.WithDefaultBounds(highSpeed)
                            .Evaluate(vec.AsSpan()).Score;
        Assert.True(scoreLow < scoreHigh,
            $"Lower speed should produce lower drag: {scoreLow:F4} vs {scoreHigh:F4}");
    }
}
