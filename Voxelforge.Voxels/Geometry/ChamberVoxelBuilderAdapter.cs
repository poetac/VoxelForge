// ChamberVoxelBuilderAdapter.cs — App/Voxels-side bridge implementing
// `Voxelforge.Optimization.IVoxelGenerator` over the static
// `ChamberVoxelBuilder.Build` / `BuildAnalytical` methods.
//
// Sprint A-3 / ADR-021 (2026-04-30): the only PicoGK escape hatch from
// the optimization pipeline. App callers (WinForms forms, batch runs,
// monolithic-engine path) instantiate this adapter and pass it through
// `RegenChamberOptimization.GenerateWith(..., voxelGenerator: new
// ChamberVoxelBuilderAdapter())`. Headless / bench callers pass
// `voxelGenerator: null` together with `skipVoxelGeometry: true`; the
// orchestrator never consults a null generator on that path.

using Voxelforge.Optimization;

namespace Voxelforge.Geometry;

/// <summary>
/// Default <see cref="IVoxelGenerator"/> implementation: a thin wrapper
/// over <see cref="ChamberVoxelBuilder.Build"/> and
/// <see cref="ChamberVoxelBuilder.BuildAnalytical"/>. The orchestrator
/// in Core consults this interface to keep the PicoGK boundary at
/// project granularity (per ADR-015) without losing access to the
/// physical voxel build for App callers.
/// </summary>
public sealed class ChamberVoxelBuilderAdapter : IVoxelGenerator
{
    /// <inheritdoc />
    public ChamberGeometryResult Build(ChamberBuildOptions opts, double voxelSize_mm)
        => ChamberVoxelBuilder.Build(opts, voxelSize_mm);

    /// <inheritdoc />
    public ChamberGeometryResult BuildAnalytical(ChamberBuildOptions opts)
        => ChamberAnalyticalBuilder.BuildAnalytical(opts);
}
