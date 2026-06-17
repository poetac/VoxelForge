// CfdDriftReportTests.cs — unit tests for CfdDriftReport.BuildMarkdown (issue #462).
//
// Covers wall-T comparison rendering, BartzScalingFactor MAP rendering, calibration
// convergence rendering, the Sprint C.3 / issue #455 Gas Model section (Cp model,
// γ used, γ source, flat-fallback flag), and NaN guards / Notes append.

using System.Collections.Generic;
using Voxelforge.Analysis;
using Voxelforge.Cfd.Config;
using Voxelforge.Cfd.Parser;
using Voxelforge.Cfd.Report;
using Xunit;

namespace Voxelforge.Cfd.Tests.Report;

public sealed class CfdDriftReportTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static Su2WallProfile MakeProfile(
        double peakK = 3000.0,
        bool converged = true,
        int nodeCount = 1000,
        IReadOnlyDictionary<int, double>? byStation = null,
        string warnings = "") =>
        new Su2WallProfile(
            Converged:                  converged,
            PeakAdiabaticWallTemp_K:    peakK,
            AdiabaticWallTempByStation: byStation ?? new Dictionary<int, double>(),
            NodeCount:                  nodeCount,
            ParseWarnings:              warnings);

    private static MultiKnobCalibrationResult MakeCalibration(
        double bartzMap = 1.05,
        double bartzPriorMean = 1.00,
        double bartzPriorSigma = 0.10,
        double ssrPrior = 5.0,
        double ssrMap = 1.5,
        int iterations = 4,
        string[]? notes = null)
    {
        KnobEstimate Knob(string name, double map) =>
            new KnobEstimate(
                Name: name,
                MapValue: map,
                PriorMean: bartzPriorMean,
                PriorSigma: bartzPriorSigma,
                SsrCurvature: 1.23,
                Interpretation: $"{name} drifted to {map:F2}");

        return new MultiKnobCalibrationResult(
            CStarEfficiency:              Knob("CStarEfficiency",              1.0),
            NozzleCfEfficiency:           Knob("NozzleCfEfficiency",           1.0),
            BartzScalingFactor:           Knob("BartzScalingFactor",           bartzMap),
            CoolantHtcScalingFactor:      Knob("CoolantHtcScalingFactor",      1.0),
            CoolantFrictionScalingFactor: Knob("CoolantFrictionScalingFactor", 1.0),
            SsrAtPrior:                   ssrPrior,
            SsrAtMap:                     ssrMap,
            IterationsUsed:               iterations,
            Notes:                        notes ?? System.Array.Empty<string>());
    }

    // ── section presence ─────────────────────────────────────────────────────

    [Fact]
    public void BuildMarkdown_ContainsAllRequiredSections()
    {
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(), MakeCalibration(), bartzPeakAdiabaticWallTemp_K: 2950.0);

        Assert.Contains("# CFD Validation Drift Report — Sprint C.3", md);
        Assert.Contains("## Wall Temperature Comparison (SU2 T_aw vs Bartz)", md);
        Assert.Contains("## BartzScalingFactor Calibration (MAP Estimate)", md);
        Assert.Contains("## Calibration Convergence", md);
        Assert.Contains("## Gas Model", md);                         // issue #455
        Assert.Contains("## Provenance", md);
        Assert.Contains("## Remaining Limitations", md);
    }

    // ── drift % math + acceptance label ──────────────────────────────────────

    [Fact]
    public void BuildMarkdown_DriftWithinTwentyPercent_ShowsAcceptance()
    {
        // 3000 vs 2950 → drift ≈ +1.7 % → within ±20 %.
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(peakK: 3000.0), MakeCalibration(), bartzPeakAdiabaticWallTemp_K: 2950.0);

        Assert.Contains("Within ±20% acceptance | Yes ✓", md);
    }

    [Fact]
    public void BuildMarkdown_DriftBeyondTwentyPercent_ShowsRejection()
    {
        // 3000 vs 2000 → drift = +50 % → outside ±20 %.
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(peakK: 3000.0), MakeCalibration(), bartzPeakAdiabaticWallTemp_K: 2000.0);

        Assert.Contains("Within ±20% acceptance | No ✗", md);
    }

    // ── NaN guards ───────────────────────────────────────────────────────────

    [Fact]
    public void BuildMarkdown_NaNTaw_RendersNAAndSkipsDrift()
    {
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(peakK: double.NaN), MakeCalibration(), bartzPeakAdiabaticWallTemp_K: 2950.0);

        Assert.Contains("SU2 peak T_aw (K) | N/A", md);
        // Drift rows are conditional on both inputs being numeric — should be absent.
        Assert.DoesNotContain("Drift (K)", md);
    }

    [Fact]
    public void BuildMarkdown_NaNBartz_SkipsDriftRows()
    {
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(), MakeCalibration(), bartzPeakAdiabaticWallTemp_K: double.NaN);

        Assert.Contains("Bartz peak T (K) | N/A", md);
        Assert.DoesNotContain("Drift (K)", md);
    }

    // ── Notes appended ───────────────────────────────────────────────────────

    [Fact]
    public void BuildMarkdown_WithNotes_AppendsNotesSection()
    {
        var cal = MakeCalibration(notes: new[] { "Coolant ΔT data missing", "WallT prior dominated" });
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(), cal, bartzPeakAdiabaticWallTemp_K: 2950.0);

        Assert.Contains("**Notes:**", md);
        Assert.Contains("- Coolant ΔT data missing", md);
        Assert.Contains("- WallT prior dominated", md);
    }

    [Fact]
    public void BuildMarkdown_WithoutNotes_OmitsNotesHeader()
    {
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(), MakeCalibration(notes: System.Array.Empty<string>()),
            bartzPeakAdiabaticWallTemp_K: 2950.0);

        Assert.DoesNotContain("**Notes:**", md);
    }

    // ── Gas Model section (issue #455) ───────────────────────────────────────

    [Fact]
    public void BuildMarkdown_GasModel_PolynomialFitActive_ShowsGammaEffSource()
    {
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(), MakeCalibration(), bartzPeakAdiabaticWallTemp_K: 2950.0,
            cpModel: CpModel.PolynomialFit,
            gammaUsed: 1.1583,
            isFlatCp: false);

        Assert.Contains("Cp model (configured) | PolynomialFit", md);
        Assert.Contains("γ used (SU2 GAMMA_VALUE) | 1.1583", md);
        Assert.Contains("γ source | γ_eff polynomial Cp(T) (C.3)", md);
        Assert.Contains("Polynomial Cp(T) flat-fallback | No", md);
    }

    [Fact]
    public void BuildMarkdown_GasModel_PolynomialFitDegenerate_ShowsFlatFallback()
    {
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(), MakeCalibration(), bartzPeakAdiabaticWallTemp_K: 2950.0,
            cpModel: CpModel.PolynomialFit,
            gammaUsed: 1.20,
            isFlatCp: true);

        Assert.Contains("Cp model (configured) | PolynomialFit", md);
        Assert.Contains("γ source | Frozen GammaChamber (C.3 polynomial degenerate)", md);
        Assert.Contains("Polynomial Cp(T) flat-fallback | Yes", md);
    }

    [Fact]
    public void BuildMarkdown_GasModel_FrozenGammaConfigured_ShowsC2Source()
    {
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(), MakeCalibration(), bartzPeakAdiabaticWallTemp_K: 2950.0,
            cpModel: CpModel.FrozenGamma,
            gammaUsed: 1.20,
            isFlatCp: false);

        Assert.Contains("Cp model (configured) | FrozenGamma", md);
        Assert.Contains("γ source | Frozen GammaChamber (C.2)", md);
    }

    [Fact]
    public void BuildMarkdown_GasModel_NaNGamma_RendersNA()
    {
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(), MakeCalibration(), bartzPeakAdiabaticWallTemp_K: 2950.0,
            cpModel: CpModel.PolynomialFit,
            gammaUsed: double.NaN,
            isFlatCp: false);

        Assert.Contains("γ used (SU2 GAMMA_VALUE) | N/A", md);
    }

    // ── default arguments preserve backward compatibility ────────────────────

    [Fact]
    public void BuildMarkdown_DefaultGasModelArgs_RendersPolynomialFitNAGamma()
    {
        // Existing callers that don't pass the new gas-model args land on the C.3 default.
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(), MakeCalibration(), bartzPeakAdiabaticWallTemp_K: 2950.0);

        Assert.Contains("Cp model (configured) | PolynomialFit", md);
        Assert.Contains("γ used (SU2 GAMMA_VALUE) | N/A", md);
        Assert.Contains("Polynomial Cp(T) flat-fallback | No", md);
    }

    // ── Transport-property provenance (issues #480, #485) ────────────────────

    [Fact]
    public void BuildMarkdown_TransportProvenance_PerPairCea_ShowsCeaSourcesAndPairLabels()
    {
        var prov = new Su2ConfigProvenance(
            SutherlandS_K:       197.0,
            SutherlandSource:    SutherlandSource.Cea,
            SutherlandPairLabel: "LOX/CH4",
            MuRef_PaS:           9.5e-5,
            MuRefSource:         MuRefSource.Cea,
            MuRefPairLabel:      "LOX/CH4");

        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(), MakeCalibration(), bartzPeakAdiabaticWallTemp_K: 2950.0,
            configProvenance: prov);

        Assert.Contains("Sutherland S [K] | 197.0", md);
        Assert.Contains("Sutherland source | CEA blend (LOX/CH4) — issue #480", md);
        Assert.Contains("Viscosity reference source | CEA blend (LOX/CH4) — issue #485", md);
        // μ_ref formatted G4 gives "9.5E-05".
        Assert.Contains("μ_ref [Pa·s] | 9.5E-05", md);
    }

    [Fact]
    public void BuildMarkdown_TransportProvenance_BartzSlopeFallback_ShowsFallbackLabels()
    {
        var prov = new Su2ConfigProvenance(
            SutherlandS_K:       377.78,
            SutherlandSource:    SutherlandSource.BartzSlope,
            SutherlandPairLabel: string.Empty,
            MuRef_PaS:           1.0e-4,
            MuRefSource:         MuRefSource.CeaTableFormula,
            MuRefPairLabel:      string.Empty);

        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(), MakeCalibration(), bartzPeakAdiabaticWallTemp_K: 2950.0,
            configProvenance: prov);

        Assert.Contains("Sutherland source | Bartz slope (S = T_c / 9, Sprint C.2 fallback)", md);
        Assert.Contains("Viscosity reference source | CeaTable2DBase formula (μ = 1e-4 · (Tc/3500)^0.7)", md);
    }

    [Fact]
    public void BuildMarkdown_DefaultTransportProvenance_RendersNAValues()
    {
        // Existing callers that don't pass configProvenance must not crash; the
        // default(Su2ConfigProvenance) struct has SutherlandS_K = 0 and MuRef_PaS = 0.
        // Renderer maps both to "N/A".
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(), MakeCalibration(), bartzPeakAdiabaticWallTemp_K: 2950.0);

        Assert.Contains("Sutherland S [K] | N/A", md);
        Assert.Contains("μ_ref [Pa·s] | N/A", md);
        // Default-struct enums = first value (BartzSlope, CeaTableFormula) → fallback labels.
        Assert.Contains("Bartz slope", md);
        Assert.Contains("CeaTable2DBase formula", md);
    }

    // ── BartzScalingFactor MAP rendering ─────────────────────────────────────

    [Fact]
    public void BuildMarkdown_RendersBartzMapValues()
    {
        string md = CfdDriftReport.BuildMarkdown(
            MakeProfile(), MakeCalibration(bartzMap: 1.0750, bartzPriorMean: 1.0000, bartzPriorSigma: 0.1500),
            bartzPeakAdiabaticWallTemp_K: 2950.0);

        Assert.Contains("MAP estimate | 1.0750", md);
        Assert.Contains("Prior mean | 1.0000", md);
        Assert.Contains("Prior σ | 0.1500", md);
    }
}
