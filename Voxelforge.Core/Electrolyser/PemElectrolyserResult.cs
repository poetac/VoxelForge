// PemElectrolyserResult.cs — Sprint EL.W1 solver output.

namespace Voxelforge.Electrolyser;

/// <summary>
/// Solve-time outputs for a single PEM electrolyser stack snapshot at
/// the design operating point (Sprint EL.W1 scaffold).
/// </summary>
/// <param name="NernstVoltage_V">E_Nernst equilibrium voltage [V] — water-splitting requires V_cell ≥ E_Nernst.</param>
/// <param name="ActivationLoss_V">η_act [V] — anode OER dominates kinetics.</param>
/// <param name="OhmicLoss_V">η_ohm [V] — membrane + interfacial.</param>
/// <param name="CellVoltage_V">V_cell = E_Nernst + η_act + η_ohm [V] (note SIGN: opposite of fuel cell).</param>
/// <param name="StackVoltage_V">V_stack = N · V_cell [V] — the required applied voltage.</param>
/// <param name="StackCurrent_A">I_stack = i · A_active [A].</param>
/// <param name="StackElectricPower_W">P_in = V_stack · I_stack [W] — the input from grid / PV.</param>
/// <param name="HhvEfficiency">η_HHV = E_HHV / V_cell [-] — fraction of input that ends in H₂ HHV.</param>
/// <param name="HydrogenProductionRate_kgs">ṁ_H₂ = N · I_stack · M_H₂ / (2·F) [kg/s].</param>
/// <param name="HydrogenProductionRate_Nm3_h">Convenience: ṁ_H₂ in Nm³/h at STP.</param>
internal sealed record PemElectrolyserResult(
    double NernstVoltage_V,
    double ActivationLoss_V,
    double OhmicLoss_V,
    double CellVoltage_V,
    double StackVoltage_V,
    double StackCurrent_A,
    double StackElectricPower_W,
    double HhvEfficiency,
    double HydrogenProductionRate_kgs,
    double HydrogenProductionRate_Nm3_h);
