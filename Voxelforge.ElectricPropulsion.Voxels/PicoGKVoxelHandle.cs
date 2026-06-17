// DUPLICATED — unify in post-Wave-1 wrap-up (matching the
// airbreathing-pillar precedent comment).
//
// Electric-propulsion pillar's concrete IVoxelHandle wrapper. Mirrors
// Voxelforge.Voxels/PicoGKVoxelHandle.cs and
// Voxelforge.Airbreathing.Voxels/PicoGKVoxelHandle.cs verbatim per the
// parallel-pillar policy in ADR-026 §2: each pillar owns its own concrete
// IVoxelHandle implementation so the rocket pipeline never depends on
// electric-propulsion PicoGK code and vice versa. All wrappers implement
// the same Voxelforge.IVoxelHandle marker interface from Voxelforge.Core.

using PicoGK;
using Voxelforge;

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Concrete <see cref="IVoxelHandle"/> implementation for the
/// electric-propulsion pillar that wraps a <c>PicoGK.Voxels</c>.
/// Constructed inside <c>ResistojetVoxelBuilder</c> (Sprint E.3) and
/// stored on result records.
/// </summary>
public sealed class PicoGKVoxelHandle : IVoxelHandle
{
    public Voxels Inner { get; }

    public PicoGKVoxelHandle(Voxels inner) { Inner = inner; }
}

/// <summary>
/// Internal extension that unwraps an <see cref="IVoxelHandle"/> as the
/// underlying PicoGK.Voxels. Internal so consumer-side AsPicoGK
/// extensions don't clash at import sites; <see cref="InternalsVisibleToAttribute"/>
/// exposes it to <c>Voxelforge.ElectricPropulsion.StlExporter</c> +
/// <c>Voxelforge.ElectricPropulsion.Tests</c>.
/// </summary>
internal static class VoxelHandleExtensions
{
    /// <summary>
    /// Unwrap an <see cref="IVoxelHandle"/> as the underlying PicoGK.Voxels.
    /// Throws <see cref="System.InvalidCastException"/> if the handle is
    /// not an electric-propulsion-pillar <see cref="PicoGKVoxelHandle"/>.
    /// </summary>
    internal static Voxels AsPicoGK(this IVoxelHandle handle)
        => ((PicoGKVoxelHandle)handle).Inner;
}
