// AerospikeBuilderAdapter.cs — Voxels-side bridge implementing
// `Voxelforge.Geometry.IAerospikeBuilder` over the static
// `AerospikeBuilder.BuildPhysicsOnly` / `BuildLinearPhysicsOnly`
// methods. Sprint A-3 Phase 2 / ADR-021 (2026-04-30).

namespace Voxelforge.Geometry;

/// <summary>
/// Default <see cref="IAerospikeBuilder"/> implementation. Dispatches
/// to <see cref="AerospikeBuilder.BuildLinearPhysicsOnly"/> when
/// <see cref="AerospikeSpec.IsLinear"/> is true; otherwise to
/// <see cref="AerospikeBuilder.BuildPhysicsOnly"/>.
/// </summary>
public sealed class AerospikeBuilderAdapter : IAerospikeBuilder
{
    /// <inheritdoc />
    public AerospikeBuildResult BuildPhysicsOnly(AerospikeSpec spec)
        => spec.IsLinear
            ? AerospikeBuilder.BuildLinearPhysicsOnly(spec)
            : AerospikeBuilder.BuildPhysicsOnly(spec);
}
