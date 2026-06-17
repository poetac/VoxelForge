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
        var estimates = new List<CostEstimate>(_factories.Count);
        foreach (var factory in _factories.Values)
            estimates.Add(factory());
        return EconomicAnalyzer.Analyze(estimates);
    }
}
