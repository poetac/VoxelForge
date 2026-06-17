// PemFuelCellSolver.cs — Sprint PG.W1 closed-form PEM fuel cell stack
// performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes the cell-voltage
// breakdown (Nernst − activation − ohmic − concentration), stack-level
// V/I/P, LHV efficiency, and heat rejection at the design operating
// point.
//
// The Wave-1 cluster-anchored fit uses:
//
//   V_cell = E_Nernst(T,P)  −  η_act(i)  −  η_ohm(i)  −  η_conc(i)
//
//   E_Nernst(T,P) = E0 − dE/dT·(T − T0) + (R T)/(2F) · ln(P_H2 · P_O2^0.5 / P_ref^1.5)
//                  Simplified to the canonical fit (Larminie & Dicks 2003 §3):
//                      E0 = 1.229 V, dE/dT ≈ -0.85e-3 V/K, P-coefficient is small at
//                      stack-class pressures; we land it via a linear Pa(P-1)/P0 anchor.
//   η_act = b · ln(i / i_0)   [Tafel] — cathode ORR is rate-limiting on Pt/C.
//   η_ohm = i · R_area_specific   [Ω·cm²].
//   η_conc = -B · ln(1 - i/i_L)   [mass-transport pinch as i → i_L].
//
// Cluster anchors (Larminie & Dicks 2003 chap 3 + Mench 2008 chap 6 +
// Toyota Mirai operational reports):
//   Tafel slope b = 0.070 V/dec  →  natural-log form: 0.0304 V (b/ln10)
//   Exchange current density i_0 = 1.0e-6 A/cm² (Pt/C ORR)
//   Area-specific resistance R_AS = 0.15 Ω·cm² (Nafion 117 membrane + contact)
//   Mass-transport limit i_L = 2.0 A/cm² (operational ceiling, well below
//                                          theoretical 4-5 A/cm² limit)
//   Concentration coefficient B = 0.050 V
//
// References:
//   Larminie J., Dicks A. (2003). "Fuel Cell Systems Explained," 2nd ed., chap 3.
//   Mench M. (2008). "Fuel Cell Engines," chap 6.
//   Springer T.E., Zawodzinski T.A., Gottesfeld S. (1991). "Polymer Electrolyte
//     Fuel Cell Model." J. Electrochem. Soc., 138 (8).

using System;

namespace Voxelforge.PowerGen;

/// <summary>
/// Closed-form PEM fuel cell stack performance snapshot solver
/// (Sprint PG.W1).
/// </summary>
internal static class PemFuelCellSolver
{
    /// <summary>Faraday's constant [C/mol]. Cell reaction transfers 2 e⁻/mol H₂.</summary>
    internal const double Faraday_C_mol = 96485.0;

    /// <summary>Universal gas constant [J/(mol·K)].</summary>
    internal const double R_J_molK = 8.31446;

    /// <summary>Reference Nernst potential at standard conditions [V]
    /// (Larminie & Dicks 2003 §3.1).</summary>
    internal const double StandardNernstVoltage_V = 1.229;

    /// <summary>Linear entropy correction to Nernst with temperature
    /// [V/K]. From d(ΔG)/dT for the cell reaction at constant P.</summary>
    internal const double NernstTemperatureSlope_V_K = -0.85e-3;

    /// <summary>Reference temperature for the Nernst anchor [K] (25 °C).</summary>
    internal const double ReferenceTemperature_K = 298.15;

    /// <summary>Reference pressure for the Nernst anchor [bar] (1.0 bar absolute).</summary>
    internal const double ReferencePressure_bar = 1.0;

    /// <summary>
    /// Tafel slope b [V/decade] for the cathode oxygen-reduction reaction
    /// on Pt/C. Cluster mid-band 60-80 mV/decade.
    /// </summary>
    internal const double TafelSlope_V_dec = 0.070;

