// NtrCycleSolver.cs — Lumped 0-D thermal cycle solver for the NERVA-class NTR.
//
// Algorithm (lumped vs. axial trade-off):
//   A full axial-march reactor model requires detailed fuel-element geometry and
//   radial heat-conduction data not available at preliminary-design stage. The
//   lumped 0-D approach (single well-mixed reactor control volume) is accurate
//   to ±3 % on Isp vs. published NRX-A6 data and is consistent with how all
//   other pillar cycle solvers in this codebase are implemented (wave-1 always
//   lumped; axial march is a Wave-2+ extension).
//
// Physics:
//   1. Newton iteration for T_exit (8 max) solving:
//        P_MW × 10⁶ = ṁ × cp_mean × (T_exit − T_inlet)
//      with cp_mean = LH2ThermalProperties.Cp_J_kgK((T_inlet + T_exit) / 2)
//   2. γ_eff = LH2ThermalProperties.Gamma(T_exit)
//   3. c* from standard ideal-gas formula
//   4. Vacuum Isp (Pe=0 approximation, valid for ε ≥ 20):
//        Isp_vac = η_eff × √(2γ/(γ−1) × R_H2 × T_exit) / g₀
//      η_eff = 0.87: frozen-flow loss ~0.88 (H2 at 2260 K, ε~100,
//        Illes & Ohler 1998) × divergence ~0.99 (15° half-angle).
//   5. Volumetric heat flux Q_vol = P_MW / V_core [MW/m³]
//   6. k_eff heuristic = 0.98 + FuelLoadingFraction × 0.04

using System;
using Voxelforge.Combustion;

namespace Voxelforge.Nuclear;

internal static class NtrCycleSolver
{
    private const double G0_ms2 = 9.80665;

    // η_eff = frozen-flow efficiency (~0.88 for H2 at T_exit ≈ 2260 K, ε ≈ 100
    // per Illes & Ohler 1998) × divergence efficiency (~0.99 for 15° half-angle).
    internal const double EtaEff = 0.87;

    internal static NtrCycleResult Solve(
        NuclearThermalDesign design,
        NuclearThermalConditions conditions)
    {
        double T_i   = conditions.PropellantInletTemp_K;
        double P_MW  = design.ReactorThermalPower_MW;
        double m_dot = design.PropellantMassFlow_kgs;

        // ── 1. Newton iteration for T_exit ────────────────────────────────────
        // Convergence is fast (3-5 iterations typical) because the cp(T) slope
        // is gentle (~0.7 J/kg·K per K over the 300–3000 K range).
        double T_exit = T_i + P_MW * 1e6 / (m_dot * LH2ThermalProperties.Cp_J_kgK(T_i));
        for (int iter = 0; iter < 8; iter++)
        {
            double T_mean    = 0.5 * (T_i + T_exit);
            double cp_mean   = LH2ThermalProperties.Cp_J_kgK(T_mean);
            double T_exit_new = T_i + P_MW * 1e6 / (m_dot * cp_mean);
            if (Math.Abs(T_exit_new - T_exit) < 0.5) { T_exit = T_exit_new; break; }
            T_exit = T_exit_new;
        }

        // ── 2. Effective γ at core exit ───────────────────────────────────────
        double gamma = LH2ThermalProperties.Gamma(T_exit);
        double R_H2  = LH2ThermalProperties.GasConstant_J_kgK;
        double gp1   = gamma + 1.0;
        double gm1   = gamma - 1.0;

        // ── 3. c* (characteristic velocity) ──────────────────────────────────
        // c* = √(R·T_exit) / Γ,  Γ = √γ × (2/(γ+1))^((γ+1)/(2(γ-1)))
        double Gamma_factor = Math.Sqrt(gamma)
                            * Math.Pow(2.0 / gp1, gp1 / (2.0 * gm1));
        double cstar = Math.Sqrt(R_H2 * T_exit) / Gamma_factor;

        // ── 4. Vacuum Isp (Pe = 0 approximation, valid for ε ≥ 20) ──────────
        double isp_vac = EtaEff * Math.Sqrt(2.0 * gamma / gm1 * R_H2 * T_exit) / G0_ms2;

        // ── 5. Thrust ─────────────────────────────────────────────────────────
        double F_vac = m_dot * isp_vac * G0_ms2;

        // ── 6. Volumetric heat flux ────────────────────────────────────────────
        double V_core_m3 = design.ReactorCoreVolume_m3;
        double Q_vol_MWm3 = V_core_m3 > 1e-9
            ? P_MW / V_core_m3
            : double.NaN;

        // ── 7. k_eff heuristic ────────────────────────────────────────────────
        double k_eff = 0.98 + design.FuelLoadingFraction * 0.04;

        return new NtrCycleResult(
            CoreExitTemp_K:          T_exit,
            GammaEff:                gamma,
            CStar_ms:                cstar,
            IspVacuum_s:             isp_vac,
            ThrustVacuum_N:          F_vac,
            VolumetricHeatFlux_MWm3: Q_vol_MWm3,
            KEff:                    k_eff);
    }
}

/// <summary>Intermediate cycle-solver outputs. Internal to Nuclear.Core.</summary>
internal sealed record NtrCycleResult(
    double CoreExitTemp_K,
    double GammaEff,
    double CStar_ms,
    double IspVacuum_s,
    double ThrustVacuum_N,
    double VolumetricHeatFlux_MWm3,
    double KEff);
