// ChamberAxialTileBuilder.cs — Axial-tiling voxel builder for
// large-thrust chambers that would otherwise exhaust the memory
// budget during a monolithic ChamberVoxelBuilder.Build call. Splits
// the chamber into N axial tiles, builds each tile's voxel grid on a
// smaller tile-local bounding box, meshes it to STL, disposes, then
// welds the tiles' meshes into a single STL via the
// centroid-x-core-range filter in StlWelder. Peak memory drops ~N×
// because the full chamber grid never co-exists.
//
// Architecture:
//   • Planner — AxialTilePlan / AxialTilingPlan records, PlanTiles
//     pure-math planner, ComputeTileBounds per-tile BBox3 computation.
//   • Builder — BuildTile body: shell + channels + manifolds + plain
//     radial ports clipped to tile bounds. BuildTiled driver iterates
//     tiles, meshes, disposes, welds via StlWelder.
//   • Welder — StlWelder pure-C# binary STL reader/writer +
//     centroid-x-core-range tile-seam filter.
//   • UI — Tile-large-builds checkbox + NUD dispatch in the main
//     form's regenerate path + auto-activate hint. Tile-aware flanges
//     share the `ChamberVoxelBuilder.AddInjectorFlangeFull` /
//     `AddMountingFlangeFull` helpers so tiled + monolithic produce
//     bit-identical flange geometry.
//
// Optional follow-ons (spawn on user demand):
//   • Threaded-boss BoolAdd for radial ports in tiled mode (today the
//     bore is cut but the boss protrusion silently drops — welder's
//     per-tile radial bbox would need to preserve the +Y cap).
//   • Hash-based vertex weld for STL consumers that reject vertex-soup
//     seams (slicers we know about all accept — Bambu / Prusa / Netfabb).
//   • Injector-orifice-bores in tiled mode (today tiled dispatch leaves
//     InjectorElementPattern at its null default, so the bore stage
//     skips; monolithic Build still runs the full ElementType switch).

using PicoGK;
using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;   // ChannelSchedule

namespace Voxelforge.Geometry;

/// <summary>
/// A single axial slice of a chamber build. <paramref name="XMin"/> and
/// <paramref name="XMax"/> are expressed in the chamber's axial frame
/// (mm, 0 = injector face, +X toward nozzle exit), matching
/// <see cref="ContourStation.X_mm"/> directly. The bounds may extend
/// slightly beyond the contour's [0, TotalLength] range when the plan
/// needs to cover injector flange / mount flange + gimbal aft extension
/// exactly like <see cref="ChamberVoxelBuilder.Build"/> does.
///
/// OverlapWithPrev / OverlapWithNext encode how much axial length the
/// tile shares with its neighbours. Phase 2b uses the overlap region to
/// weld adjacent tile meshes without holes at the seam — a non-zero
/// overlap means the implicit is sampled redundantly near the boundary
/// so each tile has its own closed cap, and the triangles on the
/// overlap plane can be de-duplicated by the welder.
/// </summary>
public readonly record struct AxialTilePlan(
    int    TileIndex,
    double XMin_mm,
    double XMax_mm,
    double OverlapWithPrev_mm,
    double OverlapWithNext_mm)
{
    public double Length_mm => XMax_mm - XMin_mm;

    /// <summary>True when this tile covers the injector end of the chamber.</summary>
    public bool IsFirst => TileIndex == 0;

    /// <summary>True when this tile covers the nozzle-exit end of the chamber.</summary>
    public bool IsLast(int totalTiles) => TileIndex == totalTiles - 1;
}

