// RotatingDetonationCycleSolver.cs — Sprint A.W4 Rotating Detonation
// Engine (RDE).
//
// Pressure-gain combustion via azimuthally-propagating Chapman-Jouguet
// (CJ) detonation waves in an annular combustor. Unlike conventional
// Brayton-cycle deflagration, RDE combustion increases total pressure
// across the wave — typical pressure-gain ratio (PGR) ≈ 1.10–1.30
// depending on the fuel-air mixture. This yields a 5–15 % Isp
// improvement at the same fuel-air ratio.
//
// Station topology (lumped 0-D, no detailed station march):
//
//   freestream (0) → inlet capture (1) → diffuser exit (2) →
//                                        annular RDE combustor (4) →
//                                        CD nozzle (8 throat → 9 exit)
//
// Cycle physics (constant-property H₂/air or CH₄/air ideal-cycle anchors):
//
//   Inlet recovery from <see cref="InletRecovery"/> (re-uses ramjet
//   inlet model).
//
//   Combustor (detonation wave):
//     T_t4 = T_t2 + (Q_fuel · η_b) / cp
//          with Q_fuel = f · LHV_fuel
//     P_t4 = PGR · P_t2     (the defining RDE pressure-gain identity)
//
//   Nozzle:
//     M_8 = 1.0 (choked throat — RDE always chokes at design point)
//     P_9 = P_∞ (perfect expansion, design point)
//     V_9 = √(2·cp·(T_t4 − T_9))
//
//   Net thrust:
//     F = (ṁ_air + ṁ_fuel) · V_9 − ṁ_air · V_∞ + (P_9 − P_∞) · A_9
//
//   Fuel Isp:
//     Isp = F / (ṁ_fuel · g₀)
//
// Hot-side cp:
//   For H₂ fuel: constant cp ≈ 1004.7 J/(kg·K) — same approximation as
//   ramjet. For JetA / JP-8 fuel: cp(T) integration deferred (cluster
//   approximation — the published RDE Isp values for hydrocarbon fuels
//   are 10-15 % above kerosene ramjet Isp at the same flight envelope).
//
// Pressure-gain calibration anchors (from cluster of CJ-detonation theory
// + AFRL test data, Anand & Gutmark 2019):
//   H₂/air at φ=1.0:    PGR ≈ 1.27, V_CJ ≈ 2070 m/s, T_CJ/T_unburned ≈ 9.0
//   CH₄/air at φ=1.0:  PGR ≈ 1.18, V_CJ ≈ 1730 m/s, T_CJ/T_unburned ≈ 7.8
//   The design's RdePressureGainRatio carries the per-design value; the
//   cluster anchors above are sanity references.
//
// Validation tolerance per ADR-029 D4 generalised: ±15 % thrust, ±10 %
// Isp. Tighter than LACE because RDE has been ground-tested and flight-
// tested (Mitsubishi-IHI 2021); the physics anchors are real-rig data.

using System;
using System.Collections.Generic;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Rotating Detonation Engine cycle solver. Pressure-gain combustion via
/// azimuthally-propagating CJ detonation waves in an annular combustor.
/// </summary>
public sealed class RotatingDetonationCycleSolver : IAirbreathingCycleSolver
{
    /// <summary>Standard gravity g₀ [m/s²].</summary>
    public const double G0_ms2 = 9.80665;

    /// <summary>
    /// Combustion efficiency η_b. RDE detonation forces near-complete
    /// reaction within the wave structure (Anand &amp; Gutmark 2019
    /// §4.2); cluster anchor 0.99 matches the ramjet/turbojet
    /// deflagration value. Previously set to 0.95 — that was a pessimistic
    /// over-correction for "detonation wave incompleteness" that doesn't
    /// match published RDE data; corrected as part of the #548-A fix.
    /// </summary>
    public const double CombustionEfficiency = 0.99;

    /// <summary>
    /// Nozzle stagnation pressure recovery π_n = P_t9 / P_t4. Matches the
    /// ramjet anchor (0.96) for cycle-comparison consistency — the
    /// "RDE has more non-uniformity" argument doesn't justify a
    /// separate value at this 0-D fidelity (detailed wave-exit profile
    /// integration is out of scope; see Anand &amp; Gutmark 2019 §6).
    /// Previously 0.94 — corrected as part of the #548-A fix.
    /// </summary>
    public const double NozzlePressureRecovery = 0.96;

