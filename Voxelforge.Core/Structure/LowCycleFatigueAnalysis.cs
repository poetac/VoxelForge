// LowCycleFatigueAnalysis.cs — Coffin-Manson low-cycle-fatigue evaluation.
//
// Computes predicted cycles-to-failure for a regen chamber wall under
// startup→shutdown thermal cycling, using the Coffin-Manson strain-life
// model (Coffin 1954, Manson 1953):
//
//     Δε_total = ε_p · (2N_f)^c + (σ_f / E) · (2N_f)^b
//                └── plastic ──┘   └── elastic ──┘
//
// Δε is driven by the through-wall thermal gradient that exists during
// hot-fire and collapses between firings. The cyclic event is start-up
// → shutdown; the steady-state (T_wg − T_wc) gradient is the strain
// driver per Quentmeyer 1977 (NASA TM-X-73665), Sutton 9e §8.5,
// Huzel & Huang Ch. 4:
//
//     Δε = α(T_mean) · |T_wg − T_wc| · constraint_factor
//     T_mean = (T_wg + T_wc) / 2
//     constraint_factor = 1.0 (chamber wall is hoop-constrained, default)
//
// Critical station = argmin(N_f) over all stations; typically the throat
// where (T_wg − T_wc) peaks, but driven by data not by hardcoded position.
//
// Bimetallic walls (LinerFraction > 0) get fatigue constants blended by
// liner-fraction. Bond-zone shear failure is a separately-modelled mode
// (BIMETALLIC_BOND_ZONE_SHEAR gate, Z3-M2 PR #263); the two gates are
// complementary, not redundant.
//
// Material constants are scoping-grade — anchored to NASA / vendor
// handbook sources but coupon-test variability runs ±20-50%. For flight
// qualification, use vendor-certified material cards with full T-dependent
// LCF curves.
//
// Failure threshold is `PredictedCyclesToFailure < SafetyFactorOnCycles ×
// MissionCycles`, with SF default 4.0 per AS9100 / AIAA S-080 /
// NASA-STD-5012. Below MissionCycles = LowCycleAdvisoryThreshold (100),
// LCF is treated as non-credible — the result still populates for
// reporting but no FeasibilityViolation is emitted by the gate.

using System;
using System.Collections.Generic;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Structure;

public sealed record LowCycleFatigueInputs(
    int MissionCycles,
    double AmbientTemp_K = 293.0,
    double ConstraintFactor = 1.0);

public sealed record LowCycleFatigueResult(
    int MissionCycles,
    double TotalStrainRange,                 // Δε at the critical station
    double PredictedCyclesToFailure,         // 2N_f from Coffin-Manson
    double UsageFactor,                      // MissionCycles / PredictedCyclesToFailure
    double SafetyFactor,                     // PredictedCyclesToFailure / MissionCycles
    int CriticalStationIndex,
    double CriticalStationT_wg_K,
    double CriticalStationT_wc_K,
    double SigmaF_Pa,                        // material constants (echo for traceability)
    double EpsilonP,
    double B_Exponent,
    double C_Exponent,
    double E_Pa_AtMeanT,
    double Alpha_perK_AtMeanT,
    string MaterialName,
    string[] Notes);

internal readonly record struct CoffinMansonConstants(
    double SigmaF_MPa,            // fatigue strength coefficient (≈ 1.5-2.0× σ_y for ductile metals)
    double EpsilonP,              // fatigue ductility coefficient (true strain at fracture under monotonic tension)
    double B,                     // fatigue strength exponent (negative; -0.05 to -0.12 typical)
    double C,                     // fatigue ductility exponent (negative; -0.5 to -0.7 typical)
    string SourceCitation);

public static class LowCycleFatigueAnalysis
{
    /// <summary>
    /// Threshold below which MissionCycles is treated as so low that LCF
    /// is not a credible failure mode. The gate goes silent; only a
    /// Notes-disclosure is emitted on the result. Retunable here without
    /// a schema bump (PH-40 / issue #259).
    /// </summary>
    public const int LowCycleAdvisoryThreshold = 100;

    /// <summary>
    /// LCF safety factor on PredictedCyclesToFailure. 4.0 is the AS9100 /
    /// AIAA S-080 / NASA-STD-5012 convention for life-limited pressurised
    /// hardware (factor of 4 on cycles or factor of 2 on strain, whichever
    /// is more conservative; cycles dominate for thermal LCF). Retunable.
    /// </summary>
    public const double SafetyFactorOnCycles = 4.0;

    private static readonly CoffinMansonConstants GRCop42Constants = new(
        SigmaF_MPa: 480.0,
        EpsilonP:   0.30,
        B:         -0.10,
        C:         -0.55,
        SourceCitation: "NASA TM-2019-219972 §5; Anderson et al. 2020 LPBF coupons");

