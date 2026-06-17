// PemElectrolyserSolver.cs — Sprint EL.W1 closed-form PEM electrolyser
// stack performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes the cell-voltage
// breakdown (Nernst + activation + ohmic — note the SIGNS flip vs the
// fuel-cell), stack-level V/I/P_input, HHV efficiency, and H₂
// production rate at the design operating point.
//
// PEM electrolyser is the THERMODYNAMIC REVERSE of the PEM fuel cell:
//   PG (fuel cell): V_cell = E_Nernst − η_act − η_ohm − η_conc
//                   (V_cell < E_Nernst; cell DELIVERS power)
//   EL (electrolyser): V_cell = E_Nernst + η_act + η_ohm
//                   (V_cell > E_Nernst; cell CONSUMES power)
//
// We mirror the loss-term structure of PG.W1 but flip the sign and
// drop the concentration-polarisation term (PEM electrolyser typically
// operates well below the gas-evolution mass-transport limit).
//
// Anchors (Carmo et al. 2013 + Bareiß et al. 2019):
//   Tafel slope b ≈ 60 mV/dec (anode OER on IrO₂ — sluggish; gives
//                              ~ 70 % of total overpotential)
//   Exchange current density i_0 ≈ 1.0e-7 A/cm² (IrO₂ ORR exchange)
//   Area-specific resistance R_AS ≈ 0.15 Ω·cm² (Nafion-117 + GDL)
//
//   Hydrogen production:
//     ṁ_H2 = N_cells · I_stack · M_H2 / (2 · F)   [kg/s]
//     where M_H2 = 2.016 g/mol = 2.016e-3 kg/mol
//   Nm³/h conversion: 1 kg H₂ = 11.126 Nm³ at STP (0 °C, 1 atm).
//
// References:
//   Carmo M., Fritz D., Mergel J., Stolten D. (2013). "A comprehensive
//     review on PEM water electrolysis." Int. J. Hydrogen Energy 38.
//   Bareiß K., de la Rua C., Möckl M., Hamacher T. (2019). "Life cycle
//     assessment of hydrogen from PEM water electrolysis." Appl. Energy
//     237.

using System;

namespace Voxelforge.Electrolyser;

/// <summary>
/// Closed-form PEM electrolyser stack performance snapshot solver
/// (Sprint EL.W1).
/// </summary>
internal static class PemElectrolyserSolver
{
    /// <summary>Faraday constant [C/mol].</summary>
    internal const double Faraday_C_mol = 96485.0;

    /// <summary>Universal gas constant [J/(mol·K)].</summary>
    internal const double R_J_molK = 8.31446;

    /// <summary>Standard Nernst voltage at 25 °C, 1 bar [V].</summary>
    internal const double StandardNernstVoltage_V = 1.229;

    /// <summary>Linear Nernst temperature slope [V/K].</summary>
    internal const double NernstTemperatureSlope_V_K = -0.85e-3;

    /// <summary>Reference temperature [K] (25 °C).</summary>
    internal const double ReferenceTemperature_K = 298.15;

    /// <summary>Reference pressure [bar].</summary>
    internal const double ReferencePressure_bar = 1.0;

    /// <summary>
    /// Anode (OER) Tafel slope b [V/decade] for IrO₂ catalyst. Cluster
    /// mid-band 50-70 mV/dec.
    /// </summary>
    internal const double TafelSlope_V_dec = 0.060;

    /// <summary>
    /// Anode (OER) exchange current density i_0 [A/cm²] on IrO₂.
    /// Cluster mid-band 1e-9 to 1e-6; we anchor at 1e-7 to land
    /// V_cell ≈ 1.85 V at i = 1.5 A/cm², T = 70 °C, P = 10 bar.
    /// </summary>
    internal const double ExchangeCurrentDensity_A_cm2 = 1.0e-7;

    /// <summary>Area-specific resistance R_AS [Ω·cm²]. Same Nafion-117
    /// anchor as the PEM fuel cell pillar.</summary>
    internal const double AreaSpecificResistance_OhmCm2 = 0.15;

