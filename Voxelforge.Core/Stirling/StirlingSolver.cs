// StirlingSolver.cs — Sprint STR.W1 closed-form Stirling-engine
// performance snapshot. Issue #84 (2026-07-19): MEP model replaced with
// West's number correlation (STR.W2).
//
// Stateless, allocation-free, deterministic. The model uses a
// Carnot-bounded 2nd-law approach combined with West's number, an
// empirical whole-engine power correlation with a built-in temperature-
// ratio term:
//
//   η_Carnot    = 1 − T_cold / T_hot
//   η_indicated = η_2nd · η_Carnot
//   τ           = (T_hot − T_cold) / (T_hot + T_cold)     (West's temperature-ratio term)
//   MEP         = Wn · P_mean · τ · fluidFactor            (West's number correlation)
//   W_cycle     = MEP · V_swept
//   P_indicated = W_cycle · f
//   Q_hot       = P_indicated / η_indicated
//   Q_cold      = Q_hot − P_indicated       (energy balance)
//
// Why West's number, not the flat "MEP = 0.5·P_mean" Schmidt-cluster fit
// this replaces: the old constant had NO dependence on temperature
// differential at all, so a barely-warm design (τ → 0, where a real
// Stirling engine's net power correctly → 0) still produced the same
// MEP as a high-ΔT design — an unbounded relative error as τ shrinks,
// which is almost certainly why the pre-fix model's over-prediction was
// reported as spanning as wide a range as 10-100× depending on the
// anchor design's ΔT, not a single fixed factor. West's number fixes
// this by construction (MEP scales with τ).
//
// Calibration: Wn ≈ 0.25 is the documented average across a wide variety
// of Stirling engines, ranging up to ≈ 0.35 for well-engineered
// high-temperature-differential designs (West, C.D. (1986), "Principles
// and Applications of Stirling Engines," Van Nostrand Reinhold — the
// standard citation for this correlation; the companion "Beale number"
// Bn = P/(P_mean·V_swept·f) omits the τ term and clusters 0.11-0.15 for
// the same engine population). Cross-checked against the GM/NASA GPU-3
// rhombic-drive test engine's published high-power helium operating
// point (P_mean ≈ 4.13 MPa, f ≈ 41.72 Hz, ~120 cm³ swept volume, ~650-
// 700 °C hot / ~13 °C cold, 3958 W measured) — back-solving West's
// formula for Wn at that specific point gives ≈ 0.35-0.37, consistent
// with "up to 0.35 for high-ΔT engines" and confirming Wn = 0.25 is a
// conservative (not cherry-picked-optimistic) central estimate rather
// than an overfit to one data point.
//
// Per-configuration phase-angle effects (Schmidt's full analytical
// solution with sinusoidal volume variations) remain deferred.
//
// References:
//   West, C.D. (1986). "Principles and Applications of Stirling
//     Engines." Van Nostrand Reinhold — West's number correlation.
//   Walker G. (1980). "Stirling Engines." Clarendon Press.
//   Urieli I., Berchowitz D.M. (1984). "Stirling Cycle Engine
//     Analysis." Adam Hilger.
//   NASA TM-2010-216806 (ASRG Advanced Stirling Radioisotope Generator).
//   NASA/DOE GPU-3 baseline + high-power test reports (General Motors
//     rhombic-drive reference engine, NASA Technical Reports Server —
//     used for the cross-check above; exact TM number not re-verified
//     here, several exist covering different GPU-3 test campaigns).

using System;

namespace Voxelforge.Stirling;

/// <summary>
/// Closed-form Stirling-engine performance snapshot solver (Sprint STR.W1).
/// </summary>
internal static class StirlingSolver
{
    /// <summary>
    /// Issue #84: West's number [-], the empirical whole-engine power
    /// correlation (see file header). 0.25 is the documented average
    /// across a wide variety of Stirling engines; well-engineered
    /// high-ΔT designs range up to ≈ 0.35. Combined with the τ =
    /// (T_hot−T_cold)/(T_hot+T_cold) temperature-ratio term at the call
    /// site, this replaces the STR.W1 flat "MEP = 0.5·P_mean" fit, which
    /// had no ΔT-dependence at all.
    /// </summary>
    internal const double WestNumber = 0.25;

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

        // 2. Mean effective pressure + work-per-cycle. Issue #84: West's
        //    number correlation, MEP = Wn · P_mean · τ · fluidFactor,
        //    where τ is the same temperature-ratio term used throughout
        //    the Stirling-engine literature for this correlation (see
        //    file header for the derivation + calibration cross-check).
        double tau = (design.HotSideTemperature_K - design.ColdSideTemperature_K)
                   / (design.HotSideTemperature_K + design.ColdSideTemperature_K);
        double mep = WestNumber * design.MeanPressure_Pa * tau * fluidFactor;
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
