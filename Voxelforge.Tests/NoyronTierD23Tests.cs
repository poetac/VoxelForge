// NoyronTierD23Tests.cs — Tier D2 + D3 forcing-function suite. Covers:
//   • D2 — MeasuredDataOverlay per-station thermocouple ingest:
//     CSV column parsing (wall_t_station_<n>_k), aggregation into
//     MeasuredSummary.WallTByStation, ComputeGoodnessOfFit reduced χ²
//     with matching / non-matching station sets, BuildOverlay Fit
//     population + warning surface on poor fit.
//   • D3 — AtomisationSMD: Rizk-Lefebvre correlation units + monotonicity
//     against the three implemented propellant pairs; FuelFluidProperties
//     lookup covers implemented pairs; unsupported pair returns NaN;
//     ComputeFromElementResult end-to-end; QualitativeLabel thresholds;
//     ParetoScatterPanel ColorBy switch round-trips; ParetoPoint
//     carries SMD.

using Voxelforge.Analysis;
using Voxelforge.Combustion;
using Voxelforge.Injector.Elements;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;
using Voxelforge.UI;

namespace Voxelforge.Tests;

public class NoyronTierD23Tests
{
    // ══════════════════════ D2 — per-station ingest ══════════════════════

    [Fact]
    public void Parser_PicksUpWallTStationColumns()
    {
        using var tmp = TestTempFile.WithUniqueName("tc", "csv");
        File.WriteAllText(tmp.Path,
            "time_s,chamber_p_pa,coolant_p_in_pa,coolant_p_out_pa," +
            "coolant_t_in_k,coolant_t_out_k,wall_t_station_5_k,wall_t_station_40_k\n" +
            "0.0,7e6,12e6,8e6,150,400,780,920\n" +
            "0.1,7e6,12e6,8e6,150,400,785,925\n" +
            "0.2,7e6,12e6,8e6,150,400,790,930\n" +
            "0.3,7e6,12e6,8e6,150,400,795,935\n" +
            "0.4,7e6,12e6,8e6,150,400,800,940\n");

        var (samples, _) = MeasuredDataOverlay.ParseCsv(tmp.Path);
        Assert.Equal(5, samples.Count);
        Assert.NotNull(samples[0].WallTByStation);
        Assert.Equal(780, samples[0].WallTByStation![5]);
        Assert.Equal(920, samples[0].WallTByStation![40]);
    }

    [Fact]
    public void Summariser_AggregatesPerStationAverages()
    {
        using var tmp = TestTempFile.WithUniqueName("tc", "csv");
        // 8 rows; middle 50% is rows 2–5 (0-indexed). Station 5
        // values in that window: 790, 795, 800, 805 → avg 797.5.
        File.WriteAllText(tmp.Path,
            "time_s,chamber_p_pa,coolant_t_in_k,coolant_t_out_k,wall_t_station_5_k\n" +
            "0.0,7e6,150,400,780\n" +
            "0.1,7e6,150,400,785\n" +
            "0.2,7e6,150,400,790\n" +
            "0.3,7e6,150,400,795\n" +
            "0.4,7e6,150,400,800\n" +
            "0.5,7e6,150,400,805\n" +
            "0.6,7e6,150,400,810\n" +
            "0.7,7e6,150,400,815\n");

        var (samples, _) = MeasuredDataOverlay.ParseCsv(tmp.Path);
        var summary = MeasuredDataOverlay.Summarise(samples);
        Assert.NotNull(summary.WallTByStation);
        Assert.True(summary.WallTByStation!.ContainsKey(5));
        Assert.InRange(summary.WallTByStation![5], 796.0, 800.0);
    }

    [Fact]
    public void GoodnessOfFit_ComputesReducedChiSquared()
    {
        var measured  = new Dictionary<int, double> { { 0, 800 }, { 10, 900 }, { 20, 850 } };
        var predicted = new Dictionary<int, double> { { 0, 820 }, { 10, 880 }, { 20, 860 } };
        // residuals: +20, -20, +10 → squared: 400, 400, 100 → sum 900.
        // χ² at σ=20 K: 400/400 + 400/400 + 100/400 = 2.25; ν=2 → χ²/ν = 1.125.
        var fit = MeasuredDataOverlay.ComputeGoodnessOfFit(measured, predicted, sigma_K: 20.0);
        Assert.NotNull(fit);
        Assert.Equal(3, fit!.ObservationCount);
        Assert.InRange(fit.ChiSquaredReduced, 1.0, 1.3);
        Assert.Equal(20, fit.WorstResidual_K, 0);
    }

