// LaceCycleSolver.cs — Sprint A.W3 Liquid Air Cycle Engine.
//
// LACE is a hybrid air-breathing / rocket cycle: cryogenic LH₂ propellant
// is used as a heat sink to cool and liquefy captured ambient air in a
// high-effectiveness counterflow precooler; liquid air + hot LH₂ then
// burn in a rocket-style chamber + CD nozzle. Reference: RB-545
// (Rolls-Royce / HOTOL 1980s precursor) at ~Mach 5 / 200 kN thrust;
// conceptual ancestor of Reaction Engines' SABRE.
//
// Solver topology (lumped 0-D, no station march):
//
//   freestream (0) → inlet capture (1) → precooler (cold-side exit, 2)
//                                      → liquid-air pump (3, isothermal model)
//                                      → rocket chamber (4)
//                                      → CD nozzle (8, throat) → exit (9)
//
//   LH₂ propellant flow:
//     fuel tank (cold) → precooler hot-side → chamber injector
//     The precooler heats LH₂ from ~25 K to ~600 K (RB-545 cluster).
//
// Energy balance:
//   Precooler:        ε · ṁ_air · cp_air · (T_air_t − T_LH2_in) = Q_pre
//                     T_air_out = T_air_t − Q_pre/(ṁ_air · cp_air)
//                     T_LH2_out = T_LH2_in + Q_pre/(ṁ_H2 · cp_H2)
//
//   Air must reach liquefaction-temperature band (~ 90 K) for the design
//   to be a true LACE (precooler-only ε criterion). The gate
//   LACE_AIR_LIQUEFACTION_INSUFFICIENT fires when T_air_out > 95 K.
//
// Chamber + nozzle (rocket-style, lumped CEA-equivalent):
//   For LH₂/Air at MR_a/f varying [5, 35], chamber T spans roughly
//   2400–3500 K. Cluster fit: T_c(MR) ≈ 3500 − 30·|MR − 10| (peak near
//   MR=10, falling either side; rocket-cluster anchor).
//
//   c* = √(R_chamber · T_c) / Γ(γ) with γ ≈ 1.20 (combustion-products
//   cluster average).
//
//   Exit velocity (finite area ratio ε = A_e/A_t):
//     V_e = η_eff · √( 2γ/(γ−1) · R · T_c · (1 − (P_e/P_c)^((γ−1)/γ)) )
//   where P_e/P_c follows from ε via the isentropic area-Mach relation
//   (SupersonicExitMachFromAreaRatio). The (1 − (P_e/P_c)^((γ−1)/γ)) factor
//   sits inside the radical — at the vacuum limit (ε → ∞, P_e → 0) it → 1 and
//   V_e recovers the maximum √(2γ/(γ−1)·R·T_c). Vacuum Isp = V_e / g₀.
//
//   For LACE flight regime (~Mach 5, ~25 km altitude, P_amb ≈ 2.5 kPa),
//   approximate the ambient-correction by Isp_eff = Isp_vac − ΔIsp(altitude).
//
// Net thrust (LACE is supersonic-cruise; ram drag matters):
//   F_net = ṁ_total · V_e + (P_e − P_amb) · A_e − ṁ_air · V_∞
//
// where ṁ_total = ṁ_air + ṁ_H2, V_e = Isp · g₀.
//
// Validation tolerance per ADR-029 D4 generalised: ±20 % thrust / ±15 %
// Isp. Looser than turbojet/turbofan because LACE depends on the
// liquefaction-band assumption that's hard to validate without a real
// precooler test rig.

using System;
using System.Collections.Generic;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Liquid Air Cycle Engine solver. Hybrid air-breathing / rocket;
/// precooler liquefies captured air, LH₂ + liquid air burn rocket-style.
/// </summary>
public sealed class LaceCycleSolver : IAirbreathingCycleSolver
{
    /// <summary>Standard gravity g₀ [m/s²].</summary>
    public const double G0_ms2 = 9.80665;

    /// <summary>LH₂ tank-side inlet temperature [K] (saturated liquid hydrogen).</summary>
    public const double LH2InletTemp_K = 25.0;

    /// <summary>
    /// LH₂ specific heat at the precooler hot-side mean temperature [J/(kg·K)].
    /// Liquid + supercritical H₂ averages ~14 000 J/(kg·K) across 25–600 K
    /// (per <see cref="Voxelforge.Combustion.LH2ThermalProperties.Cp_J_kgK"/>
    /// at T_mean ≈ 300 K).
    /// </summary>
    public const double LH2SpecificHeat_JkgK = 14_000.0;

