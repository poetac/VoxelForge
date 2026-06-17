// GateRegistry.cs — Sprint 0 PR-1 (Trimmed): declarative gate registry.
//
// Phase 1 (this commit): infrastructure-only. Adds the FeasibilityGateDescriptor
// record + EngineFamilyMask + GateSeverity + GateRegistry static class.
// FeasibilityGate.Evaluate()'s 1,150-line if-chain is UNCHANGED; the
// registry stays empty until Phase 2 migrates gates one-by-one.
//
// Why infrastructure-first:
//   The migration moves 49 gates from a monolithic if-chain to declarative
//   record registrations. Snapshot tests in
//   Tests/Optimization/GateOrderingSnapshotTests.cs pin the EXACT
//   ConstraintId emission order. With the scaffolding in place, gates
//   migrate one-by-one with the snapshot tests as the safety net for
//   each batch.
//
// Design notes:
//   • Record name is `FeasibilityGateDescriptor`, NOT `FeasibilityGate`,
//     to avoid colliding with the existing `static class FeasibilityGate`.
//     The descriptor describes a registered gate; the static class hosts
//     evaluator entry points.
//   • `EngineFamilyMask` is [Flags] but the only currently-defined
//     non-rocket mask is `All`. `Airbreathing = 1 << 2` is reserved
//     (commented-out) for when air-breathing gates land. The mask field
//     exists today even though only rocket gates use it — this saves a
//     follow-up refactor when air-breathing arrives, and the snapshot
//     test guards prevent any registry-driven ordering drift in the
//     interim.
//   • `Predicate` returns `FeasibilityViolation?` not `bool` so the gate
//     owns the diagnostic message + ActualValue/Limit construction. This
//     matches every existing emission site in FeasibilityGate.Evaluate
//     (the if-block builds + adds a violation; predicate returns one).
//   • PreScreen is NOT unified into the registry in this sprint. The
//     existing T1.5 PreScreen runs against (OperatingConditions,
//     RegenChamberDesign), not against RegenGenerationResult, so the
//     predicate signature doesn't match. Phase 2 may add a parallel
//     `IPreScreenGate` interface; deferred until first PreScreen-eligible
//     gate is migrated.
//   • Threading: registration is one-shot at type-init; reads after
//     EnsureInitialized are lock-free against the immutable list/dict.
//
// See ADR-019 for the architectural decision context.

using System;
using System.Collections.Generic;

namespace Voxelforge.Optimization;

/// <summary>
/// Engine-family applicability mask for a feasibility gate. A gate may
/// apply to one or more families; the evaluator filters by mask before
/// invoking the predicate.
/// </summary>
/// <remarks>
/// <para>
/// Today the rocket family has two topology variants (regen + aerospike)
/// that ship parallel evaluators. The mask discriminates them so the
/// registry can host both without cross-contamination — a regen gate
/// only fires on regen results, an aerospike gate only fires on aerospike
/// results. Pre-existing aerospike gates (in <c>AerospikeFeasibility</c>)
/// continue to evaluate via their own parallel path during the migration.
/// </para>
/// <para>
/// Air-breathing owns <c>1 &lt;&lt; 2</c> (Step 1;
/// shipped 2026-04 with the Sprint A pillar). Electric-propulsion owns
/// <c>1 &lt;&lt; 3</c> for the pillar mask and <c>1 &lt;&lt; 7</c> for the
/// resistojet variant (Wave-1, 2026-05). The full bit-allocation table
/// lives in <c>Voxelforge/docs/family-allocations.md</c>.
/// </para>
/// </remarks>
[Flags]
public enum EngineFamilyMask
{
    /// <summary>No family — degenerate, used as a sentinel.</summary>
    None = 0,

    /// <summary>Bell-chamber regen-cooled rocket. Default mask for
    /// rocket-regen gates registered via the existing
    /// <see cref="FeasibilityGate.Evaluate"/> path.</summary>
    RocketRegen = 1 << 0,

    /// <summary>Aerospike (axisymmetric or linear) rocket. Mask for
    /// gates currently in <c>AerospikeFeasibility.Evaluate</c>.</summary>
    RocketAerospike = 1 << 1,

    /// <summary>Convenience: any rocket family.</summary>
    Rocket = RocketRegen | RocketAerospike,

