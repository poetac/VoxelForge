// StirlingSolverTests.cs — Sprint STR.W1 unit tests for the closed-form
// Stirling-engine performance snapshot.

using System;
using Voxelforge.Stirling;
using Xunit;

namespace Voxelforge.Tests.Stirling;

public sealed class StirlingSolverTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneConfiguration()
    {
        var d = Whispergen1kW() with { Configuration = StirlingConfiguration.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsTHotAtOrBelowTCold()
    {
        var d = Whispergen1kW() with
        {
            HotSideTemperature_K  = 300.0,
            ColdSideTemperature_K = 350.0,
        };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsSecondLawEfficiencyAboveOne()
    {
        var d = Whispergen1kW() with { SecondLawEfficiency = 1.2 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── WhisperGen / Sunpower 1-kW residential CHP baseline ─────────────

    [Fact]
    public void Whispergen1kW_IndicatedPowerInClusterBand()
    {
        // V_swept = 40 cm³, P_mean = 1 MPa, f = 50 Hz → W_cycle = 20 J
        // → P_indicated = 1 kW. Cluster band [500 W, 2000 W].
        var r = StirlingSolver.Solve(Whispergen1kW());
        Assert.InRange(r.IndicatedPower_W, 500.0, 2000.0);
    }

    [Fact]
    public void Whispergen1kW_IndicatedEfficiencyInClusterBand()
    {
        // η_carnot = 1 − 350/850 = 0.588; η_2nd = 0.45 →
        // η_indicated = 0.265. Cluster band [0.15, 0.40].
        var r = StirlingSolver.Solve(Whispergen1kW());
        Assert.InRange(r.IndicatedEfficiency, 0.15, 0.40);
    }

    [Fact]
    public void Whispergen1kW_HeatInputInClusterBand()
    {
        // Q_hot = P / η ≈ 1000 / 0.265 = 3.77 kW. Cluster band [2, 6] kW.
        var r = StirlingSolver.Solve(Whispergen1kW());
        Assert.InRange(r.HeatInputRate_W, 2000.0, 6000.0);
    }

    [Fact]
    public void Whispergen1kW_EnergyBalance_QhotEqualsPplusQcold()
    {
        var r = StirlingSolver.Solve(Whispergen1kW());
        Assert.Equal(r.IndicatedPower_W + r.HeatRejectionRate_W,
                     r.HeatInputRate_W, precision: 4);
    }

    [Fact]
    public void Whispergen1kW_IndicatedEfficiencyBelowCarnot()
    {
        var r = StirlingSolver.Solve(Whispergen1kW());
        Assert.True(r.IndicatedEfficiency < r.CarnotEfficiency);
    }

    [Fact]
    public void Whispergen1kW_WorkPerCycle_EqualsMepTimesSweptVolume()
    {
        var d = Whispergen1kW();
        var r = StirlingSolver.Solve(d);
        Assert.Equal(r.MeanEffectivePressure_Pa * d.SweptVolume_m3,
                     r.WorkPerCycle_J, precision: 6);
    }

    [Fact]
    public void Whispergen1kW_IndicatedPowerEqualsWorkPerCycleTimesFrequency()
    {
        var d = Whispergen1kW();
        var r = StirlingSolver.Solve(d);
        Assert.Equal(r.WorkPerCycle_J * d.OperatingFrequency_Hz,
                     r.IndicatedPower_W, precision: 4);
    }

    // ── Scaling sanity ──────────────────────────────────────────────────

    [Fact]
    public void IndicatedPower_LinearInFrequency()
    {
        var lo = StirlingSolver.Solve(Whispergen1kW() with { OperatingFrequency_Hz = 25 });
        var hi = StirlingSolver.Solve(Whispergen1kW() with { OperatingFrequency_Hz = 50 });
        Assert.Equal(2.0, hi.IndicatedPower_W / lo.IndicatedPower_W, precision: 6);
    }

    [Fact]
    public void IndicatedPower_LinearInSweptVolume()
    {
        var lo = StirlingSolver.Solve(Whispergen1kW() with { SweptVolume_m3 = 2e-5 });
        var hi = StirlingSolver.Solve(Whispergen1kW() with { SweptVolume_m3 = 4e-5 });
        Assert.Equal(2.0, hi.IndicatedPower_W / lo.IndicatedPower_W, precision: 6);
    }

    [Fact]
    public void IndicatedPower_LinearInMeanPressure()
    {
        var lo = StirlingSolver.Solve(Whispergen1kW() with { MeanPressure_Pa = 0.5e6 });
        var hi = StirlingSolver.Solve(Whispergen1kW() with { MeanPressure_Pa = 1.0e6 });
        Assert.Equal(2.0, hi.IndicatedPower_W / lo.IndicatedPower_W, precision: 6);
    }

    [Fact]
    public void HigherHotSideTemperature_RaisesCarnotEfficiency()
    {
        // T_hot up → η_Carnot up (1 − T_c/T_h increases).
        var moderate = StirlingSolver.Solve(Whispergen1kW() with { HotSideTemperature_K = 700 });
        var hot      = StirlingSolver.Solve(Whispergen1kW() with { HotSideTemperature_K = 900 });
        Assert.True(hot.CarnotEfficiency > moderate.CarnotEfficiency);
    }

    [Fact]
    public void CarnotEfficiency_AtTHotEqualToTwiceTCold()
    {
        // T_h = 2·T_c → η_Carnot = 0.5 exactly.
        var d = Whispergen1kW() with
        {
            ColdSideTemperature_K = 300.0,
            HotSideTemperature_K  = 600.0,
        };
        var r = StirlingSolver.Solve(d);
        Assert.Equal(0.5, r.CarnotEfficiency, precision: 9);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // WhisperGen / Sunpower-class 1-kW residential CHP Stirling. Gamma
    // configuration. T_hot = 850 K (gas burner), T_cold = 350 K (water-
    // jacket cooler), P_mean = 1 MPa (He charge), V_swept = 40 cm³,
    // f = 50 Hz, η_2nd = 0.45. Lands P_indicated ≈ 1 kW.
    private static StirlingDesign Whispergen1kW() => new(
        Configuration:           StirlingConfiguration.Gamma,
        HotSideTemperature_K:    850.0,
        ColdSideTemperature_K:   350.0,
        MeanPressure_Pa:         1e6,
        SweptVolume_m3:          4e-5,
        OperatingFrequency_Hz:   50.0,
        SecondLawEfficiency:     0.45);
}
