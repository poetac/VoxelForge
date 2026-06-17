// LinkClosureMarginDistribution.cs — Sprint ANT.W5 statistical link-
// margin analysis over a rain-rate exceedance CDF.
//
// The core operation is: "what fraction of time is the link in outage
// due to rain?" expressed as P(margin < 0) = P(R > R_crit) where
// R_crit is the rain rate at which the link closure margin reaches zero.
//
// Rain-rate exceedance model (power-law fit to ITU-R P.837 mid-latitude
// climatology, zone K/L):
//
//   P(R > r) = P₀ · (R₀ / r)^b
//
//   P₀ = 1.0 × 10⁻⁴  (0.01 % = the user-supplied anchor probability)
//   R₀ = RainRate0p01pct_mmPerHr  (rain rate exceeded 0.01 % of time)
//   b  = 0.7  (ITU-R P.837 mid-latitude exponent)
//
// The critical rain rate R_crit is found by bisection on AntennaSolver:
//   M(R) = AntennaSolver.Solve(design with RainRate = R).LinkClosureMargin_dB
//   R_crit: M(R_crit) = 0
//
// For a clear-sky design (RainRate0p01pct_mmPerHr = 0) the function
// returns 0 immediately — no rain-statistics analysis is requested.
//
// References:
//   ITU-R P.837-7 (2017), Annex 1 §1 — rain-rate statistics.
//   Ippolito L.J. (2017). "Satellite Communications Systems Engineering,"
//     2nd ed., §4.2.3 (link availability from rain statistics).

using System;

namespace Voxelforge.Antenna;

/// <summary>
/// Sprint ANT.W5 — statistical link-closure-margin distribution over
/// the ITU-R P.837 power-law rain-rate exceedance model.
/// </summary>
internal static class LinkClosureMarginDistribution
{
    /// <summary>
    /// Anchor exceedance probability for the user-supplied rain rate:
    /// P(R &gt; R₀) = 1×10⁻⁴ (0.01 % of time = 87.6 hours/year).
    /// </summary>
    internal const double AnchorExceedanceProbability = 1e-4;

    /// <summary>
    /// ITU-R P.837 mid-latitude power-law exponent b in
    /// P(R &gt; r) = P₀ · (R₀/r)^b. Value 0.7 fits zone K/L (northern
    /// Europe, eastern USA, central Japan); tropical zones have b ≈ 0.5.
    /// </summary>
    internal const double ExceedanceExponent = 0.7;

    /// <summary>
    /// Compute the fraction of time the link closure margin falls below
    /// zero due to rain for <paramref name="design"/>.
    /// </summary>
    /// <param name="design">Validated antenna link design. If
    ///   <see cref="AntennaLinkDesign.RainRate0p01pct_mmPerHr"/> is 0
    ///   the method returns 0 immediately (no rain statistics).</param>
    /// <returns>P(margin &lt; 0) ∈ [0, 1]. 0 = always available;
    ///   1e-4 = unavailable 0.01 % of time (equivalent to 87.6 h/year
    ///   outage — ITU target for commercial satellite services).</returns>
    internal static double ComputeExceedanceProbability(AntennaLinkDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        double R0 = design.RainRate0p01pct_mmPerHr;
        if (R0 <= 0.0) return 0.0;

        double rCrit = FindCriticalRainRate(design, R0);

        if (rCrit <= 0.0) return 1.0;            // link fails even in clear sky
        if (rCrit >= R0)  return 0.0;            // link holds all the way to R₀

        // P(R > R_crit) = 1e-4 · (R₀ / R_crit)^0.7
        return AnchorExceedanceProbability
             * Math.Pow(R0 / rCrit, ExceedanceExponent);
    }

    // Find the rain rate at which link-closure margin = 0 by bisection.
    // Returns 0 if the link fails even at R = 0; R_max if it never fails.
    private static double FindCriticalRainRate(
        AntennaLinkDesign design,
        double R_max)
    {
        // Clear-sky margin (R = 0).
        double marginClearSky = AntennaSolver.Solve(
            design with { RainRate_mmPerHr = 0.0 }).LinkClosureMargin_dB;
        if (marginClearSky <= 0.0) return 0.0;

        // Margin at R = R_max (0.01 % rain rate).
        double marginAtMax = AntennaSolver.Solve(
            design with { RainRate_mmPerHr = R_max }).LinkClosureMargin_dB;
        if (marginAtMax >= 0.0) return R_max;

        // Bisect: margin(lo) > 0, margin(hi) ≤ 0.
        double lo = 0.0, hi = R_max;
        for (int i = 0; i < 52; i++)
        {
            double mid    = 0.5 * (lo + hi);
            double margin = AntennaSolver.Solve(
                design with { RainRate_mmPerHr = mid }).LinkClosureMargin_dB;
            if (margin > 0.0) lo = mid;
            else              hi = mid;
        }
        return 0.5 * (lo + hi);
    }
}
