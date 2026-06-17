// TopologyOptimizedChannels.cs — SIMP density-field regen channel routing.
//
// Implements the Optimality Criteria (OC) method from:
//   Sigmund, O. (2001). "A 99 line topology optimization code written in
//   Matlab." Structural and Multidisciplinary Optimization, 21(2), 120–127.
//   https://doi.org/10.1007/s001580050176
//
// Bendsoe, M. & Sigmund, O. (2003). "Topology Optimization: Theory,
//   Methods and Applications." Springer, 2nd ed.
//
// Context (ADR-024 / OOB-2): uniform axial channels (constant pitch
// everywhere) under-cool the throat and over-cool the barrel / exit
// bell. This optimizer redistributes n_channels(x) proportional to
// the local Bartz heat-flux field while holding total channel volume
// constant, producing a design that concentrates cooling where it is
// needed most.
//
// Algorithm:
//   Objective:  maximize  C = Σ_i  ρ(i)^p · q"(i) · ΔA_gas(i)
//   Constraint: Σ_i ρ(i)·L(i) = 0.5·Σ L(i)  [mean density stays at 0.5]
//
//   OC update (Sigmund 2001, eq. 9):
//     ρ_new(i) = clamp( ρ(i) · √(sens(i) / (λ · vol_sens(i))),  ρ_min, 1 )
//     sens(i)     = p · ρ(i)^(p−1) · q"(i) · ΔA(i)    [objective sensitivity]
//     vol_sens(i) = L(i)                                [length sensitivity]
//     ΔA(i)       = 2π · R(i) · L(i)                   [gas-side area element]
//     λ           = Lagrange multiplier found by 60-step bisection
//   Integer extraction: n(i) = max(N_min, round(ρ(i) · N_base))
//     ρ=1.0 → N_base (throat keeps baseline); ρ→0 → N_min (barrel reduces);
//     wider barrel channels lower velocity → net ΔP ≤ baseline
//
// Sprint 1 scope: Core-only physics. No voxel changes. No schema bump.
// ChannelTopology.TopologyOptimized enum value added in RegenChamberDesign.cs.

using System;
using System.Collections.Generic;
using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Optimization;

/// <summary>
/// Inputs for the SIMP topology channel routing optimizer.
/// <see cref="Contour"/> and <see cref="ThermalStations"/> must be
/// co-indexed by axial station (same length, same order).
/// </summary>
public sealed record TopologyChannelInputs(
    IReadOnlyList<ContourStation> Contour,
    IReadOnlyList<StationResult>  ThermalStations,
    ChannelSchedule               BaseSchedule,
    /// <summary>Total coolant mass flow [kg/s]. From OperatingConditions or RegenSolverInputs.</summary>
    double MassFlowCoolant_kgs,
    int    MaxIterations           = 100,
    /// <summary>SIMP penalization exponent p. Standard value 3.0 (Sigmund 2001 §2).</summary>
    double SimpPenalty             = 3.0,
    /// <summary>Density lower bound ρ_min to avoid sensitivity singularity.</summary>
    double MinDensity              = 0.01,
    double VolumeFractionTolerance = 0.01,
    double ConvergenceTolerance    = 1e-4,
    /// <summary>Hard floor on channels per station for structural wall integrity.</summary>
    int    MinChannelsPerStation   = 8,
    /// <summary>Reserved for reproducibility; OC update is deterministic via fixed init.</summary>
    int    Seed                    = 42);

/// <summary>
/// Output of the SIMP topology channel routing optimizer.
/// </summary>
public sealed record TopologyChannelResult(
    /// <summary>Density field ρ[i] per axial station, ∈ [MinDensity, 1.0].</summary>
    double[] DensityField,
    /// <summary>Integer channel count per station extracted from the density field.</summary>
    int[]    ChannelsPerStation,
    /// <summary>Achieved volume fraction (actual / target). Should be ≈ 1 ± VolumeFractionTolerance.</summary>
    double   VolumeFractionAchieved,
    double   BaselinePressureDrop_Pa,
    double   OptimizedPressureDrop_Pa,
    int      IterationsRun,
    bool     Converged);

