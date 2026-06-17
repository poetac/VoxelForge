// AemElectrolyserSolver.cs — Sprint EL.W2 closed-form AEM electrolyser
// stack performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes the cell-voltage
// breakdown (Nernst + activation + ohmic), stack-level V/I/P_input,
// HHV efficiency, and H₂ production rate at the design operating
// point.
//
// AEM vs PEM physics:
//   Same architecture: liquid-water in, H₂ + O₂ out, membrane
//   electrolyte. Same loss decomposition: V_cell = E_Nernst + η_act
//   + η_ohm. Same Faraday's-law H₂ production rate. Same HHV-
//   thermo-neutral voltage (1.481 V — property of the water-splitting
//   reaction, not the membrane).
//
//   AEM differentiator: membrane resistance ~ 2× PEM. The Sustainion
//   / Aemion / Piperion anion-exchange polymers conduct OH⁻ instead
//   of H⁺; anion mobility in commercial AEM membranes is ~ 50 % of
//   proton mobility in Nafion, so R_AS lands around 0.30 Ω·cm² vs
//   0.15 for Nafion-117. Catalyst kinetics on NiFe-LDH are
//   comparable to IrO₂ at cell level — the lab-scale Tafel-slope
//   advantage of NiFe (40-50 mV/dec) is largely lost to interfacial
//   resistance in commercial cells, so we anchor Tafel = 60 mV/dec
//   and i_0 = 1e-7 A/cm² to match PEM. The differentiator is
//   purely R_AS.
//
//   Consequence: AEM cells run at LOWER current density than PEM
//   (0.5-1.0 A/cm² vs 1-2 A/cm²) because higher R_AS makes the
//   ohmic loss dominant at high i. Commercial Enapter EL-2.1 / Hydrolite
//   / OxEon stacks operate near 0.6 A/cm², 60 °C, 30-35 bar.
//
// Anchors (Vincent & Bessarabov 2018, Henkensmeier et al. 2021,
// Pivovar 2019 NREL workshop):
//   Tafel slope b ≈ 60 mV/dec (NiFe-LDH anode OER at cell level)
//   Exchange current density i_0 ≈ 1.0e-7 A/cm² (NiFe-LDH ORR
//                                                exchange; same order
//                                                as IrO₂)
//   Area-specific resistance R_AS ≈ 0.30 Ω·cm² (Sustainion / Aemion
//                                                / Piperion AEM
//                                                membrane + interface)
//
// References:
//   Vincent I., Bessarabov D. (2018). "Low cost hydrogen production
//     by anion exchange membrane electrolysis: A review."
//     Renewable & Sustainable Energy Reviews 81.
//   Henkensmeier D., Najibah M., Harms C., Žitka J., Hnát J., Bouzek K.
//     (2021). "Overview: state-of-the art commercial membranes for
//     anion exchange membrane water electrolysis."
//     J. Electrochem. Energy Conv. Stor. 18.
//   Pivovar B. (2019). "AEM Electrolyzer Workshop — Summary."
//     NREL/PR-5900-72850.

using System;

namespace Voxelforge.Electrolyser;

/// <summary>
/// Closed-form AEM electrolyser stack performance snapshot solver
/// (Sprint EL.W2).
/// </summary>
internal static class AemElectrolyserSolver
{
    /// <summary>Faraday constant [C/mol] — shared with PEM.</summary>
    internal const double Faraday_C_mol = 96485.0;

    /// <summary>Universal gas constant [J/(mol·K)] — shared with PEM.</summary>
    internal const double R_J_molK = 8.31446;

    /// <summary>Standard Nernst voltage at 25 °C, 1 bar [V] — shared with PEM
    /// (property of the water-splitting reaction, not the membrane).</summary>
    internal const double StandardNernstVoltage_V = 1.229;

    /// <summary>Linear Nernst temperature slope [V/K] — shared with PEM.</summary>
    internal const double NernstTemperatureSlope_V_K = -0.85e-3;

    /// <summary>Reference temperature [K] (25 °C) — shared with PEM.</summary>
    internal const double ReferenceTemperature_K = 298.15;

    /// <summary>Reference pressure [bar] — shared with PEM.</summary>
    internal const double ReferencePressure_bar = 1.0;

