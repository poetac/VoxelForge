// HalfWavePipeAcousticCalculatorTests.cs — Wave-2 pulsejet acoustic mode
// (follow-on to PR #435, sub-step 1a.5 polish).
// Unit tests for Foa 1960 §11.3 closed-open and combined mode estimators.

using System;
using Voxelforge.Airbreathing.Cycles;
using Xunit;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class HalfWavePipeAcousticCalculatorTests
{
    // ── closed-open f = c/(4L) ───────────────────────────────────────────────

    [Fact]
    public void ClosedOpenFrequency_Hz_MatchesFormula()
    {
        // c=700, L=3.5 → f = 700/(4·3.5) = 50 Hz exactly
        double f = HalfWavePipeAcousticCalculator.ClosedOpenFrequency_Hz(
            tubeLength_m: 3.5, speedOfSound_m_s: 700.0);
        Assert.Equal(700.0 / (4.0 * 3.5), f, precision: 10);
    }

    // ── open-open f = c/(2L) ─────────────────────────────────────────────────

    [Fact]
    public void OpenOpenFrequency_Hz_MatchesFormula()
    {
        // c=700, L=3.5 → f = 700/(2·3.5) = 100 Hz exactly
        double f = HalfWavePipeAcousticCalculator.OpenOpenFrequency_Hz(
            tubeLength_m: 3.5, speedOfSound_m_s: 700.0);
        Assert.Equal(700.0 / (2.0 * 3.5), f, precision: 10);
    }

    [Fact]
    public void ClosedOpen_IsHalfOf_OpenOpen_SameInputs()
    {
        // f_closed_open = c/(4L); f_open_open = c/(2L) → ratio = 2 always
        double fCO = HalfWavePipeAcousticCalculator.ClosedOpenFrequency_Hz(5.0, 600.0);
        double fOO = HalfWavePipeAcousticCalculator.OpenOpenFrequency_Hz(5.0, 600.0);
        Assert.Equal(fOO, 2.0 * fCO, precision: 10);
    }

    // ── combined mode estimator ───────────────────────────────────────────────

    [Fact]
    public void CombinedFrequency_ShortFat_YieldsHelmholtz()
    {
        // Short-fat geometry: L=0.10 m, A=0.01 m², V=0.10 m³
        // Helmholtz scale = √(0.10/0.01) = √10 ≈ 3.162
        // r = 0.10/3.162 ≈ 0.032 → alpha ≈ 0.016 → ~98 % Helmholtz weight.
        // f_H ≈ 54 Hz; f_QW = 340/(4·0.10) = 850 Hz (short tube → very high QW freq).
        // Combined should be Helmholtz-dominant: much closer to f_H than to f_QW.
        double f_comb = HalfWavePipeAcousticCalculator.CombinedFrequency_Hz(
            tubeLength_m:         0.10,
            intakeArea_m2:        0.01,
            combustorVolume_m3:   0.10,
            speedOfSoundCold_m_s: 340.0,
            speedOfSoundHot_m_s:  340.0);   // c_hot = c_cold isolates blend weight

        double f_H  = HelmholtzFrequencyCalculator.Frequency_Hz(0.10, 0.01, 0.10, 340.0);
        double f_QW = 340.0 / (4.0 * 0.10);   // 850 Hz

        // "Helmholtz dominates" ↔ result is closer to f_H than to f_QW.
        Assert.True(
            Math.Abs(f_comb - f_H) < Math.Abs(f_comb - f_QW),
            $"Short-fat: combined ({f_comb:G4} Hz) should be closer to f_H "
          + $"({f_H:G4} Hz) than f_QW ({f_QW:G4} Hz).");
    }

    [Fact]
    public void CombinedFrequency_LongThin_YieldsQuarterWave()
    {
        // Long-thin geometry: L=10 m, A=0.01 m², V=0.010 m³
        // Helmholtz scale = √(0.010/0.01) = 1.0
        // r = 10/1.0 = 10 → alpha = min(1, 10/2) = 1.0 → pure quarter-wave
        // c_eff = √(340·1103) (same as V-1 hot gas)
        double c_cold = 340.0;
        double c_hot  = 1103.0;
        double c_eff  = Math.Sqrt(c_cold * c_hot);
        double f_QW   = c_eff / (4.0 * 10.0);

        double f_comb = HalfWavePipeAcousticCalculator.CombinedFrequency_Hz(
            tubeLength_m:        10.0,
            intakeArea_m2:       0.01,
            combustorVolume_m3:  0.010,
            speedOfSoundCold_m_s: c_cold,
            speedOfSoundHot_m_s:  c_hot);

        Assert.Equal(f_QW, f_comb, precision: 10);
    }

    // ── variant dispatch: Valveless open-open (issue #449) ───────────────────

    [Fact]
    public void CombinedFrequency_Valveless_LongThin_YieldsOpenOpenMode()
    {
        // Long-thin geometry where pipe-mode dominates (alpha = 1.0).
        // Valveless variant should use open-open f = c/(2L) — exactly 2× the closed-open
        // closed-open f = c/(4L) the Standard variant uses.
        double c_cold = 340.0;
        double c_hot  = 1103.0;
        double c_eff  = Math.Sqrt(c_cold * c_hot);
        double fOpenOpen = c_eff / (2.0 * 10.0);

        double f_comb = HalfWavePipeAcousticCalculator.CombinedFrequency_Hz(
            tubeLength_m:        10.0,
            intakeArea_m2:       0.01,
            combustorVolume_m3:  0.010,
            speedOfSoundCold_m_s: c_cold,
            speedOfSoundHot_m_s:  c_hot,
            variant:             PulsejetVariant.Valveless);

        Assert.Equal(fOpenOpen, f_comb, precision: 10);
    }

    [Fact]
    public void CombinedFrequency_Valveless_IsExactlyTwiceStandard_LongThin()
    {
        // Same geometry, both variants. In the pure pipe-mode regime (alpha = 1) the
        // Valveless / Standard ratio is exactly 2 (open-open / closed-open).
        const double L = 10.0, A = 0.01, V = 0.010, cC = 340.0, cH = 1103.0;
        double fStandard = HalfWavePipeAcousticCalculator.CombinedFrequency_Hz(
            L, A, V, cC, cH, PulsejetVariant.Standard);
        double fValveless = HalfWavePipeAcousticCalculator.CombinedFrequency_Hz(
            L, A, V, cC, cH, PulsejetVariant.Valveless);

        Assert.Equal(2.0 * fStandard, fValveless, precision: 10);
    }

    [Fact]
    public void CombinedFrequency_DefaultVariant_IsStandard()
    {
        // Backward compatibility: omitted variant parameter must reproduce Standard
        // (closed-open) behaviour — protects existing callers from a silent regression.
        const double L = 10.0, A = 0.01, V = 0.010, cC = 340.0, cH = 1103.0;
        double fDefault  = HalfWavePipeAcousticCalculator.CombinedFrequency_Hz(L, A, V, cC, cH);
        double fStandard = HalfWavePipeAcousticCalculator.CombinedFrequency_Hz(
            L, A, V, cC, cH, PulsejetVariant.Standard);

        Assert.Equal(fStandard, fDefault, precision: 10);
    }

    [Fact]
    public void CombinedFrequency_V1Geometry_CloseTo47Hz()
    {
        // V-1 / Argus As 109-014 geometry with representative c_hot.
        // c_cold ≈ 340 m/s (sea-level ambient), c_hot ≈ 1103 m/s (T_t4 ≈ 3025 K).
        // Expected: ~45 Hz — within ±10% of published 47 Hz.
        double combustorVol = 0.075 * 0.80;  // CombustorArea_m2 × CombustorLength_m
        double f = HalfWavePipeAcousticCalculator.CombinedFrequency_Hz(
            tubeLength_m:        3.40,
            intakeArea_m2:       0.030,
            combustorVolume_m3:  combustorVol,
            speedOfSoundCold_m_s: 340.0,
            speedOfSoundHot_m_s:  1103.0);

        // Loose InRange guard (42–52 Hz) — tighter ±10% validated in the fixture test.
        Assert.InRange(f, 42.0, 52.0);
    }

    // ── NaN guard ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0,  700.0)]   // zero length
    [InlineData(-1.0, 700.0)]   // negative length
    [InlineData(3.5,  0.0)]     // zero c
    [InlineData(3.5,  -1.0)]    // negative c
    [InlineData(double.NaN, 700.0)]
    [InlineData(3.5, double.NaN)]
    public void ClosedOpenFrequency_NonPositiveOrNaN_ReturnsNaN(double L, double c)
    {
        Assert.True(double.IsNaN(HalfWavePipeAcousticCalculator.ClosedOpenFrequency_Hz(L, c)));
    }

    [Theory]
    [InlineData(0.0,  0.030, 0.060, 340.0, 1100.0)]   // zero length
    [InlineData(3.40, 0.0,   0.060, 340.0, 1100.0)]   // zero intake area
    [InlineData(3.40, 0.030, 0.0,   340.0, 1100.0)]   // zero volume
    [InlineData(3.40, 0.030, 0.060, 0.0,   1100.0)]   // zero c_cold
    [InlineData(3.40, 0.030, 0.060, 340.0, 0.0)]      // zero c_hot
    [InlineData(double.NaN, 0.030, 0.060, 340.0, 1100.0)]
    public void CombinedFrequency_NonPositiveOrNaN_ReturnsNaN(
        double L, double A, double V, double cCold, double cHot)
    {
        Assert.True(double.IsNaN(
            HalfWavePipeAcousticCalculator.CombinedFrequency_Hz(L, A, V, cCold, cHot)));
    }

    // ── determinism ──────────────────────────────────────────────────────────

    [Fact]
    public void Frequency_Deterministic()
    {
        double combustorVol = 0.075 * 0.80;
        double f1 = HalfWavePipeAcousticCalculator.CombinedFrequency_Hz(
            3.40, 0.030, combustorVol, 340.0, 1103.0);
        double f2 = HalfWavePipeAcousticCalculator.CombinedFrequency_Hz(
            3.40, 0.030, combustorVol, 340.0, 1103.0);
        Assert.Equal(f1, f2);
    }
}
