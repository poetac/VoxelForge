// AirbreathingFeasibilityLpbfTests — LPBF gate evaluator tests for
// the air-breathing pillar. Drives synthetic LpbfPrintabilityResult
// instances through AirbreathingFeasibility.EvaluateLpbfGates and
// asserts the right ConstraintIds fire (or stay silent).

using System.Collections.Generic;
using System.Numerics;
using Voxelforge.Geometry.LpbfAnalysis;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Airbreathing.Tests;

public class AirbreathingFeasibilityLpbfTests
{
    [Fact]
    public void EmptyPrintability_ProducesNoViolations()
    {
        var clean = MakeResult(
            overhangViolations:    System.Array.Empty<OverhangViolation>(),
            trappedPocketCount:    0,
            drainPathViolations:   System.Array.Empty<DrainPathViolation>());
        var violations = new List<FeasibilityViolation>();

        AirbreathingFeasibility.EvaluateLpbfGates(clean, violations);

        Assert.Empty(violations);
    }

    [Fact]
    public void OverhangViolation_FiresOverhangAngleExceeded()
    {
        var bad = MakeResult(
            overhangViolations: new[]
            {
                new OverhangViolation(
                    Point:             new Vector3(0, 0, 0),
                    Normal:            new Vector3(0, 0, -1),
                    OverhangAngle_deg: 30.0,
                    Area_mm2:          25.0),
            },
            trappedPocketCount:  0,
            drainPathViolations: System.Array.Empty<DrainPathViolation>());
        var violations = new List<FeasibilityViolation>();

        AirbreathingFeasibility.EvaluateLpbfGates(bad, violations);

        var v = Assert.Single(violations);
        Assert.Equal("OVERHANG_ANGLE_EXCEEDED", v.ConstraintId);
        Assert.Equal(30.0, v.ActualValue);
        Assert.Equal(LpbfMaterialProfiles.Inconel625.MinUnsupportedOverhangAngle_deg, v.Limit);
    }

    [Fact]
    public void TrappedPowder_FiresTrappedPowderRegion()
    {
        var bad = MakeResult(
            overhangViolations:  System.Array.Empty<OverhangViolation>(),
            trappedPocketCount:  1,
            drainPathViolations: System.Array.Empty<DrainPathViolation>());
        var violations = new List<FeasibilityViolation>();

        AirbreathingFeasibility.EvaluateLpbfGates(bad, violations);

        var v = Assert.Single(violations);
        Assert.Equal("TRAPPED_POWDER_REGION", v.ConstraintId);
    }

    [Fact]
    public void DrainPathViolation_FiresDrainPathMissing()
    {
        var bad = MakeResult(
            overhangViolations:  System.Array.Empty<OverhangViolation>(),
            trappedPocketCount:  0,
            drainPathViolations: new[]
            {
                new DrainPathViolation(
                    NodeId: "manifold_branch_2",
                    Label:  "Purge tap stub",
                    Reason: "dead-end"),
            });
        var violations = new List<FeasibilityViolation>();

        AirbreathingFeasibility.EvaluateLpbfGates(bad, violations);

        var v = Assert.Single(violations);
        Assert.Equal("DRAIN_PATH_MISSING", v.ConstraintId);
        Assert.Equal(1.0, v.ActualValue);
    }

    [Fact]
    public void MultipleViolations_Accumulate()
    {
        var bad = MakeResult(
            overhangViolations: new[]
            {
                new OverhangViolation(
                    new Vector3(0, 0, 0),
                    new Vector3(0, 0, -1),
                    OverhangAngle_deg: 25.0,
                    Area_mm2:          25.0),
            },
            trappedPocketCount: 1,
            drainPathViolations: new[]
            {
                new DrainPathViolation("n", "label", "dead-end"),
            });
        var violations = new List<FeasibilityViolation>();

        AirbreathingFeasibility.EvaluateLpbfGates(bad, violations);

        Assert.Equal(3, violations.Count);
        Assert.Contains(violations, v => v.ConstraintId == "OVERHANG_ANGLE_EXCEEDED");
        Assert.Contains(violations, v => v.ConstraintId == "TRAPPED_POWDER_REGION");
        Assert.Contains(violations, v => v.ConstraintId == "DRAIN_PATH_MISSING");
    }

    [Fact]
    public void AppendsToExistingList_WithoutClearing()
    {
        var existing = new FeasibilityViolation("PRE_EXISTING", "pre", 0, 0);
        var violations = new List<FeasibilityViolation> { existing };

        var bad = MakeResult(
            overhangViolations: new[]
            {
                new OverhangViolation(new Vector3(), new Vector3(0, 0, -1), 30, 25),
            },
            trappedPocketCount:  0,
            drainPathViolations: System.Array.Empty<DrainPathViolation>());

        AirbreathingFeasibility.EvaluateLpbfGates(bad, violations);

        Assert.Equal(2, violations.Count);
        Assert.Same(existing, violations[0]);
    }

    private static LpbfPrintabilityResult MakeResult(
        OverhangViolation[]    overhangViolations,
        int                    trappedPocketCount,
        DrainPathViolation[]   drainPathViolations)
    {
        var mat = LpbfMaterialProfiles.Inconel625;
        var overhang = new OverhangReport(
            Material:               mat,
            BuildAxis:              Vector3.UnitX,
            ViolationCount:         overhangViolations.Length,
            WorstOverhangAngle_deg: overhangViolations.Length > 0 ? overhangViolations[0].OverhangAngle_deg : double.NaN,
            TotalOverhangArea_mm2:  overhangViolations.Length > 0 ? overhangViolations[0].Area_mm2 : 0.0,
            Violations:             overhangViolations);

        TrappedPowderReport? trapped = trappedPocketCount > 0
            ? new TrappedPowderReport(
                PocketCount:            trappedPocketCount,
                TotalTrappedVolume_mm3: 12.5,
                Pockets:                System.Array.Empty<TrappedPowderPocket>())
            : null;

        var drain = new DrainPathReport(
            ViolationCount: drainPathViolations.Length,
            Violations:     drainPathViolations);

        return new LpbfPrintabilityResult(
            Material:      mat,
            Overhang:      overhang,
            TrappedPowder: trapped,
            DrainPath:     drain,
            Orientation:   null);
    }
}
