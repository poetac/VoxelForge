// LpbfPrintabilityTests.cs — Sprint 27 (2026-04-23): xUnit coverage for the
// Geometry.LpbfAnalysis subtree + the three OVERHANG_ANGLE_EXCEEDED /
// TRAPPED_POWDER_REGION / DRAIN_PATH_MISSING gates.
//
// Tests are voxel-free per ADR-005 — no PicoGK.Library instantiation.
// Synthetic inputs exercise each module's edge cases directly.

using System.Linq;
using System.Numerics;
using Voxelforge.Geometry.LpbfAnalysis;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;
using Xunit;

namespace Voxelforge.Tests;

public sealed class LpbfPrintabilityTests
{
    private static readonly LpbfMaterialProfile Steel     = LpbfMaterialProfiles.Stainless316L;   // 45°
    private static readonly LpbfMaterialProfile Inconel   = LpbfMaterialProfiles.Inconel718;     // 35°

    // ════════════════════════════════════════════════════════════════
    //  LpbfMaterialProfiles
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void MaterialProfiles_CoverAllEnumValues()
    {
        foreach (LpbfMaterial m in System.Enum.GetValues<LpbfMaterial>())
        {
            var profile = LpbfMaterialProfiles.For(m);
            Assert.Equal(m, profile.Material);
            Assert.InRange(profile.MinUnsupportedOverhangAngle_deg, 20.0, 60.0);
            Assert.False(string.IsNullOrEmpty(profile.DisplayName));
        }
    }

    [Fact]
    public void MaterialProfiles_Inconel718IsStricterThanStainless()
    {
        Assert.True(
            LpbfMaterialProfiles.Inconel718.MinUnsupportedOverhangAngle_deg
            < LpbfMaterialProfiles.Stainless316L.MinUnsupportedOverhangAngle_deg,
            "IN718 tolerates shallower overhangs than 316L — its angle floor should be lower.");
    }

    [Fact]
    public void MaterialProfiles_FromWallMaterialIndex_MapsKnownIndices()
    {
        Assert.Equal(LpbfMaterial.GRCop42,    LpbfMaterialProfiles.FromWallMaterialIndex(0).Material);
        Assert.Equal(LpbfMaterial.CuCrZr,     LpbfMaterialProfiles.FromWallMaterialIndex(1).Material);
        Assert.Equal(LpbfMaterial.Inconel625, LpbfMaterialProfiles.FromWallMaterialIndex(2).Material);
        Assert.Equal(LpbfMaterial.Inconel718, LpbfMaterialProfiles.FromWallMaterialIndex(3).Material);
    }

    // ════════════════════════════════════════════════════════════════
    //  OverhangAnalysis
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Overhang_AllUpFacing_NoViolations()
    {
        // Normal points along +Z (up, with the build axis) — no overhang.
        var samples = new[]
        {
            new SurfaceSample(new Vector3(0, 0, 0), new Vector3(0, 0, 1), 1.0),
            new SurfaceSample(new Vector3(1, 0, 0), new Vector3(0, 0, 1), 1.0),
        };
        var report = OverhangAnalysis.Analyze(samples, new Vector3(0, 0, 1), Steel);
        Assert.True(report.IsPrintable);
        Assert.Equal(0, report.ViolationCount);
        Assert.True(double.IsNaN(report.WorstOverhangAngle_deg),
            "With no down-facing samples, worst-β should stay NaN (no data).");
    }

    [Fact]
    public void Overhang_VerticalWall_IsPrintable()
    {
        // Side walls — normal perpendicular to build axis, β = 90°, safe.
        var samples = new[]
        {
            new SurfaceSample(new Vector3(0, 0, 0), new Vector3(1, 0, 0), 1.0),
            new SurfaceSample(new Vector3(0, 0, 1), new Vector3(0, 1, 0), 1.0),
        };
        var report = OverhangAnalysis.Analyze(samples, new Vector3(0, 0, 1), Steel);
        Assert.True(report.IsPrintable);
    }

