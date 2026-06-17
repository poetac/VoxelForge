// AlkalineElectrolyserSolver.cs — Sprint B.2-Alk closed-form alkaline
// electrolyser stack performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes the cell-voltage
// breakdown (Nernst + activation + ohmic), stack-level V/I/P_input,
// HHV efficiency, and H₂ production rate at the design operating
// point.
//
// Alkaline vs PEM/AEM physics:
//   Same loss decomposition: V_cell = E_Nernst + η_act + η_ohm. Same
//   Faraday's-law H₂ production rate. Same HHV thermo-neutral voltage
//   (1.481 V — property of the water-splitting reaction, not the
//   catalyst).
//
//   Alkaline differentiators:
//
//     1. Catalyst — Ni / Ni-alloy on both electrodes (no platinum-
//        group metals; no rare-earth catalysts). Cathode is steel- or
//        Ni-on-steel; anode is Ni or Ni-Mo / Ni-Co.
//
//     2. Tafel kinetics — Ni-OER cluster Tafel slope runs ~ 90 mV/dec
//        at the cell level (LeRoy 1983 fit, Vincent & Bessarabov 2018
//        review), vs ~ 60 mV/dec for IrO₂ (PEM) or NiFe-LDH (AEM). The
//        higher slope is the dominant electrochemical penalty —
//        alkaline cells must run at lower current density to keep
//        V_cell in band. Cell-level effective exchange current density
//        i_0 ≈ 1e-7 A/cm² matches PEM/AEM within order-of-magnitude
//        scatter (the Ni catalyst's surface coverage + interfacial
//        resistance dominate over intrinsic kinetics at commercial
//        scale).
//
//     3. Electrolyte resistance — diaphragm-separated cells use a
//        porous Zirfon-Perl (ZrO₂ in PPS matrix) separator soaked in
//        30 wt% KOH solution. R_AS ≈ 0.25 Ω·cm² at 80 °C, between PEM
//        Nafion-117 (0.15) and AEM Sustainion (0.30). The KOH
//        ion-conduction is comparable to Nafion proton-conduction at
//        elevated T but the porous separator + bubble blanket on
//        electrodes contributes additional ohmic loss.
//
//   Consequence: alkaline cells run at LOWER current density than
//   PEM (0.2-0.4 A/cm² vs 1-2 A/cm²) because higher Tafel slope makes
//   the activation loss dominant at high i. Commercial Nel A485 /
//   Thyssenkrupp / Asahi-Kasei / Hydrogenics HyLYZER stacks operate
//   near 0.25 A/cm², 80 °C, 1-30 bar (atmospheric + pressurised
//   variants both ship).
//
// Anchors (LeRoy 1980 + 1983, Vincent & Bessarabov 2018, Schalenbach
// et al. 2018):
//   Tafel slope b ≈ 90 mV/dec (Ni/Ni-Mo cathode + anode at cell
//                              level, cluster mid-band)
//   Exchange current density i_0 ≈ 1.0e-7 A/cm² (cell-level
//                                                effective)
//   Area-specific resistance R_AS ≈ 0.25 Ω·cm² (Zirfon Perl + 30 wt%
//                                                KOH at 80 °C)
//
// References:
//   LeRoy R.L., Bowen C.T., LeRoy D.J. (1980). "The thermodynamics of
//     aqueous water electrolysis." J. Electrochem. Soc. 127.
//   LeRoy R.L. (1983). "Industrial water electrolysis: present and
//     future." Int. J. Hydrogen Energy 8.
//   Vincent I., Bessarabov D. (2018). "Low cost hydrogen production
//     by anion exchange membrane electrolysis: A review."
//     Renewable & Sustainable Energy Reviews 81 (includes alkaline
//     reference comparison).
//   Schalenbach M., Tjarks G., Carmo M., Lueke W., Mueller M.,
//     Stolten D. (2016). "Acidic or alkaline? Towards a new
//     perspective on the efficiency of water electrolysis."
//     J. Electrochem. Soc. 163.

using System;

namespace Voxelforge.Electrolyser;

/// <summary>
/// Closed-form alkaline electrolyser stack performance snapshot solver
/// (Sprint B.2-Alk).
/// </summary>
internal static class AlkalineElectrolyserSolver
{
    /// <summary>Faraday constant [C/mol] — shared with PEM/AEM.</summary>
    internal const double Faraday_C_mol = 96485.0;

    /// <summary>Universal gas constant [J/(mol·K)] — shared with PEM/AEM.</summary>
    internal const double R_J_molK = 8.31446;

    /// <summary>Standard Nernst voltage at 25 °C, 1 bar [V] — shared with PEM/AEM
    /// (property of the water-splitting reaction, not the catalyst).</summary>
    internal const double StandardNernstVoltage_V = 1.229;

    /// <summary>Linear Nernst temperature slope [V/K] — shared with PEM/AEM.</summary>
    internal const double NernstTemperatureSlope_V_K = -0.85e-3;

    /// <summary>Reference temperature [K] (25 °C) — shared with PEM/AEM.</summary>
    internal const double ReferenceTemperature_K = 298.15;

    /// <summary>Reference pressure [bar] — shared with PEM/AEM.</summary>
    internal const double ReferencePressure_bar = 1.0;

