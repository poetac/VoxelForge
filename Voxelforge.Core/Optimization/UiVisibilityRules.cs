// UiVisibilityRules.cs — UI overhaul Sprint 1, Step 1 (2026-04-28).
//
// Centralised, pure-data lookup table that answers "should this field
// be visible right now?" given a UiVisibilityState (cycle / topology /
// pair / opt-in toggles).
//
// Two important design choices:
//
//   1. Every rule that depends on the cycle's preburner/turbopump/
//      turbine state DELEGATES to CycleSolvers.Get(cycle). This is the
//      single source of truth — the same predicate the optimiser uses
//      to decide whether SizePreburnerFor / TurbopumpSizing / etc.
//      runs. A future ICycleSolver edit (e.g., adding a new cycle)
//      automatically propagates here without touching this file.
//
//   2. The rules table is a static readonly Dictionary<string, Func<...>>
//      built at type-init time. Lookups are O(1). The functional
//      shape (rules as predicates, not methods) keeps the per-rule
//      definition tight enough to read on one line.
//
// Hidden-controls discipline (CRITICAL): when a control becomes
// hidden, its underlying value must
// be PRESERVED in memory. Reverting the discriminator brings the
// control back with its previous value. Hidden ≠ reset to default.
// The App-side ControlVisibilityRegistry (separate PR) only sets
// .Visible = false; ReadDesign() continues reading the underlying
// value (no-op for the optimiser because [SaDesignVariable]'s SaGate
// already filters on the same predicate).

using Voxelforge.Combustion;
using Voxelforge.FeedSystem;

namespace Voxelforge.Optimization;

/// <summary>
/// Per-field visibility verdict. Beyond plain Hidden/Shown, two
/// additional states let the App emit a softer affordance — for
/// example a "(recommended for LOX/H2)" suffix on the cycle picker
/// rather than a hard hide of non-recommended cycles.
/// </summary>
public enum FieldRelevance
{
    /// <summary>Hide this field — its underlying value is preserved.</summary>
    Hidden = 0,

    /// <summary>Show this field with default styling.</summary>
    Shown,

    /// <summary>Show this field; UI may suffix with "(recommended)".</summary>
    Recommended,

    /// <summary>Show this field; UI may suffix with a discouragement hint.</summary>
    Discouraged,
}

/// <summary>
/// Static rules table mapping field keys to per-state visibility
/// predicates. Pure data; no Form / Control / WinForms references.
/// </summary>
/// <remarks>
/// <para>
/// Adding a field: add a constant to <see cref="FieldKeys"/> and add a
/// rule entry below. The reflection-based completeness tests fail the
/// build if you forget either side.
/// </para>
/// <para>
/// Adding a discriminator (e.g., a new <see cref="EngineCycle"/>): no
/// changes needed here — the cycle-dependent rules delegate to
/// <see cref="CycleSolvers.Get(EngineCycle)"/> which is itself
/// compile-time-exhaustive.
/// </para>
/// </remarks>
public static class UiVisibilityRules
{
    // ── Public surface ──────────────────────────────────────────────

    /// <summary>
    /// Per-field relevance verdict. Returns <see cref="FieldRelevance.Shown"/>
    /// for any key without an explicit rule (default = always shown), so
    /// adding a new always-on field is zero-config.
    /// </summary>
    public static FieldRelevance For(string fieldKey, UiVisibilityState s)
    {
        if (fieldKey is null)
            throw new System.ArgumentNullException(nameof(fieldKey));
        if (s is null)
            throw new System.ArgumentNullException(nameof(s));

        return _rules.TryGetValue(fieldKey, out var rule) ? rule(s) : FieldRelevance.Shown;
    }

    /// <summary>Convenience wrapper: <c>For(...) != Hidden</c>.</summary>
    public static bool ShouldShow(string fieldKey, UiVisibilityState s)
        => For(fieldKey, s) != FieldRelevance.Hidden;

