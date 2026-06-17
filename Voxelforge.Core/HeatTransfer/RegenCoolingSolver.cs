// RegenCoolingSolver.cs — Coupled gas / wall / coolant thermal solver.
//
// Architecture (2026-04-17 upgrade):
//
//   Pass 1 (1D march):
//     For each axial station, iteratively solve T_wg such that radial heat
//     flux is continuous across: gas-film (Bartz) → wall (k/t) → coolant
//     (Dittus-Boelter / Sieder-Tate). Coolant marched in its own direction
//     (counterflow default: enters at nozzle exit).
//
//   Pass 2 (axial wall conduction — 2D correction):
//     After pass 1, compute axial heat conducted through the wall between
//     stations using central differences on T_wg. Subtract that from the
//     local radial flux, re-solve each station locally (no Bartz re-iter).
//     Repeat 3× with relaxation ω = 0.5. Captures ~80 % of full 2D FDM
//     accuracy at ~1 % of the compute cost. Axial Peclet >> 1 in LREs,
//     so the axial diffusive term is a small correction at most stations
//     but significantly smooths throat-region peaks.
//
//   Pass 3 (radial wall profile):
//     For each station, expand the wall into N_r radial nodes and compute
//     T(r) using constant-flux conduction with k(T) integration. Profile
//     is stored for stress analysis (thermal-strain ΔT across the wall).
//
// Film cooling (optional):
//   If FilmCoolingInputs.Enabled, compute η(x) and T_film(x) over the
//   contour BEFORE Pass 1. At each station the recovery temperature
//   driving Bartz becomes T_aw_eff = T_aw − η · (T_aw − T_film).
//   This routinely drops predicted peak wall T by 400–1200 K and is the
//   single largest source of physical accuracy for realistic engines.
//
// Simplifications retained from the 1D version:
//   • Rib fin efficiency is ignored (over-credits coolant-side area ~15–30 %).
//   • Coolant axial acceleration ΔP ignored (small vs friction).
//   • No soot-layer accounting (clean first-fire assumption).
//
// Sprint 33 (2026-04-24): added Dravid Dean-number Nu enhancement (PH-6,
// helical channels) and Haaland friction factor with LPBF-roughness
// (PH-7); both correlations live in <see cref="CoolantCorrelations"/>.

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Coolant;

namespace Voxelforge.HeatTransfer;

public enum CoolantFlowDirection
{
    /// <summary>Coolant enters at nozzle exit, exits at injector. Standard for LRE.</summary>
    Counterflow,
    /// <summary>Coolant enters at injector, exits at nozzle exit. Rarely used.</summary>
    Coflow
}

public sealed record ChannelSchedule(
    int ChannelCount,
    double RibThickness_mm,
    double GasSideWallThickness_mm,
    // Height (radial) at three anchor points — linearly interpolated by axial position.
    double ChannelHeightAtChamber_mm,
    double ChannelHeightAtThroat_mm,
    double ChannelHeightAtExit_mm);

public sealed record RegenSolverInputs(
    ChamberContour Contour,
    PropellantState Gas,
    WallMaterial Wall,
    ChannelSchedule Channels,
    double CoolantMassFlow_kgs,
    double CoolantInletTemp_K,
    double CoolantInletPressure_Pa,
    CoolantFlowDirection Direction = CoolantFlowDirection.Counterflow,
    CoolantCorrelationKind CoolantCorrelation = CoolantCorrelationKind.SiederTate,
    double BartzScalingFactor = 1.0,
    double CoolantHtcScalingFactor = 1.0,
    double CoolantFrictionScalingFactor = 1.0,
    FilmCoolingInputs? FilmCooling = null,
    int AxialConductionSweeps = 3,
    int RadialWallNodes = 5,
    /// <summary>Enable Mayer acceleration + barrel-mixing corrections on Bartz.</summary>
    bool EnableBartzBLCorrections = true,
    /// <summary>Regen-jacket fluid. Null falls back to methane for back-compat.</summary>
    ICoolantFluid? CoolantFluid = null,
    /// <summary>
    /// SPRINT 2: enable rectangular-rib fin-efficiency correction on the
    /// coolant-side HTC. Classical formulation:
    ///   m = √(2·h_c / (k_wall · t_rib))
    ///   η_fin = tanh(m·L) / (m·L)          with L = channel height
    ///   h_c_eff = h_c · (w_ch + 2·η_fin·L) / (w_ch + 2·L)
    /// Typical impact: reduces coolant-side HTC by 15–30 % for tall ribs,
    /// which partially offsets the Bartz throat over-prediction. Default on.
    /// </summary>
    bool EnableFinEfficiency = true,

    /// <summary>
    /// PHASE 1 (2026-04-20): entrance loss coefficient for the coolant-jacket
    /// channel inlet. Standard K ≈ 0.5 for a sharp-edged contraction (Idelchik
    /// §3-1). Multiplies ½·ρ·v² at the first coolant station to yield a
    /// one-time ΔP_entrance added to the total jacket drop.
    /// </summary>
    double CoolantEntranceLossK = 0.5,

    /// <summary>
    /// PHASE 1 (2026-04-20): exit loss coefficient from the jacket channel
    /// into the outlet manifold. K ≈ 1.0 for a sudden expansion to infinity
    /// (full velocity-head loss, Borda–Carnot limit).
    /// </summary>
    double CoolantExitLossK = 1.0,

    /// <summary>
    /// PHASE 4 (2026-04-20): cooling channel pitch angle (deg) from the
    /// chamber axis. 0 = pure axial (legacy). Positive angles lengthen the
    /// wetted path by 1/cos(α), applied as a segment-length multiplier on
    /// the heat-uptake and pressure-drop integrals; friction gets an
    /// additional (1 + 0.15·tan²(α)) multiplier for secondary-flow losses.
    /// </summary>
    double HelixPitchAngle_deg = 0.0,

    /// <summary>
    /// ChannelTopology.None short-circuit. When true, the full
    /// 1D/2D/3D march is skipped; the solver stamps a gas-side
    /// heat-flux profile using Bartz at T_wg = material service limit
    /// + 1 K so the ablative recession analysis still has per-station
    /// q to integrate against, and returns CoolantPressureDrop_Pa = 0,
    /// CoolantOutletT_K = CoolantInletT_K, and PeakGasSideWallT_K =
    /// material.MaxServiceTemp_K + 1 so the WALL_TEMP feasibility gate
    /// fires — forcing an ablative-only chamber to go through the
    /// ablative-recession feasibility path to be feasible.
    /// </summary>
    bool SkipRegenMarch = false,

    /// <summary>
    /// TPMS cooling topology. When non-null the per-station h_c uses
    /// <see cref="TpmsCorrelations.HeatTransferCoefficient"/> and the
    /// per-station friction factor uses
    /// <see cref="TpmsCorrelations.FrictionFactor"/> with the surface-
    /// area-density-derived hydraulic diameter instead of the classical
    /// rectangular channel formulation. Null preserves the
    /// axial/helical rectangular-channel path bit-identically.
    /// </summary>
    TpmsKind? TpmsKind = null,

    /// <summary>
    /// TPMS unit-cell edge length (metres). Only consumed when
    /// <see cref="TpmsKind"/> is non-null.
    /// </summary>
    double TpmsCellEdge_m = 3e-3,

    /// <summary>
    /// TPMS solid-volume fraction. Only consumed when
    /// <see cref="TpmsKind"/> is non-null.
    /// </summary>
    double TpmsSolidFraction = 0.50,

    /// <summary>
    /// Sprint 33 / PH-7 (2026-04-24): coolant-channel relative roughness
    /// ε/D_h. Drives the Haaland friction factor at every station and
    /// raises coolant ΔP by 2-4× for typical LPBF-printed jackets
    /// (ε/D ≈ 0.01-0.05; Strauss et al. 2018). Default 0.0 preserves
    /// smooth-tube Petukhov for synthetic-fixture call sites that
    /// construct <see cref="RegenSolverInputs"/> directly. Real-design
    /// runs route through <see cref="Optimization.RegenChamberDesign.LpbfRelativeRoughness"/>
    /// (default 0.02) so end-to-end SA scoring includes the roughness
    /// penalty.
    /// </summary>
    double LpbfRelativeRoughness = 0.0,

    /// <summary>
    /// Z1 hot-fix / Track B closed-loop (2026-04-28): per-station gas-side
    /// wall thickness profile, in mm, indexed by station. When non-null
    /// AND <c>Length == Contour.Stations.Length</c> the solver substitutes
    /// the per-station value for <see cref="ChannelSchedule.GasSideWallThickness_mm"/>
    /// inside the per-station Picard loop AND in the axial-conduction
    /// post-pass — so changes to <c>ThroatWallThicknessOverride_mm</c> /
    /// <c>ChamberWallThicknessOverride_mm</c> / <c>ExitWallThicknessOverride_mm</c>
    /// actually move T_wg at the corresponding station rather than being
    /// silently ignored at the thermal solve and applied only later in
    /// <see cref="Structure.StructuralCheck"/>.
    /// Null (default) preserves uniform behaviour bit-identically. Length
    /// mismatches are silently treated as null (defensive, no exception).
    /// </summary>
    IReadOnlyList<double>? GasSideWallProfile_mm = null,

    /// <summary>
    /// OOB-12 (2026-05-04): enable Eckert-Livingood transpiration cooling.
    /// Bleeds <see cref="TranspirationBleedFraction"/> of <see cref="CoolantMassFlow_kgs"/>
    /// through the porous LPBF wall per station, reducing T_aw_eff via the
    /// blowing effectiveness F(B) = B / (exp(B) − 1). Default false preserves
    /// pre-OOB-12 behaviour bit-identically.
    /// </summary>
    bool EnableTranspirationCooling = false,

    /// <summary>Fraction of total coolant mass flow bled per station [0, 1].</summary>
    double TranspirationBleedFraction = 0.02,

    /// <summary>Transpiration efficiency factor η_t (Sutton §4.3). Default 0.85.</summary>
    double TranspirationEfficiency = 0.85);