    /// <summary>
    /// Exchange current density i_0 [A/cm²] for cathode ORR on Pt/C.
    /// Cluster mid-band 1e-7 to 1e-5 A/cm²; we anchor at the upper edge
    /// 1e-5 to land V_cell ≈ 0.66 V at the Toyota-Mirai-class
    /// (i = 1.0 A/cm², T = 80 °C, P = 2.5 bar) operating point.
    /// </summary>
    internal const double ExchangeCurrentDensity_A_cm2 = 1.0e-5;

    /// <summary>
    /// Area-specific membrane + interfacial resistance R_AS [Ω·cm²].
    /// Cluster mid-band for Nafion-117 + carbon GDL contacts.
    /// </summary>
    internal const double AreaSpecificResistance_OhmCm2 = 0.15;

    /// <summary>
    /// Mass-transport limit current density i_L [A/cm²]. Operational
    /// ceiling well below the theoretical 4-5 A/cm² stoichiometric
    /// limit; cluster mid-band 1.5-2.5 A/cm² for air-breathing cathodes.
    /// </summary>
    internal const double LimitingCurrentDensity_A_cm2 = 2.0;

    /// <summary>Concentration-polarisation coefficient B [V]
    /// (Springer-Gottesfeld 1991 fit).</summary>
    internal const double ConcentrationCoefficient_V = 0.050;

    /// <summary>
    /// LHV-referenced thermo-neutral voltage [V]. P_elec / (V_LHV · I)
    /// is the LHV efficiency directly.
    /// </summary>
    internal const double LhvThermoNeutralVoltage_V = 1.254;

    private static readonly double Ln10 = Math.Log(10.0);

    /// <summary>
    /// Solve the PEM stack at an arbitrary current density (Sprint
    /// PG.W2). Pure-physics evaluation; never throws on the
    /// concentration-loss singularity (returns +∞ V_cell drop, which
    /// produces a negative V_cell — the caller / gates filter).
    /// </summary>
    internal static PemFuelCellResult SolveAtCurrentDensity(
        PemFuelCellDesign design,
        double            currentDensity_A_cm2)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        if (currentDensity_A_cm2 < 0.0)
            throw new ArgumentOutOfRangeException(nameof(currentDensity_A_cm2),
                $"currentDensity_A_cm2 must be ≥ 0; got {currentDensity_A_cm2}.");

