// ScramjetCycleSolver.cs — Sprint A10 scramjet station march.
//
// Supersonic-combustion ramjet (scramjet) ideal cycle analysis.
// Constant-property (γ = 1.40, cp = 1004.7 J/(kg·K)) with Rayleigh-
// flow heat addition in the constant-area supersonic combustor.
//
// Station numbering (SAE AS755):
//   0  freestream
//   1  inlet face (same as 0 in lumped 0-D model)
//   2  oblique-shock inlet exit (ScramjetInletRecovery applied)
//   3  isolator exit = combustor inlet (IsolatorRecovery applied;
//      M still supersonic)
//   4  combustor exit (Rayleigh heat addition; M still > 1 at design)
//   5  = station 4 (no turbine)
//   6  degenerate (no afterburner)
//   7  degenerate (no afterburner)
//   8  = station 4 (no convergent nozzle throat; flow arrives
//      supersonic from combustor)
//   9  nozzle exit (perfect expansion → P_9 = P_∞)
//
// Simplifying assumptions (Sprint A10)
// --------------------------------------
//   1. Constant cp + γ (cold/hot side identical). cp(T) tabulation
//      deferred to a follow-on sprint when fixture tolerances tighten.
//   2. Perfect nozzle expansion at design point: P_9 = P_∞.
//   3. Rayleigh-flow model for the combustor: constant-area duct,
//      heat addition lowers M on the supersonic branch toward 1.
//      Binary search finds the supersonic-branch M_4 satisfying the
//      energy balance. Near-choke (τ > max feasible) saturates M_4
//      at 1.001 and fires an advisory gate downstream.
//   4. H2 fuel only (A10 scope). cp(T) tabulation for hydrocarbon
//      fuels lands when the fixture library demands it.
//   5. Combustor-inlet Mach from ScramjetInletRecovery.CombustorInletMach
//      (3-shock ramp approximation). Real variable-geometry schedules
//      are out of scope for this sprint.
//   6. Isolator recovery from IsolatorRecovery.Pi_iso; isolator length
//      is carried in the design record but does not feed the correlations
//      directly in this preliminary-design model.

using System;
using System.Collections.Generic;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Scramjet (supersonic combustion ramjet) cycle solver.
/// Constant-property ideal cycle with Rayleigh-flow combustor.
/// </summary>
public sealed class ScramjetCycleSolver : IAirbreathingCycleSolver
{
    /// <summary>
    /// Scramjet combustion efficiency η_b. Lower than subsonic ramjet
    /// (0.99) due to the shorter residence time at supersonic flow
    /// speeds; 0.95 is the standard preliminary-design value for a
    /// well-designed H2-fuelled scramjet combustor (Heiser &amp; Pratt
    /// §6.3).
    /// </summary>
    public const double CombustionEfficiency = 0.95;

    /// <summary>
    /// Nozzle stagnation pressure recovery factor π_n = P_t9 / P_t4.
    /// Scramjet nozzles are long expansion ramps rather than convergent-
    /// divergent ducts; 0.95 accounts for friction + shock losses.
    /// </summary>
    public const double NozzlePressureRecovery = 0.95;

    /// <summary>
    /// Minimum freestream Mach accepted. Below this the inlet shock
    /// system is not self-starting and the isolator cannot sustain
    /// supersonic combustion.
    /// </summary>
    public const double MinimumFreestreamMach = 3.0;

    /// <inheritdoc />
    public AirbreathingEngineKind Kind => AirbreathingEngineKind.Scramjet;

    /// <inheritdoc />
    public CycleSolveResult Solve(AirbreathingEngineDesign design, FlightConditions cond)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (cond is null) throw new ArgumentNullException(nameof(cond));
        if (design.Kind != AirbreathingEngineKind.Scramjet)
            throw new ArgumentException(
                $"ScramjetCycleSolver invoked with design.Kind = {design.Kind}; expected Scramjet.",
                nameof(design));
        if (cond.MachNumber < MinimumFreestreamMach)
            throw new ArgumentOutOfRangeException(nameof(cond),
                $"Freestream Mach {cond.MachNumber:F2} below scramjet minimum {MinimumFreestreamMach:F1}. "
              + "Use RamjetCycleSolver for subsonic/low-supersonic flight.");

        var fuel = AirbreathingFuelTables.Lookup(cond.Fuel);
        var atm  = StandardAtmosphere.At(cond.Altitude_m);

