// AcousticDamper.cs — closed-form acoustic-damper resonance + damping-ratio
// model. Closes OOB-6 (#200): pairs with the existing ScreechModes /
// CroccoNTau outputs by adding a damping-ratio Δζ per chamber mode,
// turning the "red/yellow/green report" into an actionable "tune the
// damper to mode X" workflow.
//
// Sprint B-3 (2026-04-30). Two damper types ship in v1:
//
//   • Helmholtz resonator — neck + buried cavity drilled into the
//     chamber outer jacket; tuned to one chamber mode by sizing the
//     neck-area / cavity-volume product. Closed-form resonance:
//
//        f₀ = (c / 2π) · √( A_neck / (V_cavity · L_eff) )
//
//     where L_eff = L_neck + 0.85 · r_neck is the end-correction for
//     the unflanged opening (Rossing & Fletcher 1998, §2.4).
//
//   • Quarter-wave resonator — long thin closed cavity drilled radially
//     outward from the chamber bore. One end open (chamber-side), one
//     end closed. Closed-form resonance:
//
//        f₀ = c / (4 · L)
//
//     where L is the cavity length.
//
// The damping-ratio model is the Harrje & Reardon §8 simplification:
//   • Each resonator adds Δζ_peak = 0.04 at perfect tune (4 % damping
//     ratio, conservative anchor).
//   • Quality factor Q = 15 (representative HF damper); damping falls
//     off as a Lorentzian (1 + Q²·(f_d/f_m − 1)²)⁻¹ off-resonance.
//   • N coherent dampers combine as √min(N, 8) — the 8-cap reflects
//     the practical limit before mutual phase scrambling dominates.
//
// Output is **advisory** — the model is empirical and the tuning band
// is approximate. ScreechModesResult.Δζ values flow into FeasibilityGate
// as advisory `ACOUSTIC_DAMPER_*` violations, never as Hard rejects.
// Designers use the report to decide whether to retune; no SA hard-
// fail.
//
// References:
//   - Harrje & Reardon (eds), "Liquid Propellant Rocket Combustion
//     Instability", NASA SP-194 (1972), Ch. 8 — damper-tuning rules.
//   - Rossing & Fletcher, "Principles of Vibration and Sound",
//     Springer 1998, §2.4 — end-correction derivation.

using System;

namespace Voxelforge.Combustion.Stability;

/// <summary>Damper family selection. <see cref="None"/> = no damper
/// modelled; the chamber sees its bare modal damping.</summary>
public enum AcousticDamperType
{
    None       = 0,
    Helmholtz  = 1,
    QuarterWave = 2,
}

/// <summary>
/// Geometry inputs for one damper-array configuration. Hashed from the
/// damper-block on <see cref="Voxelforge.Optimization.RegenChamberDesign"/>.
/// All dimensions in mm; the evaluator converts to SI internally.
/// </summary>
public sealed record AcousticDamperConfig(
    AcousticDamperType Type,
    int    Count,                       // resonator count around chamber circumference
    // Helmholtz fields (zero on quarter-wave configs)
    double NeckArea_mm2,
    double NeckLength_mm,
    double CavityVolume_mm3,
    // Quarter-wave fields (zero on Helmholtz configs)
    double QuarterWaveLength_mm,
    double QuarterWaveDiameter_mm)
{
    /// <summary>Convenience constructor: Helmholtz array.</summary>
    public static AcousticDamperConfig Helmholtz(
        int count, double neckArea_mm2, double neckLength_mm, double cavityVolume_mm3)
        => new(AcousticDamperType.Helmholtz, count,
               neckArea_mm2, neckLength_mm, cavityVolume_mm3,
               0.0, 0.0);

    /// <summary>Convenience constructor: quarter-wave array.</summary>
    public static AcousticDamperConfig QuarterWave(
        int count, double length_mm, double diameter_mm)
        => new(AcousticDamperType.QuarterWave, count,
               0.0, 0.0, 0.0,
               length_mm, diameter_mm);

    /// <summary>Returns true when the config is well-formed enough to
    /// evaluate. Empty/None configs return false; a downstream caller
    /// should skip evaluation (the resonance is undefined).</summary>
    public bool IsActive => Type != AcousticDamperType.None
                          && Count > 0
                          && Type switch
                          {
                              AcousticDamperType.Helmholtz =>
                                  NeckArea_mm2 > 0 && NeckLength_mm >= 0
                               && CavityVolume_mm3 > 0,
                              AcousticDamperType.QuarterWave =>
                                  QuarterWaveLength_mm > 0
                               && QuarterWaveDiameter_mm > 0,
                              _ => false,
                          };

    /// <summary>Aggregate gas-volume occupied by the damper array,
    /// in mm³. Quarter-wave volume = N · π · (D/2)² · L; Helmholtz
    /// volume = N · (V_cavity + A_neck · L_neck).</summary>
    public double TotalVolume_mm3
    {
        get
        {
            if (!IsActive) return 0.0;
            return Type switch
            {
                AcousticDamperType.Helmholtz =>
                    Count * (CavityVolume_mm3 + NeckArea_mm2 * NeckLength_mm),
                AcousticDamperType.QuarterWave =>
                    Count * Math.PI * Math.Pow(QuarterWaveDiameter_mm * 0.5, 2.0) * QuarterWaveLength_mm,
                _ => 0.0,
            };
        }
    }
}

