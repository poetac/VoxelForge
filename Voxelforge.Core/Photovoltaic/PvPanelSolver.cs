// PvPanelSolver.cs — Sprint PV.W1 closed-form photovoltaic panel
// performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes I_sc, V_oc,
// MPP voltage/current, MPP power, efficiency, and incident solar
// power for a panel at a specified plane-of-array irradiance + cell
// temperature.
//
// The Wave-1 model is a CLUSTER-FIT MPP envelope (not a full single-
// diode I-V curve solve):
//
//   I_sc(G, T) = I_sc_STC · (G / G_STC) · (1 + α_I · (T − T_STC))
//   V_oc(T)    = V_oc_STC + β_V · (T − T_STC)
//   V_mp_panel = 0.85 · V_oc · N_series
//   I_mp       = 0.93 · I_sc
//   P_mp       = V_mp_panel · I_mp · N_parallel
//   η          = P_mp / (G · A_panel)
//
// The "0.85 of V_oc, 0.93 of I_sc" cluster mid-band is the canonical
// silicon-cell convention (Fill Factor FF ≈ 0.79). Real cells require
// a single-diode I-V solve to find the true MPP — that lives in PV.W2.
//
// References:
//   Markvart T., Castañer L. (2003). "Practical Handbook of
//     Photovoltaics," chap 4 (single-diode model) + chap 5 (modules).
//   Honsberg C., Bowden S. (2019). "PV Education" online textbook,
//     "Solar Cells" module — cluster anchors for FF + MPP ratios.
//   IEC 61215 standard test conditions (G = 1000 W/m², T = 25 °C,
//     AM1.5G).

using System;

namespace Voxelforge.Photovoltaic;

/// <summary>
/// Closed-form photovoltaic panel performance snapshot solver
/// (Sprint PV.W1).
/// </summary>
internal static class PvPanelSolver
{
    /// <summary>Standard Test Conditions irradiance [W/m²] (IEC 61215).</summary>
    internal const double StandardIrradiance_W_m2 = 1000.0;

    /// <summary>Standard Test Conditions temperature [°C] (IEC 61215).</summary>
    internal const double StandardTemperature_C = 25.0;

    /// <summary>
    /// Cluster mid-band ratio V_mp / V_oc for silicon cells [-].
    /// Markvart & Castañer 2003 chap 4 anchor 0.80-0.88.
    /// </summary>
    internal const double MppVoltageOcvRatio = 0.85;

    /// <summary>
    /// Cluster mid-band ratio I_mp / I_sc for silicon cells [-].
    /// </summary>
    internal const double MppCurrentScRatio = 0.93;

    /// <summary>
    /// Solve the panel performance snapshot at the design (G, T)
    /// operating point.
    /// </summary>
    /// <param name="design">Validated PV panel design.</param>
    /// <returns>Solved performance snapshot.</returns>
    internal static PvPanelResult Solve(PvPanelDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var p = PhotovoltaicCellRegistry.For(design.CellType);

        double deltaT_K = design.CellTemperature_C - StandardTemperature_C;

        // 1. Irradiance + temperature corrections.
        double I_sc_cell = p.ShortCircuitCurrent_A
                         * (design.Irradiance_W_m2 / StandardIrradiance_W_m2)
                         * (1.0 + p.CurrentTemperatureCoefficient_perK * deltaT_K);
        double V_oc_cell = p.OpenCircuitVoltage_V
                         + p.VoltageTemperatureCoefficient_V_perK * deltaT_K;

        // V_oc can drop to zero at extreme temperatures; clamp to zero
        // (the panel is then non-operational).
        if (V_oc_cell < 0.0) V_oc_cell = 0.0;
        if (I_sc_cell < 0.0) I_sc_cell = 0.0;

        // 2. Cluster-fit MPP at the panel terminals. V_mp at panel is
        //    V_mp_cell · N_series; I_mp at panel is I_mp_cell · N_parallel.
        double V_mp_cell  = MppVoltageOcvRatio  * V_oc_cell;
        double I_mp_cell  = MppCurrentScRatio   * I_sc_cell;
        double V_mp_panel = V_mp_cell * design.CellsInSeries;
        double I_mp_panel = I_mp_cell * design.StringsInParallel;
        double P_mp_panel_front = V_mp_panel * I_mp_panel;
        // Sprint PV.W2 — bifacial gain. Front-side power is multiplied
        // by (1 + φ · β) where φ = rear-side irradiance gain and β =
        // cell bifaciality. φ = 0 (PV.W1 default) → bit-identical
        // monofacial behaviour.
        double bifacialMultiplier = 1.0
            + design.RearSideIrradianceGain * design.BifacialityFactor;
        double P_mp_panel = P_mp_panel_front * bifacialMultiplier;

        // Panel-terminal I_sc + V_oc.
        double I_sc_panel = I_sc_cell * design.StringsInParallel;
        double V_oc_panel = V_oc_cell * design.CellsInSeries;

        // 3. Efficiency + incident solar power.
        double incidentSolarPower_W = design.Irradiance_W_m2 * design.PanelArea_m2;
        double efficiency = incidentSolarPower_W > 0
            ? P_mp_panel / incidentSolarPower_W
            : 0.0;

        return new PvPanelResult(
            ShortCircuitCurrent_A:  I_sc_panel,
            OpenCircuitVoltage_V:   V_oc_panel,
            MaxPowerPointVoltage_V: V_mp_panel,
            MaxPowerPointCurrent_A: I_mp_panel,
            MaxPower_W:             P_mp_panel,
            ConversionEfficiency:   efficiency,
            IncidentSolarPower_W:   incidentSolarPower_W);
    }
}