public sealed record StationResult(
    int Index,
    double X_mm,
    double R_mm,
    double AreaRatioToThroat,
    double Mach,
    double StaticTemp_K,
    double AdiabaticWallTemp_K,          // core gas T_aw (no film)
    double EffectiveRecoveryTemp_K,      // T_aw_eff (with film)
    double FilmEffectiveness,            // η at this station
    double HeatFlux_Wm2,
    double h_g_Wm2K,
    double h_c_Wm2K,
    double GasSideWallTemp_K,            // T_wg
    double CoolantSideWallTemp_K,        // T_wc
    double[] WallRadialProfile_K,        // N_r nodes, gas → coolant
    double AxialConductionFlux_Wm2,      // signed; positive = into this station from neighbours
    double CoolantBulkTemp_K,
    double CoolantBulkPressure_Pa,
    double CoolantVelocity_ms,
    double Reynolds,
    double PrandtlBulk,
    double ChannelWidth_mm,
    double ChannelHeight_mm,
    double HydraulicDiameter_mm,
    double PressureGradient_Pam);

/// <summary>
/// Convergence diagnostics. Lets the user tell "result computed"
/// from "result converged cleanly" at a glance.
/// </summary>
public sealed record SolverDiagnostics(
    int MaxWallTempIterationsHit,        // # stations that hit 15-iter cap
    int ChannelWidthClampedCount,        // # stations where w < 0.3 mm forced
    int PressureClampedCount,            // # stations that hit 0.1 MPa floor
    int StationsInPseudocritical,        // # stations with P > P_crit and |T - Tpc| < 25 K
    bool CleanConvergence);              // true ⇒ no clamps, no max-iter hits

public sealed record RegenSolverOutputs(
    StationResult[] Stations,
    double PeakGasSideWallT_K,
    double PeakCoolantSideWallT_K,
    int PeakStationIndex,
    double CoolantInletT_K,
    double CoolantOutletT_K,
    double CoolantInletP_Pa,
    double CoolantOutletP_Pa,
    double CoolantPressureDrop_Pa,
    double TotalHeatLoad_W,
    double TotalWettedArea_mm2,
    double ThroatHeatFlux_Wm2,
    bool WallTempExceedsLimit,
    double WallMarginK,                  // max service T − peak T_wg
    double FilmMassFlow_kgs,             // 0 if disabled
    double IspPenaltyFraction,           // ≥ 0, fractional Isp loss from film bleed
    double AxialConductionRMS_Wm2,       // √mean(q_axial²) — diagnostic of 2D coupling strength
    SolverDiagnostics Diagnostics,
    string[] Warnings,
    // Manifold ΔP topology — break the total CoolantPressureDrop_Pa
    // into its three components so the user can see where the budget
    // goes. CoolantPressureDrop_Pa stays the sum so existing consumers
    // (FeedSystem stackup, scoring) read identical values. Defaults to
    // 0 keeps the synthetic-fixture back-compat path in
    // `Phase3SprintTests.SyntheticThermal` valid.
    double EntranceLoss_Pa = 0.0,        // K_ent · ½·ρ·v² at jacket inlet (sharp contraction)
    double FrictionLoss_Pa = 0.0,        // ∫(dP/dx) along the march (Darcy / friction)
    double ExitLoss_Pa     = 0.0,        // K_exit · ½·ρ·v² at jacket outlet (sudden expansion)
    // Sprint C.2: peak adiabatic wall temperature across all stations.
    // Used by CFD calibration loop for direct T_aw vs T_aw comparison
    // against SU2's adiabatic-wall surface output (MARKER_HEATFLUX=0).
    // Defaults to NaN to keep the synthetic-fixture back-compat path valid.
    double PeakAdiabaticWallTemp_K = double.NaN);

public static class RegenCoolingSolver
{
    // P10 (2026-05-06): floor guard for per-station wall temperatures.
    // At low-flux stations (thick wall, cold coolant, weak Bartz) floating-
    // point arithmetic can yield slightly sub-zero T_wg / T_wc, which
    // propagates into Math.Sqrt / Math.Log inside k(T) and correlation
    // helpers and either throws or returns NaN. 100 K is well below any
    // physical LRE wall temperature (cryogenic coolant inlets are 100-200 K)
    // so it never gates a real design.
    private const double MinPhysicalWallTemp_K = 100.0;

