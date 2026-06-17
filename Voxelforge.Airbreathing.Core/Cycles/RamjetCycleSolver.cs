// RamjetCycleSolver.cs — Sprint A4 ramjet station march.
//
// Constant-property (γ = 1.40, cp = 1004.7 J/(kg·K)) ideal-cycle
// analysis per Mattingly §5.3 + Hill & Peterson §5.3. No moving parts
// → freestream → inlet diffuser → subsonic combustor → CD nozzle
// (perfect expansion at design point).
//
// Station numbering (SAE AS755):
//   0  freestream
//   1  inlet face (engine intake plane) — same state as 0 in the
//      lumped 0-D model since ram compression hasn't happened yet
//   2  diffuser exit (inlet recovery applied)
//   4  combustor exit (turbojet skips 3 → 4 here too; ramjet skips
//      compressor entirely so the convention is to keep "4" for
//      combustor exit + 5 = pre-nozzle = same as 4 for a ramjet)
//   8  nozzle throat
//   9  nozzle exit (perfect expansion → P_9 = P_∞)
//
// Stations 3, 5, 6, 7 are degenerate for a ramjet and reported with
// NaN station state + zero mass flow per the StationMap convention.
//
// Simplifying assumptions
// -----------------------
//   1. Hot-side cp(T) — when Fuel ∈ {JetA, JP-8} the combustor energy
//      balance integrates cp_burnt_kerosene(T) over the stations 4-9
//      enthalpy span (Mattingly App. B Table B.1). For H2 fuel the
//      kerosene curve does not apply and the constant-cp algebraic
//      form γ·R/(γ−1) ≈ 1004.7 J/(kg·K) is used — this preserves the
//      MattinglySyntheticRamjet H2 fixture's hand-derivation exactly.
//   2. Perfect expansion at the nozzle exit: P_9 = P_∞.
//   3. Combustor stagnation pressure recovery π_b is hard-coded; no
//      Rayleigh-flow correction for the heat-addition pressure drop.
//      Mattingly §5.3 takes the same shortcut.
//   4. ṁ_a captured at the inlet face = ρ_∞ · V_∞ · A_inlet. Real
//      inlet capture-area scheduling (variable geometry, mass-flow
//      ratio for sub-cruise) is out of scope.
//   5. No fuel pre-heat / fuel-side enthalpy contribution to combustor
//      energy balance. Fuel is assumed at standard reference T.

using System;
using System.Collections.Generic;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Ramjet cycle solver. Constant-property ideal cycle.
/// </summary>
public sealed class RamjetCycleSolver : IAirbreathingCycleSolver
{
    /// <summary>
    /// Combustor stagnation pressure recovery factor π_b = P_t4 / P_t2.
    /// Production ramjet combustors cluster around 0.95-0.98; 0.98 is
    /// the standard preliminary-design value.
    /// </summary>
    public const double CombustorPressureRecovery = 0.98;

    /// <summary>
    /// Combustion efficiency η_b. Fraction of fuel LHV that surfaces
    /// as enthalpy rise in the combustor energy balance.
    /// </summary>
    public const double CombustionEfficiency = 0.99;

    /// <summary>
    /// Nozzle stagnation pressure recovery factor π_n = P_t9 / P_t4.
    /// </summary>
    public const double NozzlePressureRecovery = 0.96;

    /// <inheritdoc />
    public AirbreathingEngineKind Kind => AirbreathingEngineKind.Ramjet;

    /// <inheritdoc />
    public CycleSolveResult Solve(AirbreathingEngineDesign design, FlightConditions cond)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (cond is null) throw new ArgumentNullException(nameof(cond));
        if (design.Kind != AirbreathingEngineKind.Ramjet)
            throw new ArgumentException(
                $"RamjetCycleSolver invoked with design.Kind = {design.Kind}; expected Ramjet.",
                nameof(design));

        var fuel = AirbreathingFuelTables.Lookup(cond.Fuel);
        var atm = StandardAtmosphere.At(cond.Altitude_m);

        // Station 0 / 1 — freestream + inlet face. Static state from
        // atmosphere. Stagnation state from M_∞.
        double V_inf = cond.MachNumber * IdealGasAir.SpeedOfSound_m_s(atm.StaticT_K);
        double T_t0 = atm.StaticT_K * IdealGasAir.StagnationTemperatureRatio(cond.MachNumber);
        double P_t0 = atm.StaticP_Pa * IdealGasAir.StagnationPressureRatio(cond.MachNumber);
        double mdot_a = atm.Density_kg_m3 * V_inf * design.InletThroatArea_m2;

        var s0 = new StationState(
            StagnationT_K:    T_t0,
            StagnationP_Pa:   P_t0,
            MassFlow_kg_s:    mdot_a,
            MachNumber:       cond.MachNumber);

