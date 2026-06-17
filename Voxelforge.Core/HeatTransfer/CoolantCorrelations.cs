// CoolantCorrelations.cs — Coolant-side heat transfer & friction correlations.
//
// • Dittus–Boelter (1930): Nu = 0.023 Re^0.8 Pr^0.4 (heating).
//     Base case for smooth turbulent tube flow at bulk properties.
//
// • Sieder–Tate property correction: multiply by (μ_b/μ_w)^0.14.
//     Accounts for viscosity variation across the boundary layer.
//
// • Pizzarelli-style supercritical correction (optional):
//     Nu = 0.0185 Re^0.82 Pr^0.4 (ρ_w/ρ_b)^0.1
//     Use when near-pseudocritical operation is expected.  Reduces the
//     overshoot in predicted h that Dittus-Boelter can produce near T_pc.
//
// • Sprint 33 / PH-6 (2026-04-24): Dravid (1971) Dean-number Nu enhancement
//     for helical / curved channels:
//       Nu_curved/Nu_straight = 1 + 3.6·(1 − D_h/D_curv)·(D_h/D_curv)^0.5
//     where D_curv is the diameter of the coil cylinder. For a helix on a
//     chamber wall of radius r at pitch angle α, R_curv = r / sin²α (so
//     α=0 collapses to straight-tube, multiplier=1). Without this term
//     helical channels under-predict h_c by ~20 % at α > 5°.
//
// • Petukhov (1970): f = (0.790 ln(Re) - 1.64)^(-2) for Darcy friction
//     factor, SMOOTH turbulent flow.
//
// • Sprint 33 / PH-7 (2026-04-24): Haaland (1983) friction factor with
//     relative roughness:
//       1/√f = −1.8·log₁₀((ε/(3.7·D))^1.11 + 6.9/Re)
//     LPBF-printed channels run at ε/D ≈ 0.01-0.05 (Strauss et al. 2018);
//     smooth-tube Petukhov under-predicts f by 2-4× in this regime,
//     silently passing designs that would need impossible tank pressure.
//     The two-arg overload <see cref="FrictionFactor(double, double)"/>
//     handles roughness; the legacy one-arg form remains Petukhov for
//     back-compat with synthetic-fixture tests.
//
// Validity:  4000 < Re < 5×10⁶, 0.5 < Pr < 100, L/D > 10.
// Expect ±20 % on h_c for well-behaved supercritical methane far from T_pc,
// ±40 % inside the pseudocritical band.

using Voxelforge.Coolant;

namespace Voxelforge.HeatTransfer;

public enum CoolantCorrelationKind
{
    DittusBoelter,
    SiederTate,
    SupercriticalPizzarelli
}

/// <summary>
/// Sprint 16 / Track J / P5 (2026-04-22): pre-computed Reynolds /
/// Prandtl scaling factors that are invariant across the wall-T
/// iteration loop at a given station. Build once via
/// <see cref="CoolantCorrelations.ComputeNusseltFactors"/> then thread
/// into the new <see cref="CoolantCorrelations.HeatTransferCoefficient(in CoolantNusseltFactors, in CoolantState, in CoolantState, CoolantCorrelationKind)"/>
/// overload to skip 14 redundant <c>Math.Pow(Re, …)</c> +
/// <c>Math.Pow(Pr, 0.4)</c> calls per station — the per-iter dependency
/// is only on <c>wallState</c>, which appears in the
/// <c>(μ_b/μ_w)^0.14</c> Sieder-Tate or <c>(ρ_w/ρ_b)^0.1</c> Pizzarelli
/// tail, not in the Re/Pr factor.
/// </summary>
public readonly struct CoolantNusseltFactors
{
    public readonly double Re_0p8;          // Math.Pow(Re, 0.8) — Dittus-Boelter / Sieder-Tate
    public readonly double Re_0p82;         // Math.Pow(Re, 0.82) — Pizzarelli
    public readonly double Pr_0p4;          // Math.Pow(Pr, 0.4) — all three correlations
    public readonly double BulkConductivity_WmK;
    public readonly double HydraulicDiameter_m;

    public CoolantNusseltFactors(
        double re_0p8, double re_0p82, double pr_0p4,
        double bulkConductivity_WmK, double hydraulicDiameter_m)
    {
        Re_0p8                = re_0p8;
        Re_0p82               = re_0p82;
        Pr_0p4                = pr_0p4;
        BulkConductivity_WmK  = bulkConductivity_WmK;
        HydraulicDiameter_m   = hydraulicDiameter_m;
    }

    /// <summary>True when the factors carry usable values (non-zero D_h).</summary>
    public bool IsValid => HydraulicDiameter_m > 0;
}

