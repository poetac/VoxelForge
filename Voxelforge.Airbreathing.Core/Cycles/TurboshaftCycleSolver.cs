// TurboshaftCycleSolver.cs — Wave-2 turboshaft cycle solver (issue #428).
//
// A turboshaft is a gas turbine where essentially all available exhaust
// enthalpy is extracted by a free power turbine and delivered to a shaft
// output (helicopter rotor, ship propulsion shaft, generator). The exhaust
// exits via a non-thrust-producing duct — net propulsive thrust ≈ 0.
//
// Station numbering (SAE AS755, same as turboprop):
//   0   freestream
//   1   inlet face (= 0, lumped 0-D)
//   2   compressor face / diffuser exit
//   3   compressor exit / combustor inlet
//   4   combustor exit / gas-generator turbine inlet
//   5   gas-generator turbine exit / free power turbine inlet
//   6   free power turbine exit  (P_t6 ≈ P_amb; full expansion)
//   7   NaN (no afterburner)
//   8   NaN (no propulsive nozzle)
//   9   NaN (no propulsive nozzle)
//
// Difference from turboprop
// -------------------------
// The turboshaft solver is structurally identical to the turboprop solver
// (stations 0-5 use the same gas-generator Brayton cycle; station 6 uses
// the same free power turbine model) with two differences:
//
//   1. fpe = 1.0 is hardcoded — the turboshaft extracts all isentropic
//      enthalpy from P_t5 → P_amb. The design's
//      AirbreathingEngineDesign.PropellerPowerExtraction_frac field is
//      ignored (a turboshaft always maximises shaft output).
//
//   2. Propulsive nozzle thrust is suppressed — ThrustNet_N = 0.0.
//      The exhaust exits sideways or through a separate exhaust stack that
//      doesn't produce aerodynamic thrust. Station 6 pressure ≈ P_amb.
//
// Performance output:
//   ShaftPower_W = ṁ_gas · cp · ΔT_pt           [W]  → primary product
//   ThrustNet_N  = 0.0                           [N]  → not propulsive
//   Isp          = 0.0                           [s]  → undefined (no thrust)
//   ThermalEfficiency = W_shaft / Q_fuel               [—]
//
// Simplifying assumptions (same as TurbopropCycleSolver)
// ------------------------------------------------------
//   1. Constant cp_air, γ = 1.40 throughout.
//   2. J85-class compressor + gas-generator turbine stand-in maps.
//   3. Free power turbine efficiency η_pt = 0.88 (same as turboprop).
//   4. η_mech = 1.0 (no gearbox losses — conservative toward over-
//      prediction of delivered shaft power by ~2%).
//   5. No bleed air, no inter-stage cooling.
//
// T700-GE-701C validation band (fixture TurboshaftFixture_T700GE701C.cs):
//   Shaft power ≥ 1000 kW and ≤ 1850 kW (−29 % / +31 % of 1409 kW MCP).
//   Tolerance is wider than turboprop (±30 %) because the T700 is a
//   two-spool free-turbine engine whose compressor characteristics diverge
//   more from the J85-class maps than a single-spool design.

using System;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Turboshaft cycle solver. Gas-generator Brayton cycle (stations 0-5)
/// plus a free power turbine (station 6) extracting 100 % of the
/// available isentropic enthalpy; no propulsive nozzle thrust.
/// </summary>
public sealed class TurboshaftCycleSolver : IAirbreathingCycleSolver
{
    /// <summary>Combustor stagnation pressure recovery π_b.</summary>
    public const double CombustorPressureRecovery = 0.96;

    /// <summary>Combustion efficiency η_b.</summary>
    public const double CombustionEfficiency = 0.99;

    /// <summary>Compressor face Mach number stand-in (matches turbojet).</summary>
    public const double CompressorFaceMach = 0.5;

    /// <summary>
    /// Free power turbine isentropic efficiency η_pt.
    /// Same value as <see cref="TurbopropCycleSolver.PowerTurbineEfficiency"/>.
    /// </summary>
    public const double PowerTurbineEfficiency = 0.88;

    private readonly ICompressorMap _compressorMap;
    private readonly ITurbineMap _gasTurbineMap;

    /// <summary>
    /// Default-constructed solver — uses the J85-class off-design maps.
    /// </summary>
    public TurboshaftCycleSolver()
        : this(J85ClassCompressorMap.Default, J85ClassTurbineMap.Default) { }

    /// <summary>Custom-map constructor — for unit tests.</summary>
    public TurboshaftCycleSolver(ICompressorMap compressorMap, ITurbineMap gasTurbineMap)
    {
        _compressorMap = compressorMap ?? throw new ArgumentNullException(nameof(compressorMap));
        _gasTurbineMap = gasTurbineMap ?? throw new ArgumentNullException(nameof(gasTurbineMap));
    }

    /// <inheritdoc />
    public AirbreathingEngineKind Kind => AirbreathingEngineKind.Turboshaft;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when
    /// <see cref="AirbreathingEngineDesign.CompressorPressureRatio"/>
    /// is NaN or &lt; 1.
    /// </exception>
    public CycleSolveResult Solve(AirbreathingEngineDesign design, FlightConditions cond)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (cond is null) throw new ArgumentNullException(nameof(cond));
        if (design.Kind != AirbreathingEngineKind.Turboshaft)
            throw new ArgumentException(
                $"TurboshaftCycleSolver invoked with design.Kind = {design.Kind}; expected Turboshaft.",
                nameof(design));
        if (double.IsNaN(design.CompressorPressureRatio) || design.CompressorPressureRatio < 1.0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"Turboshaft CompressorPressureRatio = {design.CompressorPressureRatio:F3} must be >= 1.");

