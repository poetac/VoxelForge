// DesignVariableBinder.cs — Sprint 7 Track C (2026-04-22):
// Registry-driven Pack / Unpack implementation for the SA search
// vector. This closes the second half of ADR-010 — the half that
// migrates the hand-coded typed-field access in
// RegenChamberOptimization.Pack / Unpack to reflection-based
// attribute-driven iteration.
//
// Design overview
// ───────────────
//  • Pack(design, pattern) builds a 23-dim double[] by iterating
//    DesignVariableRegistry.DescriptorsForMany against both
//    RegenChamberDesign and InjectorPattern. Each descriptor's
//    DeclaringType selects which source object to read from; when
//    the source is unavailable (pattern is null for an
//    InjectorPatternPresent descriptor) the vector slot receives the
//    record's default value via a cached zero-argument factory.
//  • Unpack(packed, baseline) rebuilds the baseline by cloning via
//    the compiler-generated <Clone>$ method and applying each
//    descriptor's sampled value through PropertyInfo.SetValue —
//    which DOES work on init-only records at runtime (the `init`
//    restriction is compile-time only; .NET 9 reflection bypasses
//    it cleanly, verified in-repo before this commit landed).
//  • Gate predicates govern which descriptors are actually applied:
//    None / InjectorPatternPresent / TpmsTopology / AerospikeTopology
//    match the SaGate enum on the attribute.
//
// Why a reflection rewrite pays off
// ─────────────────────────────────
// Adding a new SA variable used to require a coordinated edit across
// Pack, Unpack, Bounds, and at least one test (ADR-010 original
// pain). Sprint 6 Track A moved Bounds into the registry so that's
// solved. This track moves Pack + Unpack so the FULL ADR-010 promise
// lands: add a new SA dim → one attribute annotation on the record
// field → no other code change required. Tests that assert specific
// behaviour on specific dims still need updating, but the plumbing
// is a one-liner.
//
// Cache + thread safety
// ─────────────────────
// PropertyInfo / <Clone>$ MethodInfo / default-instance factories
// are cached per target type. Cache access is lock-free after the
// first lookup via System.Threading.Interlocked.CompareExchange.
// Reflection metadata is thread-safe by itself; the cache layer adds
// process-wide memoisation without synchronisation cost.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Voxelforge.Optimization;

/// <summary>
/// Registry-driven Pack / Unpack for the SA search vector. Closes the
/// ADR-010 "Pack + Unpack hand-coded" debt in Sprint 7 Track C.
/// </summary>
public static partial class DesignVariableBinder
{
    // Per-type caches. Built lazily; values are immutable once set.
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propsByType = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo?> _cloneByType    = new();
    private static readonly ConcurrentDictionary<Type, object>      _defaultsByType = new();

    // Sprint 16 / Track J / P1 (2026-04-22): compiled getter/setter
    // delegates per (declaring-type, member-name). The audit measured
    // PropertyInfo.GetValue/SetValue at ~300-1000 ns per call (~7-20 µs
    // per Unpack × 2000 SA iters → 20-40 ms per SA run on reflection
    // alone). Expression.Compile() produces JIT-optimised lambdas
    // (~10-50 ns per call), a ~20-50× speedup. Init-only properties
    // work the same as in the PropertyInfo path — `init` is a C# 9
    // compile-time-only restriction; the IL setter is callable from
    // anywhere reflection or Expression-tree assignment can reach.
    private sealed class PropertyAccessor
    {
        public readonly Func<object, object?>  Getter;
        public readonly Action<object, object> Setter;
        public readonly Type                   PropertyType;

        public PropertyAccessor(Func<object, object?> getter, Action<object, object> setter, Type propertyType)
        {
            Getter       = getter;
            Setter       = setter;
            PropertyType = propertyType;
        }
    }
    private static readonly ConcurrentDictionary<string, PropertyAccessor> _accessorsByKey = new();

    private static PropertyAccessor AccessorFor(Type targetType, string memberName)
    {
        // Key = type + "|" + member. AssemblyQualifiedName guarantees
        // uniqueness across loaded assemblies.
        string key = targetType.AssemblyQualifiedName + "|" + memberName;
        return _accessorsByKey.GetOrAdd(key, _ =>
        {
            // Issue #159 (T1.4): consult the source-generated accessor
            // table first. When present, both getter + setter are
            // compile-time-resolved direct property access (getter is
            // a typed cast + property read; setter is an [UnsafeAccessor]
            // direct callvirt). This is the AOT-clean fast path. The
            // Expression.Compile fallback below remains for tests /
            // synthetic types not visible to the generator.
            string fqnKey = targetType.FullName + "|" + memberName;
            if (GeneratedAccessors.TryGetValue("global::" + fqnKey, out var gen)
             || GeneratedAccessors.TryGetValue(fqnKey, out gen))
            {
                return new PropertyAccessor(
                    getter:       gen.Getter,
                    setter:       gen.Setter,
                    propertyType: gen.PropertyType);
            }

            var prop = PropertyFor(targetType, memberName);
            return new PropertyAccessor(
                getter:       BuildGetter(prop, targetType),
                setter:       BuildSetter(prop, targetType),
                propertyType: prop.PropertyType);
        });
    }