/// <summary>
/// Full tiling plan for one chamber build. Phase 2a returns this as a
/// pure-math result of <see cref="ChamberAxialTileBuilder.PlanTiles"/>;
/// Phase 2b's driver iterates <see cref="Tiles"/> and calls
/// <see cref="ChamberAxialTileBuilder.BuildTile"/> on each.
/// </summary>
public sealed record AxialTilingPlan(
    IReadOnlyList<AxialTilePlan> Tiles,
    double                       ChamberXMin_mm,
    double                       ChamberXMax_mm,
    double                       PlannedOverlap_mm,
    string                       Rationale)
{
    public int Count => Tiles.Count;

    /// <summary>Sum of tile core lengths (excluding overlap). Equals (ChamberXMax - ChamberXMin) by construction.</summary>
    public double CoveredLength_mm => ChamberXMax_mm - ChamberXMin_mm;

    /// <summary>
    /// True when every tile is fully covered by [ChamberXMin, ChamberXMax].
    /// Diagnostic — unit tests assert this holds.
    /// </summary>
    public bool IsFullyCovered()
    {
        if (Tiles.Count == 0) return false;
        if (Tiles[0].XMin_mm > ChamberXMin_mm + 1e-6) return false;
        if (Tiles[^1].XMax_mm < ChamberXMax_mm - 1e-6) return false;
        for (int i = 1; i < Tiles.Count; i++)
        {
            // Adjacent tiles must touch or overlap — never leave a gap.
            double gap = Tiles[i].XMin_mm - Tiles[i - 1].XMax_mm;
            if (gap > 1e-6) return false;
        }
        return true;
    }
}

public static partial class ChamberAxialTileBuilder
{
    /// <summary>
    /// Default overlap (mm) between adjacent tiles. A small overlap lets
    /// each tile's mesh have its own end-cap triangles near the seam;
    /// the welder de-duplicates co-planar triangles in the overlap region.
    /// 2.0 mm is ~5 voxels at 0.4 mm — enough for the sparse-grid
    /// padding PicoGK uses internally, while keeping the per-tile
    /// memory overhead under 1 % for typical tile counts (2-8).
    /// </summary>
    public const double DefaultOverlap_mm = 2.0;

    /// <summary>
    /// Minimum tile length (mm) for a tiling plan to be useful. Below
    /// this the per-tile overhead (shell rebuild + two end caps) starts
    /// to dominate the savings. 40 mm covers ~2× the throat arc + a
    /// comfortable converging section on typical chambers.
    /// </summary>
    public const double MinTileLength_mm = 40.0;

    /// <summary>
    /// Compute an axial tiling plan for the given contour. The chamber
    /// range [ChamberXMin_mm, ChamberXMax_mm] expands the contour's
    /// [0, TotalLength_mm] by the injector flange / mount flange /
    /// gimbal-aft extensions so Phase 2b's BuildTile covers the full
    /// monolithic ChamberVoxelBuilder.Build bounding box.
    ///
    /// <paramref name="targetTileCount"/> is a soft target: if the
    /// resulting per-tile length falls below <see cref="MinTileLength_mm"/>,
    /// the plan collapses to fewer tiles (or a single tile) with a
    /// rationale string the caller can log. Returns a single-tile plan
    /// for chambers shorter than 2 × <see cref="MinTileLength_mm"/>
    /// regardless of <paramref name="targetTileCount"/> — tiling isn't
    /// worth it on small designs.
    ///
    /// <paramref name="injectorFlangeThickness_mm"/> /
    /// <paramref name="mountFlangeThickness_mm"/> /
    /// <paramref name="gimbalAftExtension_mm"/> match the same variables
    /// <see cref="ChamberVoxelBuilder.Build"/> uses to compute xMinBound
    /// / xMaxBound. Pass zero when a feature is disabled so the plan
    /// doesn't reserve empty axial space.
    /// </summary>
    public static AxialTilingPlan PlanTiles(
        ChamberContour contour,
        int            targetTileCount,
        double         injectorFlangeThickness_mm = 0.0,
        double         mountFlangeThickness_mm    = 0.0,
        double         gimbalAftExtension_mm      = 0.0,
        double         overlap_mm                 = DefaultOverlap_mm)
    {
        double xMin = -injectorFlangeThickness_mm - 2.0;   // matches Build.xMinBound pad
        double xMax = contour.TotalLength_mm + mountFlangeThickness_mm + gimbalAftExtension_mm + 2.0;
        double totalLen = xMax - xMin;

        int clamped = System.Math.Max(1, targetTileCount);

        // Degenerate / too-small cases → single-tile plan.
        if (totalLen < 2.0 * MinTileLength_mm || clamped == 1)
        {
            return new AxialTilingPlan(
                Tiles:              new[] { new AxialTilePlan(0, xMin, xMax, 0.0, 0.0) },
                ChamberXMin_mm:     xMin,
                ChamberXMax_mm:     xMax,
                PlannedOverlap_mm:  0.0,
                Rationale:          $"Single-tile plan (chamber length {totalLen:F1} mm is below 2 × MinTileLength_mm = {2 * MinTileLength_mm:F0} mm, or targetTileCount = 1).");
        }

        // Collapse target downward until per-tile core length ≥ MinTileLength_mm.
        int n = clamped;
        while (n > 1 && (totalLen / n) < MinTileLength_mm) n--;

        double coreLen = totalLen / n;
        var tiles = new AxialTilePlan[n];
        for (int i = 0; i < n; i++)
        {
            double coreMin = xMin + i * coreLen;
            double coreMax = xMin + (i + 1) * coreLen;
            double overlapPrev = (i == 0)     ? 0.0 : overlap_mm;
            double overlapNext = (i == n - 1) ? 0.0 : overlap_mm;
            tiles[i] = new AxialTilePlan(
                TileIndex:          i,
                XMin_mm:            coreMin - overlapPrev,
                XMax_mm:            coreMax + overlapNext,
                OverlapWithPrev_mm: overlapPrev,
                OverlapWithNext_mm: overlapNext);
        }

        string rationale = n == clamped
            ? $"{n}-tile plan (core length {coreLen:F1} mm / tile, overlap {overlap_mm:F1} mm)."
            : $"{n}-tile plan (requested {clamped}; reduced because per-tile core length would have fallen below MinTileLength_mm = {MinTileLength_mm:F0} mm).";

        return new AxialTilingPlan(
            Tiles:              tiles,
            ChamberXMin_mm:     xMin,
            ChamberXMax_mm:     xMax,
            PlannedOverlap_mm:  overlap_mm,
            Rationale:          rationale);
    }