public static class CoolantCorrelations
{
    /// <summary>
    /// Coolant-side heat transfer coefficient h_c [W/(m²·K)].
    /// </summary>
    /// <param name="bulk">Bulk coolant state at this station.</param>
    /// <param name="wallState">Coolant state at the wall temperature (same P).</param>
    /// <param name="velocity_ms">Bulk axial velocity in the channel.</param>
    /// <param name="hydraulicDiameter_m">D_h = 4·A / P_wetted.</param>
    /// <remarks>
    /// Sprint 16 / Track J / P5: this overload still works but
    /// recomputes <c>Math.Pow(Re, 0.8)</c> + <c>Math.Pow(Pr, 0.4)</c>
    /// each call. Hot wall-T loops should call
    /// <see cref="ComputeNusseltFactors"/> once per station and use the
    /// faster <see cref="HeatTransferCoefficient(in CoolantNusseltFactors, in CoolantState, in CoolantState, CoolantCorrelationKind)"/>
    /// overload below.
    /// </remarks>
    public static double HeatTransferCoefficient(
        in CoolantState bulk,
        in CoolantState wallState,
        double velocity_ms,
        double hydraulicDiameter_m,
        CoolantCorrelationKind kind = CoolantCorrelationKind.SiederTate)
    {
        if (hydraulicDiameter_m <= 0 || velocity_ms <= 0) return 0;

        var factors = ComputeNusseltFactors(bulk, velocity_ms, hydraulicDiameter_m);
        return HeatTransferCoefficient(factors, bulk, wallState, kind);
    }

    /// <summary>
    /// Sprint 16 / Track J / P5 (2026-04-22): pre-compute the Re/Pr
    /// scaling factors for a station's bulk state. Result is invariant
    /// across the wall-T iteration loop at that station, so call this
    /// once outside the loop and thread the returned struct into the
    /// fast <see cref="HeatTransferCoefficient(in CoolantNusseltFactors, in CoolantState, in CoolantState, CoolantCorrelationKind)"/>
    /// overload inside.
    /// </summary>
    public static CoolantNusseltFactors ComputeNusseltFactors(
        in CoolantState bulk, double velocity_ms, double hydraulicDiameter_m)
    {
        if (hydraulicDiameter_m <= 0 || velocity_ms <= 0) return default;
        double Re = ReynoldsNumber(bulk, velocity_ms, hydraulicDiameter_m);
        double Pr = bulk.Prandtl;
        return new CoolantNusseltFactors(
            re_0p8:               Math.Pow(Re, 0.8),
            re_0p82:              Math.Pow(Re, 0.82),
            pr_0p4:               Math.Pow(Pr, 0.4),
            bulkConductivity_WmK: bulk.Conductivity_WmK,
            hydraulicDiameter_m:  hydraulicDiameter_m);
    }