    private static Func<object, object?> BuildGetter(PropertyInfo prop, Type targetType)
    {
        // (object source) => (object) ((TTarget)source).PropertyName
        var sourceParam = Expression.Parameter(typeof(object), "source");
        var castSource  = Expression.Convert(sourceParam, targetType);
        var propAccess  = Expression.Property(castSource, prop);
        var box         = Expression.Convert(propAccess, typeof(object));
        return Expression.Lambda<Func<object, object?>>(box, sourceParam).Compile();
    }

    private static Action<object, object> BuildSetter(PropertyInfo prop, Type targetType)
    {
        // (object target, object value) => ((TTarget)target).PropertyName = (TPropType)value;
        // Expression.Assign on a property compiles to a callvirt against
        // the property's set_ method — bypasses init-only the same way
        // PropertyInfo.SetValue does at runtime.
        var targetParam = Expression.Parameter(typeof(object), "target");
        var valueParam  = Expression.Parameter(typeof(object), "value");
        var castTarget  = Expression.Convert(targetParam, targetType);
        var castValue   = Expression.Convert(valueParam, prop.PropertyType);
        var propAccess  = Expression.Property(castTarget, prop);
        var assign      = Expression.Assign(propAccess, castValue);
        return Expression.Lambda<Action<object, object>>(assign, targetParam, valueParam).Compile();
    }

    // Sprint 14 / Track I / P18: pre-construct the only default that
    // matters in the SA hot path. `Pack` calls `DefaultInstance` every
    // time the InjectorPattern source is null; the prior code went
    // through a `ConcurrentDictionary` lookup + Activator.CreateInstance
    // on cache miss. The static `new()` here is plain `new` (not
    // reflection) and the fast path below avoids the dict lookup
    // entirely for this type.
    private static readonly Injector.InjectorPattern _injectorPatternDefault = new();

