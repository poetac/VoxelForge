// MarineHullBuildOptions.cs — voxel-build knobs for the marine hull pipeline.
// Lives in Marine.Core so Marine.Tests can reference it without a PicoGK dep.

namespace Voxelforge.Marine.Geometry;

/// <summary>
/// Options for <see cref="IMarineVoxelGenerator.Build"/>.
/// All linear dimensions in millimetres (PicoGK convention).
/// </summary>
public sealed record MarineHullBuildOptions(
    double WallThickness_mm,
    double VoxelSize_mm      = 0,   // 0 = auto (wall/4, capped at MaxAutoVoxelSize_mm)
    double SmoothenRadius_mm = 0)   // 0 = skip smoothing pass
{
    /// <summary>
    /// Auto-voxel-size cap. AUV hulls at 0.5–5 m length print well at
    /// ≤ 0.4 mm voxel; finer wastes RAM without finding new features.
    /// </summary>
    public const double MaxAutoVoxelSize_mm = 0.4;
}
