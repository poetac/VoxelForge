// IdealGasAir.cs — constant-property + temperature-dependent ideal-gas
// air model.
//
// Sprint A4 shipped the constant-cp / constant-γ assumption — Mattingly's
// "ideal cycle" approximation. The follow-on (this file) adds cp(T)
// tables for both dry air and kerosene-class burnt-gas products. The
// cycle solvers consult cp_air(T) on the cold side (stations 0-3) and
// cp_burnt_kerosene(T) on the hot side (stations 4-9) when the fuel is
// Jet-A or JP-8. For H2 fuel the kerosene burnt-gas curve does not
// apply; H2 paths keep the constant-cp behaviour.
//
// The original const Cp_J_kg_K = γ·R/(γ−1) ≈ 1004.685 J/(kg·K) is
// retained verbatim because (a) it appears in textbook ideal-gas
// identities the existing helpers rely on (Mayer's relation, the
// stagnation ratios), and (b) the existing Cp_DerivedFromGammaAndR
// test pins the algebraic form. The new CpAir(288.15) returns ≈ 1004.6
// J/(kg·K) (NIST/Cengel measurement), which agrees with the algebraic
// form to ~0.01 % — close enough that mixing the two doesn't perturb
// existing fixtures.
//
// Why a separate type from StandardAtmosphere
// -------------------------------------------
// StandardAtmosphere returns *static* freestream state at altitude;
// IdealGasAir provides the *thermodynamic constants and tables* used
// to convert static ↔ stagnation, derive Mach, compute enthalpy, etc.

using System;

namespace Voxelforge.Airbreathing.Thermo;

/// <summary>
/// Ideal-gas air model — γ = 1.40, R = 287.05 J/(kg·K). Provides both
/// the constant-cp algebraic form (<see cref="Cp_J_kg_K"/>) used by
/// stagnation / Mach helpers, and tabulated cp(T) functions
/// (<see cref="CpAir"/>, <see cref="CpBurntKerosene"/>) used by cycle
/// solvers when temperature-dependent properties matter.
/// </summary>
public static class IdealGasAir
{
    /// <summary>Specific gas constant for air [J/(kg·K)] — matches <see cref="Atmosphere.StandardAtmosphere.AirSpecificGasConstant_J_kg_K"/>.</summary>
    public const double R_J_kg_K = 287.05287;

    /// <summary>Constant ratio of specific heats — matches <see cref="Atmosphere.StandardAtmosphere.AirGamma"/>.</summary>
    public const double Gamma = 1.40;

    /// <summary>
    /// Constant-pressure specific heat [J/(kg·K)]. Algebraic form
    /// cp = γ · R / (γ − 1) = 1004.685 J/(kg·K). Retained for stagnation /
    /// Mach helpers and for engines where temperature-dependent cp is
    /// not warranted (e.g. H2 fuel on the hot side, where the kerosene
    /// burnt-gas curve doesn't apply). Use <see cref="CpAir"/> for the
    /// NIST tabulated form when station temperature span matters.
    /// </summary>
    public const double Cp_J_kg_K = Gamma * R_J_kg_K / (Gamma - 1.0);

    /// <summary>
    /// Constant-volume specific heat [J/(kg·K)] = cp − R = R / (γ − 1).
    /// </summary>
    public const double Cv_J_kg_K = R_J_kg_K / (Gamma - 1.0);

    /// <summary>
    /// Specific enthalpy [J/kg] referenced to T = 0 K, h(T) = cp · T.
    /// Constant-cp form — only enthalpy *differences* are physical,
    /// not the absolute value. Use <see cref="EnthalpyAir"/> for the
    /// tabulated cp(T) form referenced to T_ref = 200 K.
    /// </summary>
    public static double Enthalpy_J_kg(double T_K) => Cp_J_kg_K * T_K;

    /// <summary>
    /// Speed of sound a = √(γRT) [m/s] for an ideal-gas air parcel at
    /// static temperature <paramref name="T_K"/>.
    /// </summary>
    public static double SpeedOfSound_m_s(double T_K)
        => System.Math.Sqrt(Gamma * R_J_kg_K * T_K);

    /// <summary>
    /// Stagnation-to-static temperature ratio for compressible flow:
    /// <c>T_t / T = 1 + ((γ−1)/2)·M²</c>.
    /// </summary>
    public static double StagnationTemperatureRatio(double machNumber)
        => 1.0 + 0.5 * (Gamma - 1.0) * machNumber * machNumber;

    /// <summary>
    /// Stagnation-to-static pressure ratio for compressible isentropic
    /// flow: <c>P_t / P = (1 + ((γ−1)/2)·M²)^(γ/(γ−1))</c>.
    /// </summary>
    public static double StagnationPressureRatio(double machNumber)
        => System.Math.Pow(StagnationTemperatureRatio(machNumber), Gamma / (Gamma - 1.0));

