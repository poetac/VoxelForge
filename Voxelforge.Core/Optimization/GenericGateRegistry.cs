// GenericGateRegistry.cs — multi-family gate registry (additive overlay, 2026-05-05).
//
// The original GateRegistry.cs (ADR-019, PR #281, 2026-04-29) is rocket-only:
// FeasibilityGateDescriptor.Emit is typed Action<RegenGenerationResult, ...>.
// The "rule of three" deferral in ADR-019 + AirbreathingFeasibility.cs:5-10
// kept the registry rocket-shaped while air-breathing gates inlined directly
// in AirbreathingFeasibility.Evaluate. With rocket + air-breathing both
// concrete (post-Sprint-A8 SteamTurbine, 2026-05-05), the rule-of-three
// condition is satisfied; this file introduces the generic registry shape
// non-rocket pillars register against.
//
// Why additive (parallel generic) rather than migrating rocket:
//   The rocket-side FeasibilityGateDescriptor + GateRegistry are in
//   PublicAPI.Shipped.txt with ~30 entries. Migrating them to generic would
//   require *REMOVED* entries + rewrites of RocketGates.cs / FeasibilityGate
//   .cs / GateExplainer.cs / RocketEngine.cs / 6 test files, plus careful
//   verification that GateOrderingSnapshotTests stays byte-identical. The
//   additive shape — rocket continues to use the non-generic types, new
//   pillars use the generic types — gives the multi-family registry benefit
//   with zero rocket blast radius. A future consolidation sprint (deferred)
//   may unify them once it's worth the migration cost.
//
// Additive sibling to GateRegistry.cs (ADR-019). Rocket continues to use the
// non-generic types unchanged; non-rocket pillars register against this
// generic shape via per-pillar static-singleton wrappers.

using System;
using System.Collections.Generic;

namespace Voxelforge.Optimization;

/// <summary>
/// Generic counterpart to <see cref="FeasibilityGateDescriptor"/>. Used by
/// non-rocket engine pillars (air-breathing, future marine, future electric)
/// that register against a <see cref="GateRegistry{TResult}"/> typed to
/// their own input shape.
/// </summary>
/// <typeparam name="TResult">
/// The pillar's gate-evaluator input type. For air-breathing, this is the
/// internal <c>AirbreathingGateInput</c> shim record bundling design +
/// conditions + station map + diagnostics. The shim shape lets gate
/// predicates run BEFORE the final <c>AirbreathingResult</c> is built, the
/// same pattern <c>AirbreathingFeasibility.Evaluate</c> uses today.
/// </typeparam>
/// <param name="Id">
/// Stable machine-readable key (e.g. <c>"PULSEJET_BLOWOUT_LEAN"</c>). Matches
/// the <see cref="FeasibilityViolation.ConstraintId"/> the predicate emits.
/// Per-registry uniqueness; <see cref="GateRegistry{TResult}.Register"/>
/// throws on duplicates.
/// </param>
/// <param name="Severity">
/// Classification — <see cref="GateSeverity.Hard"/> rejects the candidate in
/// optimization, <see cref="GateSeverity.Advisory"/> warns only.
/// </param>
/// <param name="Kind">
/// Categorical kind from <see cref="GateKind"/>. Mirrors the rocket-side
/// usage; same enum, no per-pillar variant.
/// </param>
/// <param name="Applicability">
/// Bitmask of engine families the gate applies to. Per ADR-026, air-breathing
/// gates use <see cref="EngineFamilyMask.Airbreathing"/>; the gate's predicate
/// further self-guards on the variant kind (e.g. <c>Pulsejet</c> vs
/// <c>Ramjet</c>) inside the predicate body.
/// </param>
/// <param name="AdrRef">
/// Free-form traceability string — e.g. <c>"Wave-1 / Glassman §3"</c>.
/// </param>
/// <param name="Emit">
/// Append-style predicate: given a <typeparamref name="TResult"/> and a
/// violations list, the gate appends zero, one, or many
/// <see cref="FeasibilityViolation"/> records depending on whether and how
/// it fires. Same shape as the rocket-side
/// <see cref="FeasibilityGateDescriptor.Emit"/>; the only difference is the
/// first parameter type is generic.
/// </param>
public sealed record FeasibilityGateDescriptor<TResult>(
    string Id,
    GateSeverity Severity,
    GateKind Kind,
    EngineFamilyMask Applicability,
    string AdrRef,
    Action<TResult, List<FeasibilityViolation>> Emit);

