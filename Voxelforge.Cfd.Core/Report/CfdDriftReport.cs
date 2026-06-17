// CfdDriftReport.cs — Markdown drift report comparing SU2 CFD wall temperature
// to Bartz prediction and summarising the BartzScalingFactor MAP calibration.

using System.Globalization;
using Voxelforge.Analysis;
using Voxelforge.Cfd.Config;
using Voxelforge.Cfd.Parser;

namespace Voxelforge.Cfd.Report;

/// <summary>
/// Generates a Markdown calibration drift report from a <see cref="CfdCalibrationResult"/>.
/// </summary>
public static class CfdDriftReport
{
    /// <summary>
    /// Builds a Markdown report summarising the SU2 T_aw vs Bartz comparison and the
    /// BartzScalingFactor MAP calibration result.
    /// </summary>
    /// <param name="wallProfile">SU2 surface temperature profile.</param>
    /// <param name="calibration">Multi-knob MAP calibration result.</param>
    /// <param name="bartzPeakAdiabaticWallTemp_K">
    /// Bartz-predicted peak wall temperature (K) for drift comparison.
    /// Pass <see cref="double.NaN"/> when unavailable.
    /// </param>
    /// <param name="cpModel">
    /// (Issue #455) Cp(T) model selected via <see cref="CfdCalibrationInputs.CpModel"/>.
    /// Default <see cref="CpModel.PolynomialFit"/> activates γ_eff when the polynomial fit
    /// is non-flat; <see cref="CpModel.FrozenGamma"/> reverts to Sprint C.2 frozen-γ.
    /// </param>
    /// <param name="gammaUsed">
    /// (Issue #455) The actual γ value SU2 saw as <c>GAMMA_VALUE</c> — equal to γ_eff when
    /// the polynomial path activated, otherwise the frozen GammaChamber. Pass NaN when
    /// unavailable; the row renders as "N/A".
    /// </param>
    /// <param name="isFlatCp">
    /// (Issue #455) True when <see cref="CpPolynomialResult.IsFlatCp"/> short-circuited the
    /// polynomial path back to frozen γ (typical for frozen-flow tables where
    /// GammaThroat ≈ GammaChamber). Surfaced so consumers can distinguish "FrozenGamma by
    /// configuration" from "FrozenGamma by polynomial degeneracy".
    /// </param>
    /// <param name="configProvenance">
    /// (Issues #480, #485) Per-pair Sutherland-S + μ_ref provenance from
    /// <see cref="Su2ConfigWriter.Write"/>. Default value renders the Sprint C.2
    /// Bartz-slope / CeaTable2DBase-formula fallback labels (preserves the
    /// pre-Sprint-C.2-follow-on report shape for callers that don't pass it).
    /// </param>
    /// <returns>Markdown string.</returns>
    public static string BuildMarkdown(
        Su2WallProfile wallProfile,
        MultiKnobCalibrationResult calibration,
        double bartzPeakAdiabaticWallTemp_K,
        CpModel cpModel = CpModel.PolynomialFit,
        double gammaUsed = double.NaN,
        bool isFlatCp = false,
        Su2ConfigProvenance configProvenance = default)
    {
        ArgumentNullException.ThrowIfNull(wallProfile);
        ArgumentNullException.ThrowIfNull(calibration);

        var ci = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# CFD Validation Drift Report — Sprint C.3");
        sb.AppendLine();

        // ── Section 1: T_aw comparison ────────────────────────────────────────
        sb.AppendLine("## Wall Temperature Comparison (SU2 T_aw vs Bartz)");
        sb.AppendLine();
        sb.AppendLine("| Quantity | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| SU2 peak T_aw (K) | {FormatK(wallProfile.PeakAdiabaticWallTemp_K, ci)} |");
        sb.AppendLine($"| Bartz peak T (K) | {FormatK(bartzPeakAdiabaticWallTemp_K, ci)} |");

        if (!double.IsNaN(wallProfile.PeakAdiabaticWallTemp_K)
            && !double.IsNaN(bartzPeakAdiabaticWallTemp_K)
            && bartzPeakAdiabaticWallTemp_K > 0)
        {
            double driftK   = wallProfile.PeakAdiabaticWallTemp_K - bartzPeakAdiabaticWallTemp_K;
            double driftPct = 100.0 * driftK / bartzPeakAdiabaticWallTemp_K;
            sb.AppendLine($"| Drift (K) | {driftK:+0.0;-0.0;0.0} K |");
            sb.AppendLine($"| Drift (%) | {driftPct:+0.0;-0.0;0.0} % |");
            sb.AppendLine($"| Within ±20% acceptance | {(Math.Abs(driftPct) <= 20.0 ? "Yes ✓" : "No ✗")} |");
        }

        sb.AppendLine($"| SU2 converged | {(wallProfile.Converged ? "Yes" : "No")} |");
        sb.AppendLine($"| Wall nodes parsed | {wallProfile.NodeCount} |");
        sb.AppendLine($"| Station-wise T_aw map size | {wallProfile.AdiabaticWallTempByStation.Count} |");
        sb.AppendLine();

        // ── Section 2: BartzScalingFactor MAP ─────────────────────────────────
        sb.AppendLine("## BartzScalingFactor Calibration (MAP Estimate)");
        sb.AppendLine();
        var bartz = calibration.BartzScalingFactor;
        sb.AppendLine("| Property | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| MAP estimate | {bartz.MapValue.ToString("F4", ci)} |");
        sb.AppendLine($"| Prior mean | {bartz.PriorMean.ToString("F4", ci)} |");
        sb.AppendLine($"| Prior σ | {bartz.PriorSigma.ToString("F4", ci)} |");
        sb.AppendLine($"| ∂²obj/∂θ² at MAP (curvature) | {bartz.SsrCurvature.ToString("G4", ci)} |");
        sb.AppendLine($"| Interpretation | {bartz.Interpretation} |");
        sb.AppendLine();

        // ── Section 3: Calibration convergence ────────────────────────────────
        sb.AppendLine("## Calibration Convergence");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| SSR at prior | {calibration.SsrAtPrior.ToString("G4", ci)} |");
        sb.AppendLine($"| SSR at MAP | {calibration.SsrAtMap.ToString("G4", ci)} |");
        sb.AppendLine($"| Outer iterations used | {calibration.IterationsUsed} |");
        sb.AppendLine();

        if (calibration.Notes.Length > 0)
        {
            sb.AppendLine("**Notes:**");
            foreach (string note in calibration.Notes)
                sb.AppendLine($"- {note}");
            sb.AppendLine();
        }

        // ── Section 3.5: Gas model (issue #455) ───────────────────────────────
        // Surfaces which γ SU2 actually saw and whether the C.3 polynomial Cp(T)
        // path was active. Distinguishes FrozenGamma-by-configuration from
        // FrozenGamma-by-polynomial-degeneracy (IsFlatCp).
        sb.AppendLine("## Gas Model");
        sb.AppendLine();
        sb.AppendLine("| Property | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Cp model (configured) | {cpModel} |");
        sb.AppendLine($"| γ used (SU2 GAMMA_VALUE) | {FormatGamma(gammaUsed, ci)} |");
        sb.AppendLine($"| γ source | {GammaSourceLabel(cpModel, isFlatCp)} |");
        sb.AppendLine($"| Polynomial Cp(T) flat-fallback | {(isFlatCp ? "Yes" : "No")} |");
        // Issues #480, #485: per-pair Sutherland-S + μ_ref provenance.
        sb.AppendLine($"| Sutherland S [K] | {FormatSutherlandS(configProvenance.SutherlandS_K, ci)} |");
        sb.AppendLine($"| Sutherland source | {SutherlandSourceLabel(configProvenance.SutherlandSource, configProvenance.SutherlandPairLabel)} |");
        sb.AppendLine($"| μ_ref [Pa·s] | {FormatMuRef(configProvenance.MuRef_PaS, ci)} |");
        sb.AppendLine($"| Viscosity reference source | {MuRefSourceLabel(configProvenance.MuRefSource, configProvenance.MuRefPairLabel)} |");
        sb.AppendLine();

        // ── Section 4: Provenance + remaining limitations ─────────────────────
        sb.AppendLine("## Provenance");
        sb.AppendLine();
        sb.AppendLine(
            "- **Direct T_aw comparison (Sprint C.2):** SU2's adiabatic surface T (MARKER_HEATFLUX=0) " +
            "is compared like-for-like against `RegenSolverOutputs.PeakAdiabaticWallTemp_K`, " +
            "computed from the same Mach + γ + Pr basis inside `RegenCoolingSolver`.");
        sb.AppendLine(
            "- **Sutherland S + μ_ref (Sprint C.2 + follow-on):** when a `PropellantPair` is " +
            "supplied via `CfdCalibrationInputs.Pair`, both values are sourced per-pair from " +
            "`SutherlandFromCea` / `MuRefFromCea` (CEA mass-fraction-blended Sutherland fits, " +
            "issues #480 / #485). When null, falls back to `Su2ConfigWriter.SutherlandConstantFromBartzSlope` " +
            "(S = T_chamber / 9) and `gas.Viscosity_PaS` (CeaTable2DBase per-temperature formula).");
        sb.AppendLine(
            "- **Polynomial Cp(T) γ_eff (Sprint C.3):** `CpPolynomialFitter.Fit` derives a " +
            "degree-4 Cp(T) fit from chamber and throat anchor points and computes a " +
            "temperature-averaged γ_eff = Cp_mean / (Cp_mean − R), replacing the frozen " +
            "chamber γ as SU2 GAMMA_VALUE. `ThroatGammaComputer.WithThroatGamma` re-queries " +
            "`PropellantTables` at P* to supply a distinct GammaThroat. Toggle via " +
            "`CpModel.FrozenGamma` to revert to the Sprint C.2 frozen-γ path.");
        sb.AppendLine();
        sb.AppendLine("## Remaining Limitations");
        sb.AppendLine();
        sb.AppendLine(
            "- **Ideal-gas model (Sprint C.3):** temperature-averaged γ_eff derived from a polynomial " +
            "Cp(T) fit across the chamber-to-throat range replaces the frozen chamber γ for " +
            "equilibrium-corrected gas states. Residual limitation: vibrational nonequilibrium " +
            "and dissociation effects above ~3500 K are not captured (requires real-gas EOS or " +
            "multi-species table).");
        sb.AppendLine();

        return sb.ToString();
    }

