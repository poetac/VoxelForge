using PicoGK;

namespace Voxelforge;

/// <summary>
/// Concrete <see cref="IVoxelHandle"/> implementation that wraps a
/// <c>PicoGK.Voxels</c>. Constructed at the moment the voxel body is
/// produced (inside <c>ChamberVoxelBuilder</c>, <c>AerospikeBuilder</c>,
/// etc.) and stored on Core records like <c>AerospikeBuildResult</c> +
/// <c>ChamberGeometryResult</c>.
/// </summary>
public sealed class PicoGKVoxelHandle : IVoxelHandle
{
    public Voxels Inner { get; }

    public PicoGKVoxelHandle(Voxels inner) { Inner = inner; }
}

public static class VoxelHandleExtensions
{
    /// <summary>
    /// Unwrap an <see cref="IVoxelHandle"/> as the underlying PicoGK.Voxels.
    /// Throws <see cref="System.InvalidCastException"/> if the handle is not
    /// a <see cref="PicoGKVoxelHandle"/> — which can only happen if a future
    /// non-PicoGK backend is added without updating call sites.
    /// </summary>
    public static Voxels AsPicoGK(this IVoxelHandle handle)
        => ((PicoGKVoxelHandle)handle).Inner;
}
