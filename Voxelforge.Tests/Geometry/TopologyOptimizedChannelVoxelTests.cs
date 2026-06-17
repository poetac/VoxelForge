// TopologyOptimizedChannelVoxelTests.cs — OOB-2 Sprint 2 voxel-build coverage.
//
// Sprint 1 (PR #378) shipped the SIMP solver in Voxelforge.Optimization.TopologyOptimizedChannels.
// Sprint 2 (this commit) wires the per-station channel-count field into the
// PicoGK voxel pipeline via TopologyOptimizedChannelImplicit + a new branch
// in ChamberVoxelBuilder when ChamberBuildOptions.TopologyOptimizedChannelsPerStation
// is populated.
//
// This test exercises the new branch end-to-end:
//   1. Build a baseline chamber with uniform N (legacy axial pattern path).
//   2. Build the SAME chamber with a varying-N field (high N at throat, low at
//      barrel — the SIMP-optimal shape).
//   3. Assert both produce > 0 triangles AND the variable-N mesh differs in
//      triangle count from the uniform baseline (proves the new branch fired
//      and produced a different geometry, not an accidental no-op fallthrough).
//
// In-process voxel build under PicoGK 2.0.0 (post-pitfall-#8 retirement, ADR-005
// retired 2026-05-04). Wall-clock ~3-5 s per build at 1 mm voxel.

using PicoGK;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

[Trait("Category", "VoxelBuild")]
public class TopologyOptimizedChannelVoxelTests
{
    [Fact]
    public void TopologyOptimizedChannels_BuildVariesFromUniformBaseline()
    {
        var cond = new OperatingConditions
        {
            Thrust_N            = 1_500,
            ChamberPressure_Pa  = 4.0e6,
            MixtureRatio        = 3.3,
            PropellantPair      = PropellantPair.LOX_CH4,
            WallMaterialIndex   = 1,
        };
        var design = new RegenChamberDesign
        {
            ExpansionRatio   = 6.0,
            ContractionRatio = 4.0,
        };

        const float voxel_mm = 1.0f;

        // Baseline: legacy uniform-N path (TopologyOptimizedChannelsPerStation = null).
        long uniformTris = BuildAndCountTriangles(cond, design, voxel_mm,
            channelsPerStation: null, axialPositions: null);

        // Topology-optimized: high N at throat (60), low N at barrel ends (12).
        // Mimics the shape the SIMP solver produces — peak channel density
        // concentrated where the Bartz heat flux is highest.
        var contour = MakeContour(cond, design);
        double L = contour.TotalLength_mm;
        double xThroat = contour.Stations[contour.ThroatIndex].X_mm;
        var xCoords = new[] { 0.5, xThroat * 0.5, xThroat, (xThroat + L) * 0.5, L - 0.5 };
        var nField  = new[] { 12,  30,             60,      30,                  12          };
        long topoTris = BuildAndCountTriangles(cond, design, voxel_mm,
            channelsPerStation: nField, axialPositions: xCoords);

        Assert.True(uniformTris > 1000, $"Baseline triangle count too low: {uniformTris}");
        Assert.True(topoTris    > 1000, $"Topology-opt triangle count too low: {topoTris}");

        // The two builds must differ — equality would mean the new branch
        // silently fell back to the uniform path. The exact magnitude of the
        // delta depends on voxel quantisation; a 1 % floor catches the
        // "same-mesh" failure mode without false-positives from quantisation
        // noise.
        long absDelta = System.Math.Abs(topoTris - uniformTris);
        long minDelta = System.Math.Max(uniformTris / 100, 100);
        Assert.True(absDelta >= minDelta,
            $"Topology-opt mesh too similar to uniform baseline (delta={absDelta}, "
          + $"min={minDelta}, uniform={uniformTris}, topo={topoTris}). "
          + "If this fires the variable-N branch in ChamberVoxelBuilder is not "
          + "actually being entered or the implicit is degenerating to the uniform N.");
    }

    private static ChamberContour MakeContour(OperatingConditions cond, RegenChamberDesign design)
    {
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
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

    private static long BuildAndCountTriangles(
        OperatingConditions cond,
        RegenChamberDesign  design,
        float               voxel_mm,
        int[]?              channelsPerStation,
        double[]?           axialPositions)
    {
        using var lib = new Library(voxel_mm);
        using var libScope = LibraryScope.Set(lib);

        var contour = MakeContour(cond, design);
        var channels = new ChannelSchedule(
            ChannelCount:              design.ChannelCount,
            RibThickness_mm:           design.RibThickness_mm,
            GasSideWallThickness_mm:   design.GasSideWallThickness_mm,
            ChannelHeightAtChamber_mm: design.ChannelHeightChamber_mm,
            ChannelHeightAtThroat_mm:  design.ChannelHeightThroat_mm,
            ChannelHeightAtExit_mm:    design.ChannelHeightExit_mm);

        var opts = new ChamberBuildOptions(
            Contour:                              contour,
            Channels:                             channels,
            OuterJacketThickness_mm:              design.OuterJacketThickness_mm,
            ManifoldLength_mm:                    design.ManifoldLength_mm,
            // Skip the fancier features so the test stays focused on the
            // channel branch under inspection.
            IncludeManifolds:                     true,
            IncludeInletOutletPorts:              false,
            IncludeInjectorFlange:                false,
            IncludeMountingFlange:                false,
            TopologyOptimizedChannelsPerStation:  channelsPerStation,
            TopologyOptimizedAxialPositions_mm:   axialPositions);

        var built = ChamberVoxelBuilder.Build(opts);
        var voxels = built.Voxels.AsPicoGK();
        var mesh = voxels.mshAsMesh();
        return mesh.nTriangleCount();
    }
}
