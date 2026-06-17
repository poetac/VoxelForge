// RegenObjective.cs — Slice 3 of the IObjective decoupling
// (2026-04-28). Concrete IObjective wrapper for the regen-rocket
// engine family. Lives in App because it depends on
// RegenChamberOptimization.GenerateWith / Evaluate (App-side
// orchestrator). Threadsafe + deterministic per the IObjective
// contract.
//
// Why one objective covers all rocket topologies:
//   The bell, dual-bell, axisymmetric aerospike, linear aerospike,
//   axial / helical / TPMS channel topologies — every variant the
//   project ships today — all dispatch through
//   RegenChamberOptimization.GenerateWith via the
//   ChannelTopology / IncludeDualBell / IncludeAerospikeRegenCooling
//   switches. The score function itself is uniform: every topology
//   produces a RegenScoreResult via RegenChamberOptimization.Evaluate.
//   So one IObjective (this one) covers the entire current rocket
//   surface — separate engine-family objectives only become necessary
//   when air-breathing / power-gen pillars start.
//
// Design-variable sourcing:
//   The 31-dim SA vector is reflected from
//   [SaDesignVariable]-tagged properties on RegenChamberDesign +
//   InjectorPattern via DesignVariableRegistry. RegenObjective just
//   projects DesignVariableRegistry.DescriptorsForMany into
//   DesignVariableInfo[] for the IObjective.Variables surface.

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Injector;

namespace Voxelforge.Optimization;

/// <summary>
/// IObjective implementation for the regen-rocket engine family.
/// Wraps the App-side <see cref="RegenChamberOptimization.GenerateWith"/>
/// + <see cref="RegenChamberOptimization.Evaluate"/> pipeline.
/// <para>
/// Construct once with a baseline <see cref="RegenChamberDesign"/> +
/// <see cref="OperatingConditions"/>; the optimizer (or any
/// IObjective consumer like CMA-ES, NSGA-II, or BoTorch via the
/// subprocess oracle) calls <see cref="Evaluate"/> repeatedly with
/// candidate vectors. Each candidate is unpacked into a derived
/// <c>RegenChamberDesign</c> via the SA registry, the physics is run,
/// and a <see cref="RegenScoreResult"/> is produced.
/// </para>
/// <para>
/// Defaults to the cheap "physics-only" path
/// (<c>skipVoxelGeometry: true</c>, <c>skipMfgAnalysis: true</c>) —
/// that's what the SA hot path needs and what the subprocess oracle
/// ships. Callers that want voxel geometry + manufacturing analysis
/// (UI preview, final-best regeneration) construct with the explicit
/// flags flipped.
/// </para>
/// </summary>
public sealed class RegenObjective : IObjective
{
    private readonly OperatingConditions _conditions;
    private readonly RegenChamberDesign _baseline;
    private readonly ScoringProfile _profile;
    private readonly DesignVariableInfo[] _variables;
    private readonly bool _skipVoxelGeometry;
    private readonly bool _skipMfgAnalysis;
    private readonly double _voxelSize_mm;

    /// <inheritdoc />
    public int DimensionCount => _variables.Length;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _variables;

    /// <summary>
    /// Operating conditions the design is evaluated against (immutable
    /// across all <see cref="Evaluate"/> calls on this objective).
    /// </summary>
    public OperatingConditions Conditions => _conditions;

    /// <summary>
    /// Baseline design that <see cref="Evaluate"/> calls
    /// <see cref="RegenChamberOptimization.Unpack"/> against. The
    /// categorical state on the baseline (cycle, propellant pair,
    /// channel topology, injector-pattern presence) gates which SA
    /// dims actually apply (see ADR-010 + <see cref="SaGate"/>).
    /// </summary>
    public RegenChamberDesign Baseline => _baseline;

    /// <summary>
    /// Scoring profile used by every <see cref="Evaluate"/> call on this
    /// objective. Passed through to
    /// <see cref="RegenChamberOptimization.Evaluate(RegenGenerationResult, ScoringProfile)"/>;
    /// each objective instance is pinned to one profile so SA runs are
    /// reproducible against an explicit weighting choice.
    /// </summary>
    public ScoringProfile Profile => _profile;