    /// <summary>
    /// Sprint 16 / Track J / P5 fast-path overload: same h_c result as
    /// the original <see cref="HeatTransferCoefficient(in CoolantState, in CoolantState, double, double, CoolantCorrelationKind)"/>,
    /// but skips the Re/Pr Math.Pow calls by consuming pre-computed
    /// factors. The wall-state-dependent tail (Sieder-Tate viscosity
    /// ratio, Pizzarelli density ratio) still recomputes per iteration
    /// — that is the only legitimately wall-T-dependent factor.
    /// </summary>
    public static double HeatTransferCoefficient(
        in CoolantNusseltFactors factors,
        in CoolantState bulk,
        in CoolantState wallState,
        CoolantCorrelationKind kind = CoolantCorrelationKind.SiederTate)
    {
        if (!factors.IsValid) return 0;

        double basePart = 0.023 * factors.Re_0p8 * factors.Pr_0p4;     // Dittus-Boelter / Sieder-Tate
        double pizzaPart = 0.0185 * factors.Re_0p82 * factors.Pr_0p4;  // Pizzarelli

        double Nu = kind switch
        {
            CoolantCorrelationKind.DittusBoelter =>
                basePart,

            CoolantCorrelationKind.SiederTate =>
                basePart
                * Math.Pow(bulk.Viscosity_PaS / Math.Max(wallState.Viscosity_PaS, 1e-9), 0.14),

            CoolantCorrelationKind.SupercriticalPizzarelli =>
                pizzaPart
                * Math.Pow(wallState.Density_kgm3 / Math.Max(bulk.Density_kgm3, 1e-9), 0.1),

            _ => basePart
        };

        return Nu * factors.BulkConductivity_WmK / factors.HydraulicDiameter_m;
    }

    /// <summary>
    /// A3 / Pizzarelli auto-select (2026-04-28): pick a per-station
    /// coolant-side Nusselt correlation based on whether the bulk state
    /// is in the fluid's pseudocritical transition band. When the user
    /// supplies a non-null <paramref name="fluid"/> and the bulk state
    /// is inside <see cref="Coolant.ICoolantFluid.IsInPseudocriticalRegion"/>,
    /// the correlation is bumped to <see cref="CoolantCorrelationKind.SupercriticalPizzarelli"/>
    /// regardless of the user's default kind. Outside the pseudocritical
    /// band the user's <paramref name="userKind"/> is used unchanged.
    /// <para>
    /// Rationale: Sieder-Tate (and especially Dittus-Boelter) systematically
    /// over-predict h_c near T_pc because the property variations
    /// (especially Cp peaking, ρ dropping) violate the constant-property
    /// derivation. Pizzarelli's <c>(ρ_w/ρ_b)^0.1</c> correction folds in
    /// the dominant pseudocritical scaling. Outside the band Pizzarelli
    /// gives essentially the same answer as Sieder-Tate (the density-ratio
    /// correction trends to 1), so leaving Pizzarelli everywhere would be
    /// fine — but auto-selecting keeps the correlation choice traceable
    /// per station for diagnostics and matches the typical engineering
    /// workflow of "use Sieder-Tate by default, switch near T_pc."
    /// </para>
    /// <para>
    /// Null <paramref name="fluid"/> short-circuits to the user's kind —
    /// preserves bit-identical behaviour for synthetic call sites that
    /// don't supply a fluid (test fixtures, legacy callers).
    /// </para>
    /// </summary>
    public static CoolantCorrelationKind AutoSelectKind(
        in CoolantState bulk,
        Coolant.ICoolantFluid? fluid,
        CoolantCorrelationKind userKind)
    {
        if (fluid is null) return userKind;
        // The user explicitly asked for Pizzarelli everywhere — respect that.
        if (userKind == CoolantCorrelationKind.SupercriticalPizzarelli) return userKind;
        return fluid.IsInPseudocriticalRegion(bulk.T_K, bulk.P_Pa)
            ? CoolantCorrelationKind.SupercriticalPizzarelli
            : userKind;
    }

    /// <summary>
    /// Darcy friction factor via Petukhov (smooth tube, turbulent).
    /// </summary>
    public static double FrictionFactor(double Re)
    {
        if (Re < 4000)
        {
            // Laminar fallback (rare in regen channels): f = 64/Re
            return 64.0 / Math.Max(Re, 1);
        }
        double term = 0.790 * Math.Log(Re) - 1.64;
        return 1.0 / (term * term);
    }

