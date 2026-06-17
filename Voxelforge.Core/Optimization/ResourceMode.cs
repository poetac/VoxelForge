// ResourceMode.cs — Headline preset for how much of the user's
// machine the solver is allowed to saturate.
//
// A preset bundles a few individual knobs (MaxParallelism, memory
// budget, priority demotion) so users pick a mood rather than four
// independent numbers. The explicit numbers are still persisted
// alongside so a user can nudge an individual knob if the preset's
// default doesn't match their machine.
//
// Defaults are auto-probed on first run via
// `ResourceBudget.AutoProbeDefaults()` — a 16-core / 32 GB dev box
// comes up Balanced with cores=14, memory=16 GB; a 4-core laptop
// comes up Quiet with cores=2, memory=4 GB.

namespace Voxelforge.UI;

public enum ResourceMode
{
    /// <summary>Laptop / battery: 25 % cores, 25 % RAM cap, BelowNormal priority.</summary>
    Quiet,
    /// <summary>Workstation default: ~75 % cores, 50 % RAM cap, BelowNormal priority while active.</summary>
    Balanced,
    /// <summary>Dedicated run: all cores, no cap, Normal priority.</summary>
    Maximum,
    /// <summary>User-set explicit numbers (MaxParallelism + MemoryBudget_MB). Skips preset resolution.</summary>
    Custom,
}

/// <summary>
/// Preset-table lookup. Returns the resolved (cores, memory_MB,
/// demote_priority) triple a given mode wants on a machine with
/// <paramref name="totalCores"/> cores and <paramref name="totalMemory_MB"/>
/// total RAM. Callers use this at startup (auto-probe) and again
/// whenever the mode changes at runtime.
/// </summary>
public static class ResourcePresets
{
    public readonly record struct Resolved(int MaxCores, int MemoryBudget_MB, bool DemotePriority);

    public static Resolved Resolve(ResourceMode mode, int totalCores, long totalMemory_MB)
    {
        totalCores       = System.Math.Max(totalCores, 1);
        totalMemory_MB   = System.Math.Max(totalMemory_MB, 1024);
        return mode switch
        {
            ResourceMode.Quiet => new Resolved(
                MaxCores:       System.Math.Max(1, totalCores / 4),
                MemoryBudget_MB: (int)System.Math.Min(int.MaxValue, totalMemory_MB / 4),
                DemotePriority: true),

            ResourceMode.Balanced => new Resolved(
                MaxCores:       System.Math.Max(1, (int)System.Math.Round(totalCores * 0.75)),
                MemoryBudget_MB: (int)System.Math.Min(int.MaxValue, totalMemory_MB / 2),
                DemotePriority: true),

            ResourceMode.Maximum => new Resolved(
                MaxCores:       totalCores,
                MemoryBudget_MB: 0,                // 0 = no cap
                DemotePriority: false),

            // Custom: caller is expected to use SessionSettings.
            // MaxParallelism / MemoryBudget_MB directly.  We still
            // return a sane fallback so misuse doesn't crash.
            _ => new Resolved(
                MaxCores:       System.Math.Max(1, totalCores - 2),
                MemoryBudget_MB: (int)System.Math.Min(int.MaxValue, totalMemory_MB / 2),
                DemotePriority: true),
        };
    }
}