    /// <summary>
    /// Anode (OER) Tafel slope b [V/decade] for NiFe-LDH catalyst at
    /// cell level. Lab-scale NiFe Tafel slopes run 40-50 mV/dec; at
    /// cell level (with interfacial resistance contributions) the
    /// effective slope lands ~ 60 mV/dec — same as PEM IrO₂.
    /// </summary>
    internal const double TafelSlope_V_dec = 0.060;

    /// <summary>
    /// Anode (OER) exchange current density i_0 [A/cm²] on NiFe-LDH.
    /// Cluster mid-band 1e-7 to 1e-5 in lab measurements; cell-level
    /// effective i_0 lands ~ 1e-7, matching PEM IrO₂ within
    /// order-of-magnitude scatter.
    /// </summary>
    internal const double ExchangeCurrentDensity_A_cm2 = 1.0e-7;

    /// <summary>
    /// Area-specific resistance R_AS [Ω·cm²] for AEM. The AEM
    /// differentiator vs PEM (Nafion-117 ≈ 0.15) — anion conduction
    /// in Sustainion / Aemion / Piperion polymers is ~ 50 % as fast
    /// as proton conduction in Nafion, landing R_AS near 0.30 Ω·cm²
    /// for commercial cells.
    /// </summary>
    internal const double AreaSpecificResistance_OhmCm2 = 0.30;

    /// <summary>HHV thermo-neutral voltage [V] — shared with PEM
    /// (property of the water-splitting reaction).</summary>
    internal const double HhvThermoNeutralVoltage_V = 1.481;

    /// <summary>Molar mass of H₂ [kg/mol] — shared with PEM.</summary>
    internal const double MolarMassH2_kg_mol = 2.016e-3;

    /// <summary>Conversion factor 1 kg H₂ → Nm³ at STP — shared with PEM.</summary>
    internal const double NormalCubicMeterPerKgH2 = 11.126;

    /// <summary>
    /// Solve the AEM electrolyser stack performance snapshot at the
    /// design operating point.
    /// </summary>
    /// <param name="design">Validated AEM electrolyser stack design.</param>
    /// <returns>Solved performance snapshot.</returns>
    internal static AemElectrolyserResult Solve(AemElectrolyserDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        double T_K = design.OperatingTemperature_C + 273.15;
        double P_bar = design.OperatingPressure_bar;
        double i = design.OperatingCurrentDensity_A_cm2;

        // 1. Nernst voltage — identical form to PEM (linear-T + ln-P
        //    correction). Higher P increases E_Nernst (Le Chatelier:
        //    harder to split water against a back-pressure of H₂ + ½O₂).
        double E_temperature = StandardNernstVoltage_V
                             + NernstTemperatureSlope_V_K * (T_K - ReferenceTemperature_K);
        double E_pressure_correction = (R_J_molK * T_K / (2.0 * Faraday_C_mol))
                                     * Math.Log(P_bar / ReferencePressure_bar);
        double E_Nernst = E_temperature + E_pressure_correction;

        // 2. Activation polarisation (NiFe-LDH OER).
        double tafelNaturalLogSlope = TafelSlope_V_dec / Math.Log(10.0);
        double eta_act = tafelNaturalLogSlope * Math.Log(i / ExchangeCurrentDensity_A_cm2);

        // 3. Ohmic polarisation — the AEM differentiator (higher R_AS
        //    than PEM Nafion-117).
        double eta_ohm = i * AreaSpecificResistance_OhmCm2;

        // 4. Cell voltage = E_Nernst + losses (same SIGN convention as
        //    PEM EL: V_cell > E_Nernst because the cell CONSUMES power).
        double V_cell  = E_Nernst + eta_act + eta_ohm;
        double V_stack = design.CellCount * V_cell;
        double I_stack = i * design.ActiveAreaPerCell_cm2;          // A
        double P_input = V_stack * I_stack;                          // W (stack-only)

        // 5. HHV efficiency = E_HHV / V_cell. Stack-only (excludes
        //    balance-of-plant; commercial AEM systems lose ~ 10-15 %
        //    additional to pumps, electronics, water purification).
        double eta_HHV = HhvThermoNeutralVoltage_V / V_cell;

        // 6. Hydrogen production rate — Faraday's law (identical to PEM).
        //    2 e⁻ per H₂ molecule regardless of charge-carrier identity.
        double m_H2_kgs = design.CellCount * I_stack * MolarMassH2_kg_mol
                       / (2.0 * Faraday_C_mol);
        double m_H2_Nm3_h = m_H2_kgs * NormalCubicMeterPerKgH2 * 3600.0;

        return new AemElectrolyserResult(
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
