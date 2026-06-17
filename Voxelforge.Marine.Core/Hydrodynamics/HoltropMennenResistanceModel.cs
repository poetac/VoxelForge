// HoltropMennenResistanceModel.cs — Sprint M.W4 simplified Holtrop-Mennen
// 1984 resistance fit for displacement-mode surface hulls.
//
// Stateless, allocation-free, deterministic. Computes total bare-hull
// resistance R_T = R_F + R_W (+ R_appendage residuum) for a displacement-
// mode round-bilge hull operating at sub-planing Froude numbers (Fn ≲ 0.4).
//
// The full Holtrop-Mennen 1984 / 1988 / 1995 model has 8 separate
// resistance components plus extensive parametric polynomials in 8+
// hull-form coefficients (Cb, Cp, Cm, LCB, iE, etc.). This module ships
// a SIMPLIFIED form suitable for parametric design exploration:
//
//   R_F  — ITTC-1957 skin friction (canonical, no simplification)
//   1+k₁ — form factor from Holtrop's polynomial in (Cb, B/L, T/L, ...)
//          simplified to 1 + 0.93·(B/L)·(T/L)·Cb^1.0686 as the dominant
//          term per Holtrop 1984 eq 11.
//   R_W  — wave-making resistance, simplified to Holtrop's dominant term
//          c₁·∇·ρ·g·exp(m₁·Fn^d), with c₁, m₁, d cluster-anchored.
//   R_appendage — lumped 5 % of (R_F · (1+k₁)) for bilge keels / shafts /
//          rudder. Real Holtrop has per-appendage form factors.
//
// Sources:
//   Holtrop J., Mennen G.G.J. (1982). "An Approximate Power Prediction
//     Method." International Shipbuilding Progress 29.
//   Holtrop J. (1984). "A Statistical Re-Analysis of Resistance and
//     Propulsion Data." International Shipbuilding Progress 31.
//   Watson D.G.M. (1998). "Practical Ship Design." Elsevier — Chap 6
//     (simplified Holtrop charts).
//
// Validation tolerance per ADR-029 D4 generalised: ±25 % resistance
// across the cluster envelope (vs measured tank-test data). Wide because
// the simplified form drops appendage form factors, transom resistance,
// bulbous-bow corrections, and air resistance.

using System;

namespace Voxelforge.Marine.Hydrodynamics;

/// <summary>
/// Output of the simplified Holtrop-Mennen displacement-hull resistance
/// model. Pure data; no reference to PicoGK or any I/O surface.
/// </summary>
/// <param name="FroudeNumber">Fn = V / √(g · LWL) [-].</param>
/// <param name="ReynoldsNumber">Re = V · LWL / ν [-].</param>
/// <param name="FormFactor">1 + k₁ [-] — Holtrop dominant-term form factor.</param>
/// <param name="FrictionResistance_N">R_F (ITTC-1957) [N].</param>
/// <param name="WaveMakingResistance_N">R_W (Holtrop dominant term) [N].</param>
/// <param name="AppendageResistance_N">R_app (lumped 5 % of viscous form) [N].</param>
/// <param name="TotalResistance_N">R_T = R_F·(1+k₁) + R_W + R_app [N].</param>
/// <param name="WettedSurfaceArea_m2">S_wet from Mumford's formula (closed-form) [m²].</param>
/// <param name="DisplacedVolume_m3">∇ = Δ / ρ_water [m³].</param>
/// <param name="SemiDisplacementReductionFactor">
/// Sprint M.W5. Multiplicative reduction applied to the displacement-only
/// wave-making term to capture the dynamic-lift mitigation in the semi-
/// displacement Froude band. 1.0 when SD correction is disabled or when
/// Fn ≤ 0.30; smoothly drops to <c>1.0 − SemiDisplacementMaxReduction</c>
/// (= 0.60 at the cluster anchors) at Fn = 0.55.
/// </param>
public sealed record HoltropMennenResult(
    double FroudeNumber,
    double ReynoldsNumber,
    double FormFactor,
    double FrictionResistance_N,
    double WaveMakingResistance_N,
    double AppendageResistance_N,
    double TotalResistance_N,
    double WettedSurfaceArea_m2,
    double DisplacedVolume_m3,
    double SemiDisplacementReductionFactor = 1.0);

