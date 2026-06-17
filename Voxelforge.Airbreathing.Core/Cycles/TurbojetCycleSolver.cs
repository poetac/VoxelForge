// TurbojetCycleSolver.cs — Sprint A7 single-spool turbojet station
// march. Brayton cycle with pluggable compressor + turbine maps; shaft
// balance closes the loop.
//
// Station numbering (SAE AS755):
//   0  freestream
//   1  inlet face
//   2  compressor face / diffuser exit
//   3  compressor exit / combustor inlet
//   4  combustor exit / turbine inlet
//   5  turbine exit
//   6, 7  afterburner inlet / exit (Sprint A7 single-spool dry; NaN)
//   8  nozzle throat
//   9  nozzle exit (perfect expansion, P_9 = P_∞)
//
// Simplifying assumptions
// -----------------------
//   1. Hot-side cp(T) — when Fuel ∈ {JetA, JP-8} the combustor + turbine
//      energy balances integrate cp_burnt_kerosene(T) over the stations
//      4-9 enthalpy span (Mattingly App. B Table B.1). For H2 fuel the
//      kerosene curve does not apply and the constant-cp algebraic form
//      γ·R/(γ−1) ≈ 1004.7 J/(kg·K) is used throughout — this preserves
//      the H2 ramjet textbook fixture exactly.
//   2. Cold-side cp_air(T) — stations 0-3 use the NIST tabulated dry-air
//      curve via the compressor map's internal energy balance. The
//      ConstantEfficiencyCompressorMap stand-in still uses constant cp
//      per its Jones-style preliminary-design contract; the
//      J85ClassCompressorMap uses cp_air(T).
//   3. Compressor face Mach hardcoded at 0.5 — a typical preliminary-
//      design value. Drives ṁ_a = ρ · M_face · a · A_inlet. Operating-
//      point Newton iteration over (N_corr, ṁ_corr) is deferred.
//   4. Shaft balance with η_mech = 1.0 (no bearing losses).
//   5. Perfect expansion at nozzle exit (P_9 = P_∞).
//   6. No bleed air, no inter-stage cooling, no power offtake.
//
// J85 fixture (~13 kN net thrust, π_c=8, T_t4=1175 K, ṁ_a=20 kg/s)
// lands within the ±15 % tolerance band given the simplifications
// above + cp(T) hot-side + table-based maps.

using System;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Single-spool turbojet cycle solver. Parametric stand-in maps;
/// shaft-balanced; perfect-expansion nozzle.
/// </summary>
public sealed class TurbojetCycleSolver : IAirbreathingCycleSolver
{
    /// <summary>Combustor stagnation pressure recovery π_b.</summary>
    public const double CombustorPressureRecovery = 0.96;

    /// <summary>Combustion efficiency η_b.</summary>
    public const double CombustionEfficiency = 0.99;

    /// <summary>Nozzle stagnation pressure recovery π_n.</summary>
    public const double NozzlePressureRecovery = 0.97;

    /// <summary>Afterburner stagnation pressure recovery π_ab.</summary>
    public const double AfterburnerPressureRecovery = 0.95;

    /// <summary>
    /// Maximum afterburner liner temperature [K]. Hard gate
    /// <c>AFTERBURNER_LINER_OVERTEMP</c> fires above this value.
    /// Representative limit for uncooled Inconel 625 afterburner duct
    /// (Mattingly §12, GE J79 design notes).
    /// </summary>
    public const double AfterburnerMaxLinerTemp_K = 2100.0;

    /// <summary>
    /// Compressor face Mach number — hardcoded for the Sprint A7
    /// parametric stand-in. Production preliminary-design value
    /// 0.4-0.6; 0.5 is the standard middle.
    /// </summary>
    public const double CompressorFaceMach = 0.5;

    private readonly ICompressorMap _compressorMap;
    private readonly ITurbineMap _turbineMap;

    /// <summary>
    /// Default-constructed solver — uses the table-based J85-class
    /// off-design maps for compressor + turbine. Production path for
    /// the J85 fixture and any other class-similar single-spool axial
    /// turbojet. Use the (ICompressorMap, ITurbineMap) overload to
    /// inject the constant-η stand-ins for unit tests that need
    /// fully-controlled physics (shaft-balance ṁ·ΔT equality, etc.).
    /// </summary>
    public TurbojetCycleSolver()
        : this(J85ClassCompressorMap.Default, J85ClassTurbineMap.Default) { }

    /// <summary>
    /// Custom-map constructor — for tests + future real-map
    /// implementations.
    /// </summary>
    public TurbojetCycleSolver(ICompressorMap compressorMap, ITurbineMap turbineMap)
    {
        _compressorMap = compressorMap ?? throw new ArgumentNullException(nameof(compressorMap));
        _turbineMap = turbineMap ?? throw new ArgumentNullException(nameof(turbineMap));
    }

