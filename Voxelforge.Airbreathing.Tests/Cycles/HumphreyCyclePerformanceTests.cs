// HumphreyCyclePerformanceTests.cs — Wave 1 PR-4 (sub-step 1a.5).
// Closed-form tests for the constant-volume combustion + peak-pressure helpers.

using Voxelforge.Airbreathing.Cycles;
using Xunit;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class HumphreyCyclePerformanceTests
{
    [Fact]
    public void CombustorExitT_K_TypicalKeroseneInputs_GivesReasonableT()
    {
        // T_t2 = 290 K, f = 0.06, LHV(JP-8) = 42.8 MJ/kg, η_b = 0.99,
        // cp = 1004.7 J/(kg·K).
        // T_t4 = 290 + 0.99 · 0.06 · 42.8e6 / 1004.7 = 290 + 2530 = 2820 K
        double T_t4 = HumphreyCyclePerformance.CombustorExitT_K(
            T_in_K: 290.0, fuelAirMassFraction: 0.06, LHV_Jkg: 42.8e6);
        double expected = 290.0 + 0.99 * 0.06 * 42.8e6 / 1004.7;
        Assert.Equal(expected, T_t4, precision: 1);
    }

    [Theory]
    [InlineData(0.0, 0.06, 42.8e6)]      // Zero T_in
    [InlineData(-50.0, 0.06, 42.8e6)]    // Negative T_in
    [InlineData(double.NaN, 0.06, 42.8e6)]  // NaN T_in
    public void CombustorExitT_K_InvalidT_in_ReturnsNaN(double T_in, double f, double LHV)
    {
        Assert.True(double.IsNaN(HumphreyCyclePerformance.CombustorExitT_K(T_in, f, LHV)));
    }

    [Theory]
    [InlineData(290.0, 0.0, 42.8e6)]     // No fuel
    [InlineData(290.0, 0.06, 0.0)]       // Zero LHV
    [InlineData(290.0, -0.01, 42.8e6)]   // Negative f
    public void CombustorExitT_K_NoCombustion_ReturnsT_in(double T_in, double f, double LHV)
    {
        Assert.Equal(T_in, HumphreyCyclePerformance.CombustorExitT_K(T_in, f, LHV));
    }

    [Fact]
    public void PeakChamberPressureRatio_V1Nominal_BelowAdvisoryThreshold()
    {
        // V-1 nominal: T_t2 ≈ 290 K, T_t4 ≈ 2800 K → T_ratio ≈ 9.66.
        // Closed-form: 1 + 0.05 · max(0, 9.66 − 1) = 1 + 0.433 = 1.433
        // Above the 1.30× advisory threshold, the gate fires for V-1 at high φ.
        double ratio = HumphreyCyclePerformance.PeakChamberPressureRatio(290.0, 2800.0);
        Assert.True(ratio > 1.0);
        Assert.True(ratio < 2.0, $"ratio {ratio} unexpectedly high");
    }

    [Fact]
    public void PeakChamberPressureRatio_NoCombustion_Returns1()
    {
        // T_in = T_out → ratio = 1.0 (no overpressure when there's no combustion).
        Assert.Equal(1.0, HumphreyCyclePerformance.PeakChamberPressureRatio(290.0, 290.0));
    }

    [Fact]
    public void PeakChamberPressureRatio_InvalidInputs_ReturnsNaN()
    {
        Assert.True(double.IsNaN(HumphreyCyclePerformance.PeakChamberPressureRatio(0.0, 1500.0)));
        Assert.True(double.IsNaN(HumphreyCyclePerformance.PeakChamberPressureRatio(290.0, double.NaN)));
        Assert.True(double.IsNaN(HumphreyCyclePerformance.PeakChamberPressureRatio(double.NaN, 1500.0)));
    }
}