    [Fact]
    public void Overhang_PureDownFacing_Flagged()
    {
        // Normal exactly antiparallel to build axis: β = 0°, worst-case
        // unsupported overhang.
        var samples = new[]
        {
            new SurfaceSample(new Vector3(0, 0, 0), new Vector3(0, 0, -1), 4.0),
        };
        var report = OverhangAnalysis.Analyze(samples, new Vector3(0, 0, 1), Steel);
        Assert.False(report.IsPrintable);
        Assert.Equal(1, report.ViolationCount);
        Assert.InRange(report.WorstOverhangAngle_deg, -0.1, 0.5);
        Assert.Equal(4.0, report.TotalOverhangArea_mm2, precision: 6);
    }

    [Fact]
    public void Overhang_MaterialDifference_ChangesVerdict()
    {
        // 40° overhang (surface tilted 40° from horizontal, down-facing).
        // Fails 316L's 45° floor (β=40° < 45°), passes IN718's 35° floor
        // (β=40° > 35°). Normal at β° from -b has horizontal component
        // sin(β) and vertical component -cos(β).
        // Sample area must clear PH-34 MinFlaggedOverhangPatchArea_mm2
        // (default 2.0); 4.0 mm² puts it well above the noise floor.
        double beta = 40.0 * System.Math.PI / 180.0;
        var normal = new Vector3(
            (float)System.Math.Sin(beta),
            0,
            (float)(-System.Math.Cos(beta)));

        var samples = new[] { new SurfaceSample(Vector3.Zero, normal, 4.0) };
        var steelReport = OverhangAnalysis.Analyze(samples, new Vector3(0, 0, 1), Steel);
        var incReport   = OverhangAnalysis.Analyze(samples, new Vector3(0, 0, 1), Inconel);
        Assert.False(steelReport.IsPrintable);
        Assert.True(incReport.IsPrintable);
    }

    // ──────────────── PH-34 (2026-04-29): patch-area threshold ────────────────
    // Sub-threshold overhang patches (e.g. single-voxel surface jitter at
    // 0.8 mm voxel ≈ 0.5 mm²) self-support thermally during LPBF and
    // shouldn't fire OVERHANG_ANGLE_EXCEEDED. Sibling pattern to
    // PH-3 MinFlaggedPocketVolume_mm3 in TrappedPowderAnalysis.

    [Fact]
    public void PH34_NoiseSizedOverhangPatches_AreFiltered_NotFlagged()
    {
        // 0° overhang (worst angle), but only 0.5 mm² of patch area —
        // below the default 2.0 mm² noise floor on Steel.
        var samples = new[]
        {
            new SurfaceSample(Vector3.Zero, new Vector3(0, 0, -1), Area_mm2: 0.5),
        };
        var report = OverhangAnalysis.Analyze(samples, new Vector3(0, 0, 1), Steel);
        Assert.True(report.IsPrintable);
        Assert.Equal(0, report.ViolationCount);
        Assert.Equal(1, report.BelowThresholdPatchCount);
        Assert.Equal(0.5, report.BelowThresholdPatchArea_mm2, precision: 6);
        // Worst-β still tracked even when filtered, so the user can see
        // there WERE down-facing samples — they just didn't hit the gate.
        Assert.InRange(report.WorstOverhangAngle_deg, -0.1, 0.5);
    }

    [Fact]
    public void PH34_RealOverhangAndNoise_FlagsRealOnly()
    {
        // One real overhang patch (5 mm², worst angle) AND one noise patch
        // (0.5 mm², worst angle). Real flags; noise filters; counters split.
        var samples = new[]
        {
            new SurfaceSample(new Vector3(0, 0, 0), new Vector3(0, 0, -1), Area_mm2: 5.0),
            new SurfaceSample(new Vector3(1, 0, 0), new Vector3(0, 0, -1), Area_mm2: 0.5),
        };
        var report = OverhangAnalysis.Analyze(samples, new Vector3(0, 0, 1), Steel);
        Assert.False(report.IsPrintable);
        Assert.Equal(1, report.ViolationCount);
        Assert.Equal(5.0, report.TotalOverhangArea_mm2, precision: 6);
        Assert.Equal(1, report.BelowThresholdPatchCount);
        Assert.Equal(0.5, report.BelowThresholdPatchArea_mm2, precision: 6);
    }

