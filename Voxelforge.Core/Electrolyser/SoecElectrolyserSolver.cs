// SoecElectrolyserSolver.cs — Sprint B.2-SOEC closed-form solid-oxide
// electrolyser stack performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes the cell-voltage
// breakdown (Nernst + activation + ohmic), stack-level V/I/P_input,
// HHV efficiency (referenced to the standard 1.481 V HHV thermo-neutral
// constant), and H₂ production rate at the design operating point.
//
// SOEC vs PEM/AEM/Alkaline physics:
//
//   Same loss decomposition: V_cell = E_Nernst + η_act + η_ohm. Same
//   Faraday's-law H₂ production rate. Same HHV thermo-neutral voltage
//   REFERENCE (1.481 V — property of the H₂ + ½O₂ → H₂O(l) reaction at
//   25 °C, used as the η_HHV denominator regardless of operating T).
//
//   SOEC differentiators:
//
//     1. Reactant — steam (H₂O vapour) instead of liquid water. The
//        upstream phase change (water → steam) is supplied by waste heat
//        or recovery from the H₂/O₂ product streams in commercial SOEC
//        systems; not modelled here at stack level.
//
//     2. Nernst formulation — at SOEC's 700-850 °C operating window the
//        Gibbs free energy of the steam-electrolysis reaction is
//        considerably lower than the liquid-water reaction at room
//        temperature (ΔG drops as T rises while ΔH stays nearly constant).
//        E_Nernst(800 °C, 1 bar) ≈ 0.93 V (Mogensen 2008; Stempien et al.
//        2013) vs ~ 1.18 V at 80 °C for the liquid-water reaction. The
//        linear -0.85 mV/K slope used by PEM / AEM / Alkaline implicitly
//        tracks the liquid-water heat-capacity reference and diverges
//        from the steam-electrolysis cluster above ~ 150 °C; SOEC anchors
//        its OWN T_ref = 800 °C and dE/dT slope.
//
//     3. Kinetics — Ni-YSZ cathode HER + LSM/LSCF anode OER at 800 °C
//        are extremely facile. Cell-level effective i₀ ≈ 0.5 A/cm²
//        (Klotz 2017, Sun 2010), three to four orders of magnitude
//        higher than liquid-T values (1e-7 to 1e-3). Tafel slope at
//        cell level lands around 0.10 V/dec (Klotz 2017). The high i₀
//        means η_act is small even at design current density;
//        activation losses are NOT the dominant penalty at SOEC.
//
//     4. Ohmic — YSZ ionic conduction is the limiting transport
//        process; thinner anode-supported electrolytes (10-20 µm YSZ)
//        plus optimised LSCF / Ni-YSZ electrode interfaces give cell-
//        level R_AS ≈ 0.4 Ω·cm² at 800 °C (Stempien 2013, Lessing 2011,
//        cluster mid-band).
//
//   Consequence: SOEC cells run at V_cell ≈ 1.10-1.40 V at design,
//   substantially BELOW the 1.481 V HHV thermo-neutral voltage. When
//   V_cell < V_TN the cell ABSORBS heat from the surroundings to
//   complete the endothermic reaction; the η_HHV = 1.481 / V_cell
//   ratio then exceeds 1.0. That is correct and physical — it is the
//   SOEC value proposition (efficient use of waste heat to drive
//   water-splitting). The total system HHV efficiency (electric + heat)
//   never exceeds 1.0; the per-electric ratio does.
//
// Anchors (Mogensen 2008, Stempien et al. 2013, Klotz 2017, Lessing
// 2011, Sunfire HyLink datasheet):
//   T_ref     = 1073.15 K (800 °C — SOEC cluster mid-band)
//   E_ref     = 0.923 V at 1073.15 K, 1 bar (Mogensen 2008 thermo)
//   dE/dT     = -0.234 mV/K (high-T steam electrolysis slope; gentler
//                            than the -0.85 mV/K liquid-water slope
//                            because the entropy contribution at high T
//                            differs by the latent heat of water
//                            vaporisation)
//   Tafel b   = 0.10 V/dec (cell-level effective; Klotz 2017)
//   i₀        = 0.5 A/cm²  (cell-level effective; Klotz 2017, Sun 2010)
//   R_AS      = 0.4 Ω·cm²  (anode-supported thin YSZ at 800 °C;
//                            Stempien 2013, Lessing 2011)
//
// References:
//   Mogensen M., Sun J., Hauch A., Hjelm J. (2008). "High-temperature
//     electrolysis of water — a review." Solid State Ionics 178.
//   Stempien J.P., Sun Q., Chan S.H. (2013). "Solid Oxide Electrolyzer
//     Cell Modeling: A Review." Journal of Power Technologies 93.
//   Klotz D., Leonide A., Weber A., Ivers-Tiffée E. (2017). "Process
//     identification of high-temperature electrolysis cells." Journal of
//     The Electrochemical Society 164.
//   Sun X., Bonaccorso A.D., Graves C., Ebbesen S.D., Jensen S.H., Hauch
//     A., Holtappels P., Hendriksen P.V., Mogensen M. (2010). "Performance
//     characterisation of a stack of solid oxide electrolysis cells."
//     ECS Transactions 35.
//   Lessing P.A. (2011). "A review of sealing technologies applicable to
//     solid oxide electrolysis cells." Journal of Materials Science 42.

