// AerospikePlugCooling.cs — Simplified regen-cooling thermal solver
// for an aerospike plug.
//
// Scope
// ─────
// One-dimensional Bartz-like march along the plug's external surface,
// with a per-station energy balance across the plug wall stack:
//   gas-side (Bartz h_g) ── wall (k / t_wall) ── coolant-side (Dittus-
//   Boelter h_c) ── coolant bulk.
//
// Simplifications vs. the regen-chamber RegenCoolingSolver
// ────────────────────────────────────────────────────────
//   • No axial wall-conduction pass (plug is short + thermally
//     forgiving; pass is a follow-on if measured data demands it).
//   • No fin-efficiency correction (plug channels are short; fin
//     effect < 5 % on h_c).
//   • No radial profile integral — we stamp peak wall T + coolant
//     outlet T + ΔP as scalars.
//   • Gas-side Bartz uses the PLUG LOCAL RADIUS as the diameter input
//     (a crude analog to the chamber's throat-diameter convention).
//     This captures the falling dynamic pressure along the plug
//     expansion; accuracy likely ±30 % pending CFD ground-truth, but
//     adequate for a 16th-gate peak-wall-T floor.
//
// Output
// ──────
// `AerospikeThermalResult` record with per-station arrays + summary
// scalars. Peak wall T drives the plug-wall-temp feasibility gate.

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.Geometry;

namespace Voxelforge.HeatTransfer;

/// <summary>
/// Inputs to the plug-cooling solver. Mirrors the regen-side
/// <see cref="RegenSolverInputs"/> record shape but is narrower —
/// aerospike plug cooling uses a single axial march along the plug
/// external surface, with no film-cooling envelope or axial
/// wall-conduction pass in the MVP.
/// </summary>
/// <param name="Contour">Parametric plug contour to march along.</param>
/// <param name="Gas">Stagnation combustion-gas state (from <c>PropellantTables.Lookup</c>).</param>
/// <param name="Wall">Wall material (<c>k(T)</c>, service limit, density).</param>
/// <param name="ChannelCount">Number of axial channels around the plug.</param>
/// <param name="ChannelWidth_mm">Single-channel circumferential width.</param>
/// <param name="ChannelDepth_mm">Single-channel radial depth (into plug).</param>
/// <param name="PlugWallThickness_mm">Gas-side wall thickness between gas and channel.</param>
/// <param name="CoolantMassFlow_kgs">Total coolant mass flow through all channels combined.</param>
/// <param name="CoolantInletTemp_K">Coolant inlet (cold) temperature.</param>
/// <param name="CoolantInletPressure_Pa">Coolant inlet pressure (before the ΔP integral).</param>
/// <param name="CoolantFluid">Coolant fluid reference (density, enthalpy, viscosity, etc.).</param>
public sealed record AerospikePlugCoolingInputs(
    AerospikeContour Contour,
    PropellantState Gas,
    WallMaterial Wall,
    int ChannelCount,
    double ChannelWidth_mm,
    double ChannelDepth_mm,
    double PlugWallThickness_mm,
    double CoolantMassFlow_kgs,
    double CoolantInletTemp_K,
    double CoolantInletPressure_Pa,
    ICoolantFluid CoolantFluid);

public static class AerospikePlugCooling
{
    /// <summary>
    /// Coolant pressure floor (Pa) used by the plug march. When the
    /// running coolant static pressure falls
    /// below this value, we clamp to the floor (avoiding negative
    /// pressure / divide-by-zero downstream), count the station, and
    /// surface both a warning AND a telemetry scalar on
    /// <see cref="AerospikeThermalResult"/> so
    /// <see cref="Geometry.AerospikeFeasibility.Evaluate"/> can emit
    /// the <c>AEROSPIKE_COOLANT_CAVITATION_RISK</c> feasibility gate.
    /// 0.1 MPa ≈ 1 atm — below this the coolant is at or below
    /// saturation for every cryogen in the library (LCH4 Tₛₐₜ ≈ 112 K
    /// at 1 atm, LH2 Tₛₐₜ ≈ 20 K at 1 atm) and cavitation / film
    /// boiling becomes a credible failure mode.
    /// </summary>
    public const double CavitationPressureFloor_Pa = 0.1e6;