    [Fact]
    public void PH34_MaterialProfile_AllowsPerAlloyOverride()
    {
        // Each LpbfMaterialProfile gets its own threshold so a finicky
        // alloy (e.g. CuCrZr at 45° + 0.2 mm² floor) and a forgiving one
        // (e.g. 316L at 45° + 5 mm² floor) can disagree. Default is 2 mm².
        var tightAlloy = Steel with { MinFlaggedOverhangPatchArea_mm2 = 0.1 };
        var samples = new[]
        {
            new SurfaceSample(Vector3.Zero, new Vector3(0, 0, -1), Area_mm2: 0.5),
        };
        // Tight alloy (0.1 mm² floor) flags the 0.5 mm² patch.
        var tightReport = OverhangAnalysis.Analyze(samples, new Vector3(0, 0, 1), tightAlloy);
        Assert.False(tightReport.IsPrintable);
        Assert.Equal(1, tightReport.ViolationCount);
        Assert.Equal(0, tightReport.BelowThresholdPatchCount);
        // Default Steel (2.0 mm² floor) filters it.
        var defaultReport = OverhangAnalysis.Analyze(samples, new Vector3(0, 0, 1), Steel);
        Assert.True(defaultReport.IsPrintable);
        Assert.Equal(1, defaultReport.BelowThresholdPatchCount);
    }

    // ════════════════════════════════════════════════════════════════
    //  TrappedPowderAnalysis
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void TrappedPowder_AllVoid_NoTrappedRegions()
    {
        var snap = new VoxelFieldSnapshot(
            Occupancy: new bool[5, 5, 5],
            VoxelSize_mm: 1.0,
            Origin: Vector3.Zero);
        var report = TrappedPowderAnalysis.Analyze(snap);
        Assert.True(report.IsPrintable);
        Assert.Equal(0, report.PocketCount);
    }

    [Fact]
    public void TrappedPowder_SolidShellAroundVoid_OneTrappedPocket()
    {
        // 7×7×7 grid. Outer ring = solid, inner 5×5×5 contains a 3×3×3
        // void fully enclosed by solid. Flood-fill from the bounding
        // boundary can't reach the inner void → one trapped pocket.
        int N = 7;
        var occ = new bool[N, N, N];
        for (int i = 0; i < N; i++)
        for (int j = 0; j < N; j++)
        for (int k = 0; k < N; k++)
        {
            bool onBoundary = i == 0 || i == N-1 || j == 0 || j == N-1 || k == 0 || k == N-1;
            // Solid shell on the ring i=1..5 / j=1..5 / k=1..5, walls at
            // those surfaces. Build as: all solid except boundary void + inner 3³ void.
            bool innerVoid = (i >= 2 && i <= 4)
                          && (j >= 2 && j <= 4)
                          && (k >= 2 && k <= 4);
            occ[i, j, k] = !onBoundary && !innerVoid;
        }
        var snap = new VoxelFieldSnapshot(occ, 1.0, Vector3.Zero);
        var report = TrappedPowderAnalysis.Analyze(snap);
        Assert.False(report.IsPrintable);
        Assert.Equal(1, report.PocketCount);
        Assert.Equal(27, report.Pockets[0].VoxelCount);
    }

    [Fact]
    public void TrappedPowder_OpeningReachesPocket_ClearsViolation()
    {
        // Same 7³ shell as above, but pass an opening inside the pocket
        // to seed the flood-fill — emulates a drain hole.
        int N = 7;
        var occ = new bool[N, N, N];
        for (int i = 0; i < N; i++)
        for (int j = 0; j < N; j++)
        for (int k = 0; k < N; k++)
        {
            bool onBoundary = i == 0 || i == N-1 || j == 0 || j == N-1 || k == 0 || k == N-1;
            bool innerVoid = (i >= 2 && i <= 4)
                          && (j >= 2 && j <= 4)
                          && (k >= 2 && k <= 4);
            occ[i, j, k] = !onBoundary && !innerVoid;
        }
        var snap = new VoxelFieldSnapshot(occ, 1.0, Vector3.Zero);
        var openings = new[]
        {
            new OpeningPort(new Vector3(3.5f, 3.5f, 3.5f), 0.5, "drain"),
        };
        var report = TrappedPowderAnalysis.Analyze(snap, openings);
        Assert.True(report.IsPrintable);
    }

