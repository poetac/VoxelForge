// ExpansionDeflectionPlugVoxelTests.cs — targeted voxel geometry tests for the
// E-D nozzle inner plug (#337, OOB-13 part 2, 2026-05-05).
//
// Complements ExpansionDeflectionPlugTests.cs (which covers defaults + topology
// comparison via GenerateWith). These tests exercise:
//   A. Flag-driven plug insertion via ChamberVoxelBuilder.Build directly
//   B. Direct AddPlug() call on an isolated empty shell (plug-only mesh)
//   C. Vertex-level geometry bounds (all plug vertices inside cowl radius)
//   D. Plug tip anchored at the throat axial station
//   E. Advisory gate still fires after geometry path lands
//   F. Guard conditions on the innerOuterRatio parameter
//   G. Determinism across two identical builds
//
// All voxel tests use the in-process PicoGK 2.0.0 pattern (scoped Library +
// LibraryScope.Set). Tests E uses skipVoxelGeometry: true — no PicoGK needed.

using System;
using System.Linq;
using System.Numerics;
using PicoGK;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class ExpansionDeflectionPlugVoxelTests
{
    // ── A. Flag-driven via ChamberVoxelBuilder.Build ─────────────────────────

    /// <summary>
    /// Setting IncludeExpansionDeflectionPlug = true on a ChamberBuildOptions
    /// object adds solid mass: the resulting mesh must have substantially more
    /// triangles than the same build with the flag off.
    /// This exercises the flag directly (independent of topology detection in
    /// GenerateWith) so the build-option API is tested in isolation.
    /// </summary>
    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void WithPlug_HasMoreTrianglesThanWithoutPlug()
    {
        var cond = new OperatingConditions
        {
            Thrust_N           = 1_500,
            ChamberPressure_Pa = 4.0e6,
            MixtureRatio       = 3.3,
            PropellantPair     = PropellantPair.LOX_CH4,
            WallMaterialIndex  = 1,
        };
        var design = new RegenChamberDesign
        {
            ExpansionRatio   = 6.0,
            ContractionRatio = 4.0,
            ChannelTopology  = ChannelTopology.Axial,
        };

        const float voxel_mm = 1.0f;
        using var lib      = new Library(voxel_mm);
        using var libScope = LibraryScope.Set(lib);

        var opts     = RegenChamberOptimization.ComposeChamberBuildOptions(cond, design);
        var noPlug   = opts;
        var withPlug = opts with { IncludeExpansionDeflectionPlug = true, EdPlugInnerOuterRatio = 0.40 };

        long trisNo   = ChamberVoxelBuilder.Build(noPlug,   voxel_mm).Voxels.AsPicoGK().mshAsMesh().nTriangleCount();
        long trisWith = ChamberVoxelBuilder.Build(withPlug, voxel_mm).Voxels.AsPicoGK().mshAsMesh().nTriangleCount();

        const long MinDelta = 500;
        long delta = trisWith - trisNo;
        Assert.True(delta >= MinDelta,
            $"IncludeExpansionDeflectionPlug=true must add >= {MinDelta} triangles "
          + $"(delta={delta}, no={trisNo}, with={trisWith}). "
          + "If this fails the plug geometry may not be fusing into the shell.");
    }

    // ── B. Direct AddPlug call on isolated empty shell ───────────────────────

    /// <summary>
    /// Calling AddPlug() on an otherwise empty voxel shell produces a non-zero
    /// mesh — the solid body of revolution is voxelised and generates surface
    /// triangles.
    /// </summary>
    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void PlugTriangleCount_WhenAddedToEmptyShell_IsNonZero()
    {
        var contour = BuildSmallContour();
        const float voxel_mm = 1.0f;

        using var lib      = new Library(voxel_mm);
        using var libScope = LibraryScope.Set(lib);

        var shell = MakeEmptyShell(contour, voxel_mm);
        var bounds = ContourBounds(contour, voxel_mm);
        ExpansionDeflectionPlugGeometry.AddPlug(shell, bounds, contour, 0.40);

        long triCount = shell.mshAsMesh().nTriangleCount();
        Assert.True(triCount > 0,
            $"AddPlug() must produce at least one surface triangle. Got {triCount}. "
          + "Check that the contour has bell stations past the throat.");
    }

    // ── C. Vertex-level geometry bounds ──────────────────────────────────────

    /// <summary>
    /// Every vertex of the plug-only mesh must lie within the cowl radius at
    /// that axial station (r ≤ innerOuterRatio × R_cowl(x)) within voxel
    /// quantisation tolerance.
    /// </summary>
    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void PlugVertices_RadialCoordinate_LessThanCowlRadius()
    {
        const double innerOuterRatio = 0.40;
        const float  voxel_mm       = 1.0f;

        var contour = BuildSmallContour();

        using var lib      = new Library(voxel_mm);
        using var libScope = LibraryScope.Set(lib);

        var shell  = MakeEmptyShell(contour, voxel_mm);
        var bounds = ContourBounds(contour, voxel_mm);
        ExpansionDeflectionPlugGeometry.AddPlug(shell, bounds, contour, innerOuterRatio);

        var mesh = shell.mshAsMesh();
        int nTri = (int)mesh.nTriangleCount();
        Assert.True(nTri > 0, "Plug produced no triangles — nothing to check.");

        float tol = 1.5f * voxel_mm; // voxelisation quantisation allowance
        for (int i = 0; i < nTri; i++)
        {
            mesh.GetTriangle(i, out Vector3 a, out Vector3 b, out Vector3 c);
            foreach (var v in new[] { a, b, c })
            {
                float r      = MathF.Sqrt(v.Y * v.Y + v.Z * v.Z);
                float rCowl  = InterpolateContourRadius(contour, v.X);
                float rLimit = (float)innerOuterRatio * rCowl + tol;
                Assert.True(r <= rLimit,
                    $"Vertex at x={v.X:F2} has r={r:F3} > ratio×rCowl+tol "
                  + $"({innerOuterRatio}×{rCowl:F3}+{tol:F3}={rLimit:F3}). "
                  + "Plug extends beyond the cowl radius.");
            }
        }
    }

    // ── D. Plug tip anchored at throat ────────────────────────────────────────

    /// <summary>
    /// The forward-most vertex of the plug mesh must be at or very near the
    /// throat axial station — the plug starts exactly at the throat plane and
    /// must not extend upstream into the converging section.
    /// </summary>
    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void PlugTip_MinAxialVertex_IsNearThroat()
    {
        const float voxel_mm = 1.0f;
        var contour = BuildSmallContour();

        using var lib      = new Library(voxel_mm);
        using var libScope = LibraryScope.Set(lib);

        var shell  = MakeEmptyShell(contour, voxel_mm);
        var bounds = ContourBounds(contour, voxel_mm);
        ExpansionDeflectionPlugGeometry.AddPlug(shell, bounds, contour, 0.40);

        var mesh = shell.mshAsMesh();
        int nTri = (int)mesh.nTriangleCount();
        Assert.True(nTri > 0, "Plug produced no triangles — nothing to check.");

        float minX = float.MaxValue;
        for (int i = 0; i < nTri; i++)
        {
            mesh.GetTriangle(i, out Vector3 a, out Vector3 b, out Vector3 c);
            minX = MathF.Min(minX, MathF.Min(a.X, MathF.Min(b.X, c.X)));
        }

        float throatX = (float)contour.Stations[contour.ThroatIndex].X_mm;
        float tol = 2f * voxel_mm;
        Assert.True(MathF.Abs(minX - throatX) <= tol,
            $"Plug tip at x={minX:F2} mm should be within {tol:F1} mm of the "
          + $"throat at x={throatX:F2} mm (delta={MathF.Abs(minX - throatX):F2}). "
          + "Check that AddPlug samples from ThroatIndex onward.");
    }

    // ── E. Advisory gate still fires ─────────────────────────────────────────

    /// <summary>
    /// The EXPANSION_DEFLECTION_PLUG_CLEARANCE advisory gate must fire for a
    /// small 500 N design whose E-D cowl radius is far below the 12 mm floor.
    /// This verifies the gate is not silenced by the voxel-geometry work.
    /// Uses skipVoxelGeometry: true — no PicoGK Library needed.
    /// </summary>
    [Fact]
    public void SmallCowl_ClearanceGateStillFires()
    {
        var cond = new OperatingConditions
        {
            Thrust_N              = 500,
            ChamberPressure_Pa    = 6.9e6,
            MixtureRatio          = 3.3,
            PropellantPair        = PropellantPair.LOX_CH4,
            CoolantInletTemp_K    = 150,
            CoolantInletPressure_Pa = 12e6,
        };
        var design = new RegenChamberDesign
        {
            ChannelTopology      = ChannelTopology.ExpansionDeflection,
            ContourStationCount  = 40,
            IncludeManifolds     = false,
            IncludePorts         = false,
            IncludeInjectorFlange = false,
        };

        var gen   = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);

        Assert.True(
            score.FeasibilityViolations.Any(v => v.ConstraintId == "EXPANSION_DEFLECTION_PLUG_CLEARANCE"),
            "Advisory gate EXPANSION_DEFLECTION_PLUG_CLEARANCE must fire on a 500 N E-D design "
          + "(cowl radius ~4 mm << 12 mm advisory floor). "
          + "If this fails the gate predicate may have been accidentally removed.");
    }

    // ── F. Guard conditions ───────────────────────────────────────────────────

    /// <summary>
    /// innerOuterRatio = 0 is a sentinel for "no plug" and must be a silent
    /// no-op — the shell is unchanged and no exception is thrown.
    /// </summary>
    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void AddPlug_WithZeroRatio_IsNoOp()
    {
        const float voxel_mm = 1.0f;
        var contour = BuildSmallContour();

        using var lib      = new Library(voxel_mm);
        using var libScope = LibraryScope.Set(lib);

        var bounds = ContourBounds(contour, voxel_mm);
        var shell  = MakeEmptyShell(contour, voxel_mm);

        // Must not throw; shell must remain empty (no triangles after the call).
        ExpansionDeflectionPlugGeometry.AddPlug(shell, bounds, contour, 0.0);

        long triCount = shell.mshAsMesh().nTriangleCount();
        Assert.Equal(0L, triCount);
    }

    /// <summary>
    /// innerOuterRatio = 1 is geometrically degenerate (plug fills the entire
    /// cowl bore) and must throw ArgumentOutOfRangeException.
    /// </summary>
    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void AddPlug_WithRatioEqualsOne_ThrowsArgumentOutOfRange()
    {
        const float voxel_mm = 1.0f;
        var contour = BuildSmallContour();

        using var lib      = new Library(voxel_mm);
        using var libScope = LibraryScope.Set(lib);

        var bounds = ContourBounds(contour, voxel_mm);
        var shell  = MakeEmptyShell(contour, voxel_mm);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExpansionDeflectionPlugGeometry.AddPlug(shell, bounds, contour, 1.0));
    }

    // ── G. Determinism ────────────────────────────────────────────────────────

    /// <summary>
    /// Two ChamberVoxelBuilder builds with identical inputs must produce the
    /// same triangle count — the plug geometry is deterministic.
    /// </summary>
    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void AddPlug_IsDeterministic_SameRatioSameTriangleCount()
    {
        var cond = new OperatingConditions
        {
            Thrust_N           = 1_500,
            ChamberPressure_Pa = 4.0e6,
            MixtureRatio       = 3.3,
            PropellantPair     = PropellantPair.LOX_CH4,
            WallMaterialIndex  = 1,
        };
        var design = new RegenChamberDesign
        {
            ExpansionRatio  = 6.0,
            ChannelTopology = ChannelTopology.ExpansionDeflection,
        };

        const float voxel_mm = 1.0f;

        // Build 1
        long count1;
        using (var lib      = new Library(voxel_mm))
        using (var libScope = LibraryScope.Set(lib))
        {
            var gen = RegenChamberOptimization.GenerateWith(
                cond, design, voxelSize_mm: voxel_mm,
                voxelGenerator: new ChamberVoxelBuilderAdapter());
            count1 = gen.Geometry.Voxels.AsPicoGK().mshAsMesh().nTriangleCount();
        }

        // Build 2 — fresh Library, same inputs
        long count2;
        using (var lib      = new Library(voxel_mm))
        using (var libScope = LibraryScope.Set(lib))
        {
            var gen = RegenChamberOptimization.GenerateWith(
                cond, design, voxelSize_mm: voxel_mm,
                voxelGenerator: new ChamberVoxelBuilderAdapter());
            count2 = gen.Geometry.Voxels.AsPicoGK().mshAsMesh().nTriangleCount();
        }

        Assert.Equal(count1, count2);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal ChamberContour from a 1500 N LOX/CH4 design at 4 MPa
    /// — small enough to run fast, large enough for the plug to have geometry.
    /// </summary>
    private static ChamberContour BuildSmallContour()
    {
        var cond = new OperatingConditions
        {
            Thrust_N           = 1_500,
            ChamberPressure_Pa = 4.0e6,
            MixtureRatio       = 3.3,
            PropellantPair     = PropellantPair.LOX_CH4,
            WallMaterialIndex  = 1,
        };
        var design = new RegenChamberDesign
        {
            ExpansionRatio   = 6.0,
            ContractionRatio = 4.0,
        };
        var gas     = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        return ChamberContourGenerator.Generate(
            throatRadius_mm:        derived.ThroatRadius_mm,
            contractionRatio:       design.ContractionRatio,
            expansionRatio:         design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            thetaN_deg:             design.BellEntranceAngle_deg,
            thetaE_deg:             design.BellExitAngle_deg,
            bellLengthFraction:     design.BellLengthFraction,
            stationCount:           40);
    }

    /// <summary>
    /// Creates a BBox3 that spans the bell region (throat → exit + 2 mm pad)
    /// with enough Y/Z clearance for the plug's outer radius.
    /// </summary>
    private static BBox3 ContourBounds(ChamberContour contour, float voxel_mm)
    {
        float throatX = (float)contour.Stations[contour.ThroatIndex].X_mm - 2f * voxel_mm;
        float exitX   = (float)contour.Stations[^1].X_mm                  + 2f * voxel_mm;
        float maxR    = (float)contour.Stations[^1].R_mm * 1.1f;
        return new BBox3(
            new Vector3(throatX, -maxR, -maxR),
            new Vector3(exitX,    maxR,  maxR));
    }

    /// <summary>
    /// Creates a voxel grid whose SDF is everywhere positive (outside), so
    /// BoolAdd will leave only the geometry added after this call.
    /// </summary>
    private static Voxels MakeEmptyShell(ChamberContour contour, float voxel_mm)
        => LibraryScope.MakeVoxels(new AlwaysOutsideImplicit(), ContourBounds(contour, voxel_mm));

    /// <summary>Linearly interpolates the contour wall radius at axial position x.</summary>
    private static float InterpolateContourRadius(ChamberContour contour, float x)
    {
        var sts = contour.Stations;
        int first = contour.ThroatIndex;

        if (x <= (float)sts[first].X_mm) return (float)sts[first].R_mm;
        if (x >= (float)sts[^1].X_mm)   return (float)sts[^1].R_mm;

        for (int i = first; i < sts.Length - 1; i++)
        {
            float xLo = (float)sts[i].X_mm,  xHi = (float)sts[i + 1].X_mm;
            if (x < xLo || x > xHi) continue;
            float t = (x - xLo) / MathF.Max(xHi - xLo, 1e-6f);
            return (float)(sts[i].R_mm + t * (sts[i + 1].R_mm - sts[i].R_mm));
        }
        return (float)sts[^1].R_mm;
    }

    // IImplicit that is everywhere outside — used to seed an empty voxel grid.
    private sealed class AlwaysOutsideImplicit : IImplicit
    {
        public float fSignedDistance(in Vector3 p) => 1_000f;
    }
}
