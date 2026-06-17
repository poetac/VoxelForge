// ElectricPropulsionCostEstimators.cs — Sprint EC.W9 cluster-anchored
// cost estimators for the 6 EP kinds.
//
// Cost basis is INPUT ELECTRIC POWER, derived from the result via:
//     P_in = ½ · F · V_exit / η_T
// where F is thrust, V_exit is exit velocity, η_T is thrust efficiency.
//
// Per-kind cluster pricing (2026 mid-band, space-qualified flight
// hardware):
//
//   Resistojet   (MR-501B-class):   $400/W input, 0.010 kg/W
//   Hall Effect  (BPT-4000-class):  $100/W,       0.002 kg/W
//   Arcjet       (MR-509-class):    $150/W,       0.003 kg/W
//   PulsedPlasma (EO-1-class):      $2500/W avg,  0.040 kg/W
//                                                 (pulsed → inflated per
//                                                  average-power figure)
//   GriddedIon   (NSTAR-class):     $500/W,       0.005 kg/W
//   MagnetoPlasmaDynamic (research): $50/W,       0.0015 kg/W
//                                                 (large-scale; cheap per
//                                                  W at the MW scale)

using System;
using Voxelforge.Economics;

namespace Voxelforge.ElectricPropulsion.Economics;

/// <summary>
/// Cluster-anchored cost / mass / CO₂ factories for electric-propulsion
/// engine kinds (Sprint EC.W9).
/// </summary>
internal static class ElectricPropulsionCostEstimators
{
    private const double Co2PerKg_EpThruster = 16.0;

    /// <summary>
    /// Estimate cost of an electric-propulsion thruster from a solved
    /// result.
    /// </summary>
    public static CostEstimate ForElectricPropulsionThruster(
        string componentName,
        ElectricPropulsionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        double thrust_N = Math.Max(0.0, result.Thrust_N);
        double V_exit   = Math.Max(0.0, result.ExitVelocity_ms);
        double eta_T    = Math.Max(1e-9, result.ThrustEfficiency);
        // P_in = ½ · F · V_exit / η_T
        double inputPower_W = 0.5 * thrust_N * V_exit / eta_T;

        var (dollarPerW, kgPerW) = result.Design.Kind switch
        {
            ElectricPropulsionEngineKind.Resistojet           => (400.0,  0.010),
            ElectricPropulsionEngineKind.HallEffect           => (100.0,  0.002),
            ElectricPropulsionEngineKind.Arcjet               => (150.0,  0.003),
            ElectricPropulsionEngineKind.PulsedPlasmaThruster => (2500.0, 0.040),
            ElectricPropulsionEngineKind.GriddedIon           => (500.0,  0.005),
            ElectricPropulsionEngineKind.MagnetoPlasmaDynamic => (50.0,   0.0015),
            _ => throw new ArgumentOutOfRangeException(nameof(result),
                $"No cost data for ElectricPropulsionEngineKind '{result.Design.Kind}'."),
        };

        double mass_kg = inputPower_W * kgPerW;
        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              mass_kg,
            CapitalCost_USD:      inputPower_W * dollarPerW,
            EmbodiedCO2_kgCO2eq:  mass_kg * Co2PerKg_EpThruster);
    }
}