    public static RegenSolverOutputs Solve(RegenSolverInputs inp)
    {
        // Pre-size at 4 — typical solver runs emit 0–3 warnings, so
        // the default capacity-0 → grow-to-4 cycle wasted ~5–10 µs and
        // a couple of GC allocs per candidate.
        var warnings = new List<string>(4);
        int N = inp.Contour.Stations.Length;
        int Nr = Math.Max(inp.RadialWallNodes, 2);
        var results = new StationResult[N];
        var fluid = inp.CoolantFluid ?? MethaneFluid.Instance;
        if (fluid is not MethaneFluid)
            warnings.Add($"Coolant = {fluid.Metadata.DisplayName}. Jacket physics uses the pair-specific fluid module.");

        // ChannelTopology.None short-circuit. No regen channels ⇒ no
        // coolant march. Stamp a gas-side heat-flux profile via Bartz
        // at T_wg = material service limit + 1 K (so the WALL_TEMP
        // gate fires) and return zero-coolant sentinel outputs.
        // AblativeAnalysis.Run reads HeatFlux_Wm2 per station, so the
        // profile must be physical for the recession integral to work.
        if (inp.SkipRegenMarch)
            return SolveAblativeOnly(inp, Nr, warnings);

        // ── Input validation (fail-fast against NaN/Inf propagation) ──
        // ChannelCount == 0 → mDotPerChannel = Inf → NaN wall temps that
        // the feasibility gates cannot distinguish from physical
        // infeasibility (garbage scores "feasible"). The attribute
        // bounds [50, 300] make this unreachable from SA Pack/Unpack,
        // but a corrupted JSON load or a direct solver caller can still
        // hit it. Fail fast with a clear message instead of silently
        // producing garbage thermal numbers the user might print.
        if (inp.Channels.ChannelCount <= 0)
            throw new ArgumentException(
                $"ChannelCount must be >= 1 for regen march (got {inp.Channels.ChannelCount}).",
                nameof(inp));

        // ── 0. Film cooling pre-pass ─────────────────────────────
        FilmCoolingProfile? film = null;
        double filmMassFlow = 0;
        double ispPenalty = 0;
        if (inp.FilmCooling is { Enabled: true } fci && fci.FuelFractionAsFilm > 0)
        {
            // Approximate chamber gas state for Stechman momentum ratio.
            double T_cham = inp.Gas.ChamberTemp_K;
            double rho_g = inp.Gas.ChamberPressure_Pa /
                           (inp.Gas.SpecificGasConst * T_cham);

            // Sprint feasibility-audit-integrity-bundle-1 (2026-04-27, ID-2):
            // chamber gas velocity from M ≈ 0.1 × local sound speed instead
            // of the prior `u_g = 50.0` rough constant. The constant was
            // wrong by 2-5× depending on propellant (LOX/CH4 c ≈ 1295 m/s,
            // LOX/H2 c ≈ 1580 m/s, LOX/RP1 c ≈ 1300 m/s — all give u_g ≈
            // 130-160 m/s at typical M=0.1, vs the constant 50). The
            // chamber Mach itself is a soft assumption (0.1-0.3 range per
            // Sutton 9e §3.3, depending on contraction ratio); 0.1 is the
            // low end appropriate for typical LRE main chambers with
            // contraction ratio 6-10.
            double a_chamber = System.Math.Sqrt(
                inp.Gas.GammaChamber * inp.Gas.SpecificGasConst * T_cham);
            const double M_chamber = 0.1;
            double u_g = M_chamber * a_chamber;

            // Sprint feasibility-audit-integrity-bundle-1 (2026-04-27, ID-1):
            // pass real fuel density at injection conditions instead of the
            // FilmCooling.Compute default 10 kg/m³. The default was wrong
            // by 7-81× across implemented propellant pairs (LCH4 ≈ 430,
            // LH2 ≈ 70, RP-1 ≈ 810 at typical injection T/P). The bug
            // partially cancelled with the Stechman β = 0.03 calibration
            // shipped in PR #88 / Sprint E (YF-1 in physics-integrity-
            // notes.md); fixing one without the other shifts η out of the
            // target band. This fix pairs with a joint β re-calibration
            // (also bundle-1) so that η stays in [0.3, 0.5] across all 5
            // canonical presets.
            var fuelInjectionState = fluid.GetState(
                inp.CoolantInletTemp_K, inp.CoolantInletPressure_Pa);
            double rho_film = System.Math.Max(fuelInjectionState.Density_kgm3, 1.0);

            // Z3-F1 (2026-04-29): per-station gas mass flux from mass
            // conservation `G(x) · A(x) = ṁ_total = const`. The chamber-
            // side scalar G is accurate at the injector face but under-
            // predicts at the throat by ~the contraction ratio. Threading
            // the per-station array into FilmCooling.Compute lets the
            // Stechman momentum-ratio factor reflect axial G_g variation.
            double G_chamber = rho_g * u_g;
            double A_chamber_mm2 = inp.Contour.Stations[0].Area_mm2;
            var G_g_per_station = new double[N];
            for (int i = 0; i < N; i++)
            {
                double A_i_mm2 = System.Math.Max(inp.Contour.Stations[i].Area_mm2, 1e-9);
                G_g_per_station[i] = G_chamber * (A_chamber_mm2 / A_i_mm2);
            }

            film = FilmCooling.Compute(
                inp.Contour, fci,
                totalFuelMassFlow_kgs: inp.CoolantMassFlow_kgs,   // fuel ≈ coolant
                gasStaticTempAtChamber_K: T_cham,
                gasDensityAtChamber_kgm3: rho_g,
                gasVelocityAtChamber_ms: u_g,
                filmDensity_kgm3: rho_film,
                gasMassFluxPerStation_kg_m2_s: G_g_per_station);
            filmMassFlow = film.TotalFilmMassFlow_kgs;
            ispPenalty = HeatTransfer.FilmCooling.IspPenaltyFraction(
                fci.FuelFractionAsFilm, inp.Gas.MixtureRatio);
            foreach (var w in film.Warnings) warnings.Add(w);
        }

        // ── Pre-pass 0.5: gas-state along contour (needed for BL-acceleration K) ──
        // K = ν · (dU/dx) / U²  requires U at neighbor stations; compute the
        // whole U[i] array up front so the thermal march can read any index.
        var M_pre = new double[N];
        var Tstat_pre = new double[N];
        var U_pre = new double[N];
        for (int i = 0; i < N; i++)
        {
            var s = inp.Contour.Stations[i];
            // Guard against contour-generator bugs producing zero/negative
            // area. !(x > 0) rejects NaN too. Bartz/D-B both propagate
            // NaN silently once it enters the march; fail fast here.
            if (!(s.Area_mm2 > 0))
                throw new ArgumentException(
                    $"Station {i} has non-positive Area_mm2={s.Area_mm2}; contour generator bug.",
                    nameof(inp));
            double areaRatio_i = inp.Contour.ThroatArea_mm2 / s.Area_mm2;
            bool ss = s.X_mm > inp.Contour.Stations[inp.Contour.ThroatIndex].X_mm;
            M_pre[i] = PropellantTables.MachFromAreaRatio(areaRatio_i, inp.Gas.Gamma, ss);
            Tstat_pre[i] = PropellantTables.StaticTemp(inp.Gas.ChamberTemp_K, M_pre[i], inp.Gas.Gamma);
            U_pre[i] = M_pre[i] * Math.Sqrt(inp.Gas.Gamma * inp.Gas.SpecificGasConst * Math.Max(Tstat_pre[i], 1));
        }
        double D_chamber_m = 2.0 * inp.Contour.ChamberRadius_mm * 1e-3;
        double L_mix_m = 2.0 * D_chamber_m;   // mixing length = 2·D_c

        // ── 1. Pass-1: 1D station march with Bartz / wall / D-B balance ──
        int first, last, step;
        if (inp.Direction == CoolantFlowDirection.Counterflow)
        { first = N - 1; last = -1; step = -1; }
        else
        { first = 0; last = N; step = +1; }

        double mDotPerChannel = inp.CoolantMassFlow_kgs / inp.Channels.ChannelCount;
        double T_bulk = inp.CoolantInletTemp_K;
        double P_bulk = inp.CoolantInletPressure_Pa;

        // Per-solve quantised cache of fluid.GetState(T, P). Each call
        // to GetState does an interpolation against the fluid table
        // (~50–100 µs); the inner wall-T iteration re-queries the
        // same (T, P) many times per station and the bulk march
        // re-queries similar (T, P) bins across stations. Quantisation
        // at 0.25 K / 100 Pa keeps cache size bounded while preserving
        // numerical determinism within the solver's own ±0.5 K
        // convergence tolerance. Local — no thread share.
        //
        // Two-layer cache (P22, 2026-04-24): a 16-slot direct-mapped
        // hint cache fronts the unbounded Dictionary. Hits go through
        // a single masked array index + key compare (~5 ns) instead of
        // Dictionary's hash-and-bucket-probe (~30-50 ns). Misses fall
        // through to the Dictionary which retains every (T, P) bin
        // ever seen this solve — so we keep the full hit rate of the
        // pre-P22 cache while raising the per-hit speed of the most
        // recent ~16 keys (which dominate by temporal locality: the
        // wall-T loop alternates between bulk and wall-state queries
        // within ~3 axial stations of each other). The hint cache
        // uses key.GetHashCode() folded to 4 bits — `(T, P)` already
        // fits in the long key so we slice the low 4 bits of the
        // T-bucket nybble for spread.
        const int HintSize = 16;
        var hintKeys = new long[HintSize];
        var hintValues = new CoolantState[HintSize];
        Array.Fill(hintKeys, long.MinValue);
        var stateCache = new Dictionary<long, CoolantState>(capacity: 512);
        CoolantState GetCached(double T, double P)
        {
            long key = ((long)(T * 4) << 32) ^ unchecked((uint)(int)(P / 100.0));
            int slot = (int)((ulong)key & (HintSize - 1));
            if (hintKeys[slot] == key) return hintValues[slot];
            if (stateCache.TryGetValue(key, out var s))
            {
                hintKeys[slot] = key; hintValues[slot] = s;
                return s;
            }
            s = fluid.GetState(T, P);
            stateCache[key] = s;
            hintKeys[slot] = key; hintValues[slot] = s;
            return s;
        }

        double H_bulk = GetCached(T_bulk, P_bulk).Enthalpy_Jkg;

        double D_t_m = 2.0 * inp.Contour.ThroatRadius_mm * 1e-3;
        // Sprint 32 (PH-5, 2026-04-24): 0.382·R_t is Bartz's Rao-TOP
        // downstream-throat longitudinal radius of curvature (Bartz
        // 1957, eq. 17a). Pre-Sprint-32 used 1.5·R_t which corresponds
        // to a much rounder upstream-throat curvature; the (r_c/R)^0.1
        // factor in the Bartz σ correction was about 14 % too low at
        // the throat as a result. Switching to the downstream curvature
        // recovers ~14 % h_g locally.
        double r_curv_m = 0.382 * inp.Contour.ThroatRadius_mm * 1e-3;
        double x_throat = inp.Contour.Stations[inp.Contour.ThroatIndex].X_mm;
        double x_exit_mm = inp.Contour.TotalLength_mm;

        // Working arrays for 2-pass correction.
        var T_wg = new double[N];
        var T_wc = new double[N];
        var qRadial = new double[N];
        var h_gArr = new double[N];
        var h_cArr = new double[N];
        var T_awArr = new double[N];
        var T_awEffArr = new double[N];
        var wChArr = new double[N];
        var hChArr = new double[N];
        var D_hArr = new double[N];
        var vArr = new double[N];
        var ReArr = new double[N];
        var PrArr = new double[N];
        var dPdxArr = new double[N];
        var T_bulkArr = new double[N];
        var P_bulkArr = new double[N];
        var dsArr = new double[N];
        var dsMarchArr = new double[N];
        var MArr = new double[N];
        var TstatArr = new double[N];

        // Convergence diagnostics.
        int diagMaxIterHits = 0;
        int diagChannelWidthClamped = 0;
        int diagPressureClamped = 0;

        // PHASE 1: capture entrance / exit density and velocity for the one-time
        // entrance (K≈0.5) and exit (K≈1.0) ΔP corrections. Stamped on the first
        // and last iteration of the march loop so they track actual flow state
        // rather than an inlet guess.
        double entranceRho = 0, entranceV = 0;
        double exitRho = 0, exitV = 0;

        // PHASE 4: helical coolant-channel multipliers. Segment-length multiplier
        // stretches each ds to the true along-the-spiral path length; friction
        // multiplier adds a small secondary-flow correction (common correlation).
        double alphaRad = inp.HelixPitchAngle_deg * Math.PI / 180.0;
        double helixLengthFactor = 1.0 / Math.Cos(alphaRad);           // ≥ 1
        double tanHelix = Math.Tan(alphaRad);
        double helixFrictionFactor = 1.0 + 0.15 * tanHelix * tanHelix; // ≥ 1

        // Sprint 33 / PH-6 (2026-04-24): pre-compute sin²α once for the
        // helix-curvature radius derivation R_curv = r_outer_wall / sin²α.
        // sin²α = 0 (axial) maps to R_curv → ∞ → DeanNumberNuMultiplier
        // returns 1, so no enhancement is applied for axial topology.
        double sinAlpha = Math.Sin(alphaRad);
        double sinAlphaSq = sinAlpha * sinAlpha;

        // Z1 hot-fix / Track B closed-loop (2026-04-28): per-station wall
        // thickness. Caller may pass a per-station profile via
        // RegenSolverInputs.GasSideWallProfile_mm; null OR length mismatch
        // falls back to the uniform ChannelSchedule.GasSideWallThickness_mm
        // (defensive — never throws on length mismatch). Captured once
        // before the march so the inner loops can read t_wall_i_mm at zero
        // overhead.
        bool useWallProfile = inp.GasSideWallProfile_mm is not null
                           && inp.GasSideWallProfile_mm.Count == N;
        double uniformWall_mm = inp.Channels.GasSideWallThickness_mm;
        double GasSideWallAt(int idx)
            => useWallProfile ? inp.GasSideWallProfile_mm![idx] : uniformWall_mm;

        for (int i = first; i != last; i += step)
        {
            var s = inp.Contour.Stations[i];
            double t_wall_i_mm = GasSideWallAt(i);
            double areaRatio = inp.Contour.ThroatArea_mm2 / s.Area_mm2;
            bool supersonic = s.X_mm > x_throat;
            double M = PropellantTables.MachFromAreaRatio(areaRatio, inp.Gas.Gamma, supersonic);
            double T_static = PropellantTables.StaticTemp(inp.Gas.ChamberTemp_K, M, inp.Gas.Gamma);
            double T_aw = PropellantTables.AdiabaticWallTemp(T_static, M, inp.Gas.Gamma, inp.Gas.Prandtl);

            double T_film = film?.FilmBulkTemp_K[i] ?? T_aw;
            double eta = film?.Effectiveness[i] ?? 0.0;
            double T_aw_eff = HeatTransfer.FilmCooling.EffectiveRecoveryTemperature(T_aw, T_film, eta);

            // Channel geometry
            double h_ch_mm = InterpChannelHeight(s.X_mm, 0, x_throat, x_exit_mm, inp.Channels);
            double r_outer_wall_mm = s.R_mm + t_wall_i_mm;
            double pitch_mm = 2.0 * Math.PI * r_outer_wall_mm / inp.Channels.ChannelCount;
            double w_ch_mm = pitch_mm - inp.Channels.RibThickness_mm;
            double A_ch_m2, P_wet_m, D_h_m;
            double v;

            // TPMS topologies replace the rectangular-channel
            // cross-section with the porous-medium effective area
            // (ψ × annulus) and hydraulic-diameter formula
            // (4·ψ / σ_SAV). Per-station channel count stays at N for
            // downstream diagnostics, but the effective per-channel
            // area is the annulus share each "channel equivalent" gets.
            if (inp.TpmsKind is { } tpmsKind)
            {
                double porosity = 1.0 - inp.TpmsSolidFraction;
                double sav_m2_per_m3 = TpmsCorrelations.SurfaceAreaDensity(
                    tpmsKind, inp.TpmsCellEdge_m, inp.TpmsSolidFraction);
                // D_h of a porous medium: 4·ψ / σ_SAV (classical).
                D_h_m = sav_m2_per_m3 > 0 ? 4.0 * porosity / sav_m2_per_m3 : 1e-4;
                // Annular flow area × porosity, split across N channel-equivalents.
                double r_inner_tpms_mm = s.R_mm + t_wall_i_mm;
                double r_outer_tpms_mm = r_inner_tpms_mm + h_ch_mm;
                double annulusArea_m2 =
                    Math.PI * ((r_outer_tpms_mm * r_outer_tpms_mm) - (r_inner_tpms_mm * r_inner_tpms_mm))
                    * 1e-6;
                A_ch_m2 = Math.Max(annulusArea_m2 * porosity / inp.Channels.ChannelCount, 1e-10);
                // Wetted perimeter is implicit in D_h; carry it for diagnostics.
                P_wet_m = A_ch_m2 > 0 ? 4.0 * A_ch_m2 / Math.Max(D_h_m, 1e-9) : 0;
                // w_ch_mm / h_ch_mm loses its rectangular meaning under TPMS;
                // stamp the TPMS strut thickness in its place so downstream
                // reports still have a useful "minimum feature" number.
                w_ch_mm = TpmsCorrelations.StrutThickness_mm(inp.TpmsCellEdge_m * 1000.0, inp.TpmsSolidFraction);
            }
            else
            {
                if (w_ch_mm < 0.3)
                {
                    warnings.Add($"Station {i}: channel width {w_ch_mm:F2}mm < 0.3mm min; clamped.");
                    w_ch_mm = 0.3;
                    diagChannelWidthClamped++;
                }
                A_ch_m2 = (w_ch_mm * h_ch_mm) * 1e-6;
                P_wet_m = 2.0 * (w_ch_mm + h_ch_mm) * 1e-3;
                D_h_m = 4.0 * A_ch_m2 / Math.Max(P_wet_m, 1e-9);
            }

            var bulk = GetCached(T_bulk, P_bulk);
            v = mDotPerChannel / (bulk.Density_kgm3 * A_ch_m2);
            double Re = CoolantCorrelations.ReynoldsNumber(bulk, v, D_h_m);

            // Sprint 16 / Track J / P5: pre-compute Re/Pr scaling factors
            // ONCE per station — the wall-T loop below repeats 15× and
            // the previous code recomputed Math.Pow(Re, 0.8) +
            // Math.Pow(Pr, 0.4) inside CoolantCorrelations every time
            // even though both factors are invariant against wallState.
            var nusseltFactors = CoolantCorrelations.ComputeNusseltFactors(bulk, v, D_h_m);

            // Sprint 33 / PH-6 (2026-04-24): per-station Dean-number Nu
            // multiplier for helical channels. R_curv = r_outer_wall / sin²α
            // for a helix on the chamber wall; sin²α = 0 (axial) returns
            // unity from DeanNumberNuMultiplier (no enhancement, free no-op).
            // Multiplier is invariant across the wall-T iteration loop, so
            // we hoist it once here alongside nusseltFactors.
            double R_curv_m = sinAlphaSq > 1e-12 && r_outer_wall_mm > 0
                ? r_outer_wall_mm * 1e-3 / sinAlphaSq
                : double.PositiveInfinity;
            double deanMultiplier =
                CoolantCorrelations.DeanNumberNuMultiplier(D_h_m, R_curv_m);

            // Boundary-layer corrections (off unless caller opts in).
            // Hoisted ABOVE the T_wg seed (was below pre-P23) so the
            // resistance-weighted seed below sees the same Bartz form
            // the iter loop will use — keeps the seed and the converged
            // state self-consistent.
            double K_accel = 0.0;
            double mixingDecay = 1e9;
            if (inp.EnableBartzBLCorrections)
            {
                // Velocity gradient dU/dx via central differences (one-sided at ends).
                int iL = Math.Max(i - 1, 0);
                int iR = Math.Min(i + 1, N - 1);
                double dx_m = Math.Max((inp.Contour.Stations[iR].X_mm
                                      - inp.Contour.Stations[iL].X_mm) * 1e-3, 1e-6);
                double dU_m = U_pre[iR] - U_pre[iL];
                double dUdx = dU_m / dx_m;
                K_accel = BartzHeatFlux.AccelerationParameter(inp.Gas, M, T_static, dUdx);
                // Barrel mixing: decays with distance from injector.
                mixingDecay = s.X_mm * 1e-3 / Math.Max(L_mix_m, 1e-6);
            }

            // P23 (2026-04-24): resistance-weighted initial T_wg seed.
            // Pre-P23 the seed was the midpoint `(T_aw_eff + T_bulk)/2`,
            // i.e. an implicit α = R_gas / R_total = 0.5. For LOX/CH4
            // regen the actual α at convergence sits closer to 0.3-0.45
            // (Cu wall, h_g ≫ h_c at the throat), so the midpoint guess
            // mostly over-estimates T_wg by ~50-150 K and the Picard
            // loop spends 5-10 iterations walking it down. The seed
            // below uses one upfront h_g + h_c eval (at the midpoint
            // wall-T proxy) to compute the actual resistance split,
            // then back-solves the seed T_wg from the steady-state
            // equation T_wg = T_aw_eff − α·(T_aw_eff − T_bulk). Costs
            // ≈ 1 extra Bartz call + 1 extra Nusselt call per station;
            // amortised against 3-7 fewer iterations on the inner loop
            // it nets ~20-50 ms/SA-run faster (CLAUDE.md P23). The
            // iter loop is unchanged — it's fully self-correcting from
            // any reasonable seed via under-relaxation.
            double T_wg_mid = Math.Max(0.5 * (T_aw_eff + T_bulk), 500);
            double h_g_seed = BartzHeatFlux.HeatTransferCoefficient(
                inp.Gas, D_t_m, r_curv_m, areaRatio, M, T_wg_mid, inp.BartzScalingFactor,
                accelerationParameterK: K_accel,
                injectorMixingDecay: mixingDecay);
            // OOB-12 (2026-05-04): apply Eckert-Livingood transpiration correction
            // to T_aw_eff. Uses h_g_seed for the blowing parameter B so the modified
            // T_aw_eff is self-consistent with the resistance-weighted seed below.
            // When disabled (or BleedFraction=0), TranspirationCooling short-circuits
            // and returns T_aw_eff unchanged — preserving bit-identical output.
            if (inp.EnableTranspirationCooling && inp.TranspirationBleedFraction > 0.0)
            {
                double ds_i_mm = inp.Contour.SegmentLength_mm(i);
                double stationWallArea_m2 = 2.0 * Math.PI * (s.R_mm * 1e-3) * (ds_i_mm * 1e-3);
                double bleedFlux_kgm2s = inp.TranspirationBleedFraction * inp.CoolantMassFlow_kgs
                    / (N * Math.Max(stationWallArea_m2, 1e-9));
                T_aw_eff = TranspirationCooling.ComputeEffectiveAdiabaticWallTemp(
                    T_aw_eff, inp.CoolantInletTemp_K, h_g_seed,
                    bleedFlux_kgm2s, inp.Gas.Cp_Jkg, inp.TranspirationEfficiency);
            }

            double h_c_seed;
            if (inp.TpmsKind is { } tpmsKindSeed)
            {
                // TPMS h_c is wall-state-independent — exact at seed time.
                h_c_seed = TpmsCorrelations.HeatTransferCoefficient(
                    kind:            tpmsKindSeed,
                    reynolds:        Re,
                    prandtl:         bulk.Prandtl,
                    conductivity_WmK: Math.Max(bulk.Conductivity_WmK, 1e-3),
                    cellEdge_m:      inp.TpmsCellEdge_m,
                    solidFraction:   inp.TpmsSolidFraction);
            }
            else
            {
                // Axial/helical: use bulk state as a wall-state proxy
                // (the wall-T correction inside CoolantCorrelations is a
                // ratio of viscosities at bulk/wall, ≈ 1 at seed time).
                // Fin-efficiency is intentionally skipped from the seed
                // — it scales h_c by a constant 0.7-0.85 factor that the
                // iter loop applies; including it would only shift the
                // resistance split by ≤ 5 % which the under-relaxation
                // recovers on the first iteration.
                // A3 / Pizzarelli auto-select (2026-04-28): bump to
                // SupercriticalPizzarelli when the bulk state is in the
                // fluid's pseudocritical band; outside the band the user's
                // default (Sieder-Tate / Dittus-Boelter) is used unchanged.
                // Per-station decision so a single solve can use different
                // correlations at different stations (typical: Sieder-Tate
                // far from T_pc, Pizzarelli through the transition).
                var seedCorrelation = CoolantCorrelations.AutoSelectKind(
                    bulk, fluid, inp.CoolantCorrelation);
                h_c_seed = CoolantCorrelations.HeatTransferCoefficient(
                    nusseltFactors, bulk, bulk, seedCorrelation)
                    * deanMultiplier;
            }
            h_c_seed *= inp.CoolantHtcScalingFactor;
            // PH-45 (2026-04-29): exact cylindrical conduction
            //     R_w_per_area_inner = r_inner · ln(r_outer / r_inner) / k
            // (heat flux referenced to the inner wall area, matching the
            // h_g balance). Pre-PH-45 used the thin-wall approximation
            // `t / k` which drops the ln(r_o/r_i) curvature term —
            // ~1-3 % off on typical chamber walls (t/r ≈ 0.05) but
            // climbs to 5-10 % on thick high-Pc designs (t/r ≈ 0.15).
            // Reduces to thin-wall for r → ∞ (Taylor: r·ln(1+t/r) → t).
            // OOB-12: transpiration cooling (Eckert-Livingood model). Applied after
            // h_g_seed so B = bleedFlux·cp/h_gas is dimensionless [−]. h_g_seed is
            // from the gas-side seed pass (not the Picard loop yet), so the blowing
            // parameter is approximate but self-consistent with the seed T_wg. The
            // modified T_aw_eff is used throughout the remaining Picard iteration,
            // so convergence is to the correct transpiration-reduced wall temperature.
            // When disabled: single branch-not-taken — bit-identical output.
            if (inp.EnableTranspirationCooling && inp.TranspirationBleedFraction > 0.0)
            {
                double ds_i_mm = inp.Contour.SegmentLength_mm(i);
                double stationWallArea_m2 = 2.0 * Math.PI * (s.R_mm * 1e-3) * (ds_i_mm * 1e-3);
                double bleedFlux_kgm2s = inp.TranspirationBleedFraction * inp.CoolantMassFlow_kgs
                    / (N * Math.Max(stationWallArea_m2, 1e-9));
                T_aw_eff = TranspirationCooling.ComputeEffectiveAdiabaticWallTemp(
                    T_aw_eff, inp.CoolantInletTemp_K, h_g_seed,
                    bleedFlux_kgm2s, inp.Gas.Cp_Jkg, inp.TranspirationEfficiency);
            }

            double R_g_seed = 1.0 / Math.Max(h_g_seed, 1e-3);
            // Z3-m1 (2026-04-29): pass the wall material + per-layer T to
            // the helper; bimetallic walls get per-layer-T composition,
            // pure walls keep the legacy single-T `ConductivityAt(T_wg)`.
            // T_wg_mid + (T_bulk + 5) is a proxy for (T_wg_seed, T_wc_seed)
            // since the seed iteration hasn't run yet.
            double R_w_seed = WallResistanceLogMean_KperWperM2(
                s.R_mm, t_wall_i_mm, inp.Wall, T_wg_mid, T_bulk + 5);
            double R_c_seed = 1.0 / Math.Max(h_c_seed, 1e-3);
            double R_total_seed = R_g_seed + R_w_seed + R_c_seed;
            double alphaGas = R_g_seed / Math.Max(R_total_seed, 1e-9);
            double T_wg_i = Math.Max(T_aw_eff - alphaGas * (T_aw_eff - T_bulk), 500);
            double h_g = 0, h_c = 0, q = 0, T_wc_i = T_bulk + 1;
            bool convergedThisStation = false;

            for (int iter = 0; iter < 15; iter++)
            {
                h_g = BartzHeatFlux.HeatTransferCoefficient(
                    inp.Gas, D_t_m, r_curv_m, areaRatio, M, T_wg_i, inp.BartzScalingFactor,
                    accelerationParameterK: K_accel,
                    injectorMixingDecay: mixingDecay);
                var wallState = GetCached(Math.Max(T_wc_i, T_bulk + 1), P_bulk);

                if (inp.TpmsKind is { } tpmsKindInner)
                {
                    // TPMS h_c via Attarzadeh 2020
                    // Nu correlation. Conductivity evaluated at the bulk
                    // state (matches the axial/helical CoolantCorrelations
                    // reference convention). Fin-efficiency does not apply
                    // to TPMS — the porous medium IS the fin structure
                    // and the Nu correlation already bakes the surface-
                    // area enhancement in.
                    h_c = TpmsCorrelations.HeatTransferCoefficient(
                        kind:            tpmsKindInner,
                        reynolds:        Re,
                        prandtl:         bulk.Prandtl,
                        conductivity_WmK: Math.Max(bulk.Conductivity_WmK, 1e-3),
                        cellEdge_m:      inp.TpmsCellEdge_m,
                        solidFraction:   inp.TpmsSolidFraction);
                }
                else
                {
                    // Use the fast-path overload that consumes the
                    // pre-computed nusseltFactors. Output is
                    // bit-identical to the old
                    // `(bulk, wallState, v, D_h, kind)` overload — the
                    // only saved work is the Re/Pr Math.Pow calls that
                    // don't depend on wallState.
                    // A3 / Pizzarelli auto-select (2026-04-28): same
                    // auto-pick used at the seed (line ~648). Decision is
                    // stable per station because bulk state doesn't
                    // change inside the wall-T iter loop — the bulk only
                    // updates at the end of the station's converged iter
                    // via the enthalpy march.
                    var iterCorrelation = CoolantCorrelations.AutoSelectKind(
                        bulk, fluid, inp.CoolantCorrelation);
                    h_c = CoolantCorrelations.HeatTransferCoefficient(
                        nusseltFactors, bulk, wallState, iterCorrelation);

                    // Sprint 33 / PH-6 (2026-04-24): Dean-number Nu
                    // enhancement for helical channels. Applied BEFORE
                    // fin-efficiency so the rib's m_fin sees the actual
                    // h_c on its surface (not the smooth-tube value).
                    // deanMultiplier = 1 for axial topology (no-op).
                    h_c *= deanMultiplier;

                    // SPRINT 2: fin-efficiency correction. Channel walls act as fins
                    // rising off the gas-side wall; only a fraction of the rib area
                    // couples effectively to the coolant. The correction consistently
                    // reduces h_c by 15–30 % for tall ribs (L >> w).
                    if (inp.EnableFinEfficiency)
                    {
                        double t_rib_m = Math.Max(inp.Channels.RibThickness_mm * 1e-3, 1e-4);
                        double L_fin_m = Math.Max(h_ch_mm * 1e-3, 1e-4);
                        double w_ch_m  = Math.Max(w_ch_mm * 1e-3, 1e-4);
                        // Use T at the rib mid-plane for k_wall — simple average of
                        // gas-side and coolant-side wall temperatures.
                        double k_rib = Math.Max(inp.Wall.ConductivityAt(0.5 * (T_wg_i + T_wc_i)), 1);
                        double m_fin = Math.Sqrt(2.0 * Math.Max(h_c, 1e-3) / (k_rib * t_rib_m));
                        double mL    = m_fin * L_fin_m;
                        double etaFin = mL > 1e-6 ? Math.Tanh(mL) / mL : 1.0;
                        double ratio = (w_ch_m + 2.0 * etaFin * L_fin_m)
                                     / (w_ch_m + 2.0 * L_fin_m);
                        h_c *= ratio;
                    }
                }
                h_c *= inp.CoolantHtcScalingFactor;

                // PH-45 (2026-04-29): log-mean cylindrical conduction —
                // see seed-block comment above.
                // Z3-m1 (2026-04-29): bimetallic walls use per-layer T
                // (liner at T_wg_i, jacket at T_wc_i) inside the helper.
                double R_total =
                    1.0 / Math.Max(h_g, 1e-3)
                  + WallResistanceLogMean_KperWperM2(
                        s.R_mm, t_wall_i_mm, inp.Wall, T_wg_i, T_wc_i)
                  + 1.0 / Math.Max(h_c, 1e-3);

                q = (T_aw_eff - T_bulk) / Math.Max(R_total, 1e-9);
                double T_wg_new = T_aw_eff - q / Math.Max(h_g, 1e-3);
                T_wc_i = T_bulk + q / Math.Max(h_c, 1e-3);
                if (Math.Abs(T_wg_new - T_wg_i) < 1.5)
                { T_wg_i = T_wg_new; convergedThisStation = true; break; }
                T_wg_i = 0.5 * (T_wg_i + T_wg_new);
            }
            if (!convergedThisStation) diagMaxIterHits++;

            T_wg[i] = Math.Max(T_wg_i, MinPhysicalWallTemp_K);
            T_wc[i] = Math.Max(T_wc_i, MinPhysicalWallTemp_K);
            qRadial[i] = q;
            h_gArr[i] = h_g; h_cArr[i] = h_c;
            T_awArr[i] = T_aw; T_awEffArr[i] = T_aw_eff;
            wChArr[i] = w_ch_mm; hChArr[i] = h_ch_mm; D_hArr[i] = D_h_m;
            vArr[i] = v; ReArr[i] = Re; PrArr[i] = bulk.Prandtl;
            MArr[i] = M; TstatArr[i] = T_static;

            double ds_mm = inp.Contour.SegmentLength_mm(i);
            dsArr[i] = ds_mm;
            dsMarchArr[i] = ds_mm;
            double A_gas_station_m2 = 2.0 * Math.PI * (s.R_mm * 1e-3) * (ds_mm * 1e-3);
            double Q_ch = q * A_gas_station_m2 / inp.Channels.ChannelCount;

            H_bulk += Q_ch / mDotPerChannel;
            T_bulk = fluid.TemperatureFromEnthalpy(H_bulk);
            double dPdx;
            if (inp.TpmsKind is { } tpmsKindDp)
            {
                // Darcy ΔP/dx = f·(ρ·v²) / (2·D_h) using the TPMS
                // friction factor. Ignores the helical multiplier —
                // TPMS occupies the whole annular band so there is no
                // axial-vs-spiral path distinction.
                double fTpms = TpmsCorrelations.FrictionFactor(
                    tpmsKindDp, Re, inp.TpmsSolidFraction);
                dPdx = fTpms * bulk.Density_kgm3 * v * v
                     / (2.0 * Math.Max(D_h_m, 1e-9));
            }
            else
            {
                // Sprint 33 / PH-7 (2026-04-24): use the Haaland friction-
                // factor overload to thread LPBF-channel relative roughness
                // (typically 0.01-0.05) into the pressure-drop integrand.
                // ε/D=0 falls back to smooth-tube Petukhov bit-identically.
                dPdx = CoolantCorrelations.PressureGradient(
                            bulk, v, D_h_m, inp.LpbfRelativeRoughness)
                     * helixFrictionFactor;
            }
            dPdx *= inp.CoolantFrictionScalingFactor;
            // PHASE 4: coolant follows a longer spiral path than ds_axial; the
            // pressure-drop integrand runs over the spiral length, not the
            // axial length. (Gas-side area and heat uptake stay on the axial
            // coordinate because the gas doesn't know about the coolant spiral.)
            P_bulk -= dPdx * (ds_mm * 1e-3 * helixLengthFactor);
            // **Sprint feasibility-audit-LH2 (2026-04-26 evening):** floor
            // P_bulk at chamber pressure × 1.1 (was 0.1 MPa). The lower
            // floor was producing unphysical coolant densities (linear-P
            // correction × P_floor / P_ref = 0.05 kg/m³ for LH2) which
            // drove the velocity calc to 130+ km/s on RL10. Physics:
            // coolant pressure MUST exceed chamber pressure for flow to
            // reach the injector (else backflow at the dome) — anything
            // below that breaks the design's basic assumption. Designs
            // that fail this floor surface a warning + the diagPressureClamped
            // counter so the user sees the friction model is over-
            // predicting ΔP. Below this floor, the design literally
            // wouldn't fire (coolant can't reach the injector).
            double minBulkP_Pa = inp.Gas.ChamberPressure_Pa * 1.1;
            if (P_bulk < minBulkP_Pa)
            {
                warnings.Add($"Station {i}: coolant pressure fell below 1.1× chamber pressure "
                           + $"({P_bulk / 1e6:F2} → {minBulkP_Pa / 1e6:F2} MPa floored). Coolant ΔP "
                           + "exceeds tank pressure budget — design wouldn't fire.");
                P_bulk = minBulkP_Pa;
                diagPressureClamped++;
            }

            dPdxArr[i] = dPdx;
            T_bulkArr[i] = T_bulk;
            P_bulkArr[i] = P_bulk;

            // PHASE 1: capture entrance / exit state for loss coefficients.
            if (i == first) { entranceRho = bulk.Density_kgm3; entranceV = v; }
            exitRho = bulk.Density_kgm3;   // last non-break iteration stays
            exitV   = v;
        }

        // ── 2. Pass-2: axial wall conduction correction ──────────
        // Applies a provably stable Laplacian low-pass filter to T_wg and
        // T_wc along the axial direction. For diffusion coefficient α ≤ 0.5
        // the operator (I + α·Δ) has all eigenvalues in [1−4α, 1] so it is
        // unconditionally stable. This captures the physical smoothing of
        // temperature peaks near the throat without the feedback-loop
        // instability of an explicit q-based corrector.
        //
        // After smoothing, we recompute q_radial and the implied axial flux
        // for reporting; these are now consistent with the new wall temps.
        var qAxial = new double[N];
        const double alphaAxial = 0.20;
        if (inp.AxialConductionSweeps > 0)
        {
            // Pre-allocate the two scratch buffers once and swap
            // references each sweep instead of allocating fresh
            // `double[N]` clones every iteration. Numerically
            // bit-identical because `Array.Copy` is a memcpy and the
            // swap preserves the sweep contract (T_wg[i] reads from
            // the previous sweep's state, Twg_new[i] writes the next).
            var Twg_new = new double[N];
            var Twc_new = new double[N];
            for (int sweep = 0; sweep < inp.AxialConductionSweeps; sweep++)
            {
                Array.Copy(T_wg, Twg_new, N);
                Array.Copy(T_wc, Twc_new, N);
                for (int i = 1; i < N - 1; i++)
                {
                    Twg_new[i] = T_wg[i] + alphaAxial * (T_wg[i - 1] + T_wg[i + 1] - 2 * T_wg[i]);
                    Twc_new[i] = T_wc[i] + alphaAxial * (T_wc[i - 1] + T_wc[i + 1] - 2 * T_wc[i]);
                }
                (T_wg, Twg_new) = (Twg_new, T_wg);
                (T_wc, Twc_new) = (Twc_new, T_wc);
            }

            // Compute implied axial flux per station for the diagnostic output.
            // L7 (post-Phase-6 logical-error audit): the smoothing loop
            // intentionally excludes endpoints (Neumann BC) but the
            // diagnostic loop should not — leaving qAxial[0] / qAxial[N-1]
            // at 0 misleads anyone reading the per-station report at the
            // chamber inlet or nozzle exit. Use one-sided FD at endpoints:
            // forward at i=0, backward at i=N-1. Endpoint flux carries
            // only the single neighbour-side gradient, so the convention
            // matches the interior formula's "net heat flux into station i."
            for (int i = 0; i < N; i++)
            {
                var s = inp.Contour.Stations[i];
                // Z1 hot-fix: respect per-station wall profile in the
                // axial-conduction post-pass too (consistent with the
                // per-station march above).
                double A_wall_m2 = 2.0 * Math.PI * (s.R_mm * 1e-3)
                                 * (GasSideWallAt(i) * 1e-3);
                double A_gas_station_m2 = 2.0 * Math.PI * (s.R_mm * 1e-3)
                                        * (dsArr[i] * 1e-3);
                if (A_gas_station_m2 < 1e-9) continue;

                double k_i = inp.Wall.ConductivityAt(T_wg[i]);
                double leftTerm = 0, rightTerm = 0;
                if (i > 0)
                {
                    double dxL = Math.Max((inp.Contour.Stations[i].X_mm
                                         - inp.Contour.Stations[i - 1].X_mm) * 1e-3, 1e-6);
                    leftTerm = (T_wg[i - 1] - T_wg[i]) / dxL;
                }
                if (i < N - 1)
                {
                    double dxR = Math.Max((inp.Contour.Stations[i + 1].X_mm
                                         - inp.Contour.Stations[i].X_mm) * 1e-3, 1e-6);
                    rightTerm = (T_wg[i + 1] - T_wg[i]) / dxR;
                }
                double q_ax_W = k_i * A_wall_m2 * (leftTerm + rightTerm);
                qAxial[i] = q_ax_W / A_gas_station_m2;
            }

            // Update q_radial so the reported flux is consistent with the
            // smoothed wall temperatures (q = h_g · (T_aw_eff − T_wg)).
            for (int i = 0; i < N; i++)
                qRadial[i] = h_gArr[i] * (T_awEffArr[i] - T_wg[i]);
        }

        // ── 3. Pass-3: per-station radial wall profile ───────────
        double totalQ = 0, totalArea_mm2 = 0, throatQ = 0;
        double peakTwg = 0, peakTwc = 0, peakTaw = 0;
        int peakIdx = 0;
        double axialSumSq = 0;
        int axialCount = 0;

        for (int i = 0; i < N; i++)
        {
            var s = inp.Contour.Stations[i];
            var profile = BuildRadialWallProfile(T_wg[i], T_wc[i], inp.Wall, Nr);

            double ds_mm = dsArr[i];
            double A_gas_station_m2 = 2.0 * Math.PI * (s.R_mm * 1e-3) * (ds_mm * 1e-3);
            totalQ += qRadial[i] * A_gas_station_m2;
            totalArea_mm2 += 2.0 * Math.PI * s.R_mm * ds_mm;
            if (i == inp.Contour.ThroatIndex) throatQ = qRadial[i];

            if (T_wg[i] > peakTwg) { peakTwg = T_wg[i]; peakIdx = i; }
            if (T_wc[i] > peakTwc) peakTwc = T_wc[i];
            if (T_awArr[i] > peakTaw) peakTaw = T_awArr[i];

            if (qAxial[i] != 0) { axialSumSq += qAxial[i] * qAxial[i]; axialCount++; }

            results[i] = new StationResult(
                Index: i,
                X_mm: s.X_mm,
                R_mm: s.R_mm,
                AreaRatioToThroat: inp.Contour.ThroatArea_mm2 / s.Area_mm2,
                Mach: MArr[i],
                StaticTemp_K: TstatArr[i],
                AdiabaticWallTemp_K: T_awArr[i],
                EffectiveRecoveryTemp_K: T_awEffArr[i],
                FilmEffectiveness: film?.Effectiveness[i] ?? 0,
                HeatFlux_Wm2: qRadial[i],
                h_g_Wm2K: h_gArr[i],
                h_c_Wm2K: h_cArr[i],
                GasSideWallTemp_K: T_wg[i],
                CoolantSideWallTemp_K: T_wc[i],
                WallRadialProfile_K: profile,
                AxialConductionFlux_Wm2: qAxial[i],
                CoolantBulkTemp_K: T_bulkArr[i],
                CoolantBulkPressure_Pa: P_bulkArr[i],
                CoolantVelocity_ms: vArr[i],
                Reynolds: ReArr[i],
                PrandtlBulk: PrArr[i],
                ChannelWidth_mm: wChArr[i],
                ChannelHeight_mm: hChArr[i],
                HydraulicDiameter_mm: D_hArr[i] * 1000.0,
                PressureGradient_Pam: dPdxArr[i]);
        }

        // PHASE 1: add entrance and exit minor-loss ΔPs. K_ent ≈ 0.5 for a
        // sharp contraction into the channel inlet; K_exit ≈ 1.0 for an exit
        // to a large outlet manifold (full velocity-head loss).
        double dPEntrance_Pa = inp.CoolantEntranceLossK * 0.5 * entranceRho * entranceV * entranceV;
        double dPExit_Pa     = inp.CoolantExitLossK     * 0.5 * exitRho     * exitV     * exitV;
        // Friction = total channel ΔP minus the two manifold heads.
        // The march integrates dP/dx along the channel into (inlet − P_bulk),
        // so this is exactly the Darcy / friction component. Surfacing
        // the breakdown lets the user see whether the manifolds or the
        // channels dominate the budget — typical regen chambers run
        // ≈ 10-20 % entrance + exit, ≈ 70-90 % friction.
        double dPFriction_Pa = inp.CoolantInletPressure_Pa - P_bulk;
        double dP_total = dPFriction_Pa + dPEntrance_Pa + dPExit_Pa;
        double wallMargin = inp.Wall.MaxServiceTemp_K - peakTwg;
        bool exceeds = peakTwg > inp.Wall.MaxServiceTemp_K;
        if (exceeds)
            warnings.Add($"Peak wall T {peakTwg:F0}K exceeds material limit {inp.Wall.MaxServiceTemp_K:F0}K.");

        bool inPC = false;
        foreach (var r in results)
            if (fluid.IsInPseudocriticalRegion(r.CoolantBulkTemp_K, r.CoolantBulkPressure_Pa))
            { inPC = true; break; }
        if (inPC) warnings.Add("Coolant passes through pseudocritical region — correlation accuracy degraded.");

        // Fluid-specific service-limit check (RP-1 coking, pyrolysis, etc.)
        double peakBulk = 0;
        foreach (var r in results) if (r.CoolantBulkTemp_K > peakBulk) peakBulk = r.CoolantBulkTemp_K;
        if (peakBulk > fluid.Metadata.MaxBulkT_K)
            warnings.Add($"{fluid.Metadata.DisplayName} peak bulk T {peakBulk:F0} K > service limit "
                       + $"{fluid.Metadata.MaxBulkT_K:F0} K. {fluid.Metadata.ServiceLimitNote}");

        double axialRms = axialCount > 0 ? Math.Sqrt(axialSumSq / axialCount) : 0;

        int pseudocritCount = 0;
        foreach (var r in results)
            if (fluid.IsInPseudocriticalRegion(r.CoolantBulkTemp_K, r.CoolantBulkPressure_Pa))
                pseudocritCount++;

        var diag = new SolverDiagnostics(
            MaxWallTempIterationsHit: diagMaxIterHits,
            ChannelWidthClampedCount: diagChannelWidthClamped,
            PressureClampedCount: diagPressureClamped,
            StationsInPseudocritical: pseudocritCount,
            CleanConvergence: diagMaxIterHits == 0 && diagChannelWidthClamped == 0 && diagPressureClamped == 0);
        if (diagMaxIterHits > 0)
            warnings.Add($"{diagMaxIterHits}/{N} stations hit the 15-iteration wall-T cap — results suspect at those stations.");

        return new RegenSolverOutputs(
            Stations: results,
            PeakGasSideWallT_K: peakTwg,
            PeakCoolantSideWallT_K: peakTwc,
            PeakStationIndex: peakIdx,
            CoolantInletT_K: inp.CoolantInletTemp_K,
            CoolantOutletT_K: T_bulk,
            CoolantInletP_Pa: inp.CoolantInletPressure_Pa,
            CoolantOutletP_Pa: P_bulk,
            CoolantPressureDrop_Pa: dP_total,
            TotalHeatLoad_W: totalQ,
            TotalWettedArea_mm2: totalArea_mm2,
            ThroatHeatFlux_Wm2: throatQ,
            WallTempExceedsLimit: exceeds,
            WallMarginK: wallMargin,
            FilmMassFlow_kgs: filmMassFlow,
            IspPenaltyFraction: ispPenalty,
            AxialConductionRMS_Wm2: axialRms,
            Diagnostics: diag,
            Warnings: warnings.ToArray(),
            // Manifold ΔP topology breakdown.
            EntranceLoss_Pa: dPEntrance_Pa,
            FrictionLoss_Pa: dPFriction_Pa,
            ExitLoss_Pa:     dPExit_Pa,
            PeakAdiabaticWallTemp_K: peakTaw);
    }

