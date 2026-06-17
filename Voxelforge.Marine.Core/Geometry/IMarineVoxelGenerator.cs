// IMarineVoxelGenerator.cs — decouples Marine physics from Marine.Voxels.
// Mirrors IAirbreathingVoxelGenerator in Voxelforge.Airbreathing.Core.
// Lives in Marine.Core so Marine.Tests references only one project.

using Voxelforge.Marine;

namespace Voxelforge.Marine.Geometry;

/// <summary>
/// Contract for the marine hull voxel-build pipeline. Isolates
/// <c>Voxelforge.Marine.Core</c> (and its tests) from the PicoGK dependency
/// in <c>Voxelforge.Marine.Voxels</c>.
/// Must be called on the task thread inside a PicoGK Library scope
/// (CLAUDE.md PicoGK pitfall #4).
/// </summary>
public interface IMarineVoxelGenerator
{
    /// <summary>
    /// Build the voxelised shell for the supplied hull design.
    /// </summary>
    MarineHullGeometryResult Build(MarineDesign design, MarineHullBuildOptions options);
}
