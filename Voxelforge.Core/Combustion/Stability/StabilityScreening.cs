// StabilityScreening.cs — Composite combustion stability screening.
//
// Wraps chug + screech-mode evaluation into a single report with a
// Priem-Guentert-style traffic-light composite rating. Consumed by
// the optimizer (Evaluate), the UI (three pills), and the report
// exporter.
//
// Priem-Guentert traffic light (preliminary design):
//
//   Green   = chug passes AND screech modes have no classical
//             red-flag condition (no T1 in the well-known 1–4 kHz
//             high-risk band AND T1 not extremely close to L1).
//   Yellow  = chug marginal OR T1 in 1–4 kHz risk band (but chug OK).
//   Red     = chug fails OR L1 within ±10 % of T1 (mode-overlap risk).
//
// STOP short of Crocco n-τ (per project scope). The rating is an
// empirical rule-of-thumb composite, not a closed-loop stability
// analysis.
//
// References:
//   Priem & Guentert, "Combustion Instability Limits Determined by a
//     Nonlinear Theory and a One-Dimensional Concentrated Combustion
//     Model", NASA TN D-1409 (1962).
//   Harrje & Reardon (eds), "Liquid Propellant Rocket Combustion
//     Instability", NASA SP-194 (1972), Ch. 3, 6.

using Voxelforge.Chamber;

namespace Voxelforge.Combustion.Stability;

/// <summary>
/// Full stability-screening report for one design point. Emitted as a
/// field on <see cref="Optimization.RegenGenerationResult"/>.
/// </summary>
public sealed record StabilityReport(
    InjectorState Injector,
    ChugResult Chug,
    ScreechModeResult Screech,
    StabilityRating Composite,
    string CompositeReason,
    string[] Notes,
    // TIER B.5 (2026-04-21): coupled Crocco n-τ growth-rate per mode.
    // Null when the propellant pair has no published (n, τ) parameters.
    CroccoReport? Crocco = null,
    // OOB-6 / Sprint B-3 (2026-04-30): closed-form acoustic-damper
    // resonance + damping-ratio Δζ per chamber mode. Null when no
    // damper is configured on the design (DamperType = None or zero
    // count). When non-null, downstream gating turns this into the
    // ACOUSTIC_DAMPER_DETUNED + ACOUSTIC_DAMPER_OVERSIZED advisory
    // gates — never a Hard fail (model is empirical).
    AcousticDamperResult? AcousticDamper = null)
{
    /// <summary>
    /// Human-readable summary line, e.g.
    /// "PASS — ΔP/Pc 20.0 %, L1 9710 Hz, T1 17230 Hz, T2 28570 Hz".
    /// </summary>
    public string OneLineSummary =>
        $"{RatingWord(Composite)} — ΔP/P_c {Chug.DeltaPRatio:P1}, " +
        $"L1 {Screech.L1_Hz:F0} Hz, T1 {Screech.T1_Hz:F0} Hz, T2 {Screech.T2_Hz:F0} Hz";

    public static string RatingWord(StabilityRating r) => r switch
    {
        StabilityRating.Pass     => "PASS",
        StabilityRating.Marginal => "MARGINAL",
        StabilityRating.Fail     => "FAIL",
        _ => "UNKNOWN",
    };
}

public static class StabilityScreening
{
    /// <summary>Lower bound of the classical high-risk screech band (Hz).</summary>
    public const double ScreechRiskBand_LowerHz = 1000.0;
    /// <summary>Upper bound of the classical high-risk screech band (Hz).</summary>
    public const double ScreechRiskBand_UpperHz = 4000.0;

    /// <summary>
    /// If L1 and T1 are within this fractional tolerance of each other,
    /// the mode-overlap risk is flagged RED. Classic design-avoid zone.
    /// </summary>
    public const double ModeOverlapTolerance = 0.10;