    /// <summary>
    /// Phase 2a skeleton for BuildTile. Constructs the tile-local
    /// <see cref="BBox3"/> matching <see cref="ChamberVoxelBuilder.Build"/>'s
    /// radial padding convention, then hands back an
    /// <see cref="AxialTileBounds"/> descriptor the tile's Voxels
    /// constructor will use. <see cref="BuildTile"/> consumes this
    /// internally; external callers (e.g. <c>Voxelforge.Benchmarks</c>
    /// summaries) use it to inspect the bbox a given tile would allocate.
    /// </summary>
    public static AxialTileBounds ComputeTileBounds(
        ChamberBuildOptions opt,
        AxialTilePlan       tile)
    {
        var contour = opt.Contour;
        var ch      = opt.Channels;

        // Compute the radial padding the same way Build() does, so a
        // tile's grid is never tighter than the monolithic equivalent.
        // The radial dimension is the same for every tile — only axial
        // extents differ.
        double xThroat = contour.Stations[contour.ThroatIndex].X_mm;
        double xEnd    = contour.TotalLength_mm;
        double maxOuterContour_mm = 0.0;
        foreach (var s in contour.Stations)
        {
            double h = opt.SkipChannelGeneration
                ? 0.0
                : InterpChannelHeight(s.X_mm, 0, xThroat, xEnd, ch);
            double r = s.R_mm + ch.GasSideWallThickness_mm + h + opt.OuterJacketThickness_mm;
            if (r > maxOuterContour_mm) maxOuterContour_mm = r;
        }

        // Flange / port contributions — match Build's maxOuterAll_mm calc.
        double flangeOuterRadius_mm = opt.IncludeInjectorFlange
            ? System.Math.Max(maxOuterContour_mm, contour.ChamberRadius_mm * opt.InjectorFlangeOuterRadiusFactor)
            : maxOuterContour_mm;
        double mountOuterRadius_mm = opt.IncludeMountingFlange
            ? System.Math.Max(maxOuterContour_mm, contour.ExitRadius_mm + opt.OuterJacketThickness_mm + 8.0)
            : maxOuterContour_mm;
        double maxOuterAll_mm = System.Math.Max(
            System.Math.Max(maxOuterContour_mm, flangeOuterRadius_mm),
            mountOuterRadius_mm);

        const float pad = 2f;
        var bounds = new BBox3(
            new System.Numerics.Vector3(
                (float)tile.XMin_mm,
                -(float)maxOuterAll_mm - pad,
                -(float)maxOuterAll_mm - pad),
            new System.Numerics.Vector3(
                (float)tile.XMax_mm,
                (float)maxOuterAll_mm + pad,
                (float)maxOuterAll_mm + pad));

        return new AxialTileBounds(tile, bounds, maxOuterAll_mm);
    }

