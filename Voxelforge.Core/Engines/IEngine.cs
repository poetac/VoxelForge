// IEngine.cs — engine-family abstraction (Sprint A Phase 1, 2026-05-04).
//
// Closes the engine-family-abstraction recommendation, deferred per
// rule of three until 3+ concrete engine families existed. With rocket-regen,
// rocket-aerospike, ramjet, turbojet, turbofan, scramjet, and RBCC all shipped
// in 2026-04 / 2026-05, the rule-of-three is satisfied. These interfaces are
// designed against 6 real engine families and codify the shape they already
// share.
//
// Phase 1 (this commit): introduce the contracts. Existing engine families
// implement them via lightweight adapters; existing call sites (optimizer,
// UI dispatch) are NOT migrated yet — they continue to reach into the
// concrete `RegenChamberOptimization.GenerateWith` / `AirbreathingOptimization.GenerateWith`
// directly. This keeps the change additive + non-breaking.
//
// Phase 2 (deferred): migrate the SA / NSGA-II / Bayesian optimizer call
// sites to consume the IEngine interface generically (the optimizer should
// see "an engine" not "the rocket").
//
// Phase 3 (deferred): migrate the UI / CLI dispatch (RegenChamberForm,
// Voxelforge.StlExporter, Voxelforge.Eval) to take an IEngine instance.
//
// Trade-off note: the memo sketched a binary `IEngine<TDesign, TResult>`
// assuming a shared `OperatingConditions`. In practice rocket has
// `OperatingConditions` (propellant pair, MR, Pc, ...) and air-breathing
// has `FlightConditions` (Mach, altitude, throttle, ...) — fundamentally
// different domains. Forcing a synthetic union would be the worst kind of
// premature abstraction. The interface is therefore ternary
// `IEngine<TDesign, TConditions, TResult>` so each family carries its own
// natural conditions type, and the family discriminator is exposed via
// `IEngineDesign.Family` / `IEngineConditions.Family` for runtime dispatch.

namespace Voxelforge.Engines;

/// <summary>
/// Marker interface for an engine design (parameter bundle). The
/// <see cref="Family"/> string discriminates which engine family the
/// design belongs to; matches the corresponding <see cref="IEngineConditions.Family"/>
/// at evaluation time.
/// </summary>
/// <remarks>
/// Recommended family identifiers:
/// <list type="bullet">
///   <item><description><c>"rocket-regen"</c> — bell or annular regen-cooled rocket chamber (legacy + aerospike topologies)</description></item>
///   <item><description><c>"airbreathing-ramjet"</c> — ramjet (sub-step 1a)</description></item>
///   <item><description><c>"airbreathing-turbojet"</c> — turbojet (sub-step 1b)</description></item>
///   <item><description><c>"airbreathing-turbofan"</c> — turbofan (sub-step 1c)</description></item>
///   <item><description><c>"airbreathing-scramjet"</c> — scramjet (sub-step 1d)</description></item>
///   <item><description><c>"airbreathing-rbcc"</c> — rocket-based combined-cycle (sub-step 1e)</description></item>
/// </list>
/// Future families (marine ramjet, gas turbine power-gen, steam Rankine,
/// fuel cells) extend this enum-of-strings without touching the interface.
/// </remarks>
public interface IEngineDesign
{
    /// <summary>
    /// Family discriminator string. See class remarks for the canonical values.
    /// </summary>
    string Family { get; }
}

/// <summary>
/// Marker interface for an engine's operating envelope (conditions). Pairs
/// with an <see cref="IEngineDesign"/> at evaluation time; <see cref="Family"/>
/// must match the design's family or the evaluator throws.
/// </summary>
/// <remarks>
/// Rocket-regen carries propellant pair, mixture ratio, chamber pressure.
/// Air-breathing carries flight Mach, altitude, throttle. Future families
/// add their own shape — this interface is intentionally minimal.
/// </remarks>
public interface IEngineConditions
{
    /// <summary>
    /// Family discriminator string — matches the corresponding
    /// <see cref="IEngineDesign.Family"/>.
    /// </summary>
    string Family { get; }
}

/// <summary>
/// Common contract for an engine evaluation result. Carries enough
/// information for an optimizer / UI to score, gate, and report on a
/// candidate design without knowing the family.
/// </summary>
/// <remarks>
/// Concrete results carry far more (rocket has thermal stations, structural
/// stress map, manufacturing report, stability analysis, ...; air-breathing
/// has station map + cycle diagnostics). This interface exposes only the
/// minimal cross-family surface — feasibility verdict + advisory list.
/// Family-specific consumers cast back to the concrete result.
/// </remarks>
public interface IEngineResult
{
    /// <summary>
    /// Hard-constraint violations. Empty on a feasible design.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<Voxelforge.Optimization.FeasibilityViolation> Violations { get; }

    /// <summary>
    /// Convenience: <c>true</c> iff <see cref="Violations"/> is empty.
    /// </summary>
    bool IsFeasible { get; }

    /// <summary>
    /// Soft warnings that surface to UI / report without gating optimization
    /// (e.g. <c>SURGE_MARGIN_INSUFFICIENT</c>, <c>BIMETALLIC_BOND_ZONE_SHEAR</c>).
    /// Reuses the <see cref="Voxelforge.Optimization.FeasibilityViolation"/>
    /// shape so downstream consumers treat advisories and hard violations
    /// uniformly except for whether they fail <see cref="IsFeasible"/>.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<Voxelforge.Optimization.FeasibilityViolation> Advisories { get; }
}

/// <summary>
/// The unifying engine-family abstraction. Each engine family ships one
/// concrete implementation that wraps its existing physics pipeline.
/// </summary>
/// <typeparam name="TDesign">The engine's design record type.</typeparam>
/// <typeparam name="TConditions">The engine's operating-conditions type.</typeparam>
/// <typeparam name="TResult">The engine's evaluation-result type.</typeparam>
public interface IEngine<TDesign, TConditions, TResult>
    where TDesign : IEngineDesign
    where TConditions : IEngineConditions
    where TResult : IEngineResult
{
    /// <summary>
    /// Family discriminator — matches every <typeparamref name="TDesign"/> +
    /// <typeparamref name="TConditions"/> instance this engine accepts.
    /// </summary>
    string Family { get; }

    /// <summary>
    /// Evaluate one (design, conditions) pair end-to-end. Implementations
    /// throw <see cref="System.ArgumentException"/> if the design / conditions
    /// family does not match this engine's <see cref="Family"/>.
    /// </summary>
    TResult Evaluate(TDesign design, TConditions conditions);
}