    /// <summary>HHV thermo-neutral voltage [V] — H₂ HHV reference for
    /// efficiency.</summary>
    internal const double HhvThermoNeutralVoltage_V = 1.481;

    /// <summary>Molar mass of H₂ [kg/mol].</summary>
    internal const double MolarMassH2_kg_mol = 2.016e-3;

    /// <summary>Conversion factor 1 kg H₂ → Nm³ at STP (0 °C, 1 atm).
    /// At STP, 1 kmol of any gas = 22.4 Nm³; for H₂ (2.016 g/mol) →
    /// 1 kg = 22.4 / 2.016 = 11.126 Nm³.</summary>
    internal const double NormalCubicMeterPerKgH2 = 11.126;

    private static readonly double Ln10 = Math.Log(10.0);

    /// <summary>
    /// Solve the PEM electrolyser stack performance snapshot at the
    /// design operating point.
    /// </summary>
    /// <param name="design">Validated PEM electrolyser stack design.</param>
    /// <returns>Solved performance snapshot.</returns>
    internal static PemElectrolyserResult Solve(PemElectrolyserDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        double T_K = design.OperatingTemperature_C + 273.15;
        double P_bar = design.OperatingPressure_bar;
        double i = design.OperatingCurrentDensity_A_cm2;

        // 1. Nernst voltage — same form as PG (linear-T + ln-P correction).
        //    Note: at higher pressure, Nernst INCREASES for an electrolyser
        //    (Le Chatelier: harder to split water against a back-pressure
        //    of H₂ + ½O₂).
        double E_temperature = StandardNernstVoltage_V
                             + NernstTemperatureSlope_V_K * (T_K - ReferenceTemperature_K);
        double E_pressure_correction = (R_J_molK * T_K / (2.0 * Faraday_C_mol))
                                     * Math.Log(P_bar / ReferencePressure_bar);
        double E_Nernst = E_temperature + E_pressure_correction;

        // 2. Activation polarisation (anode OER on IrO₂).
        double tafelNaturalLogSlope = TafelSlope_V_dec / Ln10;
        double eta_act = tafelNaturalLogSlope * Math.Log(i / ExchangeCurrentDensity_A_cm2);

        // 3. Ohmic polarisation.
        double eta_ohm = i * AreaSpecificResistance_OhmCm2;

        // 4. Cell voltage = E_Nernst + losses (NOTE THE PLUS SIGNS vs PG).
        double V_cell  = E_Nernst + eta_act + eta_ohm;
        double V_stack = design.CellCount * V_cell;
        double I_stack = i * design.ActiveAreaPerCell_cm2;          // A
        double P_input = V_stack * I_stack;                          // W

        // 5. HHV efficiency = E_HHV / V_cell. Higher cell voltage →
        //    lower efficiency (more energy lost as heat).
        double eta_HHV = HhvThermoNeutralVoltage_V / V_cell;

        // 6. Hydrogen production rate.
        //    ṁ_H2 = N_cells · I_stack · M_H2 / (2·F)   [kg/s]
        //    (Faraday's law of electrolysis; 2 e⁻ per H₂ molecule).
        double m_H2_kgs = design.CellCount * I_stack * MolarMassH2_kg_mol
                       / (2.0 * Faraday_C_mol);
        double m_H2_Nm3_h = m_H2_kgs * NormalCubicMeterPerKgH2 * 3600.0;

        return new PemElectrolyserResult(
            NernstVoltage_V:              E_Nernst,
            ActivationLoss_V:             eta_act,
            OhmicLoss_V:                  eta_ohm,
            CellVoltage_V:                V_cell,
            StackVoltage_V:               V_stack,
            StackCurrent_A:               I_stack,
            StackElectricPower_W:         P_input,
            HhvEfficiency:                eta_HHV,
            HydrogenProductionRate_kgs:   m_H2_kgs,
            HydrogenProductionRate_Nm3_h: m_H2_Nm3_h);
    }
}