    /// <summary>
    /// Evaluate a full stability screening for a generated contour and
    /// gas state. <paramref name="injector"/> defaults to nominal (20 %
    /// ΔP/P_c) if null; pass a specific value to inspect other margins.
    /// </summary>
    public static StabilityReport Evaluate(
        ChamberContour contour,
        PropellantState gas,
        double chamberPressure_Pa,
        InjectorState? injector = null,
        PropellantPair? propellantPair = null,
        AcousticDamperConfig? damperConfig = null)
    {
        var inj = injector ?? InjectorState.Nominal(chamberPressure_Pa);

        var chug = ChugAnalysis.Evaluate(inj, chamberPressure_Pa);

        // Barrel diameter (chamber section, not throat). Length L_c is
        // the injector-to-converging-entrance length per ChamberContour.
        double D_c_m = 2.0 * contour.ChamberRadius_mm * 1e-3;
        double L_c_m = contour.ChamberLength_mm * 1e-3;

        var screech = ScreechModes.Evaluate(
            gas.Gamma, gas.SpecificGasConst, gas.ChamberTemp_K,
            L_c_m, D_c_m);

        var notes = new System.Collections.Generic.List<string>();

        // Composite rating: start optimistic, demote on problems.
        var composite = StabilityRating.Pass;
        string reason = "All screening checks passed.";

        bool t1InRisk = screech.T1_Hz >= ScreechRiskBand_LowerHz
                     && screech.T1_Hz <= ScreechRiskBand_UpperHz;
        if (t1InRisk)
            notes.Add($"T1 = {screech.T1_Hz:F0} Hz falls in classical 1–4 kHz high-risk band.");

        double overlapFrac = System.Math.Abs(screech.L1_Hz - screech.T1_Hz)
                           / System.Math.Max(screech.T1_Hz, 1);
        bool modeOverlap = overlapFrac < ModeOverlapTolerance;
        if (modeOverlap)
            notes.Add($"L1 ({screech.L1_Hz:F0} Hz) and T1 ({screech.T1_Hz:F0} Hz) " +
                      $"within {ModeOverlapTolerance:P0} — mode-overlap risk.");

        if (chug.Rating == StabilityRating.Fail || modeOverlap)
        {
            composite = StabilityRating.Fail;
            reason = chug.Rating == StabilityRating.Fail
                ? $"Chug FAIL: {chug.Reason}"
                : "L1/T1 mode-overlap risk.";
        }
        else if (chug.Rating == StabilityRating.Marginal || t1InRisk)
        {
            composite = StabilityRating.Marginal;
            reason = chug.Rating == StabilityRating.Marginal
                ? $"Chug marginal: {chug.Reason}"
                : "T1 in 1–4 kHz band — inspect combustion response.";
        }

        // TIER B.5: if the caller supplied a PropellantPair, run the Crocco
        // n-τ growth-rate screening for the three chamber modes. A Fail
        // rating here demotes the composite to Fail; Marginal demotes only
        // if the composite was still Pass.
        CroccoReport? crocco = null;
        if (propellantPair is { } pair)
        {
            crocco = CroccoNTau.Evaluate(pair, screech, gas.Gamma);
            if (crocco.PairSupported)
            {
                if (crocco.Overall == StabilityRating.Fail)
                {
                    composite = StabilityRating.Fail;
                    reason = $"Crocco n-τ: mode(s) self-excited ({crocco.Notes}).";
                }
                else if (crocco.Overall == StabilityRating.Marginal
                      && composite == StabilityRating.Pass)
                {
                    composite = StabilityRating.Marginal;
                    reason = $"Crocco n-τ: mode near instability threshold.";
                }
                notes.Add($"Crocco n-τ (n={crocco.InteractionIndexN:F2}, τ={crocco.TimeLagTau_s * 1e3:F2} ms): {crocco.Notes}");
            }
        }

        if (crocco is null || !crocco.PairSupported)
            notes.Add("No Crocco n-τ coupling evaluated — (n, τ) not published for this pair.");

        // OOB-6 (#200): evaluate the damper if configured. Null = no damper
        // on this design; the Notes line surfaces the closest mode + Δζ for
        // the build-sheet section + advisory gates.
        AcousticDamperResult? damper = null;
        if (damperConfig is not null && damperConfig.IsActive)
        {
            damper = AcousticDamper.Evaluate(damperConfig, screech);
            if (damper is not null)
            {
                notes.Add($"Acoustic damper: {damper.Notes} Δζ(L1,T1,T2) = "
                        + $"({damper.DampingRatio_L1:F3}, {damper.DampingRatio_T1:F3}, "
                        + $"{damper.DampingRatio_T2:F3}).");
            }
        }

        return new StabilityReport(
            Injector: inj,
            Chug: chug,
            Screech: screech,
            Composite: composite,
            CompositeReason: reason,
            Notes: notes.ToArray(),
            Crocco: crocco,
            AcousticDamper: damper);
    }
}
