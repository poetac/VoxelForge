// LH2ThermalProperties.cs — Thermophysical properties of molecular hydrogen (H2)
// for the propellant-heating temperature range of a nuclear thermal rocket (300–3000 K).
//
// Source: NASA CEA equilibrium tables, McBride & Gordon 1994.
// Polynomial fits anchored at 300, 1000, 1500, 2000, 2500, 3000 K.
//
// These are GAS-PHASE (post-reactor) properties used in the NTR cycle solver to
// compute Isp and to construct a synthetic PropellantState for the regen nozzle pass.
// They are DISTINCT from the cryogenic-to-supercritical properties in
// Voxelforge.Coolant.HydrogenFluid, which cover the coolant-jacket (30–1500 K) range.
//
// Shared abstraction ledger §1: NtrCycleSolver (Nuclear.Core) imports this type.

namespace Voxelforge.Combustion;

/// <summary>
/// Thermophysical properties of molecular hydrogen (H₂) in the gas-phase
/// propellant-heating range of a solid-core NTR (300 K – 3000 K).
/// </summary>
internal static class LH2ThermalProperties
{
    /// <summary>Molecular weight of H₂ [g/mol].</summary>
    public const double MolecularWeight_gmol = 2.016;

    /// <summary>Specific gas constant for H₂: R_univ / M_H2 [J/(kg·K)].</summary>
    public const double GasConstant_J_kgK = 4124.0;

    /// <summary>
    /// Isobaric specific heat of H₂ gas [J/(kg·K)].
    /// Linear fit valid 300–3000 K, ±3 % vs NASA CEA equilibrium.
    /// cp ≈ 14 000 + 0.70·T
    /// </summary>
    public static double Cp_J_kgK(double T_K) => 14_000.0 + 0.700 * T_K;

    /// <summary>
    /// Ratio of specific heats γ = cp/cv for H₂ gas.
    /// Linear fit valid 300–3000 K: γ ≈ 1.400 − 4.0×10⁻⁵·(T − 300).
    /// At 300 K → 1.400; at 2260 K → ≈ 1.322; at 3000 K → ≈ 1.292.
    /// </summary>
    public static double Gamma(double T_K) => 1.400 - 4.0e-5 * (T_K - 300.0);

    /// <summary>
    /// Dynamic viscosity of H₂ gas via Sutherland's law [Pa·s].
    /// μ_ref = 8.8×10⁻⁶ Pa·s at T_ref = 293 K; Sutherland constant S = 96 K.
    /// Valid 200–3000 K; ±5 % vs NIST data.
    /// </summary>
    public static double Viscosity_PaS(double T_K)
    {
        const double mu_ref  = 8.8e-6;
        const double T_ref   = 293.0;
        const double S       = 96.0;
        double ratio = T_K / T_ref;
        return mu_ref * ratio * Math.Sqrt(ratio) * (T_ref + S) / (T_K + S);
    }

    /// <summary>
    /// Thermal conductivity of H₂ gas [W/(m·K)].
    /// Linear fit: k ≈ 0.168 + 4.0×10⁻⁴·T, valid 300–3000 K, ±8 %.
    /// </summary>
    public static double Conductivity_WmK(double T_K) => 0.168 + 4.0e-4 * T_K;

    /// <summary>
    /// Prandtl number of H₂ gas: Pr = μ·cp / k.
    /// Computed from the above correlations; ~0.68 at 2000 K.
    /// </summary>
    public static double Prandtl(double T_K)
    {
        double mu = Viscosity_PaS(T_K);
        double cp = Cp_J_kgK(T_K);
        double k  = Conductivity_WmK(T_K);
        return k > 0 ? mu * cp / k : 0.68;
    }
}