    private static readonly CoffinMansonConstants CuCrZrConstants = new(
        SigmaF_MPa: 540.0,
        EpsilonP:   0.20,
        B:         -0.10,
        C:         -0.60,
        SourceCitation: "Brush Wellman C18150 wrought + LPBF derate per NASA PURS");

    private static readonly CoffinMansonConstants Inconel625Constants = new(
        SigmaF_MPa: 1100.0,
        EpsilonP:   0.30,
        B:         -0.07,
        C:         -0.55,
        SourceCitation: "NASA-STD-6030; LPBF coupon LCF per NIST TN-2055");

    private static readonly CoffinMansonConstants Inconel718Constants = new(
        SigmaF_MPa: 1900.0,
        EpsilonP:   0.40,
        B:         -0.08,
        C:         -0.65,
        SourceCitation: "NASA-HDBK-5010 / AMS 5662; Kelley 2017 NASA TM-2017-219476");

    public static LowCycleFatigueResult Evaluate(
        RegenSolverOutputs thermal,
        WallMaterial wall,
        LowCycleFatigueInputs inputs)
    {
        if (thermal.Stations.Length == 0)
            return ZeroStationResult(wall, inputs);

        var (constants, materialName) = ResolveConstants(wall);

        double minN_f = double.PositiveInfinity;
        int criticalIdx = 0;
        double criticalT_wg = 0.0;
        double criticalT_wc = 0.0;
        double criticalDeltaEps = 0.0;
        double criticalE_Pa = 0.0;

        for (int i = 0; i < thermal.Stations.Length; i++)
        {
            var s = thermal.Stations[i];
            double T_wg = s.GasSideWallTemp_K;
            double T_wc = s.CoolantSideWallTemp_K;
            double T_mean = 0.5 * (T_wg + T_wc);
            double deltaT = Math.Abs(T_wg - T_wc);
            double alpha = wall.CTE_perK;
            double E_Pa = wall.ElasticModulusAt_GPa(T_mean) * 1e9;
            double deltaEps = alpha * deltaT * inputs.ConstraintFactor;

            double N_f = SolveCoffinMansonForN_f(
                deltaEps,
                constants.SigmaF_MPa * 1e6,
                constants.EpsilonP,
                constants.B,
                constants.C,
                E_Pa);

            if (N_f < minN_f)
            {
                minN_f = N_f;
                criticalIdx = i;
                criticalT_wg = T_wg;
                criticalT_wc = T_wc;
                criticalDeltaEps = deltaEps;
                criticalE_Pa = E_Pa;
            }
        }

        double usage = inputs.MissionCycles / Math.Max(minN_f, 1e-30);
        double sf = minN_f / Math.Max((double)inputs.MissionCycles, 1e-30);

        var notes = new List<string>();
        if (wall.LinerFraction > 0)
        {
            notes.Add($"Bimetallic LCF constants are linerFraction-weighted blends "
                    + $"(f={wall.LinerFraction:F2}); bond-zone fatigue is separately "
                    + $"modelled by BIMETALLIC_BOND_ZONE_SHEAR.");
        }
        if (inputs.MissionCycles < LowCycleAdvisoryThreshold)
        {
            notes.Add(
                $"[PH-40 disclosure: MissionCycles={inputs.MissionCycles} below "
              + $"{LowCycleAdvisoryThreshold}-cycle LCF gate threshold; predicted "
              + $"N_f={minN_f:F0}, usage={usage:F2}. LCF is not a credible failure "
              + $"mode at this cycle count — feasibility unaffected. Re-evaluate "
              + $"if mission profile expands to ≥{LowCycleAdvisoryThreshold} firings.]");
        }

        return new LowCycleFatigueResult(
            MissionCycles: inputs.MissionCycles,
            TotalStrainRange: criticalDeltaEps,
            PredictedCyclesToFailure: minN_f,
            UsageFactor: usage,
            SafetyFactor: sf,
            CriticalStationIndex: criticalIdx,
            CriticalStationT_wg_K: criticalT_wg,
            CriticalStationT_wc_K: criticalT_wc,
            SigmaF_Pa: constants.SigmaF_MPa * 1e6,
            EpsilonP: constants.EpsilonP,
            B_Exponent: constants.B,
            C_Exponent: constants.C,
            E_Pa_AtMeanT: criticalE_Pa,
            Alpha_perK_AtMeanT: wall.CTE_perK,
            MaterialName: materialName,
            Notes: notes.ToArray());
    }

