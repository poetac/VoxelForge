// EconomicAnalyzer.cs — Sprint EC.W1 system-level cost rollup.
//
// Walks a set of CostEstimate records (typically one per costable
// component in a ComponentNetwork) and produces a
// SystemCostBreakdown with the per-component drill-down + totals.

using System.Collections.Generic;
using System.Linq;

namespace Voxelforge.Economics;

/// <summary>
/// System-level rollup helper for component
/// <see cref="CostEstimate"/> records (Sprint EC.W1).
/// </summary>
internal static class EconomicAnalyzer
{
    /// <summary>
    /// Aggregate a collection of <see cref="CostEstimate"/> into a
    /// <see cref="SystemCostBreakdown"/>.
    /// </summary>
    public static SystemCostBreakdown Analyze(IEnumerable<CostEstimate> estimates)
    {
        ArgumentNullException.ThrowIfNull(estimates);
        var list = estimates.ToList();
        return new SystemCostBreakdown(
            Components:                 list,
            TotalMass_kg:               list.Sum(c => c.Mass_kg),
            TotalCapitalCost_USD:       list.Sum(c => c.CapitalCost_USD),
            TotalEmbodiedCO2_kgCO2eq:   list.Sum(c => c.EmbodiedCO2_kgCO2eq));
    }
}