    /// <inheritdoc />
    public AirbreathingEngineKind Kind => AirbreathingEngineKind.Turbojet;

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
        if (design.Kind != AirbreathingEngineKind.Turbojet)
            throw new ArgumentException(
                $"TurbojetCycleSolver invoked with design.Kind = {design.Kind}; expected Turbojet.",
                nameof(design));
        if (double.IsNaN(design.CompressorPressureRatio) || design.CompressorPressureRatio < 1.0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"Turbojet CompressorPressureRatio = {design.CompressorPressureRatio:F3} must be >= 1.");

        var fuel = AirbreathingFuelTables.Lookup(cond.Fuel);
        var atm = StandardAtmosphere.At(cond.Altitude_m);

        // Station 0 / 1 — freestream + inlet face. ṁ_a model: face-
        // Mach hardcoded at 0.5 (parametric stand-in).
        double V_inf = cond.MachNumber * IdealGasAir.SpeedOfSound_m_s(atm.StaticT_K);
        double T_t0 = atm.StaticT_K * IdealGasAir.StagnationTemperatureRatio(cond.MachNumber);
        double P_t0 = atm.StaticP_Pa * IdealGasAir.StagnationPressureRatio(cond.MachNumber);

        // Static density at compressor face from stagnation conditions
        // + face Mach.
        double T_face_static = T_t0 / IdealGasAir.StagnationTemperatureRatio(CompressorFaceMach);
        double P_face_static = P_t0 / IdealGasAir.StagnationPressureRatio(CompressorFaceMach);
        double rho_face = P_face_static / (IdealGasAir.R_J_kg_K * T_face_static);
        double V_face = CompressorFaceMach * IdealGasAir.SpeedOfSound_m_s(T_face_static);
        double mdot_a = rho_face * V_face * design.InletThroatArea_m2;

        var s0 = new StationState(T_t0, P_t0, mdot_a, cond.MachNumber);

        // Station 2 — diffuser exit (= compressor face). Adiabatic;
        // π_d from MIL-STD-5007D shock-train recovery curve.
        double pi_d = InletRecovery.Pi_d(cond.MachNumber);
        double T_t2 = T_t0;
        double P_t2 = P_t0 * pi_d;
        var s2 = new StationState(T_t2, P_t2, mdot_a, CompressorFaceMach);

        // Station 3 — compressor exit. Map handles isentropic step +
        // efficiency penalty + outlet pressure.
        var compressor = _compressorMap.Operate(T_t2, P_t2, design.CompressorPressureRatio);
        double T_t3 = compressor.OutletStagnationT_K;
        double P_t3 = compressor.OutletStagnationP_Pa;
        double W_compressor_per_air = compressor.SpecificWork_J_kg;
        var s3 = new StationState(T_t3, P_t3, mdot_a, 0.3);

        // Station 4 — combustor exit. Hot-side cp routing:
        //   Jet-A / JP-8: integrate cp_burnt_kerosene(T) via enthalpy.
        //     h_air(T_t3) + f·η_b·LHV = (1+f)·h_burnt(T_t4)
        //     T_t4 = InvertEnthalpyBurntKerosene((h_air(T_t3) + f·η_b·LHV)/(1+f))
        //   H2: kerosene burnt-gas curve doesn't apply; fall back to
        //     constant-cp algebraic energy balance (preserves H2 ramjet
        //     textbook fixture exactly).
        double f = design.EquivalenceRatio * fuel.StoichiometricFuelAirRatio;
        double T_t4 = SolveCombustorExitT(cond.Fuel, T_t3, f, fuel.LowerHeatingValue_J_kg);
        double P_t4 = P_t3 * CombustorPressureRecovery;
        double mdot_total = mdot_a * (1.0 + f);
        var s4 = new StationState(T_t4, P_t4, mdot_total, 0.3);

        // Station 5 — turbine exit. Shaft balance closes the loop:
        //   ṁ_a · W_compressor_per_air = ṁ_total · W_turbine_per_total
        //   W_turbine_per_total = W_compressor_per_air / (1 + f)
        // The turbine map decides internally whether to use constant-cp
        // (ConstantEfficiency...) or enthalpy-based cp_burnt(T)
        // (J85Class...) for the outlet T computation.
        double W_turbine_per_total = W_compressor_per_air / (1.0 + f);
        var turbine = _turbineMap.Operate(T_t4, P_t4, W_turbine_per_total);
        double T_t5 = turbine.OutletStagnationT_K;
        double P_t5 = turbine.OutletStagnationP_Pa;
        var s5 = new StationState(T_t5, P_t5, mdot_total, 0.4);

        // Stations 6-7 — afterburner (reheat). When disabled: NaN placeholders.
        StationState s6, s7;
        double T_nozzle_inlet, P_nozzle_inlet, mdot_ab_fuel;
        double f_ab = 0.0;