    [Fact]
    public void GoodnessOfFit_ReturnsNullWhenNoOverlap()
    {
        var measured  = new Dictionary<int, double> { { 0, 800 } };
        var predicted = new Dictionary<int, double> { { 99, 850 } };
        var fit = MeasuredDataOverlay.ComputeGoodnessOfFit(measured, predicted);
        Assert.Null(fit);
    }

    [Fact]
    public void GoodnessOfFit_ReturnsNullOnEmptyInputs()
    {
        Assert.Null(MeasuredDataOverlay.ComputeGoodnessOfFit(
            measuredByStation:  new Dictionary<int, double>(),
            predictedByStation: new Dictionary<int, double> { { 0, 800 } }));
        Assert.Null(MeasuredDataOverlay.ComputeGoodnessOfFit(
            measuredByStation:  new Dictionary<int, double> { { 0, 800 } },
            predictedByStation: new Dictionary<int, double>()));
    }

    [Fact]
    public void BuildOverlay_PopulatesFitWhenBothSidesPresent()
    {
        var summary = new MeasuredSummary(
            SampleCount: 10, ChamberP_Pa: 7e6, CoolantDP_Pa: 4e6,
            CoolantDT_K: 250, CoolantT_In_K: 150, CoolantT_Out_K: 400,
            Thrust_N: 2224, WallT_K: 900,
            WallTByStation: new Dictionary<int, double> { { 5, 780 }, { 40, 920 } });
        var predicted = new Dictionary<int, double> { { 5, 800 }, { 40, 900 } };
        var result = MeasuredDataOverlay.BuildOverlay(
            measured: summary,
            predicted_PeakWallT_K:  900,
            predicted_CoolantDT_K:  255,
            predicted_CoolantDP_Pa: 4e6,
            predictedWallTByStation: predicted);
        Assert.NotNull(result.Fit);
        Assert.Equal(2, result.Fit!.ObservationCount);
    }

    [Fact]
    public void BuildOverlay_OmitsFitWhenPerStationAbsent()
    {
        var summary = new MeasuredSummary(
            SampleCount: 10, ChamberP_Pa: 7e6, CoolantDP_Pa: 4e6,
            CoolantDT_K: 250, CoolantT_In_K: 150, CoolantT_Out_K: 400,
            Thrust_N: 2224, WallT_K: 900);
        var result = MeasuredDataOverlay.BuildOverlay(
            measured: summary,
            predicted_PeakWallT_K:  900,
            predicted_CoolantDT_K:  255,
            predicted_CoolantDP_Pa: 4e6);
        Assert.Null(result.Fit);
    }

    [Fact]
    public void BuildOverlay_WarnsOnPoorFit()
    {
        var summary = new MeasuredSummary(
            SampleCount: 10, ChamberP_Pa: 7e6, CoolantDP_Pa: 4e6,
            CoolantDT_K: 250, CoolantT_In_K: 150, CoolantT_Out_K: 400,
            Thrust_N: 2224, WallT_K: 900,
            WallTByStation: new Dictionary<int, double> { { 5, 700 }, { 40, 900 } });
        // Large residuals → χ²/ν > 4.
        var predicted = new Dictionary<int, double> { { 5, 900 }, { 40, 1100 } };
        var result = MeasuredDataOverlay.BuildOverlay(
            measured: summary,
            predicted_PeakWallT_K:  900,
            predicted_CoolantDT_K:  255,
            predicted_CoolantDP_Pa: 4e6,
            predictedWallTByStation: predicted,
            perStationSigma_K: 20.0);
        Assert.NotNull(result.Fit);
        Assert.Contains(result.Warnings, w => w.Contains("model"));
    }

    // ══════════════════════ D3 — AtomisationSMD ══════════════════════

    [Theory]
    [InlineData(PropellantPair.LOX_CH4)]
    [InlineData(PropellantPair.LOX_H2)]
    [InlineData(PropellantPair.LOX_RP1)]
    public void FuelFluidProperties_CoversImplementedPairs(PropellantPair pair)
    {
        var props = FuelFluidProperties.For(pair);
        Assert.True(props.FuelSurfaceTension_Nm > 0);
        Assert.True(props.FuelViscosity_kgms    > 0);
        Assert.True(props.FuelDensity_kgm3      > 0);
        Assert.True(props.OxidiserDensity_kgm3  > 0);
    }

    [Fact]
    public void FuelFluidProperties_ReturnsDefaultForUnsupportedPair()
    {
        var props = FuelFluidProperties.For(PropellantPair.N2O4_MMH);
        Assert.Equal(0.0, props.FuelSurfaceTension_Nm);
    }