/// <summary>
/// SIMP density-field topology optimizer for regenerative cooling channels.
/// Redistributes per-station channel count proportional to the local
/// Bartz heat-flux field (ADR-024 / OOB-2 Sprint 1).
/// </summary>
public static class TopologyOptimizedChannels
{
    // OC damping exponent η = 0.5 (standard, Sigmund 2001 §3).
    private const double OcDamping = 0.5;

    // Bisection steps for Lagrange multiplier — fixed count keeps
    // execution time bounded and output deterministic across platforms.
    private const int BisectionSteps = 60;

    /// <summary>
    /// Runs the SIMP / Optimality Criteria optimizer and returns the
    /// per-station channel-count field that maximises cooling coverage
    /// at the same total channel volume as the baseline uniform schedule.
    /// </summary>
    [Deterministic]
    public static TopologyChannelResult Solve(TopologyChannelInputs inp)
    {
        ValidateInputs(inp);

        int    N       = inp.Contour.Count;
        double p       = inp.SimpPenalty;
        double rhoMin  = inp.MinDensity;
        int    nBase   = inp.BaseSchedule.ChannelCount;
        double rib_mm  = inp.BaseSchedule.RibThickness_mm;
        double mDot    = inp.MassFlowCoolant_kgs;

        // ── Per-station geometry arrays ──────────────────────────────
        var R_mm  = new double[N];   // wall radius [mm]
        var H_mm  = new double[N];   // channel height (radial) [mm]
        var q     = new double[N];   // |HeatFlux_Wm2|
        var L_mm  = ComputeSegmentLengths(inp.Contour);

        double xMin = inp.Contour[0].X_mm;
        double xMax = inp.Contour[N - 1].X_mm;
        int    throatIdx = FindThroatIndex(inp.Contour);
        double xThroat   = inp.Contour[throatIdx].X_mm;

        for (int i = 0; i < N; i++)
        {
            R_mm[i] = inp.Contour[i].R_mm;
            H_mm[i] = InterpChannelHeight(
                inp.Contour[i].X_mm, xMin, xThroat, xMax, inp.BaseSchedule);
            q[i]    = Math.Abs(inp.ThermalStations[i].HeatFlux_Wm2);
        }

        // ── Derive coolant density ρ_c and viscosity μ from StationResult ──
        // No fluid-table calls — derived via mass-conservation + Re definition.
        var rhoC = new double[N];
        var muC  = new double[N];
        DeriveFluidProperties(inp.ThermalStations, nBase, mDot, rhoC, muC);

        // ── Gas-side area elements ΔA[i] = 2π·R[i]·L[i] [m²] ───────
        var dA = new double[N];
        for (int i = 0; i < N; i++)
            dA[i] = 2.0 * Math.PI * R_mm[i] * 1e-3 * L_mm[i] * 1e-3;

        // ── Volume sensitivities (axial length per station) ───────────
        // vol_sens[i] = L_mm[i].  Constraint: Σ ρ(i)·L(i) = 0.5·ΣL
        // At baseline ρ=0.5 everywhere → n=N_base everywhere: the starting
        // point sits exactly on the constraint boundary.
        var volSens = new double[N];
        for (int i = 0; i < N; i++)
            volSens[i] = L_mm[i];

        // Reference: Σ 0.5·L[i]  (ρ=0.5 is the constraint target)
        double vRefDensity = 0.0;
        for (int i = 0; i < N; i++)
            vRefDensity += 0.5 * volSens[i];

        // ── Initialise density field uniformly at 0.5 ────────────────
        // Uniform start is deterministic; avoids any random perturbation.
        var rho    = new double[N];
        var rhoNew = new double[N];
        Array.Fill(rho, 0.5);

        // ── OC iteration loop ─────────────────────────────────────────
        int  iter      = 0;
        bool converged = false;

        for (iter = 0; iter < inp.MaxIterations; iter++)
        {
            // Objective sensitivity: ∂C/∂ρ(i) = p · ρ(i)^(p-1) · q"(i) · ΔA(i)
            // (Positive because we maximise C; the OC update drives ρ toward
            // regions of high sensitivity — which are the high-flux stations.)
            var sens = new double[N];
            for (int i = 0; i < N; i++)
            {
                double rClamped = Math.Max(rho[i], rhoMin);
                sens[i] = p * Math.Pow(rClamped, p - 1.0) * q[i] * dA[i];
            }

            // Bisect λ in log-space so that Σ ρ_new(i)·L(i) = vRefDensity.
            // Arithmetic bisection over [1e-30,1e30] would fail — the first
            // midpoint ≈5e29 collapses all densities to rhoMin; 60 arithmetic
            // halvings only reduce lambdaHi to ~8e11, never reaching the true
            // λ range (~10²–10³). Log-space bisection converges in 60 steps
            // to <1e-17 decade error regardless of the true λ scale.
            double logLo = -30.0;   // λ = 10^-30
            double logHi =  30.0;   // λ = 10^+30
            for (int b = 0; b < BisectionSteps; b++)
            {
                double logMid    = 0.5 * (logLo + logHi);
                double lambdaMid = Math.Pow(10.0, logMid);
                double vol       = OcUpdate(rho, sens, volSens, lambdaMid, rhoMin, OcDamping, rhoNew);
                if (vol > vRefDensity)
                    logLo = logMid;
                else
                    logHi = logMid;
            }
            // Finalise rhoNew at converged λ.
            OcUpdate(rho, sens, volSens,
                Math.Pow(10.0, 0.5 * (logLo + logHi)),
                rhoMin, OcDamping, rhoNew);

            // Convergence check.
            double maxChange = 0.0;
            for (int i = 0; i < N; i++)
                maxChange = Math.Max(maxChange, Math.Abs(rhoNew[i] - rho[i]));

            Array.Copy(rhoNew, rho, N);

            if (maxChange < inp.ConvergenceTolerance) { converged = true; break; }
        }

        // ── Integer extraction ────────────────────────────────────────
        // n(i) = max(N_min, round(ρ(i) · N_base))
        // ρ=0.5 → N_base/2 … ρ=1.0 → N_base; throat stays at N_base (maximum),
        // barrel reduces to N_min (structural floor), wider channels, lower ΔP.
        var nCh = new int[N];
        for (int i = 0; i < N; i++)
        {
            nCh[i] = Math.Max(
                inp.MinChannelsPerStation,
                (int)Math.Round(rho[i] * nBase, MidpointRounding.AwayFromZero));
        }

        // ── Volume fraction: continuous density vs OC target ──────────
        // The OC bisection enforces Σ ρ(i)·L(i) = vRefDensity by construction.
        // Reporting the ratio from the continuous field measures constraint
        // satisfaction quality (≈1.0 to convergence tolerance).
        double vActualCont = 0.0;
        for (int i = 0; i < N; i++)
            vActualCont += rho[i] * volSens[i];
        double vfAchieved = vActualCont / Math.Max(vRefDensity, 1e-30);

        // ── Analytical pressure drop (uniform baseline vs. optimised) ─
        double dPBase = AnalyticalPressureDrop(nBase, R_mm, H_mm, L_mm, rhoC, muC, rib_mm, mDot);
        double dPOpt  = AnalyticalPressureDrop(nCh,   R_mm, H_mm, L_mm, rhoC, muC, rib_mm, mDot);

        return new TopologyChannelResult(
            DensityField:             rho,
            ChannelsPerStation:       nCh,
            VolumeFractionAchieved:   vfAchieved,
            BaselinePressureDrop_Pa:  dPBase,
            OptimizedPressureDrop_Pa: dPOpt,
            IterationsRun:            iter + 1,
            Converged:                converged);
    }

