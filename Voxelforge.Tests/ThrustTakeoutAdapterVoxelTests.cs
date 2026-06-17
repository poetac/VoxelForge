// ThrustTakeoutAdapterVoxelTests — voxel-build round-trip for the
// Hot-fire readiness Item 6 (#260) adapter.
//
// Migrated 2026-05-04 from subprocess (former ThrustTakeoutAdapterSubprocessTests)
// to in-process now that PicoGK 2.0.0 (PR #374) resolved pitfall #8 — the
// xUnit test host survives `new Library(...)` + voxelisation + dispose
// cleanly. PicoGKLibraryDisposalProbeTests pins the basic capability;
// this test exercises a full ChamberVoxelBuilder build path inside xUnit.
//
// Original contract (still honoured): build the same chamber twice — once
// with the thrust-takeout adapter on, once without — and assert that the
// WITH-ADAPTER mesh has substantially more triangles than the WITHOUT-ADAPTER
// baseline. A 1000-triangle floor catches "did the flag actually wire?" and
// "did the geometry generator emit zero-volume output?" without false-firing
// on legitimate surface-share geometry collapse.
//
// Marked [Trait("Category", "VoxelBuild")] for filterability — the test
// performs two voxel builds at 1 mm voxel (~10–20 s wall-clock total).

using PicoGK;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

[Trait("Category", "VoxelBuild")]
public class ThrustTakeoutAdapterVoxelTests
{
    [Fact]
    public void ChamberVoxelBuilder_AdapterMeshHasMoreTrianglesThanBaseline()
    {
        var cond = new OperatingConditions
        {
            Thrust_N            = 1_500,
            ChamberPressure_Pa  = 4.0e6,
            MixtureRatio        = 3.3,
            PropellantPair      = PropellantPair.LOX_CH4,
            WallMaterialIndex   = 1,
        };

        var noAdapter = new RegenChamberDesign
        {
            ExpansionRatio              = 6.0,
            ContractionRatio            = 4.0,
            IncludeMountingFlange       = true,
            IncludeThrustTakeoutAdapter = false,
            OuterJacketThickness_mm     = 1.5,
        };

        var withAdapter = noAdapter with
        {
            IncludeThrustTakeoutAdapter                  = true,
            ThrustTakeoutAdapterHeight_mm                = 40.0,
            ThrustTakeoutOuterDiameter_mm                = 0.0,
            ThrustTakeoutMountStandard                   = MountingFlangeStandard.Generic8Bolt,
            ThrustTakeoutUmbilicalPassThroughCount       = 4,
            ThrustTakeoutUmbilicalPassThroughDiameter_mm = 8.0,
        };

        const float voxel_mm = 1.0f;
        long noAdapterTris   = BuildAndCountTriangles(cond, noAdapter,   voxel_mm);
        long withAdapterTris = BuildAndCountTriangles(cond, withAdapter, voxel_mm);

        const long MinDelta = 1000;
        long actualDelta = withAdapterTris - noAdapterTris;
        Assert.True(actualDelta >= MinDelta,
            $"With-adapter triangle count must exceed baseline by at least {MinDelta} triangles "
          + $"(actual_delta={actualDelta}, baseline={noAdapterTris}, with={withAdapterTris}). "
          + "If this fires the adapter is either not wired into the build path or producing degenerate geometry.");
    }

    private static long BuildAndCountTriangles(
        OperatingConditions cond,
        RegenChamberDesign  design,
        float               voxel_mm)
    {
        // Headless PicoGK 2.0.0 — scoped Library + thread-local LibraryScope
        // exactly mirrors the StlExporter CLI's setup
        // (Voxelforge.StlExporter/Program.cs).
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
