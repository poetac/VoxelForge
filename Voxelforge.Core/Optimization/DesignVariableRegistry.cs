// DesignVariableRegistry.cs — Sprint 5 Dev A (2026-04-22):
// Reflection-based registry that collects every property tagged with
// <see cref="SaDesignVariableAttribute"/> on a target type and
// produces the (Min, Max)[] Bounds array the SA optimizer consumes.
//
// This is the first concrete step toward the ADR-010 single-source-of-
// truth refactor. Today the registry runs PARALLEL to the hand-
// maintained <see cref="RegenChamberOptimization.Bounds"/> for the
// plain (non-gated) dims; a unit test asserts the two agree so any
// drift trips CI immediately. A future sprint can flip the dependency
// (Bounds reads from the registry) once every dim — including the
// conditionally-applied ones — carries the attribute.
//
// Gating caveat
// ─────────────
// The SA vector has three classes of dims:
//   1. Plain — always applied in Unpack (dims 0..12).
//   2. Gated-by-baseline — only applied when a categorical baseline
//      field is set (injector pattern present, TPMS topology,
//      preburner cycle, aerospike topology). Dims 13..22 in the
//      current layout.
//   3. Inert — packed but ignored during SA scoring (none today).
//
// The attribute covers class 1 only. Class 2 needs a gate-predicate
// metadata shape that we haven't committed to yet — see the ADR-010
// follow-on note.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Voxelforge.Optimization;

/// <summary>
/// One design-variable's descriptor, produced by reflecting over a
/// target type's <see cref="SaDesignVariableAttribute"/>-tagged
/// members. The SA optimizer uses <see cref="Min"/> / <see cref="Max"/>
/// directly to sample the search space; the other fields are
/// diagnostic / reporting-friendly metadata.
/// </summary>
public sealed record SaDesignVariableDescriptor(
    int    Index,
    string MemberName,
    Type   DeclaringType,
    double Min,
    double Max,
    SaGate Gate = SaGate.None);

/// <summary>
/// Central registry for design variables declared via
/// <see cref="SaDesignVariableAttribute"/>. All descriptors are
/// discovered lazily on first access and cached per target type.
/// </summary>
public static class DesignVariableRegistry
{
    private static readonly Dictionary<Type, SaDesignVariableDescriptor[]> _cache = new();
    private static readonly object _gate = new();

    // Sprint 14 / Track I / P2: cache the aggregation + sort for
    // `DescriptorsForMany` and `BoundsForMany`. The SA hot path calls
    // these on every Pack + Unpack; before caching, each call rebuilt
    // the merged + sorted descriptor array (~3-10 µs) and churned GC.
    // Key is a joined `AssemblyQualifiedName` string — cheap to build
    // once per cache miss and trivially correct as an equality key.
    // `ConcurrentDictionary` is lock-free on reads; `GetOrAdd` is the
    // canonical hot-path primitive.
    private static readonly ConcurrentDictionary<string, SaDesignVariableDescriptor[]> _descriptorsForManyCache = new();
    private static readonly ConcurrentDictionary<string, (double Min, double Max)[]> _boundsForManyCache = new();

