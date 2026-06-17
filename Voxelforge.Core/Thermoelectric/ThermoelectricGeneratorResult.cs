// ThermoelectricGeneratorResult.cs — Sprint TEG.W1 solver output.

namespace Voxelforge.Thermoelectric;

/// <summary>
/// Solve-time outputs for a TEG snapshot at the design (T_hot,
/// T_cold, ZT) operating point (Sprint TEG.W1).
/// </summary>
/// <param name="CarnotEfficiency">η_Carnot = 1 − T_cold/T_hot [-] —
/// the upper bound.</param>
/// <param name="ConversionEfficiency">η_TEG [-] from the canonical
/// figure-of-merit formula η_Carnot · (√(1+ZT) − 1) / (√(1+ZT) +
/// T_cold/T_hot).</param>
/// <param name="ElectricPowerOutput_W">P_elec = η_TEG · Q_hot [W].</param>
/// <param name="HeatRejectedToColdSide_W">Q_cold = Q_hot − P_elec [W]
/// — what the radiator / heat sink must shed.</param>
/// <param name="HotSideTemperatureInValidEnvelope">Whether T_hot sits
/// inside the material's per-kind validity band.</param>
internal sealed record ThermoelectricGeneratorResult(
    double CarnotEfficiency,
    double ConversionEfficiency,
    double ElectricPowerOutput_W,
    double HeatRejectedToColdSide_W,
    bool   HotSideTemperatureInValidEnvelope);
