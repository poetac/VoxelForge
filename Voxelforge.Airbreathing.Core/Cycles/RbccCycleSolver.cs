// RbccCycleSolver.cs — Sprint A11 RBCC cycle solver (sub-step 1e).
//
// Rocket-Based Combined Cycle engine. Covers three flight-envelope bands
// by dispatching to existing sub-solvers based on RbccOperatingMode:
//
//   DuctedRocket (M ≤ 2.5)
//     Phase 1 simplified ejector model. Primary rocket flow entrains
//     atmospheric air at constant entrainment ratio ER = ṁ_s / ṁ_p.
//     Isobaric-mixing approximation: secondary exits at V_9 (same as
//     primary exhaust). Net thrust:
//       F_net = (ṁ_p·(1+f) + ṁ_s)·V_9 − (ṁ_p + ṁ_s)·V_∞
//     Stream B follow-on: variable-geometry ejector, mixing efficiency
//     map, secondary-stream pressure recovery, and transition dynamics.
//
//   Ramjet (M ≈ 2–6)
//     Delegates to RamjetCycleSolver via adapted design Kind.
//     Design is re-wrapped with Kind = Ramjet so the sub-solver's
//     kind-check passes cleanly.
//
//   Scramjet (M ≥ 4)
//     Delegates to ScramjetCycleSolver via adapted design Kind.
//
// Station numbering (SAE AS755) — DuctedRocket mode:
//   0  freestream
//   1  inlet face (same as 0 in lumped 0-D model)
//   2  diffuser exit (inlet recovery applied, primary + secondary merge)
//   3  NaN — no compressor
//   4  combustor exit (primary combustion only; T_t4 from SolveCombustorExitT)
//   5  pre-nozzle = station 4 (no turbine)
//   6  NaN — no afterburner
//   7  NaN — no afterburner
//   8  nozzle throat (choked; total ṁ = ṁ_p·(1+f) + ṁ_s)
//   9  nozzle exit (perfect expansion, isobaric mixing → same V_9)
//
// Ramjet and Scramjet modes delegate completely; their station maps are
// returned unchanged.

using System;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Rocket-Based Combined Cycle (RBCC) solver. Dispatches to
/// <see cref="RamjetCycleSolver"/> or <see cref="ScramjetCycleSolver"/>
/// for the high-Mach regimes; runs a Phase 1 simplified ejector model
/// for the low-Mach ducted-rocket regime.
/// </summary>
public sealed class RbccCycleSolver : IAirbreathingCycleSolver
{
    /// <summary>
    /// Phase 1 ejector mixing efficiency. 1.0 = isobaric perfect mixing;
    /// secondary exits at the same velocity as the primary exhaust.
    /// Variable-geometry ejector model with η_mix &lt; 1 is a Stream B
    /// follow-on.
    /// </summary>
    public const double EjectorMixingEfficiency = 1.0;

    /// <summary>
    /// Compressor face Mach for the ducted-rocket mass-flow estimate.
    /// Matches the ramjet convention (freestream ṁ = ρ·V·A at inlet).
    /// </summary>
    public const double DuctedRocketFaceMach = 0.5;

    /// <summary>
    /// Combustor stagnation pressure recovery π_b in ducted-rocket mode.
    /// Rockets typically run at very high combustion pressures; 0.97 is
    /// a conservative preliminary-design value for the primary combustor.
    /// </summary>
    public const double DuctedRocketCombustorPressureRecovery = 0.97;

    /// <summary>
    /// Nozzle stagnation pressure recovery π_n in ducted-rocket mode.
    /// </summary>
    public const double DuctedRocketNozzlePressureRecovery = 0.96;

    private readonly RamjetCycleSolver _ramjetSolver = new();
    private readonly ScramjetCycleSolver _scramjetSolver = new();

    /// <inheritdoc />
    public AirbreathingEngineKind Kind => AirbreathingEngineKind.Rbcc;

    /// <inheritdoc />
    public CycleSolveResult Solve(AirbreathingEngineDesign design, FlightConditions cond)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (cond is null) throw new ArgumentNullException(nameof(cond));
        if (design.Kind != AirbreathingEngineKind.Rbcc)
            throw new ArgumentException(
                $"RbccCycleSolver invoked with design.Kind = {design.Kind}; expected Rbcc.",
                nameof(design));