    /// <summary>
    /// Replicates <see cref="ChamberVoxelBuilder.InterpChannelHeight"/>
    /// inline so this file stays independent of ChamberVoxelBuilder's
    /// private helper surface. Kept at internal visibility so tests in
    /// the same assembly can verify the interpolation matches.
    /// </summary>
    internal static double InterpChannelHeight(
        double x_mm, double xStart_mm, double xThroat_mm, double xEnd_mm,
        ChannelSchedule ch)
    {
        if (x_mm <= xThroat_mm)
        {
            double t = xThroat_mm - xStart_mm > 0
                ? (x_mm - xStart_mm) / (xThroat_mm - xStart_mm)
                : 0.0;
            t = System.Math.Clamp(t, 0.0, 1.0);
            return ch.ChannelHeightAtChamber_mm
                 + t * (ch.ChannelHeightAtThroat_mm - ch.ChannelHeightAtChamber_mm);
        }
        else
        {
            double t = xEnd_mm - xThroat_mm > 0
                ? (x_mm - xThroat_mm) / (xEnd_mm - xThroat_mm)
                : 0.0;
            t = System.Math.Clamp(t, 0.0, 1.0);
            return ch.ChannelHeightAtThroat_mm
                 + t * (ch.ChannelHeightAtExit_mm - ch.ChannelHeightAtThroat_mm);
        }
    }
}

/// <summary>
/// Phase 2b handoff record: carries the computed per-tile <see cref="BBox3"/>
/// and the radial max so the driver can call <c>new Voxels(impl, Bounds)</c>
/// directly, and use <see cref="MaxOuterRadius_mm"/> for any tile-local
/// implicit that needs to know where the chamber ends radially.
/// </summary>
public sealed record AxialTileBounds(
    AxialTilePlan Tile,
    BBox3         Bounds,
    double        MaxOuterRadius_mm);

/// <summary>
/// Per-tile diagnostic handed back from <see cref="ChamberAxialTileBuilder.BuildTile"/>.
/// Carries the actual tile <see cref="Voxels"/> (caller owns the handle
/// and should Dispose after meshing), plus the core range used by the
/// welder. TileVoxels is nullable so a callsite that only needs the
/// plan metadata (e.g. a dry-run in tests) can pass a null-builder.
/// </summary>
public sealed record AxialTileBuildResult(
    AxialTilePlan   Tile,
    TileWeldRange   CoreRange,
    Voxels?         TileVoxels,
    int             SubtractedChannelCount,
    bool            IncludedInletManifold,
    bool            IncludedOutletManifold,
    bool            IncludedInletPort,
    bool            IncludedOutletPort,
    bool            IncludedInjectorFlange,   // Full-fidelity flange on first tile.
    bool            IncludedMountingFlange,   // Full-fidelity flange on last tile.
    double          BuildWallMs);

public static partial class ChamberAxialTileBuilder
{
    // ─── BuildTile ────────────────────────────────────────────────────
    //  Produces one tile: shell + channels (clipped to tile axial range)
    //  + manifolds + plain radial ports + full-fidelity flanges (first
    //  tile owns the injector flange, last tile owns the mount flange —
    //  the welder's cap-preservation contract keeps their outward-facing
    //  faces visible through seam de-duplication).
    //
    //  Must only be called after PicoGK's Library singleton has been
    //  initialised — callers running under Voxelforge.Benchmarks
    //  already `using var lib = new Library(voxelSize)` before dispatching
    //  tile work, and the in-app path lives inside Library.Go().

    /// <summary>
    /// Build a single axial tile. Returns a disposable
    /// <see cref="Voxels"/> containing the tile's shell with channels,
    /// manifolds, and radial ports clipped to its axial range. The
    /// caller is expected to mesh the result, write to a per-tile STL,
    /// and dispose both the mesh and the voxels before moving on to
    /// the next tile — that's the whole point of tiling.
    /// </summary>
    public static AxialTileBuildResult BuildTile(
        ChamberBuildOptions opt,
        AxialTilePlan       tile,
        int                 totalTiles)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        var contour = opt.Contour;
        var ch      = opt.Channels;

