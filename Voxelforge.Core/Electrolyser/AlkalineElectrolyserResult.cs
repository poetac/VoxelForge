// AlkalineElectrolyserResult.cs — Sprint B.2-Alk solver output.

namespace Voxelforge.Electrolyser;

/// <summary>
/// Solve-time outputs for a single alkaline electrolyser stack
/// snapshot at the design operating point (Sprint B.2-Alk scaffold).
/// Shape mirrors <see cref="PemElectrolyserResult"/> and
/// <see cref="AemElectrolyserResult"/> — all three share the same
/// thermodynamic + kinetic + ohmic decomposition; the differentiators
/// are catalyst Tafel kinetics + electrolyte resistance.
/// </summary>
/// <param name="NernstVoltage_V">E_Nernst equilibrium voltage [V] — water-splitting requires V_cell ≥ E_Nernst.</param>
/// <param name="ActivationLoss_V">η_act [V] — anode OER on Ni / Ni-alloy catalyst. Higher Tafel slope than PEM/AEM.</param>
/// <param name="OhmicLoss_V">η_ohm [V] — Zirfon-Perl-or-equivalent diaphragm + KOH electrolyte resistance.</param>
/// <param name="CellVoltage_V">V_cell = E_Nernst + η_act + η_ohm [V] (same sign convention as PEM/AEM EL).</param>
/// <param name="StackVoltage_V">V_stack = N · V_cell [V] — the required applied voltage.</param>
/// <param name="StackCurrent_A">I_stack = i · A_active [A].</param>
/// <param name="StackElectricPower_W">P_in = V_stack · I_stack [W] — stack-only (excludes BOP).</param>
/// <param name="HhvEfficiency">η_HHV = E_HHV / V_cell [-] — fraction of stack input ending in H₂ HHV. Excludes BOP losses.</param>
/// <param name="HydrogenProductionRate_kgs">ṁ_H₂ = N · I_stack · M_H₂ / (2·F) [kg/s]. Faraday's law — identical to PEM/AEM.</param>
/// <param name="HydrogenProductionRate_Nm3_h">Convenience: ṁ_H₂ in Nm³/h at STP.</param>
internal sealed record AlkalineElectrolyserResult(
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