    /// <summary>
    /// Pack a (design, pattern) pair into a double[] indexed by
    /// attribute Index. Dims whose DeclaringType is RegenChamberDesign
    /// read from <paramref name="design"/>; dims whose DeclaringType is
    /// InjectorPattern read from <paramref name="pattern"/> or a
    /// default-constructed InjectorPattern when pattern is null.
    /// </summary>
    public static double[] Pack(RegenChamberDesign design, Injector.InjectorPattern? pattern)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));

        var descriptors = DesignVariableRegistry.DescriptorsForMany(
            typeof(RegenChamberDesign), typeof(Injector.InjectorPattern));
        if (descriptors.Length == 0) return Array.Empty<double>();

        int length = descriptors[^1].Index + 1;
        var vec = new double[length];

        // Pre-resolve the default instances once so we don't re-reflect
        // per descriptor when the source record is null.
        object patternSource = pattern ?? DefaultInstance(typeof(Injector.InjectorPattern));

        // Hoist GetType() out of the descriptor loop — source is always one
        // of two known references, both with a single runtime type.
        Type designType  = design.GetType();
        Type patternType = patternSource.GetType();

        foreach (var d in descriptors)
        {
            bool isDesign = d.DeclaringType == typeof(RegenChamberDesign);
            object source     = isDesign ? design     : patternSource;
            Type   sourceType = isDesign ? designType : patternType;
            // Sprint 16 / Track J / P1: compiled getter — replaces the
            // PropertyInfo.GetValue reflection call from Sprint 7 Track C.
            var accessor = AccessorFor(sourceType, d.MemberName);
            object? raw = accessor.Getter(source);
            vec[d.Index] = ToDouble(raw);
        }
        return vec;
    }

    /// <summary>
    /// Unpack a packed vector onto <paramref name="baseline"/>,
    /// returning a new RegenChamberDesign with the sampled values
    /// applied. Gate predicates decide which descriptors are actually
    /// written — descriptors whose gate says "not applicable against
    /// this baseline" (e.g. TPMS dims on an Axial baseline) are left
    /// at the baseline's current value.
    /// </summary>
    /// <remarks>
    /// Thin shim over <see cref="Unpack(ReadOnlySpan{double}, RegenChamberDesign)"/>
    /// that preserves the array-input call shape used by every
    /// non-IObjective caller (UI / batch SA / DOE benchmarks / Pack-Unpack
    /// round-trip tests). Adds a null-array guard the span overload
    /// can't express, then delegates to the span body so both shapes
    /// share one implementation.
    /// </remarks>
    public static RegenChamberDesign Unpack(double[] packed, RegenChamberDesign baseline)
    {
        if (packed   is null) throw new ArgumentNullException(nameof(packed));
        if (baseline is null) throw new ArgumentNullException(nameof(baseline));
        return Unpack((ReadOnlySpan<double>)packed, baseline);
    }

    /// <summary>
    /// Span-input overload of <see cref="Unpack(double[], RegenChamberDesign)"/>.
    /// Reads the candidate vector directly out of a
    /// <see cref="ReadOnlySpan{Double}"/> so callers (notably
    /// <see cref="IObjective.Evaluate(ReadOnlySpan{double}, System.Threading.CancellationToken)"/>
    /// implementations on the SA hot path) never have to materialise a
    /// throwaway <c>double[]</c> just to satisfy the array-shaped API.
    /// </summary>
    /// <remarks>
    /// At ~5 M Evaluate calls per SA session × ~248 B per 31-dim
    /// <c>double[]</c> the prior <c>ToArray()</c> burned roughly 1.2 GB
    /// of Gen0 garbage per session. This span path is allocation-free.
    /// Behaviour is otherwise byte-identical to the array overload —
    /// same gate predicates, same clamp-to-bounds, same compiled
    /// getter / setter accessors, same record-clone semantics.
    /// </remarks>
    public static RegenChamberDesign Unpack(ReadOnlySpan<double> packed, RegenChamberDesign baseline)
    {
        if (baseline is null) throw new ArgumentNullException(nameof(baseline));

        var descriptors = DesignVariableRegistry.DescriptorsForMany(
            typeof(RegenChamberDesign), typeof(Injector.InjectorPattern));
        if (descriptors.Length == 0) return baseline;

        // Shallow-clone the baseline record. <Clone>$ is a compiler-
        // generated protected method on every record — we access it via
        // reflection and invoke through the cached MethodInfo.
        var result = (RegenChamberDesign)CloneRecord(baseline)!;

        // Injector-pattern clone is built lazily, only when at least one
        // injector descriptor is applied. Keeps the non-pattern case
        // allocation-free.
        Injector.InjectorPattern? patternClone = null;

        // Hoist GetType() out of the descriptor loop — result's runtime
        // type is fixed for the duration of the loop; the pattern clone's
        // type is captured the first time we materialise it.
        Type resultType        = result.GetType();
        Type? patternCloneType = null;

        foreach (var d in descriptors)
        {
            if (!GateAllowsApplication(d.Gate, baseline, d.Index, packed.Length))
                continue;
            double v = ClampToBounds(packed[d.Index], d);

            object target;
            Type   targetType;
            if (d.DeclaringType == typeof(RegenChamberDesign))
            {
                target     = result;
                targetType = resultType;
            }
            else  // InjectorPattern
            {
                if (patternClone is null)
                {
                    // Gate already ensured pattern is non-null; clone it
                    // so we can mutate without touching the baseline.
                    patternClone     = (Injector.InjectorPattern)CloneRecord(baseline.InjectorElementPattern!)!;
                    patternCloneType = patternClone.GetType();
                }
                target     = patternClone;
                targetType = patternCloneType!;
            }

            // Sprint 16 / Track J / P1: compiled setter — replaces
            // PropertyInfo.SetValue. ConvertForProperty still uses the
            // accessor's cached PropertyType so the sample → typed-value
            // coercion is unchanged.
            var accessor = AccessorFor(targetType, d.MemberName);
            object converted = ConvertForProperty(v, accessor.PropertyType);
            accessor.Setter(target, converted);
        }

        if (patternClone is not null)
        {
            // Attach the mutated pattern clone back onto the result via
            // the same compiled-setter fast path.
            var patternAccessor = AccessorFor(
                typeof(RegenChamberDesign),
                nameof(RegenChamberDesign.InjectorElementPattern));
            patternAccessor.Setter(result, patternClone);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────
    //   Internal helpers
    // ─────────────────────────────────────────────────────────────────

    private static bool GateAllowsApplication(
        SaGate gate, RegenChamberDesign baseline, int descriptorIndex, int packedLength)
    {
        // Safety: a shorter packed vector than expected (legacy call
        // site) simply leaves the out-of-range descriptor at baseline.
        if (descriptorIndex >= packedLength) return false;

        return gate switch
        {
            SaGate.None                    => true,
            SaGate.InjectorPatternPresent  => baseline.InjectorElementPattern is not null,
            SaGate.TpmsTopology            =>
                baseline.ChannelTopology is ChannelTopology.TpmsGyroid
                                          or ChannelTopology.TpmsSchwarzP
                                          or ChannelTopology.TpmsSchwarzD,
            // Sprint 26 (2026-04-23) added ChannelTopology.LinearAerospike.
            // The gate originally covered only axisymmetric Aerospike; linear
            // aerospike designs need the same aerospike-specific SA dims
            // (e.g. index 23 AerospikeContractionRatio) to apply, otherwise
            // Unpack silently reverts them to baseline — the exact silent-
            // scope-drift failure mode ADR-010 / pitfall #7 warns about.
            SaGate.AerospikeTopology       =>
                baseline.ChannelTopology is ChannelTopology.Aerospike
                                          or ChannelTopology.LinearAerospike,
            _                              => true,
        };
    }

    /// <summary>
    /// Clamp <paramref name="v"/> into the descriptor's bounds. The
    /// hand-coded Unpack applied per-dim clamps with varying strictness
    /// (e.g. <c>Math.Clamp(p[20], 0.10, 2.00)</c> for PreburnerMrRatio
    /// — WIDER than the attribute bounds so SA can accidentally
    /// explore a bit beyond). The registry-driven path applies the
    /// attribute bounds exactly — tightening where the hand-coded path
    /// was lax. SA samplers already respect the bounds, so this is a
    /// no-op in practice; the tighter envelope is a safety upgrade.
    /// </summary>
    private static double ClampToBounds(double v, SaDesignVariableDescriptor d)
        => Math.Clamp(v, d.Min, d.Max);

    /// <summary>
    /// Convert a sampled double to the property's actual type. Handles
    /// the <c>int ChannelCount</c> case (Math.Round + cast) that the
    /// hand-coded Unpack encoded inline.
    /// </summary>
    private static object ConvertForProperty(double v, Type propType)
    {
        if (propType == typeof(int))     return (int)Math.Round(v);
        if (propType == typeof(long))    return (long)Math.Round(v);
        if (propType == typeof(float))   return (float)v;
        if (propType == typeof(double))  return v;
        // Fall back on IConvertible for any primitive numeric we missed.
        return Convert.ChangeType(v, propType);
    }

    private static double ToDouble(object? value) => value switch
    {
        null     => 0.0,
        double d => d,
        int i    => (double)i,
        long l   => (double)l,
        float f  => (double)f,
        _        => Convert.ToDouble(value),
    };

    private static PropertyInfo PropertyFor(Type targetType, string memberName)
    {
        var props = _propsByType.GetOrAdd(targetType, t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
        foreach (var p in props)
            if (p.Name == memberName) return p;
        throw new InvalidOperationException(
            $"Property {targetType.Name}.{memberName} not found on the public surface.");
    }

    /// <summary>
    /// Shallow-clone a record via its compiler-generated <c>&lt;Clone&gt;$</c>
    /// method. All records have this; it's protected but accessible via
    /// reflection with BindingFlags.NonPublic.
    /// </summary>
    private static object? CloneRecord(object record)
    {
        var type = record.GetType();
        var clone = _cloneByType.GetOrAdd(type, t =>
            t.GetMethod("<Clone>$",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        if (clone is null)
            throw new InvalidOperationException(
                $"Type {type.Name} is not a record (no <Clone>$ method found).");
        return clone.Invoke(record, null);
    }

    /// <summary>
    /// Construct a default instance of <paramref name="type"/> via its
    /// parameterless constructor. Used by Pack when the pattern source
    /// is null (so injector-dim slots still get record-default values).
    /// </summary>
    private static object DefaultInstance(Type type)
    {
        // Sprint 14 / Track I / P18: fast path for the only hot caller —
        // Pack with a null InjectorPattern source. Hits a static-readonly
        // sentinel and skips both the ConcurrentDictionary lookup and
        // the Activator.CreateInstance reflection it would memoise.
        if (type == typeof(Injector.InjectorPattern)) return _injectorPatternDefault;

        return _defaultsByType.GetOrAdd(type, t =>
            Activator.CreateInstance(t)
            ?? throw new InvalidOperationException(
                $"Type {t.Name} has no parameterless constructor — cannot produce a default for the registry binder."));
    }
}