/// <summary>
/// Simplified Holtrop-Mennen 1984 resistance model for displacement-mode
/// round-bilge surface hulls. Mirror of
/// <see cref="SavitskyPlaningModel"/> for the displacement regime.
/// </summary>
public static class HoltropMennenResistanceModel
{
    /// <summary>Standard gravity [m/s²].</summary>
    public const double g0 = 9.80665;

    /// <summary>
    /// Lumped appendage-resistance fraction (5 % of R_F·(1+k₁)). Real
    /// Holtrop applies per-appendage form factors; this is the cluster-
    /// mid-band approximation for bilge keels + shafts + rudder + struts.
    /// </summary>
    public const double AppendageResistanceFraction = 0.05;

    // ── Wave-making cluster-fit constants ───────────────────────────────

    /// <summary>
    /// Holtrop dominant-term wave-making coefficient c₁ [-]. Cluster mid-
    /// band 1.0e-3 (calibrated against the Watson 1998 chart 6.4
    /// recommendation that wave-making is ~30 % of R_T for a coastal
    /// cargo at Fn=0.26). The simplified form drops the full Holtrop c₁
    /// polynomial in (B/T, iE, …); see VALIDATION-NOTES.md.
    /// </summary>
    public const double WaveMakingCoefficientC1 = 1.0e-3;

    /// <summary>
    /// Holtrop wave-making exponent constant m₁ [-]. Cluster mid-band 4.5
    /// (Watson 1998).
    /// </summary>
    public const double WaveMakingExponentM1 = 4.5;

    /// <summary>
    /// Holtrop wave-making Froude exponent d [-]. Cluster mid-band 2.0
    /// (square-law dependence on Fn within the displacement regime).
    /// </summary>
    public const double WaveMakingFroudeExponent = 2.0;

    // ── Sprint M.W5 — semi-displacement transition constants ──────────────

    /// <summary>
    /// Sprint M.W5. Below this Froude number the semi-displacement
    /// correction is a no-op (the model reverts to bit-identical Sprint
    /// M.W4 displacement-only behaviour).
    /// </summary>
    public const double SemiDisplacementOnsetFn = 0.30;

    /// <summary>
    /// Sprint M.W5. Above this Froude number even the SD correction loses
    /// fidelity; gated as hard ceiling when SD is enabled.
    /// </summary>
    public const double SemiDisplacementCeilingFn = 0.55;

    /// <summary>
    /// Sprint M.W5. Maximum SD wave-making reduction factor — the dynamic-
    /// lift transfer reduces R_W by up to 40 % at the SD ceiling Fn = 0.55,
    /// matching the cluster mid-band for semi-displacement hulls reported
    /// in Watson 1998 chap 6 and Holtrop 1984 eq 14 high-Fn fit. Smaller
    /// (less reduction) at lower Fn via a quadratic blend in t = (Fn -
    /// 0.30)/0.25.
    /// </summary>
    public const double SemiDisplacementMaxReduction = 0.40;

