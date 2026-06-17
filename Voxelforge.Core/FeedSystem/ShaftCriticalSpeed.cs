// ShaftCriticalSpeed.cs — Shaft bending critical speed (first-mode
// whirl) advisory, the next-up rotordynamic companion to the
// wheel-rim stress check.
//
// What this models
// ────────────────
// The pump + turbine sit on a common shaft ("straddle mount" on FFSC
// separate shafts, "common shaft" on staged / GG / OE). Above a
// certain RPM the shaft itself starts to whirl — its first natural
// bending frequency is crossed and vibration amplitude spikes.
// Rocket-turbopump shafts are typically designed to run **subcritical**
// (RPM < 0.8·ω_n) or **supercritical** (RPM > 1.2·ω_n); operating
// within ±20 % of ω_n is a red flag for bearing loads and seal life.
//
// The model here is deliberately simple — a fixed-fixed uniform
// Euler-Bernoulli beam with Inconel 718 material constants. Real
// shafts have stepped diameters, overhung disks, gyroscopic coupling
// and fluid-film bearing stiffness — a full Campbell-diagram analysis
// is outside scope. We emit an **advisory** warning when RPM lands in
// the ±20 % whirl band; we do not seed a feasibility gate, mirroring
// the rim-stress advisory convention.
//
// First-mode natural frequency (Timoshenko, "Vibration Problems in
// Engineering" 4e §5.3, fixed-fixed uniform beam, β₁·L = 4.73004):
//
//     ω_n = (β₁/L)² · √(E·I / (ρ·A))
//         = (4.73/L)² · √(E·d² / (16·ρ))     for a solid circular shaft
//
// with I = π·d⁴/64 and A = π·d²/4 so EI/(ρA) = E·d²/(16·ρ).
// Converting to RPM: RPM_crit = 60·ω_n / (2·π).
//
// Geometry estimates
// ──────────────────
//   • Shaft length L = pump.TotalLength_mm + turbine.TotalLength_mm +
//     bearing margin (20 mm default) — roughly the span between the
//     outermost bearings.
//   • Shaft diameter d = 0.70 × min(2·impeller_hub_radius,
//     2·wheel_hub_radius). Real LRE shafts sit at ~60–80 % of the
//     impeller-hub OD to leave room for seals + sleeve bearings; 70 %
//     is Karassik §2.5 median.
//
// Material
// ────────
// Inconel 718, aged: E = 200 GPa, ρ = 8190 kg/m³ (ASM Handbook Vol 1,
// "Inconel 718"). Matches the wheel material assumed by the rim-stress
// check so shaft + wheel live in one coherent rotordynamic model.
//
// References
//   Timoshenko "Vibration Problems in Engineering" 4e §5.3 (Euler
//     uniform-beam eigenvalues).
//   Dimarogonas "Vibration for Engineers" 2e §8 (shaft whirl).
//   Karassik "Pump Handbook" 4e §2.5 (turbopump shaft proportions).
//   Sutton RPE 9e §10.4 (subcritical / supercritical operating bands).

using System;
using Voxelforge.Turbopump;

namespace Voxelforge.FeedSystem;

/// <summary>
/// Sprint 34 / PH-10 (2026-04-25): turbopump shaft mounting layout.
/// Selects the boundary condition for the first-mode bending eigenvalue
/// in <see cref="ShaftCriticalSpeed.Estimate"/>.
/// </summary>
public enum ShaftLayout
{
    /// <summary>
    /// Straddle / between-bearings mount. Both shaft ends are stiffly
    /// supported (β₁·L = 4.73). Typical of large LRE turbopumps where
    /// pump and turbine sit between a pair of outboard bearings.
    /// </summary>
    Straddled,
    /// <summary>
    /// Overhung / cantilevered mount. One end of the shaft hangs past
    /// the outermost bearing (β₁·L = 1.875). Typical of small Rutherford-
    /// class turbopumps. Critical speed drops by (4.73/1.875)² ≈ 6×
    /// versus straddled — a large, safety-relevant correction the
    /// pre-Sprint-34 code silently mismodelled.
    /// </summary>
    Overhung,
}

