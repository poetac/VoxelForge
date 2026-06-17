// AerospikeOptimization.cs — Sprint 2b (2026-04-22):
// Bridge between the SA-facing (OperatingConditions, RegenChamberDesign)
// pair and the aerospike geometry / feasibility pipeline in
// Voxelforge.Geometry.
//
// What this provides
// ──────────────────
//   • ToSpec(cond, design) — maps the SA-side record pair into an
//     AerospikeSpec the geometry builder consumes. Pure, deterministic,
//     no PicoGK. Only accepts designs whose ChannelTopology is Aerospike
//     (other topologies go through the regen pipeline).
//   • BuildAndEvaluate(cond, design) — end-to-end convenience wrapping
//     AerospikeBuilder.BuildPhysicsOnly (Sprint 2a) and
//     AerospikeFeasibility.Evaluate into a single call. Use this in
//     tests and anywhere the SA loop needs an aerospike score path that
//     works without an active PicoGK Library.
//
// Why this exists
// ───────────────
// The SA optimizer's Pack / Unpack / GenerateWith pipeline is keyed to
// RegenGenerationResult and the regen-chamber contour. Aerospike
// designs today ride on the same RegenChamberDesign record (topology =
// Aerospike) but their physics live on a different surface
// (AerospikeBuildResult + AerospikeThermalResult). Until a unifying
// IEngineResult supertype lands, keep the two scoring paths parallel:
// regen via RegenChamberOptimization.GenerateWith, aerospike via this
// module. Callers branch on design.ChannelTopology.
//
// Scope boundary vs later sprints
// ───────────────────────────────
//   • Sprint 2b (this commit) — mapping + feasibility-only evaluation.
//     No aerospike scoring terms in the SA objective function yet.
//   • Sprint 3+ — GenerateWith dispatch inside RegenChamberOptimization
//     when topology = Aerospike (so a single SA loop covers both
//     paths), aerospike-specific scoring terms (plug mass, plume
//     altitude-compensation credit, etc.), and integration with the
//     monolithic-engine pipeline.

using Voxelforge.Geometry;

namespace Voxelforge.Optimization;

/// <summary>
/// Sprint 2b: aerospike-path bridge between (OperatingConditions,
/// RegenChamberDesign) and the aerospike geometry / feasibility
/// pipeline. All methods are pure, deterministic, thread-safe, and
/// do NOT touch PicoGK — safe to call from xUnit.
/// </summary>
public static class AerospikeOptimization
{
    /// <summary>
    /// Map the SA-facing (cond, design) pair into an
    /// <see cref="AerospikeSpec"/>. Throws
    /// <see cref="System.ArgumentException"/> when the design is not on
    /// the aerospike topology — callers should branch on
    /// <see cref="RegenChamberDesign.ChannelTopology"/> and route
    /// regen-family designs through
    /// <see cref="RegenChamberOptimization.GenerateWith"/> instead.
    /// <para>
    /// Sprint 15 / Track G (2026-04-22): the four
    /// <see cref="RegenChamberDesign.IncludeAerospikeRegenCooling"/> /
    /// <c>AerospikePlugChannel*</c> /
    /// <see cref="RegenChamberDesign.AerospikePlugWallThickness_mm"/>
    /// opt-in fields on the design now forward into
    /// <see cref="AerospikeSpec.IncludeRegenChannels"/> + the channel
    /// geometry. Default false preserves the geometry-only path
    /// (Sprint 11 Track F's scoring dispatch silently falls back to the
    /// bell-chamber thermal numbers); turning the opt-in on populates
    /// <see cref="AerospikeBuildResult.Thermal"/> so the
    /// <c>AEROSPIKE_PLUG_WALL_TEMP</c> gate fires and the SA score uses
    /// real plug-channel coolant data.
    /// </para>
    /// </summary>
    public static AerospikeSpec ToSpec(OperatingConditions cond, RegenChamberDesign design)
    {
        bool axisymmetric = design.ChannelTopology == ChannelTopology.Aerospike;
        bool linear       = design.ChannelTopology == ChannelTopology.LinearAerospike;
        if (!axisymmetric && !linear)
            throw new System.ArgumentException(
                $"AerospikeOptimization.ToSpec requires design.ChannelTopology = Aerospike "
              + $"or LinearAerospike; got {design.ChannelTopology}. Route regen-family designs "
              + $"through RegenChamberOptimization.GenerateWith instead.",
                nameof(design));

        return new AerospikeSpec(
            Thrust_N:                 cond.Thrust_N,
            ChamberPressure_Pa:       cond.ChamberPressure_Pa,
            ExpansionRatio:           design.ExpansionRatio,
            PlugLengthRatio:          design.PlugLengthRatio,
            PropellantPair:           cond.PropellantPair,
            MixtureRatio:             cond.MixtureRatio,
            CStarEfficiency:          cond.CStarEfficiency,
            // The aerospike pipeline treats the chamber as a single
            // outer shell (no regen jacket today), so map the design's
            // outer-jacket thickness in.
            OuterShellThickness_mm:   design.OuterJacketThickness_mm,
            // Sprint 15 / Track G (2026-04-22): plug-channel regen
            // cooling opt-in. When IncludeAerospikeRegenCooling is true,
            // AerospikeBuilder.BuildPhysicsOnly invokes the
            // AerospikePlugCooling solver and populates
            // AerospikeBuildResult.Thermal — closing the feature loop
            // Sprint 11 Track F opened on the scoring side.
            IncludeRegenChannels:     design.IncludeAerospikeRegenCooling,
            PlugChannelCount:         design.AerospikePlugChannelCount,
            PlugChannelWidth_mm:      design.AerospikePlugChannelWidth_mm,
            PlugChannelDepth_mm:      design.AerospikePlugChannelDepth_mm,
            PlugWallThickness_mm:     design.AerospikePlugWallThickness_mm,
            CoolantInletTemp_K:       cond.CoolantInletTemp_K,
            CoolantInletPressure_Pa:  cond.CoolantInletPressure_Pa,
            WallMaterialIndex:        cond.WallMaterialIndex,
            // Sprint 7 Track A (2026-04-22): forward the SA-visible
            // injector pattern. AerospikeBuilder.BuildPhysicsOnly sizes
            // the pattern and populates AerospikeBuildResult.InjectorSizing
            // so AerospikeFeasibility.Evaluate can fire
            // AEROSPIKE_ELEMENT_CLEARANCE when elements don't fit.
            InjectorPattern:          design.InjectorElementPattern,
            // Sprint 9 Track C (2026-04-22): SA-tunable aerospike
            // chamber contraction ratio. Default 6.0 matches the
            // pre-Sprint-9 hardcoded value.
            ChamberContractionRatio:  design.AerospikeContractionRatio,
            // Sprint 26 (2026-04-23): linear-aerospike opt-in. When
            // design.ChannelTopology = LinearAerospike, flip the spec
            // flag so BuildAndEvaluate dispatches to
            // AerospikeBuilder.BuildLinearPhysicsOnly. Transverse width
            // forwards from the dedicated design field.
            IsLinear:                 linear,
            LinearPlugWidth_mm:       design.LinearAerospikePlugWidth_mm,
            // PH-36 + PH-35 aerospike-face follow-ons (2026-04-29 — closes
            // #233 + #234). Forward the per-pair oxidizer T + face-material
            // T-limit override from OperatingConditions so the aerospike
            // injector-face thermal model gets the same plumbing the
            // bell-chamber path got in PRs #227 + #229.
            OxidizerInletTemp_K:             cond.OxidizerInletTemp_K,
            InjectorFaceMaxTemp_K_Override:  cond.InjectorFaceMaxTemp_K_Override);
    }

