// CentrifugalCompressorSolver.cs — Sprint CMP.W1 closed-form centrifugal
// compressor performance snapshot.
//
// Stateless, allocation-free, deterministic. Black-box "isentropic-
// then-corrected" stage model:
//
//   T_t2_is  = T_t1 · π_c ^ ((γ−1)/γ)
//   ΔT_is    = T_t2_is − T_t1
//   ΔT_act   = ΔT_is / η_isentropic
//   T_t2     = T_t1 + ΔT_act
//   w_specific = cp · ΔT_act
//   P_shaft  = ṁ · w_specific
//   P_t2     = π_c · P_t1
//   ρ_2/ρ_1  = (P_t2 / P_t1) · (T_t1 / T_t2)    [ideal gas]
//
// Per-stage / per-impeller geometry (slip factor, Euler work
// breakdown, tip-Mach surge / choke envelope) is deferred to CMP.W2.
//
// References:
//   Saravanamuttoo H.I.H., Rogers G.F.C., Cohen H. (2017). "Gas
//     Turbine Theory," 7th ed., chap 5 (centrifugal compressors).
//   Whitfield A., Baines N.C. (1990). "Design of Radial Turbomachines."
//   Cumpsty N.A. (2004). "Compressor Aerodynamics," 2nd ed.

using System;

namespace Voxelforge.Compressor;

/// <summary>
/// Closed-form centrifugal compressor stage performance snapshot
/// solver (Sprint CMP.W1).
/// </summary>
internal static class CentrifugalCompressorSolver
{
    /// <summary>
    /// Solve the centrifugal compressor stage performance snapshot at
    /// the design operating point.
    /// </summary>
    internal static CentrifugalCompressorResult Solve(
        CentrifugalCompressorDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        double gamma = design.WorkingGasGamma;
        double exponent = (gamma - 1.0) / gamma;
        double pi_c = design.PressureRatio;

        // Isentropic temperature rise.
        double T_t2_is = design.InletTotalTemperature_K * Math.Pow(pi_c, exponent);
        double dT_is   = T_t2_is - design.InletTotalTemperature_K;

        // Actual rise (corrected by isentropic efficiency).
        double dT_act = dT_is / design.IsentropicEfficiency;
        double T_t2   = design.InletTotalTemperature_K + dT_act;

        // Pressure + work + power.
        double P_t2 = design.InletTotalPressure_Pa * pi_c;
        double w_specific = design.WorkingGasSpecificHeat_J_kgK * dT_act;
        double P_shaft = design.MassFlow_kgs * w_specific;

        // Density ratio from ideal-gas (P/T) ratio.
        double densityRatio = pi_c * (design.InletTotalTemperature_K / T_t2);

        return new CentrifugalCompressorResult(
            IsentropicExitTemperature_K:  T_t2_is,
            ActualExitTemperature_K:      T_t2,
            ExitTotalPressure_Pa:         P_t2,
            IsentropicTemperatureRise_K:  dT_is,
            ActualTemperatureRise_K:      dT_act,
            SpecificWork_J_kg:            w_specific,
            ShaftPowerInput_W:            P_shaft,
            DensityRatio:                 densityRatio);
    }

    /// <summary>
    /// Sprint CMP.W2. Compute the overall isentropic efficiency of an
    /// N-stage axial compressor chain at constant per-stage polytropic
    /// efficiency. For a multi-stage compressor with N stages each at
    /// per-stage Pratio π_s and polytropic η_pc, the overall isentropic
    /// efficiency drops as N grows even when η_pc is constant — this
    /// is the "polytropic-isentropic gap" inversion. The formula:
    ///
    ///   π_overall = π_s ^ N
    ///   η_isentropic = (π_overall^((γ-1)/γ) − 1) /
    ///                  (π_overall^((γ-1)/(γ·η_pc)) − 1)
    ///
    /// At η_pc = 1.0, η_isen = 1.0 regardless of N.
    /// </summary>
    /// <param name="perStagePolytropicEfficiency">η_pc ∈ (0, 1] [-].</param>
    /// <param name="overallPressureRatio">π_overall &gt; 1 [-].</param>
    /// <param name="gamma">γ [-].</param>
    /// <returns>η_isentropic [-]. Always ≤ η_pc for multi-stage.</returns>
    internal static double ComputeIsentropicFromPolytropic(
        double perStagePolytropicEfficiency,
        double overallPressureRatio,
        double gamma)
    {
        if (perStagePolytropicEfficiency <= 0 || perStagePolytropicEfficiency > 1.0)
            throw new ArgumentOutOfRangeException(nameof(perStagePolytropicEfficiency),
                "η_pc must be in (0, 1].");
        if (overallPressureRatio <= 1.0)
            throw new ArgumentOutOfRangeException(nameof(overallPressureRatio),
                "Pressure ratio must be > 1.");
        if (gamma <= 1.0 || gamma > 2.0)
            throw new ArgumentOutOfRangeException(nameof(gamma),
                "γ must be in (1, 2].");

        double exponent = (gamma - 1.0) / gamma;
        double numerator   = Math.Pow(overallPressureRatio, exponent) - 1.0;
        double denominator = Math.Pow(overallPressureRatio,
                                exponent / perStagePolytropicEfficiency) - 1.0;
        return numerator / denominator;
    }

    /// <summary>
    /// Compute the polytropic efficiency from the isentropic efficiency
    /// + pressure ratio. Public-static helper for multi-stage / per-
    /// stage breakdown studies.
    /// </summary>
    /// <param name="isentropicEfficiency">η_isen ∈ (0, 1] [-].</param>
    /// <param name="pressureRatio">π_c &gt; 1 [-].</param>
    /// <param name="gamma">γ [-].</param>
    /// <returns>η_polytropic [-]. Always ≥ η_isen for compressors
    /// (η_pc = η_isen when π_c → 1; η_pc rises above η_isen as π_c grows).</returns>
    internal static double ComputePolytropicEfficiency(
        double isentropicEfficiency,
        double pressureRatio,
        double gamma)
    {
        if (isentropicEfficiency <= 0 || isentropicEfficiency > 1.0)
            throw new ArgumentOutOfRangeException(nameof(isentropicEfficiency),
                "η_isen must be in (0, 1].");
        if (pressureRatio <= 1.0)
            throw new ArgumentOutOfRangeException(nameof(pressureRatio),
                "Pressure ratio must be > 1.");
        if (gamma <= 1.0 || gamma > 2.0)
            throw new ArgumentOutOfRangeException(nameof(gamma),
                "γ must be in (1, 2].");

        double exponent = (gamma - 1.0) / gamma;
        // Relation: π^(exponent/η_pc) = 1 + (π^exponent − 1)/η_isen
        // → exponent/η_pc = ln(1 + (π^exponent − 1)/η_isen) / ln(π)
        // → η_pc = exponent · ln(π) / ln(1 + (π^exponent − 1)/η_isen)
        double numerator   = exponent * Math.Log(pressureRatio);
        double denominator = Math.Log(1.0
            + (Math.Pow(pressureRatio, exponent) - 1.0) / isentropicEfficiency);
        return numerator / denominator;
    }
}