/// <summary>
/// Per-pillar gate registry. Non-rocket pillars wrap an instance of this in
/// a static-singleton class (e.g. <c>AirbreathingGateRegistry.Instance</c>),
/// passing a <c>RegisterAll</c> bootstrap delegate that calls into the
/// pillar's <c>*Gates.RegisterAll()</c> entry points.
/// </summary>
/// <typeparam name="TResult">
/// The pillar's gate-evaluator input type. Must match
/// <see cref="FeasibilityGateDescriptor{TResult}"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// Threading: registration is one-shot at first <see cref="EnsureInitialized"/>
/// call, gated by a per-instance lock; reads are lock-free against the
/// immutable list / dict thereafter. Same pattern as the rocket-side
/// <see cref="GateRegistry"/>.
/// </para>
/// <para>
/// Each pillar should expose ONE static-singleton wrapper around an instance
/// of this class. Air-breathing's wrapper is
/// <c>AirbreathingGateRegistry.Instance</c> in
/// <c>Voxelforge.Airbreathing.Core.Optimization</c>. The wrapper passes its
/// pillar's <c>*Gates.RegisterAll</c> as the bootstrap delegate so the
/// registry self-populates on first access.
/// </para>
/// </remarks>
public sealed class GateRegistry<TResult>
{
    private readonly List<FeasibilityGateDescriptor<TResult>> _all = new();
    private readonly Dictionary<string, FeasibilityGateDescriptor<TResult>> _byId = new();
    private bool _initialized;
    private readonly object _lock = new();
    private readonly Action _registerAll;

    /// <summary>
    /// Construct a registry. The <paramref name="registerAll"/> delegate is
    /// invoked exactly once on first <see cref="EnsureInitialized"/> call;
    /// it should call into the pillar's <c>*Gates.RegisterAll()</c> entry
    /// points (which in turn call <see cref="Register"/>).
    /// </summary>
    public GateRegistry(Action registerAll)
    {
        _registerAll = registerAll ?? throw new ArgumentNullException(nameof(registerAll));
    }

    /// <summary>
    /// All registered gates in registration order. Order is stable across
    /// processes; per-pillar snapshot tests pin it.
    /// </summary>
    public IReadOnlyList<FeasibilityGateDescriptor<TResult>> All
    {
        get
        {
            EnsureInitialized();
            return _all;
        }
    }

    /// <summary>
    /// Look up a gate by its <see cref="FeasibilityGateDescriptor{TResult}.Id"/>.
    /// Throws <see cref="KeyNotFoundException"/> if the ID is unknown.
    /// </summary>
    public FeasibilityGateDescriptor<TResult> ById(string id)
    {
        EnsureInitialized();
        return _byId[id];
    }

    /// <summary>
    /// Try-get variant for callers that want to detect missing IDs without
    /// exception-handling.
    /// </summary>
    public bool TryGetById(string id, out FeasibilityGateDescriptor<TResult>? gate)
    {
        EnsureInitialized();
        if (_byId.TryGetValue(id, out var found))
        {
            gate = found;
            return true;
        }
        gate = null;
        return false;
    }

    /// <summary>
    /// Registration entry point. Called from the pillar's <c>*Gates.RegisterAll</c>
    /// during one-shot init. Throws <see cref="InvalidOperationException"/>
    /// on duplicate IDs.
    /// </summary>
    /// <remarks>
    /// Public (rather than internal as on the rocket-side <see cref="GateRegistry"/>)
    /// because pillar gates live in different assemblies (e.g.
    /// <c>Voxelforge.Airbreathing.Core</c>) and can't reach internal
    /// registration. The "only <c>*Gates.cs</c> files call this" discipline
    /// is enforced socially + by code review, not by visibility.
    /// </remarks>
    public void Register(FeasibilityGateDescriptor<TResult> gate)
    {
        if (gate is null) throw new ArgumentNullException(nameof(gate));
        if (_byId.TryGetValue(gate.Id, out var existing))
            throw new InvalidOperationException(
                $"Duplicate gate ID '{gate.Id}' (already registered with "
              + $"applicability {existing.Applicability}; new "
              + $"applicability {gate.Applicability}).");
        _all.Add(gate);
        _byId[gate.Id] = gate;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            _registerAll();
            _initialized = true;
        }
    }
}
