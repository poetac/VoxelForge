// PemFuelCellResult.cs — Sprint PG.W1 solver output.

namespace Voxelforge.PowerGen;

/// <summary>
/// Solve-time outputs for a single PEM fuel cell stack snapshot at the
/// design operating point (Sprint PG.W1 scaffold). Companion to
/// <see cref="PemFuelCellDesign"/>.
/// </summary>
/// <param name="NernstVoltage_V">Nernst equilibrium voltage E_Nernst [V] (no losses).</param>
/// <param name="ActivationLoss_V">η_act [V] — cathode-dominated Tafel kinetics.</param>
/// <param name="OhmicLoss_V">η_ohm [V] — membrane + interfacial ionic + electronic resistance.</param>
/// <param name="ConcentrationLoss_V">η_conc [V] — mass-transport limit (approaching i_L).</param>
/// <param name="CellVoltage_V">V_cell = E − η_act − η_ohm − η_conc [V].</param>
/// <param name="StackVoltage_V">V_stack = N_cells · V_cell [V].</param>
/// <param name="StackCurrent_A">I_stack = i · A_active [A] (same current through every series-stacked cell).</param>
/// <param name="StackElectricPower_W">P_elec = V_stack · I_stack [W].</param>
/// <param name="LhvEfficiency">η_LHV = V_cell / 1.254 [-] — H₂ LHV reference.</param>
/// <param name="HeatRejectionPower_W">Q_heat [W] balancing electrochemical + ohmic dissipation.</param>
internal sealed record PemFuelCellResult(
    double NernstVoltage_V,
    double ActivationLoss_V,
    double OhmicLoss_V,
    double ConcentrationLoss_V,
    double CellVoltage_V,
    double StackVoltage_V,
    double StackCurrent_A,
    double StackElectricPower_W,
    double LhvEfficiency,
    double HeatRejectionPower_W);