/// <summary>
/// First-mode shaft bending critical-speed advisory for one shaft
/// (fuel or ox). Produced by <see cref="ShaftCriticalSpeed.Estimate"/>;
/// attached to <see cref="TurbopumpResult.FuelShaft"/> / <see cref="TurbopumpResult.OxShaft"/>
/// when both the pump and turbine geometries are available.
/// </summary>
public sealed record ShaftCriticalSpeedResult(
    string Label,                       // "fuel" / "ox"
    double ShaftLength_mm,              // L — total span (pump + turbine + bearing margin)
    double ShaftDiameter_mm,            // d — estimated from min hub OD × 0.70
    double MaterialYoungsModulus_Pa,    // E — Inconel 718 aged
    double MaterialDensity_kgm3,        // ρ — Inconel 718 aged
    double FirstCriticalFrequency_Hz,   // f_n = ω_n / (2π) — isotropic; min(forward,backward) when split (PH-50)
    double FirstCriticalRpm,            // 60 · f_n — gates against this; min critical when split
    double OperatingRpm,                // pump.Rpm (imposed)
    double WhirlSafetyMargin,           // (RPM_crit − RPM_op) / RPM_crit; > 0 subcritical, < 0 supercritical
    bool   WhirlOk,                     // true if |margin| ≥ WhirlBandHalfWidth (0.20)
    string Notes,
    // Sprint 34 / PH-10 (2026-04-25): mounting layout that picked the BC.
    // Default Straddled to preserve back-compat for synthetic test
    // fixtures that build ShaftCriticalSpeedResult directly.
    ShaftLayout Layout = ShaftLayout.Straddled,
    // PH-50 (2026-04-29): asymmetric-bearing-stiffness whirl split. When
    // the caller specifies non-zero BearingAsymmetryRatio the natural
    // frequency splits into forward (higher) and backward (lower) modes:
    //     ω_forward  = ω_n · √(1 + ε)
    //     ω_backward = ω_n · √(1 − ε)
    // (Childs "Turbomachinery Rotordynamics" 1993 §5.4; Vance "Rotordynamics
    // of Turbomachinery" 1988 §3.3.) The gate keys on the lower of the two
    // — backward whirl is the safety-critical mode because it falls inside
    // the operating band first as RPM rises.
    //
    // Default 0 / FirstCriticalRpm preserves bit-for-bit isotropic
    // behavior for back-compat (existing fixtures don't supply asymmetry).
    double BearingAsymmetryRatio = 0.0,
    double ForwardCriticalRpm = 0.0,    // 60·f_n·√(1+ε); = FirstCriticalRpm when isotropic
    double BackwardCriticalRpm = 0.0);  // 60·f_n·√(1−ε); = FirstCriticalRpm when isotropic

/// <summary>
/// Pure-math shaft bending critical speed estimator. Deterministic,
/// thread-safe, no PicoGK / filesystem dependency. See file header for
/// model assumptions.
/// </summary>
public static class ShaftCriticalSpeed
{
    /// <summary>
    /// Young's modulus (Pa) for Inconel 718 aged condition at room T.
    /// ASM Metals Handbook Vol 1, "Inconel 718".
    /// </summary>
    public const double InconelYoungsModulus_Pa = 200.0e9;

    /// <summary>
    /// Density (kg/m³) for Inconel 718 aged condition. ASM Metals
    /// Handbook Vol 1, "Inconel 718". Slightly lower than the 8900
    /// kg/m³ used on the wheel-rim stress check (generic nickel-alloy
    /// figure) — kept separate so future shaft-material swaps are a
    /// one-constant change.
    /// </summary>
    public const double InconelDensity_kgm3 = 8190.0;

    /// <summary>
    /// First-mode eigenvalue for a fixed-fixed uniform Euler-Bernoulli
    /// beam: β₁·L = 4.73004. Timoshenko "Vibration Problems" 4e §5.3
    /// Table 2. Use for straddled-mount turbopump shafts (both ends
    /// stiffly supported between bearings).
    /// </summary>
    public const double FixedFixedBeta1_L = 4.73004;

    /// <summary>
    /// Sprint 34 / PH-10 (2026-04-25): first-mode eigenvalue for a
    /// fixed-free (cantilever) uniform Euler-Bernoulli beam: β₁·L =
    /// 1.87510. Timoshenko "Vibration Problems" 4e §5.3 Table 2.
    /// Use for overhung / cantilevered turbopump shafts where the pump
    /// (or turbine) hangs off one end past the outermost bearing —
    /// typical of small Rutherford-class turbopumps. Critical speed
    /// scales as β₁L² → ω_n drops by (4.73 / 1.875)² ≈ 6× vs straddled.
    /// </summary>
    public const double CantileverBeta1_L = 1.87510;