    /// <summary>
    /// Ablative-only short-circuit used when the caller sets
    /// <see cref="RegenSolverInputs.SkipRegenMarch"/> (ChannelTopology.None).
    /// Walks every station computing only the gas-side Bartz heat flux at
    /// a wall temperature pinned 1 K above the material service limit;
    /// zero coolant flow, zero pressure drop, peak wall T stamped above
    /// the material limit so the WALL_TEMP gate fires.
    /// </summary>
    private static RegenSolverOutputs SolveAblativeOnly(
        RegenSolverInputs inp, int Nr, List<string> warnings)
    {
        int N = inp.Contour.Stations.Length;
        var stations = new StationResult[N];
        double T_wg_pinned = inp.Wall.MaxServiceTemp_K + 1.0;

        double D_t_m = 2.0 * inp.Contour.ThroatRadius_mm * 1e-3;
        // Sprint 32 (PH-5, 2026-04-24): 0.382·R_t is Bartz's Rao-TOP
        // downstream-throat longitudinal radius of curvature (Bartz
        // 1957, eq. 17a). Pre-Sprint-32 used 1.5·R_t which corresponds
        // to a much rounder upstream-throat curvature; the (r_c/R)^0.1
        // factor in the Bartz σ correction was about 14 % too low at
        // the throat as a result. Switching to the downstream curvature
        // recovers ~14 % h_g locally.
        double r_curv_m = 0.382 * inp.Contour.ThroatRadius_mm * 1e-3;
        double x_throat = inp.Contour.Stations[inp.Contour.ThroatIndex].X_mm;

        double totalQ = 0, totalArea_mm2 = 0, throatQ = 0;
        double peakQ = 0, peakTaw = 0;
        int peakIdx = 0;

        for (int i = 0; i < N; i++)
        {
            var s = inp.Contour.Stations[i];
            double areaRatio = inp.Contour.ThroatArea_mm2 / s.Area_mm2;
            bool supersonic = s.X_mm > x_throat;
            double M = PropellantTables.MachFromAreaRatio(areaRatio, inp.Gas.Gamma, supersonic);
            double T_static = PropellantTables.StaticTemp(inp.Gas.ChamberTemp_K, M, inp.Gas.Gamma);
            double T_aw = PropellantTables.AdiabaticWallTemp(T_static, M, inp.Gas.Gamma, inp.Gas.Prandtl);
            if (T_aw > peakTaw) peakTaw = T_aw;

            double h_g = BartzHeatFlux.HeatTransferCoefficient(
                inp.Gas, D_t_m, r_curv_m, areaRatio, M, T_wg_pinned, inp.BartzScalingFactor);
            double q = BartzHeatFlux.HeatFlux(h_g, T_aw, T_wg_pinned);

            double ds_mm = inp.Contour.SegmentLength_mm(i);
            double A_gas_station_m2 = 2.0 * Math.PI * (s.R_mm * 1e-3) * (ds_mm * 1e-3);
            totalQ += q * A_gas_station_m2;
            totalArea_mm2 += 2.0 * Math.PI * s.R_mm * ds_mm;
            if (i == inp.Contour.ThroatIndex) throatQ = q;
            if (q > peakQ) { peakQ = q; peakIdx = i; }

            var profile = new double[Math.Max(Nr, 2)];
            for (int j = 0; j < profile.Length; j++) profile[j] = T_wg_pinned;

            stations[i] = new StationResult(
                Index: i,
                X_mm: s.X_mm,
                R_mm: s.R_mm,
                AreaRatioToThroat: areaRatio,
                Mach: M,
                StaticTemp_K: T_static,
                AdiabaticWallTemp_K: T_aw,
                EffectiveRecoveryTemp_K: T_aw,
                FilmEffectiveness: 0,
                HeatFlux_Wm2: q,
                h_g_Wm2K: h_g,
                h_c_Wm2K: 0,
                GasSideWallTemp_K: T_wg_pinned,
                CoolantSideWallTemp_K: T_wg_pinned,
                WallRadialProfile_K: profile,
                AxialConductionFlux_Wm2: 0,
                CoolantBulkTemp_K: inp.CoolantInletTemp_K,
                CoolantBulkPressure_Pa: inp.CoolantInletPressure_Pa,
                CoolantVelocity_ms: 0,
                Reynolds: 0,
                PrandtlBulk: 0,
                ChannelWidth_mm: 0,
                ChannelHeight_mm: 0,
                HydraulicDiameter_mm: 0,
                PressureGradient_Pam: 0);
        }

        warnings.Add("ChannelTopology.None — regen march skipped; wall pinned above service limit so an ablative-only build must pass the recession gate.");

        var diag = new SolverDiagnostics(
            MaxWallTempIterationsHit: 0,
            ChannelWidthClampedCount: 0,
            PressureClampedCount: 0,
            StationsInPseudocritical: 0,
            CleanConvergence: true);

        return new RegenSolverOutputs(
            Stations: stations,
            PeakGasSideWallT_K: T_wg_pinned,
            PeakCoolantSideWallT_K: T_wg_pinned,
            PeakStationIndex: peakIdx,
            CoolantInletT_K: inp.CoolantInletTemp_K,
            CoolantOutletT_K: inp.CoolantInletTemp_K,
            CoolantInletP_Pa: inp.CoolantInletPressure_Pa,
            CoolantOutletP_Pa: inp.CoolantInletPressure_Pa,
            CoolantPressureDrop_Pa: 0,
            TotalHeatLoad_W: totalQ,
            TotalWettedArea_mm2: totalArea_mm2,
            ThroatHeatFlux_Wm2: throatQ,
            WallTempExceedsLimit: true,
            WallMarginK: inp.Wall.MaxServiceTemp_K - T_wg_pinned,
            FilmMassFlow_kgs: 0,
            IspPenaltyFraction: 0,
            AxialConductionRMS_Wm2: 0,
            Diagnostics: diag,
            Warnings: warnings.ToArray(),
            PeakAdiabaticWallTemp_K: peakTaw);
    }