    /// <summary>
    /// Constant-property cp [J/(kg·K)] for cycle energy balance. Same
    /// anchor as ramjet/turbojet so the cross-cycle comparison stays
    /// apples-to-apples at this 0-D fidelity. Hot-side cp(T) table is
    /// deferred (see <see cref="RamjetCycleSolver"/> docstring).
    /// </summary>
    public const double HotSideCp_JkgK = 1004.7;

    /// <summary>
    /// Hot-side γ for the Chapman-Jouguet detonation-wave velocity helper
    /// (cluster mid-band for combustion products). Used ONLY by
    /// <see cref="ChapmanJouguetVelocity_ms"/> for the wave-speed sanity
    /// check; the main cycle solve uses cold-air γ=1.4 throughout the
    /// nozzle for cross-cycle consistency with the ramjet.
    /// </summary>
    public const double HotSideGamma = 1.30;

    /// <inheritdoc />
    public AirbreathingEngineKind Kind => AirbreathingEngineKind.RotatingDetonation;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any of the RDE design fields
    /// (<see cref="AirbreathingEngineDesign.RdePressureGainRatio"/>,
    /// <see cref="AirbreathingEngineDesign.RdeWaveCount"/>,
    /// <see cref="AirbreathingEngineDesign.RdeAnnularOuterDiameter_m"/>,
    /// <see cref="AirbreathingEngineDesign.RdeAnnularInnerDiameter_m"/>,
    /// <see cref="AirbreathingEngineDesign.RdeAnnularLength_m"/>,
    /// <see cref="AirbreathingEngineDesign.InletThroatArea_m2"/>,
    /// <see cref="AirbreathingEngineDesign.NozzleThroatArea_m2"/>,
    /// <see cref="AirbreathingEngineDesign.NozzleExitArea_m2"/>,
    /// <see cref="AirbreathingEngineDesign.EquivalenceRatio"/>) is NaN
    /// or out of its valid range.
    /// </exception>
    public CycleSolveResult Solve(AirbreathingEngineDesign design, FlightConditions cond)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (cond is null) throw new ArgumentNullException(nameof(cond));
        if (design.Kind != AirbreathingEngineKind.RotatingDetonation)
            throw new ArgumentException(
                $"RotatingDetonationCycleSolver.Solve called with Kind={design.Kind}; "
              + "expected RotatingDetonation.",
                nameof(design));

