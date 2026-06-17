// J85ClassTurbineMap.cs — table-based off-design turbine map for
// J85-class single-stage axial turbines.
//
// Source attribution
// ------------------
// Sibling to J85ClassCompressorMap. Real GE J85 OEM turbine maps
// are proprietary. The tables below are class-similar — synthesized
// from Mattingly Appendix B representative single-stage axial-turbine
// map, normalized so the 100 % corrected-speed line passes through
// the J85-21 design point (T_t4 = 1175 K, π_t ≈ 2.8, η_t ≈ 0.90).
// J85 design point itself is from declassified GE J85 spec sheets.
//
// Turbines have a much wider operating envelope than compressors —
// most single-stage axial turbines run choked at the first nozzle
// vanes across most of their range, so corrected mass flow is nearly
// constant once choked. The diagnostics returned here reflect that:
// SurgeMargin is reported as +∞ (turbines don't surge — they choke or
// stall the rotor); ChokeMarginRel is meaningful (operates near 1.0
// across most of the envelope by design).
//
// Sprint-scope simplifications (deferred)
// ---------------------------------------
//   1. Operating point assumed at 100 % corrected speed (matched to
//      compressor). Off-design speed deferred.
//   2. Surge concept doesn't apply to turbines (nozzle-fed, vane-
//      stalled, not blade-stalled like compressors). Reported as +∞
//      for diagnostic uniformity with compressor.

using System;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Table-based off-design turbine map for J85-class single-stage axial
/// turbines. Models the corrected-speed envelope at 100 % N with a
/// choke ceiling; lookup at design speed.
/// </summary>
public sealed class J85ClassTurbineMap : ITurbineMap
{
    /// <summary>Standard reference temperature [K] for corrected scaling.</summary>
    public const double T_ref_K = 288.15;

    /// <summary>Standard reference pressure [Pa] for corrected scaling.</summary>
    public const double P_ref_Pa = 101_325.0;

    /// <summary>Default-constructed singleton — production cycle solver path.</summary>
    public static readonly J85ClassTurbineMap Default = new();

    /// <summary>
    /// J85-class turbine corrected-mass-flow ceiling at 100 % N
    /// [kg/s pseudo-units]. First-vane-choked turbines sit very near
    /// this value across most of the operating envelope.
    /// </summary>
    public const double Choke_MdotCorr_100pct = 7.5;

    // ------------------------------------------------------------------
    // 100 % N_corr efficiency vs (W_required / cp_burnt(T_t4) / T_t4) —
    // a non-dimensional energy-extraction parameter. For J85-class
    // single-stage axial turbines, η_t spans 0.86-0.91 across the
    // operating range with peak near nominal extraction. Below nominal
    // (low W_required, partial throttle) η falls; above nominal (over-
    // extracted), η also falls but the turbine still works mechanically.
    // ------------------------------------------------------------------

    private static readonly double[] _ExtractionFraction =
        { 0.50, 0.70, 0.85, 1.00, 1.15, 1.30, 1.50 };

    private static readonly double[] _Eta_100pct =
        { 0.84, 0.87, 0.89, 0.90, 0.89, 0.86, 0.78 };

    /// <inheritdoc />
    public TurbinePoint Operate(double inletStagnationT_K, double inletStagnationP_Pa, double requiredSpecificWork_J_kg_total)
    {
        if (inletStagnationT_K <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(inletStagnationT_K),
                $"Turbine inlet T = {inletStagnationT_K} K must be positive.");
        if (inletStagnationP_Pa <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(inletStagnationP_Pa),
                $"Turbine inlet P = {inletStagnationP_Pa} Pa must be positive.");

        // 1. cp(T)-aware energy balance: h_burnt(T_t5) = h_burnt(T_t4) − W_required.
        //    Newton-style: invert h_burnt to find T_t5_actual.
        double h_t4 = IdealGasAir.EnthalpyBurntKerosene(inletStagnationT_K);
        double h_t5_actual = h_t4 - requiredSpecificWork_J_kg_total;
        if (h_t5_actual <= 0.0)
            throw new InvalidOperationException(
                $"Turbine outlet enthalpy = {h_t5_actual:F0} J/kg (≤ 0) — required work "
              + $"{requiredSpecificWork_J_kg_total:F0} J/kg exceeds available "
              + $"enthalpy at T_t4 = {inletStagnationT_K:F1} K.");

        double T_actual_outlet = IdealGasAir.InvertEnthalpyBurntKerosene(h_t5_actual);

        // 2. Look up η_t at the extraction fraction. With cp(T) routing
        //    on the hot side, J85 design-point extraction lands at
        //    ~280 kJ/kg per kg total. Set the η-curve abscissa nominal
        //    to that so design-point extraction = 1.0 = η peak.
        const double W_nominal_J_kg = 280_000.0;
        double extractionFraction = requiredSpecificWork_J_kg_total / W_nominal_J_kg;
        double eta_t = InterpAt(extractionFraction, _ExtractionFraction, _Eta_100pct);

        // 3. Pressure step: same isentropic-temperature relationship
        //    used by the constant-η stand-in, but applied with the
        //    actual ΔT derived from cp(T) above. Use γ_burnt ≈ 1.30
        //    for hot-side expansion (Mattingly App. B effective γ for
        //    kerosene burnt gas at high T).
        const double gamma_burnt = 1.30;
        double dT_actual = inletStagnationT_K - T_actual_outlet;
        double dT_isentropic = dT_actual / eta_t;
        double T_isentropic_outlet = inletStagnationT_K - dT_isentropic;
        if (T_isentropic_outlet <= 0.0)
            throw new InvalidOperationException(
                $"Turbine isentropic outlet T = {T_isentropic_outlet:F1} K (≤ 0) at η_t = {eta_t:F3}.");

        double P_outlet = inletStagnationP_Pa
            * Math.Pow(T_isentropic_outlet / inletStagnationT_K, gamma_burnt / (gamma_burnt - 1.0));

        // 4. Diagnostics: corrected mass flow at the operating point
        //    (turbine ṁ at design = compressor ṁ · (1+f) ≈ 20.3 kg/s
        //    at SLS / mil-power dry; corrected by inlet stagnation
        //    state). Turbines don't surge — sentinel +∞ for SurgeMargin.
        //
        //    ChokeMarginRel for turbine: fraction of W_max_safe (the
        //    point where the η curve falls off the cliff). Design sits
        //    at ~0.62 (well below 1.0); over-extraction past 1.0 fires
        //    CORRECTED_MASS_FLOW_OUT_OF_MAP via the gate evaluator.
        const double W_max_safe_J_kg = 450_000.0; // ~1.6× J85 design
        double mdotCorr_design_100pct = 20.0;
        double chokeMarginRel = requiredSpecificWork_J_kg_total / W_max_safe_J_kg;

        return new TurbinePoint(
            OutletStagnationT_K:                T_actual_outlet,
            OutletStagnationP_Pa:               P_outlet,
            IsentropicEfficiency:               eta_t,
            ExtractedSpecificWork_J_kg_total:   requiredSpecificWork_J_kg_total)
        {
            Diagnostics = new MapInfo(
                SurgeMargin:               double.PositiveInfinity,  // turbines don't surge
                CorrectedMassFlow_kg_s:    mdotCorr_design_100pct,
                ChokeMarginRel:            chokeMarginRel),
        };
    }

    // ------------------------------------------------------------------
    // Lookup helpers
    // ------------------------------------------------------------------

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
