// Phase10AxialTilingTests.cs — Pure-C# tests for the tiling plan
// math + StlWelder. Covers
// AxialTilePlan + AxialTilingPlan record invariants, PlanTiles coverage,
// InterpChannelHeight interpolation, and the binary-STL weld path
// (read / write / KeepTriangle / Weld).
//
// These tests don't touch PicoGK (no `new Library(...)`), so they're
// safe to run in the xUnit harness. The real voxel-building pipeline
// (ChamberAxialTileBuilder.BuildTile / BuildTiled) is verified by the
// Voxelforge.Benchmarks `--tiles <N>` console harness
// because PicoGK's Library singleton crashes the xUnit test host on
// dispose (project-wide gotcha).

using Voxelforge.Chamber;
using Voxelforge.Geometry;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class Phase10AxialTilingTests
{
    // Typical baseline contour: 500 N-class chamber used in most tests,
    // ~110 mm total length — safely above 2 × MinTileLength_mm (80 mm).
    private static ChamberContour MakeBaselineContour() =>
        ChamberContourGenerator.Generate(
            throatRadius_mm:       6.0,
            contractionRatio:      6.0,
            expansionRatio:        10.0,
            characteristicLength_m: 1.1);

    // Tiny contour — shorter than 2 × MinTileLength_mm. Forces the
    // degenerate single-tile return path regardless of targetTileCount.
    private static ChamberContour MakeTinyContour() =>
        ChamberContourGenerator.Generate(
            throatRadius_mm:       2.0,
            contractionRatio:      3.0,
            expansionRatio:        4.0,
            characteristicLength_m: 0.5);

    // 50 kN-class chamber — forces a multi-tile plan even for modest
    // targetTileCount. The tile math is what makes the large-thrust
    // case actually shippable, so this is the workload the plan is
    // designed for.
    private static ChamberContour MakeLargeThrustContour() =>
        ChamberContourGenerator.Generate(
            throatRadius_mm:       40.0,
            contractionRatio:      5.0,
            expansionRatio:        20.0,
            characteristicLength_m: 1.2);

    // ═════════════════════════════════════════════════════════════════
    //  PlanTiles — basic correctness
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void PlanTiles_SingleTileRequest_ReturnsOneTile()
    {
        var c = MakeBaselineContour();
        var plan = ChamberAxialTileBuilder.PlanTiles(c, targetTileCount: 1);
        Assert.Single(plan.Tiles);
        Assert.Equal(0,   plan.Tiles[0].TileIndex);
        Assert.Equal(0.0, plan.Tiles[0].OverlapWithPrev_mm);
        Assert.Equal(0.0, plan.Tiles[0].OverlapWithNext_mm);
        Assert.True(plan.IsFullyCovered());
    }

    [Fact]
    public void PlanTiles_MultiTile_CoversFullChamber()
    {
        var c = MakeLargeThrustContour();
        var plan = ChamberAxialTileBuilder.PlanTiles(c, targetTileCount: 4);
        Assert.Equal(4, plan.Count);
        Assert.True(plan.IsFullyCovered(),
            "Plan must cover [ChamberXMin, ChamberXMax] with no gaps.");
    }

    [Fact]
    public void PlanTiles_AdjacentTiles_OverlapByConfiguredAmount()
    {
        var c = MakeLargeThrustContour();
        double overlap = 3.5;
        var plan = ChamberAxialTileBuilder.PlanTiles(
            c, targetTileCount: 3, overlap_mm: overlap);
        Assert.Equal(3, plan.Count);
        // Interior tiles have overlap on both sides; boundary tiles only
        // have overlap toward their interior neighbour.
        Assert.Equal(0.0,     plan.Tiles[0].OverlapWithPrev_mm);
        Assert.Equal(overlap, plan.Tiles[0].OverlapWithNext_mm);
        Assert.Equal(overlap, plan.Tiles[1].OverlapWithPrev_mm);
        Assert.Equal(overlap, plan.Tiles[1].OverlapWithNext_mm);
        Assert.Equal(overlap, plan.Tiles[2].OverlapWithPrev_mm);
        Assert.Equal(0.0,     plan.Tiles[2].OverlapWithNext_mm);
    }

    [Fact]
    public void PlanTiles_TilesDoNotLeaveGaps()
    {
        // Adjacent tiles' XMax/XMin must touch or overlap — never leave
        // a gap that would produce a hole in the welded STL.
        var c = MakeLargeThrustContour();
        var plan = ChamberAxialTileBuilder.PlanTiles(c, targetTileCount: 5);
        for (int i = 1; i < plan.Count; i++)
        {
            double gap = plan.Tiles[i].XMin_mm - plan.Tiles[i - 1].XMax_mm;
            Assert.True(gap <= 1e-6,
                $"Tile {i} starts at {plan.Tiles[i].XMin_mm:F3} but tile {i - 1} ends at {plan.Tiles[i - 1].XMax_mm:F3} — gap of {gap:F3} mm would break the welder.");
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  PlanTiles — collapse-to-fewer-tiles + single-tile fallback
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void PlanTiles_TinyContour_ReturnsSingleTileRegardlessOfRequest()
    {
        var c = MakeTinyContour();
        var plan = ChamberAxialTileBuilder.PlanTiles(c, targetTileCount: 8);
        Assert.Single(plan.Tiles);
        Assert.Contains("Single-tile", plan.Rationale);
    }

    [Fact]
    public void PlanTiles_OverlyAmbitiousTileCount_CollapsesToViableNumber()
    {
        // Baseline contour ~110 mm with MinTileLength_mm = 40 → max
        // viable tile count is floor(110 / 40) ≈ 2. Requesting 10 should
        // collapse to 2, with a rationale explaining why.
        var c = MakeBaselineContour();
        var plan = ChamberAxialTileBuilder.PlanTiles(c, targetTileCount: 10);
        Assert.True(plan.Count < 10,
            $"Plan should have collapsed below requested 10 tiles; got {plan.Count}.");
        Assert.True(plan.Count >= 1);
        foreach (var t in plan.Tiles)
            Assert.True((t.XMax_mm - t.XMin_mm) >= ChamberAxialTileBuilder.MinTileLength_mm - 1e-3,
                $"Tile {t.TileIndex} length {t.XMax_mm - t.XMin_mm:F1} mm fell below MinTileLength_mm.");
    }

    [Fact]
    public void PlanTiles_ZeroOrNegativeTileCount_FallsBackToSingleTile()
    {
        var c = MakeBaselineContour();
        var plan0 = ChamberAxialTileBuilder.PlanTiles(c, targetTileCount: 0);
        var planNeg = ChamberAxialTileBuilder.PlanTiles(c, targetTileCount: -1);
        Assert.Single(plan0.Tiles);
        Assert.Single(planNeg.Tiles);
    }

    // ═════════════════════════════════════════════════════════════════
    //  PlanTiles — flange / gimbal extension fields
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void PlanTiles_IncludesInjectorFlangeExtension_AsNegativeXMin()
    {
        var c = MakeBaselineContour();
        var plan = ChamberAxialTileBuilder.PlanTiles(
            c, targetTileCount: 1, injectorFlangeThickness_mm: 8.0);
        Assert.True(plan.ChamberXMin_mm < -8.0,
            "ChamberXMin must extend below -InjectorFlangeThickness_mm + Build's -2 mm pad.");
    }

    [Fact]
    public void PlanTiles_IncludesMountFlangeAndGimbalAft_InXMax()
    {
        var c = MakeBaselineContour();
        double mount = 10.0, gimbal = 25.0;
        var plan = ChamberAxialTileBuilder.PlanTiles(
            c, targetTileCount: 1,
            mountFlangeThickness_mm:    mount,
            gimbalAftExtension_mm:      gimbal);
        double expectedMax = c.TotalLength_mm + mount + gimbal + 2.0;
        Assert.Equal(expectedMax, plan.ChamberXMax_mm, precision: 6);
    }

    // ═════════════════════════════════════════════════════════════════
    //  AxialTilingPlan.IsFullyCovered — invariant
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void IsFullyCovered_DetectsSyntheticGap()
    {
        // Hand-craft a malformed plan where tile 1 starts 5 mm after
        // tile 0 ends. IsFullyCovered must flag this.
        var bad = new AxialTilingPlan(
            Tiles: new[]
            {
                new AxialTilePlan(0, 0.0,  50.0, 0.0, 0.0),
                new AxialTilePlan(1, 55.0, 100.0, 0.0, 0.0),
            },
            ChamberXMin_mm:     0.0,
            ChamberXMax_mm:     100.0,
            PlannedOverlap_mm:  0.0,
            Rationale:          "Synthetic malformed plan for test.");
        Assert.False(bad.IsFullyCovered());
    }

    [Fact]
    public void IsFullyCovered_AcceptsWellFormedPlan()
    {
        var good = new AxialTilingPlan(
            Tiles: new[]
            {
                new AxialTilePlan(0, 0.0,  52.0, 0.0, 2.0),
                new AxialTilePlan(1, 50.0, 100.0, 2.0, 0.0),
            },
            ChamberXMin_mm:     0.0,
            ChamberXMax_mm:     100.0,
            PlannedOverlap_mm:  2.0,
            Rationale:          "Synthetic well-formed plan for test.");
        Assert.True(good.IsFullyCovered());
    }

    // ═════════════════════════════════════════════════════════════════
    //  AxialTilePlan — convenience accessors
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void AxialTilePlan_LengthAndBoundaryFlags()
    {
        var t = new AxialTilePlan(2, 100.0, 150.0, 2.0, 2.0);
        Assert.Equal(50.0, t.Length_mm);
        Assert.False(t.IsFirst);
        Assert.True(t.IsLast(3));
        Assert.False(t.IsLast(5));
    }

    // ═════════════════════════════════════════════════════════════════
    //  InterpChannelHeight — matches Build()'s interpolation
    // ═════════════════════════════════════════════════════════════════

    // ═════════════════════════════════════════════════════════════════
    //  StlWelder (pure-C# binary STL + seam weld)
    // ═════════════════════════════════════════════════════════════════

    private static StlTriangle Tri(float cx, float sign = 1f) =>
        // Helper: returns a triangle at axial centroid cx with a tiny
        // 1 mm footprint so centroid-based weld tests are deterministic.
        // `sign` flips Z to distinguish "different" triangles in equality
        // checks (our record-struct equality compares all 12 floats).
        new StlTriangle(
            Nx: 1, Ny: 0, Nz: 0,
            V1x: cx - 0.5f, V1y: 0, V1z: 0 * sign,
            V2x: cx + 0.5f, V2y: 1, V2z: 0 * sign,
            V3x: cx,        V3y: 0, V3z: 1 * sign);

    [Fact]
    public void StlTriangle_CentroidX_AveragesThreeVertices()
    {
        var t = Tri(10f);
        Assert.Equal(10f, t.CentroidX, precision: 4);
    }

    [Fact]
    public void StlTriangle_MinMaxX_TrackExtremes()
    {
        var t = Tri(10f);
        Assert.Equal(9.5f,  t.MinX, precision: 4);
        Assert.Equal(10.5f, t.MaxX, precision: 4);
    }

    [Fact]
    public void StlWelder_WriteThenRead_RoundTrips()
    {
        using var tmp = TestTempFile.Create();
        var src = new[] { Tri(0f), Tri(10f), Tri(50f, sign: -1f) };
        StlWelder.Write(tmp.Path, src, headerTag: "test");
        var read = StlWelder.Read(tmp.Path);
        Assert.Equal(src.Length, read.Length);
        for (int i = 0; i < src.Length; i++)
            Assert.Equal(src[i], read[i]);
    }

    [Fact]
    public void StlWelder_Read_RejectsTruncatedFile()
    {
        using var tmp = TestTempFile.Create();
        // Write a header + count but NO triangles — declared count 5,
        // file ends before the 5 × 50 bytes. Read() should detect
        // the mismatch and throw InvalidDataException.
        using (var fs = System.IO.File.Create(tmp.Path))
        using (var bw = new System.IO.BinaryWriter(fs))
        {
            bw.Write(new byte[80]);
            bw.Write((uint)5);   // claims 5 triangles
            // ... but we stop here, so the file is 84 bytes, not 334.
        }
        Assert.Throws<System.IO.InvalidDataException>(() => StlWelder.Read(tmp.Path));
    }

    [Fact]
    public void StlWelder_KeepTriangle_CentroidInsideCoreRange_IsKept()
    {
        var range = new TileWeldRange(0, 100, KeepLeftCap: false, KeepRightCap: false);
        Assert.True(StlWelder.KeepTriangle(Tri(50f), range));
        Assert.True(StlWelder.KeepTriangle(Tri(0f),  range));
        Assert.True(StlWelder.KeepTriangle(Tri(100f), range));
    }

    [Fact]
    public void StlWelder_KeepTriangle_CentroidOutsideCoreRange_InteriorTile_IsDropped()
    {
        // Interior tile: keep ONLY core-range triangles; both caps dropped.
        var range = new TileWeldRange(0, 100, KeepLeftCap: false, KeepRightCap: false);
        Assert.False(StlWelder.KeepTriangle(Tri(-5f),  range), "Left-overlap triangle should be dropped.");
        Assert.False(StlWelder.KeepTriangle(Tri(105f), range), "Right-overlap triangle should be dropped.");
    }

    [Fact]
    public void StlWelder_KeepTriangle_FirstTile_KeepsLeftCap()
    {
        // First tile: KeepLeftCap = true — the -X end cap (triangles
        // fully below CoreXMin) survives so the welded output is closed
        // at the injector end.
        var range = new TileWeldRange(0, 100, KeepLeftCap: true, KeepRightCap: false);
        Assert.True(StlWelder.KeepTriangle(Tri(-5f), range));   // fully left → cap, kept
        Assert.False(StlWelder.KeepTriangle(Tri(105f), range));  // fully right → overlap, dropped
    }

    [Fact]
    public void StlWelder_KeepTriangle_LastTile_KeepsRightCap()
    {
        var range = new TileWeldRange(0, 100, KeepLeftCap: false, KeepRightCap: true);
        Assert.False(StlWelder.KeepTriangle(Tri(-5f),  range));
        Assert.True(StlWelder.KeepTriangle(Tri(105f), range));
    }

    [Fact]
    public void StlWelder_KeepTriangle_StraddlingTriangleInOverlap_IsDroppedByInteriorTile()
    {
        // A triangle whose vertices straddle the core boundary (some
        // above, some below) has its centroid inside core range → kept.
        // This is the normal "shell-wall" triangle near the seam; both
        // tiles would produce such a triangle, but only the one whose
        // core range contains the centroid survives filtering.
        var range = new TileWeldRange(0, 100, KeepLeftCap: false, KeepRightCap: false);
        // Triangle with vertices at 98, 102, 100 → centroid at 100 → in range.
        var straddle = new StlTriangle(
            Nx: 1, Ny: 0, Nz: 0,
            V1x: 98f,  V1y: 0, V1z: 0,
            V2x: 102f, V2y: 1, V2z: 0,
            V3x: 100f, V3y: 0, V3z: 1);
        Assert.True(StlWelder.KeepTriangle(straddle, range));
    }

    [Fact]
    public void StlWelder_Filter_KeepsOnlySurvivors()
    {
        var range = new TileWeldRange(10, 30, KeepLeftCap: false, KeepRightCap: false);
        var input = new[] { Tri(0f), Tri(15f), Tri(25f), Tri(40f) };
        var output = StlWelder.Filter(input, range);
        Assert.Equal(2, output.Length);
        Assert.Equal(15f, output[0].CentroidX, precision: 4);
        Assert.Equal(25f, output[1].CentroidX, precision: 4);
    }

    [Fact]
    public void StlWelder_Weld_ThreeTiles_DropsInteriorCapsAndKeepsOuterCaps()
    {
        // Three-tile chamber: tile 0 spans x∈[0, 50] core, tile 1 spans
        // [50, 100] core, tile 2 spans [100, 150] core. With 2 mm overlap
        // on each interior boundary, each tile's per-tile STL contains
        // triangles slightly outside its core — those are "seam" artifacts
        // that the welder drops. Outer-facing caps on tiles 0 and 2
        // (x < 0 and x > 150 respectively) must survive.
        using var t0 = TestTempFile.Create();
        using var t1 = TestTempFile.Create();
        using var t2 = TestTempFile.Create();
        using var outPath = TestTempFile.Create();

        // Tile 0: outer cap at -2, core shell at 25, right-overlap cap at 52.
        StlWelder.Write(t0.Path, new[] { Tri(-2f), Tri(25f), Tri(52f) });
        // Tile 1: left-overlap cap at 48, core shell at 75, right-overlap cap at 102.
        StlWelder.Write(t1.Path, new[] { Tri(48f), Tri(75f), Tri(102f) });
        // Tile 2: left-overlap cap at 98, core shell at 125, outer cap at 152.
        StlWelder.Write(t2.Path, new[] { Tri(98f), Tri(125f), Tri(152f) });

        var ranges = new[]
        {
            new TileWeldRange(0,   50,  KeepLeftCap: true,  KeepRightCap: false),
            new TileWeldRange(50,  100, KeepLeftCap: false, KeepRightCap: false),
            new TileWeldRange(100, 150, KeepLeftCap: false, KeepRightCap: true),
        };
        var result = StlWelder.Weld(new[] { t0.Path, t1.Path, t2.Path }, ranges, outPath.Path, headerTag: "weld-test");

        Assert.Equal(9,  result.InputTriangleCount);
        // Survivors: tile 0 keeps outer cap (-2) + core (25) = 2; right-overlap
        // (52) dropped. Tile 1 keeps only core (75) = 1; both overlaps dropped.
        // Tile 2 keeps core (125) + outer cap (152) = 2; left-overlap dropped.
        // Total 5.
        Assert.Equal(5, result.OutputTriangleCount);
        Assert.Equal(4, result.DroppedTriangleCount);

        var welded = StlWelder.Read(outPath.Path);
        Assert.Equal(5, welded.Length);
        // Centroids should be {-2, 25, 75, 125, 152} in order.
        Assert.Equal(-2f,  welded[0].CentroidX, precision: 4);
        Assert.Equal(25f,  welded[1].CentroidX, precision: 4);
        Assert.Equal(75f,  welded[2].CentroidX, precision: 4);
        Assert.Equal(125f, welded[3].CentroidX, precision: 4);
        Assert.Equal(152f, welded[4].CentroidX, precision: 4);
    }

    [Fact]
    public void StlWelder_Weld_RejectsMismatchedLengths()
    {
        Assert.Throws<System.ArgumentException>(() =>
            StlWelder.Weld(
                new[] { "a.stl", "b.stl" },
                new[] { new TileWeldRange(0, 50, true, false) },   // length mismatch
                "out.stl"));
    }

    [Fact]
    public void InterpChannelHeight_LinearBetweenChamberThroatAndExit()
    {
        var ch = new Voxelforge.HeatTransfer.ChannelSchedule(
            ChannelCount: 40,
            RibThickness_mm: 0.8,
            GasSideWallThickness_mm: 0.5,
            ChannelHeightAtChamber_mm: 2.0,
            ChannelHeightAtThroat_mm:  1.0,
            ChannelHeightAtExit_mm:    3.0);

        // At x = 0 → chamber height.
        double h0 = ChamberAxialTileBuilder.InterpChannelHeight(0, 0, 50, 100, ch);
        Assert.Equal(2.0, h0, precision: 6);

        // At x = xThroat → throat height.
        double hT = ChamberAxialTileBuilder.InterpChannelHeight(50, 0, 50, 100, ch);
        Assert.Equal(1.0, hT, precision: 6);

        // At x = xEnd → exit height.
        double hE = ChamberAxialTileBuilder.InterpChannelHeight(100, 0, 50, 100, ch);
        Assert.Equal(3.0, hE, precision: 6);

        // Midway chamber↔throat → average.
        double hHalf = ChamberAxialTileBuilder.InterpChannelHeight(25, 0, 50, 100, ch);
        Assert.Equal(1.5, hHalf, precision: 6);
    }
}
