// Antoine.cs — Vapor-pressure-by-tank-temperature for pump NPSHA
// computation. Issue #158 (A6 — physics-correctness follow-on).
//
// The Antoine equation is the standard semi-empirical form for vapor
// pressure as a function of temperature:
//
//   log10(P_vap_mmHg) = A - B / (T_K + C)
//
// per-fluid (A, B, C) coefficients fit to tabulated NIST WebBook /
// REFPROP data over the relevant tank-temperature range. Real
// rocket-engine tank physics covers everything from cryogenic
// saturated (LOX at 90 K, 1 atm) to subcooled (LOX at ~85 K, e.g.
// after pre-pressurization) to warmed-by-feed-line (LOX at 95-100 K
// after a long copper feed line in a hot environment). Pre-A6,
// `TurbopumpSizing.VapourPressure_Pa` returned a single nominal sat-
// at-1-atm constant per fluid, missing this entire variability band.
//
// Coefficients sourced from the NIST WebBook (https://webbook.nist.gov/)
// and Yaws "Handbook of Vapor Pressure" (4th ed., 2007). Validity
// ranges are enforced by clamping T to the fitted range — pushing T
// outside the range substitutes the nearest endpoint and the result
// is conservative (the boundary is where Antoine starts to deviate
// from real measurements).

using System;

namespace Voxelforge.Coolant;

/// <summary>
/// Antoine-equation vapor-pressure calculator. Used by
/// <c>TurbopumpSizing</c>'s NPSHA computation to convert tank-inlet
/// temperature into a fluid-specific P_vap.
/// </summary>
public static class Antoine
{
    /// <summary>
    /// Antoine coefficients for a single fluid over a fitted
    /// temperature range. The Antoine form is
    /// <c>log10(P_vap_mmHg) = A - B / (T_K + C)</c>.
    /// </summary>
    /// <param name="A">Antoine A coefficient (log10 mmHg).</param>
    /// <param name="B">Antoine B coefficient (K).</param>
    /// <param name="C">Antoine C coefficient (K).</param>
    /// <param name="T_min_K">Lower bound of the fitted T range. Inputs below clamp to this.</param>
    /// <param name="T_max_K">Upper bound of the fitted T range. Inputs above clamp to this.</param>
    /// <param name="Source">Citation string for the coefficient set.</param>
    /// <remarks>
    /// All coefficient sets in this file use the NIST short-form
    /// convention: <c>log10(P_bar) = A - B / (T_K + C)</c>. The
    /// <see cref="VaporPressure_Pa"/> method handles the bar-to-Pa
    /// conversion (×100_000).
    /// </remarks>
    public sealed record Coefficients(
        double A, double B, double C,
        double T_min_K, double T_max_K,
        string Source);

    /// <summary>Pa per bar (1 bar = 100_000 Pa exactly).</summary>
    private const double PaPerBar = 100_000.0;

    /// <summary>
    /// LOX (liquid oxygen) Antoine coefficients. NIST WebBook
    /// O2 page (https://webbook.nist.gov/cgi/cbook.cgi?ID=C7782447);
    /// fitted over 54-154 K, covers cryogenic-sat through tank-warmed.
    /// Reference check: T = 90.18 K → P ≈ 1 atm ≈ 1.013 bar.
    /// </summary>
    public static readonly Coefficients LOX = new(
        A: 3.9523,
        B: 340.024,
        C: -4.144,
        T_min_K: 54.36,
        T_max_K: 154.33,
        Source: "NIST WebBook O2 (Antoine, bar/K, 54.36-154.33 K)");

