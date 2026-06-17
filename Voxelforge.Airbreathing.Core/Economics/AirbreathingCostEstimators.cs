// AirbreathingCostEstimators.cs — Sprint EC.W8 cluster-anchored cost
// estimators for the 10 airbreathing-pillar engine kinds.
//
// Pricing data is heavily kind-dependent — a $1.5M J79 turbojet is
// orders of magnitude different from a $200 valveless pulsejet at
// the same nominal thrust. Cluster mid-band (2026):
//
//   Ramjet                        — $400/N      (simple inlet+combustor+nozzle)
//   Turbojet (J79-class)          — $2000/N
//   Turbofan (CFM56-class)        — $4000/N     (bypass duct adds cost)
//   Scramjet (X-43-class)         — $20 000/N   (one-off experimental)
//   RBCC                          — $15 000/N
//   Pulsejet (V-1 / Argus-class)  — $200/N      (very simple)
//   RotatingDetonationEngine      — $5000/N     (research-grade)
//   LiquidAirCycle (LACE)         — $8000/N     (cryogenic complexity)
//
// Shaft-power machines:
//   GasTurbine (LM2500-class)     — $400/kW shaft
//   SteamTurbine (Rankine)        — $1200/kW shaft
//   Turboprop (T56-class)         — $800/kW shaft
//   Turboshaft (T700-class)       — $600/kW shaft
//
// Mass: 0.10-0.20 kg/N for jet engines; 5-10 kg/kW shaft for
// industrial turbines; 0.02 kg/N for pulsejets.

using System;
using Voxelforge.Economics;

namespace Voxelforge.Airbreathing.Economics;

/// <summary>
/// Cluster-anchored cost / mass / CO₂ factories for airbreathing
/// engine kinds (Sprint EC.W8).
/// </summary>
internal static class AirbreathingCostEstimators
{
    private const double Co2PerKg_JetEngine = 14.0;
    private const double Co2PerKg_GasTurbine = 12.0;

    /// <summary>
    /// Estimate cost of an airbreathing engine from a solved result.
    /// The factory dispatches on
    /// <see cref="AirbreathingEngineKind"/>; thrust-based machines
    /// price per Newton and shaft-power machines price per kW.
    /// </summary>
    public static CostEstimate ForAirbreathingEngine(string componentName,
        AirbreathingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        double thrust_N      = Math.Max(0.0, result.Stations.ThrustNet_N);
        double shaftPower_kW = Math.Max(0.0, result.ShaftPower_W) / 1000.0;

        return result.Design.Kind switch
        {
            AirbreathingEngineKind.Ramjet             => ThrustBased(componentName, thrust_N, 400.0,  0.08),
            AirbreathingEngineKind.Turbojet           => ThrustBased(componentName, thrust_N, 2000.0, 0.15),
            AirbreathingEngineKind.Turbofan           => ThrustBased(componentName, thrust_N, 4000.0, 0.20),
            AirbreathingEngineKind.Scramjet           => ThrustBased(componentName, thrust_N, 20_000.0, 0.10),
            AirbreathingEngineKind.Rbcc               => ThrustBased(componentName, thrust_N, 15_000.0, 0.15),
            AirbreathingEngineKind.Pulsejet           => ThrustBased(componentName, thrust_N, 200.0,  0.02),
            AirbreathingEngineKind.RotatingDetonation => ThrustBased(componentName, thrust_N, 5000.0, 0.08),
            AirbreathingEngineKind.LiquidAirCycle     => ThrustBased(componentName, thrust_N, 8000.0, 0.20),
            AirbreathingEngineKind.GasTurbine         => ShaftBased(componentName, shaftPower_kW, 400.0,  6.0),
            AirbreathingEngineKind.SteamTurbine       => ShaftBased(componentName, shaftPower_kW, 1200.0, 10.0),
            AirbreathingEngineKind.Turboprop          => ShaftBased(componentName, shaftPower_kW, 800.0,  4.0),
            AirbreathingEngineKind.Turboshaft         => ShaftBased(componentName, shaftPower_kW, 600.0,  3.0),
            _ => throw new ArgumentOutOfRangeException(nameof(result),
                $"No cost data for AirbreathingEngineKind '{result.Design.Kind}'."),
        };
    }

    private static CostEstimate ThrustBased(string name, double thrust_N,
        double dollarPerN, double kgPerN)
    {
        double mass_kg = thrust_N * kgPerN;
        return new CostEstimate(
            ComponentName:        name,
            Mass_kg:              mass_kg,
            CapitalCost_USD:      thrust_N * dollarPerN,
            EmbodiedCO2_kgCO2eq:  mass_kg * Co2PerKg_JetEngine);
    }

    private static CostEstimate ShaftBased(string name, double power_kW,
        double dollarPerKW, double kgPerKW)
    {
        double mass_kg = power_kW * kgPerKW;
        return new CostEstimate(
            ComponentName:        name,
            Mass_kg:              mass_kg,
            CapitalCost_USD:      power_kW * dollarPerKW,
            EmbodiedCO2_kgCO2eq:  mass_kg * Co2PerKg_GasTurbine);
    }
}
