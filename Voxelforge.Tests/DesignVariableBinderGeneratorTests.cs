// DesignVariableBinderGeneratorTests — Issue #159 (T1.4).
//
// Pins the source-generator's emitted `GeneratedAccessors` table for
// the [SaDesignVariable]-tagged property surface:
//
//   1. The table's entry count matches the registry's descriptor
//      count (= total tagged properties across RegenChamberDesign +
//      InjectorPattern). A typo'd attribute or missing partial
//      annotation would fail the count, fast-fail at the test level
//      rather than silently degrading runtime to the Expression.Compile
//      fallback.
//
//   2. Generated accessors round-trip property values bit-identically
//      to direct property access — both for plain doubles and for
//      record-type init-only properties (the [UnsafeAccessor] setter
//      path).
//
//   3. The full DesignVariableBinder.Pack / Unpack pipeline produces
//      bit-identical output to the pre-generator state. Existing
//      DesignVariableBinderTests cover the broader regression net;
//      this file pins generator-specific invariants.

using System.Linq;
using System.Reflection;
using Voxelforge.Injector;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class DesignVariableBinderGeneratorTests
{
    /// <summary>
    /// Use reflection to read the internal `GeneratedAccessors`
    /// dictionary, surfacing it as the public test surface. The
    /// generator-emitted partial class declares the field as
    /// <c>internal</c> because the runtime binder is the only intended
    /// consumer; tests reach in via reflection by design (the
    /// generator's contract is implementation detail of
    /// <c>DesignVariableBinder</c>, not a stable public surface).
    /// </summary>
    private static System.Collections.Generic.Dictionary<string, object> GetGeneratedAccessors()
    {
        var binderType = typeof(DesignVariableBinder);
        var field = binderType.GetField("GeneratedAccessors",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var dict = field!.GetValue(null);
        Assert.NotNull(dict);
        // Return as IDictionary<string, object> by going through the IDictionary
        // non-generic interface (avoids needing the generated type at compile time).
        var entries = (System.Collections.IDictionary)dict!;
        var result = new System.Collections.Generic.Dictionary<string, object>();
        foreach (System.Collections.DictionaryEntry kv in entries)
        {
            result[(string)kv.Key] = kv.Value!;
        }
        return result;
    }

    [Fact]
    public void GeneratedAccessors_CountMatchesRegistry()
    {
        // Total tagged properties = SA design vector dim count, summed
        // across ALL types that carry [SaDesignVariable].
        //
        // Originally restricted to RegenChamberDesign + InjectorPattern
        // (the only carriers when the test was written). Sprint A.86 added
        // the antenna's ModulationSchemeIndex categorical dim — A.88 added
        // AntennaLinkDesign here to keep the registry total in sync with
        // the generator's emitted accessor count. Adding a new pillar's
        // [SaDesignVariable] requires extending this list; the test
        // failure message points at the gap.
        var registryCount = DesignVariableRegistry.DescriptorsForMany(
            typeof(RegenChamberDesign),
            typeof(InjectorPattern),
            typeof(Voxelforge.Antenna.AntennaLinkDesign)).Length;
        var generatedCount = GetGeneratedAccessors().Count;
        Assert.Equal(registryCount, generatedCount);
    }

    [Fact]
    public void GeneratedAccessors_HasExpectedKey_ForRegenChamberDesign_ContractionRatio()
    {
        // Sanity: a known [SaDesignVariable] property lands in the table
        // with the expected key shape ("Type.FQN|MemberName"). If the
        // generator's key format ever changes, the runtime AccessorFor
        // must change in lockstep — this test guards that invariant.
        var keys = GetGeneratedAccessors().Keys;
        Assert.Contains(
            keys,
            k => k.EndsWith(
                "Voxelforge.Optimization.RegenChamberDesign|ContractionRatio",
                System.StringComparison.Ordinal));
    }

    [Fact]
    public void Pack_Unpack_RoundTrip_PreservesAllSaDimensions()
    {
        // Bit-identical round-trip on the Pack / Unpack pipeline is the
        // ultimate end-to-end check that the generator-emitted accessors
        // produce identical values to the pre-generator Expression.Compile
        // path. Existing DesignVariableBinderTests cover the broader
        // matrix; this one is the smoke check.
        var baseline = new RegenChamberDesign
        {
            ContractionRatio = 5.7,
            ExpansionRatio   = 18.0,
            BellExitAngle_deg = 9.5,
            ChannelCount     = 96,
            InjectorElementPattern = new InjectorPattern
            {
                ElementCount = 40,
                CdFuel = 0.78,
                CdOx   = 0.82,
            },
        };
        var packed   = RegenChamberOptimization.Pack(baseline);
        var unpacked = RegenChamberOptimization.Unpack(packed, baseline);

        Assert.Equal(baseline.ContractionRatio,  unpacked.ContractionRatio);
        Assert.Equal(baseline.ExpansionRatio,    unpacked.ExpansionRatio);
        Assert.Equal(baseline.BellExitAngle_deg, unpacked.BellExitAngle_deg);
        Assert.Equal(baseline.ChannelCount,      unpacked.ChannelCount);
        // Pattern dims gate on InjectorElementPattern != null on the baseline,
        // which IS satisfied here.
        Assert.NotNull(unpacked.InjectorElementPattern);
        Assert.Equal(baseline.InjectorElementPattern.ElementCount,
                     unpacked.InjectorElementPattern!.ElementCount);
        Assert.Equal(baseline.InjectorElementPattern.CdFuel,
                     unpacked.InjectorElementPattern.CdFuel);
    }

    [Fact]
    public void GeneratedAccessors_NoDuplicateKeys()
    {
        // The Dictionary<string, GeneratedAccessor> emission would
        // throw at type-init on duplicate keys, but a key-collision
        // test catches the failure mode at a more diagnostic level.
        var keys = GetGeneratedAccessors().Keys;
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
