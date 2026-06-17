// DUPLICATED — unify in post-Wave-1 wrap-up (matching the marine- and
// electric-propulsion-pillar precedent comments in their copies).
//
// Nuclear-pillar concrete IVoxelHandle wrapper. Mirrors
// Voxelforge.Voxels/PicoGKVoxelHandle.cs,
// Voxelforge.Marine.Voxels/PicoGKVoxelHandle.cs, and
// Voxelforge.ElectricPropulsion.Voxels/PicoGKVoxelHandle.cs verbatim per
// the parallel-pillar policy in ADR-026 §2: each pillar owns its own
// concrete IVoxelHandle implementation.

using PicoGK;
using Voxelforge;

namespace Voxelforge.Nuclear;

/// <summary>
/// Concrete <see cref="IVoxelHandle"/> implementation for the nuclear
/// thermal pillar that wraps a <c>PicoGK.Voxels</c>. Constructed inside
/// <c>NtrChamberVoxelBuilder</c> and stored on
/// <c>NtrGeometryResult.Voxels</c>.
/// </summary>
public sealed class PicoGKVoxelHandle : IVoxelHandle
{
    public Voxels Inner { get; }

    public PicoGKVoxelHandle(Voxels inner) { Inner = inner; }
}

/// <summary>
/// Internal extension that unwraps a nuclear <see cref="IVoxelHandle"/>
/// as the underlying PicoGK.Voxels. Internal so consumer-side extensions
/// don't clash at import sites; <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/>
/// exposes it to <c>Voxelforge.Nuclear.StlExporter</c>.
/// </summary>
internal static class VoxelHandleExtensions
{
    internal static Voxels AsPicoGK(this IVoxelHandle handle)
        => ((PicoGKVoxelHandle)handle).Inner;
}