    /// <summary>
    /// Inverse of <see cref="StagnationPressureRatio"/>: given a
    /// stagnation/static pressure ratio (≥ 1), return the Mach number
    /// that would produce it under isentropic flow with γ = 1.40.
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">Ratio &lt; 1.</exception>
    public static double MachFromStagnationPressureRatio(double pTotalOverPStatic)
    {
        if (pTotalOverPStatic < 1.0)
            throw new System.ArgumentOutOfRangeException(nameof(pTotalOverPStatic),
                $"Stagnation/static pressure ratio {pTotalOverPStatic} must be ≥ 1.");
        double exponent = (Gamma - 1.0) / Gamma;
        double tempRatio = System.Math.Pow(pTotalOverPStatic, exponent);
        double m2 = (tempRatio - 1.0) * 2.0 / (Gamma - 1.0);
        return System.Math.Sqrt(m2);
    }

    // -----------------------------------------------------------------
    // cp(T) tables
    // -----------------------------------------------------------------

    /// <summary>Reference temperature [K] for the tabulated enthalpy integrals.</summary>
    public const double EnthalpyReferenceT_K = 200.0;

    /// <summary>Lower bound of the cp(T) tables [K].</summary>
    public const double CpTable_T_min_K = 200.0;

    /// <summary>Upper bound of the cp(T) tables [K].</summary>
    public const double CpTable_T_max_K = 2200.0;

    // 21-point grid: 200, 300, ..., 2200 K (100-K step).
    private static readonly double[] _T_grid_K =
    {
        200.0, 300.0, 400.0, 500.0, 600.0, 700.0,
        800.0, 900.0, 1000.0, 1100.0, 1200.0, 1300.0,
        1400.0, 1500.0, 1600.0, 1700.0, 1800.0, 1900.0,
        2000.0, 2100.0, 2200.0,
    };

    // Dry-air cp(T) [J/(kg·K)]. Source: Cengel & Boles, *Thermodynamics:
    // An Engineering Approach* (8th ed., 2015) Table A-22 — ideal-gas
    // specific heats for dry air, derived from NIST JANAF tables.
    // Cross-check: cp_air(288.15 K) interpolates to ~1004.6 J/(kg·K),
    // matching the algebraic γR/(γ−1) = 1004.685 to ~0.01 %.
    private static readonly double[] _CpAir_J_kg_K =
    {
        1002.5,  // 200 K
        1004.9,  // 300 K
        1014.0,  // 400 K
        1029.5,  // 500 K
        1051.1,  // 600 K
        1075.0,  // 700 K
        1098.7,  // 800 K
        1121.1,  // 900 K
        1141.7,  // 1000 K
        1160.0,  // 1100 K
        1175.6,  // 1200 K
        1189.0,  // 1300 K
        1200.8,  // 1400 K
        1211.1,  // 1500 K
        1220.1,  // 1600 K
        1228.0,  // 1700 K
        1234.9,  // 1800 K
        1240.9,  // 1900 K
        1246.2,  // 2000 K
        1250.9,  // 2100 K
        1255.0,  // 2200 K
    };

    // Kerosene burnt-gas cp(T) [J/(kg·K)] — combustion products of
    // Jet-A / JP-8 in air at ~stoichiometric. Source: Mattingly,
    // *Elements of Propulsion: Gas Turbines and Rockets* (AIAA 2006),
    // Appendix B Table B.1 (products of combustion of hydrocarbon
    // fuels with air). Used at stations 4-9 only when the fuel is
    // kerosene-class (Jet-A or JP-8); for H2 fuel the kerosene curve
    // does not apply and the cycle solver falls back to constant cp.
    private static readonly double[] _CpBurntKerosene_J_kg_K =
    {
        1003.0,  // 200 K
        1005.0,  // 300 K
        1024.0,  // 400 K
        1057.0,  // 500 K
        1093.0,  // 600 K
        1130.0,  // 700 K
        1165.0,  // 800 K
        1196.0,  // 900 K
        1224.0,  // 1000 K
        1247.0,  // 1100 K
        1268.0,  // 1200 K
        1287.0,  // 1300 K
        1304.0,  // 1400 K
        1320.0,  // 1500 K
        1335.0,  // 1600 K
        1349.0,  // 1700 K
        1362.0,  // 1800 K
        1374.0,  // 1900 K
        1385.0,  // 2000 K
        1395.0,  // 2100 K
        1405.0,  // 2200 K
    };

    // Cumulative enthalpy at each grid point [J/kg], referenced to
    // EnthalpyReferenceT_K = 200 K. Computed once via trapezoidal-rule
    // integration of the cp(T) tables. Static-constructor populated.
    private static readonly double[] _HAir_J_kg;
    private static readonly double[] _HBurntKerosene_J_kg;

    static IdealGasAir()
    {
        _HAir_J_kg = ComputeCumulativeEnthalpy(_T_grid_K, _CpAir_J_kg_K);
        _HBurntKerosene_J_kg = ComputeCumulativeEnthalpy(_T_grid_K, _CpBurntKerosene_J_kg_K);
    }

