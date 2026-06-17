// InjectorState.cs — Minimum injector-side state needed for chug screening.
//
// Preliminary-design fidelity: this is the small set of injector-side
// parameters that feed the chug-mode (low-frequency feed-system coupling)
// stability check. Richer injector-element geometry lives in
// Injector.Elements — Stability only needs the aggregate injector
// pressure drop.
//
// References:
//   Huzel & Huang, Modern Engineering for Design of Liquid-Propellant
//     Rocket Engines, AIAA Progress Series Vol. 147, Ch. 8
//     ("Combustion-Devices Design"), §8.3 on chug stability margin.
//   Sutton & Biblarz, Rocket Propulsion Elements, 9th ed., §9.4
//     ("Combustion Instability").

namespace Voxelforge.Combustion.Stability;

/// <summary>
/// Aggregate injector-side state used by chug stability screening.
/// <para>
/// <b>ΔP_inj / P_c</b> is the single most-cited feed-system-coupling
/// metric for low-frequency ("chug") instability. Standard design rule
/// of thumb: keep the ratio in [0.15, 0.25]. Below 0.15 the injector
/// fails to decouple the feed system from chamber oscillations; above
/// 0.25 wastes tank pressure with no stability benefit.
/// </para>
/// <para>
/// <see cref="OxDeltaPInj_Pa"/> + <see cref="FuelDeltaPInj_Pa"/> are
/// optional; when both default to 0 the chug check uses the aggregate
/// <see cref="DeltaPInj_Pa"/> for both sides (legacy back-compat).
/// When either is non-zero, <see cref="ChugAnalysis.Evaluate"/>
/// screens each side independently and returns the WORSE rating in
/// <see cref="ChugResult.Rating"/> while exposing per-side ratings
/// in the new <see cref="ChugResult.OxRating"/> /
/// <see cref="ChugResult.FuelRating"/> fields. Flight hardware needs
/// both sides screened independently; this gives the user a way to
/// model an asymmetric injector explicitly.
/// </para>
/// </summary>
public readonly record struct InjectorState(
    double DeltaPInj_Pa,
    double OxDeltaPInj_Pa   = 0.0,
    double FuelDeltaPInj_Pa = 0.0)
{
    /// <summary>
    /// Standard design convention: start with ΔP_inj = 20% of P_c
    /// (the middle of the classical feasible band). Use this whenever
    /// a specific injector drop has not yet been chosen.
    /// </summary>
    public static InjectorState Nominal(double chamberPressure_Pa)
        => new(DeltaPInj_Pa: 0.20 * chamberPressure_Pa);

    /// <summary>
    /// Compute the chug-margin ratio ΔP_inj / P_c for a given chamber
    /// pressure. Guards against divide-by-zero but does NOT clamp
    /// (callers care about out-of-band conditions).
    /// </summary>
    public double RatioToChamberPressure(double chamberPressure_Pa)
        => DeltaPInj_Pa / System.Math.Max(chamberPressure_Pa, 1.0);

    /// <summary>True iff the user supplied per-side drops separate from the aggregate.</summary>
    public bool HasSideDrops => OxDeltaPInj_Pa > 0 || FuelDeltaPInj_Pa > 0;
}