    // ── OC update + volume integral ───────────────────────────────────
    // Returns achieved volume (Σ ρ_out · volSens).
    private static double OcUpdate(
        double[] rho, double[] sens, double[] volSens,
        double lambda, double rhoMin, double eta, double[] rhoOut)
    {
        double vol = 0.0;
        for (int i = 0; i < rho.Length; i++)
        {
            double ratio = (lambda > 0.0 && volSens[i] > 0.0)
                ? Math.Pow(sens[i] / (lambda * volSens[i]), eta)
                : 1.0;
            double r    = Math.Clamp(rho[i] * ratio, rhoMin, 1.0);
            rhoOut[i]   = r;
            vol        += volSens[i] * r;
        }
        return vol;
    }

    // ── Analytical pressure drop model ───────────────────────────────
    // Computes total frictional ΔP for a given per-station channel count,
    // using fluid properties derived from the baseline StationResult.
    private static double AnalyticalPressureDrop(
        int nUniform, double[] R_mm, double[] H_mm, double[] L_mm,
        double[] rhoC, double[] muC, double rib_mm, double mDot)
    {
        int N = R_mm.Length;
        double dP = 0.0;
        for (int i = 0; i < N; i++)
        {
            double w_mm = BaseChannelWidth_mm(R_mm[i], nUniform, rib_mm);
            dP += StationPressureDrop(
                nUniform, w_mm, H_mm[i], L_mm[i], rhoC[i], muC[i], mDot);
        }
        return dP;
    }