    /// <summary>
    /// Solve the simplified Holtrop-Mennen resistance.
    /// </summary>
    /// <param name="speed_ms">Vessel speed V [m/s].</param>
    /// <param name="lengthWaterline_m">LWL [m].</param>
    /// <param name="beamWaterline_m">B [m].</param>
    /// <param name="draft_m">T [m].</param>
    /// <param name="blockCoefficient">C_b ∈ [0.40, 0.85] [-].</param>
    /// <param name="massDisplacement_kg">Δ [kg].</param>
    /// <param name="waterDensity_kgm3">ρ_water [kg/m³].</param>
    /// <param name="kinematicViscosity_m2s">ν [m²/s].</param>
    /// <param name="enableSemiDisplacementCorrection">
    /// Sprint M.W5. When true, applies the semi-displacement Froude-band
    /// correction on R_W for Fn ∈ [0.30, 0.55]. When false (default), the
    /// solver behaves bit-identically to Sprint M.W4.
    /// </param>
    /// <returns>Solved resistance state.</returns>
    public static HoltropMennenResult Solve(
        double speed_ms,
        double lengthWaterline_m,
        double beamWaterline_m,
        double draft_m,
        double blockCoefficient,
        double massDisplacement_kg,
        double waterDensity_kgm3,
        double kinematicViscosity_m2s,
        bool enableSemiDisplacementCorrection = false)
    {
        if (speed_ms <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed_ms),
                $"Speed_ms must be positive; got {speed_ms}.");
        if (lengthWaterline_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(lengthWaterline_m),
                $"LWL must be positive; got {lengthWaterline_m}.");
        if (beamWaterline_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(beamWaterline_m),
                $"BeamWaterline_m must be positive; got {beamWaterline_m}.");
        if (draft_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(draft_m),
                $"Draft_m must be positive; got {draft_m}.");
        if (blockCoefficient < 0.40 || blockCoefficient > 0.85)
            throw new ArgumentOutOfRangeException(nameof(blockCoefficient),
                $"BlockCoefficient must be in [0.40, 0.85]; got {blockCoefficient}.");
        if (massDisplacement_kg <= 0)
            throw new ArgumentOutOfRangeException(nameof(massDisplacement_kg),
                $"MassDisplacement_kg must be positive; got {massDisplacement_kg}.");
        if (waterDensity_kgm3 <= 0)
            throw new ArgumentOutOfRangeException(nameof(waterDensity_kgm3),
                $"WaterDensity_kgm3 must be positive; got {waterDensity_kgm3}.");
        if (kinematicViscosity_m2s <= 0)
            throw new ArgumentOutOfRangeException(nameof(kinematicViscosity_m2s),
                $"KinematicViscosity_m2s must be positive; got {kinematicViscosity_m2s}.");

        double V    = speed_ms;
        double L    = lengthWaterline_m;
        double B    = beamWaterline_m;
        double T    = draft_m;
        double Cb   = blockCoefficient;
        double rho  = waterDensity_kgm3;
        double nu   = kinematicViscosity_m2s;
        double Delta = massDisplacement_kg;

        // 1. Volume + Froude + Reynolds.
        double volume_m3 = Delta / rho;
        double Fn = V / Math.Sqrt(g0 * L);
        double Re = V * L / nu;

        // 2. Wetted surface area — Mumford's empirical formula (cleaner
        //    than the full Holtrop S_wet polynomial, ±5 % for typical
        //    displacement hulls):
        //      S_wet ≈ 1.025 · L · (Cb · B + 1.7 · T)
        double S_wet = 1.025 * L * (Cb * B + 1.7 * T);

        // 3. ITTC-1957 friction.
        double C_F = Re > 1e3
            ? 0.075 / Math.Pow(Math.Log10(Re) - 2.0, 2.0)
            : 0.075;
        double R_F = 0.5 * rho * V * V * S_wet * C_F;

        // 4. Form factor — simplified parametric form anchored to the
        //    Holtrop 1984 cluster envelope (1+k₁) ∈ [1.10, 1.30] for
        //    round-bilge displacement hulls. Real Holtrop has multiplicative
        //    c₁₂ + c₁₃ corrections that pull the asymptote to 1.10; this
        //    simplification baselines at 1.10 and adds geometric +
        //    block-coefficient sensitivity above that. Exponents 0.92 + 0.52
        //    on B/L and T/L track Holtrop 1984 eq. 11 (the simplified term
        //    inside the brackets).
        double formFactor = 1.10
            + 0.93
              * Math.Pow(B / L, 0.92)
              * Math.Pow(T / L, 0.52)
              * Math.Pow(Cb, 1.0686);

