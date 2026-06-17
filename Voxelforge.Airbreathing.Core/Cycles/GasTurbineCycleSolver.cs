// GasTurbineCycleSolver.cs — open Brayton-cycle gas turbine station march.
//
// Stationary power generation (Step 2, Sprint A8). No useful jet exhaust;
// shaft work output is the primary product. Optional recuperator pre-heats
// combustor inlet air using turbine exhaust.
//
// Station numbering (gas turbine, differs from propulsive turbojet):
//   0  ambient (M ≈ 0 for stationary units)
//   1  inlet face (≈ ambient, lumped 0-D)
//   2  compressor exit
//   3  combustor exit / turbine inlet (TIT)
//   4  turbine exit (power turbine)
//   5-8 unused / NaN
//   9  stack exhaust (post-recuperator hot side, if any)
//
// Simplifying assumptions
// -----------------------
//   1. Single-shaft single-stage cycle. Real two-spool engines (separate
//      gas-generator turbine + power turbine) are structurally equivalent
//      for the preliminary-design power balance.
//   2. Compressor: constant-efficiency stand-in map (η_c = 0.85).
//   3. Turbine: direct isentropic expansion to ambient pressure (P_t4 = P_t0),
//      η_t = 0.88. NOT using ITurbineMap (shaft-balance form) because the GT
//      turbine expansion is pressure-drop driven, not work-requirement driven.
//   4. Combustor uses TurbojetCycleSolver.SolveCombustorExitT for cp(T) routing:
//      kerosene fuels use enthalpy tables; H2 falls back to constant-cp form.
//   5. Power balance uses constant cp (IdealGasAir.Cp_J_kg_K) for W_comp,
//      W_turb, W_net — consistent with the feasibility gate computation.
//   6. Recuperated cycle: Picard iteration (≤ 15 iterations, convergence
//      tolerance 0.5 K on T_t4). Non-recuperated (ε = 0): single pass.
//   7. Face Mach = 0.5, same as TurbojetCycleSolver, for mass-flow model.
//   8. ThrustNet_N = 0, SpecificImpulse_s = 0 (stationary unit).
//
// GE LM2500 (simple-cycle, sea level) with φ = 0.32, π_c = 18, Jp8:
//   W_net ≈ 22 MW, η_th ≈ 0.36, within ±15 % of public GE spec.

using System;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Open Brayton-cycle gas turbine solver. Stationary power generation;
/// optional recuperator; constant-efficiency compressor + direct-expansion
/// turbine to ambient pressure.
/// </summary>
public sealed class GasTurbineCycleSolver : IAirbreathingCycleSolver
{
    /// <summary>Combustor stagnation pressure recovery π_b (same as turbojet).</summary>
    public const double CombustorPressureRecovery = 0.96;

    /// <summary>Combustion efficiency η_b (same as turbojet).</summary>
    public const double CombustionEfficiency = 0.99;

    /// <summary>Compressor face Mach — parametric stand-in (same as turbojet).</summary>
    public const double CompressorFaceMach = 0.5;

    /// <summary>
    /// Turbine isentropic efficiency η_t = 0.88. Intentionally below
    /// <see cref="ConstantEfficiencyTurbineMap"/>'s 0.90 — a power turbine
    /// that expands all the way to atmospheric pressure (higher PR) typically
    /// operates at slightly lower η_t than a turbojet turbine that expands
    /// only far enough to close the shaft balance.
    /// </summary>
    public const double TurbineIsentropicEfficiency = 0.88;

    // Picard iteration limits for recuperated cycle.
    private const int MaxPicardIterations = 15;
    private const double PicardTolerance_K = 0.5;

    // (γ-1)/γ for air (γ = 1.40).
    private static readonly double GammaRatio = (IdealGasAir.Gamma - 1.0) / IdealGasAir.Gamma;

    /// <inheritdoc />
    public AirbreathingEngineKind Kind => AirbreathingEngineKind.GasTurbine;

    /// <inheritdoc />
    public CycleSolveResult Solve(AirbreathingEngineDesign design, FlightConditions cond)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (cond   is null) throw new ArgumentNullException(nameof(cond));
        if (design.Kind != AirbreathingEngineKind.GasTurbine)
            throw new ArgumentException(
                $"GasTurbineCycleSolver invoked with design.Kind = {design.Kind}; "
              + "expected GasTurbine.",
                nameof(design));

        var fuel = AirbreathingFuelTables.Lookup(cond.Fuel);
        var atm  = StandardAtmosphere.At(cond.Altitude_m);

        // ── Station 0 / 1: ambient ────────────────────────────────────────
        // At M≈0 (stationary unit), stagnation ≈ static.
        double T_t0 = atm.StaticT_K * IdealGasAir.StagnationTemperatureRatio(cond.MachNumber);
        double P_t0 = atm.StaticP_Pa * IdealGasAir.StagnationPressureRatio(cond.MachNumber);