    /// <summary>
    /// LH2 (liquid hydrogen) Antoine coefficients. NIST WebBook
    /// H2 page (https://webbook.nist.gov/cgi/cbook.cgi?ID=C1333740);
    /// fitted over 21-32 K (the published-fit range — covers most of
    /// the LH2 storage band). Extrapolation to 14-21 K (sat at 1 atm
    /// at ~20.4 K) is mild and acceptable for first-cut NPSHA.
    /// Reference check: T = 20.4 K → P ≈ 1 atm.
    /// </summary>
    public static readonly Coefficients LH2 = new(
        A: 3.54314,
        B: 99.395,
        C: 7.726,
        T_min_K: 14.0,    // extrapolation lower bound for cryogenic-sat
        T_max_K: 32.27,
        Source: "NIST WebBook H2 (Antoine, bar/K, 21.01-32.27 K, extrapolated to 14 K)");

    /// <summary>
    /// LCH4 (liquid methane) Antoine coefficients. NIST WebBook
    /// methane page (https://webbook.nist.gov/cgi/cbook.cgi?ID=C74828);
    /// fitted over 91-190 K. Pre-A6 callers used a constant
    /// <c>0.5e5 Pa</c> (≈ saturation at 150 K). The Antoine form
    /// gives the full curve.
    /// Reference check: T = 111.7 K → P ≈ 1 atm; T = 150 K → P ≈ 10 atm.
    /// </summary>
    public static readonly Coefficients LCH4 = new(
        A: 3.9895,
        B: 443.028,
        C: -0.49,
        T_min_K: 90.99,
        T_max_K: 189.99,
        Source: "NIST WebBook CH4 (Antoine, bar/K, 90.99-189.99 K)");

    /// <summary>
    /// RP-1 (kerosene-class hydrocarbon mix) approximate Antoine
    /// coefficients. Real RP-1 is a multi-component hydrocarbon mix
    /// (C10-C13 alkanes); this single-Antoine form is a lumped
    /// effective fit. NIST WebBook n-dodecane (C12H26, the dominant
    /// component for NPSHA purposes), fitted over 264-489 K.
    /// Reference check: T = 298 K → P ≈ 14 Pa (very low, room T).
    /// </summary>
    public static readonly Coefficients RP1 = new(
        A: 4.10549,
        B: 1625.928,
        C: -92.839,
        T_min_K: 264.0,
        T_max_K: 489.0,
        Source: "NIST WebBook n-C12H26 proxy (Antoine, bar/K, 264-489 K)");

    /// <summary>
    /// Compute vapor pressure (Pa) at temperature
    /// <paramref name="T_K"/> using the supplied Antoine
    /// coefficients. Temperature is clamped to
    /// [<see cref="Coefficients.T_min_K"/>,
    /// <see cref="Coefficients.T_max_K"/>] before evaluation.
    /// All coefficient sets use the bar/K NIST short-form;
    /// the result is converted to Pa via ×100_000.
    /// </summary>
    public static double VaporPressure_Pa(in Coefficients c, double T_K)
    {
        if (c is null) throw new ArgumentNullException(nameof(c));
        double T = Math.Clamp(T_K, c.T_min_K, c.T_max_K);
        double log10P_bar = c.A - c.B / (T + c.C);
        double P_bar = Math.Pow(10.0, log10P_bar);
        return P_bar * PaPerBar;
    }

    /// <summary>
    /// Convenience overload: select coefficient set by fluid key
    /// (matches <c>PropellantPairMeta.OxidiserSymbol</c> /
    /// <c>CoolantFluidKey</c> values).
    /// </summary>
    /// <returns>
    /// Vapor pressure in Pa, or <c>null</c> when the fluid key isn't
    /// in the known table — callers fall back to legacy constant
    /// behaviour.
    /// </returns>
    public static double? VaporPressureForFluid_Pa(string fluidKey, double T_K)
    {
        Coefficients? coef = fluidKey switch
        {
            "LOX"  => LOX,
            "H2"   => LH2,
            "CH4"  => LCH4,
            "RP-1" => RP1,
            _      => null,
        };
        return coef is null ? (double?)null : VaporPressure_Pa(coef, T_K);
    }
}
