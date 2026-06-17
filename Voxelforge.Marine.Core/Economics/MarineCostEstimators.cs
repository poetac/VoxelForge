// MarineCostEstimators.cs — Sprint EC.W10 cluster-anchored cost
// estimator for the Marine pillar (AUV displacement hulls).
//
// Marine pillar is mass-driven: the cost equation reduces to hull
// material $/kg × HullMass_kg + an integration / payload allowance.
//
// Cluster-anchored 2026 pricing (REMUS-100 / Bluefin-21 lineage):
//
//   Ti-6Al-4V LPBF hull:   $200/kg, 35 kgCO₂/kg
//   Al-6061 monocoque:     $40/kg,  12 kgCO₂/kg
//   AISI-316L (LPBF):      $80/kg,  10 kgCO₂/kg
//   CFRP composite:        $250/kg, 25 kgCO₂/kg
//
// Integration overhead: a flat $50 000 + 0.5 × (hull capex) for
// sensors, payload bay, thrusters, batteries, control electronics.

using System;
using Voxelforge.Economics;

namespace Voxelforge.Marine.Economics;

/// <summary>
/// Cluster-anchored cost estimator for the Marine pillar (Sprint
/// EC.W10).
/// </summary>
internal static class MarineCostEstimators
{
    /// <summary>
    /// Estimate cost of an AUV from a solved Marine result.
    /// </summary>
    public static CostEstimate ForAuvDisplacement(string componentName,
        MarineResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        double hullMass_kg = Math.Max(0.0, result.HullMass_kg);

        var (dollarPerKg, co2PerKg) = result.Design.MaterialIndex switch
        {
            0 => (200.0, 35.0),   // Ti-6Al-4V LPBF
            1 => ( 40.0, 12.0),   // Al-6061 monocoque
            2 => ( 80.0, 10.0),   // AISI-316L LPBF
            _ => throw new ArgumentOutOfRangeException(nameof(result),
                $"No cost data for MaterialIndex '{result.Design.MaterialIndex}'."),
        };

        // Hull capex + integration overhead.
        double hullCapex = hullMass_kg * dollarPerKg;
        const double IntegrationFlat_USD     = 50_000.0;
        const double IntegrationProportional = 0.50;
        double capex = hullCapex * (1.0 + IntegrationProportional)
                     + IntegrationFlat_USD;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              hullMass_kg,
            CapitalCost_USD:      capex,
            EmbodiedCO2_kgCO2eq:  hullMass_kg * co2PerKg);
    }
}
