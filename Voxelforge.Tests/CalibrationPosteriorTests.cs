// CalibrationPosteriorTests.cs — regression tests for the five-knob
// MAP calibration (OOB-1 extended). Uses synthetic "runners" (pure math)
// instead of full GenerateWith calls so the tests are fast and PicoGK-free.

using System;
using System.Collections.Generic;
using Voxelforge.Analysis;
using Xunit;

namespace Voxelforge.Tests;

public sealed class CalibrationPosteriorTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal measured summary with all thermal channels set to zero/NaN
    /// (only the supplied fields populated).
    /// </summary>
    private static MeasuredSummary ThermalOnly(
        double wallT_K, double dT_K, double dP_Pa)
        => new(
            SampleCount:      10,
            ChamberP_Pa:      4_000_000,
            CoolantDP_Pa:     dP_Pa,
            CoolantDT_K:      dT_K,
            CoolantT_In_K:    150,
            CoolantT_Out_K:   150 + dT_K,
            Thrust_N:         double.NaN,
            WallT_K:          wallT_K);

    private static MeasuredSummary MassFlowOnly(double massFlow_kgs)
        => new(
            SampleCount:      10,
            ChamberP_Pa:      4_000_000,
            CoolantDP_Pa:     0,
            CoolantDT_K:      0,
            CoolantT_In_K:    150,
            CoolantT_Out_K:   150,
            Thrust_N:         double.NaN,
            WallT_K:          double.NaN,
            TotalMassFlow_kgs: massFlow_kgs);

    private static MeasuredSummary FullMeasured(
        double wallT_K, double dT_K, double dP_Pa, double massFlow_kgs)
        => new(
            SampleCount:      20,
            ChamberP_Pa:      4_000_000,
            CoolantDP_Pa:     dP_Pa,
            CoolantDT_K:      dT_K,
            CoolantT_In_K:    150,
            CoolantT_Out_K:   150 + dT_K,
            Thrust_N:         double.NaN,
            WallT_K:          wallT_K,
            TotalMassFlow_kgs: massFlow_kgs);

    private static MeasuredSummary NoObservables()
        => new(
            SampleCount:  5,
            ChamberP_Pa:  4_000_000,
            CoolantDP_Pa: 0,
            CoolantDT_K:  0,
            CoolantT_In_K: 150,
            CoolantT_Out_K: 150,
            Thrust_N:     double.NaN,
            WallT_K:      double.NaN);

    // ── Test 1: thermal-only — BartzSF shifts toward truth ───────────────────

    [Fact]
    public void Calibrate_ThermalOnly_BartzShiftsTowardTruth()
    {
        // True BartzScalingFactor = 1.15 (model under-predicts heat load
        // by 15 %). Measured data is consistent with BartzSF = 1.15.
        const double trueBartzSF = 1.15;

        // Synthetic runner: thermal outputs scale linearly with bartzSF;
        // mass flow is not provided. htcSF and frictionSF are not used here.
        CalibrationObservables Runner(double cstar, double cf, double bartz,
                                      double htcSF, double frictionSF) =>
            new(
                TotalMassFlow_kgs: double.NaN,
                PeakWallT_K:       800.0  * bartz,
                CoolantDT_K:       45.0   * bartz,
                CoolantDP_Pa:      900_000 * bartz);

        // Measured values at the true bartzSF.
        var measured = ThermalOnly(
            wallT_K: 800.0  * trueBartzSF,
            dT_K:    45.0   * trueBartzSF,
            dP_Pa:   900_000 * trueBartzSF);

        var result = CalibrationPosterior.Calibrate(measured, Runner);

        // BartzSF should shift toward 1.15 (within 2 %).
        Assert.InRange(result.BartzScalingFactor.MapValue, 1.12, 1.18);

        // Efficiency knobs should stay near their prior means (no mass flow).
        Assert.InRange(result.CStarEfficiency.MapValue,    0.93, 0.97);
        Assert.InRange(result.NozzleCfEfficiency.MapValue, 0.92, 0.96);

        // MAP must improve on the prior.
        Assert.True(result.SsrAtMap < result.SsrAtPrior,
            $"SSR at MAP ({result.SsrAtMap:G4}) should be < SSR at prior ({result.SsrAtPrior:G4}).");

        // Notes should mention no mass-flow channel.
        Assert.Contains(result.Notes,
            n => n.Contains("total_mass_flow_kgs", StringComparison.OrdinalIgnoreCase));
    }

    // ── Test 2: mass-flow-only — efficiency product shifts toward truth ───────

    [Fact]
    public void Calibrate_MassFlowOnly_EfficiencyProductShiftsTowardTruth()
    {
        // True combined efficiency product: 0.91 × 0.92 = 0.8372
        // (lower than the prior product 0.95 × 0.94 = 0.893).
        // Lower efficiency → engine must burn more propellant to hit spec thrust
        // → measured mass flow is higher than the prior predicts.
        const double trueCstar  = 0.91;
        const double trueCf     = 0.92;
        const double trueEtaProd = trueCstar * trueCf;   // 0.8372

        // Synthetic runner: mass flow ∝ 1 / (cstar × cf) — sizing holds spec
        // thrust constant by adjusting m_dot. htcSF and frictionSF not used.
        const double K = 2.50; // arbitrary proportionality constant (kg/s)
        CalibrationObservables Runner(double cstar, double cf, double bartz,
                                      double htcSF, double frictionSF) =>
            new(
                TotalMassFlow_kgs: K / (cstar * cf),
                PeakWallT_K:       double.NaN,
                CoolantDT_K:       double.NaN,
                CoolantDP_Pa:      double.NaN);

        var measured = MassFlowOnly(K / trueEtaProd);   // 2.50 / 0.8372 ≈ 2.987 kg/s

        var result = CalibrationPosterior.Calibrate(measured, Runner);

        // The product CStarEff × CfEff should shift clearly below the prior product 0.893
        // and toward the true 0.8372 (within 3 %).
        double calibProd = result.CStarEfficiency.MapValue * result.NozzleCfEfficiency.MapValue;
        Assert.InRange(calibProd, 0.810, 0.870);

        // BartzSF should stay near its prior (no thermal data).
        Assert.InRange(result.BartzScalingFactor.MapValue, 0.94, 1.06);

        // MAP must improve on the prior.
        Assert.True(result.SsrAtMap < result.SsrAtPrior);
    }

    // ── Test 3: joint calibration — both axes shift ───────────────────────────

    [Fact]
    public void Calibrate_Joint_BothAxesShiftTowardTruth()
    {
        // True values: combined efficiency product = 0.91 × 0.92 = 0.8372;
        //              BartzScalingFactor = 1.18.
        const double trueEtaProd = 0.91 * 0.92;
        const double trueBartzSF = 1.18;
        const double K           = 2.50;  // mass-flow proportionality constant

        CalibrationObservables Runner(double cstar, double cf, double bartz,
                                      double htcSF, double frictionSF) =>
            new(
                TotalMassFlow_kgs: K / (cstar * cf),
                PeakWallT_K:       820.0   * bartz,
                CoolantDT_K:       48.0    * bartz,
                CoolantDP_Pa:      950_000 * bartz);

        var measured = FullMeasured(
            wallT_K:       820.0    * trueBartzSF,
            dT_K:          48.0     * trueBartzSF,
            dP_Pa:         950_000  * trueBartzSF,
            massFlow_kgs:  K / trueEtaProd);

        var result = CalibrationPosterior.Calibrate(measured, Runner);

        // Efficiency product shifts toward truth (within 3 % of true 0.8372).
        double calibProd = result.CStarEfficiency.MapValue * result.NozzleCfEfficiency.MapValue;
        Assert.InRange(calibProd, 0.810, 0.868);

        // BartzSF shifts toward 1.18 (within 3 %).
        Assert.InRange(result.BartzScalingFactor.MapValue, 1.14, 1.22);

        // MAP objective strictly better than prior.
        Assert.True(result.SsrAtMap < result.SsrAtPrior);

        // Notes mention SSR improvement.
        Assert.Contains(result.Notes, n => n.Contains("SSR", StringComparison.Ordinal));
    }

    // ── Test 4: no observables — returns prior means unchanged ───────────────

    [Fact]
    public void Calibrate_NoObservables_HoldsAtPriorMeans()
    {
        int calls = 0;
        CalibrationObservables Runner(double cstar, double cf, double bartz,
                                      double htcSF, double frictionSF)
        {
            calls++;
            return new(
                TotalMassFlow_kgs: double.NaN,
                PeakWallT_K:       double.NaN,
                CoolantDT_K:       double.NaN,
                CoolantDP_Pa:      double.NaN);
        }

        var result = CalibrationPosterior.Calibrate(NoObservables(), Runner);

        // With no active channels the coordinate-descent axes don't move; the
        // only objective contribution is the prior penalty at the prior means
        // (which is zero). MAP values should sit at the prior means.
        Assert.InRange(result.CStarEfficiency.MapValue,    0.93, 0.97);
        Assert.InRange(result.NozzleCfEfficiency.MapValue, 0.92, 0.96);
        Assert.InRange(result.BartzScalingFactor.MapValue, 0.96, 1.04);

        // SSR at prior and at MAP should both be near zero (only prior penalty).
        Assert.InRange(result.SsrAtMap, 0, 0.05);

        // Notes must mention at least one missing channel.
        Assert.NotEmpty(result.Notes);
    }

    // ── Test 7: HTC axis — CoolantHtcSF shifts toward truth ─────────────────

    [Fact]
    public void Calibrate_HtcAxis_HtcSFShiftsTowardTruth()
    {
        // True CoolantHtcScalingFactor = 1.20 (model under-predicts heat
        // pickup by 20 %). Runner output scales only with htcSF.
        const double trueHtcSF = 1.20;

        CalibrationObservables Runner(double cstar, double cf, double bartz,
                                      double htcSF, double frictionSF) =>
            new(
                TotalMassFlow_kgs: double.NaN,
                PeakWallT_K:       double.NaN,
                CoolantDT_K:       45.0 * htcSF,
                CoolantDP_Pa:      double.NaN);

        // Measured: only CoolantDT channel.
        var measured = ThermalOnly(
            wallT_K: double.NaN,
            dT_K:    45.0 * trueHtcSF,
            dP_Pa:   0);

        var result = CalibrationPosterior.Calibrate(measured, Runner);

        // HtcSF should shift toward 1.20 (within 5 %).
        Assert.InRange(result.CoolantHtcScalingFactor.MapValue, 1.14, 1.26);

        // BartzSF and FrictionSF stay near prior (runner ignores them here).
        Assert.InRange(result.BartzScalingFactor.MapValue,         0.90, 1.10);
        Assert.InRange(result.CoolantFrictionScalingFactor.MapValue, 0.90, 1.10);

        Assert.True(result.SsrAtMap < result.SsrAtPrior);
    }

    // ── Test 8: Friction axis — CoolantFrictionSF shifts toward truth ────────

    [Fact]
    public void Calibrate_FrictionAxis_FrictionSFShiftsTowardTruth()
    {
        // True CoolantFrictionScalingFactor = 0.75 (model over-predicts
        // pressure drop by 33 %). Runner output scales only with frictionSF.
        const double trueFrictionSF = 0.75;

        CalibrationObservables Runner(double cstar, double cf, double bartz,
                                      double htcSF, double frictionSF) =>
            new(
                TotalMassFlow_kgs: double.NaN,
                PeakWallT_K:       double.NaN,
                CoolantDT_K:       double.NaN,
                CoolantDP_Pa:      900_000.0 * frictionSF);

        // Measured: only CoolantDP channel.
        var measured = ThermalOnly(
            wallT_K: double.NaN,
            dT_K:    0,
            dP_Pa:   900_000.0 * trueFrictionSF);

        var result = CalibrationPosterior.Calibrate(measured, Runner);

        // FrictionSF should shift toward 0.75 (within 7 %).
        Assert.InRange(result.CoolantFrictionScalingFactor.MapValue, 0.69, 0.81);

        // HtcSF stays near prior (no DT channel).
        Assert.InRange(result.CoolantHtcScalingFactor.MapValue, 0.90, 1.10);

        Assert.True(result.SsrAtMap < result.SsrAtPrior);
    }

    // ── Test 5: golden-section internal helper ────────────────────────────────

    [Fact]
    public void GoldenSection_FindsMinimumOfConvexFunction()
    {
        // f(x) = (x − 0.72)² minimised over [0.5, 1.0] → min at x = 0.72.
        double found = CalibrationPosterior.GoldenSection(x => (x - 0.72) * (x - 0.72), 0.5, 1.0);
        Assert.InRange(found, 0.7195, 0.7205);
    }

    // ── Test 6: MeasuredSummary TotalMassFlow_kgs round-trips through Summarise

    [Fact]
    public void MeasuredDataOverlay_ParseCsv_IncludesMassFlowColumn()
    {
        // Build an in-memory CSV with a total_mass_flow_kgs column and parse it.
        string csv =
            "time_s,chamber_p_pa,coolant_p_in_pa,coolant_p_out_pa," +
            "coolant_t_in_k,coolant_t_out_k,total_mass_flow_kgs\n" +
            "1.0,4000000,12000000,11000000,150,180,2.45\n" +
            "2.0,4000000,12000000,11000000,150,180,2.47\n" +
            "3.0,4000000,12000000,11000000,150,180,2.46\n";

        string tmp = System.IO.Path.GetTempFileName() + ".csv";
        System.IO.File.WriteAllText(tmp, csv);
        try
        {
            var (samples, warnings) = MeasuredDataOverlay.ParseCsv(tmp);
            Assert.Empty(warnings.FindAll(w => w.Contains("Error", StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(3, samples.Count);
            Assert.All(samples, s => Assert.False(double.IsNaN(s.TotalMassFlow_kgs)));

            var summary = MeasuredDataOverlay.Summarise(samples);
            Assert.InRange(summary.TotalMassFlow_kgs, 2.44, 2.48);
        }
        finally
        {
            System.IO.File.Delete(tmp);
        }
    }
}
