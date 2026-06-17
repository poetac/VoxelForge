// J85ClassCompressorMap.cs — table-based off-design compressor map
// for J85-class single-spool axial compressors.
//
// Source attribution
// ------------------
// Real GE J85 OEM compressor maps are proprietary and not in open
// data. The tables below are class-similar — synthesized from
// Mattingly *Elements of Propulsion: Gas Turbines and Rockets*
// (AIAA 2006) Chapter 8 representative single-spool axial-compressor
// map (Fig. 8.4-class), normalized so the 100 % corrected-speed line
// passes through the J85-21 design point (π_c = 8.0, ṁ_corr ≈ 20 kg/s,
// η_c ≈ 0.85). The J85 design point itself is from declassified GE
// J85 spec sheets + USAF F-5E performance manuals.
//
// This is the "right" honest framing: J85 *design point* is real;
// the *off-design surface* around it is a class-typical preliminary-
// design fit, suitable for ±10-15 % cycle-prediction tolerance.
// Real-OEM-map fidelity is deferred to a follow-on sprint when an
// owner of the data engages.
//
// Sprint-scope simplifications (deferred)
// ---------------------------------------
//   1. Operating point assumed at 100 % corrected speed (mil-power
//      dry / design point). Off-design speed (partial throttle,
//      windmilling, transient) requires 2D Newton iteration over
//      (N_corr, ṁ_corr) and is deferred.
//   2. Bilinear interpolation between speed lines is not used because
//      we lock to 100 % N. Adjacent speed lines (60 / 80 / 90 / 110)
//      are present so a future phase can extend.
//   3. The map's own corrected-mass-flow lookup is reported as a
//      diagnostic, but the cycle solver's inlet-face-Mach ṁ_a model
//      remains authoritative for ṁ_a in this sprint.

using System;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Table-based off-design compressor map for J85-class single-spool
/// axial compressors. Models the 60 / 80 / 90 / 100 / 110 % corrected-
/// speed envelope with surge and choke boundaries; lookup at 100 % N.
/// </summary>
public sealed class J85ClassCompressorMap : ICompressorMap
{
    /// <summary>Standard reference temperature [K] for corrected-flow / corrected-speed scaling.</summary>
    public const double T_ref_K = 288.15;

    /// <summary>Standard reference pressure [Pa] for corrected-flow scaling.</summary>
    public const double P_ref_Pa = 101_325.0;

    /// <summary>Default-constructed singleton — production cycle solver path.</summary>
    public static readonly J85ClassCompressorMap Default = new();

    // ------------------------------------------------------------------
    // 100 % N_corr right-branch speed line (the operational branch).
    // Right branch = monotonically decreasing π_c as ṁ_corr increases
    // past the surge peak. Operating point at the design intersection
    // (ṁ_corr = 20 kg/s, π_c = 8.0, η_c = 0.85).
    // Eight points spanning surge → choke.
    // ------------------------------------------------------------------

    private static readonly double[] _MdotCorr_100pct =
        { 18.0, 19.0, 20.0, 21.0, 21.5, 22.0, 22.5, 22.7 };

    private static readonly double[] _Pi_100pct =
        { 9.4,  8.7,  8.0,  7.0,  6.5,  5.8,  4.5,  4.0 };

    private static readonly double[] _Eta_100pct =
        { 0.83, 0.84, 0.85, 0.84, 0.82, 0.78, 0.70, 0.62 };

    // ------------------------------------------------------------------
    // Surge line — locus of speed-line peaks (ṁ_corr_peak, π_peak)
    // for each speed line. Defines the upper-π envelope.
    // ------------------------------------------------------------------

    private static readonly double[] _SurgeLine_NCorr_pct = { 60.0, 80.0, 90.0, 100.0, 110.0 };
    private static readonly double[] _SurgeLine_MdotCorr  = { 7.0, 12.0, 16.0,  18.0,  22.0 };
    private static readonly double[] _SurgeLine_Pi        = { 1.7,  4.0,  6.0,   9.4,  11.0 };

    // ------------------------------------------------------------------
    // Choke line — locus of speed-line right-edge ṁ_corr (max ṁ before
    // the next speed-line jump). Defines the lower-π / max-ṁ envelope.
    // ------------------------------------------------------------------

    private static readonly double[] _ChokeLine_NCorr_pct  = { 60.0, 80.0, 90.0, 100.0, 110.0 };
    private static readonly double[] _ChokeLine_MdotCorr   = { 11.5, 18.5, 20.5, 22.7,  26.7 };

    // For Operate at 100 % N: the choke ṁ_corr.
    private const double Choke_MdotCorr_100pct = 22.7;

