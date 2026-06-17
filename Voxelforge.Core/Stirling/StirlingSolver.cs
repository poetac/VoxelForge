// StirlingSolver.cs — Sprint STR.W1 closed-form Stirling-engine
// performance snapshot.
//
// Stateless, allocation-free, deterministic. The Wave-1 model uses a
// Carnot-bounded 2nd-law approach combined with a Schmidt-style
// indicated-work-fraction heuristic:
//
//   η_Carnot   = 1 − T_cold / T_hot
//   η_indicated = η_2nd · η_Carnot
//   MEP        = 0.5 · P_mean              (Schmidt cluster fit)
//   W_cycle    = MEP · V_swept
//   P_indicated = W_cycle · f
//   Q_hot      = P_indicated / η_indicated
//   Q_cold     = Q_hot − P_indicated       (energy balance)
//
// Per-configuration phase-angle effects (Schmidt's full analytical
// solution with sinusoidal volume variations) deferred to STR.W2.
//
// References:
//   Walker G. (1980). "Stirling Engines." Clarendon Press.
//   Urieli I., Berchowitz D.M. (1984). "Stirling Cycle Engine
//     Analysis." Adam Hilger.
//   NASA TM-2010-216806 (ASRG Advanced Stirling Radioisotope Generator).

using System;

namespace Voxelforge.Stirling;

/// <summary>
/// Closed-form Stirling-engine performance snapshot solver (Sprint STR.W1).
/// </summary>
internal static class StirlingSolver
{
    /// <summary>
    /// Schmidt-style indicated-work-fraction coefficient [-]. Cluster
    /// mid-band 0.30-0.70; we anchor at 0.5 for the Wave-1 scaffold.
    /// Real Stirling indicators run higher for high-T configurations +
    /// lower for high-dead-volume designs.
    /// </summary>
    internal const double IndicatedWorkFractionOfMeanPressure = 0.5;

    /// <summary>
    /// Solve the Stirling-engine snapshot at the design (T_hot, T_cold,
    /// P_mean, V_swept, f) operating point.
    /// </summary>
    internal static StirlingResult Solve(StirlingDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        // 1. Carnot + real indicated efficiency. Sprint STR.W2 applies a
        //    per-working-fluid efficiency factor — Helium and Hydrogen
        //    are the modern high-performance defaults (factor 1.0); Air
        //    is derated by ~ 15 % due to lower thermal conductivity in
        //    the regenerator. The per-configuration (α / β / γ) penalty
        //    is captured by the cluster η_2nd input rather than as a
        //    separate factor here — configurations show up only in the
        //    result for reporting + UI / gate purposes.
        double fluidFactor = GetWorkingFluidEfficiencyFactor(design.WorkingFluid);
        double eta_carnot = 1.0 - design.ColdSideTemperature_K / design.HotSideTemperature_K;
        double eta_indicated = design.SecondLawEfficiency * fluidFactor * eta_carnot;

        // 2. Mean effective pressure + work-per-cycle.
        double mep = IndicatedWorkFractionOfMeanPressure * design.MeanPressure_Pa
                   * fluidFactor;
        double W_cycle = mep * design.SweptVolume_m3;

        // 3. Power roll-up.
        double P_indicated = W_cycle * design.OperatingFrequency_Hz;
        double Q_hot = eta_indicated > 0 ? P_indicated / eta_indicated : 0.0;
        double Q_cold = Q_hot - P_indicated;

        return new StirlingResult(
            CarnotEfficiency:           eta_carnot,
            IndicatedEfficiency:        eta_indicated,
            MeanEffectivePressure_Pa:   mep,
            WorkPerCycle_J:             W_cycle,
            IndicatedPower_W:           P_indicated,
            HeatInputRate_W:            Q_hot,
            HeatRejectionRate_W:        Q_cold);
    }

    /// <summary>
    /// Per-working-fluid efficiency-derating factor (Sprint STR.W2).
    /// Helium + Hydrogen are the modern-high-performance reference
    /// (factor 1.0 → no derating); Air's lower thermal conductivity in
    /// the regenerator costs ≈ 15 % vs the He/H₂ cluster. Public-static
    /// for tests + future per-fluid material-selection studies.
    /// </summary>
    internal static double GetWorkingFluidEfficiencyFactor(StirlingWorkingFluid fluid)
        => fluid switch
    {
        StirlingWorkingFluid.Helium   => 1.0,
        StirlingWorkingFluid.Hydrogen => 1.0,
        StirlingWorkingFluid.Air      => 0.85,
        _ => throw new ArgumentOutOfRangeException(nameof(fluid), fluid,
                $"Unknown StirlingWorkingFluid '{fluid}'."),
    };
}
