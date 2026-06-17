// CostEstimate.cs — Sprint EC.W1 first-class triplet for cost,
// mass, and embodied carbon across components.
//
// Every component cost estimator emits one of these. The system-
// level EconomicAnalyzer rolls them up into a SystemCostBreakdown.
// All numbers are cluster-anchored 2026 figures; cost data ages —
// refresh the registry tables on each new market reading.

namespace Voxelforge.Economics;

/// <summary>
/// Cluster-anchored cost, mass, and embodied-carbon triplet emitted
/// by a component-level cost estimator (Sprint EC.W1).
/// </summary>
/// <param name="ComponentName">Name of the component this estimate
/// belongs to.</param>
/// <param name="Mass_kg">Component dry mass [kg]. Used for vehicle-
/// integration weight rollups (M-class spacecraft / EV / aircraft).</param>
/// <param name="CapitalCost_USD">Acquisition cost at 2026 cluster-
/// average pricing [USD]. Does NOT include installation, financing,
/// or maintenance — capex only.</param>
/// <param name="EmbodiedCO2_kgCO2eq">Cradle-to-gate embodied carbon
/// dioxide equivalent [kg CO₂-eq]. Mostly material extraction +
/// manufacturing energy.</param>
internal sealed record CostEstimate(
    string ComponentName,
    double Mass_kg,
    double CapitalCost_USD,
    double EmbodiedCO2_kgCO2eq);