    /// <summary>
    /// Sprint 33 / PH-7 (2026-04-24): Darcy friction factor via Haaland
    /// (1983) with relative-roughness term. Required for LPBF-printed
    /// channels (ε/D ≈ 0.01–0.05) where smooth-tube Petukhov under-
    /// predicts f by 2-4× and silently masks impossible tank-pressure
    /// requirements at the FEED_PRESSURE_INSUFFICIENT gate.
    ///
    ///   1/√f = −1.8·log₁₀((ε/(3.7·D))^1.11 + 6.9/Re)
    ///
    /// When <paramref name="relativeRoughness"/> is 0, falls back to the
    /// smooth-tube <see cref="FrictionFactor(double)"/> overload (Petukhov)
    /// so existing test fixtures pinned on smooth-tube literals remain
    /// bit-identical.
    /// </summary>
    public static double FrictionFactor(double Re, double relativeRoughness)
    {
        if (relativeRoughness <= 0) return FrictionFactor(Re);
        if (Re < 4000)
        {
            return 64.0 / Math.Max(Re, 1);
        }
        double rough = relativeRoughness / 3.7;
        double inner = Math.Pow(rough, 1.11) + 6.9 / Re;
        double recip = -1.8 * Math.Log10(Math.Max(inner, 1e-30));
        return 1.0 / (recip * recip);
    }

    /// <summary>
    /// Pressure drop per unit length [Pa/m] for flow at bulk state (smooth tube).
    /// </summary>
    public static double PressureGradient(
        in CoolantState bulk,
        double velocity_ms,
        double hydraulicDiameter_m)
    {
        double Re = ReynoldsNumber(bulk, velocity_ms, hydraulicDiameter_m);
        double f = FrictionFactor(Re);
        return f * bulk.Density_kgm3 * velocity_ms * velocity_ms
             / (2.0 * Math.Max(hydraulicDiameter_m, 1e-9));
    }

    /// <summary>
    /// Sprint 33 / PH-7: pressure-gradient overload that propagates a
    /// relative-roughness term into the Haaland friction factor.
    /// <paramref name="relativeRoughness"/> = 0 falls back to smooth tube.
    /// </summary>
    public static double PressureGradient(
        in CoolantState bulk,
        double velocity_ms,
        double hydraulicDiameter_m,
        double relativeRoughness)
    {
        double Re = ReynoldsNumber(bulk, velocity_ms, hydraulicDiameter_m);
        double f = FrictionFactor(Re, relativeRoughness);
        return f * bulk.Density_kgm3 * velocity_ms * velocity_ms
             / (2.0 * Math.Max(hydraulicDiameter_m, 1e-9));
    }

    /// <summary>
    /// Sprint 33 / PH-6 (2026-04-24): Dravid Nu enhancement multiplier
    /// for helical / coiled tube flow.
    ///
    ///   Nu_curved/Nu_straight = 1 + 3.6·(1 − D_h/D_curv)·(D_h/D_curv)^0.5
    ///
    /// <paramref name="curvatureRadius_m"/> is the radius of the coil the
    /// channel wraps around; for a helix on a cylinder of radius r at
    /// pitch angle α (from chamber axis), R_curv = r / sin²α. Pure-axial
    /// flow (α=0) corresponds to R_curv → ∞, returning multiplier 1
    /// (no enhancement, no-op). Multiplier is bounded by construction:
    /// the (1 − D_h/D_curv) factor caps it to ≤ 1 + 3.6·(D_h/D_curv)^0.5.
    /// </summary>
    public static double DeanNumberNuMultiplier(
        double hydraulicDiameter_m, double curvatureRadius_m)
    {
        if (hydraulicDiameter_m <= 0) return 1.0;
        if (curvatureRadius_m <= 0 || double.IsInfinity(curvatureRadius_m))
            return 1.0;
        double D_curv_m = 2.0 * curvatureRadius_m;
        double ratio = hydraulicDiameter_m / D_curv_m;
        if (ratio >= 1.0) return 1.0; // Degenerate; channel wider than coil
        return 1.0 + 3.6 * (1.0 - ratio) * Math.Sqrt(ratio);
    }

    public static double ReynoldsNumber(
        in CoolantState bulk, double velocity_ms, double hydraulicDiameter_m)
    {
        return bulk.Density_kgm3 * velocity_ms * hydraulicDiameter_m
             / Math.Max(bulk.Viscosity_PaS, 1e-9);
    }
}