    /// <summary>
    /// PH-45 (2026-04-29): exact 1-D radial cylindrical conduction
    /// resistance referenced to the inner-wall area
    ///     R_w = r_inner · ln(r_outer / r_inner) / k
    /// Reduces to the thin-wall limit (t / k) as t/r → 0 by Taylor:
    /// r·ln(1 + t/r) → t. For t/r ≈ 0.05 (typical chamber walls) the
    /// correction is sub-3 %; for t/r ≈ 0.15 (thick high-Pc designs)
    /// it climbs to 5-10 %.
    /// </summary>
    /// <param name="rInner_mm">Inner wall radius (gas side) in mm.</param>
    /// <param name="tWall_mm">Wall thickness in mm.</param>
    /// <param name="wall">Wall material — bimetallic stacks (LinerFraction > 0)
    /// dispatch to the per-layer-T form; pure materials use the single-T
    /// `ConductivityAt(T_wg)` evaluation.</param>
    /// <param name="T_wg_K">Gas-side wall T (K) — the liner T on a bimetallic stack.</param>
    /// <param name="T_wc_K">Coolant-side wall T (K) — the jacket T on a bimetallic stack.</param>
    /// <returns>Thermal resistance per unit inner-wall area (K·m²/W).</returns>
    private static double WallResistanceLogMean_KperWperM2(
        double rInner_mm, double tWall_mm,
        WallMaterial wall, double T_wg_K, double T_wc_K)
    {
        // Z3-m1 (2026-04-29): bimetallic per-layer T. Inner liner (GRCop-42)
        // sees gas-side wall T; outer jacket (Inconel 625) sees coolant-side
        // wall T. Each layer's k evaluated at its own T then summed in
        // series via log-mean cylindrical conduction. Pre-Z3-m1 the
        // ConductivityAt(T_wg) single-T evaluation overstated jacket k by
        // ~10-15 % at the typical T_wg=900 K vs the ~T_wc=400 K the jacket
        // really sees.
        if (wall.LinerFraction > 0)
        {
            return BimetallicLogMeanResistance_KperWperM2(
                rInner_mm, tWall_mm, wall.LinerFraction, T_wg_K, T_wc_K);
        }
        // Pure-material path (legacy) — single-T evaluation at the gas-
        // side wall T, matching pre-Z3-m1 semantics bit-identically.
        double k = Math.Max(wall.ConductivityAt(T_wg_K), 1.0);
        if (rInner_mm <= 1e-3 || tWall_mm <= 0)
            return (tWall_mm * 1e-3) / k;
        double r_i_m = rInner_mm * 1e-3;
        double r_o_m = (rInner_mm + tWall_mm) * 1e-3;
        return r_i_m * Math.Log(r_o_m / r_i_m) / k;
    }

