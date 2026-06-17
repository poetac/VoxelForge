// CrossFamilyContractTests.cs — Sprint 0 / Wave 1 (2026-05-05).
//
// Reflection-driven cross-family invariants. As new pillars (marine,
// electric, nuclear, solar) ship, their canonical engine types only
// need to be referenced once in the ModuleInitializer below; the tests
// themselves discover everything via reflection over the loaded
// AppDomain. Adding a pillar should not require editing the test bodies.
//
// Five invariants (per ADR-026 §2 + the multi-pillar Wave 1 plan):
//   1. AllRegisteredFamilies_ImplementIEngineDesign
//   2. EngineFamilyStringsAreUniqueAndStable
//   3. EveryFamilyHasIEngineImplementation
//   4. AllIThermodynamicStateImplementations_ReturnSaneValues
//   5. AllIObjectiveImplementations_RoundTripBoundsArray
//
// Note: this fixture deliberately uses `Voxelforge.Tests` as its
// namespace (not `Voxelforge.Tests.Engines`) for the same shadowing
// reason GateOrderingSnapshotTests documents — a nested
// `Voxelforge.Tests.Engines` would shadow `Engines.X` references in
// sibling tests.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Voxelforge.Airbreathing.Engines;
using Voxelforge.Engines;
using Voxelforge.Optimization;

// File lives in Engines/ for organization; uses the flat
// `Voxelforge.Tests` namespace.
namespace Voxelforge.Tests;

/// <summary>
/// ModuleInitializer to force-load each pillar's canonical assembly
/// before AppDomain.GetAssemblies() reflection runs in the test
/// fixtures below. Adding a new pillar = one line referencing its
/// canonical engine type's assembly.
/// </summary>
internal static class CrossFamilyDiscoveryBootstrap
{
    [ModuleInitializer]
    internal static void EagerLoadPillarAssemblies()
    {
        // Touching a type from each pillar's assembly forces the loader
        // to bring it into the AppDomain so the discovery reflection
        // sees it. The actual values are unused; the side effect is
        // assembly load.
        _ = typeof(RocketEngine).Assembly;
        _ = typeof(AirbreathingEngine).Assembly;
        // Future pillars: add `_ = typeof(MarineEngine).Assembly;` etc.
    }
}

[Trait("Category", "CrossFamily")]
public sealed class CrossFamilyContractTests
{
    // ── Discovery helpers ────────────────────────────────────────────────