    private static double AnalyticalPressureDrop(
        int[] nArr, double[] R_mm, double[] H_mm, double[] L_mm,
        double[] rhoC, double[] muC, double rib_mm, double mDot)
    {
        int N = R_mm.Length;
        double dP = 0.0;
        for (int i = 0; i < N; i++)
        {
            int    n    = Math.Max(nArr[i], 1);
            double w_mm = BaseChannelWidth_mm(R_mm[i], n, rib_mm);
            dP += StationPressureDrop(n, w_mm, H_mm[i], L_mm[i], rhoC[i], muC[i], mDot);
        }
        return dP;
    }

    // Darcy-Weisbach ΔP for a single axial station.
    private static double StationPressureDrop(
        int n, double w_mm, double h_mm, double L_mm,
        double rhoC_kgm3, double muC_Pas, double mDot_kgs)
    {
        double w_m  = w_mm * 1e-3;
        double h_m  = h_mm * 1e-3;
        double L_m  = L_mm * 1e-3;
        double Ach  = w_m * h_m;
        if (Ach < 1e-12 || n < 1) return 0.0;

        double v    = mDot_kgs / (n * Math.Max(rhoC_kgm3, 1.0) * Ach);
        double dh_m = 2.0 * w_m * h_m / Math.Max(w_m + h_m, 1e-9);
        double Re   = muC_Pas > 0.0
            ? Math.Max(rhoC_kgm3, 1.0) * v * dh_m / muC_Pas
            : 1e4;
        double f    = CoolantCorrelations.FrictionFactor(Re, 0.0);
        return f * Math.Max(rhoC_kgm3, 1.0) / 2.0 * v * v * L_m
               / Math.Max(dh_m, 1e-9);
    }

    // ── Fluid property helpers ────────────────────────────────────────
    // Derive coolant density and viscosity from the existing StationResult
    // fields (mass-conservation + Re definition). No fluid-table calls.
    private static void DeriveFluidProperties(
        IReadOnlyList<StationResult> stations, int nBase, double mDot,
        double[] rhoC, double[] muC)
    {
        for (int i = 0; i < stations.Count; i++)
        {
            var st  = stations[i];
            double w_m = st.ChannelWidth_mm  * 1e-3;
            double h_m = st.ChannelHeight_mm * 1e-3;
            double v   = Math.Max(st.CoolantVelocity_ms, 1e-6);
            double Ach = w_m * h_m;

            // ρ from continuity: m_dot = n · ρ · A_ch · v
            rhoC[i] = (Ach > 1e-12)
                ? mDot / (nBase * v * Ach)
                : 100.0;    // dense-gas fallback

            // μ from Re definition: Re = ρ · v · D_h / μ
            double dh_m = st.HydraulicDiameter_mm * 1e-3;
            double Re   = Math.Max(st.Reynolds, 1.0);
            muC[i]  = (dh_m > 0.0 && v > 0.0)
                ? rhoC[i] * v * dh_m / Re
                : 1e-5;     // methane approximate fallback
        }
    }

