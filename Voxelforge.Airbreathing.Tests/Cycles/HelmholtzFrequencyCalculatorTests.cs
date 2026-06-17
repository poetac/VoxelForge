// HelmholtzFrequencyCalculatorTests.cs — Wave 1 PR-4 (sub-step 1a.5).
// Closed-form unit tests for Foa 1960 §11.2 eq 11-3.

using System;
using Voxelforge.Airbreathing.Cycles;
using Xunit;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class HelmholtzFrequencyCalculatorTests
{
    [Fact]
    public void Frequency_HzMatchesClosedForm_StandardInputs()
    {
        // f = (c / 2π) · √(A / (V·L))
        // For c=340, A=0.030, V=0.060, L=3.40 the closed-form gives
        //   340 / (2π) · √(0.030 / (0.060 · 3.40))
        // = 54.115 · √(0.147059) = 54.115 · 0.38346 ≈ 20.74 Hz
        double f = HelmholtzFrequencyCalculator.Frequency_Hz(
            tubeLength_m:        3.40,
            intakeArea_m2:       0.030,
            combustorVolume_m3:  0.060,
            speedOfSound_m_s:    340.0);
        double expected = 340.0 / (2.0 * Math.PI) * Math.Sqrt(0.030 / (0.060 * 3.40));
        Assert.Equal(expected, f, precision: 6);
    }

    [Theory]
    [InlineData(0.0, 0.030, 0.060, 340.0)]    // Zero tube length
    [InlineData(3.40, 0.0, 0.060, 340.0)]     // Zero intake area
    [InlineData(3.40, 0.030, 0.0, 340.0)]     // Zero volume
    [InlineData(3.40, 0.030, 0.060, 0.0)]     // Zero speed of sound
    [InlineData(-1.0, 0.030, 0.060, 340.0)]   // Negative tube length
    [InlineData(double.NaN, 0.030, 0.060, 340.0)]  // NaN tube length
    public void Frequency_NonPositiveOrNaNInputs_ReturnsNaN(
        double L, double A, double V, double c)
    {
        Assert.True(double.IsNaN(HelmholtzFrequencyCalculator.Frequency_Hz(L, A, V, c)));
    }

    [Fact]
    public void Frequency_Deterministic()
    {
        // Two invocations with identical inputs must produce bit-identical results.
        double f1 = HelmholtzFrequencyCalculator.Frequency_Hz(3.40, 0.030, 0.060, 340.0);
        double f2 = HelmholtzFrequencyCalculator.Frequency_Hz(3.40, 0.030, 0.060, 340.0);
        Assert.Equal(f1, f2);
    }
}
