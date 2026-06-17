// MarineHullGeometryResult.cs — output of the marine hull voxel pipeline.
// Lives in Marine.Core (using IVoxelHandle) so consumers remain PicoGK-free.

using Voxelforge;

namespace Voxelforge.Marine.Geometry;

/// <summary>
/// Voxelised result of <see cref="IMarineVoxelGenerator.Build"/>.
/// <see cref="Shell"/> is the opaque voxel handle for the printable shell.
/// Consumers in Marine.Voxels / Marine.StlExporter unwrap via the internal
/// <c>AsPicoGK()</c> extension only when meshing or exporting.
/// </summary>
public sealed record MarineHullGeometryResult(
    IVoxelHandle Shell,
    double HullLength_mm,
    double HullDiameter_mm,
    double ShellVolume_mm3,
    double EstimatedMass_g,
    double VoxelSize_mm,
    string Description);