    /// <summary>
    /// Half-width of the whirl-danger band around ω_n, expressed as a
    /// fraction. Operating RPM within ±20 % of the first critical is
    /// flagged. Source: Sutton RPE 9e §10.4 (subcritical / supercritical
    /// operating guidance).
    /// </summary>
    public const double WhirlBandHalfWidth = 0.20;

    /// <summary>
    /// Shaft diameter / hub OD ratio. Karassik "Pump Handbook" 4e §2.5
    /// median — real LRE shafts sit at 60–80 % of impeller hub OD.
    /// </summary>
    public const double ShaftDiameterFraction = 0.70;

    /// <summary>
    /// Bearing + seal axial allowance (mm) added to the pump + turbine
    /// body lengths to approximate the span between outermost bearings.
    /// A single LRE turbopump typically has one bearing pair at each
    /// end of the rotor — 20 mm is a median allowance for seal + bearing
    /// stacks.
    /// </summary>
    public const double BearingMargin_mm = 20.0;

    /// <summary>
    /// Estimate the first-mode bending critical speed of one shaft and
    /// report a whirl-band advisory. Returns null when either the pump
    /// or the turbine geometry is unavailable (no shaft to size).
    /// </summary>
    /// <param name="label">"fuel" or "ox" — used in Notes + warnings.</param>
    /// <param name="pump">
    /// Sized pump geometry (<see cref="TurbopumpGeometry"/>). Must have
    /// positive <see cref="TurbopumpGeometry.ImpellerHubRadius_mm"/> and
    /// <see cref="TurbopumpGeometry.TotalLength_mm"/>.
    /// </param>
    /// <param name="turbine">
    /// Sized turbine geometry (<see cref="TurbineGeometry"/>). Must have
    /// positive <see cref="TurbineGeometry.WheelHubRadius_mm"/> and
    /// <see cref="TurbineGeometry.TotalLength_mm"/>.
    /// </param>
    /// <param name="operatingRpm">
    /// Imposed shaft speed (from <see cref="PumpSizing.Rpm"/>).
    /// </param>
    /// <summary>
    /// PH-50 (2026-04-29): cap on bearing-asymmetry ratio. ε = (k_x − k_y) /
    /// (k_x + k_y) for orthogonal bearing stiffness components. Real
    /// rolling-element bearings rarely exceed ε ≈ 0.5; values higher than
    /// that wander into nonlinear-bearing-stiffness territory the linear
    /// whirl split doesn't model. We clamp at 0.5 to keep the formula
    /// well-behaved.
    /// </summary>
    public const double MaxBearingAsymmetryRatio = 0.5;

