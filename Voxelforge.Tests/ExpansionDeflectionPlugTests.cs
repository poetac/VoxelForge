// ExpansionDeflectionPlugTests.cs — coverage for the E-D inner plug voxel geometry
// (#337, OOB-13 part 2, 2026-05-04).
//
// Two tests:
//   A. Pure-data defaults check: new ChamberBuildOptions fields are off by
//      default so legacy designs produce bit-identical STL output.
//   B. In-process voxel round-trip: ExpansionDeflection topology produces
//      more triangles than Axial topology (the plug adds solid mass).
//
// Test B was originally subprocess-only (xUnit + PicoGK pitfall #8). Migrated
// 2026-05-04 to in-process now that PicoGK 2.0.0 (PR #374) resolves the
// disposal crash; xUnit host survives `new Library(...)` + ChamberVoxelBuilder
// + dispose cleanly. Trait switched from "Subprocess" → "VoxelBuild".

using PicoGK;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class ExpansionDeflectionPlugTests
{
    // ── Test A: pure-data defaults ───────────────────────────────────────────

    /// <summary>
    /// New ChamberBuildOptions fields must default to off / 0.40 so that
    /// existing designs that never set them produce bit-identical output.
    /// </summary>
    [Fact]
    public void ChamberBuildOptions_EdPlugDefaults_AreOff()
    {
        // Generate a minimal but valid contour + channels from the canonical
        // small-chamber fixture used by the per-station wall tests.
        var cond = new OperatingConditions
        {
            Thrust_N            = 5_000,
            ChamberPressure_Pa  = 4.0e6,
            MixtureRatio        = 3.3,
            WallMaterialIndex   = 0,
            PropellantPair      = PropellantPair.LOX_CH4,
        };
        var design  = new RegenChamberDesign();
        var gas     = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm:        derived.ThroatRadius_mm,
            contractionRatio:       design.ContractionRatio,
            expansionRatio:         design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            thetaN_deg:             design.BellEntranceAngle_deg,
            thetaE_deg:             design.BellExitAngle_deg,
            bellLengthFraction:     design.BellLengthFraction,
            stationCount:           40);
        var channels = new ChannelSchedule(
            ChannelCount:              design.ChannelCount,
            RibThickness_mm:           design.RibThickness_mm,
            GasSideWallThickness_mm:   design.GasSideWallThickness_mm,
            ChannelHeightAtChamber_mm: design.ChannelHeightChamber_mm,
            ChannelHeightAtThroat_mm:  design.ChannelHeightThroat_mm,
            ChannelHeightAtExit_mm:    design.ChannelHeightExit_mm);

        var opts = new ChamberBuildOptions(Contour: contour, Channels: channels);

        Assert.False(opts.IncludeExpansionDeflectionPlug,
            "IncludeExpansionDeflectionPlug must default to false so legacy designs are unaffected.");
        Assert.Equal(0.40, opts.EdPlugInnerOuterRatio, precision: 10);
    }

    // ── Test B: in-process voxel round-trip ──────────────────────────────────

    /// <summary>
    /// An E-D topology design must produce substantially more triangles than a
    /// standard axial-channel bell design, because the solid truncated-cone plug
    /// adds mass to the mesh. The plug is wired automatically when
    /// ChannelTopology = ExpansionDeflection in RegenChamberOptimization.GenerateWith.
    /// </summary>
    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void EdTopology_HasMoreTrianglesThanBellBaseline()
    {
        var cond = new OperatingConditions
        {
            Thrust_N            = 1_500,
            ChamberPressure_Pa  = 4.0e6,
            MixtureRatio        = 3.3,
            PropellantPair      = PropellantPair.LOX_CH4,
            WallMaterialIndex   = 1,
        };

        // Bell baseline: standard axial channels, no plug.
        var bellDesign = new RegenChamberDesign
        {
            ExpansionRatio   = 6.0,
            ContractionRatio = 4.0,
            ChannelTopology  = ChannelTopology.Axial,
        };

        // E-D design: same geometry but with the annular throat topology.
        // IncludeExpansionDeflectionPlug is set automatically by GenerateWith
        // when ChannelTopology == ExpansionDeflection.
        var edDesign = bellDesign with
        {
            ChannelTopology = ChannelTopology.ExpansionDeflection,
        };

        const float voxel_mm = 1.0f;
        long bellTris = BuildAndCountTriangles(cond, bellDesign, voxel_mm);
        long edTris   = BuildAndCountTriangles(cond, edDesign,   voxel_mm);

        // The solid inner plug at innerOuterRatio = 0.40 fills ~16 % of the
        // annular cross-section area. At 1 mm voxel on a 1500-N chamber the
        // plug contributes several thousand new surface triangles. 500 is a
        // conservative floor that clears voxel-quantisation noise (~100 tris)
        // without false-positives from surface-sharing geometry collapse.
        const long MinDelta = 500;
        long delta = edTris - bellTris;
        Assert.True(delta >= MinDelta,
            $"E-D plug must add >= {MinDelta} triangles over the bell baseline "
          + $"(delta={delta}, bell={bellTris}, ed={edTris}). "
          + "If this fires check that IncludeExpansionDeflectionPlug is wired in "
          + "RegenChamberOptimization.GenerateWith for ExpansionDeflection topology.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static long BuildAndCountTriangles(
        OperatingConditions cond,
        RegenChamberDesign  design,
        float               voxel_mm)
    {
        // Headless PicoGK 2.0.0 — scoped Library + thread-local LibraryScope
        // exactly mirrors the StlExporter CLI's setup.
        using var lib = new Library(voxel_mm);
        using var libScope = LibraryScope.Set(lib);

        var gen = RegenChamberOptimization.GenerateWith(
            cond, design, voxelSize_mm: voxel_mm,
            voxelGenerator: new ChamberVoxelBuilderAdapter());

        var voxels = gen.Geometry.Voxels.AsPicoGK();
        var mesh = voxels.mshAsMesh();
        return mesh.nTriangleCount();
    }
}
