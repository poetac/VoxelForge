// MarineGateTests.cs — unit tests for MarineGates (Sprint M.2).
//
// Each gate must have at least one test that fires the violation and one
// that does not, per ADR-026 §Gate census requirement.

using System.Linq;
using Voxelforge.Marine;
using Voxelforge.Marine.Hydrodynamics;
using Voxelforge.Marine.Optimization;
using Voxelforge.Marine.Structure;
using Xunit;
using static Voxelforge.Marine.Tests.ScaffoldingSmokeTests;

namespace Voxelforge.Marine.Tests;

public sealed class MarineGateTests
{
    // ── Full integration gate evaluation ─────────────────────────────────────

    [Fact]
    public void REMUS100_Seed_IsFeasible()
    {
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.True(result.IsFeasible,
            $"Expected feasible but got violations: {string.Join(", ", result.Violations.Select(v => v.ConstraintId))}");
    }

    // ── HULL_BUOYANCY_NEGATIVE ────────────────────────────────────────────────

    [Fact]
    public void HullBuoyancyNegative_Fires_WhenHullSinks()
    {
        // Very thick stainless wall → hull heavier than displaced water.
        var design = MakeRemus100Design() with
        {
            MaterialIndex   = 2,     // AISI-316L, 7950 kg/m³
            WallThickness_m = 0.020, // 20 mm — much heavier than displaced water
        };
        var result = MarineOptimization.GenerateWith(design, MakeRemus100Conditions());
        Assert.Contains(result.Violations,
            v => v.ConstraintId == MarineConstraintIds.HullBuoyancyNegative);
    }

    [Fact]
    public void HullBuoyancyNegative_Clear_WhenHullFloats()
    {
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == MarineConstraintIds.HullBuoyancyNegative);
    }

    // ── HULL_BUCKLING_INSUFFICIENT ───────────────────────────────────────────

    [Fact]
    public void HullBucklingInsufficient_Fires_WhenSfBelow1p5()
    {
        // Very thin wall → SF << 1.5.
        var design = MakeRemus100Design() with { WallThickness_m = 0.0005 };
        var result = MarineOptimization.GenerateWith(design, MakeRemus100Conditions());
        Assert.Contains(result.Violations,
            v => v.ConstraintId == MarineConstraintIds.HullBucklingInsufficient);
    }

    [Fact]
    public void HullBucklingInsufficient_Clear_WhenSfAbove1p5()
    {
        // Thick Ti wall → large SF.
        var design = MakeRemus100Design() with
        {
            MaterialIndex   = 0,     // Ti-6Al-4V (stiffest)
            WallThickness_m = 0.010, // 10 mm
        };
        var result = MarineOptimization.GenerateWith(design, MakeRemus100Conditions());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == MarineConstraintIds.HullBucklingInsufficient);
    }

    // ── HULL_WATERTIGHT_INTEGRITY ─────────────────────────────────────────────

    [Fact]
    public void HullWatertightIntegrity_Fires_WhenWallBelowHardFloor()
    {
        // 1.0 mm < 1.5 mm hard floor.
        var design = MakeRemus100Design() with { WallThickness_m = 0.001 };
        var result = MarineOptimization.GenerateWith(design, MakeRemus100Conditions());
        Assert.Contains(result.Violations,
            v => v.ConstraintId == MarineConstraintIds.HullWatertightIntegrity);
    }

    [Fact]
    public void HullWatertightIntegrity_Clear_WhenWallAboveHardFloor()
    {
        // 4 mm > 1.5 mm.
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == MarineConstraintIds.HullWatertightIntegrity);
    }

    // ── DEPTH_RATING_EXCEEDED ─────────────────────────────────────────────────

    [Fact]
    public void DepthRatingExceeded_Fires_WhenMaxDepthExceedsRating()
    {
        var design = MakeRemus100Design() with { DepthRating_m = 50.0 }; // rated only 50 m
        var cond   = new MarineConditions(CruiseSpeed_ms: 1.5, MaxDepth_m: 100.0); // operating at 100 m
        var result = MarineOptimization.GenerateWith(design, cond);
        Assert.Contains(result.Violations,
            v => v.ConstraintId == MarineConstraintIds.DepthRatingExceeded);
    }

    [Fact]
    public void DepthRatingExceeded_Clear_WhenMaxDepthWithinRating()
    {
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == MarineConstraintIds.DepthRatingExceeded);
    }

    // ── HULL_FINENESS_EXTREME (hard gate) ─────────────────────────────────────

    [Fact]
    public void FinenessTooExtremeHard_Fires_WhenLOverDBelow4()
    {
        // L/D = 1.0/0.5 = 2.0 < 4.0 hard min.
        var design = MakeRemus100Design() with
        {
            Length_m   = 1.0,
            Diameter_m = 0.5,  // L/D = 2.0
        };
        var result = MarineOptimization.GenerateWith(design, MakeRemus100Conditions());
        Assert.Contains(result.Violations,
            v => v.ConstraintId == MarineConstraintIds.FinenessTooExtremeHard);
    }

    [Fact]
    public void FinenessTooExtremeHard_Clear_WhenFinenessInBand()
    {
        // REMUS-100 L/D ≈ 8.4 — solidly in [4, 15].
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == MarineConstraintIds.FinenessTooExtremeHard);
    }

    // ── HULL_LPBF_WALL_TOO_THIN (advisory) ───────────────────────────────────

    [Fact]
    public void LpbfHullWallTooThin_Fires_WhenWallBetween1p5AndTwoMm()
    {
        // 1.8 mm is above the hard 1.5 mm floor but below the advisory 2.0 mm.
        var design = MakeRemus100Design() with { WallThickness_m = 0.0018 };
        var result = MarineOptimization.GenerateWith(design, MakeRemus100Conditions());
        Assert.Contains(result.Advisories,
            v => v.ConstraintId == MarineConstraintIds.LpbfHullWallTooThin);
    }

    [Fact]
    public void LpbfHullWallTooThin_Clear_WhenWallAtOrAboveTwoMm()
    {
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.DoesNotContain(result.Advisories,
            v => v.ConstraintId == MarineConstraintIds.LpbfHullWallTooThin);
    }

    // ── HULL_FINENESS_OUT_OF_BAND (advisory) ──────────────────────────────────

    [Fact]
    public void FinenesRatioOutOfBand_Fires_WhenLOverDBelow5()
    {
        // L/D = 4.5 — passes hard gate [4,15] but below advisory min of 5.
        var design = MakeRemus100Design() with
        {
            Length_m   = 4.5 * 0.190,  // = 0.855 m
            Diameter_m = 0.190,
        };
        var result = MarineOptimization.GenerateWith(design, MakeRemus100Conditions());
        Assert.Contains(result.Advisories,
            v => v.ConstraintId == MarineConstraintIds.FinenesRatioOutOfBand);
    }

    [Fact]
    public void FinenesRatioOutOfBand_Clear_WhenFinenessInOptimumBand()
    {
        // REMUS-100 L/D ≈ 8.4 is in [5, 12].
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.DoesNotContain(result.Advisories,
            v => v.ConstraintId == MarineConstraintIds.FinenesRatioOutOfBand);
    }
}