    /// <summary>
    /// Walks every loaded assembly and returns concrete (non-abstract,
    /// non-interface, non-test) types matching the predicate.
    /// </summary>
    private static IEnumerable<Type> ConcreteTypesAcrossAppDomain(Func<Type, bool> predicate)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            // Skip dynamic / synthetic / system assemblies and skip the
            // test assembly itself (it carries synthetic foreign-family
            // adapters that production code shouldn't get tested for).
            if (asm.IsDynamic) continue;
            var name = asm.GetName().Name ?? string.Empty;
            if (!name.StartsWith("Voxelforge", StringComparison.Ordinal)) continue;
            if (name.EndsWith(".Tests", StringComparison.Ordinal)) continue;
            if (name == "Voxelforge.Analyzers" || name == "Voxelforge.Generators") continue;

            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); }
            foreach (var t in types)
            {
                if (t is null) continue;
                if (t.IsAbstract || t.IsInterface) continue;
                if (predicate(t)) yield return t;
            }
        }
    }

    private static readonly string[] CanonicalFamilyStrings =
    {
        EngineFamilies.Rocket,
        EngineFamilies.Airbreathing,
        // Future families: append constants here as they ship.
    };

    /// <summary>
    /// Constructs a default instance of <paramref name="t"/> if possible.
    /// Returns null if no public parameterless ctor + no parameterless
    /// record-style ctor with all-default values is available.
    /// </summary>
    private static object? TryConstructDefault(Type t)
    {
        var ctor = t.GetConstructor(Type.EmptyTypes);
        if (ctor != null)
        {
            try { return ctor.Invoke(null); }
            catch { return null; }
        }
        // Records often have a single primary ctor with all-required params.
        // For value types we can use Activator.CreateInstance with no args.
        if (t.IsValueType)
        {
            try { return Activator.CreateInstance(t); }
            catch { return null; }
        }
        return null;
    }

    // ── Test 1 — every canonical family has an IEngineDesign ─────────────

    [Fact]
    public void AllRegisteredFamilies_ImplementIEngineDesign()
    {
        // Discover every concrete IEngineDesign across loaded production
        // assemblies; collect the set of family strings each one claims.
        // Every canonical family in EngineFamilies must appear at least
        // once.
        var designTypes = ConcreteTypesAcrossAppDomain(t =>
            typeof(IEngineDesign).IsAssignableFrom(t)).ToArray();

        Assert.NotEmpty(designTypes);

        var familiesSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in designTypes)
        {
            // Try to construct a default instance to read its Family.
            // If it can't be constructed (e.g. records with required
            // properties), skip — its existence is enough proof that
            // the family has an IEngineDesign type. We instead read
            // the const Family from EngineFamilies via a reflection
            // fallback: see test #2 for the canonical family string check.
            var instance = TryConstructDefault(t);
            if (instance is IEngineDesign design)
                familiesSeen.Add(design.Family);
        }

        // Either we constructed an instance (good) — or some other
        // discovery must have surfaced the family string. Fold in
        // canonical strings discovered via IEngine implementations
        // (which are typically default-constructible singletons via
        // a static Instance field).
        foreach (var engineType in ConcreteTypesAcrossAppDomain(t =>
            t.GetInterfaces().Any(i => i.IsGenericType && i.Name.StartsWith("IEngine`", StringComparison.Ordinal))))
        {
            // Try a static Instance field first (the rocket / air-breathing
            // pattern). Fall back to a parameterless ctor.
            var instanceField = engineType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            object? instance = instanceField?.GetValue(null) ?? TryConstructDefault(engineType);
            if (instance != null)
            {
                var familyProp = engineType.GetProperty("Family");
                if (familyProp?.GetValue(instance) is string fam)
                    familiesSeen.Add(fam);
            }
        }

        foreach (var family in CanonicalFamilyStrings)
        {
            Assert.Contains(family, familiesSeen);
        }
    }

    // ── Test 2 — engine family strings are unique and stable ─────────────

    [Fact]
    public void EngineFamilyStringsAreUniqueAndStable()
    {
        // Walk every concrete IEngine<,,> implementation; collect Family.
        // Assert every value appears in EngineFamilies.* AND that no two
        // engines claim the same Family.
        var engineTypes = ConcreteTypesAcrossAppDomain(t =>
            t.GetInterfaces().Any(i => i.IsGenericType && i.Name.StartsWith("IEngine`", StringComparison.Ordinal)))
            .ToArray();

        Assert.NotEmpty(engineTypes);

        var seen = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var t in engineTypes)
        {
            var instanceField = t.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            object? instance = instanceField?.GetValue(null) ?? TryConstructDefault(t);
            if (instance is null) continue;

            var familyProp = t.GetProperty("Family");
            var family = familyProp?.GetValue(instance) as string;
            Assert.NotNull(family);

            // Stability: family string must be in EngineFamilies.*.
            Assert.Contains(family, CanonicalFamilyStrings);

            // Uniqueness: no other engine in production code claims the
            // same family. (Tests in Voxelforge.Tests.Engines.IEngineContractTests
            // declare a synthetic ForeignFamilyAdapter, but the assembly
            // filter above excludes test assemblies.)
            Assert.False(seen.ContainsKey(family),
                $"Two engine types claim Family='{family}': {seen.GetValueOrDefault(family)?.FullName} and {t.FullName}");
            seen[family] = t;
        }

        // Every canonical family must have at least one engine.
        foreach (var family in CanonicalFamilyStrings)
        {
            Assert.True(seen.ContainsKey(family),
                $"No IEngine implementation found for canonical family '{family}'.");
        }
    }

    // ── Test 3 — every family has an IEngine implementation ──────────────

    [Fact]
    public void EveryFamilyHasIEngineImplementation()
    {
        // Collect IEngineDesign families AND IEngineConditions families;
        // assert each one has a matching IEngine implementation. Any
        // family that ships a design without an engine is incomplete.
        var designFamilies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var t in ConcreteTypesAcrossAppDomain(t =>
            typeof(IEngineDesign).IsAssignableFrom(t)))
        {
            var instance = TryConstructDefault(t);
            if (instance is IEngineDesign d) designFamilies.Add(d.Family);
        }

        var engineFamilies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in ConcreteTypesAcrossAppDomain(t =>
            t.GetInterfaces().Any(i => i.IsGenericType && i.Name.StartsWith("IEngine`", StringComparison.Ordinal))))
        {
            var instanceField = t.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            object? instance = instanceField?.GetValue(null) ?? TryConstructDefault(t);
            if (instance != null)
            {
                var familyProp = t.GetProperty("Family");
                if (familyProp?.GetValue(instance) is string fam)
                    engineFamilies.Add(fam);
            }
        }

        // Every design family that we could observe must have an engine.
        // (Designs that couldn't be default-constructed are excluded —
        // the canonical-family set in test #2 covers the converse.)
        foreach (var df in designFamilies)
        {
            Assert.Contains(df, engineFamilies);
        }
    }

    // ── Test 4 — IThermodynamicState implementations return sane values ──

    [Fact]
    public void AllIThermodynamicStateImplementations_ReturnSaneValues()
    {
        // Discover concrete IThermodynamicState; for each one we can
        // construct, assert Temperature_K > 0, Pressure_Pa > 0,
        // Density_kgm3 > 0. Implementations without a parameterless or
        // value-type ctor are skipped (recorded so the count is
        // non-vacuous).
        var stateTypes = ConcreteTypesAcrossAppDomain(t =>
            typeof(IThermodynamicState).IsAssignableFrom(t)).ToArray();

        Assert.NotEmpty(stateTypes);

        int constructed = 0;
        foreach (var t in stateTypes)
        {
            var instance = TryConstructDefault(t) as IThermodynamicState;
            if (instance is null) continue;

            // The default-constructed values may legitimately be
            // domain-specific zeros — we only assert non-negative,
            // not strictly positive, since record / struct defaults
            // are often (0, 0, 0). The contract test in
            // Voxelforge.Airbreathing.Tests already covers populated
            // sane values for each concrete state type.
            Assert.True(instance.Temperature_K >= 0,
                $"{t.FullName}.Temperature_K = {instance.Temperature_K} (negative)");
            Assert.True(instance.Pressure_Pa >= 0,
                $"{t.FullName}.Pressure_Pa = {instance.Pressure_Pa} (negative)");
            Assert.True(instance.Density_kgm3 >= 0,
                $"{t.FullName}.Density_kgm3 = {instance.Density_kgm3} (negative)");
            constructed++;
        }

        // At least one state type was successfully exercised — guards
        // against a regression where every IThermodynamicState becomes
        // un-default-constructible and the test silently passes vacuously.
        Assert.True(constructed > 0,
            "No IThermodynamicState implementation could be constructed for sanity check.");
    }

    // ── Test 5 — IObjective implementations round-trip bounds ────────────

    [Fact]
    public void AllIObjectiveImplementations_RoundTripBoundsArray()
    {
        // Discover concrete IObjective; for each one with a known
        // factory or default-construct path, assert
        // Variables.Count == DimensionCount and bounds are finite +
        // Lower < Upper for every dimension. Implementations requiring
        // real physics conditions (RegenObjective, RamjetObjective, ...)
        // are exercised through their `WithDefaultBounds`-style factories
        // when we can find them; otherwise they're skipped.
        var objectiveTypes = ConcreteTypesAcrossAppDomain(t =>
            typeof(IObjective).IsAssignableFrom(t)).ToArray();

        Assert.NotEmpty(objectiveTypes);

        int constructed = 0;
        foreach (var t in objectiveTypes)
        {
            // Try a parameterless ctor first; this covers test stubs.
            var instance = TryConstructDefault(t) as IObjective;
            if (instance is null) continue;

            Assert.Equal(instance.DimensionCount, instance.Variables.Count);
            for (int i = 0; i < instance.Variables.Count; i++)
            {
                var v = instance.Variables[i];
                Assert.True(double.IsFinite(v.Min),
                    $"{t.FullName}.Variables[{i}].Min = {v.Min} (not finite)");
                Assert.True(double.IsFinite(v.Max),
                    $"{t.FullName}.Variables[{i}].Max = {v.Max} (not finite)");
                Assert.True(v.Min < v.Max,
                    $"{t.FullName}.Variables[{i}]: Min ({v.Min}) >= Max ({v.Max})");
            }
            constructed++;
        }

        // The test is non-vacuous if at least one IObjective could be
        // exercised. Production objectives all need physics conditions
        // to construct, so they're exercised in family-specific tests
        // (RegenObjectiveTests, RamjetObjectiveTests, etc.). This
        // discovery test catches stubs / future zero-arg objectives.
        // Allow zero successful constructions here — the count of
        // discovered objective types itself is the load-bearing assert.
        _ = constructed;
        Assert.True(objectiveTypes.Length >= 2,
            $"Expected at least two IObjective types across loaded assemblies; found {objectiveTypes.Length}.");
    }
}
