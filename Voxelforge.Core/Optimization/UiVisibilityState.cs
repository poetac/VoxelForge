// UiVisibilityState.cs — UI overhaul Sprint 1, Step 1 (2026-04-28).
//
// A pure data snapshot of the categorical UI choices (cycle, topology,
// propellant pair) plus a handful of opt-in subsystem toggles. Driven
// into UiVisibilityRules.For(...) to compute per-field visibility.
//
// Lives in Core (not App) for two reasons:
//   • Pure data — no WinForms types, no PicoGK references. Means the
//     rules + state can be unit-tested in xUnit without tripping the
//     xUnit + PicoGK GLFW-teardown crash documented in CLAUDE.md
//     pitfall #8.
//   • Same predicate as `[SaDesignVariable]`'s Gate enum — keeping
//     them adjacent makes it obvious that one is the optimizer-side
//     view and the other is the UI-side view of the same conditional
//     logic.
//
// The state record carries no Form references and no Control instances.
// The App-side ControlVisibilityRegistry (Sprint 1 Step 2, separate PR)
// reads form state, builds a UiVisibilityState, and passes it here.

using Voxelforge.Combustion;
using Voxelforge.FeedSystem;

namespace Voxelforge.Optimization;

/// <summary>
/// Snapshot of the UI's discriminator state (cycle, topology, propellant
/// pair) plus opt-in subsystem toggles. Consumed by
/// <see cref="UiVisibilityRules"/> to compute per-field visibility.
/// </summary>
/// <remarks>
/// Constructed from form state by the App-side wiring layer. Pure data;
/// equality-by-value semantics are intentional so callers can short-
/// circuit visibility recomputation when the state hasn't changed.
/// </remarks>
public sealed record UiVisibilityState(
    EngineCycle Cycle,
    ChannelTopology Topology,
    PropellantPair Pair,

    // Subsystem opt-in toggles (form-level checkboxes).
    bool HasInjectorPattern,
    bool HasDualBell,
    bool ChilldownEnabled,
    bool StartTransientEnabled,
    bool LpbfPrintabilityEnabled,
    bool PreburnerCoolingEnabled,
    bool AerospikeCoolingEnabled,
    bool FilmCoolingEnabled,
    bool MountingFlangeEnabled,
    bool InjectorStlEnabled,
    bool FeedSystemEnabled,

    // Injector-pattern discriminator (separate from HasInjectorPattern
    // because the App enables a few pintle-specific dims only when the
    // pattern is the pintle variant, not just any pattern).
    InjectorPatternKind InjectorPattern = InjectorPatternKind.None);

/// <summary>
/// Coarse classification of the injector pattern for UI visibility
/// purposes. Maps to the App-side InjectorPattern type but stays in
/// Core (no App reference) by carrying only the categorical bit.
/// </summary>
public enum InjectorPatternKind
{
    /// <summary>No pattern set or unimplemented pair.</summary>
    None = 0,
    /// <summary>Coaxial injector elements (LOX/H2 typical).</summary>
    Coaxial,
    /// <summary>Pintle injector (LOX/CH4, LOX/RP-1 typical).</summary>
    Pintle,
    /// <summary>Showerhead / impinging-jet pattern.</summary>
    Showerhead,
    /// <summary>Other / future patterns.</summary>
    Other,
}
