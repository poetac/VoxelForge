// Sprint 34c (2026-04-25) / PH-8 companion — Stepanoff η correlation.
//
// Replaces the constant 0.65 efficiency assumption in TurbopumpSizing
// with an N_s-Q lookup. Real centrifugal LRE pumps see η = 0.40-0.85
// depending on specific speed and volumetric flow rate; the constant
// flattered shaft-power numbers on tiny (sub-10 kN) and large
// (multi-MN) thrust classes by ±20 %.
//
// Sources:
//   Stepanoff, A. J. "Centrifugal and Axial Flow Pumps" 2e (1957) §2.7
//     fig. 2.18 — η vs N_s curves at various Q. Anchor data tabulated below.
//   Karassik, I. J. "Pump Handbook" 4e (2008) §2.5 — same η-vs-N_s
//     family with sharper Q correction at small flows.
//   Sutton & Biblarz "Rocket Propulsion Elements" 9e (2017) §10.4 —
//     LRE-specific efficiency band 0.50-0.78 (single-stage centrifugal).
//
// Curve shape: η peaks near N_s ≈ 2700 (US units: rpm·√gpm/ft^0.75),
// rolls off below 1500 (low-N_s = paddle-ish radial impellers, friction
// losses dominate) and above 5000 (high-N_s = mixed/axial, recirculation
// losses dominate). Q correction subtracts ~3-5 % for small pumps
// (< 100 gpm, friction-dominated) and adds ~1-2 % for large pumps
// (> 500 gpm). Bounds clamp η ∈ [0.30, 0.92].

using System;

namespace Voxelforge.FeedSystem;

/// <summary>
/// Stepanoff η-vs-N_s correlation for centrifugal LRE turbopumps. Replaces
/// the pre-Sprint-34c constant <see cref="TurbopumpSizing.DefaultPumpEfficiency"/>
/// with a lookup that responds to specific speed and volumetric flow rate.
/// </summary>
public static class PumpEfficiencyCorrelation
{
    // Anchor points — (N_s_US, η_peak at typical Q ≈ 200 gpm).
    // Extracted from Stepanoff §2.7 fig. 2.18 mid-Q curve and cross-
    // checked against Karassik §2.5 fig. 2.18.
    private static readonly (double Ns, double EtaPeak)[] s_curve =
    {
        ( 600.0, 0.55),
        (1000.0, 0.68),
        (1500.0, 0.78),
        (2000.0, 0.83),
        (2700.0, 0.85),  // peak
        (4000.0, 0.84),
        (6000.0, 0.80),
        (9000.0, 0.72),
    };

    /// <summary>
    /// Returns η for a centrifugal LRE pump at the given specific speed
    /// (US units) and volumetric flow (US gpm). Falls back to the legacy
    /// constant <see cref="TurbopumpSizing.DefaultPumpEfficiency"/> when
    /// inputs are non-positive (degenerate sizing case).
    /// </summary>
    /// <param name="specificSpeed_US">N_s in US units: rpm·√gpm/ft^0.75. Clamped to [600, 9000].</param>
    /// <param name="flowRate_gpm">Volumetric flow rate in US gpm. Drives the small-pump friction correction.</param>
    public static double Efficiency(double specificSpeed_US, double flowRate_gpm)
    {
        if (specificSpeed_US <= 0 || flowRate_gpm <= 0)
        {
            return TurbopumpSizing.DefaultPumpEfficiency;
        }

        double Ns = Math.Clamp(specificSpeed_US, s_curve[0].Ns, s_curve[^1].Ns);
        double etaPeak = InterpolateLogNs(Ns);
        double qCorrection = QCorrection(flowRate_gpm);
        return Math.Clamp(etaPeak + qCorrection, 0.30, 0.92);
    }

    private static double InterpolateLogNs(double Ns)
    {
        double logNs = Math.Log10(Ns);
        for (int i = 0; i < s_curve.Length - 1; i++)
        {
            double logLo = Math.Log10(s_curve[i].Ns);
            double logHi = Math.Log10(s_curve[i + 1].Ns);
            if (logNs <= logHi)
            {
                double frac = (logNs - logLo) / (logHi - logLo);
                return s_curve[i].EtaPeak + frac * (s_curve[i + 1].EtaPeak - s_curve[i].EtaPeak);
            }
        }
        return s_curve[^1].EtaPeak;
    }

    // Small pumps (< 100 gpm) lose efficiency to viscous / friction
    // losses scaling poorly with size; large pumps (> 500 gpm) gain a
    // small bump. Curve fit from Karassik §2.5: linear in log-Q,
    // anchored at 200 gpm = 0 correction.
    private static double QCorrection(double Q_gpm)
    {
        const double Q_anchor_gpm = 200.0;
        const double slope_per_decade = 0.040;  // ±4 % per decade of Q
        double logRatio = Math.Log10(Math.Max(Q_gpm, 1.0) / Q_anchor_gpm);
        return slope_per_decade * logRatio;
    }
}
