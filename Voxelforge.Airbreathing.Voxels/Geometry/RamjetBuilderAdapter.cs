// RamjetBuilderAdapter.cs — concrete IAirbreathingVoxelGenerator wrapping
// the static RamjetVoxelBuilder.Build. Mirrors ChamberVoxelBuilderAdapter on
// the rocket side (ADR-021 / Phase 0).

using Voxelforge.Airbreathing.Geometry;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Default <see cref="IAirbreathingVoxelGenerator"/> implementation: a thin
/// wrapper over <see cref="RamjetVoxelBuilder.Build"/>. App-side callers
/// (the <c>Voxelforge</c> main program's task thread) instantiate this adapter
/// and pass it to the airbreathing regen handler.
/// </summary>
public sealed class RamjetBuilderAdapter : IAirbreathingVoxelGenerator
{
    /// <inheritdoc />
    public RamjetGeometryResult Build(RamjetContour contour, RamjetBuildOptions opts)
        => RamjetVoxelBuilder.Build(contour, opts);
}