    /// <inheritdoc />
    public CompressorPoint Operate(double inletStagnationT_K, double inletStagnationP_Pa, double pressureRatio)
    {
        if (pressureRatio < 1.0)
            throw new ArgumentOutOfRangeException(nameof(pressureRatio),
                $"Compressor pressure ratio {pressureRatio} must be ≥ 1.");

        // 1. Look up corrected mass flow + isentropic efficiency at the
        //    requested π_c on the 100 % N right branch. Outside the
        //    bracket, edge-clamp + flag in diagnostics.
        bool outOfMap = pressureRatio > _Pi_100pct[0] || pressureRatio < _Pi_100pct[^1];
        double mdotCorr = InterpRightBranch(pressureRatio, _Pi_100pct, _MdotCorr_100pct);
        double eta_c    = InterpRightBranch(pressureRatio, _Pi_100pct, _Eta_100pct);

        // 2. Compute outlet T via cp_air(T)-aware enthalpy. Use γ-based
        //    isentropic ratio for the temperature step (cold-side γ=1.40
        //    is acceptable for compressor T < 600 K), then convert via
        //    enthalpy to handle the small cp(T) drift in the integration.
        double gamma = IdealGasAir.Gamma;
        double T_isentropic = inletStagnationT_K
            * Math.Pow(pressureRatio, (gamma - 1.0) / gamma);
        double h_isen   = IdealGasAir.EnthalpyAir(T_isentropic);
        double h_in     = IdealGasAir.EnthalpyAir(inletStagnationT_K);
        double W_isen   = h_isen - h_in;
        double W_actual = W_isen / eta_c;
        double h_actual = h_in + W_actual;
        double T_actual = IdealGasAir.InvertEnthalpyAir(h_actual);

        double P_outlet = inletStagnationP_Pa * pressureRatio;

        // 3. Compute diagnostics. Surge margin uses the surge-line
        //    interpolation at ṁ_corr_op; choke margin is ṁ_corr_op /
        //    ṁ_corr_choke at 100 % N.
        double piSurgeAtMdot = InterpAt(mdotCorr, _SurgeLine_MdotCorr, _SurgeLine_Pi);
        double surgeMargin   = piSurgeAtMdot > 0.0
            ? (piSurgeAtMdot - pressureRatio) / piSurgeAtMdot
            : double.NaN;
        double chokeMarginRel = mdotCorr / Choke_MdotCorr_100pct;

        // Above-surge or past-choke = out of map. Pin the diagnostics
        // to flag these explicitly so the gate evaluator can read them.
        if (outOfMap)
        {
            // Force-negative SM and >1 ChokeMarginRel sentinel so both
            // CORRECTED_MASS_FLOW_OUT_OF_MAP triggers fire.
            surgeMargin    = pressureRatio > _Pi_100pct[0] ? -1.0 : surgeMargin;
            chokeMarginRel = pressureRatio < _Pi_100pct[^1] ? 2.0 : chokeMarginRel;
        }

        return new CompressorPoint(
            OutletStagnationT_K:    T_actual,
            OutletStagnationP_Pa:   P_outlet,
            IsentropicEfficiency:   eta_c,
            SpecificWork_J_kg:      W_actual)
        {
            Diagnostics = new MapInfo(
                SurgeMargin:               surgeMargin,
                CorrectedMassFlow_kg_s:    mdotCorr,
                ChokeMarginRel:            chokeMarginRel),
        };
    }

    // ------------------------------------------------------------------
    // Lookup helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Interpolate the ordinate of a *monotonically-decreasing* abscissa
    /// table (the right-branch speed line: π decreases as ṁ rises).
    /// Edge-clamps outside the bracket.
    /// </summary>
    private static double InterpRightBranch(double pi, double[] piTable, double[] yTable)
    {
        if (pi >= piTable[0]) return yTable[0];
        if (pi <= piTable[^1]) return yTable[^1];
        int i = 0;
        while (i < piTable.Length - 1 && piTable[i + 1] > pi) i++;
        // piTable[i] >= pi > piTable[i+1]
        double frac = (piTable[i] - pi) / (piTable[i] - piTable[i + 1]);
        return yTable[i] + frac * (yTable[i + 1] - yTable[i]);
    }

    /// <summary>
    /// Linear interpolation on a monotonically-rising table; edge-clamp
    /// outside the bracket.
    /// </summary>
    private static double InterpAt(double x, double[] xs, double[] ys)
    {
        if (x <= xs[0]) return ys[0];
        if (x >= xs[^1]) return ys[^1];
        int i = 0;
        while (i < xs.Length - 1 && xs[i + 1] < x) i++;
        double frac = (x - xs[i]) / (xs[i + 1] - xs[i]);
        return ys[i] + frac * (ys[i + 1] - ys[i]);
    }
}