        // Inner / outer revolved implicits — identical to monolithic
        // Build; the implicits don't know or care about tiles.
        double xThroat = contour.Stations[contour.ThroatIndex].X_mm;
        double xEnd    = contour.TotalLength_mm;

        var innerPts = new (double, double)[contour.Stations.Length];
        var outerPts = new (double, double)[contour.Stations.Length];
        for (int i = 0; i < contour.Stations.Length; i++)
        {
            var s = contour.Stations[i];
            innerPts[i] = (s.X_mm, s.R_mm);

            double h = opt.SkipChannelGeneration
                ? 0.0
                : InterpChannelHeight(s.X_mm, 0, xThroat, xEnd, ch);
            double rOut = s.R_mm + ch.GasSideWallThickness_mm + h + opt.OuterJacketThickness_mm;
            outerPts[i] = (s.X_mm, rOut);
        }
        var innerImpl = new RevolvedContourImplicit(innerPts);
        var outerImpl = new RevolvedContourImplicit(outerPts);

        // Tile-local BBox3. Radial padding matches ComputeTileBounds.
        var tb     = ComputeTileBounds(opt, tile);
        var bounds = tb.Bounds;

        // ── Shell = outer − inner, sampled on tile bounds only ─────
        var outerSolid = LibraryScope.MakeVoxels(outerImpl, bounds);
        var innerSolid = LibraryScope.MakeVoxels(innerImpl, bounds);
        outerSolid.BoolSubtract(innerSolid);
        (innerSolid as System.IDisposable)?.Dispose();

        // ── Channels: axial range [xChStart, xChEnd]; each channel's
        //    implicit is sampled on the tile's bbox → only the portion
        //    of the helix that lives in the tile survives.
        int subtractedChannels = 0;
        int N = ch.ChannelCount;
        if (!opt.SkipChannelGeneration && N > 0)
        {
            float xChStart = (float)(opt.IncludeManifolds ? opt.ManifoldLength_mm : 0.5);
            float xChEnd   = (float)(contour.TotalLength_mm
                          - (opt.IncludeManifolds ? opt.ManifoldLength_mm : 0.5));
            if (xChEnd <= xChStart) xChEnd = xChStart + 1f;

            // Skip channels entirely if the tile doesn't intersect the
            // channel span — saves one new Voxels per channel on the
            // injector-flange-only and mount-flange-only tiles.
            bool tileIntersectsChannels =
                tile.XMax_mm > xChStart && tile.XMin_mm < xChEnd;

            if (tileIntersectsChannels)
            {
                // Pattern-mode: one voxelise + one BoolSubtract for the
                // full N-channel bundle. Voxelising against the tile bbox
                // clips the pattern to the tile's axial range exactly as
                // the prior per-channel loop did, since the pattern SDF
                // is well-defined globally and every voxel outside the
                // tile bounds is unreachable. SubtractedChannelCount
                // reports N for telemetry parity with the pre-refactor
                // per-tile log.
                var patImpl = new AxialChannelPatternImplicit(
                    innerImpl,
                    (float)ch.GasSideWallThickness_mm,
                    (float)ch.ChannelHeightAtChamber_mm,
                    (float)ch.ChannelHeightAtThroat_mm,
                    (float)ch.ChannelHeightAtExit_mm,
                    xChStart, (float)xThroat, xChEnd,
                    N,
                    (float)ch.RibThickness_mm,
                    phaseOffsetRad: 0f,
                    manifoldFilletRadius_mm: (float)opt.ChannelManifoldFilletRadius_mm,
                    helixPitchAngle_deg:     (float)opt.HelixPitchAngle_deg);

                var patVox = LibraryScope.MakeVoxels(patImpl, bounds);
                outerSolid.BoolSubtract(patVox);
                (patVox as System.IDisposable)?.Dispose();
                subtractedChannels = N;
            }
        }

