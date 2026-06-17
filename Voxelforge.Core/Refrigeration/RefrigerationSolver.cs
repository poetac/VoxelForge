// RefrigerationSolver.cs — Sprint RFG.W1 closed-form vapor-compression
// refrigeration / heat-pump performance snapshot.
//
// Stateless, allocation-free, deterministic. The Wave-1 model uses
// the canonical Carnot-bounded 2nd-law approach:
//
//   COP_Carnot,cooling = T_cold / (T_hot − T_cold)
//   COP_Carnot,heating = T_hot / (T_hot − T_cold)   (= cooling + 1)
//   COP_cooling        = η_2nd · COP_Carnot,cooling
//   COP_heating        = COP_cooling + 1
//   Q_cold = COP_cooling · W_compressor
//   Q_hot  = Q_cold + W_compressor                  (energy balance)
//
// η_2nd-law is the per-refrigerant cluster fit — the ratio of real-
// cycle COP to Carnot. Cluster mid-band 0.50-0.65 for vapor-compression
// systems running near the design point.
//
// References:
//   ASHRAE Handbook — Refrigeration (2022).
//   Cengel Y., Boles M. (2014). "Thermodynamics: An Engineering
//     Approach," 8th ed., chap 11 (refrigeration cycles).
//   Stoecker W.F. (1998). "Industrial Refrigeration Handbook."

using System;

namespace Voxelforge.Refrigeration;

/// <summary>
/// Closed-form vapor-compression refrigeration / heat-pump solver
/// (Sprint RFG.W1).
/// </summary>
internal static class RefrigerationSolver
{
    /// <summary>
    /// Solve the refrigeration cycle performance snapshot at the design
    /// (T_cold, T_hot, refrigerant, W) operating point.
    /// </summary>
    internal static RefrigerationResult Solve(RefrigerationDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var props = RefrigerantRegistry.For(design.Refrigerant);

        // 1. Carnot bounds.
        double dT = design.HotReservoirTemperature_K - design.ColdReservoirTemperature_K;
        double cop_carnot_cooling = design.ColdReservoirTemperature_K / dT;
        double cop_carnot_heating = design.HotReservoirTemperature_K  / dT;

        // 2. Real cycle COPs. Sprint RFG.W2 — subcooling boosts COP by
        //    ~ 0.6 %/K (cluster mid-band; Cengel chap 11 state-point
        //    analysis); superheat reduces COP by ~ 0.2 %/K. Both default
        //    to 0 → bit-identical RFG.W1 behaviour.
        double subcoolingBoost  = 1.0 + 0.006 * design.SubcoolingDepth_K;
        double superheatPenalty = 1.0 - 0.002 * design.SuperheatDepth_K;
        double cop_cooling = props.SecondLawEfficiency * cop_carnot_cooling
                           * subcoolingBoost * superheatPenalty;
        double cop_heating = cop_cooling + 1.0;

        // 3. Heat fluxes from the compressor work + COPs.
        double Q_cold = cop_cooling * design.CompressorPowerInput_W;
        double Q_hot  = Q_cold + design.CompressorPowerInput_W;

        return new RefrigerationResult(
            CarnotCoolingCop:        cop_carnot_cooling,
            CarnotHeatingCop:        cop_carnot_heating,
            CoolingCop:              cop_cooling,
            HeatingCop:              cop_heating,
            ColdSideHeatRemoval_W:   Q_cold,
            HotSideHeatDelivery_W:   Q_hot);
    }
}
