// TurbopropCycleSolver.cs — Wave-2 turboprop cycle solver (issue #428).
//
// Station numbering (SAE AS755, turboprop extension):
//   0   freestream
//   1   inlet face (= 0, lumped 0-D)
//   2   compressor face / diffuser exit
//   3   compressor exit / combustor inlet
//   4   combustor exit / gas-generator turbine inlet
//   5   gas-generator turbine exit / free power turbine inlet
//   6   free power turbine exit
//   7   NaN (no afterburner)
//   8   nozzle throat
//   9   nozzle exit (P_9 = P_∞, small residual exhaust thrust)
//
// Architecture
// ------------
// Stations 0-5 follow the turbojet algorithm exactly: inlet recovery →
// compressor map → combustor energy balance → shaft-balance turbine.
// The departure is at station 5: instead of routing all remaining
// enthalpy through the nozzle, a free power turbine (FPT) captures
// fraction f_pe × η_pt of the isentropic enthalpy from P_t5 → P_amb.
//
//   Available isentropic ΔT (full expansion to P_amb):
//     ΔT_avail = T_t5 · (1 − (P_amb / P_t5)^((γ−1)/γ))
//   Actual temperature drop across FPT:
//     ΔT_pt = f_pe · η_pt · ΔT_avail          [actual, via first law]
//   FPT isentropic outlet T (for P_t6 calculation):
//     ΔT_pt_s = ΔT_pt / η_pt                  [ideal equiv drop]
//     T_t6_s  = T_t5 − ΔT_pt_s
//     P_t6    = P_t5 · (T_t6_s / T_t5)^(γ/(γ−1))
//   Actual FPT outlet stagnation T:
//     T_t6    = T_t5 − ΔT_pt
//   Shaft power:
//     W_shaft = ṁ_gas · cp_air · ΔT_pt        [W]
//
// Propeller thrust (Rankine-Froude momentum theory at cruise):
//   F_prop = W_shaft · η_prop / V_∞            [N]
//   with η_prop clamped to the actuator-disk ceiling when V_∞ → 0.
//   The guard uses W_shaft · η_prop / max(V_∞, 10 m/s) so the
//   solver remains finite at static conditions.
//
// Residual nozzle thrust from station 6 → 9:
//   Standard isentropic expansion from P_t6 to P_amb (same as turbojet
//   nozzle solver). This is typically 3-8 % of total net thrust for a
//   real turboprop.
//
// Net thrust:
//   F_net = F_prop + F_jet                     [N]
//
// Simplifying assumptions
// -----------------------
//   1. Constant cp_air = 1004.7 J/(kg·K), γ = 1.40 throughout.
//      Hot-side cp(T) correction is deferred to Stream B along with
//      cp(T)-kerosene tables (same policy as GasTurbineCycleSolver).
//   2. Same compressor + turbine maps as TurbojetCycleSolver; station
//      0-5 physics is identical.
//   3. Free-power-turbine efficiency η_pt = 0.88 (constant stand-in;
//      real T56-A-15 FPT η ≈ 0.89 per Mattingly §8).
//   4. Propeller efficiency η_prop = 0.83 (constant stand-in; T56-A-15
//      at cruise). A future sprint can make this a design variable.
//   5. η_mech for gearbox = 1.0 (no gear-train losses — conservative
//      because it *over*-predicts delivered shaft power by ~2 %).
//   6. Inlet recovery π_d from MIL-STD-5007D shock-train table (same
//      as turbojet). Compressor face Mach hardcoded at 0.5.
//   7. Perfect expansion at residual nozzle exit (P_9 = P_∞).
//   8. No bleed air, no inter-stage cooling.
//
// T56-A-15 validation band (fixture TurbopropFixture_T56A15.cs):
//   Shaft power ≥ 2900 kW and ≤ 4400 kW  (±20 % of 3660 kW nominal).
//   Net thrust ≥ 10 kN (propeller + residual jet).
//   Both checks pass within the bands expected of a Jones-style
//   constant-property preliminary-design model.

