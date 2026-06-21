// DesignVariableBinderGetOnlySetterRegressionTests — red-team audit
// (analyzer/generator pass).
//
// Pins the source generator's handling of a GET-ONLY [SaDesignVariable]
// member. The carrier is AntennaLinkDesign.ModulationSchemeIndex:
//
//     [SaDesignVariable(index: 0, ...)]
//     public int ModulationSchemeIndex => ModulationSchemeTable.ToIndex(Modulation);
//
// It is a computed, get-only property — there is NO set_ModulationSchemeIndex
// method on the type. Before the fix, DesignVariableBinderGenerator emitted an
//
//     [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_ModulationSchemeIndex")]
//     private static extern void __Set_..._ModulationSchemeIndex(... value);
//
// extern for EVERY tagged member unconditionally, then wired the generated
// accessor's Setter to call it. [UnsafeAccessor] resolves at JIT time, so the
// broken extern compiled cleanly and only blew up — with an obscure
// MissingMethodException — the first time the setter delegate was actually
// invoked. That is a latent landmine: the moment any code routes a get-only
// design variable through the registry setter path it faults at runtime
// instead of failing fast.
//
// The generator now detects members with no settable accessor and emits a
// setter that throws a clear NotSupportedException naming the member, while
// still emitting a working getter. This test asserts that contract directly
// off the generated GeneratedAccessors table.
//
// Fail-on-old / pass-on-new: against the pre-fix generator the setter throws
// MissingMethodException (wrong type) and Assert.Throws<NotSupportedException>
// fails; against the fixed generator it throws NotSupportedException and passes.

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Voxelforge.Antenna;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Core.Tests;

public class DesignVariableBinderGetOnlySetterRegressionTests
{
    // Reflect the internal generated table into a name->accessor map. The
    // generated GeneratedAccessor type is internal; we read it as a plain
    // object and pull its Getter/Setter via reflection so the test never has to
    // name the generated type.
    private static object GetAccessor(string keySuffix)
    {
        var field = typeof(DesignVariableBinder).GetField(
            "GeneratedAccessors", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var dict = (IDictionary)field!.GetValue(null)!;
        Assert.NotNull(dict);

        object? match = null;
        foreach (DictionaryEntry kv in dict)
        {
            if (((string)kv.Key).EndsWith(keySuffix, StringComparison.Ordinal))
            {
                match = kv.Value;
                break;
            }
        }
        Assert.NotNull(match);
        return match!;
    }

    private static Func<object, object?> Getter(object accessor) =>
        (Func<object, object?>)accessor.GetType().GetProperty("Getter")!.GetValue(accessor)!;

    private static Action<object, object> Setter(object accessor) =>
        (Action<object, object>)accessor.GetType().GetProperty("Setter")!.GetValue(accessor)!;

    private static AntennaLinkDesign SampleDesign() => new(
        TransmitAntennaKind: default,
        ReceiveAntennaKind:  default,
        Frequency_Hz:        2.4e9,
        TransmitPower_W:     10.0,
        LinkDistance_m:      1000.0,
        Modulation:          ModulationScheme.QpskUncoded);

    [Fact]
    public void GetOnlyDesignVariable_Getter_StillReadsTheComputedValue()
    {
        var accessor = GetAccessor("Voxelforge.Antenna.AntennaLinkDesign|ModulationSchemeIndex");
        var design = SampleDesign();

        object? read = Getter(accessor)(design);

        // The generated getter must still bind the computed property — only the
        // (impossible) setter is special-cased.
        Assert.Equal(design.ModulationSchemeIndex, Assert.IsType<int>(read));
    }

    [Fact]
    public void GetOnlyDesignVariable_Setter_ThrowsNotSupported_NotMissingMethod()
    {
        var accessor = GetAccessor("Voxelforge.Antenna.AntennaLinkDesign|ModulationSchemeIndex");
        var design = SampleDesign();

        // Pre-fix this invoked an [UnsafeAccessor] extern bound to a
        // non-existent set_ModulationSchemeIndex and threw MissingMethodException.
        // The fix emits an explicit, eager NotSupportedException instead.
        var ex = Assert.Throws<NotSupportedException>(() => Setter(accessor)(design, 1));
        Assert.Contains("ModulationSchemeIndex", ex.Message, StringComparison.Ordinal);
        Assert.Contains("get-only", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SettableDesignVariable_Setter_StillWritesThroughUnsafeAccessor()
    {
        // Control: a normal init-only [SaDesignVariable] keeps a working setter
        // (the [UnsafeAccessor] path), proving the get-only special-case did not
        // regress the common settable case.
        var accessor = GetAccessor("Voxelforge.Optimization.RegenChamberDesign|ContractionRatio");
        var design = new RegenChamberDesign { ContractionRatio = 6.0 };

        Setter(accessor)(design, 7.5);

        Assert.Equal(7.5, design.ContractionRatio);
    }
}
