// DoeTests.cs — OOB-10 DOE campaign designer + post-test report tests.
//
// All tests are pure-physics (no voxel building) → run directly in .Tests.
// Test 1 exercises the Sobol DOE loop directly; tests 2-3 use a synthetic
// measured CSV + a lightweight synthetic runner to keep execution < 2 s.

using System;
using System.IO;
using Voxelforge.Analysis;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public sealed class DoeTests
{
    // ── Test 1: Sobol sampling produces distinct, in-bounds designs ──────────
    //
    // The DOE loop's sampling stage is: Sobol.Next() → map to physical bounds
    // → RegenChamberOptimization.Unpack. This test verifies all three steps
    // produce non-null, geometrically distinct designs within the SA bounds.
    // It also exercises FeasibilityGate.PreScreen to confirm it doesn't crash
    // on any Sobol-generated design (returning null or non-null is both fine).

    [Fact]
    public void DesignDoe_SobolSampling_ProducesDistinctInBoundsDesigns()
    {
        var spec = new EngineSpec(
            PropellantPair:         Combustion.PropellantPair.LOX_RP1,
            Thrust_N:               15_000,
            ChamberPressure_Pa:     3_400_000,
            ExpansionRatio:         8.0);
        var seedDesign = AutoSeeder.Seed(spec).Design;
        var cond       = AutoSeeder.Seed(spec).Conditions;
        var bounds     = RegenChamberOptimization.Bounds;
        var sobol      = new SobolSequence(bounds.Length);
        sobol.SkipTo(1);

        const int N       = 10;
        var seenRawFirst2 = new System.Collections.Generic.HashSet<string>();

        for (int i = 0; i < N; i++)
        {
            double[] normalized = sobol.Next();

            // All components must be in [0, 1).
            Assert.Equal(bounds.Length, normalized.Length);
            for (int d = 0; d < bounds.Length; d++)
                Assert.InRange(normalized[d], 0.0, 1.0);

            // Map to physical space.
            var raw = new double[bounds.Length];
            for (int d = 0; d < bounds.Length; d++)
                raw[d] = bounds[d].Min + normalized[d] * (bounds[d].Max - bounds[d].Min);

            // Unpack must succeed (no exception).
            var design = RegenChamberOptimization.Unpack(raw, seedDesign);
            Assert.NotNull(design);

            // Track first-two-dim signature: all N samples must be distinct.
            seenRawFirst2.Add($"{raw[0]:F6},{raw[1]:F6}");

            // PreScreen must not throw (result is null or non-null — both OK).
            _ = FeasibilityGate.PreScreen(cond, design);
        }

        Assert.Equal(N, seenRawFirst2.Count);
    }

    // ── Test 2: post-test report renders without exception ────────────────────

    [Fact]
    public void PostTestReport_SyntheticCsv_RendersWithoutException()
    {
        string csv =
            "time_s,chamber_p_pa,coolant_p_in_pa,coolant_p_out_pa," +
            "coolant_t_in_k,coolant_t_out_k,total_mass_flow_kgs,wall_t_k\n" +
            "1.0,4000000,12000000,11000000,150,195,2.45,850\n" +
            "2.0,4000000,12000000,11000000,150,195,2.47,855\n" +
            "3.0,4000000,12000000,11000000,150,195,2.46,852\n";

        string tmp = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tmp, csv);
        try
        {
            var (samples, warnings) = MeasuredDataOverlay.ParseCsv(tmp);
            Assert.Equal(3, samples.Count);
            var measured = MeasuredDataOverlay.Summarise(samples);

            // Synthetic runner: knob sensitivities scale each observable.
            CalibrationObservables Runner(double cstar, double cf, double bartz,
                                          double htcSF, double frictionSF) =>
                new(
                    TotalMassFlow_kgs: 2.46 / (cstar * cf),
                    PeakWallT_K:       852.0 * bartz,
                    CoolantDT_K:       45.0  * htcSF,
                    CoolantDP_Pa:      1_000_000.0 * frictionSF);

            // Prior means come from OperatingConditions defaults.
            var priorPrediction = Runner(0.95, 0.94, 1.0, 1.0, 1.0);

            var cal = CalibrationPosterior.Calibrate(measured, Runner, maxOuterIterations: 2);

            string report = DoePostTestReport.BuildMarkdown(
                measured, cal, priorPrediction, [.. warnings]);

            Assert.False(string.IsNullOrWhiteSpace(report));
            Assert.Contains("## Calibrated Knobs",         report, StringComparison.Ordinal);
            Assert.Contains("## Predicted vs Measured",    report, StringComparison.Ordinal);
            Assert.Contains("## Fit Quality",              report, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ── Test 3: all calibrated MAP values are finite ──────────────────────────

    [Fact]
    public void PostTestReport_KnobDeltas_AreFinite()
    {
        string csv =
            "time_s,chamber_p_pa,coolant_p_in_pa,coolant_p_out_pa," +
            "coolant_t_in_k,coolant_t_out_k,total_mass_flow_kgs,wall_t_k\n" +
            "1.0,4000000,12000000,11000000,150,195,2.45,850\n" +
            "2.0,4000000,12000000,11000000,150,195,2.47,855\n" +
            "3.0,4000000,12000000,11000000,150,195,2.46,852\n";

        string tmp = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tmp, csv);
        try
        {
            var (samples, _) = MeasuredDataOverlay.ParseCsv(tmp);
            var measured      = MeasuredDataOverlay.Summarise(samples);

            CalibrationObservables Runner(double cstar, double cf, double bartz,
                                          double htcSF, double frictionSF) =>
                new(
                    TotalMassFlow_kgs: 2.46 / (cstar * cf),
                    PeakWallT_K:       852.0 * bartz,
                    CoolantDT_K:       45.0  * htcSF,
                    CoolantDP_Pa:      1_000_000.0 * frictionSF);

            var cal = CalibrationPosterior.Calibrate(measured, Runner, maxOuterIterations: 2);

            Assert.True(double.IsFinite(cal.CStarEfficiency.MapValue),             "CStarEfficiency MAP must be finite");
            Assert.True(double.IsFinite(cal.NozzleCfEfficiency.MapValue),           "NozzleCfEfficiency MAP must be finite");
            Assert.True(double.IsFinite(cal.BartzScalingFactor.MapValue),           "BartzScalingFactor MAP must be finite");
            Assert.True(double.IsFinite(cal.CoolantHtcScalingFactor.MapValue),      "CoolantHtcScalingFactor MAP must be finite");
            Assert.True(double.IsFinite(cal.CoolantFrictionScalingFactor.MapValue), "CoolantFrictionScalingFactor MAP must be finite");
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
