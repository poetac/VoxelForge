// DUPLICATED — unify in Step-1 wrap-up.
//
// Air-breathing pillar's concrete IVoxelHandle wrapper. Mirrors
// Voxelforge.Voxels/PicoGKVoxelHandle.cs verbatim per the
// parallel-pillar policy: each pillar
// owns its own concrete IVoxelHandle implementation so the rocket
// pipeline never depends on air-breathing PicoGK code and vice versa.
// Both wrappers implement the same Voxelforge.IVoxelHandle marker
// interface from Voxelforge.Core. The unify-via-interfaces refactor
// happens at the end of Step 1 once 3+ concrete pillars exist
// (rocket + ramjet + turbojet).

using PicoGK;
using Voxelforge;

namespace Voxelforge.Airbreathing;

/// <summary>
/// Concrete <see cref="IVoxelHandle"/> implementation for the air-breathing
/// pillar that wraps a <c>PicoGK.Voxels</c>. Constructed inside
/// <c>RamjetVoxelBuilder</c> (and future air-breathing voxel builders)
/// and stored on result records like <c>RamjetGeometryResult</c>.
/// </summary>
public sealed class PicoGKVoxelHandle : IVoxelHandle
{
    public Voxels Inner { get; }

    public PicoGKVoxelHandle(Voxels inner) { Inner = inner; }
}

/// <summary>
/// Internal extension that unwraps an <see cref="IVoxelHandle"/> as the
/// underlying PicoGK.Voxels. Internal so the rocket-side
/// <c>VoxelHandleExtensions.AsPicoGK</c> doesn't clash at consumer
/// import sites; <see cref="InternalsVisibleToAttribute"/> exposes it
/// to <c>Voxelforge.Airbreathing.StlExporter</c> +
/// <c>Voxelforge.Airbreathing.Tests</c>.
/// </summary>
internal static class VoxelHandleExtensions
{
    /// <summary>
    /// Unwrap an <see cref="IVoxelHandle"/> as the underlying PicoGK.Voxels.
    /// Throws <see cref="System.InvalidCastException"/> if the handle is not
    /// an air-breathing-pillar <see cref="PicoGKVoxelHandle"/>.
    /// </summary>
    internal static Voxels AsPicoGK(this IVoxelHandle handle)
        => ((PicoGKVoxelHandle)handle).Inner;
}
