// RefrigerationResult.cs — Sprint RFG.W1 solver output.

namespace Voxelforge.Refrigeration;

/// <summary>
/// Solve-time outputs for a refrigeration / heat-pump cycle snapshot
/// (Sprint RFG.W1).
/// </summary>
/// <param name="CarnotCoolingCop">COP_Carnot,cooling = T_c / (T_h − T_c) [-]
/// — the upper bound on cooling COP.</param>
/// <param name="CarnotHeatingCop">COP_Carnot,heating = T_h / (T_h − T_c) [-]
/// — the upper bound on heating COP. Always = CarnotCoolingCop + 1.</param>
/// <param name="CoolingCop">Real cooling COP = η_2nd-law · COP_Carnot,cooling
/// [-]. Figure of merit when <see cref="RefrigerationMode.Cooling"/>.</param>
/// <param name="HeatingCop">Real heating COP = CoolingCop + 1 [-]. Figure of
/// merit when <see cref="RefrigerationMode.Heating"/>.</param>
/// <param name="ColdSideHeatRemoval_W">Q_cold = COP_cooling · W [W] —
/// heat extracted from the cold reservoir.</param>
/// <param name="HotSideHeatDelivery_W">Q_hot = Q_cold + W [W] — heat
/// delivered to the hot reservoir (energy balance).</param>
internal sealed record RefrigerationResult(
    double CarnotCoolingCop,
    double CarnotHeatingCop,
    double CoolingCop,
    double HeatingCop,
    double ColdSideHeatRemoval_W,
    double HotSideHeatDelivery_W);