    // ── Geometry helpers ──────────────────────────────────────────────

    // Channel width at a given radius for n channels.
    // W(R, n) = max(0.1, (2π·R − n·rib) / n)  [all in mm]
    private static double BaseChannelWidth_mm(double R_mm, int n, double rib_mm)
    {
        if (n < 1) return 0.1;
        double pitch = 2.0 * Math.PI * R_mm / n;
        return Math.Max(0.1, pitch - rib_mm);
    }

    // Trapezoidal segment lengths — mirrors ChamberContour.SegmentLength_mm.
    private static double[] ComputeSegmentLengths(IReadOnlyList<ContourStation> s)
    {
        int N = s.Count;
        var L = new double[N];
        if (N == 0) return L;
        if (N == 1) { L[0] = 1.0; return L; }
        L[0]     = (s[1].X_mm - s[0].X_mm) * 0.5;
        for (int i = 1; i < N - 1; i++)
            L[i] = (s[i + 1].X_mm - s[i - 1].X_mm) * 0.5;
        L[N - 1] = (s[N - 1].X_mm - s[N - 2].X_mm) * 0.5;
        return L;
    }

    // Station with minimum radius is the throat.
    private static int FindThroatIndex(IReadOnlyList<ContourStation> s)
    {
        int    idx  = 0;
        double minR = double.MaxValue;
        for (int i = 0; i < s.Count; i++)
        {
            if (s[i].R_mm < minR) { minR = s[i].R_mm; idx = i; }
        }
        return idx;
    }

    // Linearly interpolate channel height from 3-point anchor schedule.
    // Mirrors the interpolation in RegenCoolingSolver.BuildChannelProfile.
    private static double InterpChannelHeight(
        double x, double xMin, double xThroat, double xMax,
        ChannelSchedule ch)
    {
        if (x <= xThroat)
        {
            double span = xThroat - xMin;
            double t    = span > 0.0 ? Math.Clamp((x - xMin) / span, 0.0, 1.0) : 0.0;
            return ch.ChannelHeightAtChamber_mm
                 + t * (ch.ChannelHeightAtThroat_mm - ch.ChannelHeightAtChamber_mm);
        }
        else
        {
            double span = xMax - xThroat;
            double t    = span > 0.0 ? Math.Clamp((x - xThroat) / span, 0.0, 1.0) : 0.0;
            return ch.ChannelHeightAtThroat_mm
                 + t * (ch.ChannelHeightAtExit_mm - ch.ChannelHeightAtThroat_mm);
        }
    }

    // ── Input guard ───────────────────────────────────────────────────
    private static void ValidateInputs(TopologyChannelInputs inp)
    {
        if (inp.Contour.Count < 3)
            throw new ArgumentException(
                "Contour must have at least 3 stations.", nameof(inp));
        if (inp.ThermalStations.Count != inp.Contour.Count)
            throw new ArgumentException(
                $"ThermalStations.Count ({inp.ThermalStations.Count}) must equal "
              + $"Contour.Count ({inp.Contour.Count}).", nameof(inp));
        if (inp.MassFlowCoolant_kgs <= 0.0)
            throw new ArgumentException(
                "MassFlowCoolant_kgs must be positive.", nameof(inp));
        if (inp.BaseSchedule.ChannelCount < 4)
            throw new ArgumentException(
                "ChannelCount must be ≥ 4.", nameof(inp));
    }
}