    /// <summary>Air-breathing engines (ramjet, turbojet, turbofan, scramjet,
    /// RBCC, gas-turbine, steam-turbine). Gates currently live in the
    /// parallel evaluator <c>Voxelforge.Airbreathing.AirbreathingFeasibility.Evaluate</c>;
    /// the registry-vs-parallel unification is deferred to a future ADR
    /// (post-Wave-1) per ADR-026 §9 risk #2.</summary>
    Airbreathing = 1 << 2,

    /// <summary>Electric-propulsion engines (Wave-1: resistojet only;
    /// Wave-2 extends to HET / MPD / GriddedIon / Arcjet behind a
    /// plasma-state audit). Gates live in the parallel evaluator
    /// <c>Voxelforge.ElectricPropulsion.ElectricPropulsionFeasibility.Evaluate</c>.</summary>
    ElectricPropulsion = 1 << 3,

    /// <summary>Resistojet variant of the electric-propulsion pillar.
    /// Per-variant bit so resistojet-specific gates register here and
    /// future HET / MPD gates don't accidentally fire on resistojet
    /// results.</summary>
    ElectricResistojet = 1 << 7,

    /// <summary>Hall-Effect Thruster (HET) variant of the electric-propulsion
    /// pillar (Wave-2, ADR-029). Per-variant bit so HET-specific gates
    /// (HET_DISCHARGE_VOLTAGE_OUT_OF_BAND, HET_ANODE_OVERHEAT, etc.) register
    /// here. Plasma physics via Goebel &amp; Katz §3 Busch discharge model.
    /// Gates live in <c>ElectricPropulsionFeasibility.Evaluate</c>'s
    /// kind-predicated HallEffect block.</summary>
    ElectricHallEffect = 1 << 8,

    /// <summary>Gridded-Ion Thruster (GIT) variant of the electric-propulsion
    /// pillar (Wave-2, Sprint EP.W2.GIT). Per-variant bit so GIT-specific gates
    /// (GIT_BEAM_VOLTAGE_OUT_OF_BAND, GIT_PERVEANCE_LIMIT_EXCEEDED,
    /// GIT_NEUTRALIZER_CURRENT_MISMATCH, GIT_PLUME_DIVERGENCE_EXCESSIVE,
    /// GIT_GRID_LIFETIME_BELOW_FLOOR) register here. Physics via Goebel &amp;
    /// Katz §5 Child-Langmuir beam-extraction model. NSTAR (Deep Space 1 /
    /// Dawn) validation fixture. Fourth <c>IPlasmaState</c> consumer.</summary>
    ElectricGriddedIon = 1 << 9,

    /// <summary>Nuclear thermal propulsion. Wave-1: NERVA-class solid-core NTR (LH₂ propellant).
    /// Gates live in the parallel evaluator <c>Voxelforge.Nuclear.Optimization.NuclearGates.Evaluate</c>.
    /// Wave-2+ extends to bimodal NTR and Project Pluto nuclear ramjet.</summary>
    NuclearPropulsion = 1 << 4,

    /// <summary>Arcjet variant of the electric-propulsion pillar (Wave-2,
    /// Sprint EP.W2.AJ). Per-variant bit so arcjet-specific gates
    /// (ARCJET_VOLTAGE_OUT_OF_BAND, ARCJET_ANODE_OVERHEAT, etc.) register
    /// here. Physics via Sutton &amp; Biblarz 9e §16.3 Maecker-Kovitya
    /// constricted-arc thermal model. Gates live in
    /// <c>ElectricPropulsionFeasibility.Evaluate</c>'s kind-predicated
    /// Arcjet block. ADR-029 second IPlasmaState consumer.</summary>
    ElectricArcjet = 1 << 10,

    /// <summary>Magnetoplasmadynamic Thruster (MPD) variant of the electric-
    /// propulsion pillar (Wave-2, Sprint EP.W2.MPD). Per-variant bit so MPD-
    /// specific gates (MPD_ARC_CURRENT_OUT_OF_BAND, MPD_CATHODE_OVERHEAT,
    /// MPD_GEOMETRY_INVERTED, MPD_ONSET_PARAMETER_EXCESSIVE,
    /// MPD_THRUST_EFFICIENCY_LOW) register here. Physics via Maecker self-
    /// field formula T = b·J² with b = (μ₀/4π)·(ln(r_a/r_c)+3/4). NASA-Lewis
    /// 200 kW SF-MPD validation fixture (Sovey 1990). Fifth and final
    /// <c>IPlasmaState</c> consumer.</summary>
    ElectricMpd = 1 << 11,

