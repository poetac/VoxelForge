// ResourceBudget.cs (App half) — SessionSettings adapter for the
// Core-resident `ResourceBudget` static class. Sprint A-3 Phase 2 /
// ADR-021 (2026-04-30): the bulk of `ResourceBudget` moved to Core
// (`Voxelforge.Core/Optimization/ResourceBudget.cs`) so the
// orchestrator can read live caps without referencing App. This file
// adds a separate App-side static class
// (<see cref="ResourceBudgetSettings"/>) that binds the Core
// primitive `Apply` / `ResolveDefaults` methods to `SessionSettings`
// (a WinForms-tied type that stays in App).
//
// Why a separate class instead of a partial-class continuation: C#
// partials cannot span assemblies, so the Core+App-spanning seam uses
// distinct types. App callers that previously called
// `ResourceBudget.ApplySettings(...)` now call
// `ResourceBudgetSettings.ApplySettings(...)`.

namespace Voxelforge.UI;

/// <summary>
/// Sprint A-3 Phase 2 / ADR-021 (2026-04-30): App-side bridge from
/// <see cref="SessionSettings"/> into the Core-resident
/// <see cref="ResourceBudget"/> snapshot. Replaces the prior
/// <c>ResourceBudget.ApplySettings(SessionSettings)</c> /
/// <c>AutoProbeDefaults(SessionSettings)</c> static methods.
/// </summary>
public static class ResourceBudgetSettings
{
    /// <summary>
    /// Read the user's <see cref="SessionSettings"/> and fold into the
    /// live snapshot. Called from the form constructor (after
    /// <c>SessionSettings.Load</c>) and again any time the user
    /// changes the preset / individual caps. When settings' explicit
    /// caps are 0, the preset's resolved value is used; when &gt; 0,
    /// the explicit override wins. Routes through the primitive
    /// <see cref="ResourceBudget.Apply"/> in Core.
    /// </summary>
    public static void ApplySettings(SessionSettings s)
        => ResourceBudget.Apply(
            mode:               s.ResourceMode,
            maxParallelism:     s.MaxParallelism,
            memoryBudget_MB:    s.MemoryBudget_MB,
            demotePriority:     s.DemotePriorityDuringSolves,
            gcLatencyTuning:    s.GcLatencyTuning,
            sweepTimeoutSec:    s.SweepTimeoutSeconds,
            optTimeoutSec:      s.OptTimeoutSeconds,
            abortOpOnInputEdit: s.AbortOpOnInputEdit,
            autoCoarsenVoxel:   s.AutoCoarsenVoxelToFitBudget,
            fastPreviewMode:    s.FastPreviewMode,
            tileLargeBuilds:    s.TileLargeBuilds,
            tileCount:          s.TileCount,
            isolateLargeBuilds: s.IsolateLargeBuildsAtFailProjection);

    /// <summary>
    /// First-run auto-probe. Writes sensible defaults into
    /// <paramref name="s"/> so a fresh laptop comes up with a smaller
    /// memory cap than a workstation. Returns <c>true</c> when fields
    /// were changed (caller saves) and <c>false</c> when the settings
    /// already have explicit values. Reads the resolved preset bundle
    /// from <see cref="ResourceBudget.ResolveDefaults"/> in Core.
    /// </summary>
    public static bool AutoProbeDefaults(SessionSettings s)
    {
        bool changed = false;
        var resolved = ResourceBudget.ResolveDefaults(s.ResourceMode);

        if (s.MaxParallelism == 0)
        {
            s.MaxParallelism = resolved.MaxCores;
            changed = true;
        }
        if (s.MemoryBudget_MB == 0 && resolved.MemoryBudget_MB > 0)
        {
            s.MemoryBudget_MB = resolved.MemoryBudget_MB;
            changed = true;
        }
        return changed;
    }
}