    /// <summary>
    /// Anode (OER) Tafel slope b [V/decade] for Ni / Ni-alloy catalyst
    /// at cell level. Lab-scale Ni Tafel slopes run 40-80 mV/dec; at
    /// cell level (with interfacial resistance + bubble-coverage
    /// effects) the effective slope lands ~ 90 mV/dec — the dominant
    /// electrochemical penalty vs PEM IrO₂ (60 mV/dec) and AEM NiFe-LDH
    /// (60 mV/dec). Cluster mid-band from LeRoy 1983 + Vincent &amp;
    /// Bessarabov 2018.
    /// </summary>
    internal const double TafelSlope_V_dec = 0.090;

    /// <summary>
    /// Cell-level effective anode exchange current density i_0
    /// [A/cm²] on Ni-OER. Cluster mid-band 1e-8 to 1e-6 in lab
    /// measurements; cell-level effective i_0 lands ~ 1e-7, matching
    /// PEM IrO₂ and AEM NiFe-LDH within order-of-magnitude scatter.
    /// </summary>
    internal const double ExchangeCurrentDensity_A_cm2 = 1.0e-7;

    /// <summary>
    /// Area-specific resistance R_AS [Ω·cm²] for alkaline cell —
    /// Zirfon-Perl-or-equivalent diaphragm soaked in 30 wt% KOH at
    /// 80 °C. Between PEM Nafion-117 (0.15) and AEM Sustainion
    /// (0.30) — the porous separator + bubble-blanket ohmic contribution
    /// dominates over the bulk KOH conductivity, which is itself
    /// comparable to Nafion proton-conduction at elevated T.
    /// </summary>
    internal const double AreaSpecificResistance_OhmCm2 = 0.25;

    /// <summary>HHV thermo-neutral voltage [V] — shared with PEM/AEM
    /// (property of the water-splitting reaction).</summary>
    internal const double HhvThermoNeutralVoltage_V = 1.481;

    /// <summary>Molar mass of H₂ [kg/mol] — shared with PEM/AEM.</summary>
    internal const double MolarMassH2_kg_mol = 2.016e-3;

    /// <summary>Conversion factor 1 kg H₂ → Nm³ at STP — shared with PEM/AEM.</summary>
    internal const double NormalCubicMeterPerKgH2 = 11.126;

    /// <summary>
    /// Solve the alkaline electrolyser stack performance snapshot at
    /// the design operating point.
    /// </summary>
    /// <param name="design">Validated alkaline electrolyser stack design.</param>
    /// <returns>Solved performance snapshot.</returns>
    internal static AlkalineElectrolyserResult Solve(AlkalineElectrolyserDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        double T_K = design.OperatingTemperature_C + 273.15;
        double P_bar = design.OperatingPressure_bar;
        double i = design.OperatingCurrentDensity_A_cm2;

        // 1. Nernst voltage — identical form to PEM/AEM (linear-T +
        //    ln-P correction). Property of water-splitting, independent
        //    of catalyst / electrolyte choice.
        double E_temperature = StandardNernstVoltage_V
                             + NernstTemperatureSlope_V_K * (T_K - ReferenceTemperature_K);
        double E_pressure_correction = (R_J_molK * T_K / (2.0 * Faraday_C_mol))
                                     * Math.Log(P_bar / ReferencePressure_bar);
        double E_Nernst = E_temperature + E_pressure_correction;

        // 2. Activation polarisation (Ni-OER) — the alkaline
        //    differentiator. Higher Tafel slope than PEM/AEM produces
        //    a larger η_act at any given i.
        double tafelNaturalLogSlope = TafelSlope_V_dec / Math.Log(10.0);
        double eta_act = tafelNaturalLogSlope * Math.Log(i / ExchangeCurrentDensity_A_cm2);

        // 3. Ohmic polarisation — Zirfon-Perl + KOH electrolyte. R_AS
        //    lies between PEM (0.15) and AEM (0.30).
        double eta_ohm = i * AreaSpecificResistance_OhmCm2;

        // 4. Cell voltage = E_Nernst + losses (same SIGN convention as
        //    PEM/AEM EL: V_cell > E_Nernst because the cell CONSUMES
        //    power).
        double V_cell  = E_Nernst + eta_act + eta_ohm;
        double V_stack = design.CellCount * V_cell;
        double I_stack = i * design.ActiveAreaPerCell_cm2;          // A
        double P_input = V_stack * I_stack;                          // W (stack-only)

        // 5. HHV efficiency = E_HHV / V_cell. Stack-only (excludes
        //    balance-of-plant; commercial alkaline systems lose
        //    ~ 10-15 % additional to electrolyte circulation, gas /
        //    liquid separators, KOH cooling).
        double eta_HHV = HhvThermoNeutralVoltage_V / V_cell;

        // 6. Hydrogen production rate — Faraday's law (identical to
        //    PEM/AEM). 2 e⁻ per H₂ molecule regardless of charge-carrier
        //    identity.
        double m_H2_kgs = design.CellCount * I_stack * MolarMassH2_kg_mol
                       / (2.0 * Faraday_C_mol);
        double m_H2_Nm3_h = m_H2_kgs * NormalCubicMeterPerKgH2 * 3600.0;

        return new AlkalineElectrolyserResult(
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