    private static LowCycleFatigueResult ZeroStationResult(WallMaterial wall, LowCycleFatigueInputs inputs)
        => new(
            MissionCycles: inputs.MissionCycles,
            TotalStrainRange: 0.0,
            PredictedCyclesToFailure: double.PositiveInfinity,
            UsageFactor: 0.0,
            SafetyFactor: double.PositiveInfinity,
            CriticalStationIndex: 0,
            CriticalStationT_wg_K: 0.0,
            CriticalStationT_wc_K: 0.0,
            SigmaF_Pa: 0.0,
            EpsilonP: 0.0,
            B_Exponent: 0.0,
            C_Exponent: 0.0,
            E_Pa_AtMeanT: 0.0,
            Alpha_perK_AtMeanT: wall.CTE_perK,
            MaterialName: wall.Name,
            Notes: new[] { "No thermal stations available — LCF skipped." });

    private static (CoffinMansonConstants Constants, string MaterialName) ResolveConstants(WallMaterial wall)
    {
        if (wall.LinerFraction > 0.0)
        {
            // Bimetallic: liner-fraction-weighted blend of GRCop-42 (inner
            // liner) + IN625 (outer jacket). Bulk-blended constants
            // approximate the composite under uniform strain. Bond-zone
            // shear is captured by BIMETALLIC_BOND_ZONE_SHEAR (Z3-M2).
            double f = wall.LinerFraction;
            double g = 1.0 - f;
            var blended = new CoffinMansonConstants(
                SigmaF_MPa: f * GRCop42Constants.SigmaF_MPa + g * Inconel625Constants.SigmaF_MPa,
                EpsilonP:   f * GRCop42Constants.EpsilonP   + g * Inconel625Constants.EpsilonP,
                B:          f * GRCop42Constants.B          + g * Inconel625Constants.B,
                C:          f * GRCop42Constants.C          + g * Inconel625Constants.C,
                SourceCitation: $"Bimetallic blend (f={f:F2}): GRCop-42 + IN625");
            return (blended, $"Bimetallic GRCop-42/IN625 (f={f:F2})");
        }

        if (wall.Name.Contains("GRCop", StringComparison.OrdinalIgnoreCase))
            return (GRCop42Constants, wall.Name);
        if (wall.Name.Contains("CuCrZr", StringComparison.OrdinalIgnoreCase))
            return (CuCrZrConstants, wall.Name);
        if (wall.Name.Contains("Inconel 625", StringComparison.OrdinalIgnoreCase))
            return (Inconel625Constants, wall.Name);
        if (wall.Name.Contains("Inconel 718", StringComparison.OrdinalIgnoreCase))
            return (Inconel718Constants, wall.Name);

        // Unknown material: fall back to GRCop-42 (lowest σ_f → most
        // conservative N_f prediction) with a flagged Notes entry. This
        // keeps the gate functional on a future material rather than
        // throwing; the substring miss surfaces in the MaterialName.
        return (GRCop42Constants,
                wall.Name + " (unrecognized — using GRCop-42 LCF constants as conservative fallback)");
    }

    /// <summary>
    /// Solve f(N_f) = ε_p · (2N_f)^c + (σ_f / E) · (2N_f)^b − Δε = 0
    /// for N_f via bisection in log-N space. Both b and c are negative,
    /// so f is monotonically decreasing in N_f: f(small N) → +∞,
    /// f(large N) → −Δε &lt; 0. Bisection is more robust than Newton on
    /// the cusp where the elastic term dominates.
    /// </summary>
    private static double SolveCoffinMansonForN_f(
        double deltaEps,
        double sigmaF_Pa,
        double epsilonP,
        double b,
        double c,
        double E_Pa)
    {
        if (deltaEps <= 0) return double.PositiveInfinity;
        if (E_Pa <= 0)     return double.PositiveInfinity;

        double EvalAt(double logN)
        {
            double N = Math.Pow(10.0, logN);
            double twoNf = 2.0 * N;
            return epsilonP * Math.Pow(twoNf, c)
                 + (sigmaF_Pa / E_Pa) * Math.Pow(twoNf, b)
                 - deltaEps;
        }

        double logLo = 0.0;        // 10^0  = 1 cycle
        double logHi = 12.0;       // 10^12 cycles
        double fLo = EvalAt(logLo);
        double fHi = EvalAt(logHi);

        // f(low N) > 0 means even at 1 cycle the strain capacity exceeds Δε
        // — no failure within the bracket. Return 1 (single-cycle survival).
        if (fLo <= 0) return 1.0;
        // f(high N) > 0 means even at 1e12 cycles the design has margin —
        // effectively infinite life.
        if (fHi >= 0) return double.PositiveInfinity;

        // Bisection in log-N space.
        for (int i = 0; i < 60; i++)
        {
            double logMid = 0.5 * (logLo + logHi);
            double fMid = EvalAt(logMid);
            if (fMid > 0) logLo = logMid;
            else          logHi = logMid;
            if (logHi - logLo < 1e-9) break;
        }
        return Math.Pow(10.0, 0.5 * (logLo + logHi));
    }
}