/// <summary>
/// Output bundle from <see cref="AcousticDamper.Evaluate"/>. Surfaces
/// the resonance frequency and per-mode damping-ratio Δζ for the L1,
/// T1, T2 chamber modes — the same triplet
/// <see cref="ScreechModeResult"/> reports.
/// </summary>
public sealed record AcousticDamperResult(
    AcousticDamperType Type,
    int                Count,
    double             ResonanceFrequency_Hz,
    /// <summary>Damping-ratio addition to the L1 chamber mode (dimensionless).
    /// Add to the bare modal ζ; Δζ ≈ 0.04 indicates strong damping.</summary>
    double             DampingRatio_L1,
    double             DampingRatio_T1,
    double             DampingRatio_T2,
    /// <summary>True when the damper's f₀ lands within ±10 % of any
    /// of L1 / T1 / T2. False indicates a detuned damper that
    /// contributes negligible damping.</summary>
    bool               IsTunedToAnyMode,
    /// <summary>Diagnostic reason string, e.g.
    /// "Helmholtz f₀ = 7240 Hz, tuned to T1 (7320 Hz) within 1.1 %".</summary>
    string             Notes);

public static class AcousticDamper
{
    /// <summary>Peak damping-ratio contribution per resonator at perfect
    /// tune. Conservative anchor from Harrje & Reardon §8 small-
    /// resonator data; production HF dampers can exceed this on a
    /// well-tuned mode but rarely drop below.</summary>
    public const double PeakDampingRatio_PerResonator = 0.04;

    /// <summary>Quality factor for the damper's frequency response.
    /// Q ≈ 15 spans the typical range (10–30) reported in HF damper
    /// rig data; the Lorentzian roll-off uses Q² · (Δf/f)².</summary>
    public const double QualityFactor = 15.0;

    /// <summary>Coherent-combination cap. Beyond this count the Σ damping
    /// saturates; the √N scaling assumption breaks down once mutual
    /// phase scrambling dominates.</summary>
    public const int CoherentCombiningCap = 8;

    /// <summary>Tuning band around any chamber mode. Inside the band a
    /// damper contributes ≥ 50 % of its peak damping ratio; outside,
    /// the Lorentzian roll-off is steep enough that the damper is
    /// effectively detuned. Drives <see cref="AcousticDamperResult.IsTunedToAnyMode"/>
    /// + the ACOUSTIC_DAMPER_DETUNED advisory gate.</summary>
    public const double TuningBandFraction = 0.10;

    /// <summary>
    /// Closed-form Helmholtz resonance frequency in Hz.
    ///
    /// f₀ = (c / 2π) · √(A_neck / (V_cavity · L_eff))
    ///
    /// where L_eff = L_neck + 0.85 · r_neck is the unflanged-end
    /// correction.
    /// </summary>
    public static double HelmholtzFrequency_Hz(
        double soundSpeed_ms,
        double neckArea_m2,
        double neckLength_m,
        double cavityVolume_m3)
    {
        if (neckArea_m2 <= 0 || cavityVolume_m3 <= 0) return 0.0;
        double r_neck = Math.Sqrt(neckArea_m2 / Math.PI);
        double L_eff  = Math.Max(neckLength_m, 0) + 0.85 * r_neck;
        if (L_eff <= 0) return 0.0;
        double ratio  = neckArea_m2 / (cavityVolume_m3 * L_eff);
        if (ratio <= 0) return 0.0;
        return soundSpeed_ms / (2.0 * Math.PI) * Math.Sqrt(ratio);
    }

    /// <summary>
    /// Closed-form quarter-wave resonance frequency in Hz.
    /// f₀ = c / (4 · L); one end open, one end closed.
    /// </summary>
    public static double QuarterWaveFrequency_Hz(
        double soundSpeed_ms,
        double cavityLength_m)
    {
        if (cavityLength_m <= 0) return 0.0;
        return soundSpeed_ms / (4.0 * cavityLength_m);
    }