    /// <summary>
    /// Construct an objective that wraps the regen-rocket pipeline at
    /// the given baseline + conditions + scoring profile.
    /// </summary>
    /// <param name="conditions">Operating conditions (thrust, Pc, MR, etc.).</param>
    /// <param name="baseline">
    /// Baseline design. Categorical state (cycle, propellant pair,
    /// topology, injector pattern presence) is preserved across every
    /// <see cref="Evaluate"/> call; only SA-tunable dims are perturbed.
    /// </param>
    /// <param name="profile">
    /// Scoring profile (weights for wall T, ΔP, mass, etc.) used by
    /// every <see cref="Evaluate"/> call on this objective. Required —
    /// callers must pick a profile explicitly rather than relying on a
    /// hidden default.
    /// </param>
    /// <param name="skipVoxelGeometry">
    /// When <c>true</c> (default), <see cref="RegenChamberOptimization.GenerateWith"/>
    /// runs in physics-only mode — no PicoGK voxel build. Required for
    /// the SA hot path and the subprocess oracle. Set to <c>false</c>
    /// only for UI-preview / final-best regeneration paths.
    /// </param>
    /// <param name="skipMfgAnalysis">
    /// When <c>true</c> (default), skips manufacturing / printability
    /// analysis. Recommended for the SA hot path; set to <c>false</c>
    /// when the printability gates need to fire (final-best, report).
    /// </param>
    /// <param name="voxelSize_mm">
    /// Voxel size for the physics path. <c>0.0</c> (default) tells
    /// GenerateWith to pick a default; ignored when
    /// <paramref name="skipVoxelGeometry"/> is <c>true</c>.
    /// </param>
    public RegenObjective(
        OperatingConditions conditions,
        RegenChamberDesign baseline,
        ScoringProfile profile,
        bool skipVoxelGeometry = true,
        bool skipMfgAnalysis = true,
        double voxelSize_mm = 0.0)
    {
        _conditions        = conditions ?? throw new ArgumentNullException(nameof(conditions));
        _baseline          = baseline   ?? throw new ArgumentNullException(nameof(baseline));
        _profile           = profile    ?? throw new ArgumentNullException(nameof(profile));
        _skipVoxelGeometry = skipVoxelGeometry;
        _skipMfgAnalysis   = skipMfgAnalysis;
        _voxelSize_mm      = voxelSize_mm;

        // Project DesignVariableRegistry descriptors into the
        // engine-family-agnostic DesignVariableInfo shape. The registry
        // already orders by .Index ascending and validates contiguity;
        // we just rename + drop the SA-attribute-specific fields.
        var descriptors = DesignVariableRegistry.DescriptorsForMany(
            typeof(RegenChamberDesign), typeof(InjectorPattern));
        _variables = new DesignVariableInfo[descriptors.Length];
        for (int i = 0; i < descriptors.Length; i++)
        {
            var d = descriptors[i];
            _variables[i] = new DesignVariableInfo(d.MemberName, d.Min, d.Max);
        }
    }

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        if (vector.Length != _variables.Length)
            throw new ArgumentException(
                $"vector length {vector.Length} != DimensionCount {_variables.Length}",
                nameof(vector));

        // Span overload of Unpack reads directly from the candidate
        // vector — no per-call double[] materialisation. Eliminates
        // ~250 B × ~5 M evaluations ≈ 1.2 GB of Gen0 garbage per SA
        // session on the rocket hot path (audit 12-perf §1.1).
        var design = RegenChamberOptimization.Unpack(vector, _baseline);

        // Sprint 0 / Wave 1 (2026-05-05): pre-screen short-circuit. The
        // SA hot path used to do this inline; folding it in here lets
        // the multi-chain session consume IObjective uniformly while
        // preserving the ~50-200 ms savings on infeasible candidates.
        var preScreen = FeasibilityGate.PreScreen(_conditions, design);
        if (preScreen is not null)
        {
            return new EvaluationResult(
                Score:                   double.PositiveInfinity,
                Violations:              new[] { preScreen },
                EngineSpecificBreakdown: null);
        }

        return ScoreDesign(
            _conditions, design, _profile,
            skipVoxelGeometry: _skipVoxelGeometry,
            skipMfgAnalysis:   _skipMfgAnalysis,
            voxelSize_mm:      _voxelSize_mm);
    }

    /// <summary>
    /// Score a specific <see cref="RegenChamberDesign"/> directly,
    /// without the SA <c>Pack</c> / <c>Unpack</c> round-trip that
    /// <see cref="Evaluate"/> performs. Equivalent to the legacy
    /// (<see cref="RegenChamberOptimization.GenerateWith"/> →
    /// <see cref="RegenChamberOptimization.Evaluate"/>) path.
    /// <para>
    /// Used by the <c>voxelforge-eval</c> subprocess oracle to
    /// preserve exact "evaluate this design as-given" semantics
    /// while still routing through the IObjective abstraction's
    /// <see cref="EvaluationResult"/> shape — no Pack/Unpack
    /// clamping that would silently mutate hand-crafted inputs.
    /// </para>
    /// </summary>
    public static EvaluationResult ScoreDesign(
        OperatingConditions conditions,
        RegenChamberDesign design,
        ScoringProfile profile,
        bool skipVoxelGeometry = true,
        bool skipMfgAnalysis = true,
        double voxelSize_mm = 0.0)
    {
        if (conditions is null) throw new ArgumentNullException(nameof(conditions));
        if (design     is null) throw new ArgumentNullException(nameof(design));
        if (profile    is null) throw new ArgumentNullException(nameof(profile));

        var gen = RegenChamberOptimization.GenerateWith(
            conditions, design,
            voxelSize_mm:      voxelSize_mm,
            skipVoxelGeometry: skipVoxelGeometry,
            skipMfgAnalysis:   skipMfgAnalysis);
        var score = RegenChamberOptimization.Evaluate(gen, profile);
        return new EvaluationResult(
            Score:                   score.TotalScore,
            Violations:              score.FeasibilityViolations,
            EngineSpecificBreakdown: score);
    }
}
