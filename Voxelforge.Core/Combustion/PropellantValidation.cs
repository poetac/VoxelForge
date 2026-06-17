// PropellantValidation.cs — Single entry point for hard-failing when a
// user (or a loaded design)
// selects a propellant pair whose CEA table is a stub, or whose coolant
// fluid key is not wired into CoolantRegistry. Prior behaviour was a
// silent fallback to LOX/CH4 + CH4 coolant, which let users optimise
// under a completely different physics model than intended.
//
// Call Validate(pair) before any call that forwards into
// PropellantTables.Lookup / CoolantRegistry.Get. The canonical call
// site is RegenChamberOptimization.GenerateWith; UI also queries
// IsImplemented to disable Generate / Start Opt.

using Voxelforge.Coolant;

namespace Voxelforge.Combustion;

/// <summary>
/// Error codes surfaced on <see cref="UnsupportedPropellantException"/>.
/// Kept as string constants rather than an enum so the values are stable
/// across versions for log scraping.
/// </summary>
public static class PropellantValidationCode
{
    public const string PairNotImplemented    = "PAIR_NOT_IMPLEMENTED";
    public const string CoolantKeyUnknown     = "COOLANT_KEY_UNKNOWN";
}

public sealed class UnsupportedPropellantException : InvalidOperationException
{
    public string Code { get; }
    public PropellantPair Pair { get; }
    public string? CoolantFluidKey { get; }

    public UnsupportedPropellantException(
        string code, PropellantPair pair, string? coolantFluidKey, string message)
        : base(message)
    {
        Code = code;
        Pair = pair;
        CoolantFluidKey = coolantFluidKey;
    }
}

public static class PropellantValidation
{
    // Cache of pairs that have already passed EnsureSupported. The SA
    // hot path calls this on
    // every candidate even though the propellant pair is invariant
    // across an SA run. Cache hit → noop; first call per pair runs
    // the full validation. PropellantPair is an enum, so the set is
    // bounded (~6 entries today) — backed by a thread-safe
    // ConcurrentDictionary so parallel SA batches don't race.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<PropellantPair, byte> _validated = new();

    /// <summary>
    /// Validate that the propellant pair has a populated CEA table AND
    /// a coolant fluid module that <see cref="CoolantRegistry"/> can
    /// resolve. Throws <see cref="UnsupportedPropellantException"/> on
    /// any violation. Returns normally on success.
    /// <para>
    /// Sprint 16 / Track J / P12: subsequent calls with the same
    /// <paramref name="pair"/> short-circuit on a cached "already
    /// validated" flag (the SA hot path triggers thousands of redundant
    /// invocations otherwise). The cache only stores pairs that
    /// successfully validated — failure paths still throw every time.
    /// </para>
    /// </summary>
    public static void EnsureSupported(PropellantPair pair)
    {
        if (_validated.ContainsKey(pair)) return;

        var meta = PropellantPairs.GetMeta(pair);

        if (!meta.Implemented)
            throw new UnsupportedPropellantException(
                PropellantValidationCode.PairNotImplemented,
                pair,
                meta.CoolantFluidKey,
                $"Propellant pair {meta.Name} is declared but its CEA table " +
                $"is not populated. Generation is blocked to avoid a silent " +
                $"fallback to LOX/CH4 physics. Pick an implemented pair " +
                $"(LOX/CH4, LOX/H2, LOX/RP-1).");

        if (!CoolantRegistry.IsKnown(meta.CoolantFluidKey))
            throw new UnsupportedPropellantException(
                PropellantValidationCode.CoolantKeyUnknown,
                pair,
                meta.CoolantFluidKey,
                $"Propellant pair {meta.Name} references coolant fluid key " +
                $"'{meta.CoolantFluidKey}' which is not registered in " +
                $"CoolantRegistry. Add a Coolant/{meta.CoolantFluidKey}Fluid.cs " +
                $"module or correct the metadata before generating.");

        // Validation passed — cache for subsequent calls.
        _validated.TryAdd(pair, 0);
    }

    /// <summary>
    /// Sprint 16 / Track J / P12 helper: clear the validated-pair cache.
    /// Tests use this when they want EnsureSupported to re-run its full
    /// check rather than short-circuit on a previous test's success
    /// (mostly for negative-test isolation).
    /// </summary>
    public static void ClearValidatedCache() => _validated.Clear();

    /// <summary>
    /// Non-throwing variant for UI gating. Returns null on success, or a
    /// short reason string on failure (suitable for a red banner).
    /// </summary>
    public static string? Explain(PropellantPair pair)
    {
        try { EnsureSupported(pair); return null; }
        catch (UnsupportedPropellantException ex) { return ex.Message; }
    }

    public static bool IsSupported(PropellantPair pair) => Explain(pair) is null;
}