    [Fact]
    public void SMD_IsPositiveForTypicalLoxCh4Inputs()
    {
        var props = FuelFluidProperties.For(PropellantPair.LOX_CH4);
        // Typical LOX/CH4 coax: fuel 60 m/s, ox 20 m/s, D_fuel 1 mm.
        // At U_rel = 40 m/s (high airblast) Rizk-Lefebvre gives ~5 µm;
        // at lower relative velocity (more like 10 m/s, RS-25 coax
        // regime) the correlation produces 50-200 µm. The physical
        // envelope across coax operating points is therefore ~3-500 µm.
        double smd = AtomisationSMD.Compute(
            fluidProps: props,
            oxVelocity_ms:          20.0,
            fuelVelocity_ms:        60.0,
            characteristicDiameter_m: 1e-3);
        Assert.True(smd > 0);
        Assert.InRange(smd, 1.0, 500.0);
    }

    [Fact]
    public void SMD_DecreasesWithRelativeVelocity()
    {
        // Higher relative velocity → smaller drops (airblast physics).
        var props = FuelFluidProperties.For(PropellantPair.LOX_CH4);
        double smdLow  = AtomisationSMD.Compute(props, 10, 30, 1e-3);
        double smdHigh = AtomisationSMD.Compute(props, 10, 100, 1e-3);
        Assert.True(smdHigh < smdLow,
            $"SMD at high v_rel {smdHigh:F1} µm should be smaller than at low v_rel {smdLow:F1} µm.");
    }

    [Fact]
    public void SMD_ReturnsNaNForUnsupportedPair()
    {
        var props = FuelFluidProperties.For(PropellantPair.N2O4_MMH);
        double smd = AtomisationSMD.Compute(props, 10, 50, 1e-3);
        Assert.True(double.IsNaN(smd));
    }

    [Fact]
    public void SMD_ReturnsNaNOnZeroRelativeVelocity()
    {
        var props = FuelFluidProperties.For(PropellantPair.LOX_CH4);
        double smd = AtomisationSMD.Compute(props, 30, 30, 1e-3);
        Assert.True(double.IsNaN(smd));
    }

    [Fact]
    public void SMD_ComputeFromElementResult_EndToEnd()
    {
        var e = new CoaxElement();
        var inp = new SizingInputs(
            DeltaPInj_Pa:           1.4e6,
            OxDensity_kgm3:         1140.0,
            FuelDensity_kgm3:       420.0,
            OxFlowPerElement_kgs:   0.12,
            FuelFlowPerElement_kgs: 0.036);
        var r = e.Size(inp);
        double smd = AtomisationSMD.ComputeFromElementResult(PropellantPair.LOX_CH4, r);
        Assert.True(smd > 0);
        Assert.False(double.IsNaN(smd));
    }

    [Theory]
    [InlineData(20,  "Excellent")]
    [InlineData(50,  "Good")]
    [InlineData(100, "Marginal")]
    [InlineData(200, "Poor")]
    [InlineData(double.NaN, "n/a")]
    public void QualitativeLabel_Bands(double smd, string expected)
    {
        Assert.Equal(expected, AtomisationSMD.QualitativeLabel(smd));
    }

    // ══════════════════════ D3 — Pareto integration ══════════════════════

    [Fact]
    public void ParetoPoint_DefaultSMDIsNaN_PreV42CompatibilityGuard()
    {
        // Legacy call sites used a 5-arg ctor; default NaN SMD
        // keeps them compilable unchanged.
        var p = new ParetoPoint(
            PeakWallT_K: 900, CoolantDP_Pa: 4e6, Mass_g: 500,
            Parameters: Array.Empty<double>(), Iteration: 42);
        Assert.True(double.IsNaN(p.SMD_um));
    }

    [Fact]
    public void ParetoPoint_WithSMDRoundTrips()
    {
        var p = new ParetoPoint(900, 4e6, 500, Array.Empty<double>(), 42, SMD_um: 75.5);
        Assert.Equal(75.5, p.SMD_um);
        // `with` mutator preserves SMD by default.
        var p2 = p with { Mass_g = 450 };
        Assert.Equal(75.5, p2.SMD_um);
    }

    [Fact]
    public void ParetoScatterPanel_ColorBySwitchesDefaultsToMass()
    {
        using var panel = new ParetoScatterPanel();
        Assert.Equal(ParetoColorBy.Mass, panel.ColorBy);
        panel.SetColorBy(ParetoColorBy.SMD);
        Assert.Equal(ParetoColorBy.SMD, panel.ColorBy);
        panel.SetColorBy(ParetoColorBy.Mass);
        Assert.Equal(ParetoColorBy.Mass, panel.ColorBy);
    }
}