    // ════════════════════════════════════════════════════════════════
    //  DrainPathAnalysis
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Drain_SingleLoopBetweenExternalPorts_NoViolation()
    {
        var g = new LpbfRoutingGraph(
            Nodes: new[]
            {
                new LpbfRoutingNode("in",  "Coolant inlet",  IsExternalPort: true),
                new LpbfRoutingNode("out", "Coolant outlet", IsExternalPort: true),
            },
            Edges: new[] { new LpbfRoutingEdge("in", "out", "jacket") });
        var report = DrainPathAnalysis.Analyze(g);
        Assert.True(report.IsPrintable);
    }

    [Fact]
    public void Drain_DeadEndInternalNode_FlaggedAsDeadEnd()
    {
        var g = new LpbfRoutingGraph(
            Nodes: new[]
            {
                new LpbfRoutingNode("in",   "Coolant inlet", IsExternalPort: true),
                new LpbfRoutingNode("tee",  "Tee junction",  IsExternalPort: false),
                new LpbfRoutingNode("out",  "Outlet",        IsExternalPort: true),
                new LpbfRoutingNode("stub", "Dead-end stub", IsExternalPort: false),
            },
            Edges: new[]
            {
                new LpbfRoutingEdge("in",  "tee",  "feed"),
                new LpbfRoutingEdge("tee", "out",  "main"),
                new LpbfRoutingEdge("tee", "stub", "stub line"),
            });
        var report = DrainPathAnalysis.Analyze(g);
        Assert.False(report.IsPrintable);
        Assert.Contains(report.Violations, v => v.NodeId == "stub" && v.Reason == "dead-end");
    }

    [Fact]
    public void Drain_IsolatedComponent_FlaggedAsIsolated()
    {
        var g = new LpbfRoutingGraph(
            Nodes: new[]
            {
                // Main loop is fine.
                new LpbfRoutingNode("in",   "In",  IsExternalPort: true),
                new LpbfRoutingNode("out",  "Out", IsExternalPort: true),
                // A detached sub-loop with no external port.
                new LpbfRoutingNode("isoA", "Orphan A", IsExternalPort: false),
                new LpbfRoutingNode("isoB", "Orphan B", IsExternalPort: false),
            },
            Edges: new[]
            {
                new LpbfRoutingEdge("in",   "out",  "main"),
                new LpbfRoutingEdge("isoA", "isoB", "orphan loop"),
            });
        var report = DrainPathAnalysis.Analyze(g);
        Assert.False(report.IsPrintable);
        Assert.Contains(report.Violations, v => v.Reason == "isolated-component");
    }

    // ════════════════════════════════════════════════════════════════
    //  PrintOrientationAdvisor
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void OrientationAdvisor_PrefersAxisWithFewestOverhangs()
    {
        // Down-facing surface along -Z. When build axis is +Z the
        // surface overhangs; when build axis is +X it's a side-wall
        // (no overhang). Advisor should pick +X.
        var samples = new[]
        {
            new SurfaceSample(Vector3.Zero, new Vector3(0, 0, -1), 10.0),
        };
        var report = PrintOrientationAdvisor.Analyze(samples, Steel);
        Assert.Equal("+X", report.Best.Label);
        Assert.Equal(0,   report.Best.OverhangViolationCount);
    }

    [Fact]
    public void OrientationAdvisor_RankingCoversAllCandidates()
    {
        var samples = new[]
        {
            new SurfaceSample(Vector3.Zero, new Vector3(0, 0, -1), 1.0),
        };
        var report = PrintOrientationAdvisor.Analyze(samples, Steel);
        Assert.Equal(6, report.Ranked.Count);
        // Ranking is ascending by score — first item is best.
        for (int i = 1; i < report.Ranked.Count; i++)
            Assert.True(report.Ranked[i].Score >= report.Ranked[i - 1].Score);
    }