        // ── Manifolds: inlet at [totalLen − manifoldLen, totalLen],
        //    outlet at [0, manifoldLen]. Only subtract on tiles whose
        //    axial range actually intersects the manifold segment.
        bool didInletMan = false, didOutletMan = false;
        if (opt.IncludeManifolds && !opt.SkipChannelGeneration)
        {
            double xInletStart  = contour.TotalLength_mm - opt.ManifoldLength_mm;
            double xInletEnd    = contour.TotalLength_mm;
            double xOutletStart = 0.0;
            double xOutletEnd   = opt.ManifoldLength_mm;

            float hInlet    = (float)ch.ChannelHeightAtExit_mm;
            float hOutlet   = (float)ch.ChannelHeightAtChamber_mm;
            float clearance = 0.4f;

            if (tile.XMax_mm > xInletStart && tile.XMin_mm < xInletEnd)
            {
                var man = new RevolvedPlenumImplicit(
                    innerImpl,
                    (float)xInletStart, (float)xInletEnd,
                    (float)ch.GasSideWallThickness_mm, hInlet, clearance);
                var mVox = LibraryScope.MakeVoxels(man, bounds);
                outerSolid.BoolSubtract(mVox);
                (mVox as System.IDisposable)?.Dispose();
                didInletMan = true;
            }
            if (tile.XMax_mm > xOutletStart && tile.XMin_mm < xOutletEnd)
            {
                var man = new RevolvedPlenumImplicit(
                    innerImpl,
                    (float)xOutletStart, (float)xOutletEnd,
                    (float)ch.GasSideWallThickness_mm, hOutlet, clearance);
                var mVox = LibraryScope.MakeVoxels(man, bounds);
                outerSolid.BoolSubtract(mVox);
                (mVox as System.IDisposable)?.Dispose();
                didOutletMan = true;
            }
        }

        // ── Radial ports: plain drilled bore branch only. Threaded-boss
        //    BoolAdd protrusions are NOT yet emitted by the tile builder
        //    because the boss sticks out radially and sits inside one
        //    specific tile's bbox — the welder's per-tile radial bbox
        //    check would need to be extended to preserve the +Y cap.
        //    Today tiled mode with
        //    `CoolantPortStandard != Plain` will silently drop the
        //    threaded boss — the bore itself is still cut.
        bool didInletPort = false, didOutletPort = false;
        if (opt.IncludeInletOutletPorts
            && opt.IncludeManifolds
            && !opt.SkipChannelGeneration)
        {
            double xInletPort  = contour.TotalLength_mm - 0.5 * opt.ManifoldLength_mm;
            double xOutletPort = 0.5 * opt.ManifoldLength_mm;

            if (tile.XMin_mm <= xInletPort && tile.XMax_mm >= xInletPort)
            {
                AddPlainRadialPort(outerSolid, bounds, innerImpl, outerImpl,
                    (float)xInletPort,
                    (float)(opt.PortDiameter_mm * 0.5),
                    (float)ch.GasSideWallThickness_mm,
                    (float)opt.OuterJacketThickness_mm);
                didInletPort = true;
            }
            if (tile.XMin_mm <= xOutletPort && tile.XMax_mm >= xOutletPort)
            {
                AddPlainRadialPort(outerSolid, bounds, innerImpl, outerImpl,
                    (float)xOutletPort,
                    (float)(opt.PortDiameter_mm * 0.5),
                    (float)ch.GasSideWallThickness_mm,
                    (float)opt.OuterJacketThickness_mm);
                didOutletPort = true;
            }
        }

        // ── Smoothen: apply within tile. Same safety cap as monolithic
        //    Build (0.25 × min wall thickness) to prevent feature loss.
        if (opt.SmoothingRadius_mm > 0)
        {
            double minWall = System.Math.Min(ch.GasSideWallThickness_mm, ch.RibThickness_mm);
            double safeRadius = System.Math.Min(opt.SmoothingRadius_mm, 0.25 * minWall);
            if (safeRadius > 0.02) outerSolid.Smoothen((float)safeRadius);
        }