        return design.RbccMode switch
        {
            RbccOperatingMode.Ramjet   => SolveRamjetMode(design, cond),
            RbccOperatingMode.Scramjet => SolveScramjetMode(design, cond),
            RbccOperatingMode.DuctedRocket => SolveDuctedRocket(design, cond),
            _ => throw new ArgumentException(
                $"Unrecognised RbccOperatingMode = {design.RbccMode}.", nameof(design)),
        };
    }

    // ── Ramjet delegation ────────────────────────────────────────────────

    private CycleSolveResult SolveRamjetMode(AirbreathingEngineDesign design, FlightConditions cond)
    {
        // Re-wrap with Kind = Ramjet so RamjetCycleSolver's kind-check passes.
        var adapted = design with { Kind = AirbreathingEngineKind.Ramjet };
        return _ramjetSolver.Solve(adapted, cond);
    }

    // ── Scramjet delegation ──────────────────────────────────────────────

    private CycleSolveResult SolveScramjetMode(AirbreathingEngineDesign design, FlightConditions cond)
    {
        // Return a degenerate result below the scramjet solver's hard floor so
        // AirbreathingFeasibility can fire RBCC_MODE_OUT_OF_ENVELOPE rather than
        // propagating an ArgumentOutOfRangeException before gate evaluation.
        if (cond.MachNumber < ScramjetCycleSolver.MinimumFreestreamMach)
            return BuildDegenerateResult();

        // Re-wrap with Kind = Scramjet so ScramjetCycleSolver's kind-check passes.
        var adapted = design with { Kind = AirbreathingEngineKind.Scramjet };
        return _scramjetSolver.Solve(adapted, cond);
    }

    private static CycleSolveResult BuildDegenerateResult()
    {
        var stations = new StationState[10];
        for (int i = 0; i < 10; i++)
            stations[i] = NaNStation();

        var stationMap = new StationMap(
            Stations:          stations,
            ThrustNet_N:       0.0,
            SpecificImpulse_s: 0.0,
            FuelMassFlow_kg_s: 0.0);

        return new CycleSolveResult(
            Stations:              stationMap,
            CompressorDiagnostics: null,
            TurbineDiagnostics:    null);
    }

    // ── Ducted-rocket Phase 1 ejector model ─────────────────────────────

    private static CycleSolveResult SolveDuctedRocket(AirbreathingEngineDesign design, FlightConditions cond)
    {
        var fuel = AirbreathingFuelTables.Lookup(cond.Fuel);
        var atm  = StandardAtmosphere.At(cond.Altitude_m);

        // Station 0 / 1 — freestream. Stagnation state from M_∞.
        double V_inf = cond.MachNumber * IdealGasAir.SpeedOfSound_m_s(atm.StaticT_K);
        double T_t0  = atm.StaticT_K * IdealGasAir.StagnationTemperatureRatio(cond.MachNumber);
        double P_t0  = atm.StaticP_Pa * IdealGasAir.StagnationPressureRatio(cond.MachNumber);

        // Primary mass-flow captured at the rocket inlet face.
        // ṁ_p = ρ_∞ · V_∞ · A_inlet (Mattingly §5.3 ramjet convention).
        double mdot_p = atm.Density_kg_m3 * V_inf * design.InletThroatArea_m2;

        // Clamp to a small positive floor so quiescent / sea-level-static
        // calculations (M ≈ 0) don't produce zero mass flow.
        if (mdot_p < 1e-6)
        {
            // Sea-level static: estimate via face-Mach 0.5 density/velocity.
            double T_face = T_t0 / IdealGasAir.StagnationTemperatureRatio(DuctedRocketFaceMach);
            double P_face = P_t0 / IdealGasAir.StagnationPressureRatio(DuctedRocketFaceMach);
            double rho_face = P_face / (IdealGasAir.R_J_kg_K * T_face);
            double V_face = DuctedRocketFaceMach * IdealGasAir.SpeedOfSound_m_s(T_face);
            mdot_p = rho_face * V_face * design.InletThroatArea_m2;
        }

        var s0 = new StationState(T_t0, P_t0, mdot_p, cond.MachNumber);

        // Station 2 — diffuser exit + ejector inlet. Ram recovery applied.
        double pi_d = InletRecovery.Pi_d(cond.MachNumber);
        double T_t2 = T_t0;
        double P_t2 = P_t0 * pi_d;
        var s2 = new StationState(T_t2, P_t2, mdot_p, 0.2);

        // Station 4 — combustor exit. Primary rocket combustion of ṁ_p.
        // T_t4 from TurbojetCycleSolver.SolveCombustorExitT (shared helper).
        // T_t3 ≡ T_t2 (no compressor in ducted-rocket primary path).
        double f     = design.EquivalenceRatio * fuel.StoichiometricFuelAirRatio;
        double T_t4  = TurbojetCycleSolver.SolveCombustorExitT(
            cond.Fuel, T_t2, f, fuel.LowerHeatingValue_J_kg);
        double P_t4  = P_t2 * DuctedRocketCombustorPressureRecovery;
        double mdot_primary_exhaust = mdot_p * (1.0 + f);
        var s4 = new StationState(T_t4, P_t4, mdot_primary_exhaust, 0.2);

        // Ejector secondary stream. ṁ_s = ER · ṁ_p.
        // Phase 1 isobaric-mixing: secondary exits at the same V_9 as the
        // primary exhaust (EjectorMixingEfficiency = 1.0). The secondary
        // mass is added to the nozzle total-flow for F_net computation.
        double mdot_s = design.EjectorEntrainmentRatio * mdot_p;
        double mdot_nozzle = mdot_primary_exhaust + mdot_s;

        // Station 9 — nozzle exit, perfect expansion (P_9 = P_∞).
        double T_t9 = T_t4;
        double P_t9 = P_t4 * DuctedRocketNozzlePressureRecovery;
        double pStagOverPStatic = P_t9 / atm.StaticP_Pa;

        double M_9, V_9, F_net;
        if (pStagOverPStatic >= 1.0)
        {
            M_9 = IdealGasAir.MachFromStagnationPressureRatio(pStagOverPStatic);
            double T_9 = T_t9 / IdealGasAir.StagnationTemperatureRatio(M_9);
            V_9 = M_9 * IdealGasAir.SpeedOfSound_m_s(T_9);
            // Isobaric mixing: both primary exhaust and entrained secondary
            // air exit at V_9. Secondary entered at V_inf (freestream).
            // F_net = (ṁ_primary·V_9 + ṁ_s·V_9) − (ṁ_p + ṁ_s)·V_inf
            //       = mdot_nozzle·V_9 − (ṁ_p + ṁ_s)·V_inf
            F_net = mdot_nozzle * V_9 - (mdot_p + mdot_s) * V_inf;
        }
        else
        {
            M_9   = double.NaN;
            V_9   = 0.0;
            F_net = 0.0;
        }

        var s9 = new StationState(T_t9, P_t9, mdot_nozzle, M_9);

        // Performance — Isp referenced to primary fuel flow only.
        double mdot_f = mdot_p * f;
        double Isp    = (mdot_f > 0.0 && F_net > 0.0)
            ? F_net / (mdot_f * StandardAtmosphere.G0_m_s2)
            : 0.0;

        var stations = new StationState[10];
        stations[0] = s0;
        stations[1] = s0;
        stations[2] = s2;
        stations[3] = NaNStation();
        stations[4] = s4;
        stations[5] = s4;
        stations[6] = NaNStation();
        stations[7] = NaNStation();
        stations[8] = new StationState(T_t9, P_t9, mdot_nozzle, 1.0);
        stations[9] = s9;

        var stationMap = new StationMap(
            Stations:          stations,
            ThrustNet_N:       F_net,
            SpecificImpulse_s: Isp,
            FuelMassFlow_kg_s: mdot_f);

        return new CycleSolveResult(
            Stations:              stationMap,
            CompressorDiagnostics: null,
            TurbineDiagnostics:    null);
    }

    private static StationState NaNStation()
        => new(double.NaN, double.NaN, 0.0, double.NaN);
}