        // Station 2 — diffuser exit. Adiabatic (T_t2 = T_t0); π_d
        // applied to stagnation pressure. Diffuser-exit Mach is
        // designed-low (~0.2) per Mattingly so the combustor sees
        // near-stagnation flow; we report 0.2 as a representative
        // diagnostic value.
        double pi_d = InletRecovery.Pi_d(cond.MachNumber);
        double T_t2 = T_t0;
        double P_t2 = P_t0 * pi_d;
        var s2 = new StationState(
            StagnationT_K:    T_t2,
            StagnationP_Pa:   P_t2,
            MassFlow_kg_s:    mdot_a,
            MachNumber:       0.2);

        // Station 4 — combustor exit. Hot-side cp routing same as
        // turbojet (factored helper). Kerosene fuels integrate
        // cp_burnt_kerosene(T) via enthalpy; H2 falls back to constant
        // cp. T_t3 ≡ T_t2 for a ramjet (no compressor).
        double f = design.EquivalenceRatio * fuel.StoichiometricFuelAirRatio;
        double T_t4 = TurbojetCycleSolver.SolveCombustorExitT(
            cond.Fuel, T_t2, f, fuel.LowerHeatingValue_J_kg);
        double P_t4 = P_t2 * CombustorPressureRecovery;
        double mdot_total = mdot_a * (1.0 + f);
        var s4 = new StationState(
            StagnationT_K:    T_t4,
            StagnationP_Pa:   P_t4,
            MassFlow_kg_s:    mdot_total,
            MachNumber:       0.2);

        // Station 9 — nozzle exit, perfect expansion (P_9 = P_∞).
        //   π_n applied to stagnation pressure
        //   T_t9 = T_t4 (adiabatic CD nozzle)
        //   M_9 inverted from P_t9 / P_∞
        double T_t9 = T_t4;
        double P_t9 = P_t4 * NozzlePressureRecovery;
        double pStagOverPStatic = P_t9 / atm.StaticP_Pa;

        // Guard: if combustor ate too much pressure (bad inlet recovery
        // + bad ducting), nozzle could produce P_t9 < P_∞. That's
        // physically meaningful (engine cannot accelerate flow against
        // atmosphere) but the math fails in MachFromStagnationPressureRatio.
        // Surface as M_9 = NaN; Sprint A5 will turn this into a
        // NOZZLE_INSUFFICIENT_DRIVE_PRESSURE feasibility gate.
        double M_9 = pStagOverPStatic >= 1.0
            ? IdealGasAir.MachFromStagnationPressureRatio(pStagOverPStatic)
            : double.NaN;

        double T_9, V_9, F_net;
        if (double.IsNaN(M_9))
        {
            T_9  = double.NaN;
            V_9  = 0.0;
            F_net = 0.0;
        }
        else
        {
            T_9 = T_t9 / IdealGasAir.StagnationTemperatureRatio(M_9);
            double a_9 = IdealGasAir.SpeedOfSound_m_s(T_9);
            V_9 = M_9 * a_9;
            // Perfect expansion: F = ṁ_total · V_9 − ṁ_a · V_∞
            //                    = ṁ_a · ((1+f)·V_9 − V_∞)
            F_net = mdot_a * ((1.0 + f) * V_9 - V_inf);
        }

        var s9 = new StationState(
            StagnationT_K:    T_t9,
            StagnationP_Pa:   P_t9,
            MassFlow_kg_s:    mdot_total,
            MachNumber:       M_9);

        // Performance
        double mdot_f = mdot_a * f;
        double Isp = (mdot_f > 0.0 && F_net > 0.0)
            ? F_net / (mdot_f * StandardAtmosphere.G0_m_s2)
            : 0.0;

        // Build the 10-element station array. Stations 1, 3, 5-8
        // unused for this 0-D ramjet model — populated with
        // forward-propagated state where it makes physical sense
        // (station 1 = same as station 0, station 8 = same as
        // station 9 throat-state) and NaN/zero where it doesn't.
        var stations = new StationState[10];
        stations[0] = s0;
        stations[1] = s0;                           // inlet face = freestream (lumped 0-D)
        stations[2] = s2;
        stations[3] = NaNStation();                 // ramjet has no compressor
        stations[4] = s4;
        stations[5] = s4;                           // ramjet pre-nozzle = post-combustor
        stations[6] = NaNStation();                 // no afterburner
        stations[7] = NaNStation();                 // no afterburner
        stations[8] = new StationState(             // throat: choked at design
            StagnationT_K:    T_t9,
            StagnationP_Pa:   P_t9,
            MassFlow_kg_s:    mdot_total,
            MachNumber:       1.0);
        stations[9] = s9;

        var stationMap = new StationMap(
            Stations:           stations,
            ThrustNet_N:        F_net,
            SpecificImpulse_s:  Isp,
            FuelMassFlow_kg_s:  mdot_f);

        // Ramjet has no rotating turbomachinery — both diagnostics null.
        return new CycleSolveResult(
            Stations:                stationMap,
            CompressorDiagnostics:   null,
            TurbineDiagnostics:      null);
    }

    private static StationState NaNStation()
        => new(double.NaN, double.NaN, 0.0, double.NaN);
}