    /// <summary>Air specific heat at constant pressure [J/(kg·K)] (constant-property approximation).</summary>
    public const double AirSpecificHeat_JkgK = 1004.7;

    /// <summary>Target liquid-air temperature [K] — saturated-liquid air at ~1 bar.</summary>
    public const double LiquidAirTargetTemp_K = 80.0;

    /// <summary>Frost-line trigger temperature [K] — below this, water-vapour ice fouls precooler fins.</summary>
    public const double FrostLineTriggerTemp_K = 180.0;

    /// <summary>Chamber γ for combustion-products cluster average.</summary>
    public const double GammaChamber = 1.20;

    /// <summary>R_chamber [J/(kg·K)] — H₂O-dominated combustion products.</summary>
    public const double GasConstantChamber_JkgK = 360.0;

    /// <summary>
    /// LACE effective-Isp efficiency η_eff = frozen-flow × nozzle-divergence
    /// × combustion-efficiency. Cluster mid-band 0.92 — LACE chambers run
    /// cooler than pure-rocket so the frozen-flow loss is smaller.
    /// </summary>
    public const double EffectiveIspEfficiency = 0.92;

    /// <inheritdoc />
    public AirbreathingEngineKind Kind => AirbreathingEngineKind.LiquidAirCycle;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any of the LACE design fields
    /// (<see cref="AirbreathingEngineDesign.PrecoolerEffectiveness"/>,
    /// <see cref="AirbreathingEngineDesign.LH2MassFlow_kgs"/>,
    /// <see cref="AirbreathingEngineDesign.LaceChamberPressure_bar"/>,
    /// <see cref="AirbreathingEngineDesign.LaceAirToFuelRatio"/>,
    /// <see cref="AirbreathingEngineDesign.InletThroatArea_m2"/>,
    /// <see cref="AirbreathingEngineDesign.NozzleExitArea_m2"/>) is NaN
    /// or out of its valid range.
    /// </exception>
    public CycleSolveResult Solve(AirbreathingEngineDesign design, FlightConditions cond)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (cond is null)   throw new ArgumentNullException(nameof(cond));
        if (design.Kind != AirbreathingEngineKind.LiquidAirCycle)
            throw new ArgumentException(
                $"LaceCycleSolver.Solve called with Kind={design.Kind}; expected LiquidAirCycle.",
                nameof(design));

