// ChugAnalysis.cs — Low-frequency (feed-system-coupled) stability screening.
//
// Chug is a Helmholtz-type low-frequency (~50–500 Hz) oscillation where
// feed-system capacitance couples to the chamber through the injector.
// The classical screening criterion is the injector pressure-drop ratio:
//
//     ΔP_inj / P_c   ≥ 0.15   (below this: chug-prone)
//                    ≤ 0.25   (above this: wasted tank pressure)
//
// These are the widely-cited Huzel & Huang / Sutton bounds. Real flight
// hardware needs a coupled feed-system model for confirmation; this
// check flags designs that fail the necessary condition.
//
// Preliminary-design fidelity: pass/fail against the rule-of-thumb band
// only. Not a replacement for a feed-system model.

namespace Voxelforge.Combustion.Stability;

/// <summary>Traffic-light rating for any stability sub-check.</summary>
public enum StabilityRating
{
    /// <summary>Design is inside the classical feasible band.</summary>
    Pass,
    /// <summary>Design is close to a boundary (±20 % of band width) — inspect.</summary>
    Marginal,
    /// <summary>Design fails the rule-of-thumb check — redesign required.</summary>
    Fail,
}

/// <summary>
/// Chug (low-frequency, feed-system-coupled) stability screening result.
/// <para>
/// Carries per-side ratings for ox/fuel ΔP. When the input
/// <see cref="InjectorState"/> only carries the aggregate
/// <c>DeltaPInj_Pa</c>, the per-side fields mirror the aggregate.
/// When the user supplies per-side drops, the per-side fields hold
/// their own ratings and <see cref="Rating"/> (the headline composite)
/// is the WORSE of the two.
/// </para>
/// </summary>
public readonly record struct ChugResult(
    double DeltaPRatio,              // aggregate ΔP_inj / P_c (or worst side)
    double LowerBand,                // 0.15 by convention
    double UpperBand,                // 0.25 by convention
    StabilityRating Rating,          // headline composite (worse of ox / fuel)
    string Reason,
    // Per-side ratings + ratios. When the user did not supply
    // per-side ΔPs, both default to the aggregate values so existing
    // consumers see consistent readouts.
    double          OxDeltaPRatio   = 0.0,
    double          FuelDeltaPRatio = 0.0,
    StabilityRating OxRating        = StabilityRating.Pass,
    StabilityRating FuelRating      = StabilityRating.Pass);

public static class ChugAnalysis
{
    /// <summary>Lower feasibility bound on ΔP_inj / P_c (Huzel & Huang §8.3).</summary>
    public const double LowerBand = 0.15;
    /// <summary>Upper "wasted pressure" bound on ΔP_inj / P_c.</summary>
    public const double UpperBand = 0.25;

    /// <summary>
    /// Fraction of the band width used to define the "Marginal" zone
    /// just outside [LowerBand, UpperBand]. 0.20 → Marginal if ratio is
    /// within 20 % of the band width from a boundary.
    /// </summary>
    public const double MarginalFraction = 0.20;

    public static ChugResult Evaluate(InjectorState inj, double chamberPressure_Pa)
    {
        // When the injector state carries per-side drops, screen each
        // side independently and return the worse rating in the
        // headline `Rating` field. Otherwise fall back to single-channel
        // (legacy back-compat with callers that pass `new InjectorState(dP)`).
        double rAgg = inj.RatioToChamberPressure(chamberPressure_Pa);
        var (aggRating, aggReason) = ScreenRatio(rAgg, sideLabel: "");

        if (!inj.HasSideDrops)
        {
            return new ChugResult(
                DeltaPRatio: rAgg, LowerBand: LowerBand, UpperBand: UpperBand,
                Rating: aggRating, Reason: aggReason,
                OxDeltaPRatio: rAgg, FuelDeltaPRatio: rAgg,
                OxRating: aggRating, FuelRating: aggRating);
        }

        double rOx   = inj.OxDeltaPInj_Pa   / System.Math.Max(chamberPressure_Pa, 1.0);
        double rFuel = inj.FuelDeltaPInj_Pa / System.Math.Max(chamberPressure_Pa, 1.0);
        var (oxR,   oxReason)   = ScreenRatio(rOx,   sideLabel: "Ox ");
        var (fuelR, fuelReason) = ScreenRatio(rFuel, sideLabel: "Fuel ");

        // Headline = worse of the two; reason names whichever side drove it.
        StabilityRating worst = (StabilityRating)System.Math.Max((int)oxR, (int)fuelR);
        string worstReason = oxR >= fuelR ? oxReason : fuelReason;

        return new ChugResult(
            DeltaPRatio: System.Math.Min(rOx, rFuel),     // worst-case ratio shown in headline numeric
            LowerBand: LowerBand, UpperBand: UpperBand,
            Rating: worst, Reason: worstReason,
            OxDeltaPRatio: rOx, FuelDeltaPRatio: rFuel,
            OxRating: oxR,      FuelRating: fuelR);
    }

    private static (StabilityRating rating, string reason) ScreenRatio(double r, string sideLabel)
    {
        double bandWidth = UpperBand - LowerBand;
        double marginPad = bandWidth * MarginalFraction;

        if (r >= LowerBand && r <= UpperBand)
            return (StabilityRating.Pass,
                    $"{sideLabel}ΔP/P_c = {r:P1} is inside [{LowerBand:P0}, {UpperBand:P0}].");

        if (r >= LowerBand - marginPad && r <= UpperBand + marginPad)
            return (StabilityRating.Marginal, r < LowerBand
                ? $"{sideLabel}ΔP/P_c = {r:P1} is below {LowerBand:P0} but within marginal band — chug risk."
                : $"{sideLabel}ΔP/P_c = {r:P1} is above {UpperBand:P0} but within marginal band — tank P wasted.");

        return (StabilityRating.Fail, r < LowerBand
            ? $"{sideLabel}ΔP/P_c = {r:P1} < {LowerBand:P0} — chug-prone injector."
            : $"{sideLabel}ΔP/P_c = {r:P1} > {UpperBand:P0} — tank pressure wasted without stability benefit.");
    }
}