    public static ShaftCriticalSpeedResult? Estimate(
        string label,
        TurbopumpGeometry? pump,
        TurbineGeometry? turbine,
        double operatingRpm,
        ShaftLayout layout = ShaftLayout.Straddled,
        double bearingAsymmetryRatio = 0.0)
    {
        if (pump is null || turbine is null) return null;
        if (pump.ImpellerHubRadius_mm <= 0 || turbine.WheelHubRadius_mm <= 0) return null;
        if (pump.TotalLength_mm <= 0 || turbine.TotalLength_mm <= 0) return null;
        if (operatingRpm <= 0) return null;

        // Shaft diameter: 70 % of the smaller of the two hub diameters.
        // Ensures the shaft actually fits through both hubs.
        double minHubDiameter_mm = Math.Min(
            2.0 * pump.ImpellerHubRadius_mm,
            2.0 * turbine.WheelHubRadius_mm);
        double shaftDiameter_mm = ShaftDiameterFraction * minHubDiameter_mm;

        // Shaft length: pump body + bearing margin + turbine body.
        double shaftLength_mm = pump.TotalLength_mm + BearingMargin_mm + turbine.TotalLength_mm;

        // Sprint 34 / PH-10 (2026-04-25): pick β₁L per layout. Straddled
        // (between-bearings, fixed-fixed): β₁L = 4.73. Overhung
        // (cantilevered, fixed-free): β₁L = 1.875. The cantilever case
        // drops ω_n by (4.73/1.875)² ≈ 6× vs straddled — a critical
        // safety-relevant correction for small turbopumps that hang the
        // pump or turbine off one end past the outermost bearing.
        double beta1_L = layout == ShaftLayout.Overhung
            ? CantileverBeta1_L
            : FixedFixedBeta1_L;

        // Uniform-beam first natural frequency:
        //   ω_n = (β₁/L)² · √(E·d² / (16·ρ))   with EI/(ρA) = E·d²/(16·ρ)
        double L_m = shaftLength_mm * 1e-3;
        double d_m = shaftDiameter_mm * 1e-3;
        double betaOverL = beta1_L / L_m;
        double omega_n = betaOverL * betaOverL
                       * Math.Sqrt(InconelYoungsModulus_Pa * d_m * d_m
                                 / (16.0 * InconelDensity_kgm3));
        double f_n_iso_Hz = omega_n / (2.0 * Math.PI);
        double rpm_iso = 60.0 * f_n_iso_Hz;

        // PH-50 (2026-04-29): split forward / backward criticals when the
        // caller specifies non-zero bearing asymmetry. The forward whirl
        // mode tracks the higher principal stiffness; the backward mode
        // tracks the lower. Backward is the safety-critical mode — it
        // crosses into the whirl band first as RPM rises. The gate keys
        // on the LOWER of the two so the existing WhirlSafetyMargin /
        // WhirlOk semantics still answer "is operating RPM safely outside
        // the danger band?".
        double eps = Math.Clamp(bearingAsymmetryRatio, 0.0, MaxBearingAsymmetryRatio);
        double rpm_forward  = rpm_iso * Math.Sqrt(1.0 + eps);
        double rpm_backward = rpm_iso * Math.Sqrt(1.0 - eps);
        // Gate-keying critical = min of the two. Equal to rpm_iso when
        // eps = 0 (back-compat with pre-PH-50 isotropic semantics).
        double rpm_crit = rpm_backward;
        double f_n_Hz   = rpm_crit / 60.0;

        double margin = rpm_crit > 0 ? (rpm_crit - operatingRpm) / rpm_crit : 0.0;
        bool whirlOk = Math.Abs(margin) >= WhirlBandHalfWidth;

        string band = margin > 0 ? "subcritical" : "supercritical";
        string layoutLabel = layout == ShaftLayout.Overhung
            ? "fixed-free (overhung) β₁L=1.875"
            : "fixed-fixed (straddled) β₁L=4.73";
        string asymmetryNote = eps > 0
            ? $", ε={eps:F2} (forward {rpm_forward:F0} / backward {rpm_backward:F0})"
            : string.Empty;
        string notes = $"{label} shaft: L={shaftLength_mm:F0} mm, d={shaftDiameter_mm:F1} mm, "
                     + $"Inconel-718 {layoutLabel}, RPM_crit={rpm_crit:F0}{asymmetryNote}, "
                     + $"RPM_op={operatingRpm:F0}, margin={margin * 100:F0}% ({band})"
                     + (whirlOk ? "." : " [WITHIN ±20% WHIRL BAND].");

        return new ShaftCriticalSpeedResult(
            Label:                     label,
            ShaftLength_mm:            shaftLength_mm,
            ShaftDiameter_mm:          shaftDiameter_mm,
            MaterialYoungsModulus_Pa:  InconelYoungsModulus_Pa,
            MaterialDensity_kgm3:      InconelDensity_kgm3,
            FirstCriticalFrequency_Hz: f_n_Hz,
            FirstCriticalRpm:          rpm_crit,
            OperatingRpm:              operatingRpm,
            WhirlSafetyMargin:         margin,
            WhirlOk:                   whirlOk,
            Notes:                     notes,
            Layout:                    layout,
            BearingAsymmetryRatio:     eps,
            ForwardCriticalRpm:        rpm_forward,
            BackwardCriticalRpm:       rpm_backward);
    }

    /// <summary>
    /// Format an advisory warning string for a shaft that lands in the
    /// whirl band. Returns null when the shaft is outside the band
    /// (no warning needed).
    /// </summary>
    public static string? FormatWarning(ShaftCriticalSpeedResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (result.WhirlOk) return null;

        string band = result.WhirlSafetyMargin > 0 ? "below" : "above";
        return $"{result.Label} shaft bending critical: RPM_crit={result.FirstCriticalRpm:F0} "
             + $"vs RPM_op={result.OperatingRpm:F0} ({result.WhirlSafetyMargin * 100:+0;-0}% margin, "
             + $"{band} critical, within ±{WhirlBandHalfWidth * 100:F0}% whirl band). "
             + $"Thicken shaft, shorten bearing span, or retune RPM to clear the band.";
    }
}