        // 5. Wave-making resistance, simplified Holtrop dominant term.
        //    R_W_disp = c₁ · ∇ · ρ · g · exp(m₁ · Fn^d)
        //    The exp(m₁ Fn^d) term captures the steep rise of wave-making
        //    drag as Fn approaches 0.4 (the hump speed). For Fn > 0.4 the
        //    formula extrapolates beyond its validity envelope; the gate
        //    HOLTROP_FROUDE_OUT_OF_BAND flags this when SD correction is
        //    disabled.
        double R_W_disp = WaveMakingCoefficientC1
                        * volume_m3 * rho * g0
                        * Math.Exp(WaveMakingExponentM1
                                   * Math.Pow(Fn, WaveMakingFroudeExponent));

        // 5b. Sprint M.W5 — semi-displacement transition correction.
        //     Apply a quadratic-in-t multiplier on R_W_disp for Fn ∈
        //     [0.30, 0.55] when the SD-correction flag is enabled. The
        //     reduction captures the dynamic-lift transfer observed for
        //     semi-displacement hulls (Watson 1998 chap 6; Holtrop 1984
        //     eq 14 high-Fn fit). Cluster anchor: ~40 % reduction at the
        //     SD ceiling Fn = 0.55.
        double sdReductionFactor = ComputeSemiDisplacementReductionFactor(
            Fn, enableSemiDisplacementCorrection);
        double R_W = R_W_disp * sdReductionFactor;

        // 6. Appendage resistance lumped at 5 % of viscous-form-corrected
        //    friction (real Holtrop has per-appendage form factors).
        double R_app = AppendageResistanceFraction * R_F * formFactor;

        // 7. Total resistance.
        double R_total = R_F * formFactor + R_W + R_app;

        return new HoltropMennenResult(
            FroudeNumber:                     Fn,
            ReynoldsNumber:                   Re,
            FormFactor:                       formFactor,
            FrictionResistance_N:             R_F,
            WaveMakingResistance_N:           R_W,
            AppendageResistance_N:            R_app,
            TotalResistance_N:                R_total,
            WettedSurfaceArea_m2:             S_wet,
            DisplacedVolume_m3:               volume_m3,
            SemiDisplacementReductionFactor:  sdReductionFactor);
    }

    /// <summary>
    /// Compute the Sprint M.W5 semi-displacement wave-making reduction
    /// factor. Public static helper for tests + future high-Fn fits.
    /// </summary>
    /// <param name="froudeNumber">Fn [-] from the resistance solve.</param>
    /// <param name="enableSemiDisplacementCorrection">
    /// Whether the SD correction is active. When false, always returns 1.0
    /// (no reduction; bit-identical Sprint M.W4 behaviour).
    /// </param>
    /// <returns>
    /// Multiplicative factor on the displacement-only R_W. 1.0 means no
    /// reduction; <c>1.0 − SemiDisplacementMaxReduction</c> = 0.60 is the
    /// floor at Fn = 0.55. Linear quadratic blend in t = (Fn − 0.30)/0.25
    /// clamped to [0, 1] for Fn ∈ [0.30, 0.55]; below 0.30 returns 1.0;
    /// above 0.55 returns the floor (clamped).
    /// </returns>
    public static double ComputeSemiDisplacementReductionFactor(
        double froudeNumber,
        bool   enableSemiDisplacementCorrection)
    {
        if (!enableSemiDisplacementCorrection) return 1.0;
        if (froudeNumber <= SemiDisplacementOnsetFn) return 1.0;
        double t = (froudeNumber - SemiDisplacementOnsetFn)
                 / (SemiDisplacementCeilingFn - SemiDisplacementOnsetFn);
        if (t > 1.0) t = 1.0;
        return 1.0 - SemiDisplacementMaxReduction * t * t;
    }
}