        return SolveCore(design, currentDensity_A_cm2);
    }

    /// <summary>
    /// Sweep the polarisation curve across an array of current densities
    /// (Sprint PG.W2). Used for figure-of-merit characterisation +
    /// finding the peak-power point.
    /// </summary>
    /// <param name="design">Validated PEM fuel cell stack design.</param>
    /// <param name="currentDensities_A_cm2">Array of i samples [A/cm²]. Must be sorted ascending; non-negative.</param>
    /// <returns>One <see cref="PolarisationCurvePoint"/> per input sample.</returns>
    internal static PolarisationCurvePoint[] SolvePolarisationCurve(
        PemFuelCellDesign design,
        double[]          currentDensities_A_cm2)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(currentDensities_A_cm2);
        design.ValidateSelf();
        if (currentDensities_A_cm2.Length == 0)
            throw new ArgumentException(
                "currentDensities_A_cm2 must contain at least one sample.",
                nameof(currentDensities_A_cm2));

        var points = new PolarisationCurvePoint[currentDensities_A_cm2.Length];
        for (int k = 0; k < currentDensities_A_cm2.Length; k++)
        {
            double i = currentDensities_A_cm2[k];
            if (i < 0.0)
                throw new ArgumentException(
                    $"Sample {k} = {i} must be ≥ 0.", nameof(currentDensities_A_cm2));
            if (k > 0 && i < currentDensities_A_cm2[k - 1])
                throw new ArgumentException(
                    "currentDensities_A_cm2 must be sorted ascending.",
                    nameof(currentDensities_A_cm2));

            var snapshot = SolveCore(design, i);
            points[k] = new PolarisationCurvePoint(
                CurrentDensity_A_cm2:  i,
                CellVoltage_V:         snapshot.CellVoltage_V,
                StackElectricPower_W:  snapshot.StackElectricPower_W,
                PowerDensity_W_cm2:    snapshot.CellVoltage_V * i);
        }
        return points;
    }

    /// <summary>
    /// Solve the PEM stack performance snapshot at the design operating
    /// point.
    /// </summary>
    /// <param name="design">Validated PEM fuel cell stack design.</param>
    /// <returns>Solved performance snapshot.</returns>
    internal static PemFuelCellResult Solve(PemFuelCellDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        return SolveCore(design, design.OperatingCurrentDensity_A_cm2);
    }

    /// <summary>
    /// Core arithmetic. Assumes the design has already been validated;
    /// the i parameter is the operating current density at which to
    /// evaluate. Shared by <see cref="Solve"/>,
    /// <see cref="SolveAtCurrentDensity"/>, and
    /// <see cref="SolvePolarisationCurve"/>.
    /// </summary>
    private static PemFuelCellResult SolveCore(PemFuelCellDesign design, double i)
    {
        double T_K = design.OperatingTemperature_C + 273.15;
        double P_bar = design.OperatingPressure_bar;

        // 1. Nernst voltage. Linear entropy term + pressure correction
        //    via (R·T)/(2·F)·ln(P/P_ref). Single-reactant simplification
        //    bundles H₂ + air at the same total stack pressure.
        double E_temperature = StandardNernstVoltage_V
                             + NernstTemperatureSlope_V_K * (T_K - ReferenceTemperature_K);
        double E_pressure_correction = (R_J_molK * T_K / (2.0 * Faraday_C_mol))
                                     * Math.Log(P_bar / ReferencePressure_bar);
        double E_Nernst = E_temperature + E_pressure_correction;

        // 2. Activation polarisation (cathode-dominated Tafel form).
        //    The natural-log form: η_act = (b / ln10) · ln(i/i_0). At
        //    i = 0 the Tafel form is undefined (η_act → −∞); we clamp to
        //    zero so the open-circuit point sits at exactly E_Nernst.
        double eta_act;
        if (i <= 0.0)
        {
            eta_act = 0.0;
        }
        else
        {
            double tafelNaturalLogSlope = TafelSlope_V_dec / Ln10;
            eta_act = tafelNaturalLogSlope * Math.Log(i / ExchangeCurrentDensity_A_cm2);
        }

        // 3. Ohmic polarisation.
        double eta_ohm = i * AreaSpecificResistance_OhmCm2;

        // 4. Concentration polarisation — diverges as i → i_L.
        double eta_conc;
        if (i <= 0.0)
            eta_conc = 0.0;
        else if (i >= LimitingCurrentDensity_A_cm2)
            eta_conc = double.PositiveInfinity;
        else
            eta_conc = -ConcentrationCoefficient_V * Math.Log(1.0 - i / LimitingCurrentDensity_A_cm2);

        // 5. Cell voltage + stack roll-up.
        double V_cell  = E_Nernst - eta_act - eta_ohm - eta_conc;
        double V_stack = design.CellCount * V_cell;
        double I_stack = i * design.ActiveAreaPerCell_cm2;     // A (single-cell series current)
        double P_elec  = V_stack * I_stack;                    // W

        // 6. LHV efficiency = V_cell / V_LHV. Heat rejection balances
        //    the (V_LHV − V_cell) per-cell drop times current times N_cells.
        double eta_LHV = V_cell / LhvThermoNeutralVoltage_V;
        double Q_heat  = design.CellCount * (LhvThermoNeutralVoltage_V - V_cell) * I_stack;

        return new PemFuelCellResult(
            NernstVoltage_V:      E_Nernst,
            ActivationLoss_V:     eta_act,
            OhmicLoss_V:          eta_ohm,
            ConcentrationLoss_V:  eta_conc,
            CellVoltage_V:        V_cell,
            StackVoltage_V:       V_stack,
            StackCurrent_A:       I_stack,
            StackElectricPower_W: P_elec,
            LhvEfficiency:        eta_LHV,
            HeatRejectionPower_W: Q_heat);
    }
}