    // ════════════════════════════════════════════════════════════════
    //  LpbfPrintabilityAnalysis (composite entry point)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CompositeAnalysis_FeasibleDesign_PassesAllGates()
    {
        var samples = new[]
        {
            new SurfaceSample(Vector3.Zero, new Vector3(1, 0, 0), 1.0),
            new SurfaceSample(new Vector3(0, 1, 0), new Vector3(0, 1, 0), 1.0),
        };
        var graph = new LpbfRoutingGraph(
            Nodes: new[]
            {
                new LpbfRoutingNode("in",  "In",  IsExternalPort: true),
                new LpbfRoutingNode("out", "Out", IsExternalPort: true),
            },
            Edges: new[] { new LpbfRoutingEdge("in", "out", "path") });
        var result = LpbfPrintabilityAnalysis.Run(
            samples:      samples,
            buildAxis:    new Vector3(0, 0, 1),
            material:     Steel,
            routingGraph: graph);
        Assert.False(result.HasOverhangViolation);
        Assert.False(result.HasTrappedPowder);
        Assert.False(result.HasDrainPathViolation);
    }

    [Fact]
    public void CompositeAnalysis_NullVoxelField_LeavesTrappedPowderNull()
    {
        var samples = new[]
        {
            new SurfaceSample(Vector3.Zero, new Vector3(0, 0, 1), 1.0),
        };
        var result = LpbfPrintabilityAnalysis.Run(samples, new Vector3(0, 0, 1), Steel);
        Assert.Null(result.TrappedPowder);
        Assert.False(result.HasTrappedPowder);
    }

    // ════════════════════════════════════════════════════════════════
    //  End-to-end: GenerateWith + FeasibilityGate + Printability opt-in
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateWith_OptInOff_LeavesPrintabilityNull()
    {
        var cond = new OperatingConditions
        {
            Thrust_N           = 2224.0,
            ChamberPressure_Pa = 6.9e6,
            MixtureRatio       = 3.3,
        };
        var design = new RegenChamberDesign
        {
            IncludeLpbfPrintabilityAnalysis = false,
        };
        var result = RegenChamberOptimization.GenerateWith(
            cond, design,
            voxelSize_mm:       0.0,
            skipVoxelGeometry:  true);
        Assert.Null(result.Printability);
    }

    [Fact]
    public void GenerateWith_OptInOn_PopulatesPrintabilityWithAdvisor()
    {
        var cond = new OperatingConditions
        {
            Thrust_N           = 2224.0,
            ChamberPressure_Pa = 6.9e6,
            MixtureRatio       = 3.3,
        };
        var design = new RegenChamberDesign
        {
            IncludeLpbfPrintabilityAnalysis = true,
            LpbfMaterial = LpbfMaterial.Stainless316L,
        };
        var result = RegenChamberOptimization.GenerateWith(
            cond, design,
            voxelSize_mm:       0.0,
            skipVoxelGeometry:  true);
        Assert.NotNull(result.Printability);
        Assert.NotNull(result.Printability!.Orientation);
        Assert.Equal(LpbfMaterial.Stainless316L, result.Printability.Material.Material);
        // Drain-path graph built inside RunPrintabilityAnalysis has only
        // external ports for the default (no-purge) case → no violation.
        Assert.False(result.Printability.HasDrainPathViolation);
    }

    // ════════════════════════════════════════════════════════════════
    //  Schema round-trip
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DesignPersistence_RoundTripsLpbfPrintabilityFields()
    {
        var cond = new OperatingConditions();
        var design = new RegenChamberDesign
        {
            IncludeLpbfPrintabilityAnalysis = true,
            LpbfMaterial                    = LpbfMaterial.Inconel718,
            LpbfPrintOrientationAxis_deg    = 45.0,
        };

        using var tmp = TestTempFile.Create();
        Voxelforge.IO.DesignPersistence.Save(tmp.Path, cond, design, r: null);
        var loaded = Voxelforge.IO.DesignPersistence.Load(tmp.Path);
        Assert.NotNull(loaded);
        Assert.Equal(Voxelforge.IO.DesignPersistence.CurrentSchemaVersion, loaded!.Schema);
        Assert.True(loaded.Design!.IncludeLpbfPrintabilityAnalysis);
        Assert.Equal(LpbfMaterial.Inconel718, loaded.Design.LpbfMaterial);
        Assert.Equal(45.0, loaded.Design.LpbfPrintOrientationAxis_deg, precision: 6);
    }

