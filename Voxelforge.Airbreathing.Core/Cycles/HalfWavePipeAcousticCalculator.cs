// HalfWavePipeAcousticCalculator.cs — pulsejet half-wave pipe acoustic mode
// (Wave-2 follow-on to PR #435, sub-step 1a.5 polish).
//
// Complements HelmholtzFrequencyCalculator (Foa §11.2) by implementing the
// half-wave open-pipe resonance that dominates in long-tail pulsejets such as
// the V-1 / Argus As 109-014. The Helmholtz lump treats the combustor as a
// spring + intake-neck as a mass; the pipe mode treats the entire tube length
// as a quarter-wave resonator closed at the reed-valve end and open at the
// tailpipe exit (Foa 1960 §11.3).
//
// Effective speed of sound: a wave traversing the resonant tube sees a
// temperature gradient (cold intake air → hot combustor exit). The effective
// c is the geometric mean of the endpoint sound speeds,
//   c_eff = √(c_cold · c_hot),
// which corresponds to the harmonic-mean travel time across a monotonic
// temperature gradient (Morse & Ingard §9.1; Foa §11.3 tabulation).
//
// V-1 calibration (sea-level static, JP-8, φ=0.95):
//   c_cold ≈ 340 m/s  (T_amb ≈ 288 K)
//   c_hot  ≈ 1103 m/s (T_t4 ≈ 3025 K from Humphrey cycle)
//   c_eff  ≈  612 m/s → f_QW = 612/(4·3.4) ≈ 45 Hz vs 47 Hz published (4.3 %)
//
// Combined estimator: linearly blends Helmholtz (f_H) and quarter-wave (f_QW)
// based on the dimensionless tube-length ratio
//   r = L_tube / √(V_comb / A_intake)
// with full quarter-wave dominance at r ≥ QuarterWaveDominanceRatio = 2.0
// (calibrated to NACA RM E50A04 V-1 instrumented-test data, per Foa §11.3).

using System;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Half-wave open-pipe acoustic frequency for long-tail pulsejets, plus a
/// combined Helmholtz + quarter-wave mode estimator. Static helper; no state.
/// </summary>
public static class HalfWavePipeAcousticCalculator
{
    /// <summary>
    /// Tube-length-to-Helmholtz-scale ratio above which the quarter-wave mode
    /// fully dominates. Calibrated to NACA RM E50A04 V-1 data per Foa 1960 §11.3.
    /// </summary>
    private const double QuarterWaveDominanceRatio = 2.0;

    /// <summary>
    /// Closed-open quarter-wave frequency <c>f = c / (4L)</c> per Foa §11.3.
    /// Models a tube closed at the reed-valve intake and open at the tailpipe exit.
    /// Returns NaN if either input is non-positive or non-finite.
    /// </summary>
    public static double ClosedOpenFrequency_Hz(double tubeLength_m, double speedOfSound_m_s)
    {
        if (tubeLength_m <= 0.0 || !double.IsFinite(tubeLength_m))
            return double.NaN;
        if (speedOfSound_m_s <= 0.0 || !double.IsFinite(speedOfSound_m_s))
            return double.NaN;
        return speedOfSound_m_s / (4.0 * tubeLength_m);
    }

    /// <summary>
    /// Open-open half-wave frequency <c>f = c / (2L)</c> — both ends open;
    /// theoretical upper bound on the pipe acoustic mode (Foa §11.3).
    /// Returns NaN if either input is non-positive or non-finite.
    /// </summary>
    public static double OpenOpenFrequency_Hz(double tubeLength_m, double speedOfSound_m_s)
    {
        if (tubeLength_m <= 0.0 || !double.IsFinite(tubeLength_m))
            return double.NaN;
        if (speedOfSound_m_s <= 0.0 || !double.IsFinite(speedOfSound_m_s))
            return double.NaN;
        return speedOfSound_m_s / (2.0 * tubeLength_m);
    }