    /// <summary>
    /// Z3-m1 (2026-04-29): bimetallic series-resistance log-mean conduction
    /// referenced to the inner-wall area. Inner liner = GRCop-42 evaluated
    /// at <paramref name="T_wg_K"/>; outer jacket = Inconel 625 evaluated at
    /// <paramref name="T_wc_K"/>. Per-layer log-mean cylindrical R summed in
    /// series:
    /// <code>
    ///   R_liner  = r_inner · ln(r_iface / r_inner) / k_liner(T_wg)
    ///   R_jacket = r_inner · ln(r_outer / r_iface) / k_jacket(T_wc)
    /// </code>
    /// where <c>r_iface = r_inner + linerFraction · t_total</c>.
    /// Reduces to thin-wall on tiny radii.
    /// </summary>
    private static double BimetallicLogMeanResistance_KperWperM2(
        double rInner_mm, double tWall_mm,
        double linerFraction, double T_wg_K, double T_wc_K)
    {
        double k_liner = Math.Max(WallMaterials.GRCop42.ConductivityAt(T_wg_K), 1.0);
        double k_jacket = Math.Max(WallMaterials.Inconel625.ConductivityAt(T_wc_K), 1.0);

        double t_liner_mm = linerFraction * tWall_mm;
        double t_jacket_mm = (1.0 - linerFraction) * tWall_mm;

        // Thin-wall fallback for tiny radii.
        if (rInner_mm <= 1e-3 || tWall_mm <= 0)
            return (t_liner_mm * 1e-3) / k_liner + (t_jacket_mm * 1e-3) / k_jacket;

        double r_i_m = rInner_mm * 1e-3;
        double r_iface_m = (rInner_mm + t_liner_mm) * 1e-3;
        double r_o_m = (rInner_mm + tWall_mm) * 1e-3;

        double R_liner = r_i_m * Math.Log(r_iface_m / r_i_m) / k_liner;
        double R_jacket = r_i_m * Math.Log(r_o_m / r_iface_m) / k_jacket;
        return R_liner + R_jacket;
    }

