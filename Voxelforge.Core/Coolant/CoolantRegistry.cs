// CoolantRegistry.cs — Single lookup point for the thermal solver to
// resolve a coolant fluid from the propellant-pair metadata's
// CoolantFluidKey. Adding a new fluid is a two-line operation:
//
//   1. Create `Coolant/XxxFluid.cs` implementing TabulatedCoolantFluid.
//   2. Add the `case "Xxx": return XxxFluid.Instance;` arm below.
//
// The UI never touches this registry directly — it reads the key from
// the selected propellant pair's metadata. This keeps coolant selection
// logically tied to propellant choice and avoids the cross-product
// combinatorial explosion.

namespace Voxelforge.Coolant;

/// <summary>
/// Single lookup point that resolves an <see cref="ICoolantFluid"/>
/// implementation from the propellant-pair metadata's
/// <c>CoolantFluidKey</c> string. The thermal solver calls
/// <see cref="Get"/> at the start of each <c>Solve()</c> invocation; the
/// UI never calls this registry directly — the key is read from the
/// selected propellant pair so coolant choice stays logically tied to
/// propellant choice.
/// <para>
/// Adding a new fluid is a two-line operation:
/// </para>
/// <list type="number">
///   <item><description>Create <c>Coolant/XxxFluid.cs</c> implementing
///   <see cref="TabulatedCoolantFluid"/>.</description></item>
///   <item><description>Add a <c>case "Xxx": return XxxFluid.Instance;</c>
///   arm to <see cref="Get"/> + extend <see cref="IsKnown"/> + extend
///   <see cref="All"/>.</description></item>
/// </list>
/// <para>
/// Unknown keys throw <see cref="InvalidOperationException"/> rather
/// than silently falling back to methane — silent fallback would
/// optimise under an unintended physics model.
/// </para>
/// </summary>
public static class CoolantRegistry
{
    /// <summary>
    /// Resolve a coolant-fluid singleton by its propellant-metadata key.
    /// Recognised keys: "CH4", "H2", "RP-1".
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="fluidKey"/> is not registered. The
    /// message identifies the unknown key and points the caller at the
    /// extension recipe in this class's summary.
    /// </exception>
    public static ICoolantFluid Get(string fluidKey)
    {
        return fluidKey switch
        {
            "CH4"   => MethaneFluid.Instance,
            "H2"    => HydrogenFluid.Instance,
            "RP-1"  => RP1Fluid.Instance,
            _       => throw new InvalidOperationException(
                $"Coolant fluid key '{fluidKey}' is not registered in CoolantRegistry. " +
                $"Add a matching Coolant/*Fluid.cs module or correct the propellant-pair metadata. " +
                $"Unknown keys are rejected rather than silently falling back to methane, which " +
                $"would optimise under an unintended physics model."),
        };
    }

    /// <summary>
    /// Non-throwing variant of <see cref="Get"/>. Returns true when the
    /// key is registered. Useful for validation / UI population paths
    /// where an exception would be heavyweight.
    /// </summary>
    public static bool IsKnown(string fluidKey) => fluidKey switch
    {
        "CH4" or "H2" or "RP-1" => true,
        _ => false,
    };

    /// <summary>
    /// Snapshot of every registered coolant fluid. Returned as a fresh
    /// array on each get so callers cannot mutate the registry through
    /// the property. Order is stable (CH4, H2, RP-1) for deterministic
    /// enumeration in tests + UI lists.
    /// </summary>
    public static ICoolantFluid[] All => new ICoolantFluid[]
    {
        MethaneFluid.Instance,
        HydrogenFluid.Instance,
        RP1Fluid.Instance,
    };
}