    /// <summary>
    /// Combined Helmholtz + half-wave-pipe frequency estimator per Foa 1960 §11.3.
    /// <para>
    /// The Helmholtz component uses <paramref name="speedOfSoundCold_m_s"/>; the
    /// pipe-mode component uses <c>c_eff = √(c_cold · c_hot)</c> (geometric mean of
    /// intake and combustor-exit speeds of sound — effective tube-mean acoustic
    /// speed under a monotonic temperature gradient).
    /// </para>
    /// <para>
    /// Variant dispatch: <see cref="PulsejetVariant.Standard"/> (reed-valve, e.g.
    /// V-1 / Argus As 109-014) uses the closed-open quarter-wave mode <c>f = c/(4L)</c>.
    /// <see cref="PulsejetVariant.Valveless"/> (Lockwood-Hiller U-tube) has both ends
    /// open and uses the open-open half-wave mode <c>f = c/(2L)</c> per Foa §11.4.
    /// </para>
    /// <para>
    /// The blend weight <c>alpha = min(1, r / 2.0)</c> where
    /// <c>r = L / √(V_comb / A_intake)</c> smoothly transitions from Helmholtz
    /// (r ≪ 1, short-fat resonators) to pipe mode (r ≥ 2, long-thin tubes).
    /// </para>
    /// Returns NaN if any input is non-positive or non-finite.
    /// </summary>
    /// <param name="tubeLength_m">Total resonant tube length (m).</param>
    /// <param name="intakeArea_m2">Intake neck cross-sectional area (m²).</param>
    /// <param name="combustorVolume_m3">Combustor cavity volume (m³).</param>
    /// <param name="speedOfSoundCold_m_s">Speed of sound at intake/ambient temperature (m/s).</param>
    /// <param name="speedOfSoundHot_m_s">Speed of sound at combustor-exit temperature T_t4 (m/s).</param>
    /// <param name="variant">Pulsejet topology variant — selects closed-open vs open-open pipe mode.</param>
    public static double CombinedFrequency_Hz(
        double tubeLength_m,
        double intakeArea_m2,
        double combustorVolume_m3,
        double speedOfSoundCold_m_s,
        double speedOfSoundHot_m_s,
        PulsejetVariant variant = PulsejetVariant.Standard)
    {
        if (tubeLength_m <= 0.0       || !double.IsFinite(tubeLength_m))        return double.NaN;
        if (intakeArea_m2 <= 0.0      || !double.IsFinite(intakeArea_m2))       return double.NaN;
        if (combustorVolume_m3 <= 0.0 || !double.IsFinite(combustorVolume_m3))  return double.NaN;
        if (speedOfSoundCold_m_s <= 0.0 || !double.IsFinite(speedOfSoundCold_m_s)) return double.NaN;
        if (speedOfSoundHot_m_s <= 0.0  || !double.IsFinite(speedOfSoundHot_m_s))  return double.NaN;

        double f_H = HelmholtzFrequencyCalculator.Frequency_Hz(
            tubeLength_m, intakeArea_m2, combustorVolume_m3, speedOfSoundCold_m_s);

        double c_eff = Math.Sqrt(speedOfSoundCold_m_s * speedOfSoundHot_m_s);
        double f_pipe = variant == PulsejetVariant.Valveless
            ? OpenOpenFrequency_Hz(tubeLength_m, c_eff)     // Lockwood-Hiller U-tube: f = c/(2L)
            : ClosedOpenFrequency_Hz(tubeLength_m, c_eff);  // V-1 reed-valve:        f = c/(4L)

        double helmholtzScale = Math.Sqrt(combustorVolume_m3 / intakeArea_m2);
        double r     = tubeLength_m / helmholtzScale;
        double alpha = Math.Min(1.0, r / QuarterWaveDominanceRatio);

        return alpha * f_pipe + (1.0 - alpha) * f_H;
    }
}