    /// <summary>Pulsed Plasma Thruster variant of the electric-propulsion
    /// pillar (Wave-2, Sprint EP.W2.PPT). Per-variant bit so PPT-specific
    /// gates (PPT_CAPACITOR_ENERGY_OUT_OF_BAND, PPT_NO_BREAKDOWN,
    /// PPT_IMPULSE_BIT_BELOW_FLOOR, PPT_ABLATION_RATE_EXCESSIVE) register
    /// here. Physics via Solbes-Vondra ablation-discharge fit on solid
    /// PTFE; Aerojet EO-1 EP-12 validation fixture (~860 µN·s impulse bit,
    /// ~860 s Isp). Gates live in <c>ElectricPropulsionFeasibility.Evaluate</c>'s
    /// kind-predicated PPT block. Third IPlasmaState consumer — rule of
    /// three met (ADR-029a); abstraction promoted to <c>Voxelforge.Core/Plasma/</c>.</summary>
    ElectricPpt = 1 << 12,

    // Bits 5-6 reserved for future rocket sub-types / airbreathing. See
    // Voxelforge/docs/family-allocations.md for the authoritative bit registry.

    /// <summary>Marine AUV / displacement hull. Wave 1, M1 (2026-05-05, Sprint M.0).</summary>
    Marine = 1 << 13,

    /// <summary>
    /// Surface-hull marine sub-family (Savitsky 1964 planing regime). Wave 3
    /// (Sprint M.W3). Per-variant bit so planing-specific gates
    /// (PLANING_SPEED_COEFFICIENT_OUT_OF_BAND, PLANING_TRIM_OUT_OF_BAND,
    /// PLANING_WETTED_LENGTH_TO_BEAM_OUT_OF_BAND, PLANING_DEADRISE_OUT_OF_BAND,
    /// PLANING_LCG_OUT_OF_BAND, PLANING_RESISTANCE_ABOVE_BAND) register here.
    /// Physics via <c>SavitskyPlaningModel</c>. Gates live in
    /// <c>MarineGates.EvaluatePlaning</c>.
    /// </summary>
    MarineHull = 1 << 14,

    /// <summary>
    /// VASIMR (Variable Specific Impulse Magnetoplasma Rocket) variant of
    /// the electric-propulsion pillar. Sprint EP.W4 (Wave-3, deferred).
    /// Reserved bit — enum value registered so designs serialised today
    /// with <c>Kind = Vasimr</c> can round-trip cleanly through schema
    /// migration, even though the physics dispatch throws
    /// <see cref="System.NotImplementedException"/> until the helicon +
    /// ICRH solver ships. References: Chang Diaz et al. (Ad Astra Rocket
    /// VX-200, J. Propulsion &amp; Power 2009).
    /// </summary>
    ElectricVasimr = 1 << 15,

    /// <summary>
    /// FEEP (Field-Emission Electric Propulsion) variant of the
    /// electric-propulsion pillar. Sprint EP.W5 (Wave-3, deferred per
    /// ADR-034 D1 bit reservation). Reserved bit — enum value registered
    /// so designs with <c>Kind = Feep</c> round-trip through schema
    /// migration. Physics dispatch throws until Sprint EP.W5 phase 2
    /// (Mair-Lozano emitter model). References: Mair G., Genovese A.,
    /// Tajmar M. (Indium-FEEP development).
    /// </summary>
    ElectricFeep = 1 << 16,

    /// <summary>
    /// HDLT (Helicon Double-Layer Thruster) variant of the electric-
    /// propulsion pillar. Sprint EP.W6 (Wave-3, deferred per ADR-034
    /// D1 bit reservation). Reserved bit — enum value registered so
    /// designs with <c>Kind = Hdlt</c> round-trip through schema
    /// migration. Physics dispatch throws until Sprint EP.W6 phase 2
    /// (helicon + double-layer solver). References: Charles C.,
    /// Boswell R.W. (2003) "Current-free double-layer formation in a
    /// high-density helicon discharge." Appl. Phys. Lett. 82(9).
    /// </summary>
    ElectricHdlt = 1 << 17,

