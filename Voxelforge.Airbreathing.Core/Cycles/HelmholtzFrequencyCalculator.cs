// HelmholtzFrequencyCalculator.cs — pulsejet acoustic-resonance helper
// (Wave 1 PR-4, sub-step 1a.5).
//
// Closed-form Helmholtz-resonator frequency for a pulsejet's combustor +
// intake "neck" lump. The pulsejet acts as a self-driven Helmholtz
// resonator: the combustor cavity acts as the spring; the intake horn is
// the neck mass; the resonance frequency couples to the combustion timing
// to produce the characteristic buzz.
//
// Approximation note: real valveless pulsejets (V-1 buzz bomb at ~45 Hz)
// also exhibit half-wave open-pipe acoustic dynamics that the Helmholtz
// lumped model doesn't capture. For V-1-class long-tail geometry, the
// measured buzz frequency can be 1.5-2× the Helmholtz prediction. This
// helper is a first-order estimate per Foa 1960 §11.2 eq 11-3; a
// follow-on sprint may add a tube-acoustic complement when CFD or
// instrumented test data justifies the additional model complexity.

using System;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Closed-form Helmholtz-resonator frequency for a pulsejet combustor +
/// intake-neck geometry. Static helper; no state.
/// </summary>
public static class HelmholtzFrequencyCalculator
{
    /// <summary>
    /// Helmholtz-resonator frequency
    /// <c>f = (c / 2π) · √(A_neck / (V · L_neck))</c>
    /// per Foa 1960 §11.2 eq 11-3. Returns NaN if any input is non-positive
    /// or non-finite.
    /// </summary>
    /// <param name="tubeLength_m">
    /// Effective neck length (m). For a valveless pulsejet, the intake-horn
    /// + diffuser axial length acts as the lumped neck mass.
    /// </param>
    /// <param name="intakeArea_m2">
    /// Neck cross-sectional area (m²) — the forward-firing diffuser intake
    /// area on a valveless geometry.
    /// </param>
    /// <param name="combustorVolume_m3">
    /// Cavity volume (m³) — the combustor segment, V = A_combustor × L_combustor.
    /// </param>
    /// <param name="speedOfSound_m_s">
    /// Speed of sound in the cavity gas (m/s). For cold-flow pre-combustion
    /// estimates use ambient c ≈ 340 m/s; for hot-cycle estimates blend
    /// with the burnt-gas value.
    /// </param>
    public static double Frequency_Hz(
        double tubeLength_m,
        double intakeArea_m2,
        double combustorVolume_m3,
        double speedOfSound_m_s)
    {
        if (tubeLength_m <= 0.0 || intakeArea_m2 <= 0.0 || combustorVolume_m3 <= 0.0)
            return double.NaN;
        if (speedOfSound_m_s <= 0.0 || double.IsNaN(speedOfSound_m_s))
            return double.NaN;
        return speedOfSound_m_s / (2.0 * Math.PI)
             * Math.Sqrt(intakeArea_m2 / (combustorVolume_m3 * tubeLength_m));
    }
}