    /// <summary>
    /// Radial T(r) across the wall. Constant-flux assumption with k(T) applied
    /// at each interior node's temperature (solved by fixed-point iteration).
    /// Nr = total nodes; node 0 is gas side (T_wg), node Nr-1 is coolant side (T_wc).
    /// </summary>
    private static double[] BuildRadialWallProfile(
        double T_wg, double T_wc, WallMaterial wall, int Nr)
    {
        var T = new double[Nr];
        // Linear initial guess.
        for (int j = 0; j < Nr; j++)
            T[j] = T_wg + (T_wc - T_wg) * j / (double)(Nr - 1);

        // With T-dependent k and constant flux, ΔT_j = q · Δx / k(T_mid_j).
        // Normalize so total ΔT matches (T_wg − T_wc).
        for (int iter = 0; iter < 10; iter++)
        {
            var dT = new double[Nr - 1];
            double totalInvK = 0;
            for (int j = 0; j < Nr - 1; j++)
            {
                double Tmid = 0.5 * (T[j] + T[j + 1]);
                double kj = Math.Max(wall.ConductivityAt(Tmid), 0.1);
                dT[j] = 1.0 / kj;
                totalInvK += dT[j];
            }
            double scale = (T_wg - T_wc) / Math.Max(totalInvK, 1e-9);
            T[0] = T_wg;
            for (int j = 1; j < Nr; j++)
                T[j] = T[j - 1] - dT[j - 1] * scale;

            // Enforce exact coolant-side value.
            double err = Math.Abs(T[Nr - 1] - T_wc);
            T[Nr - 1] = T_wc;
            if (err < 0.5) break;
        }
        return T;
    }

    /// <summary>Interpolate channel height across the three anchor points.</summary>
    private static double InterpChannelHeight(
        double x_mm, double x_cham, double x_throat, double x_exit, ChannelSchedule c)
    {
        if (x_mm <= x_throat)
        {
            double t = Math.Clamp((x_mm - x_cham) / Math.Max(x_throat - x_cham, 1e-6), 0, 1);
            return c.ChannelHeightAtChamber_mm + t * (c.ChannelHeightAtThroat_mm - c.ChannelHeightAtChamber_mm);
        }
        double t2 = Math.Clamp((x_mm - x_throat) / Math.Max(x_exit - x_throat, 1e-6), 0, 1);
        return c.ChannelHeightAtThroat_mm + t2 * (c.ChannelHeightAtExit_mm - c.ChannelHeightAtThroat_mm);
    }
}