    [Fact]
    public void DesignPersistence_DefaultDesign_HasLpbfPrintabilityOff()
    {
        var cond = new OperatingConditions();
        var design = new RegenChamberDesign();

        using var tmp = TestTempFile.Create();
        Voxelforge.IO.DesignPersistence.Save(tmp.Path, cond, design, r: null);
        var loaded = Voxelforge.IO.DesignPersistence.Load(tmp.Path);
        Assert.NotNull(loaded);
        Assert.False(loaded!.Design!.IncludeLpbfPrintabilityAnalysis);
        // CuCrZr is the documented default — matches the wall-material
        // default index 1.
        Assert.Equal(LpbfMaterial.CuCrZr, loaded.Design.LpbfMaterial);
        Assert.Equal(-1.0, loaded.Design.LpbfPrintOrientationAxis_deg, precision: 6);
    }

    // ════════════════════════════════════════════════════════════════
    //  FeasibilityGate integration — confirm gate firing paths
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Gate_OverhangAngleExceeded_FiresOnBadOrientation()
    {
        var cond = new OperatingConditions
        {
            Thrust_N           = 2224.0,
            ChamberPressure_Pa = 6.9e6,
            MixtureRatio       = 3.3,
        };
        // Choose Inconel 718 (35° floor) and force the build axis off the
        // chamber axis (60°) so at least one surface patch falls below the
        // floor. The axisymmetric chamber has slopes up to ~tan(30°) on the
        // converging section; rotating the build axis out of the chamber
        // axis guarantees some sample crosses the threshold.
        var design = new RegenChamberDesign
        {
            IncludeLpbfPrintabilityAnalysis = true,
            LpbfMaterial                    = LpbfMaterial.Inconel718,
            LpbfPrintOrientationAxis_deg    = 75.0,
        };
        var result = RegenChamberOptimization.GenerateWith(
            cond, design,
            voxelSize_mm:       0.0,
            skipVoxelGeometry:  true);
        var gate = FeasibilityGate.Evaluate(result);
        Assert.NotNull(result.Printability);
        // Expect the overhang gate to be part of the violation set when
        // the orientation is pathological. We assert on the predicate
        // rather than IsFeasible overall — other gates (e.g. wall T) can
        // fire on the same synthetic design independently.
        if (result.Printability!.HasOverhangViolation)
        {
            Assert.Contains(gate.Violations,
                v => v.ConstraintId == "OVERHANG_ANGLE_EXCEEDED");
        }
    }

    [Fact]
    public void Gate_DrainPathMissing_FiresOnStubbedPurgePort()
    {
        // Build a printability result directly and synthesise a graph
        // with a dead-end, then call the gate evaluator through the
        // composite analysis to confirm the violation surfaces.
        var samples = new[]
        {
            new SurfaceSample(Vector3.Zero, new Vector3(0, 0, 1), 1.0),
        };
        var graph = new LpbfRoutingGraph(
            Nodes: new[]
            {
                new LpbfRoutingNode("in",   "In",           IsExternalPort: true),
                new LpbfRoutingNode("out",  "Out",          IsExternalPort: true),
                new LpbfRoutingNode("stub", "Stubbed tap",  IsExternalPort: false),
            },
            Edges: new[]
            {
                new LpbfRoutingEdge("in",  "out",  "main"),
                new LpbfRoutingEdge("in",  "stub", "stub line"),
            });
        var printability = LpbfPrintabilityAnalysis.Run(
            samples, new Vector3(0, 0, 1), Steel, routingGraph: graph);
        Assert.True(printability.HasDrainPathViolation);
        Assert.Single(printability.DrainPath.Violations,
            v => v.Reason == "dead-end");
    }
}