    /// <summary>
    /// Per-mode damping-ratio contribution. Combines the peak-at-tune
    /// damping ratio with a Lorentzian roll-off off-resonance and a
    /// √N coherent-combination scaling capped at
    /// <see cref="CoherentCombiningCap"/>.
    /// </summary>
    public static double DampingRatioForMode(
        double damperFreq_Hz, double modeFreq_Hz, int resonatorCount)
    {
        if (damperFreq_Hz <= 0 || modeFreq_Hz <= 0 || resonatorCount <= 0)
            return 0.0;
        // Lorentzian envelope: Δζ(f) = Δζ_peak / (1 + Q² · (f_d/f_m − 1)²)
        double detune = damperFreq_Hz / modeFreq_Hz - 1.0;
        double envelope = 1.0 / (1.0 + QualityFactor * QualityFactor * detune * detune);
        // Coherent N-resonator scaling, saturated at the cap.
        int nEff = Math.Min(resonatorCount, CoherentCombiningCap);
        double coherent = Math.Sqrt(nEff);
        return PeakDampingRatio_PerResonator * envelope * coherent;
    }

    /// <summary>
    /// Evaluate the damper against a screech-mode triplet. Returns null
    /// when the config is inactive (no damper, zero count, or zero-volume
    /// fields) so callers can short-circuit cleanly.
    /// </summary>
    public static AcousticDamperResult? Evaluate(
        AcousticDamperConfig config,
        ScreechModeResult     screech)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.IsActive) return null;

        double c = screech.SoundSpeed_ms;
        double f0 = config.Type switch
        {
            AcousticDamperType.Helmholtz => HelmholtzFrequency_Hz(
                soundSpeed_ms: c,
                neckArea_m2:    config.NeckArea_mm2 * 1e-6,
                neckLength_m:   config.NeckLength_mm * 1e-3,
                cavityVolume_m3: config.CavityVolume_mm3 * 1e-9),
            AcousticDamperType.QuarterWave => QuarterWaveFrequency_Hz(
                soundSpeed_ms:  c,
                cavityLength_m: config.QuarterWaveLength_mm * 1e-3),
            _ => 0.0,
        };
        if (f0 <= 0)
        {
            // Degenerate inputs (negative dimension after migration / serializer
            // misconfiguration) — surface a non-null result with zero damping
            // so the gate can fire ACOUSTIC_DAMPER_DETUNED rather than hide
            // the configuration error behind a silent null.
            return new AcousticDamperResult(
                Type: config.Type,
                Count: config.Count,
                ResonanceFrequency_Hz: 0.0,
                DampingRatio_L1: 0.0,
                DampingRatio_T1: 0.0,
                DampingRatio_T2: 0.0,
                IsTunedToAnyMode: false,
                Notes: "Damper config produced f₀ = 0; check input geometry.");
        }

        double dzL1 = DampingRatioForMode(f0, screech.L1_Hz, config.Count);
        double dzT1 = DampingRatioForMode(f0, screech.T1_Hz, config.Count);
        double dzT2 = DampingRatioForMode(f0, screech.T2_Hz, config.Count);

        // Tuning band check: damper considered "tuned" when f₀ lands
        // within ±TuningBandFraction of any mode.
        bool tunedL1 = ModeWithinBand(f0, screech.L1_Hz, TuningBandFraction);
        bool tunedT1 = ModeWithinBand(f0, screech.T1_Hz, TuningBandFraction);
        bool tunedT2 = ModeWithinBand(f0, screech.T2_Hz, TuningBandFraction);
        bool tuned   = tunedL1 || tunedT1 || tunedT2;

        // Notes string identifies the closest mode in a single readable
        // line so the build sheet + report exporter can surface it
        // without re-deriving the comparison.
        var (closest, closestF) = ClosestMode(f0, screech);
        double err = Math.Abs(f0 - closestF) / Math.Max(closestF, 1.0);
        string notes = $"{config.Type} f₀ = {f0:F0} Hz, closest mode {closest} ({closestF:F0} Hz) " +
                       $"detune {err:P1}, ×{config.Count} resonators.";

        return new AcousticDamperResult(
            Type:                  config.Type,
            Count:                 config.Count,
            ResonanceFrequency_Hz: f0,
            DampingRatio_L1:       dzL1,
            DampingRatio_T1:       dzT1,
            DampingRatio_T2:       dzT2,
            IsTunedToAnyMode:      tuned,
            Notes:                 notes);
    }

    private static bool ModeWithinBand(double f0, double mode, double bandFraction)
    {
        if (mode <= 0) return false;
        double err = Math.Abs(f0 - mode) / mode;
        return err <= bandFraction;
    }

    private static (string Name, double Frequency_Hz) ClosestMode(
        double f0, ScreechModeResult screech)
    {
        double dL1 = Math.Abs(f0 - screech.L1_Hz);
        double dT1 = Math.Abs(f0 - screech.T1_Hz);
        double dT2 = Math.Abs(f0 - screech.T2_Hz);
        if (dL1 <= dT1 && dL1 <= dT2) return ("L1", screech.L1_Hz);
        if (dT1 <= dT2)               return ("T1", screech.T1_Hz);
        return ("T2", screech.T2_Hz);
    }
}