        // ── Station 0 / 1 — freestream + inlet face ──────────────────────
        double V_inf = cond.MachNumber * IdealGasAir.SpeedOfSound_m_s(atm.StaticT_K);
        double T_t0  = atm.StaticT_K * IdealGasAir.StagnationTemperatureRatio(cond.MachNumber);
        double P_t0  = atm.StaticP_Pa * IdealGasAir.StagnationPressureRatio(cond.MachNumber);
        double mdot_a = atm.Density_kg_m3 * V_inf * design.InletThroatArea_m2;

        var s0 = new StationState(
            StagnationT_K:  T_t0,
            StagnationP_Pa: P_t0,
            MassFlow_kg_s:  mdot_a,
            MachNumber:     cond.MachNumber);

        // ── Station 2 — oblique-shock inlet exit ─────────────────────────
        // Adiabatic inlet: T_t2 = T_t0. Oblique-shock recovery applied to
        // stagnation pressure. Combustor-inlet Mach from 3-shock ramp model.
        double pi_d = ScramjetInletRecovery.Pi_d(cond.MachNumber);
        double T_t2 = T_t0;
        double P_t2 = P_t0 * pi_d;
        double M_2  = ScramjetInletRecovery.CombustorInletMach(cond.MachNumber);

        var s2 = new StationState(
            StagnationT_K:  T_t2,
            StagnationP_Pa: P_t2,
            MassFlow_kg_s:  mdot_a,
            MachNumber:     M_2);

        // ── Station 3 — isolator exit = combustor inlet ──────────────────
        // Adiabatic isolator: T_t3 = T_t2.
        // Pseudo-shock-train recovery applied to stagnation pressure.
        // M_3 ≈ M_2 (the isolator primarily redistributes pressure via
        // the shock train; Mach decreases slightly but the constant-
        // property model treats M_3 = M_2 for this preliminary-design pass).
        double pi_iso = IsolatorRecovery.Pi_iso(M_2);
        double T_t3   = T_t2;
        double P_t3   = P_t2 * pi_iso;
        double M_3    = M_2;

        var s3 = new StationState(
            StagnationT_K:  T_t3,
            StagnationP_Pa: P_t3,
            MassFlow_kg_s:  mdot_a,
            MachNumber:     M_3);

        // ── Station 4 — combustor exit (Rayleigh heat addition) ──────────
        //
        // Energy balance (same form as ramjet):
        //   T_t4 = (T_t3 + f · η_b · LHV / cp) / (1 + f)
        //
        // Rayleigh-flow combustor: constant-area duct, supersonic entry.
        // Heat addition lowers Mach on the supersonic branch toward unity.
        // Binary search finds M_4 > 1 satisfying the Tt/Tt* ratio.
        //
        // Rayleigh temperature-ratio function:
        //   Tt_ratio(M) = 2(γ+1)M²(1 + (γ−1)/2·M²) / (1 + γM²)²
        //
        // Rayleigh pressure-ratio function:
        //   Pt_ratio(M) = (γ+1)/(1+γM²) · ((2/(γ+1))(1+(γ−1)/2·M²))^(γ/(γ−1))
        //
        // P_t4/P_t3 = Pt_ratio(M_4) / Pt_ratio(M_3)
        double f   = design.EquivalenceRatio * fuel.StoichiometricFuelAirRatio;
        double T_t4 = (T_t3 + f * CombustionEfficiency * fuel.LowerHeatingValue_J_kg
                              / IdealGasAir.Cp_J_kg_K)
                    / (1.0 + f);
        double tau = T_t4 / T_t3;   // heating ratio

        double M_4     = SolveCombustorExitMach(M_3, tau);
        double P_t4    = P_t3 * RayleighPtRatio(M_4) / RayleighPtRatio(M_3);
        double mdot_total = mdot_a * (1.0 + f);

        var s4 = new StationState(
            StagnationT_K:  T_t4,
            StagnationP_Pa: P_t4,
            MassFlow_kg_s:  mdot_total,
            MachNumber:     M_4);

