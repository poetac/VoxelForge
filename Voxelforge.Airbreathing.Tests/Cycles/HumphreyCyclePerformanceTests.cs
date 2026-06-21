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
        // cp = 1004.7 J/(kg·K). Mass-conserving balance (1+f divisor):
        // T_t4 = (290 + 0.99·0.06·42.8e6/1004.7) / 1.06 ≈ 2661 K
        double T_t4 = HumphreyCyclePerformance.CombustorExitT_K(
            T_in_K: 290.0, fuelAirMassFraction: 0.06, LHV_Jkg: 42.8e6);
        double expected = (290.0 + 0.99 * 0.06 * 42.8e6 / 1004.7) / 1.06;
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

    [Fact]
    public void CombustorExitT_K_ConservesMassWithFuelAddition()
    {
        // Mass-conserving combustor balance: (1+f)·cp·T_t4 = cp·T_t2 + f·η_b·LHV.
        // The old code omitted the (1+f) divisor (T_t4 = T_t2 + f·η_b·LHV/cp),
        // over-predicting T_t4 by a factor of (1+f) — the same defect as the RDE
        // solver and inconsistent with every constant-pressure sibling.
        const double T_t2 = 290.0, f = 0.06, lhv = 42.8e6;
        const double cp = HumphreyCyclePerformance.CpAir_JkgK;
        const double etaB = HumphreyCyclePerformance.CombustionEfficiency;

        double T_t4 = HumphreyCyclePerformance.CombustorExitT_K(T_t2, f, lhv);
        double lhs = (1.0 + f) * cp * T_t4;
        double rhs = cp * T_t2 + f * etaB * lhv;
        Assert.True(System.Math.Abs(lhs - rhs) <= 1e-6 * rhs,
            $"combustor energy balance violated: lhs={lhs:E6}, rhs={rhs:E6}, "
          + $"rel-err={System.Math.Abs(lhs - rhs) / rhs:E3}");
    }
}