    /// <summary>All currently-defined families (rocket today; expands
    /// as new families register).</summary>
    All = ~0,
}

/// <summary>
/// Severity classification for a feasibility gate.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Hard"/> — failing the gate makes the candidate
/// unconditionally infeasible (the optimizer rejects it via
/// <c>TotalScore = +∞</c>).
/// </para>
/// <para>
/// <see cref="Advisory"/> — failing the gate raises a warning but does
/// not necessarily block scoring. Today every gate registered through
/// the existing <see cref="FeasibilityGate.Evaluate"/> path is treated as
/// Hard at the optimizer level (any non-empty <c>Violations[]</c> array
/// flips <c>IsFeasible</c> to false). Advisory gates still surface to
/// the UI / report but do not gate optimization. Distinct from
/// <see cref="GateKind"/> which classifies the *physics* basis of the
/// gate (PhysicsLimit / EmpiricalBand / ManufacturabilityFloor /
/// AdvisoryHeuristic).
/// </para>
/// </remarks>
public enum GateSeverity
{
    /// <summary>Failure makes the candidate infeasible.</summary>
    Hard,

    /// <summary>Failure surfaces a warning but does not gate optimization.</summary>
    Advisory,
}

/// <summary>
/// Declarative description of a feasibility gate: identity, taxonomy,
/// applicability, traceability, and the predicate that evaluates it
/// against a generated result.
/// </summary>
/// <param name="Id">
/// Stable machine-readable key (e.g. <c>"WALL_TEMP"</c>). Matches the
/// <see cref="FeasibilityViolation.ConstraintId"/> the predicate emits.
/// Must be globally unique across the registry; <see cref="GateRegistry.Register"/>
/// throws on duplicates.
/// </param>
/// <param name="Severity">
/// Classification — <see cref="GateSeverity.Hard"/> rejects the candidate
/// in optimization, <see cref="GateSeverity.Advisory"/> warns only.
/// </param>
/// <param name="Kind">
/// Categorical kind from <see cref="GateKind"/> (PhysicsLimit /
/// EmpiricalBand / ManufacturabilityFloor / AdvisoryHeuristic). Mirrors the
/// pre-existing <c>FeasibilityGate.GetGateKind</c> classification — once
/// all gates are migrated to the registry, the static switch in that
/// method becomes redundant and can be replaced with
/// <c>GateRegistry.ById(id).Kind</c>.
/// </param>
/// <param name="Applicability">
/// Bitmask of engine families the gate applies to. The evaluator filters
/// by this before invoking <see cref="Predicate"/>.
/// </param>
/// <param name="AdrRef">
/// Free-form traceability string — e.g. <c>"ADR-009 + PR #96"</c>. Used
/// in auto-generated gate-inventory documentation; not interpreted at
/// runtime.
/// </param>
/// <param name="Emit">
/// Append-style predicate: given a <see cref="RegenGenerationResult"/>
/// and a violations list, the gate appends zero, one, or many
/// <see cref="FeasibilityViolation"/> records depending on whether and
/// how it fires. Must be deterministic and side-effect-free apart from
/// the append. The callback shape (vs <c>Func&lt;..., FeasibilityViolation?&gt;</c>)
/// supports gates that emit multiple violations with the same ConstraintId
/// (PURGE_FLOW_INSUFFICIENT — one per failing port; TRAPPED_POWDER_REGION
/// — one per pocket; TURBINE_UNCHOKED — up to 4 turbines per design;
/// PUMP_SPECIFIC_SPEED_OFF_BAND — fuel + ox; PREBURNER_WALL_TEMP — fuel
/// + ox-rich; INSTRUMENTATION_THERMAL_BRIDGE_RISK + INSTRUMENTATION_TAP_INTERFERENCE
/// — one per boss / clash) without per-call allocation overhead vs
/// returning <c>IReadOnlyList&lt;...&gt;</c>.
/// </param>
/// <remarks>
/// The record's name is <c>FeasibilityGateDescriptor</c> (not
/// <c>FeasibilityGate</c>) to avoid colliding with the pre-existing
/// <see cref="FeasibilityGate"/> static class that hosts the
/// <see cref="FeasibilityGate.Evaluate"/> entry point. The descriptor
/// describes a registered gate; the static class hosts the evaluator.
/// </remarks>
public sealed record FeasibilityGateDescriptor(
    string Id,
    GateSeverity Severity,
    GateKind Kind,
    EngineFamilyMask Applicability,
    string AdrRef,
    Action<RegenGenerationResult, List<FeasibilityViolation>> Emit);

