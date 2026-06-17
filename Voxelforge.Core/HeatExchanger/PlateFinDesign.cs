// PlateFinDesign.cs — Sprint HX.W1 plate-fin counterflow HX design record.
//
// Stateless, immutable. Sized to bracket the printed plate-fin cluster
// (block volume O(10⁻³ to 10⁻¹) m³, fin pitch 1-3 mm, fin thickness
// 0.4-1.0 mm, plate spacing 5-15 mm). Both sides share the same fin
// geometry — a Wave-1 simplification; real plate-fin HXs typically
// use asymmetric fin densities to match the per-side flow regime.

using System;

namespace Voxelforge.HeatExchanger;

/// <summary>
/// Design parameters for a counterflow plate-fin heat exchanger
/// (Sprint HX.W1 scaffold). Both sides share the same fin geometry; a
/// future HX.W2 sprint will admit asymmetric fin sizing.
/// </summary>
/// <param name="Kind">Sub-variant — <see cref="HeatExchangerKind.PlateFinCounterflow"/> for Wave-1.</param>
/// <param name="CoreLength_m">Block length along the flow direction L [m].</param>
/// <param name="CoreWidth_m">Block width transverse to flow W [m].</param>
/// <param name="CoreHeight_m">Block stack height H [m] (sum of all plate spacings).</param>
/// <param name="PlateSpacing_m">Single channel height (between two plates) s [m].</param>
/// <param name="FinPitch_m">Fin centre-to-centre pitch p_fin [m].</param>
/// <param name="FinThickness_m">Fin web thickness t_fin [m].</param>
/// <param name="HotMassFlow_kgs">Hot-side mass flow ṁ_hot [kg/s].</param>
/// <param name="ColdMassFlow_kgs">Cold-side mass flow ṁ_cold [kg/s].</param>
/// <param name="HotInletTemperature_K">Hot inlet T_hot_in [K].</param>
/// <param name="ColdInletTemperature_K">Cold inlet T_cold_in [K] (must be &lt; hot inlet).</param>
/// <param name="HotCp_JkgK">Hot-side specific heat cp_hot [J/(kg·K)].</param>
/// <param name="ColdCp_JkgK">Cold-side specific heat cp_cold [J/(kg·K)].</param>
/// <param name="HotDensity_kgm3">Hot-side density ρ_hot [kg/m³] (incompressible-flow simplification).</param>
/// <param name="ColdDensity_kgm3">Cold-side density ρ_cold [kg/m³].</param>
/// <param name="HotViscosity_PaS">Hot-side dynamic viscosity µ_hot [Pa·s].</param>
/// <param name="ColdViscosity_PaS">Cold-side dynamic viscosity µ_cold [Pa·s].</param>
internal sealed record PlateFinDesign(
    HeatExchangerKind Kind,
    double CoreLength_m,
    double CoreWidth_m,
    double CoreHeight_m,
    double PlateSpacing_m,
    double FinPitch_m,
    double FinThickness_m,
    double HotMassFlow_kgs,
    double ColdMassFlow_kgs,
    double HotInletTemperature_K,
    double ColdInletTemperature_K,
    double HotCp_JkgK,
    double ColdCp_JkgK,
    double HotDensity_kgm3,
    double ColdDensity_kgm3,
    double HotViscosity_PaS,
    double ColdViscosity_PaS)
{
    // ── Sprint HX.W2 — fin-efficiency correction ─────────────────────────
    //
    // The Wave-1 solver assumed perfect-conductor fins (η_fin = 1). For
    // thin LPBF fins running at moderate h, the temperature-gradient
    // along the fin reduces the effective area-averaged h_eff by 10-30 %.
    // Sprint HX.W2 adds the fin-efficiency correction η_fin = tanh(m·L)
    // / (m·L) with m = √(2h / (k_fin · t_fin)) and L = half-fin-height
    // (PlateSpacing/2 — the fin is plate-mounted both top and bottom).
    //
    // Activation: opt-in via EnableFinEfficiencyCorrection (default false
    // for bit-identical HX.W1 backwards-compat). Wave-1 result tests
    // continue to assert h_eff = h (no correction) when the flag is off.

    /// <summary>
    /// Enable the Sprint HX.W2 fin-efficiency correction. When false
    /// (default), the solver behaves bit-identically to Sprint HX.W1
    /// (assumes perfect-conductor fins, η_fin = 1). When true, applies
    /// `h_eff = h · η_fin` per side using the canonical 1-D fin
    /// formulation η_fin = tanh(m·L) / (m·L).
    /// </summary>
    public bool EnableFinEfficiencyCorrection { get; init; } = false;

    /// <summary>
    /// Fin thermal conductivity k_fin [W/(m·K)]. Drives the fin-
    /// efficiency correction's `m = √(2h/(k_fin · t_fin))` parameter.
    /// Default 12.0 W/(m·K) — LPBF Inconel-718 cluster anchor. Ignored
    /// when <see cref="EnableFinEfficiencyCorrection"/> is false.
    /// </summary>
    public double FinThermalConductivity_WmK { get; init; } = 12.0;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    /// <exception cref="ArgumentException">When any dimension is non-positive, fin
    /// thickness exceeds fin pitch, plate spacing exceeds core height, or
    /// inlet-temperature ordering is reversed.</exception>
    public void ValidateSelf()
    {
        if (Kind != HeatExchangerKind.PlateFinCounterflow)
            throw new ArgumentException(
                $"Wave-1 supports only PlateFinCounterflow; got {Kind}.", nameof(Kind));
        if (CoreLength_m   <= 0) throw new ArgumentException("CoreLength_m must be > 0.",   nameof(CoreLength_m));
        if (CoreWidth_m    <= 0) throw new ArgumentException("CoreWidth_m must be > 0.",    nameof(CoreWidth_m));
        if (CoreHeight_m   <= 0) throw new ArgumentException("CoreHeight_m must be > 0.",   nameof(CoreHeight_m));
        if (PlateSpacing_m <= 0) throw new ArgumentException("PlateSpacing_m must be > 0.", nameof(PlateSpacing_m));
        if (FinPitch_m     <= 0) throw new ArgumentException("FinPitch_m must be > 0.",     nameof(FinPitch_m));
        if (FinThickness_m <= 0) throw new ArgumentException("FinThickness_m must be > 0.", nameof(FinThickness_m));
        if (FinThickness_m >= FinPitch_m)
            throw new ArgumentException(
                $"FinThickness_m ({FinThickness_m:E3}) must be < FinPitch_m ({FinPitch_m:E3}); "
              + "otherwise the channel is fully blocked.",
                nameof(FinThickness_m));
        if (PlateSpacing_m >= CoreHeight_m)
            throw new ArgumentException(
                $"PlateSpacing_m ({PlateSpacing_m:E3}) must be < CoreHeight_m ({CoreHeight_m:E3}); "
              + "otherwise the block has no stacking room.",
                nameof(PlateSpacing_m));
        if (HotMassFlow_kgs  <= 0) throw new ArgumentException("HotMassFlow_kgs must be > 0.",  nameof(HotMassFlow_kgs));
        if (ColdMassFlow_kgs <= 0) throw new ArgumentException("ColdMassFlow_kgs must be > 0.", nameof(ColdMassFlow_kgs));
        if (HotCp_JkgK  <= 0) throw new ArgumentException("HotCp_JkgK must be > 0.",  nameof(HotCp_JkgK));
        if (ColdCp_JkgK <= 0) throw new ArgumentException("ColdCp_JkgK must be > 0.", nameof(ColdCp_JkgK));
        if (HotDensity_kgm3  <= 0) throw new ArgumentException("HotDensity_kgm3 must be > 0.",  nameof(HotDensity_kgm3));
        if (ColdDensity_kgm3 <= 0) throw new ArgumentException("ColdDensity_kgm3 must be > 0.", nameof(ColdDensity_kgm3));
        if (HotViscosity_PaS  <= 0) throw new ArgumentException("HotViscosity_PaS must be > 0.",  nameof(HotViscosity_PaS));
        if (ColdViscosity_PaS <= 0) throw new ArgumentException("ColdViscosity_PaS must be > 0.", nameof(ColdViscosity_PaS));
        if (HotInletTemperature_K <= ColdInletTemperature_K)
            throw new ArgumentException(
                $"HotInletTemperature_K ({HotInletTemperature_K:F2}) must exceed "
              + $"ColdInletTemperature_K ({ColdInletTemperature_K:F2}) for the HX to drive net heat transfer.",
                nameof(HotInletTemperature_K));
        if (EnableFinEfficiencyCorrection && FinThermalConductivity_WmK <= 0)
            throw new ArgumentException(
                "FinThermalConductivity_WmK must be > 0 when EnableFinEfficiencyCorrection is true.",
                nameof(FinThermalConductivity_WmK));
    }
}
