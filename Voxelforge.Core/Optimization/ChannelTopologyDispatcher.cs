// tech-debt T5 (2026-04-28): single source of truth for per-topology
// dispatch decisions.
//
// Background. Before this file landed, the codebase had ~20 sites
// branching on ChannelTopology directly: voxel-builder selection,
// scoring-path selection, channel-phase skip, helix-pitch consumption,
// aerospike sidecar population. Adding a new topology meant
// finger-grepping for every comparison — silent fall-through bugs
// like the 2026-04-25 Aerospike-preview regression (tracked in
// ChannelTopologyHelpers.cs's header comment) were the result.
//
// Design. This dispatcher offers two layers:
//
//   1. A `Family` enum that coarsens the eight ChannelTopology values
//      into the five families that the rest of the codebase actually
//      branches on (Bell / AxialHelical / Tpms / Aerospike /
//      LinearAerospike). Adding a new topology forces an arm in
//      `ClassifyFamily` because of the throw-on-default — no new
//      enum value can sneak through unclassified.
//
//   2. Predicate helpers that consolidate the recurring patterns:
//        IsAerospike            — Aerospike OR LinearAerospike
//        IsAerospikeAxisymmetric — just Aerospike (voxel-builder check)
//        IsTpms                 — any TPMS variant
//        HasChannelPhase        — same shape as ChannelTopologyHelpers.IsChannelStyle
//        ShouldSkipChannelGeneration — inverse of HasChannelPhase + None
//
// Sites that check ONE specific variant (Helical-only pitch angle,
// Axial-only sensor-boss clash) deliberately stay on raw equality —
// no helper exists because no family abstraction would help them.
// Those are flagged in the call-site comments.

namespace Voxelforge.Optimization;

/// <summary>
/// Centralized dispatch for <see cref="ChannelTopology"/> values.
/// Adding a new topology means adding an arm to <see cref="ClassifyFamily"/>
/// and to <see cref="ChannelTopologyHelpers.IsChannelStyle"/> —
/// the throw-on-default behaviour in both forces a deliberate
/// classification decision.
/// </summary>
public static class ChannelTopologyDispatcher
{
    /// <summary>
    /// Coarse classification of <see cref="ChannelTopology"/> into the
    /// five families the rest of the codebase actually branches on.
    /// Picking a family here corresponds to "what voxel pipeline does
    /// this design route through" + "what scoring axes apply."
    /// </summary>
    public enum Family
    {
        /// <summary>
        /// No channel voxelization phase. Bell-chamber shell only.
        /// (<see cref="ChannelTopology.None"/>.)
        /// </summary>
        Bell,

        /// <summary>
        /// Discrete cooling channels (axial straight or helical pitched)
        /// added to a baseline bell-chamber. (<see cref="ChannelTopology.Axial"/>,
        /// <see cref="ChannelTopology.Helical"/>.)
        /// </summary>
        AxialHelical,

        /// <summary>
        /// Triply-Periodic Minimal Surface lattice acting as the channel
        /// topology. (<see cref="ChannelTopology.TpmsGyroid"/>,
        /// <see cref="ChannelTopology.TpmsSchwarzP"/>,
        /// <see cref="ChannelTopology.TpmsSchwarzD"/>.)
        /// </summary>
        Tpms,

        /// <summary>
        /// Axisymmetric aerospike plug-nozzle replacement. The chamber
        /// + bell + plug are dispatched to <c>AerospikeBuilder.Build</c>;
        /// the bell-chamber voxel path doesn't run.
        /// (<see cref="ChannelTopology.Aerospike"/>.)
        /// </summary>
        Aerospike,

        /// <summary>
        /// Extruded-rectangular linear aerospike. Same conceptual
        /// replacement as <see cref="Aerospike"/> but linear; today
        /// physics-only, no voxel builder yet.
        /// (<see cref="ChannelTopology.LinearAerospike"/>.)
        /// </summary>
        LinearAerospike,

        /// <summary>
        /// Expansion-deflection nozzle. The outer bell uses the standard
        /// regen-bell pipeline with the cowl-inflated throat radius;
        /// the inner plug is geometry-only in the first-pass model.
        /// Altitude-compensation comes from the annular geometry rather
        /// than an open plug.
        /// (<see cref="ChannelTopology.ExpansionDeflection"/>.)
        /// </summary>
        ExpanderDeflector,

        /// <summary>
        /// SIMP density-field topology-optimized channel routing (OOB-2 / ADR-024).
        /// Per-station channel count redistributed by the OC optimizer; same
        /// total channel volume as the baseline axial schedule.
        /// (<see cref="ChannelTopology.TopologyOptimized"/>.)
        /// </summary>
        TopologyOptimized,

        /// <summary>
        /// Ablative + regen hybrid throat (OOB-14 / issue #341).
        /// Regen channels cover chamber + divergence; an ablative liner
        /// occupies the throat band. The bell voxel pipeline runs as
        /// normal; the ablative geometry is added as a liner shell.
        /// (<see cref="ChannelTopology.AblativeThroat"/>.)
        /// </summary>
        AblativeThroat,
    }

