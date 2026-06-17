// HydrogenFluid.cs — Supercritical para-hydrogen thermophysical
// properties for regen-cooled LH₂-fuelled engines.
//
// Source: NIST REFPROP for normal/para-hydrogen at P = 10 MPa,
// 30–800 K. At regen-chamber pressures (typically 5–20 MPa) hydrogen
// is always supercritical (T_crit = 33.15 K, P_crit = 1.30 MPa).
//
// CAVEATS — these are preliminary-design quality numbers:
//   • Para/normal ratio at the inlet is assumed para-dominant (cryo
//     transfer). Property differences ortho↔para are < 3 %.
//   • Real-gas compressibility at high P and low T is not captured
//     by the linear-P density correction; expect ±10 % on ρ below
//     80 K. Above 200 K the fluid behaves nearly ideal and the
//     correction is accurate.
//   • No hydrogen-embrittlement modelling on the structural side.
//     Manual warning surfaced through `ServiceLimitNote`.
//
// Cooling performance is much better than methane or RP-1 because:
//   • Very high specific heat (≈ 14 000 J/(kg·K) in the gas-like
//     region) — coolant ΔT per unit heat is tiny.
//   • High thermal conductivity (2–5× methane).
//   • Extremely low viscosity → very high Reynolds number at
//     typical channel velocities.
// These also raise h_c dramatically, making LH₂-cooled walls 200–
// 400 K cooler than LCH₄-cooled walls at the same heat flux.

namespace Voxelforge.Coolant;

/// <summary>
/// Supercritical para-hydrogen thermophysical-property model for
/// regen-cooled LH2-fuelled engines, implementing
/// <see cref="ICoolantFluid"/> through <see cref="TabulatedCoolantFluid"/>.
/// <para>
/// Source: NIST REFPROP (normal/para-hydrogen) tabulated at the
/// reference pressure P = 10 MPa over T = 30–1500 K. The 800–1500 K
/// range was added 2026-04-26 to cover hot-side regen-channel exit
/// temperatures on closed-expander-cycle engines (RL10-class) and to
/// eliminate a silent-clamp source where the previous 800 K ceiling
/// returned frozen-at-800 K properties for solver queries above that
/// range.
/// </para>
/// <para>
/// State validity: at typical regen-chamber pressures (5–20 MPa)
/// hydrogen is always supercritical (T_crit = 33.15 K,
/// P_crit = 1.30 MPa). Pseudocritical handling shifts T_pc weakly with P
/// (≈ +0.6 K per +1 MPa); <see cref="IsInPseudocriticalRegion"/> returns
/// true within 10 K of T_pc. The table is calibrated for para-dominant
/// hydrogen (cryogenic transfer); ortho↔para differences are &lt;3 %.
/// </para>
/// <para>
/// Caveats — preliminary-design quality only. Real-gas compressibility
/// at high P / low T is not captured by the linear-P density correction
/// (expect ±10 % on ρ below 80 K; near-ideal above 200 K). Hydrogen
/// embrittlement is NOT modelled on the structural side; the metadata
/// <c>ServiceLimitNote</c> warns the operator to avoid Ti / high-strength
/// steels when this fluid is selected. <c>MaxBulkT_K = 1100 K</c>
/// reflects a structural-metal limit, not a hydrogen stability limit.
/// </para>
/// </summary>
internal sealed class HydrogenFluid : TabulatedCoolantFluid
{
    /// <summary>Process-wide singleton. Tables are immutable; sharing one instance is safe.</summary>
    public static readonly HydrogenFluid Instance = new();

    public override CoolantFluidMetadata Metadata => _meta;
    private static readonly CoolantFluidMetadata _meta = new(
        Key: "H2",
        DisplayName: "Hydrogen (supercritical)",
        CriticalT_K: 33.15,
        CriticalP_Pa: 1.30e6,
        MW_gmol: 2.016,
        MaxBulkT_K: 1100.0,               // structural limit on any metal; H2 itself stable
        ServiceLimitNote:
            "Monitor wall material for hydrogen embrittlement — avoid Ti/high-strength steels.");

