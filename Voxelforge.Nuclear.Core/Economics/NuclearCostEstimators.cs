// NuclearCostEstimators.cs — Sprint EC.W10 cluster-anchored cost
// estimator for the Nuclear pillar (NERVA-class solid-core NTR).
//
// Cost is dominated by fuel enrichment tier. Cluster-anchored 2026
// figures per kg of fuel in the core (uranium fuel-pin loaded mass):
//
//   LEU   (~5 % U-235):   $1 800/kg-fuel,  20 kgCO₂/kg
//   HALEU (~19.75 %):     $25 000/kg-fuel, 100 kgCO₂/kg
//   HEU   (>= 20 %):      $250 000/kg-fuel, 500 kgCO₂/kg
//                                            (security + handling)
//
// Engine hardware (nozzle + pressure shell + manifold) adds a flat
// $5M per unit for a NERVA-class NTR — the reactor + control system
// dwarfs the LPBF print-cost line for the chamber.
//
// Fuel mass derived from ReactorCoreVolume_m3 × FuelLoadingFraction
// × ρ_UN (12 g/cm³ default; cluster anchor for uranium nitride).

using System;
using Voxelforge.Economics;
using Voxelforge.Nuclear;

namespace Voxelforge.Nuclear.Economics;

/// <summary>
/// Cluster-anchored cost estimator for the Nuclear pillar (Sprint
/// EC.W10).
/// </summary>
internal static class NuclearCostEstimators
{
    /// <summary>Cluster anchor for uranium nitride fuel density [kg/m³].</summary>
    private const double UraniumNitrideDensity_kgm3 = 12_000.0;
    /// <summary>Flat reactor / pressure-shell / control hardware $/unit.</summary>
    private const double EngineHardware_USD = 5_000_000.0;
    /// <summary>Flat reactor / pressure-shell / control hardware mass [kg].</summary>
    private const double EngineHardware_kg  = 8_000.0;
    /// <summary>Hardware embodied CO₂ [kg/unit].</summary>
    private const double EngineHardware_CO2kg = 35_000.0;

    /// <summary>
    /// Estimate cost of an NTR from a solved generation result.
    /// </summary>
    public static CostEstimate ForNtrEngine(string componentName,
        NtrGenerationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // Fuel mass: V_core × loading fraction × ρ_UN.
        double fuelMass_kg = result.Design.ReactorCoreVolume_m3
                           * result.Design.FuelLoadingFraction
                           * UraniumNitrideDensity_kgm3;

        var (dollarPerKgFuel, co2PerKgFuel) = result.Design.EnrichmentTier switch
        {
            // None defaults to HEU per Wave-1 NU.W5 (legacy behaviour).
            UraniumEnrichment.None  => (250_000.0, 500.0),
            UraniumEnrichment.LEU   => (  1_800.0,  20.0),
            UraniumEnrichment.HALEU => ( 25_000.0, 100.0),
            UraniumEnrichment.HEU   => (250_000.0, 500.0),
            _ => throw new ArgumentOutOfRangeException(nameof(result),
                $"No cost data for enrichment '{result.Design.EnrichmentTier}'."),
        };

        double fuelCapex = fuelMass_kg * dollarPerKgFuel;
        double fuelCo2   = fuelMass_kg * co2PerKgFuel;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              fuelMass_kg + EngineHardware_kg,
            CapitalCost_USD:      fuelCapex + EngineHardware_USD,
            EmbodiedCO2_kgCO2eq:  fuelCo2 + EngineHardware_CO2kg);
    }
}