    /// <summary>
    /// Cycles ordered with the propellant-pair-recommended ones first.
    /// Cosmetic reorder only — all cycles remain selectable. The App
    /// suffixes recommended cycles with "(recommended for &lt;pair&gt;)"
    /// in the dropdown.
    /// </summary>
    public static IEnumerable<EngineCycle> RecommendedCycles(PropellantPair p)
        => p switch
        {
            // LOX/H2 has enough regen heat to drive an expander cycle.
            // RL10 (closed) and Vinci (closed) are the canonical examples.
            // Open expander is a regional alternative (J-2X dev ran open).
            PropellantPair.LOX_H2  => new[]
            {
                EngineCycle.ClosedExpander,
                EngineCycle.OpenExpander,
                EngineCycle.StagedCombustion,
            },

            // LOX/RP-1 has limited regen heat (kerosene coking caps
            // coolant T). Gas generator (Merlin) and ox-rich staged
            // combustion (RD-180/191) dominate.
            PropellantPair.LOX_RP1 => new[]
            {
                EngineCycle.GasGenerator,
                EngineCycle.StagedCombustion,
                EngineCycle.ORSC,
            },

            // LOX/CH4 supports multiple cycle architectures. Full-flow
            // staged combustion is the SpaceX Raptor signature; gas
            // generator is the BE-4 / vintage; ORSC sits between.
            PropellantPair.LOX_CH4 => new[]
            {
                EngineCycle.GasGenerator,
                EngineCycle.FullFlow,
                EngineCycle.StagedCombustion,
            },

            // Storables (N2O4_MMH, H2O2_RP1) — tables not implemented;
            // keep the recommendation set empty so the App falls back to
            // showing all cycles unsuffixed.
            _ => System.Array.Empty<EngineCycle>(),
        };

    /// <summary>
    /// All cycles — convenience wrapper for the App's dropdown population.
    /// Equivalent to <c>System.Enum.GetValues&lt;EngineCycle&gt;()</c>;
    /// kept as a method for symmetry with <see cref="RecommendedCycles"/>.
    /// </summary>
    public static IEnumerable<EngineCycle> AvailableCycles(PropellantPair p)
        => System.Enum.GetValues<EngineCycle>();

    /// <summary>
    /// Optional one-line hint shown in the App as a tooltip / status-
    /// bar message. Null = no hint. Today only a small set of fields
    /// emit hints; opt-in growth as the App-side wiring lands in
    /// Sprint 1 Step 4.
    /// </summary>
    public static string? Hint(string fieldKey, UiVisibilityState s)
        => _hints.TryGetValue(fieldKey, out var rule) ? rule(s) : null;

    // ── Topology helpers (private) ──────────────────────────────────
    // Folded into the rule predicates below. Kept private because they
    // describe UI-side classification, not optimizer-side dispatch
    // (ChannelTopologyDispatcher.Family is the optimizer-side helper).

    internal static bool IsDiscreteChannel(ChannelTopology t)
        => t is ChannelTopology.Axial or ChannelTopology.Helical;

    internal static bool IsTpms(ChannelTopology t)
        => t is ChannelTopology.TpmsGyroid
              or ChannelTopology.TpmsSchwarzP
              or ChannelTopology.TpmsSchwarzD;

    internal static bool IsAerospike(ChannelTopology t)
        => t is ChannelTopology.Aerospike or ChannelTopology.LinearAerospike;

    internal static bool IsAerospikeAxisymmetric(ChannelTopology t)
        => t is ChannelTopology.Aerospike;

    internal static bool IsLinearAerospike(ChannelTopology t)
        => t is ChannelTopology.LinearAerospike;

    // ── Rules table ──────────────────────────────────────────────────

    private static readonly System.Collections.Generic.Dictionary<
        string, System.Func<UiVisibilityState, FieldRelevance>>
        _rules = BuildRules();

    private static readonly System.Collections.Generic.Dictionary<
        string, System.Func<UiVisibilityState, string?>>
        _hints = BuildHints();

