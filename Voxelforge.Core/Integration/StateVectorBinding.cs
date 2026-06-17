// StateVectorBinding.cs — Phase 1 foundation for #557 item 1.
//
// Maps an IStatefulComponent's `StateVariables` (string list) to flat
// `double[]` indices. Future phases of the dict→array migration:
//
//   Phase 1 (this commit): binding type + cache helper. No integrator
//     change yet — purely additive. Lets a follow-up PR migrate
//     TimeStepIntegrator internals without designing the index map
//     under pressure.
//
//   Phase 2 (next PR, tracked separately): replace
//     `Dictionary<string, Dictionary<string, double>> _state` (and the
//     6 sibling per-step buffers) in TimeStepIntegrator with
//     `Dictionary<string, double[]>`. The flat arrays sit on the
//     binding's name→index map. ComputeDerivatives still takes the
//     existing dict-based shape; the integrator builds the temp dict
//     from the array at the IStatefulComponent boundary. The CN
//     residual loop + RK4 stage adds become array-indexed (the perf
//     win — saves the dict hash-lookup on every iteration).
//
//   Phase 3: replace the IStatefulComponent dict surface with span-
//     based methods. Each of the 7 production components and the
//     test fixtures migrate to read state directly from
//     ReadOnlySpan<double>. Closes the last per-call dict allocation.
//
//   Phase 4: same treatment on the port-value side
//     (ComponentNetwork.Solve and friends — `Dictionary<string,
//     Dictionary<string, double>>` port maps become flat arrays).
//
// Phase 1's scope is intentionally small: ship the type + a cache
// helper. Zero behaviour change; zero perf change. The architectural
// decision the type encodes (binding is per-component, computed once
// at RegisterStateful time, immutable thereafter) is the load-bearing
// piece a Phase 2 needs to commit to.

using System;
using System.Collections.Generic;

namespace Voxelforge.Integration;

/// <summary>
/// Immutable mapping from a stateful component's `StateVariables`
/// names to flat `double[]` indices. One per registered
/// <see cref="IStatefulComponent"/>. Phase 1 of the #557 item 1
/// flatten — the integrator does not yet use bindings at runtime;
/// the type exists so Phase 2 can plumb it through without rebuilding
/// the index map under pressure.
/// </summary>
/// <param name="ComponentName">The owning component's
/// <see cref="SystemComponent.Name"/>.</param>
/// <param name="VariableNames">Ordered state-variable names. Same
/// instance as <see cref="IStatefulComponent.StateVariables"/>.</param>
/// <param name="NameToIndex">Per-variable name → index lookup.
/// Indexes into a flat <c>double[]</c> of length
/// <paramref name="VariableNames"/>.Count.</param>
internal sealed record StateVectorBinding(
    string                              ComponentName,
    IReadOnlyList<string>               VariableNames,
    IReadOnlyDictionary<string, int>    NameToIndex)
{
    /// <summary>
    /// Compute a binding from a stateful component. Walks
    /// <see cref="IStatefulComponent.StateVariables"/> once and
    /// builds the index map. Throws if the component declares
    /// duplicate variable names (illegal — the integrator would
    /// produce ambiguous state-vector positions).
    /// </summary>
    public static StateVectorBinding Compute(string componentName, IStatefulComponent component)
    {
        ArgumentNullException.ThrowIfNull(componentName);
        ArgumentNullException.ThrowIfNull(component);

        var vars = component.StateVariables
            ?? throw new InvalidOperationException(
                $"Component '{componentName}' returned a null StateVariables list.");

        var map = new Dictionary<string, int>(vars.Count, StringComparer.Ordinal);
        for (int i = 0; i < vars.Count; i++)
        {
            string name = vars[i] ?? throw new InvalidOperationException(
                $"Component '{componentName}' has a null state-variable name at index {i}.");
            if (!map.TryAdd(name, i))
            {
                throw new InvalidOperationException(
                    $"Component '{componentName}' declares duplicate state-variable '{name}' " +
                    $"(positions {map[name]} and {i}). State-variable names must be unique.");
            }
        }

        return new StateVectorBinding(
            ComponentName: componentName,
            VariableNames: vars,
            NameToIndex:   map);
    }

    /// <summary>Length of the flat state vector this binding indexes.</summary>
    public int VariableCount => VariableNames.Count;

    /// <summary>
    /// Phase 2 ergonomic helper — copy values from a name-keyed dict
    /// into the flat array slot the binding describes. The integrator
    /// will use this at the IStatefulComponent boundary until Phase 3
    /// migrates the components themselves.
    /// </summary>
    public void CopyDictToArray(IReadOnlyDictionary<string, double> source, Span<double> destination)
    {
        if (destination.Length < VariableNames.Count)
            throw new ArgumentException(
                $"Destination span length {destination.Length} < binding variable count {VariableNames.Count}",
                nameof(destination));
        for (int i = 0; i < VariableNames.Count; i++)
        {
            string name = VariableNames[i];
            destination[i] = source.TryGetValue(name, out double v)
                ? v
                : throw new InvalidOperationException(
                    $"Source dict missing state variable '{name}' (component '{ComponentName}')");
        }
    }

    /// <summary>
    /// Phase 2 ergonomic helper — fan a flat array into a name-keyed
    /// dict. Reuses the destination dict's allocation; assumes the
    /// dict's keys already match <see cref="VariableNames"/> (the
    /// integrator's pre-keyed buffer pool from #610 guarantees this).
    /// </summary>
    public void CopyArrayToDict(ReadOnlySpan<double> source, IDictionary<string, double> destination)
    {
        if (source.Length < VariableNames.Count)
            throw new ArgumentException(
                $"Source span length {source.Length} < binding variable count {VariableNames.Count}",
                nameof(source));
        for (int i = 0; i < VariableNames.Count; i++)
            destination[VariableNames[i]] = source[i];
    }
}