    private static string FormatK(double v, CultureInfo ci)
        => double.IsNaN(v) ? "N/A" : $"{v.ToString("F0", ci)} K";

    private static string FormatGamma(double v, CultureInfo ci)
        => double.IsNaN(v) ? "N/A" : v.ToString("F4", ci);

    private static string GammaSourceLabel(CpModel cpModel, bool isFlatCp) => cpModel switch
    {
        CpModel.FrozenGamma                => "Frozen GammaChamber (C.2)",
        CpModel.PolynomialFit when isFlatCp => "Frozen GammaChamber (C.3 polynomial degenerate)",
        CpModel.PolynomialFit              => "γ_eff polynomial Cp(T) (C.3)",
        _                                  => cpModel.ToString(),
    };

    private static string FormatSutherlandS(double v, CultureInfo ci)
        => double.IsNaN(v) || v == 0.0 ? "N/A" : v.ToString("F1", ci);

    private static string FormatMuRef(double v, CultureInfo ci)
        => double.IsNaN(v) || v == 0.0 ? "N/A" : v.ToString("G4", ci);

    private static string SutherlandSourceLabel(SutherlandSource src, string pairLabel) => src switch
    {
        SutherlandSource.Cea        => $"CEA blend ({pairLabel}) — issue #480",
        SutherlandSource.BartzSlope => "Bartz slope (S = T_c / 9, Sprint C.2 fallback)",
        _                           => src.ToString(),
    };

    private static string MuRefSourceLabel(MuRefSource src, string pairLabel) => src switch
    {
        MuRefSource.Cea             => $"CEA blend ({pairLabel}) — issue #485",
        MuRefSource.CeaTableFormula => "CeaTable2DBase formula (μ = 1e-4 · (Tc/3500)^0.7)",
        _                           => src.ToString(),
    };
}
