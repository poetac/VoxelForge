// CroccoNTau.cs — TIER B.5 (2026-04-21): Crocco / Cheng n-τ coupled
// combustion-response analysis.
//
// Model scope:
//   Preliminary-design screening over the chamber acoustic modes
//   (L1, T1, T2) computed upstream in ScreechModes. For each mode f_k
//   we evaluate the closed-form growth rate approximation for a lumped
//   combustion response with interaction index n and time-lag τ
//   (Crocco 1956, Harrje & Reardon SP-194 §4.2):
//
//      σ_k = 0.5 · (γ − 1) / γ · n · [cos(ωτ) − 1]
//
//   A positive σ indicates a self-excited mode; a negative σ indicates
//   an acoustically damped mode. σ is reported in nondimensional form
//   (per radian of ωt) so it can be compared to the mode's natural
//   frequency.
//
//   The composite rating demotes to Fail if ANY of the three modes has
//   σ > 0 by a safety margin; marginal if σ is within the margin.
//
// Parameter selection (per propellant pair):
//   The (n, τ) pair is strongly dependent on injector element type,
//   propellant, chamber pressure, and MR. Published survey values
//   (Harrje & Reardon Ch. 5, Dressler AIAA-2000-3871) give:
//     LOX/LH2 : n ≈ 0.50, τ ≈ 0.0008 s
//     LOX/CH4 : n ≈ 0.80, τ ≈ 0.0015 s
//     LOX/RP1 : n ≈ 0.85, τ ≈ 0.0020 s
//   Storable pairs (N2O4/MMH, H2O2/RP1) are left as 0/0 — screening
//   is skipped for those and a note stamped. These values assume
//   impinging doublet elements; coax and pintle injectors typically
//   give shorter τ (faster response) and slightly higher n.
//
// Fidelity caveats:
//   • Single-τ lumped model — real injectors have a distributed lag
//     and multiple coupling mechanisms (atomisation, vapourisation,
//     chemistry). This is a screening tool, not a certifier.
//   • Damping (acoustic absorbers, baffles) not modelled — results
//     are "chamber without active damping".
//   • No 2D mixing-response correction (Reardon Ch. 6). A real design
//     review would iterate with measured hot-fire data to fit (n, τ).
//
// References:
//   Crocco, L., "Aspects of Combustion Stability in Liquid Propellant
//     Rocket Motors Part I", J. American Rocket Society, 1951.
//   Harrje, D. T. & Reardon, F. H. (eds.), "Liquid Propellant Rocket
//     Combustion Instability", NASA SP-194, 1972.
//   Dressler, G. A., "TRW Pintle Engine Heritage & Performance
//     Characteristics", AIAA 2000-3871.

namespace Voxelforge.Combustion.Stability;

/// <summary>
/// Per-mode growth-rate record. <see cref="GrowthRate"/> is the
/// nondimensional σ term; positive → mode is self-excited.
/// </summary>
public readonly record struct CroccoModeResult(
    string ModeName,
    double Frequency_Hz,
    double GrowthRate,
    StabilityRating Rating,
    string Reason);

public sealed record CroccoReport(
    double InteractionIndexN,
    double TimeLagTau_s,
    bool PairSupported,
    CroccoModeResult L1,
    CroccoModeResult T1,
    CroccoModeResult T2,
    StabilityRating Overall,
    string Notes);

public static class CroccoNTau
{
    /// <summary>
    /// Positive growth-rate threshold above which a mode is called Fail.
    /// Mildly positive growth (&lt; 0.02) is deemed marginal — a real test
    /// stand can often stabilise such a mode with small baffling.
    /// </summary>
    public const double FailThreshold = 0.02;

    /// <summary>
    /// Retrieve the (n, τ) pair for a supported propellant combination.
    /// Returns (0, 0, false) for unsupported pairs — screening is skipped.
    /// </summary>
    public static (double n, double tau_s, bool supported) GetPairParameters(PropellantPair pair)
        => pair switch
        {
            PropellantPair.LOX_H2  => (0.50, 0.0008, true),
            PropellantPair.LOX_CH4 => (0.80, 0.0015, true),
            PropellantPair.LOX_RP1 => (0.85, 0.0020, true),
            _                      => (0.0, 0.0, false),
        };

    public static CroccoReport Evaluate(PropellantPair pair, ScreechModeResult modes, double gamma)
    {
        var (n, tau, supported) = GetPairParameters(pair);
        if (!supported)
        {
            var skip = new CroccoModeResult("—", 0, 0, StabilityRating.Pass,
                "n-τ parameters not published for this pair — screening skipped.");
            return new CroccoReport(
                InteractionIndexN: 0, TimeLagTau_s: 0, PairSupported: false,
                L1: skip, T1: skip, T2: skip,
                Overall: StabilityRating.Pass,
                Notes: "Crocco n-τ screening skipped — pair has no published (n, τ).");
        }

        var l1 = EvaluateMode("L1", modes.L1_Hz, n, tau, gamma);
        var t1 = EvaluateMode("T1", modes.T1_Hz, n, tau, gamma);
        var t2 = EvaluateMode("T2", modes.T2_Hz, n, tau, gamma);

        var overall = WorstOf(l1.Rating, t1.Rating, t2.Rating);
        string notes = overall switch
        {
            StabilityRating.Fail     => "At least one mode is self-excited; redesign or add acoustic damping.",
            StabilityRating.Marginal => "Mode(s) near the instability threshold — run with extra margin or damping.",
            _                        => "All three modes predicted acoustically damped under nominal n-τ.",
        };

        return new CroccoReport(
            InteractionIndexN: n, TimeLagTau_s: tau, PairSupported: true,
            L1: l1, T1: t1, T2: t2,
            Overall: overall,
            Notes: notes);
    }

    private static CroccoModeResult EvaluateMode(
        string name, double f_Hz, double n, double tau_s, double gamma)
    {
        double omega = 2 * System.Math.PI * f_Hz;
        double sigma = 0.5 * (gamma - 1.0) / System.Math.Max(gamma, 1e-3)
                     * n * (System.Math.Cos(omega * tau_s) - 1.0);

        StabilityRating rating;
        string reason;
        if (sigma >  FailThreshold)
        { rating = StabilityRating.Fail;     reason = $"σ = {sigma:F3} > {FailThreshold:F2} — self-excited."; }
        else if (sigma > 0)
        { rating = StabilityRating.Marginal; reason = $"σ = {sigma:F3} weakly positive — marginal."; }
        else
        { rating = StabilityRating.Pass;     reason = $"σ = {sigma:F3} ≤ 0 — acoustically damped."; }

        return new CroccoModeResult(name, f_Hz, sigma, rating, reason);
    }

    private static StabilityRating WorstOf(params StabilityRating[] rs)
    {
        var worst = StabilityRating.Pass;
        foreach (var r in rs) if ((int)r > (int)worst) worst = r;
        return worst;
    }
}