        // NaN-trap: every LACE field must be populated.
        if (double.IsNaN(design.PrecoolerEffectiveness)
            || design.PrecoolerEffectiveness <= 0 || design.PrecoolerEffectiveness > 1)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"PrecoolerEffectiveness must be in (0, 1]; got {design.PrecoolerEffectiveness:F3}.");
        if (double.IsNaN(design.LH2MassFlow_kgs) || design.LH2MassFlow_kgs <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"LH2MassFlow_kgs must be positive; got {design.LH2MassFlow_kgs:F3} kg/s.");
        if (double.IsNaN(design.LaceChamberPressure_bar) || design.LaceChamberPressure_bar <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"LaceChamberPressure_bar must be positive; got {design.LaceChamberPressure_bar:F3} bar.");
        if (double.IsNaN(design.LaceAirToFuelRatio) || design.LaceAirToFuelRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"LaceAirToFuelRatio must be positive; got {design.LaceAirToFuelRatio:F3}.");
        if (double.IsNaN(design.InletThroatArea_m2) || design.InletThroatArea_m2 <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"InletThroatArea_m2 must be positive; got {design.InletThroatArea_m2:F6} m^2.");
        if (double.IsNaN(design.NozzleExitArea_m2) || design.NozzleExitArea_m2 <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"NozzleExitArea_m2 must be positive; got {design.NozzleExitArea_m2:F6} m^2.");

        // ── Station 0: freestream ─────────────────────────────────────────
        var atm = StandardAtmosphere.At(cond.Altitude_m);
        double T_amb   = atm.StaticT_K;
        double P_amb   = atm.StaticP_Pa;
        double rho_amb = atm.Density_kg_m3;
        double a_amb   = atm.SpeedOfSound_m_s;
        double V_inf  = cond.MachNumber * a_amb;

        // Ram total-temperature rise: T_t1 = T_amb · (1 + (γ-1)/2 · M²).
        double T_t1 = T_amb * (1.0 + 0.2 * cond.MachNumber * cond.MachNumber);

        // ṁ_air captured at the inlet face = ρ_∞ · V_∞ · A_inlet.
        double mDot_air = rho_amb * V_inf * design.InletThroatArea_m2;
        double mDot_H2  = design.LH2MassFlow_kgs;

        // ── Precooler energy balance ──────────────────────────────────────
        // Hot-side (air) inlet at stagnation T_t1; cold-side (LH₂) inlet at
        // LH2InletTemp_K. The effectiveness is hot-side: T_air_out is the
        // primary metric for the liquefaction-target gate.
        double T_air_out = T_t1 - design.PrecoolerEffectiveness * (T_t1 - LH2InletTemp_K);
        double Q_pre    = mDot_air * AirSpecificHeat_JkgK * (T_t1 - T_air_out);
        double T_LH2_out = mDot_H2 > 0
            ? LH2InletTemp_K + Q_pre / (mDot_H2 * LH2SpecificHeat_JkgK)
            : double.NaN;

        // ── Chamber temperature from MR cluster fit ──────────────────────
        // T_c(MR_a/f) ≈ 3500 − 30·|MR − 10|, clamped to [2000, 3700] K.
        double MR = design.LaceAirToFuelRatio;
        double T_c = 3500.0 - 30.0 * Math.Abs(MR - 10.0);
        T_c = Math.Max(2000.0, Math.Min(3700.0, T_c));

        // ── Chamber + nozzle (rocket-style) ──────────────────────────────
        double R_c   = GasConstantChamber_JkgK;
        double g     = GammaChamber;
        double gp1   = g + 1.0;
        double gm1   = g - 1.0;
        double Pc_Pa = design.LaceChamberPressure_bar * 1e5;

        // ── Nozzle expansion from the design area ratio ε = A_e/A_t ─────────
        // Invert the isentropic area-Mach relation for the supersonic exit
        // Mach, then the exit/chamber pressure ratio P_e/P_c. For γ=1.20 and
        // ε in the cluster range [10, 100], P_e/P_c spans roughly [3e-3, 5e-4].
        double areaRatio = design.NozzleThroatArea_m2 > 0
            ? design.NozzleExitArea_m2 / design.NozzleThroatArea_m2
            : 40.0;
        double M_e      = SupersonicExitMachFromAreaRatio(Math.Max(1.0001, areaRatio), g);
        double peOverPc = Math.Pow(1.0 + 0.5 * gm1 * M_e * M_e, -g / gm1);

        // Exit velocity. V_eq_vac is the vacuum (infinite-ε) limit; the
        // finite-expansion factor √(1 − (P_e/P_c)^((γ−1)/γ)) brings it down to
        // the value for this nozzle's actual area ratio — the same factor every
        // rocket-style cycle carries (and that this solver's own docstring
        // lists). It was previously dropped, so V_e was taken at the
        // infinite-ε limit, over-predicting exit velocity by ~20-30 % at the
        // cluster area ratios (and thrust/Isp more, after ram-drag subtraction).
        double V_eq_vac      = EffectiveIspEfficiency * Math.Sqrt(2.0 * g / gm1 * R_c * T_c);
        double finiteExp     = Math.Sqrt(Math.Max(0.0, 1.0 - Math.Pow(peOverPc, gm1 / g)));
        // Coarse ambient back-pressure correction: ΔV_eq ≈ V_eq · (P_amb/P_c)·factor.
        double ambCorrection = Math.Max(0.0, P_amb / Math.Max(Pc_Pa, 1e3));
        double V_eq_eff      = V_eq_vac * finiteExp * (1.0 - 0.3 * ambCorrection);

        double mDot_total = mDot_air + mDot_H2;
        double F_jet      = mDot_total * V_eq_eff;
        double F_ram_drag = mDot_air * V_inf;
        double F_net      = F_jet - F_ram_drag;

        // Fuel Isp: kg-of-fuel basis (air-breathing convention).
        double isp_fuel = mDot_H2 > 0
            ? F_net / (mDot_H2 * G0_ms2)
            : 0.0;

        // ── Stations ───────────────────────────────────────────────────────
        var stations = new StationState[10];
        // 0  freestream
        stations[0] = new StationState(T_amb, P_amb, mDot_air, cond.MachNumber);
        // 1  inlet face (post-ram, pre-precooler)
        stations[1] = new StationState(T_t1, P_amb, mDot_air, cond.MachNumber);
        // 2  precooler exit (cold air, ready for liquefaction)
        stations[2] = new StationState(T_air_out, P_amb * 0.97, mDot_air, 0.20);
        // 3  liquid-air pump exit (compressed to Pc)
        stations[3] = new StationState(T_air_out + 5.0, Pc_Pa, mDot_air, 0.05);
        // 4  chamber (post-combustion)
        stations[4] = new StationState(T_c, Pc_Pa, mDot_total, 0.10);
        // 5-7 degenerate
        stations[5] = new StationState(double.NaN, double.NaN, 0.0, double.NaN);
        stations[6] = new StationState(double.NaN, double.NaN, 0.0, double.NaN);
        stations[7] = new StationState(double.NaN, double.NaN, 0.0, double.NaN);
        // 8  nozzle throat
        stations[8] = new StationState(T_c * 2.0 / gp1, Pc_Pa * Math.Pow(2.0 / gp1, g / gm1), mDot_total, 1.0);
        // 9  nozzle exit — consistent with the area-ratio expansion above.
        double P_e = Pc_Pa * peOverPc;
        double T_e = T_c * Math.Pow(peOverPc, gm1 / g);
        stations[9] = new StationState(T_e, P_e, mDot_total, M_e);

        var stationMap = new StationMap(
            Stations:          stations,
            ThrustNet_N:       F_net,
            SpecificImpulse_s: isp_fuel,
            FuelMassFlow_kg_s: mDot_H2);

        return new CycleSolveResult(stationMap, CompressorDiagnostics: null, TurbineDiagnostics: null)
        {
            // Use SpecificWork_Jkg to surface the precooler heat duty Q_pre
            // as a diagnostic quantity (per-kg-air basis).
            SpecificWork_Jkg = mDot_air > 0 ? Q_pre / mDot_air : 0.0,
            // ThermalEfficiency = jet kinetic / fuel chemical = (½·ṁ·V²) / (LHV·ṁ_f).
            // LHV_H2 = 120 MJ/kg.
            ThermalEfficiency = mDot_H2 > 0
                ? Math.Max(0.0, Math.Min(1.0,
                    0.5 * mDot_total * V_eq_eff * V_eq_eff / (mDot_H2 * 120e6)))
                : 0.0,
        };
    }

    /// <summary>
    /// Supersonic exit Mach number from the nozzle area ratio ε = A_e/A_t,
    /// by inverting the isentropic area-Mach relation
    /// A/A* = (1/M)·[(2/(γ+1))·(1 + (γ−1)/2·M²)]^((γ+1)/(2(γ−1))) on its
    /// monotone supersonic branch. Fixed-iteration bisection → deterministic
    /// (no convergence-tolerance branching). γ is a parameter because the
    /// LACE chamber γ (≈1.20) differs from air's 1.40, so the
    /// air-specialised <c>IdealGasAir</c> helpers don't apply.
    /// </summary>
    /// <param name="areaRatio">Nozzle area ratio ε = A_e/A_t (must be ≥ 1).</param>
    /// <param name="gamma">Ratio of specific heats for the exhaust.</param>
    internal static double SupersonicExitMachFromAreaRatio(double areaRatio, double gamma)
    {
        double gp1 = gamma + 1.0;
        double gm1 = gamma - 1.0;
        double expo = gp1 / (2.0 * gm1);
        double lo = 1.0, hi = 50.0;
        for (int i = 0; i < 80; i++)
        {
            double m = 0.5 * (lo + hi);
            double term = (2.0 / gp1) * (1.0 + 0.5 * gm1 * m * m);
            double aOverAStar = Math.Pow(term, expo) / m;
            if (aOverAStar > areaRatio) hi = m; else lo = m;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>
    /// Precooler outlet temperature for the air-side. Closed-form helper
    /// for gate evaluators that need to inspect the precooler state
    /// without re-running the full solve.
    /// </summary>
    public static double PrecoolerOutletAirTemp_K(
        double effectiveness,
        double inletAirTotalTemp_K,
        double lh2InletTemp_K = LH2InletTemp_K)
    {
        if (effectiveness < 0 || effectiveness > 1)
            throw new ArgumentOutOfRangeException(nameof(effectiveness),
                $"Effectiveness must be in [0, 1]; got {effectiveness}.");
        return inletAirTotalTemp_K - effectiveness * (inletAirTotalTemp_K - lh2InletTemp_K);
    }
}