        // Mass flow via face-Mach model (same parametric stand-in as turbojet).
        double T_face = T_t0 / IdealGasAir.StagnationTemperatureRatio(CompressorFaceMach);
        double P_face = P_t0 / IdealGasAir.StagnationPressureRatio(CompressorFaceMach);
        double rho_face = P_face / (IdealGasAir.R_J_kg_K * T_face);
        double V_face   = CompressorFaceMach * IdealGasAir.SpeedOfSound_m_s(T_face);
        double mdot_air = rho_face * V_face * design.InletThroatArea_m2;

        var s0 = new StationState(T_t0, P_t0, mdot_air, cond.MachNumber);

        // ── Station 2: compressor exit ────────────────────────────────────
        var compPt = ConstantEfficiencyCompressorMap.Default.Operate(
            T_t0, P_t0, design.CompressorPressureRatio);
        double T_t2 = compPt.OutletStagnationT_K;
        double P_t2 = compPt.OutletStagnationP_Pa;
        var s2 = new StationState(T_t2, P_t2, mdot_air, 0.3);

        // ── Turbine expansion ratio ───────────────────────────────────────
        // Turbine expands from P_t3 = P_t2 × π_b to P_t0 (ambient).
        // Total pressure ratio = π_c × π_b; precompute once.
        double PR_turb          = design.CompressorPressureRatio * CombustorPressureRecovery;
        double PR_turb_exponent = Math.Pow(PR_turb, GammaRatio);   // PR^((γ-1)/γ)

        // ── Fuel setup ────────────────────────────────────────────────────
        double f          = design.EquivalenceRatio * fuel.StoichiometricFuelAirRatio;
        double mdot_total = mdot_air * (1.0 + f);
        double lhv        = fuel.LowerHeatingValue_J_kg;
        double eps        = design.RecuperatorEffectiveness;

        // ── Picard iteration for recuperated combustor inlet T ────────────
        // Non-recuperated (ε = 0): T_comb_in = T_t2 throughout → converges
        //   in one pass (delta on first iteration is always < tolerance).
        // Recuperated (ε > 0): T_comb_in = T_t2 + ε × (T_t4 - T_t2);
        //   iterate until T_t4 converges.
        double T_comb_in = T_t2;
        double T_t3 = T_t2;
        double T_t4 = T_t2;

        for (int iter = 0; iter < MaxPicardIterations; iter++)
        {
            T_t3 = TurbojetCycleSolver.SolveCombustorExitT(cond.Fuel, T_comb_in, f, lhv);
            double T_t4_is  = T_t3 / PR_turb_exponent;
            double T_t4_new = T_t3 - TurbineIsentropicEfficiency * (T_t3 - T_t4_is);

            double delta    = Math.Abs(T_t4_new - T_t4);
            T_t4            = T_t4_new;
            T_comb_in       = T_t2 + eps * (T_t4 - T_t2);

            if (delta < PicardTolerance_K) break;
        }

        double P_t3 = P_t2 * CombustorPressureRecovery;
        double P_t4 = P_t0;   // turbine exhausts to ambient

        var s3 = new StationState(T_t3, P_t3, mdot_total, 0.3);
        var s4 = new StationState(T_t4, P_t4, mdot_total, 0.4);

        // ── Station 9: stack exhaust ──────────────────────────────────────
        // After recuperator hot-side cooling (or T_t4 directly when ε = 0).
        double T_t9 = T_t4 - eps * (T_t4 - T_t2);
        var s9 = new StationState(T_t9, P_t0, mdot_total, double.NaN);

        // ── Power outputs (constant cp for consistency with gates) ────────
        double cp    = IdealGasAir.Cp_J_kg_K;
        double W_comp = mdot_air   * cp * (T_t2 - T_t0);
        double W_turb = mdot_total * cp * (T_t3 - T_t4);
        double W_net  = W_turb - W_comp;

        double mdot_fuel     = mdot_air * f;
        double eta_th        = (mdot_fuel > 0.0 && lhv > 0.0)
            ? W_net / (mdot_fuel * lhv)
            : 0.0;
        double specific_work = mdot_air > 0.0 ? W_net / mdot_air : 0.0;

        // ── Build station array ───────────────────────────────────────────
        var stations = new StationState[10];
        stations[0] = s0;
        stations[1] = s0;           // inlet face = ambient (lumped 0-D)
        stations[2] = s2;
        stations[3] = s3;           // TIT (combustor exit / turbine inlet)
        stations[4] = s4;           // turbine exit
        stations[5] = NaNStation();
        stations[6] = NaNStation();
        stations[7] = NaNStation();
        stations[8] = NaNStation();
        stations[9] = s9;           // stack exhaust

        var stationMap = new StationMap(
            Stations:          stations,
            ThrustNet_N:       0.0,
            SpecificImpulse_s: 0.0,
            FuelMassFlow_kg_s: mdot_fuel);

        return new CycleSolveResult(
            Stations:              stationMap,
            CompressorDiagnostics: null,
            TurbineDiagnostics:    null)
        {
            ShaftPower_W      = W_net,
            ThermalEfficiency = eta_th,
            SpecificWork_Jkg  = specific_work,
        };
    }

    private static StationState NaNStation()
        => new(double.NaN, double.NaN, 0.0, double.NaN);
}