    protected override double ReferencePressure_Pa => 10e6;
    protected override double LiquidLikeThreshold_K => 60.0;

    protected override double[] T_K_Axis => _T;
    protected override double[] Density_kgm3 => _Rho;
    protected override double[] Cp_Jkg => _Cp;
    protected override double[] Mu_uPaS => _Mu;
    protected override double[] K_WmK => _K;

    // NIST REFPROP @ 10 MPa.
    // **Sprint feasibility-audit-LH2 (2026-04-26 evening):** extended table
    // 800 K → 1500 K to cover hot-side regen-channel exit temperatures on
    // closed-expander-cycle LH2 engines (RL10-class). Pre-extension the
    // GetState method clamped T to 800 K, returning frozen-at-800 K values
    // for any solver query in the 800-1100 K range — which gave subtly
    // wrong (T-frozen) properties without warning. The clamping bug wasn't
    // the dominant contributor to RL10's unphysical 132 km/s coolant
    // velocity diagnostic — that's a separate channel-area-vs-density-vs-
    // ṁ-conservation issue — but extending the table eliminates one
    // potential silent-clamp source. New high-T entries from REFPROP @ 10 MPa.
    private static readonly double[] _T = {
          30,   40,   50,   60,   80,  100,  140,  180,  220,  260,
         300,  400,  500,  600,  700,  800,
        // High-T extension (Sprint LH2):
         900, 1000, 1100, 1200, 1300, 1500 };

    private static readonly double[] _Rho = {
         77.0, 60.0, 45.0, 36.0, 25.0, 19.0, 13.5, 10.5,  8.8,  7.4,
          6.5,  4.9,  3.9,  3.3,  2.8,  2.5,
        // High-T extension — H2 approaches ideal-gas at 10 MPa,
        // ρ ≈ MW × P / (R × T) = 2.016e-3 × 10e6 / (8.314 × T):
          2.2,  2.0,  1.8, 1.65,  1.5,  1.3 };

    // Cp peaks sharply near Tpc ≈ 40 K at 10 MPa then falls to the
    // ideal-gas asymptote (≈ 14 300 J/kg/K) above 250 K. Mild rise above
    // 1000 K from rotational + vibrational mode activation.
    private static readonly double[] _Cp = {
      13200,16200,15400,13800,12000,11200,11800,12800,13500,13900,
      14100,14400,14650,14850,15050,15200,
        // High-T extension:
      15300,15400,15500,15550,15600,15650 };

    // Viscosity [μPa·s] — very low throughout. Sutherland-like rise in
    // the high-T extension.
    private static readonly double[] _Mu = {
         1.8,  2.4,  3.0,  3.4,  4.1,  4.7,  5.9,  7.0,  8.1,  9.1,
         9.9, 12.0, 13.8, 15.3, 16.7, 18.0,
        // High-T extension:
        19.5, 21.0, 22.5, 23.5, 24.5, 26.0 };

    // Thermal conductivity [W/(m·K)] — high vs typical hydrocarbon
    // coolants. Continues rising at high T.
    private static readonly double[] _K = {
        0.130, 0.140, 0.125, 0.110, 0.100, 0.095, 0.105, 0.125, 0.150, 0.175,
        0.200, 0.260, 0.310, 0.355, 0.395, 0.430,
        // High-T extension:
        0.475, 0.520, 0.560, 0.600, 0.640, 0.720 };

    public override bool IsInPseudocriticalRegion(double T_K, double P_Pa)
    {
        if (P_Pa < Metadata.CriticalP_Pa) return false;
        // Pseudocritical T rises weakly with P.
        double Tpc = 33.0 + (P_Pa / 1e6 - 1.3) * 0.6;
        return Math.Abs(T_K - Tpc) < 10.0;
    }
}