    /// <summary>
    /// End-to-end convenience: map (cond, design) → AerospikeSpec →
    /// <see cref="AerospikeBuilder.BuildPhysicsOnly"/> →
    /// <see cref="AerospikeFeasibility.Evaluate"/>. Returns the build
    /// result and the feasibility check side-by-side so SA / UI / CLI
    /// callers can surface both without re-running the geometry.
    /// <para>
    /// Does not touch PicoGK — safe to call from xUnit or any non-task
    /// thread (unlike the voxel-producing
    /// <see cref="AerospikeBuilder.Build"/> path which must run on the
    /// PicoGK task thread).
    /// </para>
    /// </summary>
    public static (AerospikeBuildResult Build, FeasibilityGateResult Feasibility) BuildAndEvaluate(
        OperatingConditions cond, RegenChamberDesign design,
        // Sprint A-3 Phase 2 / ADR-021 (2026-04-30): IAerospikeBuilder
        // seam so this method can move into Core (which can't reference
        // the Voxels-side AerospikeBuilder static directly). Null falls
        // App callers pass `new AerospikeBuilderAdapter()` (in Voxels);
        // headless / unit-test callers may pass null and BuildAndEvaluate
        // throws if aerospike physics is requested without a builder.
        IAerospikeBuilder? aerospikeBuilder = null)
    {
        if (aerospikeBuilder is null)
            throw new System.ArgumentNullException(nameof(aerospikeBuilder),
                "AerospikeOptimization.BuildAndEvaluate requires an IAerospikeBuilder. "
              + "App callers: pass new Voxelforge.Geometry.AerospikeBuilderAdapter().");
        var spec  = ToSpec(cond, design);
        // Sprint 26 (2026-04-23): dispatch to the linear builder when
        // the spec is flagged linear. Physics-only (no voxelisation) on
        // both paths. Sprint A-3 Phase 2: routed through the seam (the
        // adapter dispatches on spec.IsLinear internally).
        var build = aerospikeBuilder.BuildPhysicsOnly(spec);
        var feas  = AerospikeFeasibility.Evaluate(build, cond.WallMaterialIndex);
        return (build, feas);
    }
}
