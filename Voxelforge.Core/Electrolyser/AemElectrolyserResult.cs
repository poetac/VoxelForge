// AemElectrolyserResult.cs — Sprint EL.W2 solver output.

namespace Voxelforge.Electrolyser;

/// <summary>
/// Solve-time outputs for a single AEM electrolyser stack snapshot at
/// the design operating point (Sprint EL.W2 scaffold). Shape mirrors
/// <see cref="PemElectrolyserResult"/> — AEM and PEM share the same
/// thermodynamic + kinetic + ohmic decomposition; only the membrane
/// resistance constant differs.
/// </summary>
/// <param name="NernstVoltage_V">E_Nernst equilibrium voltage [V] — water-splitting requires V_cell ≥ E_Nernst.</param>
/// <param name="ActivationLoss_V">η_act [V] — anode OER on NiFe-LDH catalyst.</param>
/// <param name="OhmicLoss_V">η_ohm [V] — anion-exchange membrane + interfacial. Higher than PEM at equal i.</param>
/// <param name="CellVoltage_V">V_cell = E_Nernst + η_act + η_ohm [V] (same sign convention as PEM EL).</param>
/// <param name="StackVoltage_V">V_stack = N · V_cell [V] — the required applied voltage.</param>
/// <param name="StackCurrent_A">I_stack = i · A_active [A].</param>
/// <param name="StackElectricPower_W">P_in = V_stack · I_stack [W] — stack-only (excludes BOP).</param>
/// <param name="HhvEfficiency">η_HHV = E_HHV / V_cell [-] — fraction of stack input ending in H₂ HHV. Excludes BOP losses.</param>
/// <param name="HydrogenProductionRate_kgs">ṁ_H₂ = N · I_stack · M_H₂ / (2·F) [kg/s]. Faraday's law — identical to PEM.</param>
/// <param name="HydrogenProductionRate_Nm3_h">Convenience: ṁ_H₂ in Nm³/h at STP.</param>
internal sealed record AemElectrolyserResult(
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