using System;

namespace Voxelforge.Electrolyser;

/// <summary>
/// Closed-form SOEC (solid-oxide electrolyser cell) stack performance
/// snapshot solver (Sprint B.2-SOEC).
/// </summary>
internal static class SoecElectrolyserSolver
{
    /// <summary>Faraday constant [C/mol] — shared with PEM/AEM/Alkaline.</summary>
    internal const double Faraday_C_mol = 96485.0;

    /// <summary>Universal gas constant [J/(mol·K)] — shared with PEM/AEM/Alkaline.</summary>
    internal const double R_J_molK = 8.31446;

    /// <summary>
    /// SOEC Nernst reference temperature [K] (800 °C — cluster mid-band).
    /// Not shared with PEM/AEM/Alkaline because the steam-electrolysis
    /// reaction's thermodynamic reference is anchored at high T.
    /// </summary>
    internal const double SoecReferenceTemperature_K = 1073.15;

    /// <summary>
    /// SOEC Nernst voltage at the reference temperature (800 °C) and
    /// reference pressure (1 bar) [V]. Mogensen 2008 thermo-cluster value
    /// for the steam-electrolysis reaction H₂O(g) → H₂ + ½O₂. Much lower
    /// than the liquid-water E_Nernst at room temperature (1.229 V)
    /// because ΔG_rxn falls with rising T while ΔH stays nearly constant.
    /// </summary>
    internal const double SoecNernstVoltageAtReference_V = 0.923;

    /// <summary>
    /// SOEC Nernst temperature slope [V/K] — local linear fit in the
    /// 700-900 °C cluster. Much gentler than the liquid-water slope
    /// (-0.85 mV/K) because the entropy contribution at high T differs by
    /// the latent heat of water vaporisation, which is already paid
    /// upstream of the SOEC stack. Cluster fit from Mogensen 2008 +
    /// Stempien 2013.
    /// </summary>
    internal const double SoecNernstTemperatureSlope_V_K = -0.234e-3;

    /// <summary>Reference pressure [bar] — shared with PEM/AEM/Alkaline.</summary>
    internal const double ReferencePressure_bar = 1.0;

    /// <summary>
    /// Effective cell-level Tafel slope b [V/decade] for SOEC. Lumps the
    /// Ni-YSZ cathode HER + LSM/LSCF anode OER kinetics at 800 °C cluster
    /// mid-band. Cluster fit from Klotz 2017.
    /// </summary>
    internal const double TafelSlope_V_dec = 0.100;

    /// <summary>
    /// Effective cell-level exchange current density i₀ [A/cm²] for SOEC
    /// at 800 °C. Three to four orders of magnitude higher than liquid-T
    /// values (PEM/AEM/Alkaline ~ 1e-7) because Ni-YSZ + LSM/LSCF
    /// electrode kinetics are facile at SOEC operating T. Klotz 2017,
    /// Sun 2010 cluster mid-band.
    /// </summary>
    internal const double ExchangeCurrentDensity_A_cm2 = 0.5;

    /// <summary>
    /// Area-specific resistance R_AS [Ω·cm²] for an anode-supported thin
    /// YSZ electrolyte SOEC at 800 °C, including electrode interfaces.
    /// Cluster mid-band from Stempien 2013 + Lessing 2011. YSZ ionic
    /// conductivity is the limiting transport process; thinner
    /// electrolytes and lower-resistance interconnect coatings push this
    /// below 0.3 Ω·cm² in advanced cells; 0.4 is the commercial
    /// cluster centre.
    /// </summary>
    internal const double AreaSpecificResistance_OhmCm2 = 0.40;