    /// <summary>
    /// Maps every <see cref="ChannelTopology"/> value to its
    /// <see cref="Family"/>. Throws on unhandled values so a new enum
    /// addition forces a deliberate classification decision rather
    /// than silently joining whatever family the default would pick.
    /// </summary>
    public static Family ClassifyFamily(ChannelTopology topology) => topology switch
    {
        ChannelTopology.None            => Family.Bell,
        ChannelTopology.Axial           => Family.AxialHelical,
        ChannelTopology.Helical         => Family.AxialHelical,
        ChannelTopology.TpmsGyroid      => Family.Tpms,
        ChannelTopology.TpmsSchwarzP    => Family.Tpms,
        ChannelTopology.TpmsSchwarzD    => Family.Tpms,
        ChannelTopology.Aerospike           => Family.Aerospike,
        ChannelTopology.LinearAerospike     => Family.LinearAerospike,
        ChannelTopology.ExpansionDeflection  => Family.ExpanderDeflector,
        ChannelTopology.TopologyOptimized    => Family.TopologyOptimized,
        ChannelTopology.AblativeThroat       => Family.AblativeThroat,
        _ => throw new System.ArgumentOutOfRangeException(nameof(topology),
                $"Unhandled ChannelTopology value '{topology}' — add it to "
              + "ChannelTopologyDispatcher.ClassifyFamily."),
    };

    /// <summary>
    /// True for any aerospike variant (axisymmetric or linear).
    /// Use this when scoring or feasibility code branches on
    /// "is this design a plug-nozzle?" — the linear and axisymmetric
    /// variants share the Aerospike sidecar population and a common
    /// set of feasibility gates.
    /// </summary>
    public static bool IsAerospike(ChannelTopology topology)
        => ClassifyFamily(topology) is Family.Aerospike or Family.LinearAerospike;

    /// <summary>
    /// True only for the axisymmetric aerospike. Use this where the
    /// voxel pipeline matters: <c>AerospikeBuilder.Build</c> only
    /// handles the axisymmetric form; the linear variant currently
    /// has only <c>AerospikeBuilder.BuildLinearPhysicsOnly</c>.
    /// </summary>
    public static bool IsAerospikeAxisymmetric(ChannelTopology topology)
        => ClassifyFamily(topology) is Family.Aerospike;

    /// <summary>
    /// True only for the expansion-deflection nozzle topology. The outer
    /// bell runs the standard regen pipeline with a cowl-inflated throat
    /// radius; the inner plug is geometry-only in the first-pass model.
    /// Use this where code needs to adjust the throat radius or
    /// apply E-D-specific gates.
    /// </summary>
    public static bool IsExpansionDeflection(ChannelTopology topology)
        => ClassifyFamily(topology) is Family.ExpanderDeflector;

    /// <summary>
    /// True for any TPMS lattice variant. Use this when AutoSeeder /
    /// pre-screens / scoring code applies the same logic to all three
    /// TPMS topologies (cell edge sizing, strut-thickness floor, etc.).
    /// </summary>
    public static bool IsTpms(ChannelTopology topology)
        => ClassifyFamily(topology) is Family.Tpms;

    /// <summary>
    /// True iff the topology has a discrete channel-voxelization phase
    /// (i.e. axial / helical / TPMS variants, or the E-D outer-bell
    /// regen jacket). False for the plain bell-only path (None) and for
    /// full-geometry replacements (aerospike). Equivalent to
    /// <see cref="ChannelTopologyHelpers.IsChannelStyle"/>; kept here
    /// for symmetry with the other dispatcher predicates so call sites
    /// don't need to import two helper modules.
    /// </summary>
    public static bool HasChannelPhase(ChannelTopology topology)
        => ClassifyFamily(topology) is Family.AxialHelical or Family.Tpms
                                    or Family.ExpanderDeflector or Family.TopologyOptimized
                                    or Family.AblativeThroat;

    /// <summary>
    /// True only for <see cref="ChannelTopology.TopologyOptimized"/>.
    /// Use this at call sites that invoke the SIMP channel-routing solver
    /// or apply topology-optimized routing metadata (OOB-2 / ADR-024).
    /// </summary>
    public static bool IsTopologyOptimized(ChannelTopology topology)
        => ClassifyFamily(topology) is Family.TopologyOptimized;

    /// <summary>
    /// True only for <see cref="ChannelTopology.AblativeThroat"/>.
    /// Use this at call sites that need to apply ablative-zone routing
    /// (OOB-14 / issue #341).
    /// </summary>
    public static bool IsAblativeThroat(ChannelTopology topology)
        => ClassifyFamily(topology) is Family.AblativeThroat;

    /// <summary>
    /// True iff the design has no channel-voxelization step at all
    /// (i.e. bell-only — <see cref="ChannelTopology.None"/>). Distinct
    /// from <see cref="HasChannelPhase"/>'s inverse: aerospike variants
    /// also lack channels but have a different geometry pipeline.
    /// </summary>
    public static bool ShouldSkipChannelGeneration(ChannelTopology topology)
        => ClassifyFamily(topology) is Family.Bell;
}