    private static System.Collections.Generic.Dictionary<
        string, System.Func<UiVisibilityState, FieldRelevance>>
        BuildRules()
    {
        const FieldRelevance Shown   = FieldRelevance.Shown;
        const FieldRelevance Hidden  = FieldRelevance.Hidden;

        var d = new System.Collections.Generic.Dictionary<
            string, System.Func<UiVisibilityState, FieldRelevance>>(
                System.StringComparer.Ordinal);

        // ── Always-on (categorical discriminators + bell geometry) ──
        // ContractionRatio, ExpansionRatio, characteristic length,
        // bell entrance/exit angles, bell length fraction — every
        // chamber regardless of topology has these.
        d[FieldKeys.ContractionRatio]        = _ => Shown;
        d[FieldKeys.ExpansionRatio]          = _ => Shown;
        d[FieldKeys.CharacteristicLength_m]  = _ => Shown;
        d[FieldKeys.BellEntranceAngle_deg]   = _ => Shown;
        d[FieldKeys.BellExitAngle_deg]       = _ => Shown;
        d[FieldKeys.BellLengthFraction]      = _ => Shown;
        d[FieldKeys.GasSideWallThickness_mm] = _ => Shown;
        d[FieldKeys.OuterJacketThickness_mm] = _ => Shown;
        d[FieldKeys.FlangeRadialProjection_mm] = _ => Shown;

        // ── Discriminators themselves ───────────────────────────────
        d[FieldKeys.PropellantPair]    = _ => Shown;
        d[FieldKeys.EngineCycle]       = _ => Shown;
        d[FieldKeys.ChannelTopology]   = _ => Shown;
        d[FieldKeys.MixtureRatio]      = _ => Shown;
        d[FieldKeys.ChamberPressure_MPa] = _ => Shown;
        d[FieldKeys.Thrust_N]          = _ => Shown;
        d[FieldKeys.AmbientPressure_kPa] = _ => Shown;
        d[FieldKeys.WallMaterial]      = _ => Shown;

        // ── Discrete-channel-only (Axial / Helical) ─────────────────
        d[FieldKeys.ChannelCount]            = s => IsDiscreteChannel(s.Topology) ? Shown : Hidden;
        d[FieldKeys.ChannelHeightChamber_mm] = s => IsDiscreteChannel(s.Topology) ? Shown : Hidden;
        d[FieldKeys.ChannelHeightThroat_mm]  = s => IsDiscreteChannel(s.Topology) ? Shown : Hidden;
        d[FieldKeys.ChannelHeightExit_mm]    = s => IsDiscreteChannel(s.Topology) ? Shown : Hidden;
        d[FieldKeys.RibThickness_mm]         = s => IsDiscreteChannel(s.Topology) ? Shown : Hidden;

        // Per-station wall-thickness overrides — relevant whenever
        // there is a regen jacket, which is everything except a few
        // exotic fully-ablative configs. Currently shown for all
        // topologies. (Track B per-station wall is exercised across
        // Axial/Helical/TPMS — only topology None drops out, but
        // the user will rarely select None.)
        d[FieldKeys.ChamberWallThicknessOverride_mm] =
            s => s.Topology == ChannelTopology.None ? Hidden : Shown;
        d[FieldKeys.ThroatWallThicknessOverride_mm]  =
            s => s.Topology == ChannelTopology.None ? Hidden : Shown;
        d[FieldKeys.ExitWallThicknessOverride_mm]    =
            s => s.Topology == ChannelTopology.None ? Hidden : Shown;

        // ── TPMS-only ────────────────────────────────────────────────
        d[FieldKeys.TpmsCellEdge_mm]   = s => IsTpms(s.Topology) ? Shown : Hidden;
        d[FieldKeys.TpmsSolidFraction] = s => IsTpms(s.Topology) ? Shown : Hidden;

        // ── Aerospike-only ──────────────────────────────────────────
        d[FieldKeys.PlugLengthRatio]            = s => IsAerospike(s.Topology) ? Shown : Hidden;
        d[FieldKeys.AerospikeContractionRatio]  = s => IsAerospike(s.Topology) ? Shown : Hidden;
        d[FieldKeys.AerospikePlugCooling]       = s => IsAerospike(s.Topology) ? Shown : Hidden;
        d[FieldKeys.LinearAerospikePlugWidth_mm] = s => IsLinearAerospike(s.Topology) ? Shown : Hidden;
        d[FieldKeys.LinearAerospikePlugDepth_mm] = s => IsLinearAerospike(s.Topology) ? Shown : Hidden;

        // ── Cycle-dependent (turbopump / preburner / turbine) ───────
        // Delegate to CycleSolvers.Get to stay in sync with the
        // optimizer's view of each cycle's capabilities.

        d[FieldKeys.PumpInletPressure_MPa] =
            s => CycleSolvers.Get(s.Cycle).HasTurbopump ? Shown : Hidden;

        d[FieldKeys.PreburnerChamberPressure_MPa] = s =>
        {
            var cs = CycleSolvers.Get(s.Cycle);
            return (cs.HasFuelRichPreburner || cs.HasOxRichPreburner) ? Shown : Hidden;
        };

        d[FieldKeys.PreburnerMrRatio] = s =>
        {
            var cs = CycleSolvers.Get(s.Cycle);
            return (cs.HasFuelRichPreburner || cs.HasOxRichPreburner) ? Shown : Hidden;
        };

        d[FieldKeys.TurbineInletTemperature_K] =
            s => CycleSolvers.Get(s.Cycle).HasTurbine ? Shown : Hidden;
        d[FieldKeys.TurbinePressureRatio] =
            s => CycleSolvers.Get(s.Cycle).HasTurbine ? Shown : Hidden;

        d[FieldKeys.PreburnerCoolingChannelCount] = s =>
        {
            var cs = CycleSolvers.Get(s.Cycle);
            return s.PreburnerCoolingEnabled
                && (cs.HasFuelRichPreburner || cs.HasOxRichPreburner)
                ? Shown : Hidden;
        };
        d[FieldKeys.PreburnerCoolingChannelDepth_mm] = s =>
        {
            var cs = CycleSolvers.Get(s.Cycle);
            return s.PreburnerCoolingEnabled
                && (cs.HasFuelRichPreburner || cs.HasOxRichPreburner)
                ? Shown : Hidden;
        };

        // ── Film cooling (opt-in, may be propellant-pair-restricted) ──
        // Currently shown for all pairs when enabled; future
        // restrictions (e.g., fuel-only film) can layer on without
        // changing the rule's call signature.
        d[FieldKeys.FilmCoolingEnabled]         = _ => Shown;
        d[FieldKeys.FilmFuelFraction]           = s => s.FilmCoolingEnabled ? Shown : Hidden;
        d[FieldKeys.FilmSlotHeightOverride_mm]  = s => s.FilmCoolingEnabled ? Shown : Hidden;
        d[FieldKeys.FilmInjectionAxialFraction] = s => s.FilmCoolingEnabled ? Shown : Hidden;

        // ── Pintle-only injector dims ───────────────────────────────
        d[FieldKeys.PintleDiameterOverride_mm] =
            s => s.HasInjectorPattern && s.InjectorPattern == InjectorPatternKind.Pintle
                 ? Shown : Hidden;
        d[FieldKeys.PintleSleeveHoleCountOverride] =
            s => s.HasInjectorPattern && s.InjectorPattern == InjectorPatternKind.Pintle
                 ? Shown : Hidden;
        d[FieldKeys.InjectorPatternKind] =
            s => PropellantPairs.IsImplemented(s.Pair) ? Shown : Hidden;

        // ── Hot-fire readiness subsystems (opt-in checkboxes) ──────
        d[FieldKeys.ChilldownEnabled]         = _ => Shown;
        d[FieldKeys.StartTransientEnabled]    = _ => Shown;
        d[FieldKeys.LpbfPrintabilityEnabled]  = _ => Shown;

        // ── Mounting + instrumentation + igniter ───────────────────
        d[FieldKeys.MountingFlangeEnabled]  = _ => Shown;
        d[FieldKeys.MountingFlangeStandard] = s => s.MountingFlangeEnabled ? Shown : Hidden;
        d[FieldKeys.IgniterType]            = _ => Shown;
        d[FieldKeys.SensorBosses]           = _ => Shown;

        // ── Injector face STL passthrough ──────────────────────────
        d[FieldKeys.InjectorStlEnabled] = _ => Shown;
        d[FieldKeys.InjectorStlPath]    = s => s.InjectorStlEnabled ? Shown : Hidden;

        // ── Voxel + run-control settings (always on) ───────────────
        d[FieldKeys.VoxelSize_mm]   = _ => Shown;
        d[FieldKeys.SaIterations]   = _ => Shown;
        d[FieldKeys.SaSeed]         = _ => Shown;

        return d;
    }

    private static System.Collections.Generic.Dictionary<
        string, System.Func<UiVisibilityState, string?>>
        BuildHints()
    {
        var d = new System.Collections.Generic.Dictionary<
            string, System.Func<UiVisibilityState, string?>>(
                System.StringComparer.Ordinal);

        // Stub: a small number of fields emit hints today. Sprint 1 Step
        // 4 (App-side wiring) consumes these to populate tooltips. More
        // hints can layer on without API changes.

        d[FieldKeys.EngineCycle] = s =>
        {
            var recommended = RecommendedCycles(s.Pair);
            foreach (var c in recommended)
                if (c == s.Cycle) return null;  // already a recommended pick
            // Pair has recommendations and the current pick isn't one of
            // them — issue a soft nudge.
            using var e = recommended.GetEnumerator();
            if (!e.MoveNext()) return null;
            return $"Tip: {e.Current} is the natural cycle for {s.Pair}.";
        };

        d[FieldKeys.PropellantPair] = s =>
            PropellantPairs.IsImplemented(s.Pair)
                ? null
                : $"Note: {s.Pair} tables are not yet implemented; the optimiser will reject Generate.";

        return d;
    }
}
