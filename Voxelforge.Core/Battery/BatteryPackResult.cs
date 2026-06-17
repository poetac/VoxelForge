// BatteryPackResult.cs — Sprint BP.W1 solver output.

namespace Voxelforge.Battery;

/// <summary>
/// Solve-time outputs for a battery pack snapshot at the design
/// operating point (Sprint BP.W1 scaffold).
/// </summary>
/// <param name="OpenCircuitCellVoltage_V">V_oc per cell at the snapshot SoC [V].</param>
/// <param name="LoadedCellVoltage_V">V_cell − i·R_int per cell at the snapshot load current [V].</param>
/// <param name="PackOpenCircuitVoltage_V">V_pack_oc = N_series · V_oc [V].</param>
/// <param name="PackLoadedVoltage_V">V_pack_loaded = N_series · V_cell_loaded [V].</param>
/// <param name="PackInternalResistance_Ohm">R_pack = (N_series · R_cell) / N_parallel [Ω].</param>
/// <param name="PackEnergyStored_Wh">E = N_series · N_parallel · C_cell · V_avg · SoC [Wh].</param>
/// <param name="PackElectricalPower_W">P = V_pack_loaded · I_pack [W]. Positive on discharge.</param>
/// <param name="PackHeatGeneration_W">Q_heat = I_pack² · R_pack [W] — Joule (resistive) dissipation only at this fidelity.</param>
internal sealed record BatteryPackResult(
    double OpenCircuitCellVoltage_V,
    double LoadedCellVoltage_V,
    double PackOpenCircuitVoltage_V,
    double PackLoadedVoltage_V,
    double PackInternalResistance_Ohm,
    double PackEnergyStored_Wh,
    double PackElectricalPower_W,
    double PackHeatGeneration_W);