        // ── Tile-aware flanges (full fidelity).
        //    First tile (x ≤ 0 region) owns the injector flange — disc +
        //    two threaded/plain propellant ports (LOX +Y, fuel -Y) + 6-bolt
        //    clearance pattern. Last tile (x ≥ totalLength region) owns the
        //    mounting flange — disc + exit bore + preset-driven bolt pattern
        //    (MountingFlangePresets). Welder's first-tile-keeps-left-cap /
        //    last-tile-keeps-right-cap preserves outward-facing flange
        //    surfaces unchanged.
        //
        //    Calls the shared `AddInjectorFlangeFull` /
        //    `AddMountingFlangeFull` helpers on `ChamberVoxelBuilder` so
        //    monolithic Build() and BuildTile produce the same flange
        //    geometry bit-identically.
        double maxOuterContour_mm = tb.MaxOuterRadius_mm - 10.0;  // undo the +10 flange-lip pad
        bool didInjFlange = false, didMountFlange = false;

        if (tile.IsFirst && opt.IncludeInjectorFlange)
        {
            var propPortSpec = PortStandards.Get(opt.PropellantPortStandard);
            ChamberVoxelBuilder.AddInjectorFlangeFull(
                outerSolid, bounds, opt, contour, propPortSpec, maxOuterContour_mm);
            didInjFlange = true;
        }

        if (tile.IsLast(totalTiles) && opt.IncludeMountingFlange)
        {
            ChamberVoxelBuilder.AddMountingFlangeFull(
                outerSolid, bounds, opt, contour, maxOuterContour_mm);
            didMountFlange = true;
        }

        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        double wallMs = (t1 - t0) / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0;

        var coreRange = new TileWeldRange(
            CoreXMin_mm:  tile.XMin_mm + tile.OverlapWithPrev_mm,
            CoreXMax_mm:  tile.XMax_mm - tile.OverlapWithNext_mm,
            KeepLeftCap:  tile.IsFirst,
            KeepRightCap: tile.IsLast(totalTiles));

