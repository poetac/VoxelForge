// IAerospikeBuilder.cs — Abstraction over the static
// `AerospikeBuilder.BuildPhysicsOnly` / `BuildLinearPhysicsOnly` calls
// that AerospikeOptimization.BuildAndEvaluate (moving to Core in
// Phase 2 of ADR-021) consults to score aerospike physics without
// dragging the Voxels project + PicoGK into Core.

namespace Voxelforge.Geometry;

/// <summary>
/// Sprint A-3 Phase 2 / ADR-021 (2026-04-30): seam over the
/// physics-only aerospike builders. Voxels-side adapter wraps both
/// <c>AerospikeBuilder.BuildPhysicsOnly</c> (axisymmetric) and
/// <c>BuildLinearPhysicsOnly</c> (Sprint-26 linear plug) and dispatches
/// on <see cref="AerospikeSpec.IsLinear"/>.
/// <para>
/// Headless callers (bench-SA non-aerospike topologies, unit tests,
/// the <c>voxelforge-eval</c> subprocess running non-aerospike presets)
/// pass <c>null</c>. The orchestrator skips the aerospike branch when
/// the design's <c>ChannelTopology</c> is not aerospike, so a null
/// builder is observed only on aerospike presets which always run
/// through the App side anyway.
/// </para>
/// </summary>
public interface IAerospikeBuilder
{
    /// <summary>
    /// Run the aerospike physics-only build for a sized
    /// <see cref="AerospikeSpec"/>. Dispatches to the axisymmetric or
    /// linear path based on <see cref="AerospikeSpec.IsLinear"/>.
    /// </summary>
    AerospikeBuildResult BuildPhysicsOnly(AerospikeSpec spec);
}