    /// <summary>HHV thermo-neutral voltage [V] — shared with
    /// PEM/AEM/Alkaline. Constant reference for η_HHV calculation,
    /// defined at 25 °C / liquid-water for the H₂ + ½O₂ → H₂O(l) reverse
    /// reaction. SOEC's V_cell typically lands BELOW this constant — the
    /// cell absorbs heat from the surroundings, giving η_HHV &gt; 1.0 on
    /// the electric input (correct and physical).</summary>
    internal const double HhvThermoNeutralVoltage_V = 1.481;

    /// <summary>Molar mass of H₂ [kg/mol] — shared with PEM/AEM/Alkaline.</summary>
    internal const double MolarMassH2_kg_mol = 2.016e-3;

    /// <summary>Conversion factor 1 kg H₂ → Nm³ at STP — shared with PEM/AEM/Alkaline.</summary>
    internal const double NormalCubicMeterPerKgH2 = 11.126;

    /// <summary>
    /// Solve the SOEC stack performance snapshot at the design operating
    /// point.
    /// </summary>
    /// <param name="design">Validated SOEC electrolyser stack design.</param>
    /// <returns>Solved performance snapshot.</returns>
    internal static SoecElectrolyserResult Solve(SoecElectrolyserDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        double T_K = design.OperatingTemperature_C + 273.15;
        double P_bar = design.OperatingPressure_bar;
        double i = design.OperatingCurrentDensity_A_cm2;

        // 1. Nernst voltage — high-T steam-electrolysis formulation.
        //    Linear around the SOEC reference (800 °C), NOT the
        //    PEM/AEM/Alkaline ambient-water reference. The ln-P
        //    correction has the same Nernst form as the liquid-T kinds
        //    (still 2 electrons per H₂; pressure dependence is identical
        //    in the gas-phase reaction).
        double E_temperature = SoecNernstVoltageAtReference_V
                             + SoecNernstTemperatureSlope_V_K
                             * (T_K - SoecReferenceTemperature_K);
        double E_pressure_correction = (R_J_molK * T_K / (2.0 * Faraday_C_mol))
                                     * Math.Log(P_bar / ReferencePressure_bar);
        double E_Nernst = E_temperature + E_pressure_correction;

        // 2. Activation polarisation (lumped Ni-YSZ + LSM/LSCF). Small at
        //    SOEC operating T because i₀ is large.
        double tafelNaturalLogSlope = TafelSlope_V_dec / Math.Log(10.0);
        double eta_act = tafelNaturalLogSlope * Math.Log(i / ExchangeCurrentDensity_A_cm2);

        // 3. Ohmic polarisation — YSZ electrolyte + electrode interfaces.
        //    Dominates over η_act at typical SOEC current density.
        double eta_ohm = i * AreaSpecificResistance_OhmCm2;

        // 4. Cell voltage = E_Nernst + losses (same SIGN convention as
        //    the liquid-T kinds: V_cell > E_Nernst because the cell
        //    CONSUMES electric power).
        double V_cell  = E_Nernst + eta_act + eta_ohm;
        double V_stack = design.CellCount * V_cell;
        double I_stack = i * design.ActiveAreaPerCell_cm2;          // A
        double P_input = V_stack * I_stack;                          // W (stack-only electric; SOEC also absorbs heat at V_cell < V_TN)

        // 5. HHV efficiency = E_HHV / V_cell. Stack-only electric input.
        //    SOEC value: V_cell typically lands below V_TN = 1.481 V, so
        //    η_HHV exceeds 1.0 — heat absorption from the surroundings
        //    completes the endothermic water-splitting reaction. The
        //    total energy balance (electric + heat) never exceeds unity;
        //    the per-electric ratio does.
        double eta_HHV = HhvThermoNeutralVoltage_V / V_cell;

        // 6. Hydrogen production rate — Faraday's law (identical to
        //    PEM/AEM/Alkaline). 2 e⁻ per H₂ molecule regardless of
        //    operating T or charge-carrier identity.
        double m_H2_kgs = design.CellCount * I_stack * MolarMassH2_kg_mol
                       / (2.0 * Faraday_C_mol);
        double m_H2_Nm3_h = m_H2_kgs * NormalCubicMeterPerKgH2 * 3600.0;

        return new SoecElectrolyserResult(
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