        return new AxialTileBuildResult(
            Tile:                    tile,
            CoreRange:               coreRange,
            TileVoxels:              outerSolid,
            SubtractedChannelCount:  subtractedChannels,
            IncludedInletManifold:   didInletMan,
            IncludedOutletManifold:  didOutletMan,
            IncludedInletPort:       didInletPort,
            IncludedOutletPort:      didOutletPort,
            IncludedInjectorFlange:  didInjFlange,
            IncludedMountingFlange:  didMountFlange,
            BuildWallMs:             wallMs);
    }

    /// <summary>
    /// Phase 2b driver. Iterates the plan's tiles, builds each,
    /// meshes to a per-tile binary STL in <paramref name="tempDir"/>,
    /// disposes the per-tile voxels + mesh, then welds the per-tile
    /// STLs into a single <paramref name="outputStlPath"/> via
    /// <see cref="StlWelder.Weld"/>. Per-tile temp STLs are deleted
    /// on success; on failure they're kept for diagnosis.
    /// </summary>
    public static TiledBuildSummary BuildTiled(
        ChamberBuildOptions opt,
        AxialTilingPlan     plan,
        string              outputStlPath,
        string?             tempDir = null)
    {
        // Per-invocation unique subdir avoids two concurrent BuildTiled
        // calls clobbering each other's per-tile STLs (audit 01-security
        // L4: previously a fixed "regen-axial-tiles" name raced under
        // parallel invocations). Caller can still pin a deterministic
        // path via the explicit parameter for diagnostic capture.
        tempDir ??= System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"regen-axial-tiles-{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempDir);

        var tilePaths  = new string[plan.Count];
        var tileRanges = new TileWeldRange[plan.Count];
        var tileSummaries = new AxialTileBuildResult?[plan.Count];

        long tStart = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            for (int i = 0; i < plan.Count; i++)
            {
                var tile = plan.Tiles[i];
                var result = BuildTile(opt, tile, plan.Count);
                tileSummaries[i] = result;
                tileRanges[i]    = result.CoreRange;

                // Mesh + save per-tile STL, then dispose.
                string tilePath = System.IO.Path.Combine(
                    tempDir, $"tile-{i:D2}-x{tile.XMin_mm:F0}-{tile.XMax_mm:F0}mm.stl");
                tilePaths[i] = tilePath;

                if (result.TileVoxels is Voxels vox)
                {
                    var mesh = new Mesh(vox);
                    mesh.SaveToStlFile(tilePath);
                    (mesh as System.IDisposable)?.Dispose();
                    (vox  as System.IDisposable)?.Dispose();
                }
            }

            long tAfterBuild = System.Diagnostics.Stopwatch.GetTimestamp();

            // Weld per-tile STLs into the output.
            // PR-2 namespace rename (2026-04-30): STL header tag kept as
            // the literal "RegenChamberDesigner tiled STL" so existing
            // STL files round-trip without consumer-side updates.
            string headerTag =
                $"RegenChamberDesigner tiled STL ({plan.Count} tiles)";
            var weld = StlWelder.Weld(tilePaths, tileRanges, outputStlPath, headerTag);

            long tAfterWeld = System.Diagnostics.Stopwatch.GetTimestamp();

            // Cleanup per-tile temp files on success.
            foreach (var p in tilePaths)
                try { System.IO.File.Delete(p); } catch { /* best-effort */ }

            double buildMs = (tAfterBuild - tStart)     / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0;
            double weldMs  = (tAfterWeld  - tAfterBuild) / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0;

            // Strip nullable wrapping now that every tile built.
            var solidSummaries = new AxialTileBuildResult[plan.Count];
            for (int i = 0; i < plan.Count; i++)
                solidSummaries[i] = tileSummaries[i]!;

            return new TiledBuildSummary(
                Plan:            plan,
                TileResults:     solidSummaries,
                WeldResult:      weld,
                OutputStlPath:   outputStlPath,
                PerTileBuild_ms: buildMs,
                Weld_ms:         weldMs);
        }
        catch
        {
            // Leave per-tile STLs in place for diagnosis, but dispose
            // any dangling voxels from tiles we'd started but not written.
            for (int i = 0; i < plan.Count; i++)
            {
                if (tileSummaries[i] is { TileVoxels: Voxels vox })
                    try { (vox as System.IDisposable)?.Dispose(); } catch { }
            }
            throw;
        }
    }

    /// <summary>
    /// Plain drilled radial port — subtract a cylinder from the shell
    /// starting inside the plenum and exiting past the jacket OD.
    /// Replicates <c>ChamberVoxelBuilder.AddRadialPort</c>'s non-
    /// threaded branch. Threaded-boss BoolAdd protrusion is an
    /// optional follow-on: the welder would need to preserve a per-tile
    /// +Y cap for the boss, which today's centroid-core-range filter
    /// doesn't do. Today threaded ports in tiled mode cut the bore
    /// correctly but the boss is silently dropped.
    /// </summary>
    private static void AddPlainRadialPort(
        Voxels                   shell,
        BBox3                    bounds,
        RevolvedContourImplicit  innerWall,
        RevolvedContourImplicit  outerJacket,
        float                    xPort_mm,
        float                    plainPortRadius_mm,
        float                    tWall_mm,
        float                    tJacket_mm)
    {
        float rJacketOuter = outerJacket.RadiusAt(xPort_mm);
        float rInnerWall   = innerWall.RadiusAt(xPort_mm);
        float rStart       = rInnerWall + tWall_mm + 0.5f;
        float rEnd         = rJacketOuter + 15f;
        float length       = System.Math.Max(rEnd - rStart, tJacket_mm + 12f);

        var cyl = new CylinderImplicit(
            new System.Numerics.Vector3(xPort_mm, rStart, 0),
            new System.Numerics.Vector3(0, 1, 0),
            plainPortRadius_mm,
            length);
        var cylVox = LibraryScope.MakeVoxels(cyl, bounds);
        shell.BoolSubtract(cylVox);
        (cylVox as System.IDisposable)?.Dispose();
    }
}

/// <summary>
/// Summary of a full tiled build. <see cref="TileResults"/> carries the
/// per-tile diagnostics so benchmark harnesses / UI can render the
/// per-stage wall-clock breakdown the user needs to decide whether
/// tiling paid off vs monolithic Build.
/// </summary>
public sealed record TiledBuildSummary(
    AxialTilingPlan                Plan,
    IReadOnlyList<AxialTileBuildResult> TileResults,
    StlWeldResult                  WeldResult,
    string                         OutputStlPath,
    double                         PerTileBuild_ms,
    double                         Weld_ms);
