// SoecElectrolyserResult.cs — Sprint B.2-SOEC solver output.

namespace Voxelforge.Electrolyser;

/// <summary>
/// Solve-time outputs for a single SOEC (solid-oxide electrolyser cell)
/// stack snapshot at the design operating point (Sprint B.2-SOEC scaffold).
/// Shape mirrors <see cref="PemElectrolyserResult"/>,
/// <see cref="AemElectrolyserResult"/>, and
/// <see cref="AlkalineElectrolyserResult"/> — all four share the same
/// thermodynamic + kinetic + ohmic decomposition. SOEC's differentiators
/// are (1) the high-T steam-electrolysis Nernst formulation, which makes
/// E_Nernst land below the low-T cluster (~ 0.93 V at 800 °C vs ~ 1.18 V
/// at 80 °C); (2) very high exchange current density (kinetics are facile
/// at 800 °C), so η_act is small; (3) the cell typically runs near or
/// below the HHV thermo-neutral voltage (1.481 V), making
/// <see cref="HhvEfficiency"/> approach or exceed 1.0 (the cell absorbs
/// heat from the surroundings — the SOEC value proposition).
/// </summary>
/// <param name="NernstVoltage_V">E_Nernst equilibrium voltage [V] at the operating (T, P) for the steam-electrolysis reaction H₂O(g) → H₂ + ½O₂.</param>
/// <param name="ActivationLoss_V">η_act [V] — anode OER on LSM/LSCF + cathode HER on Ni-YSZ. Small at SOEC operating T because of high i₀.</param>
/// <param name="OhmicLoss_V">η_ohm [V] — YSZ electrolyte + electrode interfaces. Dominates over η_act at typical i.</param>
/// <param name="CellVoltage_V">V_cell = E_Nernst + η_act + η_ohm [V] (same sign convention as the other electrolyser kinds).</param>
/// <param name="StackVoltage_V">V_stack = N · V_cell [V] — the required applied voltage.</param>
/// <param name="StackCurrent_A">I_stack = i · A_active [A].</param>
/// <param name="StackElectricPower_W">P_in = V_stack · I_stack [W] — stack-only electric input (excludes BOP + the heat input that the SOEC absorbs from surroundings at endothermic V_cell &lt; V_TN).</param>
/// <param name="HhvEfficiency">η_HHV = E_HHV / V_cell [-] — fraction of stack ELECTRIC input ending in H₂ HHV. May exceed 1.0 when V_cell &lt; V_TN (the SOEC absorbs heat to make up the difference); that is correct and physical.</param>
/// <param name="HydrogenProductionRate_kgs">ṁ_H₂ = N · I_stack · M_H₂ / (2·F) [kg/s]. Faraday's law — identical to PEM/AEM/Alkaline.</param>
/// <param name="HydrogenProductionRate_Nm3_h">Convenience: ṁ_H₂ in Nm³/h at STP.</param>
internal sealed record SoecElectrolyserResult(
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
