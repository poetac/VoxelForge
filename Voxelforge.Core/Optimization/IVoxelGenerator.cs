// IVoxelGenerator.cs — Abstraction over the chamber voxel/STL build
// pipeline. Sprint A-3 / ADR-021 (2026-04-30): the orchestrators move
// to Core (PicoGK-free) and consult this interface; App + Voxels
// provide the concrete implementation that wraps the
// `ChamberVoxelBuilder.Build` / `BuildAnalytical` static methods.

namespace Voxelforge.Optimization;

/// <summary>
/// Abstraction over <see cref="Geometry.ChamberVoxelBuilder"/>'s static
/// build methods, used by the headless orchestrators
/// (<see cref="RegenChamberOptimization"/>,
/// <see cref="AerospikeOptimization"/>) to produce a chamber geometry
/// from a build-options bundle without referencing PicoGK directly.
/// <para>
/// Two implementations are expected:
/// <list type="bullet">
/// <item>
/// <c>Voxelforge.Voxels.ChamberVoxelBuilderAdapter</c> — thin wrapper
/// over the static builder; the only PicoGK escape hatch from the
/// optimization pipeline. App-side callers (WinForms forms, batch
/// runs, monolithic-engine path) pass an instance of this adapter.
/// </item>
/// <item>
/// <c>null</c> — headless / bench-SA / unit-test callers that pass
/// <c>skipVoxelGeometry: true</c> to <see cref="RegenChamberOptimization.GenerateWith"/>.
/// The orchestrator never consults the generator on that path.
/// </item>
/// </list>
/// </para>
/// </summary>
public interface IVoxelGenerator
{
    /// <summary>
    /// Build the chamber voxel body at the supplied voxel size. Throws
    /// <see cref="Voxelforge.Analysis.MemoryBudgetExceededException"/>
    /// when the projected grid exceeds the runtime memory budget;
    /// callers handle that by retrying with a coarser voxel size.
    /// </summary>
    Geometry.ChamberGeometryResult Build(
        Geometry.ChamberBuildOptions opts,
        double voxelSize_mm);

    /// <summary>
    /// Analytical-only path — returns the same
    /// <see cref="Geometry.ChamberGeometryResult"/> shape but without
    /// allocating a voxel body. Used by the bench-SA and
    /// <c>voxelforge-eval</c> paths that score physics without paying
    /// the voxel allocation cost.
    /// </summary>
    Geometry.ChamberGeometryResult BuildAnalytical(
        Geometry.ChamberBuildOptions opts);
}