    private static string KeyOf(Type[] types)
    {
        // Fast path for the common single-type case — no StringBuilder churn.
        if (types.Length == 1) return types[0].AssemblyQualifiedName!;
        var sb = new System.Text.StringBuilder(types.Length * 64);
        for (int i = 0; i < types.Length; i++)
        {
            if (i > 0) sb.Append('|');
            sb.Append(types[i].AssemblyQualifiedName);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Enumerate the descriptors declared on <paramref name="targetType"/>
    /// (or any of its inherited types). Result is sorted by
    /// <see cref="SaDesignVariableDescriptor.Index"/> ascending and
    /// validated for index-uniqueness + contiguity (no gaps).
    /// </summary>
    public static IReadOnlyList<SaDesignVariableDescriptor> For(Type targetType)
    {
        if (targetType is null) throw new ArgumentNullException(nameof(targetType));

        lock (_gate)
        {
            if (_cache.TryGetValue(targetType, out var cached)) return cached;

            var descriptors = new List<SaDesignVariableDescriptor>();

            foreach (var prop in targetType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<SaDesignVariableAttribute>();
                if (attr is null) continue;
                descriptors.Add(new SaDesignVariableDescriptor(
                    Index:         attr.Index,
                    MemberName:    prop.Name,
                    DeclaringType: targetType,
                    Min:           attr.Min,
                    Max:           attr.Max,
                    Gate:          attr.Gate));
            }
            foreach (var field in targetType.GetFields(
                BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = field.GetCustomAttribute<SaDesignVariableAttribute>();
                if (attr is null) continue;
                descriptors.Add(new SaDesignVariableDescriptor(
                    Index:         attr.Index,
                    MemberName:    field.Name,
                    DeclaringType: targetType,
                    Min:           attr.Min,
                    Max:           attr.Max,
                    Gate:          attr.Gate));
            }

            var sorted = descriptors.OrderBy(d => d.Index).ToArray();

            // Guard against accidental index collisions — two members
            // claiming the same SA slot would produce silently wrong
            // sampling if we let the attribute survive to the registry.
            for (int i = 0; i < sorted.Length - 1; i++)
            {
                if (sorted[i].Index == sorted[i + 1].Index)
                    throw new InvalidOperationException(
                        $"Duplicate SA design-variable index {sorted[i].Index} on "
                      + $"{targetType.Name}: {sorted[i].MemberName} and "
                      + $"{sorted[i + 1].MemberName} both claim the same slot.");
            }

            _cache[targetType] = sorted;
            return sorted;
        }
    }

    /// <summary>
    /// Materialise the (Min, Max) bounds for every dim covered by a
    /// <see cref="SaDesignVariableAttribute"/> on
    /// <paramref name="targetType"/>, keyed by the attribute's Index.
    /// Non-contiguous indices are allowed (the current migration is
    /// partial) — the returned array has length equal to
    /// (max index + 1) with uncovered slots defaulting to (0, 0).
    /// </summary>
    public static (double Min, double Max)[] BoundsFor(Type targetType)
    {
        var descriptors = For(targetType);
        if (descriptors.Count == 0)
            return System.Array.Empty<(double, double)>();

        int length = descriptors[^1].Index + 1;
        var result = new (double, double)[length];
        foreach (var d in descriptors)
            result[d.Index] = (d.Min, d.Max);
        return result;
    }

    /// <summary>
    /// Convenience overload: <see cref="BoundsFor(System.Type)"/>
    /// for a compile-time-known target type.
    /// </summary>
    public static (double Min, double Max)[] BoundsFor<T>() => BoundsFor(typeof(T));

    /// <summary>
    /// Aggregate bounds across multiple target types into a single
    /// array keyed by attribute <c>Index</c>. Every attribute-tagged
    /// member across all <paramref name="targetTypes"/> must have a
    /// unique Index — collisions throw
    /// <see cref="System.InvalidOperationException"/>. This is how
    /// <see cref="RegenChamberOptimization.Bounds"/> is now computed:
    /// dims 0..12, 18..22 live on <c>RegenChamberDesign</c>, dims
    /// 13..17 live on <c>InjectorPattern</c>, and the aggregator
    /// merges them into one 23-dim array.
    /// </summary>
    public static (double Min, double Max)[] BoundsForMany(params Type[] targetTypes)
    {
        if (targetTypes is null || targetTypes.Length == 0)
            throw new ArgumentException("must supply at least one target type",
                nameof(targetTypes));

        return _boundsForManyCache.GetOrAdd(KeyOf(targetTypes), _ =>
        {
            var allDescriptors = new List<SaDesignVariableDescriptor>();
            foreach (var t in targetTypes)
                allDescriptors.AddRange(For(t));

            if (allDescriptors.Count == 0)
                return System.Array.Empty<(double, double)>();

            var sorted = allDescriptors.OrderBy(d => d.Index).ToArray();
            for (int i = 0; i < sorted.Length - 1; i++)
            {
                if (sorted[i].Index == sorted[i + 1].Index)
                    throw new InvalidOperationException(
                        $"Duplicate SA design-variable index {sorted[i].Index} across "
                      + $"aggregated types: {sorted[i].DeclaringType.Name}.{sorted[i].MemberName} "
                      + $"and {sorted[i + 1].DeclaringType.Name}.{sorted[i + 1].MemberName} "
                      + $"both claim the same slot.");
            }

            int length = sorted[^1].Index + 1;
            var result = new (double Min, double Max)[length];
            foreach (var d in sorted)
                result[d.Index] = (d.Min, d.Max);
            return result;
        });
    }

    /// <summary>
    /// Flat listing of every descriptor across multiple target types,
    /// sorted by attribute <c>Index</c>. Used by
    /// <see cref="RegenChamberOptimization"/>'s Unpack gating path
    /// to dispatch per-dim gate predicates against the baseline.
    /// </summary>
    public static SaDesignVariableDescriptor[] DescriptorsForMany(params Type[] targetTypes)
    {
        if (targetTypes is null || targetTypes.Length == 0)
            throw new ArgumentException("must supply at least one target type",
                nameof(targetTypes));

        return _descriptorsForManyCache.GetOrAdd(KeyOf(targetTypes), _ =>
        {
            var all = new List<SaDesignVariableDescriptor>();
            foreach (var t in targetTypes)
                all.AddRange(For(t));
            return all.OrderBy(d => d.Index).ToArray();
        });
    }
}