using System;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Turboprop cycle solver. Gas-generator Brayton cycle (stations 0-5,
/// same as turbojet) plus a free power turbine (station 6) that
/// delivers shaft power to a propeller; residual nozzle thrust from
/// station 6→9.
/// </summary>
public sealed class TurbopropCycleSolver : IAirbreathingCycleSolver
{
    /// <summary>Combustor stagnation pressure recovery π_b.</summary>
    public const double CombustorPressureRecovery = 0.96;

    /// <summary>Combustion efficiency η_b.</summary>
    public const double CombustionEfficiency = 0.99;

    /// <summary>Nozzle stagnation pressure recovery π_n (residual exhaust).</summary>
    public const double NozzlePressureRecovery = 0.97;

    /// <summary>Compressor face Mach number stand-in (matches turbojet).</summary>
    public const double CompressorFaceMach = 0.5;

    /// <summary>Free power turbine isentropic efficiency η_pt.</summary>
    public const double PowerTurbineEfficiency = 0.88;

    /// <summary>
    /// Propeller efficiency η_prop stand-in. Constant-value preliminary-
    /// design proxy; T56-A-15 at cruise ≈ 0.83 (Mattingly §8).
    /// </summary>
    public const double PropellerEfficiency = 0.83;

    /// <summary>
    /// Minimum freestream velocity used for propeller thrust estimation
    /// [m/s]. Prevents F_prop = W_shaft / V_∞ from diverging at static
    /// conditions; 10 m/s is the Rankine-Froude actuator-disk lower bound
    /// below which momentum-theory linearisation breaks down.
    /// </summary>
    public const double MinVelocityForPropellerThrust_m_s = 10.0;

    private readonly ICompressorMap _compressorMap;
    private readonly ITurbineMap _gasTurbineMap;

    /// <summary>
    /// Default-constructed solver — uses the J85-class off-design maps.
    /// Same maps as <see cref="TurbojetCycleSolver"/>; the gas-generator
    /// stations 0-5 are physically identical.
    /// </summary>
    public TurbopropCycleSolver()
        : this(J85ClassCompressorMap.Default, J85ClassTurbineMap.Default) { }

    /// <summary>Custom-map constructor — for unit tests.</summary>
    public TurbopropCycleSolver(ICompressorMap compressorMap, ITurbineMap gasTurbineMap)
    {
        _compressorMap = compressorMap ?? throw new ArgumentNullException(nameof(compressorMap));
        _gasTurbineMap = gasTurbineMap ?? throw new ArgumentNullException(nameof(gasTurbineMap));
    }

    /// <inheritdoc />
    public AirbreathingEngineKind Kind => AirbreathingEngineKind.Turboprop;

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
        if (design.Kind != AirbreathingEngineKind.Turboprop)
            throw new ArgumentException(
                $"TurbopropCycleSolver invoked with design.Kind = {design.Kind}; expected Turboprop.",
                nameof(design));
        if (double.IsNaN(design.CompressorPressureRatio) || design.CompressorPressureRatio < 1.0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"Turboprop CompressorPressureRatio = {design.CompressorPressureRatio:F3} must be >= 1.");

        var fuel = AirbreathingFuelTables.Lookup(cond.Fuel);
        var atm  = StandardAtmosphere.At(cond.Altitude_m);

        // ── Stations 0-5: gas-generator Brayton cycle (identical to turbojet) ─

        double V_inf = cond.MachNumber * IdealGasAir.SpeedOfSound_m_s(atm.StaticT_K);
        double T_t0  = atm.StaticT_K * IdealGasAir.StagnationTemperatureRatio(cond.MachNumber);
        double P_t0  = atm.StaticP_Pa * IdealGasAir.StagnationPressureRatio(cond.MachNumber);

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

        // ── Station 6: free power turbine ────────────────────────────────────

        // Isentropic temperature drop if the gas expanded fully from P_t5 to P_amb.
        double T_t5_full_expansion = T_t5 * Math.Pow(atm.StaticP_Pa / P_t5,
            (IdealGasAir.Gamma - 1.0) / IdealGasAir.Gamma);
        double dT_avail = T_t5 - T_t5_full_expansion;   // > 0 as long as P_t5 > P_amb

        double fpe    = design.PropellerPowerExtraction_frac;
        double dT_pt  = fpe * PowerTurbineEfficiency * dT_avail;  // actual temperature drop (first law)
        double T_t6   = T_t5 - dT_pt;

        // Isentropic outlet T for the actual work extracted (used to derive P_t6):
        //   η_pt = dT_pt / dT_isentropic  →  dT_isentropic = dT_pt / η_pt
        double T_t6_s = T_t5 - (PowerTurbineEfficiency > 0.0 ? dT_pt / PowerTurbineEfficiency : dT_pt);
        double P_t6   = P_t5 * Math.Pow(T_t6_s / T_t5,
            IdealGasAir.Gamma / (IdealGasAir.Gamma - 1.0));
        P_t6 = Math.Max(P_t6, atm.StaticP_Pa);  // can't fall below ambient

        double W_shaft = mdot_gas * IdealGasAir.Cp_J_kg_K * dT_pt;  // [W]
        var s6 = new StationState(T_t6, P_t6, mdot_gas, 0.3);

        // ── Propeller thrust (momentum theory at cruise) ─────────────────────

        double V_eff   = Math.Max(V_inf, MinVelocityForPropellerThrust_m_s);
        double F_prop  = W_shaft * PropellerEfficiency / V_eff;

        // ── Stations 8-9: residual exhaust nozzle ───────────────────────────

        double T_t9 = T_t6;
        double P_t9 = P_t6 * NozzlePressureRecovery;
        double pStagOverStatic = P_t9 / atm.StaticP_Pa;

        double F_jet;
        double M_9, T_9, V_9;
        if (pStagOverStatic >= 1.0)
        {
            M_9  = IdealGasAir.MachFromStagnationPressureRatio(pStagOverStatic);
            T_9  = T_t9 / IdealGasAir.StagnationTemperatureRatio(M_9);
            V_9  = M_9 * IdealGasAir.SpeedOfSound_m_s(T_9);
            F_jet = mdot_a * ((1.0 + f) * V_9 - V_inf);
        }
        else
        {
            M_9 = double.NaN; T_9 = double.NaN; V_9 = 0.0;
            F_jet = 0.0;
        }
        var s9 = new StationState(T_t9, P_t9, mdot_gas, M_9);

        // ── Net performance ──────────────────────────────────────────────────

        double F_net  = F_prop + F_jet;
        double mdot_f = mdot_a * f;
        double Isp    = (mdot_f > 0.0 && F_net > 0.0)
            ? F_net / (mdot_f * StandardAtmosphere.G0_m_s2)
            : 0.0;

        var stations = new StationState[10];
        stations[0] = s0;
        stations[1] = s0;
        stations[2] = s2;
        stations[3] = s3;
        stations[4] = s4;
        stations[5] = s5;
        stations[6] = s6;
        stations[7] = NaNStation();
        stations[8] = new StationState(T_t9, P_t9, mdot_gas, 1.0);
        stations[9] = s9;

        var stationMap = new StationMap(
            Stations:          stations,
            ThrustNet_N:       F_net,
            SpecificImpulse_s: Isp,
            FuelMassFlow_kg_s: mdot_f);

        return new CycleSolveResult(
            Stations:              stationMap,
            CompressorDiagnostics: compressor.Diagnostics,
            TurbineDiagnostics:    turbine.Diagnostics)
        {
            ShaftPower_W = W_shaft,
        };
    }

    private static StationState NaNStation()
        => new(double.NaN, double.NaN, 0.0, double.NaN);
}