/// <summary>
/// Static registry of all feasibility gates. Gates are populated lazily
/// at first access via <see cref="EnsureInitialized"/>.
/// </summary>
/// <remarks>
/// <para>
/// During Sprint 0 PR-1 the registry is the migration target for the
/// existing 49 gates currently emitted by
/// <see cref="FeasibilityGate.Evaluate"/>. Each migration step:
/// </para>
/// <list type="number">
///   <item>Extracts a gate's predicate into a static method.</item>
///   <item>Registers it via the per-family registration helper
///         (e.g. <c>RocketRegenGates.RegisterAll()</c>).</item>
///   <item>Removes the inline if-block from
///         <see cref="FeasibilityGate.Evaluate"/>.</item>
///   <item>Runs <c>GateOrderingSnapshotTests</c> to confirm the
///         emission order is preserved.</item>
/// </list>
/// <para>
/// Once all gates have migrated, <see cref="FeasibilityGate.Evaluate"/>
/// becomes a thin loop over <see cref="All"/>. Until then, both paths
/// coexist — the registry hosts migrated gates only.
/// </para>
/// </remarks>
public static class GateRegistry
{
    private static readonly List<FeasibilityGateDescriptor> _all = new();
    private static readonly Dictionary<string, FeasibilityGateDescriptor> _byId = new();
    private static bool _initialized;
    private static readonly object _lock = new();

    /// <summary>
    /// All registered gates in registration order. Order is stable across
    /// processes; <see cref="GateOrderingSnapshotTests"/> pin it.
    /// </summary>
    public static IReadOnlyList<FeasibilityGateDescriptor> All
    {
        get
        {
            EnsureInitialized();
            return _all;
        }
    }

    /// <summary>
    /// Look up a gate by its <see cref="FeasibilityGateDescriptor.Id"/>.
    /// Throws <see cref="KeyNotFoundException"/> if the ID is unknown —
    /// intentional, since silent <c>null</c> would mask typos.
    /// </summary>
    public static FeasibilityGateDescriptor ById(string id)
    {
        EnsureInitialized();
        return _byId[id];
    }

    /// <summary>
    /// Try-get variant for callers that want to detect missing IDs
    /// without exception-handling.
    /// </summary>
    public static bool TryGetById(string id, out FeasibilityGateDescriptor? gate)
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
    /// Internal registration entry point. Called from per-family
    /// registration helpers (e.g. <c>RocketRegenGates.RegisterAll</c>)
    /// during one-shot init. Throws <see cref="InvalidOperationException"/>
    /// on duplicate IDs to surface accidental shadowing immediately.
    /// </summary>
    internal static void Register(FeasibilityGateDescriptor gate)
    {
        if (_byId.TryGetValue(gate.Id, out var existing))
            throw new InvalidOperationException(
                $"Duplicate gate ID '{gate.Id}' (already registered with "
              + $"applicability {existing.Applicability}; new "
              + $"applicability {gate.Applicability}).");
        _all.Add(gate);
        _byId[gate.Id] = gate;
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            // Sprint 0 PR-1 Phase 2: rocket-family gates self-register
            // through RocketGates.RegisterAll. Registration order =
            // declaration order in FeasibilityGate.Evaluate's pre-refactor
            // if-chain; pinned by GateOrderingSnapshotTests.
            //
            // Each migration batch moves additional gates from the inline
            // if-chain into RocketGates.cs. While Phase 2 is in flight,
            // FeasibilityGate.Evaluate dispatches the registered prefix
            // FIRST, then runs the remaining (not-yet-migrated) inline
            // gates as a suffix. Once all 49 gates have migrated,
            // FeasibilityGate.Evaluate is just the registry loop.
            RocketGates.RegisterAll();
            _initialized = true;
        }
    }
}
