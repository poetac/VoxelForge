// PicoGKVoxelHandle.cs — concrete IVoxelHandle wrapper for the marine pillar.
//
// DUPLICATED — mirrors Voxelforge.Airbreathing.Voxels/PicoGKVoxelHandle.cs
// per the parallel-pillar policy. Each pillar
// owns its own concrete IVoxelHandle so the rocket and air-breathing pipelines
// never depend on marine PicoGK code and vice versa.

using PicoGK;
using Voxelforge;

namespace Voxelforge.Marine;

/// <summary>
/// Concrete <see cref="IVoxelHandle"/> that wraps a <c>PicoGK.Voxels</c>
/// for the marine pillar. Constructed inside <see cref="Marine.Geometry.MarineHullVoxelBuilder"/>
/// and stored on <see cref="Marine.Geometry.MarineHullGeometryResult.Shell"/>.
/// </summary>
public sealed class PicoGKVoxelHandle : IVoxelHandle
{
    public Voxels Inner { get; }

    public PicoGKVoxelHandle(Voxels inner) { Inner = inner; }
}

/// <summary>
/// Internal extension that unwraps an <see cref="IVoxelHandle"/> as the
/// underlying PicoGK.Voxels for the marine pillar. Exposed to
/// <c>Voxelforge.Marine.StlExporter</c> via InternalsVisibleTo.
/// </summary>
internal static class VoxelHandleExtensions
{
    internal static Voxels AsPicoGK(this IVoxelHandle handle)
        => ((PicoGKVoxelHandle)handle).Inner;
}
