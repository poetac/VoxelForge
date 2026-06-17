// ChannelTopologyHelpers.cs — predicates over ChannelTopology values.
//
// Sprint fix (2026-04-25): Aerospike preview bug. The Fast-Preview-Mode
// optimization in `Program.cs.RegenerateForManualMode` cloaks the design's
// ChannelTopology to None to skip ~84 % of build time at 0.4 mm voxel.
// That optimization only makes sense for CHANNEL-STYLE topologies (Axial,
// Helical, TPMS variants) — for full-geometry topologies (Aerospike,
// LinearAerospike) the channel-voxelize phase doesn't run anyway, so
// cloaking them silently produces a bell-chamber shell instead of the
// requested aerospike. This helper makes the distinction explicit and
// testable, so future enum additions force a deliberate classification.

namespace Voxelforge.Optimization;

public static class ChannelTopologyHelpers
{
    /// <summary>
    /// Returns true when the topology represents discrete cooling channels
    /// (or a TPMS lattice acting as channels) added to a baseline bell-
    /// chamber geometry. Such topologies have a separate channel-voxelize
    /// phase that <c>FastPreviewMode</c> can legitimately skip.
    ///
    /// Returns false for:
    ///   - <see cref="ChannelTopology.None"/>: no channels at all.
    ///   - <see cref="ChannelTopology.Aerospike"/>: full-geometry replacement
    ///     dispatched to <c>AerospikeBuilder.Build</c>; no channel phase.
    ///   - <see cref="ChannelTopology.LinearAerospike"/>: same as Aerospike
    ///     but extruded; dispatches to <c>BuildLinearPhysicsOnly</c>.
    /// </summary>
    public static bool IsChannelStyle(this ChannelTopology topology) => topology switch
    {
        ChannelTopology.Axial         => true,
        ChannelTopology.Helical       => true,
        ChannelTopology.TpmsGyroid    => true,
        ChannelTopology.TpmsSchwarzP  => true,
        ChannelTopology.TpmsSchwarzD  => true,
        ChannelTopology.None                => false,
        ChannelTopology.Aerospike           => false,
        ChannelTopology.LinearAerospike     => false,
        // E-D nozzle: the outer bell regen channel phase DOES run (it's a
        // standard bell with a cowl-inflated throat), so Fast Preview
        // cloaking to None is incorrect — return true so Fast Preview
        // knows to skip cloaking this topology.
        ChannelTopology.ExpansionDeflection  => true,
        // TopologyOptimized: has a channel phase; Fast Preview cloaking to None
        // is incorrect — return true so Fast Preview skips cloaking.
        ChannelTopology.TopologyOptimized    => true,
        // AblativeThroat: regen channels cover chamber + divergence; the
        // ablative liner occupies the throat band only. Channel phase runs —
        // Fast Preview cloaking to None is incorrect.
        ChannelTopology.AblativeThroat       => true,
        // Default deliberate: a future enum addition forces a classification
        // decision rather than silently inheriting "false" (which would let
        // it skip Fast Preview cloaking that may legitimately apply).
        _ => throw new System.ArgumentOutOfRangeException(nameof(topology),
                $"Unhandled ChannelTopology value '{topology}' — add it to ChannelTopologyHelpers.IsChannelStyle"),
    };
}