    /// <summary>
    /// Tabulated cp(T) for dry air [J/(kg·K)]. Linear interpolation
    /// over the 200-2200 K NIST/Cengel grid; edge-clamped outside.
    /// </summary>
    public static double CpAir(double T_K) => Interp(T_K, _T_grid_K, _CpAir_J_kg_K);

    /// <summary>
    /// Tabulated cp(T) for stoichiometric kerosene-class burnt gas
    /// (Jet-A / JP-8 combustion products in air) [J/(kg·K)]. Linear
    /// interpolation over the 200-2200 K Mattingly App. B grid;
    /// edge-clamped outside. Use only when the fuel is Jet-A or JP-8.
    /// </summary>
    public static double CpBurntKerosene(double T_K) => Interp(T_K, _T_grid_K, _CpBurntKerosene_J_kg_K);

    /// <summary>
    /// Specific enthalpy of dry air [J/kg], referenced to
    /// <see cref="EnthalpyReferenceT_K"/> = 200 K. Trapezoidal-rule
    /// integral of cp_air from 200 K to <paramref name="T_K"/>.
    /// Edge-clamped at the table extremes.
    /// </summary>
    public static double EnthalpyAir(double T_K)
        => InterpEnthalpy(T_K, _T_grid_K, _CpAir_J_kg_K, _HAir_J_kg);

    /// <summary>
    /// Specific enthalpy of stoichiometric kerosene-class burnt gas
    /// [J/kg], referenced to <see cref="EnthalpyReferenceT_K"/> = 200 K.
    /// </summary>
    public static double EnthalpyBurntKerosene(double T_K)
        => InterpEnthalpy(T_K, _T_grid_K, _CpBurntKerosene_J_kg_K, _HBurntKerosene_J_kg);

    /// <summary>
    /// Inverse of <see cref="EnthalpyBurntKerosene"/>: given a target
    /// enthalpy [J/kg], return the temperature [K] that produces it.
    /// Bisection + linear interp inside the bracket. Edge-clamped at
    /// the table extremes (returns 200 K for h ≤ 0; returns 2200 K
    /// for h ≥ EnthalpyBurntKerosene(2200)).
    /// </summary>
    public static double InvertEnthalpyBurntKerosene(double h_J_kg)
        => InvertEnthalpy(h_J_kg, _T_grid_K, _HBurntKerosene_J_kg);

    /// <summary>
    /// Inverse of <see cref="EnthalpyAir"/>: given a target enthalpy
    /// [J/kg], return the temperature [K] that produces it.
    /// </summary>
    public static double InvertEnthalpyAir(double h_J_kg)
        => InvertEnthalpy(h_J_kg, _T_grid_K, _HAir_J_kg);

    // -----------------------------------------------------------------
    // private helpers
    // -----------------------------------------------------------------

    private static double Interp(double T_K, double[] xs, double[] ys)
    {
        if (T_K <= xs[0]) return ys[0];
        if (T_K >= xs[xs.Length - 1]) return ys[xs.Length - 1];
        int i = 0;
        while (i < xs.Length - 1 && xs[i + 1] < T_K) i++;
        double frac = (T_K - xs[i]) / (xs[i + 1] - xs[i]);
        return ys[i] + frac * (ys[i + 1] - ys[i]);
    }

    private static double InterpEnthalpy(double T_K, double[] xs, double[] cps, double[] hs)
    {
        if (T_K <= xs[0]) return 0.0;
        if (T_K >= xs[xs.Length - 1]) return hs[hs.Length - 1];
        int i = 0;
        while (i < xs.Length - 1 && xs[i + 1] < T_K) i++;
        // Inside the bracket, integrate cp(T) from xs[i] to T_K with
        // trapezoidal rule on linear-in-T cp.
        double dT = T_K - xs[i];
        double cp_T = Interp(T_K, xs, cps);
        double meanCp = 0.5 * (cps[i] + cp_T);
        return hs[i] + meanCp * dT;
    }

    private static double[] ComputeCumulativeEnthalpy(double[] xs, double[] cps)
    {
        var hs = new double[xs.Length];
        hs[0] = 0.0;
        for (int i = 1; i < xs.Length; i++)
        {
            double meanCp = 0.5 * (cps[i - 1] + cps[i]);
            hs[i] = hs[i - 1] + meanCp * (xs[i] - xs[i - 1]);
        }
        return hs;
    }

    private static double InvertEnthalpy(double h_J_kg, double[] xs, double[] hs)
    {
        if (h_J_kg <= 0.0) return xs[0];
        if (h_J_kg >= hs[hs.Length - 1]) return xs[xs.Length - 1];
        int i = 0;
        while (i < hs.Length - 1 && hs[i + 1] < h_J_kg) i++;
        double frac = (h_J_kg - hs[i]) / (hs[i + 1] - hs[i]);
        return xs[i] + frac * (xs[i + 1] - xs[i]);
    }
}