        if (design.EnableAfterburner && design.AfterburnerFuelAirRatio > 0.0)
        {
            f_ab = design.AfterburnerFuelAirRatio;
            // Station 6: afterburner inlet = turbine exit conditions.
            s6 = new StationState(T_t5, P_t5, mdot_total, 0.3);
            // Station 7: afterburner exit. Second combustion starting from T_t5.
            double T_t7 = SolveCombustorExitT(cond.Fuel, T_t5, f_ab, fuel.LowerHeatingValue_J_kg);
            double P_t7 = P_t5 * AfterburnerPressureRecovery;
            double mdot_ab = mdot_total + mdot_a * f_ab;  // add afterburner fuel
            s7 = new StationState(T_t7, P_t7, mdot_ab, 0.3);
            T_nozzle_inlet = T_t7;
            P_nozzle_inlet = P_t7;
            mdot_ab_fuel   = mdot_a * f_ab;
        }
        else
        {
            s6 = NaNStation();
            s7 = NaNStation();
            T_nozzle_inlet = T_t5;
            P_nozzle_inlet = P_t5;
            mdot_ab_fuel   = 0.0;
        }

        double mdot_nozzle = mdot_total + mdot_ab_fuel;

        // Station 8-9 — nozzle throat + exit, perfect expansion.
        double T_t9 = T_nozzle_inlet;
        double P_t9 = P_nozzle_inlet * NozzlePressureRecovery;
        double pStagOverPStatic = P_t9 / atm.StaticP_Pa;

        double M_9, T_9, V_9, F_net;
        if (pStagOverPStatic >= 1.0)
        {
            M_9 = IdealGasAir.MachFromStagnationPressureRatio(pStagOverPStatic);
            T_9 = T_t9 / IdealGasAir.StagnationTemperatureRatio(M_9);
            double a_9 = IdealGasAir.SpeedOfSound_m_s(T_9);
            V_9 = M_9 * a_9;
            F_net = mdot_nozzle * V_9 - mdot_a * V_inf;
        }
        else
        {
            M_9 = double.NaN;
            T_9 = double.NaN;
            V_9 = 0.0;
            F_net = 0.0;
        }

        var s9 = new StationState(T_t9, P_t9, mdot_nozzle, M_9);

        // Performance
        double mdot_f = mdot_a * f + mdot_ab_fuel;
        double Isp = (mdot_f > 0.0 && F_net > 0.0)
            ? F_net / (mdot_f * StandardAtmosphere.G0_m_s2)
            : 0.0;

        var stations = new StationState[10];
        stations[0] = s0;
        stations[1] = s0;            // inlet face = freestream (lumped 0-D)
        stations[2] = s2;
        stations[3] = s3;
        stations[4] = s4;
        stations[5] = s5;
        stations[6] = s6;            // afterburner inlet (NaN when dry)
        stations[7] = s7;            // afterburner exit  (NaN when dry)
        stations[8] = new StationState(T_t9, P_t9, mdot_nozzle, 1.0);
        stations[9] = s9;

        var stationMap = new StationMap(
            Stations:           stations,
            ThrustNet_N:        F_net,
            SpecificImpulse_s:  Isp,
            FuelMassFlow_kg_s:  mdot_f);

        return new CycleSolveResult(
            Stations:                stationMap,
            CompressorDiagnostics:   compressor.Diagnostics,
            TurbineDiagnostics:      turbine.Diagnostics);
    }

    private static StationState NaNStation()
        => new(double.NaN, double.NaN, 0.0, double.NaN);

    /// <summary>
    /// Combustor energy balance with optional hot-side cp(T). For
    /// kerosene fuels (Jet-A, JP-8) integrates cp_burnt_kerosene(T)
    /// via enthalpy; for H2 falls back to the constant-cp algebraic
    /// form. Public-static so RamjetCycleSolver can reuse it.
    /// </summary>
    /// <param name="fuel">Fuel-stream identity (drives cp curve choice).</param>
    /// <param name="T_t3_K">Compressor exit / combustor inlet stagnation T [K]. For ramjet pass T_t2.</param>
    /// <param name="f">Fuel-air mass ratio = φ · f_st [dimensionless].</param>
    /// <param name="lhv_J_kg">Fuel lower heating value [J/kg].</param>
    /// <returns>Combustor exit stagnation T_t4 [K].</returns>
    public static double SolveCombustorExitT(
        AirbreathingFuel fuel,
        double T_t3_K,
        double f,
        double lhv_J_kg)
    {
        double q_per_air = f * CombustionEfficiency * lhv_J_kg;

        if (fuel == AirbreathingFuel.JetA || fuel == AirbreathingFuel.Jp8)
        {
            // h_air(T_t3) + q_per_air = (1+f) · h_burnt(T_t4)
            double h_air_t3 = IdealGasAir.EnthalpyAir(T_t3_K);
            double h_burnt_t4 = (h_air_t3 + q_per_air) / (1.0 + f);
            // Reference frame: h_air and h_burnt both reference 200 K.
            // h_air(200) = 0 = h_burnt(200), so the inversion is
            // self-consistent — burnt-gas at 200 K starts from same
            // datum as air at 200 K, only the slope cp(T) differs.
            return IdealGasAir.InvertEnthalpyBurntKerosene(h_burnt_t4);
        }

        // H2 (or any other future fuel without a kerosene-class burnt-gas
        // curve): preserve constant-cp form. This keeps the Mattingly
        // synthetic ramjet H2 fixture's hand-derivation exact.
        return (T_t3_K + q_per_air / IdealGasAir.Cp_J_kg_K) / (1.0 + f);
    }
}
