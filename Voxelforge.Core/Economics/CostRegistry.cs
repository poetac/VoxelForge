// CostRegistry.cs — Sprint EC.W3 lazy registry that pairs with a
// ComponentNetwork for system-level cost rollups.
//
// Users build a registry alongside their network, register a
// CostEstimate factory per costable component, and call
// BuildBreakdown() to get a SystemCostBreakdown.
//
//   var net    = new ComponentNetwork();
//   var costs  = new CostRegistry();
//   var design = ModelSPack();
//   net.Add(new BatteryComponent("pack", design));
//   costs.Register("pack",
//       () => ComponentCostEstimators.ForBattery("pack", design));
//   // … more components …
//   var breakdown = costs.BuildBreakdown();
//
// Kept as a standalone helper (not bolted onto ComponentNetwork) to
// avoid coupling the Integration namespace to Economics.

using System;
using System.Collections.Generic;

namespace Voxelforge.Economics;

/// <summary>
/// Lazy registry of <see cref="CostEstimate"/> factories keyed by
/// component name (Sprint EC.W3).
/// </summary>
internal sealed class CostRegistry
{
    private readonly Dictionary<string, Func<CostEstimate>> _factories = new();

    /// <summary>Number of registered cost factories.</summary>
    public int Count => _factories.Count;

    /// <summary>
    /// Register a cost-estimate factory for the named component. Each
    /// call to BuildBreakdown re-invokes the factory; use this to thread
    /// design-state changes (e.g. swept chemistry / power rating)
    /// through the rollup without rebuilding the registry.
    /// </summary>
    public void Register(string componentName, Func<CostEstimate> factory)
    {
        ArgumentNullException.ThrowIfNull(componentName);
        ArgumentNullException.ThrowIfNull(factory);
        if (_factories.ContainsKey(componentName))
            throw new InvalidOperationException(
                $"Component '{componentName}' is already registered.");
        _factories[componentName] = factory;
    }

    /// <summary>
    /// Invoke every registered factory + roll up into a
    /// <see cref="SystemCostBreakdown"/>.
    /// </summary>
    public SystemCostBreakdown BuildBreakdown()
    {
        // Iterate registered components in a deterministic, culture-invariant
        // order (Dictionary enumeration order is not a contract) so the
        // resulting SystemCostBreakdown.Components list is stable across runs.
        var keys = new List<string>(_factories.Keys);
        keys.Sort(StringComparer.Ordinal);
        var estimates = new List<CostEstimate>(keys.Count);
        foreach (var key in keys)
            estimates.Add(_factories[key]());
        return EconomicAnalyzer.Analyze(estimates);
    }
}
