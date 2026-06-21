// SystemCostBreakdown.cs — Sprint EC.W1 aggregate cost summary
// produced by EconomicAnalyzer.Analyze.
//
// Sums Mass / CapitalCost / EmbodiedCO2 across all component
// estimates fed into the analyzer; surfaces the individual
// breakdown for drill-down.

using System.Collections.Generic;
using System.Linq;

namespace Voxelforge.Economics;

/// <summary>
/// Aggregate cost / mass / carbon rollup across a set of
/// <see cref="CostEstimate"/> records (Sprint EC.W1).
/// </summary>
/// <param name="Components">Per-component breakdown.</param>
/// <param name="TotalMass_kg">Σ component masses [kg].</param>
/// <param name="TotalCapitalCost_USD">Σ component capex [USD].</param>
/// <param name="TotalEmbodiedCO2_kgCO2eq">Σ component embodied
/// CO₂-eq [kg].</param>
internal sealed record SystemCostBreakdown(
    IReadOnlyList<CostEstimate> Components,
    double TotalMass_kg,
    double TotalCapitalCost_USD,
    double TotalEmbodiedCO2_kgCO2eq)
{
    /// <summary>
    /// Render the breakdown as a multi-line table for log / console
    /// output.
    /// </summary>
    public string ToTable()
    {
        // InvariantCulture so the rendered numbers (and any consumer that
        // diffs/snapshots this table) are byte-stable regardless of the host
        // locale's decimal separator — matching the CSV/Sobol output paths.
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Component               Mass_kg       Cost_USD        CO2_kg");
        sb.AppendLine("──────────────────────  ───────────  ───────────  ───────────");
        foreach (var c in Components.OrderByDescending(c => c.CapitalCost_USD))
            sb.AppendLine(string.Format(ci, "{0,-22}  {1,11:F1}  {2,11:F0}  {3,11:F0}",
                c.ComponentName, c.Mass_kg, c.CapitalCost_USD, c.EmbodiedCO2_kgCO2eq));
        sb.AppendLine("──────────────────────  ───────────  ───────────  ───────────");
        sb.AppendLine(string.Format(ci, "{0,-22}  {1,11:F1}  {2,11:F0}  {3,11:F0}",
            "Total", TotalMass_kg, TotalCapitalCost_USD, TotalEmbodiedCO2_kgCO2eq));
        return sb.ToString();
    }
}
