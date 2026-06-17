// BatteryPackSolver.cs — Sprint BP.W1 closed-form battery pack
// performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes the open-circuit
// + loaded cell + pack voltage, pack internal resistance, stored
// energy, electrical power, and resistive heat generation at a
// specified SoC + load current.
//
//   OCV(SoC) = V_min + (V_max - V_min) · SoC       [linear cluster fit;
//                                                   a future BP.W2 sprint
//                                                   will refit per-chemistry
//                                                   cubic / sigmoid forms.]
//   I_cell   = I_pack / N_parallel
//   V_cell   = OCV(SoC) − I_cell · R_int
//   V_pack   = N_series · V_cell
//   R_pack   = (N_series · R_cell) / N_parallel
//   E_stored = N_series · N_parallel · C_cell · V_oc_avg · SoC
//   Q_heat   = I_pack² · R_pack   (Joule heating; ignores entropic /
//                                  reaction-enthalpy contributions which
//                                  are 5-15 % of total at typical C-rates)
//
// References:
//   Plett G. (2015). "Battery Management Systems," vols 1+2 (Wiley).
//   Doyle M., Newman J. (1996). "Comparison of modeling predictions with
//     experimental data from plastic lithium-ion cells." J. Electrochem. Soc.
//   Wang J., Liu P. et al. (2011). "Cycle-life model for graphite-LiFePO₄
//     cells." J. Power Sources, 196.

using System;

namespace Voxelforge.Battery;

/// <summary>
/// Closed-form battery pack performance snapshot solver (Sprint BP.W1).
/// </summary>
internal static class BatteryPackSolver
{
    /// <summary>
    /// Solve the battery pack performance snapshot at the design SoC +
    /// load current.
    /// </summary>
    /// <param name="design">Validated battery pack design.</param>
    /// <returns>Solved performance snapshot.</returns>
    internal static BatteryPackResult Solve(BatteryPackDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var chem = BatteryChemistryRegistry.For(design.Chemistry);

        // 1. Open-circuit cell voltage from a linear OCV(SoC) cluster fit.
        double V_oc_cell = chem.OcvMin_V
                         + (chem.OcvMax_V - chem.OcvMin_V) * design.StateOfCharge;

        // 2. Per-cell current = pack current / N_parallel. Per-cell V_drop
        //    is I_cell · R_int; pack V_drop is the series sum.
        double I_cell_A = design.LoadCurrent_A / design.ParallelStrings;
        double V_cell_loaded = V_oc_cell - I_cell_A * chem.InternalResistance_Ohm;

        // 3. Pack roll-up.
        double V_pack_oc      = design.CellsInSeries * V_oc_cell;
        double V_pack_loaded  = design.CellsInSeries * V_cell_loaded;
        double R_pack         = (design.CellsInSeries * chem.InternalResistance_Ohm)
                              / design.ParallelStrings;

        // 4. Stored energy at the midpoint OCV. Sprint BP.W2 applies a
        //    temperature-derating factor on the nominal capacity. The
        //    factor is 1.0 at 25 °C (BP.W1 default) → bit-identical
        //    Wave-1 behaviour for the canonical test conditions.
        double tempDerating = ComputeTemperatureCapacityDerating(design.CellTemperature_C);
        double deratedCapacity_Ah = chem.NominalCapacity_Ah * tempDerating;
        double deltaV = chem.OcvMax_V - chem.OcvMin_V;
        double integratedVdSoC = chem.OcvMin_V * design.StateOfCharge
                               + 0.5 * deltaV * design.StateOfCharge * design.StateOfCharge;
        double E_stored_Wh = design.CellsInSeries
                           * design.ParallelStrings
                           * deratedCapacity_Ah
                           * integratedVdSoC;

        // 5. Power + resistive heat. Power is positive on discharge
        //    (I_pack > 0); negative on charge.
        double P_pack_W = V_pack_loaded * design.LoadCurrent_A;
        double Q_heat_W = design.LoadCurrent_A * design.LoadCurrent_A * R_pack;

        return new BatteryPackResult(
            OpenCircuitCellVoltage_V:     V_oc_cell,
            LoadedCellVoltage_V:          V_cell_loaded,
            PackOpenCircuitVoltage_V:     V_pack_oc,
            PackLoadedVoltage_V:          V_pack_loaded,
            PackInternalResistance_Ohm:   R_pack,
            PackEnergyStored_Wh:          E_stored_Wh,
            PackElectricalPower_W:        P_pack_W,
            PackHeatGeneration_W:         Q_heat_W);
    }

    /// <summary>
    /// Sprint BP.W2. Compute the Li-ion capacity-derating factor at a
    /// given cell temperature. The fit is piecewise-linear:
    ///   T &lt; 0 °C: factor = 1 − 0.005 · (0 − T) (cold-derating)
    ///   0 ≤ T ≤ 45 °C: factor = 1.0 (nominal)
    ///   T &gt; 45 °C: factor = 1 − 0.003 · (T − 45) (hot-derating)
    /// Clamped at minimum 0.1 to prevent non-physical zero capacity.
    /// </summary>
    internal static double ComputeTemperatureCapacityDerating(double cellTemperature_C)
    {
        double factor = cellTemperature_C switch
        {
            < 0.0  => 1.0 - 0.005 * (0.0 - cellTemperature_C),
            > 45.0 => 1.0 - 0.003 * (cellTemperature_C - 45.0),
            _      => 1.0,
        };
        return factor < 0.1 ? 0.1 : factor;
    }
}
