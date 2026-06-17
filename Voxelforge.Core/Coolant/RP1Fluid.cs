// RP1Fluid.cs — RP-1 (rocket-grade kerosene) thermophysical properties
// for regen-cooled LOX/kerosene engines.
//
// Source: Huang & Wadel, NASA TM-107084 (1997) "Comparison of Cooling
// Channel Designs" Appendix B; Edwards, "Liquid Fuels and Propellants
// for Aerospace Propulsion" JPP 2003; MIL-R-25576 specification
// baseline properties + temperature-dependent correlations from
// Rachner, "Die Stoffeigenschaften des Kerosins Jet A-1" DLR IB-325
// (1998). Representative values at P = 10 MPa.
//
// RP-1 is NOT supercritical at any normal chamber pressure
// (T_crit ≈ 670 K, P_crit ≈ 2.4 MPa). At 10 MPa it's a subcooled
// liquid throughout the regen jacket. Property variation with P is
// small (2-3 %) and we neglect it.
//
// ═══════════════════════════════════════════════════════════════════
//   CRITICAL OPERATING CONSTRAINT — COKING
//
//   Above ≈ 550 K bulk temperature OR ≈ 700 K wall temperature, RP-1
//   pyrolyses and deposits carbon on the coolant-side wall. This
//   progressively reduces heat-transfer coefficient and can cause
//   burn-through. The solver does NOT detect coking directly; the
//   user must size the jacket so coolant-side wall T < 700 K and
//   bulk T < 600 K.
//
//   This module sets MaxBulkT_K = 600 K so that the solver's
//   warnings-layer surfaces a clear note whenever bulk T exceeds
//   that value. The wall T check belongs to the thermal-solve
//   output (`PeakCoolantSideWallT_K`) and should be added to the
//   warnings pass when RP-1 is selected.
// ═══════════════════════════════════════════════════════════════════

namespace Voxelforge.Coolant;

/// <summary>
/// RP-1 (rocket-grade kerosene) thermophysical-property model for
/// LOX/RP-1 regen-cooled engines, implementing
/// <see cref="ICoolantFluid"/> through <see cref="TabulatedCoolantFluid"/>.
/// <para>
/// Source: Huang &amp; Wadel (NASA TM-107084 1997) "Comparison of Cooling
/// Channel Designs" Appendix B; Edwards (JPP 2003) "Liquid Fuels and
/// Propellants for Aerospace Propulsion"; MIL-R-25576 baseline
/// properties; temperature-dependent correlations from Rachner
/// (DLR IB-325 1998). Tabulated at the reference pressure P = 10 MPa
/// over T = 230–700 K.
/// </para>
/// <para>
/// State validity: RP-1 is NOT supercritical at any normal chamber
/// pressure (T_crit ≈ 670 K, P_crit ≈ 2.4 MPa). At 10 MPa it is a
/// subcooled liquid throughout the regen jacket. Property variation
/// with P is small (2–3 %) and is neglected here;
/// <see cref="IsInPseudocriticalRegion"/> always returns false.
/// </para>
/// <para>
/// CRITICAL operating constraint — coking. Above ~550 K bulk OR ~700 K
/// wall, RP-1 pyrolyses and deposits carbon on the coolant-side wall,
/// progressively reducing h_c and risking burn-through. The solver does
/// NOT detect coking directly; <c>MaxBulkT_K = 600 K</c> is set so the
/// warnings layer surfaces a service-limit note when bulk T exceeds the
/// safe envelope. Wall-T checks against the 700 K coking threshold
/// belong to the thermal-solve output, not this property module.
/// </para>
/// </summary>
internal sealed class RP1Fluid : TabulatedCoolantFluid
{
    /// <summary>Process-wide singleton. Tables are immutable; sharing one instance is safe.</summary>
    public static readonly RP1Fluid Instance = new();

    public override CoolantFluidMetadata Metadata => _meta;
    private static readonly CoolantFluidMetadata _meta = new(
        Key: "RP-1",
        DisplayName: "RP-1 (kerosene)",
        CriticalT_K: 670.0,
        CriticalP_Pa: 2.4e6,
        MW_gmol: 170.0,                 // representative (C₁₂ average)
        MaxBulkT_K: 600.0,
        ServiceLimitNote:
            "Coking onset ≈ 550 K bulk, 700 K wall. Keep coolant outlet T_bulk < 600 K.");

    protected override double ReferencePressure_Pa => 10e6;
    protected override double LiquidLikeThreshold_K => 800.0;  // treat as incompressible always

    protected override double[] T_K_Axis => _T;
    protected override double[] Density_kgm3 => _Rho;
    protected override double[] Cp_Jkg => _Cp;
    protected override double[] Mu_uPaS => _Mu;
    protected override double[] K_WmK => _K;

    // Representative RP-1 at 10 MPa, temperature range 230–700 K.
    // At 230 K (well below freezing for commercial kerosene ~220 K) the
    // tool is OFF-DESIGN but we supply values for numerical continuity.
    private static readonly double[] _T = {
        230,  260,  298,  340,  400,  450,  500,  550,  600,  650,  700 };

    // Density [kg/m³] — linear fit from Rachner Table 3; ~820 at 298 K,
    // decreases at higher T (thermal expansion dominant over compression).
    private static readonly double[] _Rho = {
        850,  835,  820,  800,  770,  745,  720,  690,  655,  610,  555 };

    // Cp [J/(kg·K)] — rises with T, typical mid-distillate behaviour.
    private static readonly double[] _Cp = {
       1850, 1920, 2010, 2130, 2320, 2490, 2680, 2890, 3140, 3460, 3900 };

    // Dynamic viscosity [μPa·s] — drops rapidly from freezing toward
    // moderate T, then slowly. Major source of ΔP at low T.
    private static readonly double[] _Mu = {
       5000, 2400, 1450,  780,  420,  280,  200,  150,  120,  100,   90 };

    // Thermal conductivity [W/(m·K)] — essentially flat, slight drop with T.
    private static readonly double[] _K = {
       0.135, 0.133, 0.131, 0.128, 0.123, 0.119, 0.115, 0.111, 0.107, 0.103, 0.099 };

    public override bool IsInPseudocriticalRegion(double T_K, double P_Pa)
    {
        // At all chamber pressures of interest (< 25 MPa) RP-1 is
        // single-phase liquid — no pseudocritical transition.
        return false;
    }
}