        // ── Station 9 — nozzle exit, perfect expansion (P_9 = P_∞) ──────
        double T_t9 = T_t4;
        double P_t9 = P_t4 * NozzlePressureRecovery;
        double pStagOverPStatic = P_t9 / atm.StaticP_Pa;

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
            T_9   = T_t9 / IdealGasAir.StagnationTemperatureRatio(M_9);
            double a_9 = IdealGasAir.SpeedOfSound_m_s(T_9);
            V_9   = M_9 * a_9;
            F_net = mdot_a * ((1.0 + f) * V_9 - V_inf);
        }

        var s9 = new StationState(
            StagnationT_K:  T_t9,
            StagnationP_Pa: P_t9,
            MassFlow_kg_s:  mdot_total,
            MachNumber:     M_9);

        // ── Performance ──────────────────────────────────────────────────
        double mdot_f = mdot_a * f;
        double Isp = (mdot_f > 0.0 && F_net > 0.0)
            ? F_net / (mdot_f * StandardAtmosphere.G0_m_s2)
            : 0.0;

        // ── Build the 10-element station array ───────────────────────────
        var stations = new StationState[10];
        stations[0] = s0;
        stations[1] = s0;         // inlet face = freestream (lumped 0-D)
        stations[2] = s2;
        stations[3] = s3;
        stations[4] = s4;
        stations[5] = s4;         // no turbine; pre-nozzle = post-combustor
        stations[6] = NaNStation();
        stations[7] = NaNStation();
        stations[8] = s4;         // no convergent throat; carry combustor-exit state
        stations[9] = s9;

        return new CycleSolveResult(
            Stations: new StationMap(
                Stations:          stations,
                ThrustNet_N:       F_net,
                SpecificImpulse_s: Isp,
                FuelMassFlow_kg_s: mdot_f),
            CompressorDiagnostics: null,
            TurbineDiagnostics:    null);
    }

    // ── Rayleigh-flow helpers ─────────────────────────────────────────────

    /// <summary>
    /// Rayleigh T_t / T_t* ratio for ideal gas.
    /// Monotone decreasing on the supersonic branch (M → ∞ → 0) and
    /// monotone increasing on the subsonic branch (M → 0 → 1) with
    /// the maximum at M = 1.
    /// </summary>
    internal static double RayleighTtRatio(double M)
    {
        const double gp1 = IdealGasAir.Gamma + 1.0;   // γ + 1 = 2.4
        const double gm1 = IdealGasAir.Gamma - 1.0;   // γ − 1 = 0.4
        double M2 = M * M;
        double num = 2.0 * gp1 * M2 * (1.0 + 0.5 * gm1 * M2);
        double denom = (1.0 + IdealGasAir.Gamma * M2);
        return num / (denom * denom);
    }

    /// <summary>
    /// Rayleigh P_t / P_t* ratio for ideal gas.
    /// </summary>
    internal static double RayleighPtRatio(double M)
    {
        const double gp1 = IdealGasAir.Gamma + 1.0;
        const double gm1 = IdealGasAir.Gamma - 1.0;
        double M2   = M * M;
        double term1 = gp1 / (1.0 + IdealGasAir.Gamma * M2);
        double term2 = Math.Pow((2.0 / gp1) * (1.0 + 0.5 * gm1 * M2),
                                IdealGasAir.Gamma / gm1);
        return term1 * term2;
    }

    /// <summary>
    /// Solve for the supersonic-branch M_4 satisfying:
    ///   RayleighTtRatio(M_4) = RayleighTtRatio(M_3) × tau
    ///
    /// Uses binary search on [1 + ε, MachSearchCeiling] — the
    /// supersonic branch of Tt/Tt* is monotone decreasing in M, so
    /// a single root exists between M = 1 and M = M_3 when tau ≤ 1,
    /// and there is no supersonic solution when tau > max feasible.
    /// Near-choke (target ratio ≥ 1.0) saturates at 1.001.
    /// </summary>
    private static double SolveCombustorExitMach(double M_3, double tau)
    {
        const double MachSearchCeiling = 20.0;
        const double MachFloor         = 1.0010;
        const int    MaxIterations     = 80;
        const double Tolerance         = 1e-9;

        double target = RayleighTtRatio(M_3) * tau;

        // At M = 1, Tt/Tt* = 1.0 (maximum for the function). If the
        // target ratio is at or above 1.0 the combustor is near thermal
        // choke; saturate M_4 at just above 1 (advisory gate fires).
        if (target >= 1.0) return MachFloor;

        // Supersonic branch is monotone decreasing: high M → low ratio,
        // low M → ratio approaching 1. Search [MachFloor, MachSearchCeiling].
        double lo = MachFloor;
        double hi = MachSearchCeiling;

        // Sanity: if M_3 itself already satisfies the target (τ = 1, no
        // heating) return M_3 directly.
        if (Math.Abs(tau - 1.0) < 1e-12) return M_3;

        for (int i = 0; i < MaxIterations; i++)
        {
            double mid = 0.5 * (lo + hi);
            double val = RayleighTtRatio(mid);
            if (Math.Abs(val - target) < Tolerance) return mid;
            // Supersonic branch: higher M → lower Tt/Tt*
            if (val > target)
                lo = mid;   // need higher M to reduce the ratio
            else
                hi = mid;   // need lower M to increase the ratio
        }

        return 0.5 * (lo + hi);
    }

    private static StationState NaNStation()
        => new(double.NaN, double.NaN, 0.0, double.NaN);
}