        if (double.IsNaN(design.RdePressureGainRatio) || design.RdePressureGainRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"RdePressureGainRatio must be positive; got {design.RdePressureGainRatio:F3}.");
        if (design.RdeWaveCount < 1)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"RdeWaveCount must be >= 1; got {design.RdeWaveCount}.");
        if (double.IsNaN(design.RdeAnnularOuterDiameter_m) || design.RdeAnnularOuterDiameter_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"RdeAnnularOuterDiameter_m must be positive; got {design.RdeAnnularOuterDiameter_m:F4} m.");
        if (double.IsNaN(design.RdeAnnularInnerDiameter_m)
            || design.RdeAnnularInnerDiameter_m <= 0
            || design.RdeAnnularInnerDiameter_m >= design.RdeAnnularOuterDiameter_m)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"RdeAnnularInnerDiameter_m must be in (0, RdeAnnularOuterDiameter_m); "
              + $"got {design.RdeAnnularInnerDiameter_m:F4} m vs outer {design.RdeAnnularOuterDiameter_m:F4} m.");
        if (double.IsNaN(design.RdeAnnularLength_m) || design.RdeAnnularLength_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"RdeAnnularLength_m must be positive; got {design.RdeAnnularLength_m:F4} m.");
        if (double.IsNaN(design.InletThroatArea_m2) || design.InletThroatArea_m2 <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"InletThroatArea_m2 must be positive; got {design.InletThroatArea_m2:F6} m^2.");
        if (double.IsNaN(design.NozzleThroatArea_m2) || design.NozzleThroatArea_m2 <= 0
            || double.IsNaN(design.NozzleExitArea_m2) || design.NozzleExitArea_m2 <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"NozzleThroatArea_m2 and NozzleExitArea_m2 must both be positive; "
              + $"got throat={design.NozzleThroatArea_m2:F6} m^2, exit={design.NozzleExitArea_m2:F6} m^2.");
        if (double.IsNaN(design.EquivalenceRatio) || design.EquivalenceRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"EquivalenceRatio must be positive for RDE; got {design.EquivalenceRatio:F3}.");

        // ── Station 0: freestream ─────────────────────────────────────────
        var atm = StandardAtmosphere.At(cond.Altitude_m);
        double T_amb   = atm.StaticT_K;
        double P_amb   = atm.StaticP_Pa;
        double rho_amb = atm.Density_kg_m3;
        double a_amb   = atm.SpeedOfSound_m_s;
        double V_inf  = cond.MachNumber * a_amb;

        // Ram total state at stations 0 / 1 / 2.
        double gAir = 1.40;
        double T_t0 = T_amb * (1.0 + 0.5 * (gAir - 1) * cond.MachNumber * cond.MachNumber);
        double P_t0 = P_amb * Math.Pow(T_t0 / T_amb, gAir / (gAir - 1));

        // Inlet recovery (re-use ramjet model).
        double pi_d = InletRecovery.Pi_d(cond.MachNumber);
        double T_t2 = T_t0;          // adiabatic intake — ram T preserved
        double P_t2 = P_t0 * pi_d;

        // ṁ_air captured.
        double mDot_air = rho_amb * V_inf * design.InletThroatArea_m2;

        // ── Station 4: RDE combustor exit (with pressure gain) ───────────
        // Fuel mass flow from equivalence ratio + stoich:
        //   f = φ · f_stoich
        //   ṁ_fuel = f · ṁ_air
        // For H₂/air f_stoich ≈ 0.0291; for JetA f_stoich ≈ 0.0680.
        double f_stoich = cond.Fuel switch
        {
            AirbreathingFuel.H2   => 0.0291,
            AirbreathingFuel.JetA => 0.0680,
            AirbreathingFuel.Jp8  => 0.0680,
            _                     => 0.0680,
        };
        double f = design.EquivalenceRatio * f_stoich;
        double mDot_fuel = f * mDot_air;
        double LHV = cond.Fuel switch
        {
            AirbreathingFuel.H2   => 120e6,    // 120 MJ/kg LH2
            AirbreathingFuel.JetA => 43e6,     // 43 MJ/kg Jet-A
            AirbreathingFuel.Jp8  => 43e6,
            _                     => 43e6,
        };
        // Combustor energy balance with η_b:
        //   T_t4 = T_t2 + f · LHV · η_b / cp
        double T_t4 = T_t2 + f * LHV * CombustionEfficiency / HotSideCp_JkgK;

        // Pressure gain — the defining RDE identity.
        double P_t4 = design.RdePressureGainRatio * P_t2;

        // ── Station 8 / 9: CD nozzle ──────────────────────────────────────
        // Pressure-gain → higher exit velocity. Perfect-expansion design
        // point: P_9 = P_∞. Uses the same IdealGasAir helpers as the
        // ramjet/turbojet path for cross-cycle consistency (constant-γ
        // ideal-gas expansion; hot-side cp(T) table is deferred — see
        // RamjetCycleSolver docstring).
        double T_t9 = T_t4;
        double P_t9 = P_t4 * NozzlePressureRecovery;
        double pStagOverPStatic = P_t9 / P_amb;

        // Guard: if the cycle eats too much pressure (e.g. negative-gain
        // RDE design at high altitude) the nozzle can't accelerate against
        // atmosphere. Surface as M_9 = NaN and zero thrust, matching the
        // ramjet's behaviour at the same boundary.
        double M_9 = pStagOverPStatic >= 1.0
            ? IdealGasAir.MachFromStagnationPressureRatio(pStagOverPStatic)
            : double.NaN;

        double T_9, V_9, F_net;
        if (double.IsNaN(M_9))
        {
            T_9   = double.NaN;
            V_9   = 0.0;
            F_net = 0.0;
        }
        else
        {
            T_9 = T_t9 / IdealGasAir.StagnationTemperatureRatio(M_9);
            double a_9 = IdealGasAir.SpeedOfSound_m_s(T_9);
            V_9 = M_9 * a_9;
            // Perfect expansion: F = ṁ_total · V_9 − ṁ_a · V_∞
            //                     = ṁ_a · ((1+f)·V_9 − V_∞)
            F_net = mDot_air * ((1.0 + f) * V_9 - V_inf);
        }

        double mDot_total = mDot_air + mDot_fuel;

        // Fuel Isp.
        double isp_fuel = (mDot_fuel > 0.0 && F_net > 0.0)
            ? F_net / (mDot_fuel * G0_ms2)
            : 0.0;

        // ── Stations array ────────────────────────────────────────────────
        var stations = new StationState[10];
        stations[0] = new StationState(T_amb, P_amb, mDot_air, cond.MachNumber);
        stations[1] = new StationState(T_t0, P_t0, mDot_air, cond.MachNumber);
        stations[2] = new StationState(T_t2, P_t2, mDot_air, 0.3);  // diffuser exit nominal
        // Station 3 (compressor exit) — degenerate for RDE.
        stations[3] = new StationState(double.NaN, double.NaN, 0.0, double.NaN);
        stations[4] = new StationState(T_t4, P_t4, mDot_total, 0.5);  // combustor exit
        // Stations 5-7 degenerate.
        stations[5] = new StationState(double.NaN, double.NaN, 0.0, double.NaN);
        stations[6] = new StationState(double.NaN, double.NaN, 0.0, double.NaN);
        stations[7] = new StationState(double.NaN, double.NaN, 0.0, double.NaN);
        // Station 8: choked throat. Uses cold-air γ=1.4 throat ratios
        // (T_8/T_t = 2/(γ+1), P_8/P_t = (2/(γ+1))^(γ/(γ-1))) for
        // consistency with the IdealGasAir-based station-9 expansion.
        const double Gamma_Air = 1.4;
        const double Throat_T_Ratio = 2.0 / (Gamma_Air + 1.0);                    // 0.8333
        const double Throat_P_Ratio = 0.5282817877171739;                          // (2/2.4)^3.5
        double T_8 = T_t4 * Throat_T_Ratio;
        double P_8 = P_t9 * Throat_P_Ratio;
        stations[8] = new StationState(T_8, P_8, mDot_total, 1.0);
        // Station 9: nozzle exit.
        stations[9] = new StationState(T_9, P_amb, mDot_total, M_9);

        var stationMap = new StationMap(
            Stations:          stations,
            ThrustNet_N:       F_net,
            SpecificImpulse_s: isp_fuel,
            FuelMassFlow_kg_s: mDot_fuel);

        return new CycleSolveResult(stationMap, CompressorDiagnostics: null, TurbineDiagnostics: null);
    }

    /// <summary>
    /// Chapman-Jouguet detonation-wave velocity helper [m/s]. Closed-form
    /// approximation V_CJ ≈ √(2·(γ²−1)·q) where q = f·LHV·η_b is the
    /// combustion-energy release per unit mass of mixture. Used by the
    /// fixture + gate for sanity checks; not invoked by the main solver
    /// (which assumes PGR is provided as a design parameter).
    /// </summary>
    public static double ChapmanJouguetVelocity_ms(double mixtureEnergy_Jkg, double gamma = HotSideGamma)
    {
        if (mixtureEnergy_Jkg < 0)
            throw new ArgumentOutOfRangeException(nameof(mixtureEnergy_Jkg),
                $"mixtureEnergy_Jkg must be non-negative; got {mixtureEnergy_Jkg}.");
        return Math.Sqrt(2.0 * (gamma * gamma - 1.0) * mixtureEnergy_Jkg);
    }

    /// <summary>
    /// Annular flow area helper A_annular = (π/4)·(D_o² − D_i²) [m²].
    /// </summary>
    public static double AnnularArea_m2(double outerDiameter_m, double innerDiameter_m)
    {
        if (outerDiameter_m <= 0 || innerDiameter_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(outerDiameter_m),
                "Outer + inner diameters must both be positive.");
        if (innerDiameter_m >= outerDiameter_m)
            throw new ArgumentOutOfRangeException(nameof(innerDiameter_m),
                $"Inner diameter ({innerDiameter_m}) must be less than outer ({outerDiameter_m}).");
        return Math.PI / 4.0 * (outerDiameter_m * outerDiameter_m
                              - innerDiameter_m * innerDiameter_m);
    }
}