    /// <summary>
    /// Solve the 1-D plug-cooling energy balance. Returns an
    /// <see cref="AerospikeThermalResult"/> populated with per-
    /// station wall T / bulk T / q arrays + summary scalars.
    /// </summary>
    public static AerospikeThermalResult Solve(AerospikePlugCoolingInputs inp)
    {
        var stations = inp.Contour.Stations;
        int N = stations.Length;
        if (N == 0)
            return new AerospikeThermalResult(
                GasSideWallT_K:  System.Array.Empty<double>(),
                CoolantBulkT_K:  System.Array.Empty<double>(),
                HeatFlux_Wm2:    System.Array.Empty<double>(),
                PeakGasSideWallT_K:    0,
                PeakStation_X_mm:      0,
                CoolantOutletT_K:      inp.CoolantInletTemp_K,
                CoolantPressureDrop_Pa: 0,
                TotalHeatLoad_W:       0,
                Warnings:              new[] { "Empty contour." });

        var T_wg = new double[N];
        var T_bulk = new double[N];
        var q = new double[N];
        // Pre-size at 4 — see RegenCoolingSolver.
        var warnings = new System.Collections.Generic.List<string>(4);

        double T_bulk_run = inp.CoolantInletTemp_K;
        double P_bulk_run = inp.CoolantInletPressure_Pa;
        double H_bulk = inp.CoolantFluid.GetState(T_bulk_run, P_bulk_run).Enthalpy_Jkg;
        double totalQ = 0;
        double peakTwg = 0;
        double peakX = 0;
        double mDotPerChannel = inp.CoolantMassFlow_kgs
                              / System.Math.Max(inp.ChannelCount, 1);
        // Cavitation-risk telemetry. Every time the coolant static
        // pressure drops below `CavitationPressureFloor_Pa`, we count the
        // station and track the minimum pressure seen along the march so
        // the feasibility gate can surface a meaningful "how bad" scalar.
        int cavitationRiskStations = 0;
        double minPressure = inp.CoolantInletPressure_Pa;

        // Throat reference: use the plug-tip outer radius as the "throat
        // diameter" surrogate for Bartz. For an aerospike, the true
        // hydraulic scale is the annulus thickness 2·(R_o − R_i) — using
        // 2·R_o here biases h_g via the D_t^−0.2 term but is consistent
        // with the chamber-side convention.
        //
        // PH-41 (2026-04-29): set rCurv = D_ref so the (D_t/r_c)^0.1
        // curvature enhancement collapses to 1.0. The plug nozzle has no
        // longitudinal "throat curvature" in the bell-nozzle sense
        // (Bartz's r_c is the throat-region wall curvature). Pre-PH-41
        // rCurv = 0.5·D_ref gave (D_t/r_c)^0.1 = 2^0.1 ≈ 1.072, a 7 %
        // fictitious enhancement with no physical basis (file header
        // already flags ±30 % uncertainty pending CFD).
        double D_ref_m = 2.0 * inp.Contour.ThroatOuterRadius_mm * 1e-3;
        double rCurv_m = D_ref_m;     // (D_t/r_c) = 1.0 → no curvature enhancement

        for (int i = 0; i < N; i++)
        {
            var s = stations[i];
            double rSurf_mm = s.R_inner_mm;
            if (rSurf_mm <= 0) { T_wg[i] = T_bulk_run; T_bulk[i] = T_bulk_run; q[i] = 0; continue; }

            // PH-42 (#187, 2026-04-29): consume the per-station
            // FlowAngle_rad from the contour generator instead of
            // duplicating its linear-Prandtl-Meyer assumption inline.
            // The contour records `FlowAngle_rad = ν_exit − ν_local`
            // at every station (Sprint 31 / PH-15). Algebraically:
            //     ν_local = ν_exit − FlowAngle_rad
            // Pre-PH-42 the cooling solver computed ν_local from a
            // separate `ν_exit · (x / L_full)` ramp — the same Angelino
            // approximation but evaluated independently, so a future
            // upgrade of the contour generator (e.g. real MoC characteristic-
            // net per option (a) of #187) would have left the cooling
            // solver still on the linear ramp. Wiring the solver through
            // FlowAngle_rad future-proofs that follow-up.
            //
            // Note: this is option (b) of #187 — flag the approximation,
            // structurally rewire so the upgrade path is one-step.
            // Quantitative accuracy of M_local along the plug stays at
            // the Angelino ±25 % per-station / ~zero plug-integral
            // wetted-area level documented in the file header. The
            // CFD-derived table that would supersede this lives behind
            // T2.3 (#160). Until then, every Solve() call emits a Note
            // documenting the limitation (see the Notes-emit block at
            // the bottom of the function).
            double nuLocal;
            if (inp.Contour.DesignExitMach > 1)
            {
                double nuExit = AerospikeContourGenerator.PrandtlMeyer(
                    inp.Contour.DesignExitMach, inp.Gas.Gamma);
                nuLocal = System.Math.Max(0.0, nuExit - s.FlowAngle_rad);
            }
            else
            {
                nuLocal = 0;
            }
            double M_local = AerospikeContourGenerator.SolveMachFromPrandtlMeyer(nuLocal, inp.Gas.Gamma);

            double T_static = PropellantTables.StaticTemp(inp.Gas.ChamberTemp_K, M_local, inp.Gas.Gamma);
            double T_aw = PropellantTables.AdiabaticWallTemp(T_static, M_local, inp.Gas.Gamma, inp.Gas.Prandtl);

            // PH-43 (2026-04-29): areaRatio = A_t / A_local from the
            // isentropic compressible-flow area-Mach relation
            //     A/A* = (1/M) · ((2 + (γ−1)·M²) / (γ+1))^((γ+1)/(2(γ−1)))
            // evaluated at the local (post-throat) Mach number. BartzHeatFlux
            // expects areaRatioToThroat = A_t/A_local ∈ (0, 1] and applies it
            // as `term5 = areaRatio^0.9`.
            //
            // Pre-PH-43 used (R_throatOuter / r_surf)² and clamped to ≥ 1, which
            // (a) treated the throat as a disk (π·R_t²) instead of an annulus
            // (π·(R_o² − R_i²)), and (b) clamped UPWARD so that BartzHeatFlux's
            // own `> 1.0 → 1.0` ceiling neutralised term5 to 1.0 at every plug
            // station. The area-ratio enhancement was therefore silently absent
            // post-throat. The compressible-flow form gives the physically
            // meaningful term5 ≤ 1 that decreases with Mach (thinning BL on
            // density-falling expansion).
            double M_for_area = System.Math.Max(M_local, 1.0);
            double area_exp = (inp.Gas.Gamma + 1.0) / (2.0 * (inp.Gas.Gamma - 1.0));
            double aOverAt = (1.0 / M_for_area) * System.Math.Pow(
                (2.0 + (inp.Gas.Gamma - 1.0) * M_for_area * M_for_area) / (inp.Gas.Gamma + 1.0),
                area_exp);
            double areaRatio = 1.0 / System.Math.Max(aOverAt, 1.0);  // = A_t / A_local ∈ (0, 1]

            // Channel geometry at this station.
            double A_ch_m2 = (inp.ChannelWidth_mm * inp.ChannelDepth_mm) * 1e-6;
            double P_wet_m = 2.0 * (inp.ChannelWidth_mm + inp.ChannelDepth_mm) * 1e-3;
            double D_h_m = 4.0 * A_ch_m2 / System.Math.Max(P_wet_m, 1e-9);

            var bulk = inp.CoolantFluid.GetState(T_bulk_run, P_bulk_run);
            double v = mDotPerChannel / (bulk.Density_kgm3 * A_ch_m2);
            double Re = CoolantCorrelations.ReynoldsNumber(bulk, v, D_h_m);

            // Sprint 16 / Track J / P5: pre-compute Re/Pr factors once
            // per station; the 12-iter wall-T loop below recomputes them
            // each call otherwise. Same fix as RegenCoolingSolver.cs.
            var nusseltFactors = CoolantCorrelations.ComputeNusseltFactors(bulk, v, D_h_m);

            double T_wg_i = T_aw - 0.5 * (T_aw - T_bulk_run);
            double T_wc_i = T_bulk_run + 10;
            double h_g = 0, h_c = 0, qLocal = 0;

            for (int iter = 0; iter < 12; iter++)
            {
                h_g = BartzHeatFlux.HeatTransferCoefficient(
                    inp.Gas, D_ref_m, rCurv_m, areaRatio, M_local, T_wg_i, 1.0);
                var wallState = inp.CoolantFluid.GetState(
                    System.Math.Max(T_wc_i, T_bulk_run + 1), P_bulk_run);
                h_c = CoolantCorrelations.HeatTransferCoefficient(
                    nusseltFactors, bulk, wallState, CoolantCorrelationKind.SiederTate);

                double k_wall = System.Math.Max(inp.Wall.ConductivityAt(T_wg_i), 1);
                double R_total = 1.0 / System.Math.Max(h_g, 1e-3)
                               + (inp.PlugWallThickness_mm * 1e-3) / k_wall
                               + 1.0 / System.Math.Max(h_c, 1e-3);

                qLocal = (T_aw - T_bulk_run) / System.Math.Max(R_total, 1e-9);
                double T_wg_new = T_aw - qLocal / System.Math.Max(h_g, 1e-3);
                T_wc_i = T_bulk_run + qLocal / System.Math.Max(h_c, 1e-3);
                if (System.Math.Abs(T_wg_new - T_wg_i) < 1.5)
                {
                    T_wg_i = T_wg_new; break;
                }
                T_wg_i = 0.5 * (T_wg_i + T_wg_new);
            }

            T_wg[i] = T_wg_i;
            T_bulk[i] = T_bulk_run;
            q[i] = qLocal;
            if (T_wg_i > peakTwg) { peakTwg = T_wg_i; peakX = s.X_mm; }

            // Advance coolant bulk enthalpy + pressure.
            double ds_mm = inp.Contour.SegmentLengthApprox_mm(i);
            // Sprint 26 (2026-04-23): wetted surface per axial segment
            // depends on the plug topology. Axisymmetric plug wraps a
            // single 2π·r surface; linear plug has TWO flat surfaces
            // (top + bottom of the rectangular extrusion), each of
            // transverse length PlugWidth_mm. The factor of 2 captures
            // the bilateral-symmetric XRS-2200 geometry. Both paths use
            // the same per-channel flow-split — ChannelCount is the
            // total channel count across the whole plug.
            double wetArea_m2 = inp.Contour.IsLinear
                ? 2.0 * (inp.Contour.PlugWidth_mm * 1e-3) * (ds_mm * 1e-3)
                : 2.0 * System.Math.PI * (rSurf_mm * 1e-3) * (ds_mm * 1e-3);
            double Q_segment = qLocal * wetArea_m2 / inp.ChannelCount;
            H_bulk += Q_segment / System.Math.Max(mDotPerChannel, 1e-9);
            T_bulk_run = inp.CoolantFluid.TemperatureFromEnthalpy(H_bulk);
            totalQ += Q_segment * inp.ChannelCount;

            double dPdx = CoolantCorrelations.PressureGradient(bulk, v, D_h_m);
            P_bulk_run -= dPdx * (ds_mm * 1e-3);
            if (P_bulk_run < minPressure) minPressure = P_bulk_run;
            if (P_bulk_run < CavitationPressureFloor_Pa)
            {
                // Emit a per-station warning once per offending
                // station AND count it so AerospikeFeasibility can
                // promote the condition to a gate.
                warnings.Add($"Plug-cooling coolant pressure at station {i} fell below "
                           + $"{CavitationPressureFloor_Pa / 1e6:F1} MPa — cavitation risk.");
                cavitationRiskStations++;
                P_bulk_run = CavitationPressureFloor_Pa;
            }
        }

        // PH-42 (#187): always-on note documenting the Angelino linear-
        // Prandtl-Meyer assumption backing the per-station local-Mach
        // march. Distinct from `warnings` — that array fires only when
        // something went wrong (cavitation, empty contour, etc.); this
        // is a permanent informational record of solver limitations
        // until the CFD-derived M(x) table from T2.3 (#160) supersedes it.
        var notes = new[]
        {
            "PH-42 (#187): local Mach M(x) along the plug surface is "
          + "computed from the contour's per-station FlowAngle_rad, which "
          + "in turn derives from Angelino's linear-ν(x) approximation "
          + "(parametric form ν(x) = ν_exit · x / L_full). Per-station "
          + "accuracy ±25 %; plug-integral wetted-area heat-load impact "
          + "near zero (over- and under-prediction roughly cancel along "
          + "the integration). Replace with a CFD-derived M(x) table "
          + "(per #187 option (a)) once #160 (T2.3 CFD validation) ships.",
        };

        return new AerospikeThermalResult(
            GasSideWallT_K:        T_wg,
            CoolantBulkT_K:        T_bulk,
            HeatFlux_Wm2:          q,
            PeakGasSideWallT_K:    peakTwg,
            PeakStation_X_mm:      peakX,
            CoolantOutletT_K:      T_bulk_run,
            CoolantPressureDrop_Pa: inp.CoolantInletPressure_Pa - P_bulk_run,
            TotalHeatLoad_W:       totalQ,
            Warnings:              warnings.ToArray(),
            CavitationRiskStationCount: cavitationRiskStations,
            MinCoolantPressure_Pa:      minPressure,
            Notes:                      notes);
    }
}