        var fuel = AirbreathingFuelTables.Lookup(cond.Fuel);
        var atm  = StandardAtmosphere.At(cond.Altitude_m);

        // ── Stations 0-5: gas-generator Brayton cycle ─────────────────────

        double T_t0 = atm.StaticT_K * IdealGasAir.StagnationTemperatureRatio(cond.MachNumber);
        double P_t0 = atm.StaticP_Pa * IdealGasAir.StagnationPressureRatio(cond.MachNumber);

        double T_face_static = T_t0 / IdealGasAir.StagnationTemperatureRatio(CompressorFaceMach);
        double P_face_static = P_t0 / IdealGasAir.StagnationPressureRatio(CompressorFaceMach);
        double rho_face = P_face_static / (IdealGasAir.R_J_kg_K * T_face_static);
        double V_face   = CompressorFaceMach * IdealGasAir.SpeedOfSound_m_s(T_face_static);
        double mdot_a   = rho_face * V_face * design.InletThroatArea_m2;

        var s0 = new StationState(T_t0, P_t0, mdot_a, cond.MachNumber);

        double pi_d = InletRecovery.Pi_d(cond.MachNumber);
        double T_t2 = T_t0;
        double P_t2 = P_t0 * pi_d;
        var s2 = new StationState(T_t2, P_t2, mdot_a, CompressorFaceMach);

        var compressor = _compressorMap.Operate(T_t2, P_t2, design.CompressorPressureRatio);
        double T_t3 = compressor.OutletStagnationT_K;
        double P_t3 = compressor.OutletStagnationP_Pa;
        double W_comp_per_air = compressor.SpecificWork_J_kg;
        var s3 = new StationState(T_t3, P_t3, mdot_a, 0.3);

        double f    = design.EquivalenceRatio * fuel.StoichiometricFuelAirRatio;
        double T_t4 = TurbojetCycleSolver.SolveCombustorExitT(cond.Fuel, T_t3, f, fuel.LowerHeatingValue_J_kg);
        double P_t4 = P_t3 * CombustorPressureRecovery;
        double mdot_gas = mdot_a * (1.0 + f);
        var s4 = new StationState(T_t4, P_t4, mdot_gas, 0.3);

        double W_turbine_per_gas = W_comp_per_air / (1.0 + f);
        var turbine = _gasTurbineMap.Operate(T_t4, P_t4, W_turbine_per_gas);
        double T_t5 = turbine.OutletStagnationT_K;
        double P_t5 = turbine.OutletStagnationP_Pa;
        var s5 = new StationState(T_t5, P_t5, mdot_gas, 0.4);

        // ── Station 6: free power turbine — full expansion (fpe = 1.0) ───

        double T_t5_full_expansion = T_t5 * Math.Pow(atm.StaticP_Pa / P_t5,
            (IdealGasAir.Gamma - 1.0) / IdealGasAir.Gamma);
        double dT_avail = T_t5 - T_t5_full_expansion;

        // fpe = 1.0 hardcoded (turboshaft extracts all available enthalpy).
        double dT_pt = PowerTurbineEfficiency * dT_avail;
        double T_t6  = T_t5 - dT_pt;

        // P_t6 ≈ P_amb after full expansion (isentropic).
        double T_t6_s = T_t5 - dT_pt / PowerTurbineEfficiency;
        double P_t6   = P_t5 * Math.Pow(T_t6_s / T_t5,
            IdealGasAir.Gamma / (IdealGasAir.Gamma - 1.0));
        P_t6 = Math.Max(P_t6, atm.StaticP_Pa);

        double W_shaft = mdot_gas * IdealGasAir.Cp_J_kg_K * dT_pt;
        var s6 = new StationState(T_t6, P_t6, mdot_gas, 0.1);

        // ── Performance: shaft power only; no propulsive thrust ─────────

        double mdot_f = mdot_a * f;
        double Q_fuel = mdot_f * fuel.LowerHeatingValue_J_kg;
        double eta_th = Q_fuel > 0.0 ? W_shaft / Q_fuel : 0.0;

        var stations = new StationState[10];
        stations[0] = s0;
        stations[1] = s0;
        stations[2] = s2;
        stations[3] = s3;
        stations[4] = s4;
        stations[5] = s5;
        stations[6] = s6;
        stations[7] = NaNStation();
        stations[8] = NaNStation();   // no propulsive nozzle
        stations[9] = NaNStation();

        var stationMap = new StationMap(
            Stations:          stations,
            ThrustNet_N:       0.0,    // turboshaft: no propulsive thrust by design
            SpecificImpulse_s: 0.0,    // undefined (no thrust)
            FuelMassFlow_kg_s: mdot_f);

        return new CycleSolveResult(
            Stations:              stationMap,
            CompressorDiagnostics: compressor.Diagnostics,
            TurbineDiagnostics:    turbine.Diagnostics)
        {
            ShaftPower_W      = W_shaft,
            ThermalEfficiency = eta_th,
        };
    }

    private static StationState NaNStation()
        => new(double.NaN, double.NaN, 0.0, double.NaN);
}
