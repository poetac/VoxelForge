// TurbofanCycleSolver.cs — Sprint A8 single-spool low-bypass mixed-
// flow turbofan station march. Constant-property Brayton cycle with
// fan + HPC on the same shaft, single turbine driving both, and a
// constant-area mixer absorbed into a single recovery factor.
//
// Station numbering (SAE AS755, with the turbofan extensions):
//   0   freestream
//   1   inlet face
//   2   compressor face / diffuser exit / fan inlet
//   3   compressor exit / combustor inlet
//   4   combustor exit / turbine inlet
//   5   turbine exit (mixer hot inlet)
//   6   mixer exit (replaces afterburner-inlet slot for dry turbofan)
//   7   afterburner exit (NaN — phase 1 dry only)
//   8   nozzle throat
//   9   nozzle exit (perfect expansion, P_9 = P_∞)
//  13   fan exit (cold path, post-fan)
//  16   bypass duct exit (cold path, mixer cold inlet)
//
// Single-spool architecture
// -------------------------
// Fan and HPC sit on the same shaft, driven by a single turbine.
// HPC inlet is colocated with fan exit on the cold side: T_t21 ≡ T_t13
// (no inter-compressor work in the simplified model). The full intake
// passes through the fan, then splits into core (mass m_core, fed to
// the HPC) + bypass (mass BPR · m_core, fed to the bypass duct).
// Bypass duct is lossless in phase 1 (T_t16 = T_t13, P_t16 = P_t13).
//
// Shaft balance (per-core-mass unitization mirrors the turbojet
// pattern at TurbojetCycleSolver.cs:144):
//
//   ΔT_turb = [(1 + BPR) · (T_t13 − T_t2) + (T_t3 − T_t13)] / [(1 + f) · η_mech]
//   T_t5    = T_t4 − ΔT_turb
//
// The (1 + BPR) factor on the fan-work term is load-bearing — that is
// exactly how bypass mass loads the turbine more than a turbojet at
// the same fan-pressure-ratio.
//
// Fan pressure ratio
// ------------------
// Phase 1 derives π_fan = √π_c (a single-spool min-fuel proxy). The
// derivation lives in DefaultFanPressureRatio so a future sprint can
// swap it for the BPR-aware optimum (Mattingly §7
// π_fan_opt ≈ π_c^(BPR/(1+BPR))) or promote π_fan to a separate SA dim
// without touching this solver's caller-facing API.
//
// Mixer
// -----
// Mass-flow-weighted constant-area mixer:
//
//   T_t6 = (m_hot · T_t5 + m_cold · T_t16) / m_total
//   P_t6 = π_mixer · (m_hot · P_t5 + m_cold · P_t16) / m_total
//
// where m_hot = (1 + f) · m_core and m_cold = BPR · m_core. The single
// recovery factor π_mixer = 0.97 absorbs both the canonical pressure-
// recovery loss and the entropy-of-mixing loss (which is dominant when
// T_t5 ≫ T_t13, always true here). Stream B with cp(T) tabulation will
// replace the lumped recovery factor with a constant-area Mach-
// equilibrium mixer that captures the mixing-entropy term explicitly.
//
// Simplifying assumptions (Sprint A8 phase 1)
// -------------------------------------------
//   1. Constant cp + γ throughout (cold + hot side).
//   2. Single-spool (fan + HPC on same shaft).
//   3. π_fan = √π_c (single-spool min-fuel proxy).
//   4. Lossless bypass duct (T_t16 = T_t13, P_t16 = P_t13).
//   5. Mass-flow-weighted constant-area mixer with lumped π_mixer = 0.97.
//   6. No afterburner (station 7 is NaN).
//   7. η_mech = 0.99 (1% bearing loss), other constants from turbojet.
//   8. Compressor face Mach hardcoded at 0.5 (matches turbojet).
//   9. Perfect expansion at nozzle exit (P_9 = P_∞).
//
// F404-class fixture (~48 kN dry, π_c = 25, BPR = 0.34, T_t4 ≈ 1700 K)
// lands within the ±25 % tolerance band given the simplifications
// above. Single-spool optimism (real F404 is two-spool) is the dominant
// error term; documented in the F404 fixture's tolerance choice.

using System;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Single-spool low-bypass mixed-flow turbofan cycle solver. Phase 1
/// uses parametric stand-in maps + constant-property gas + lumped
/// mixer recovery; real off-design behaviour and cp(T) tabulation
/// land in Stream B.
/// </summary>
public sealed class TurbofanCycleSolver : IAirbreathingCycleSolver
{
    /// <summary>Combustor stagnation pressure recovery π_b.</summary>
    public const double CombustorPressureRecovery = 0.96;

    /// <summary>Combustion efficiency η_b.</summary>
    public const double CombustionEfficiency = 0.99;

    /// <summary>Nozzle stagnation pressure recovery π_n.</summary>
    public const double NozzlePressureRecovery = 0.97;

    /// <summary>
    /// Compressor face Mach number — hardcoded for the Sprint A7/A8
    /// parametric stand-in. Matches the turbojet to keep the fan-
    /// intake mass-flow model identical.
    /// </summary>
    public const double CompressorFaceMach = 0.5;

    /// <summary>
    /// Mechanical efficiency η_mech of the single shaft connecting
    /// turbine, HPC, and fan. 0.99 captures bearing + windage losses.
    /// </summary>
    public const double DefaultMechanicalEfficiency = 0.99;

    /// <summary>
    /// Stagnation pressure recovery factor of the constant-area mixer
    /// π_mixer = P_t6 / mass-flow-weighted mean of (P_t5, P_t16).
    /// 0.97 absorbs both the canonical pressure-recovery loss and the
    /// entropy-of-mixing loss. Constant for phase 1; Stream B will
    /// replace this with a Mach-equilibrium mixer that derives the
    /// recovery from the inlet-flow Mach + temperature-ratio.
    /// </summary>
    public const double DefaultMixerPressureRecovery = 0.97;

    /// <summary>
    /// Length of the StationState array for a turbofan solve. Indices
    /// 0-9 mirror the turbojet; 13 = fan exit, 16 = bypass duct exit.
    /// Indices 10-12 + 14-15 stay default (unused, reserved for
    /// two-spool stations 21 / 25 / etc. in a Stream B sprint).
    /// </summary>
    public const int StationArrayLength = 17;

    /// <summary>
    /// Default fan pressure ratio derivation for the phase-1 single-
    /// spool model: π_fan = √π_c. A min-fuel proxy that closes shaft
    /// balance cleanly across the F404 envelope (π_c=25, BPR=0.34
    /// gives π_fan = 5.0, T_t13 ≈ 422 K, T_t3 ≈ 711 K).
    /// </summary>
    /// <remarks>
    /// Mattingly §7 gives the BPR-aware thrust-matched optimum as
    /// π_fan ≈ π_c^(BPR/(1+BPR)) — for F404 that yields ≈2.0, which
    /// underloads the fan vs the √π_c proxy. The √π_c rule is the
    /// established preliminary-design heuristic for low-BPR military
    /// engines and is what we ship in phase 1. A future sprint can
    /// swap in the BPR-aware form (or promote π_fan to its own SA dim)
    /// without touching the cycle-solver caller surface.
    /// </remarks>
    public static double DefaultFanPressureRatio(double compressorPressureRatio, double bypassRatio)
    {
        _ = bypassRatio; // reserved for the BPR-aware Stream B variant
        if (compressorPressureRatio < 1.0)
            throw new ArgumentOutOfRangeException(nameof(compressorPressureRatio),
                $"π_c = {compressorPressureRatio} must be ≥ 1.");
        return Math.Sqrt(compressorPressureRatio);
    }

    private readonly ICompressorMap _compressorMap;
    private readonly ITurbineMap _turbineMap;
    private readonly double _mechanicalEfficiency;
    private readonly double _mixerPressureRecovery;

    /// <summary>
    /// Default-constructed solver — uses the constant-η Jones-style
    /// stand-in maps for compressor (fan + HPC share one map) +
    /// turbine, and the canonical η_mech / π_mixer constants.
    /// </summary>
    public TurbofanCycleSolver()
        : this(
            ConstantEfficiencyCompressorMap.Default,
            ConstantEfficiencyTurbineMap.Default,
            DefaultMechanicalEfficiency,
            DefaultMixerPressureRecovery) { }

    /// <summary>
    /// Custom-map constructor — for tests + future real-map
    /// implementations.
    /// </summary>
    public TurbofanCycleSolver(
        ICompressorMap compressorMap,
        ITurbineMap turbineMap,
        double mechanicalEfficiency,
        double mixerPressureRecovery)
    {
        _compressorMap = compressorMap ?? throw new ArgumentNullException(nameof(compressorMap));
        _turbineMap = turbineMap ?? throw new ArgumentNullException(nameof(turbineMap));
        if (mechanicalEfficiency <= 0.0 || mechanicalEfficiency > 1.0)
            throw new ArgumentOutOfRangeException(nameof(mechanicalEfficiency),
                $"η_mech = {mechanicalEfficiency} must lie in (0, 1].");
        if (mixerPressureRecovery <= 0.0 || mixerPressureRecovery > 1.0)
            throw new ArgumentOutOfRangeException(nameof(mixerPressureRecovery),
                $"π_mixer = {mixerPressureRecovery} must lie in (0, 1].");
        _mechanicalEfficiency = mechanicalEfficiency;
        _mixerPressureRecovery = mixerPressureRecovery;
    }

    /// <inheritdoc />
    public AirbreathingEngineKind Kind => AirbreathingEngineKind.Turbofan;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when
    /// <see cref="AirbreathingEngineDesign.CompressorPressureRatio"/>
    /// is NaN or &lt; 1, or when
    /// <see cref="AirbreathingEngineDesign.BypassRatio"/> is NaN or
    /// &lt; 0.
    /// </exception>
    public CycleSolveResult Solve(AirbreathingEngineDesign design, FlightConditions cond)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (cond is null) throw new ArgumentNullException(nameof(cond));
        if (design.Kind != AirbreathingEngineKind.Turbofan)
            throw new ArgumentException(
                $"TurbofanCycleSolver invoked with design.Kind = {design.Kind}; expected Turbofan.",
                nameof(design));
        if (double.IsNaN(design.CompressorPressureRatio) || design.CompressorPressureRatio < 1.0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"Turbofan CompressorPressureRatio = {design.CompressorPressureRatio:F3} must be >= 1.");
        if (double.IsNaN(design.BypassRatio) || design.BypassRatio < 0.0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"Turbofan BypassRatio = {design.BypassRatio:F3} must be >= 0.");

        var fuel = AirbreathingFuelTables.Lookup(cond.Fuel);
        var atm = StandardAtmosphere.At(cond.Altitude_m);
        double bpr = design.BypassRatio;

        // Station 0 / 1 — freestream + inlet face. ṁ_a is the FULL
        // fan intake (core + bypass); the splitter downstream of the
        // fan sets m_core = ṁ_a / (1 + BPR).
        double V_inf = cond.MachNumber * IdealGasAir.SpeedOfSound_m_s(atm.StaticT_K);
        double T_t0 = atm.StaticT_K * IdealGasAir.StagnationTemperatureRatio(cond.MachNumber);
        double P_t0 = atm.StaticP_Pa * IdealGasAir.StagnationPressureRatio(cond.MachNumber);

        double T_face_static = T_t0 / IdealGasAir.StagnationTemperatureRatio(CompressorFaceMach);
        double P_face_static = P_t0 / IdealGasAir.StagnationPressureRatio(CompressorFaceMach);
        double rho_face = P_face_static / (IdealGasAir.R_J_kg_K * T_face_static);
        double V_face = CompressorFaceMach * IdealGasAir.SpeedOfSound_m_s(T_face_static);
        double mdot_inlet = rho_face * V_face * design.InletThroatArea_m2;
        double mdot_core = mdot_inlet / (1.0 + bpr);
        double mdot_cold = bpr * mdot_core;

        var s0 = new StationState(T_t0, P_t0, mdot_inlet, cond.MachNumber);

        // Station 2 — diffuser exit (= fan face). Adiabatic; π_d from
        // MIL-STD-5007D shock-train recovery curve. Mass through this
        // station is the full intake (everything passes through the
        // fan).
        double pi_d = InletRecovery.Pi_d(cond.MachNumber);
        double T_t2 = T_t0;
        double P_t2 = P_t0 * pi_d;
        var s2 = new StationState(T_t2, P_t2, mdot_inlet, CompressorFaceMach);

        // Station 13 — fan exit (cold path). π_fan is the explicit
        // design knob when PiFan is set (two-spool), otherwise the
        // Sprint-A8 phase-1 √π_c proxy (single-spool).
        double pi_fan = design.PiFan ?? DefaultFanPressureRatio(design.CompressorPressureRatio, bpr);
        var fan = _compressorMap.Operate(T_t2, P_t2, pi_fan);
        double T_t13 = fan.OutletStagnationT_K;
        double P_t13 = fan.OutletStagnationP_Pa;
        var s13 = new StationState(T_t13, P_t13, mdot_inlet, CompressorFaceMach);

        // Station 16 — bypass duct exit (cold path, mixer entry).
        // Lossless duct in phase 1.
        double T_t16 = T_t13;
        double P_t16 = P_t13;
        var s16 = new StationState(T_t16, P_t16, mdot_cold, CompressorFaceMach);

        // Station 3 — HPC exit (core path). HPC inlet ≡ fan exit
        // (T_t21 = T_t13, P_t21 = P_t13). Residual HPC ratio
        // π_hpc = π_c / π_fan keeps the overall compression at π_c.
        double pi_hpc = design.CompressorPressureRatio / pi_fan;
        var compressor = _compressorMap.Operate(T_t13, P_t13, pi_hpc);
        double T_t3 = compressor.OutletStagnationT_K;
        double P_t3 = compressor.OutletStagnationP_Pa;
        var s3 = new StationState(T_t3, P_t3, mdot_core, 0.3);

        // Station 4 — combustor exit. Energy balance same as turbojet
        // but on the core mass flow only (bypass air does not see the
        // combustor).
        double f = design.EquivalenceRatio * fuel.StoichiometricFuelAirRatio;
        double T_t4 = (T_t3 + f * CombustionEfficiency * fuel.LowerHeatingValue_J_kg / IdealGasAir.Cp_J_kg_K)
                    / (1.0 + f);
        double P_t4 = P_t3 * CombustorPressureRecovery;
        double mdot_hot = mdot_core * (1.0 + f);

        // Cooled-turbine blending: τ > 0 blends HPC exit into the
        // effective TIT, simulating compressor-bleed film cooling.
        //   T_t4_eff = T_t4 · (1 − τ) + T_t3 · τ
        double tau = design.TurbineCoolingFraction;
        double T_t4_eff = T_t4 * (1.0 - tau) + T_t3 * tau;
        var s4 = new StationState(T_t4_eff, P_t4, mdot_hot, 0.3);

        // Stations 5, 6, 10 — turbines + mixer.
        // Two-spool path when PiFan is set: separate HP spool (HPC +
        // HP turbine) and LP spool (fan + LP turbine) with a
        // Mach-equilibrium mixer.
        // Single-spool path when PiFan is null: unchanged Sprint A8
        // shaft balance + lumped π_mixer recovery.
        StationState s5, s6, s10;
        double T_t6, P_t6, mdot_mixed;

        if (design.PiFan.HasValue)
        {
            // HP turbine drives HPC only. Per kg total turbine mass flow:
            //   W_hpt = Cp · (T_t3 − T_t13) / (η_mech · (1+f))
            double W_hpt_per_total = IdealGasAir.Cp_J_kg_K * (T_t3 - T_t13)
                                   / (_mechanicalEfficiency * (1.0 + f));
            var hpt = _turbineMap.Operate(T_t4_eff, P_t4, W_hpt_per_total);
            double T_t45 = hpt.OutletStagnationT_K;
            double P_t45 = hpt.OutletStagnationP_Pa;
            s10 = new StationState(T_t45, P_t45, mdot_hot, 0.4);

            // LP turbine drives fan. All bypass mass flows through the
            // fan, so the LP turbine load includes (1+BPR):
            //   W_lpt = Cp · (1+BPR) · (T_t13 − T_t2) / (η_mech · (1+f))
            double W_lpt_per_total = IdealGasAir.Cp_J_kg_K * (1.0 + bpr) * (T_t13 - T_t2)
                                   / (_mechanicalEfficiency * (1.0 + f));
            var lpt = _turbineMap.Operate(T_t45, P_t45, W_lpt_per_total);
            double T_t5_2s = lpt.OutletStagnationT_K;
            double P_t5_2s = lpt.OutletStagnationP_Pa;
            s5 = new StationState(T_t5_2s, P_t5_2s, mdot_hot, 0.4);

            // Mach-equilibrium mixer. Assume M_hot = 0.4 at the mixing
            // plane; derive static P_s from hot stream; solve M_cold via
            // pressure equality; compute T_t6 (energy) + P_t6 (momentum).
            const double MixerHotMach = 0.4;
            double P_s_mix   = P_t5_2s / IdealGasAir.StagnationPressureRatio(MixerHotMach);
            double M_cold_mix = P_t16 > P_s_mix
                ? IdealGasAir.MachFromStagnationPressureRatio(P_t16 / P_s_mix)
                : CompressorFaceMach;  // fallback when bypass pressure is too low

            double T_s_hot_m  = T_t5_2s / IdealGasAir.StagnationTemperatureRatio(MixerHotMach);
            double T_s_cold_m = T_t16   / IdealGasAir.StagnationTemperatureRatio(M_cold_mix);
            double V_hot_m    = MixerHotMach * IdealGasAir.SpeedOfSound_m_s(T_s_hot_m);
            double V_cold_m   = M_cold_mix   * IdealGasAir.SpeedOfSound_m_s(T_s_cold_m);

            mdot_mixed = mdot_hot + mdot_cold;
            T_t6 = (mdot_hot * T_t5_2s + mdot_cold * T_t16) / mdot_mixed;
            double V_mix_m = (mdot_hot * V_hot_m + mdot_cold * V_cold_m) / mdot_mixed;
            double T_s6_m  = T_t6 - V_mix_m * V_mix_m / (2.0 * IdealGasAir.Cp_J_kg_K);
            double M_mix_m = T_s6_m > 1.0
                ? V_mix_m / IdealGasAir.SpeedOfSound_m_s(T_s6_m)
                : 0.4;
            P_t6 = P_s_mix * IdealGasAir.StagnationPressureRatio(M_mix_m);

            // Station 16 Mach = bypass-duct Mach at mixing plane.
            // Used by the BYPASS_DUCT_CHOKED feasibility gate.
            s16 = new StationState(T_t16, P_t16, mdot_cold, M_cold_mix);
            s6  = new StationState(T_t6,  P_t6,  mdot_mixed, M_mix_m);
        }
        else
        {
            // Single-spool path (Sprint A8 phase 1, unchanged).
            // Shaft balance (per-core-mass):
            //   ΔT_turb = [(1 + BPR)·(T_t13 − T_t2) + (T_t3 − T_t13)] / [(1 + f)·η_mech]
            double dT_fan_per_core = (1.0 + bpr) * (T_t13 - T_t2);
            double dT_hpc_per_core = T_t3 - T_t13;
            double W_required_per_core = IdealGasAir.Cp_J_kg_K * (dT_fan_per_core + dT_hpc_per_core)
                                       / _mechanicalEfficiency;
            double W_required_per_total = W_required_per_core / (1.0 + f);
            var turbine = _turbineMap.Operate(T_t4_eff, P_t4, W_required_per_total);
            double T_t5 = turbine.OutletStagnationT_K;
            double P_t5 = turbine.OutletStagnationP_Pa;
            s5  = new StationState(T_t5, P_t5, mdot_hot, 0.4);
            s10 = NaNStation();

            // Lumped mixer: mass-flow-weighted with π_mixer recovery.
            mdot_mixed = mdot_hot + mdot_cold;
            T_t6 = (mdot_hot * T_t5 + mdot_cold * T_t16) / mdot_mixed;
            P_t6 = _mixerPressureRecovery
                 * (mdot_hot * P_t5 + mdot_cold * P_t16) / mdot_mixed;
            s6 = new StationState(T_t6, P_t6, mdot_mixed, 0.4);
        }

        // Station 9 — nozzle exit, perfect expansion of the mixed
        // flow.
        double T_t9 = T_t6;
        double P_t9 = P_t6 * NozzlePressureRecovery;
        double pStagOverPStatic = P_t9 / atm.StaticP_Pa;

        double M_9, T_9, V_9, F_net;
        if (pStagOverPStatic >= 1.0)
        {
            M_9 = IdealGasAir.MachFromStagnationPressureRatio(pStagOverPStatic);
            T_9 = T_t9 / IdealGasAir.StagnationTemperatureRatio(M_9);
            double a_9 = IdealGasAir.SpeedOfSound_m_s(T_9);
            V_9 = M_9 * a_9;
            // Momentum thrust for the mixed flow: ṁ_mixed · V_9 −
            // ṁ_inlet · V_∞. Fuel-mass term is in m_mixed (≡ m_inlet
            // + m_fuel); ram drag uses the full intake.
            F_net = mdot_mixed * V_9 - mdot_inlet * V_inf;
        }
        else
        {
            M_9 = double.NaN;
            T_9 = double.NaN;
            V_9 = 0.0;
            F_net = 0.0;
        }

        var s9 = new StationState(T_t9, P_t9, mdot_mixed, M_9);

        // Performance
        double mdot_f = mdot_core * f;
        double Isp = (mdot_f > 0.0 && F_net > 0.0)
            ? F_net / (mdot_f * StandardAtmosphere.G0_m_s2)
            : 0.0;

        var stations = new StationState[StationArrayLength];
        stations[0] = s0;
        stations[1] = s0;            // inlet face = freestream (lumped 0-D)
        stations[2] = s2;
        stations[3] = s3;
        stations[4] = s4;
        stations[5] = s5;
        stations[6] = s6;
        stations[7] = NaNStation();   // no afterburner in phase 1
        stations[8] = new StationState(T_t9, P_t9, mdot_mixed, 1.0);
        stations[9] = s9;
        // 10 = HP turbine exit in two-spool mode; NaN in single-spool.
        // 11-12 + 14-15 stay default — reserved for future extensions.
        stations[10] = s10;
        stations[11] = NaNStation();
        stations[12] = NaNStation();
        stations[13] = s13;
        stations[14] = NaNStation();
        stations[15] = NaNStation();
        stations[16] = s16;

        return new CycleSolveResult(
            Stations: new StationMap(
                Stations:           stations,
                ThrustNet_N:        F_net,
                SpecificImpulse_s:  Isp,
                FuelMassFlow_kg_s:  mdot_f),
            CompressorDiagnostics: null,
            TurbineDiagnostics:    null);
    }

    /// <summary>
    /// Solve the turbofan at a corrected speed fraction
    /// <paramref name="N_corr_frac"/> by iterating π_c via a 1-D Newton
    /// loop over the shaft-balance residual.
    /// </summary>
    /// <param name="design">Engine design (design-point π_c used as upper bound).</param>
    /// <param name="cond">Flight conditions.</param>
    /// <param name="N_corr_frac">
    /// Corrected speed fraction N/N_design ∈ (0, 1.5]. At 1.0 the result
    /// converges to <see cref="Solve"/> within the residual tolerance.
    /// </param>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// <paramref name="N_corr_frac"/> ≤ 0 or &gt; 1.5.
    /// </exception>
    /// <exception cref="CycleNotConvergedException">
    /// Newton loop did not converge within 20 iterations.
    /// </exception>
    public CycleSolveResult SolveAtOperatingPoint(
        AirbreathingEngineDesign design,
        FlightConditions cond,
        double N_corr_frac = 1.0)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (cond is null) throw new ArgumentNullException(nameof(cond));
        if (N_corr_frac <= 0.0 || N_corr_frac > 1.5)
            throw new ArgumentOutOfRangeException(nameof(N_corr_frac),
                $"N_corr_frac = {N_corr_frac} must lie in (0, 1.5].");

        // Affinity-law initial guess: π_c ≈ 1 + (π_c_design − 1)·N²
        double pi_c_design = design.CompressorPressureRatio;
        double pi_c_k = 1.0 + (pi_c_design - 1.0) * N_corr_frac * N_corr_frac;
        pi_c_k = Math.Max(1.01, Math.Min(pi_c_k, pi_c_design * 1.5));

        double residual = double.NaN;
        const int MaxIter = 20;
        const double Tolerance = 1e-3;

        for (int iter = 0; iter < MaxIter; iter++)
        {
            // Evaluate shaft balance at current pi_c_k
            double r = ShaftBalanceResidual(design, cond, pi_c_k);

            // Convergence check normalised by |W_turb| + small guard
            double scale = Math.Abs(TurbineWorkAtPiC(design, cond, pi_c_k));
            residual = Math.Abs(r) / Math.Max(scale, 1.0);
            if (residual <= Tolerance)
            {
                var designAtPiC = design with { CompressorPressureRatio = pi_c_k };
                return Solve(designAtPiC, cond);
            }

            // Finite-difference Jacobian
            double delta = pi_c_k * 1e-4;
            double r_fd = ShaftBalanceResidual(design, cond, pi_c_k + delta);
            double dR_dpi = (r_fd - r) / delta;

            if (Math.Abs(dR_dpi) < 1e-30) break; // degenerate — give up

            // Newton step with clamping
            pi_c_k -= r / dR_dpi;
            pi_c_k = Math.Max(1.01, Math.Min(pi_c_k, pi_c_design * 1.5));
        }

        throw new CycleNotConvergedException(MaxIter, residual);
    }

    // Shaft-balance residual R(π_c) = W_turb·η_mech − W_fan − W_hpc (units: cp·ṁ·K).
    private double ShaftBalanceResidual(AirbreathingEngineDesign design, FlightConditions cond, double piC)
    {
        var fuel = AirbreathingFuelTables.Lookup(cond.Fuel);
        var atm = StandardAtmosphere.At(cond.Altitude_m);
        double bpr = design.BypassRatio;

        double T_t0 = atm.StaticT_K * IdealGasAir.StagnationTemperatureRatio(cond.MachNumber);
        double P_t0 = atm.StaticP_Pa * IdealGasAir.StagnationPressureRatio(cond.MachNumber);
        double pi_d = InletRecovery.Pi_d(cond.MachNumber);
        double T_t2 = T_t0;
        double P_t2 = P_t0 * pi_d;

        double T_face_static = T_t2 / IdealGasAir.StagnationTemperatureRatio(CompressorFaceMach);
        double P_face_static = P_t2 / IdealGasAir.StagnationPressureRatio(CompressorFaceMach);
        double rho_face = P_face_static / (IdealGasAir.R_J_kg_K * T_face_static);
        double V_face = CompressorFaceMach * IdealGasAir.SpeedOfSound_m_s(T_face_static);
        double mdot_inlet = rho_face * V_face * design.InletThroatArea_m2;
        double mdot_core = mdot_inlet / (1.0 + bpr);

        double pi_fan = DefaultFanPressureRatio(piC, bpr);
        var fan = _compressorMap.Operate(T_t2, P_t2, pi_fan);
        double T_t13 = fan.OutletStagnationT_K;

        double pi_hpc = piC / pi_fan;
        var comp = _compressorMap.Operate(T_t13, fan.OutletStagnationP_Pa, pi_hpc);
        double T_t3 = comp.OutletStagnationT_K;
        double P_t3 = comp.OutletStagnationP_Pa;

        double f = design.EquivalenceRatio * fuel.StoichiometricFuelAirRatio;
        double T_t4 = (T_t3 + f * CombustionEfficiency * fuel.LowerHeatingValue_J_kg / IdealGasAir.Cp_J_kg_K)
                    / (1.0 + f);
        double tau = design.TurbineCoolingFraction;
        double T_t4_eff = T_t4 * (1.0 - tau) + T_t3 * tau;
        double P_t4 = P_t3 * CombustorPressureRecovery;

        double dT_fan = (1.0 + bpr) * (T_t13 - T_t2);
        double dT_hpc = T_t3 - T_t13;
        double W_fan_hpc = IdealGasAir.Cp_J_kg_K * (dT_fan + dT_hpc) * mdot_core;
        double W_required_per_total = IdealGasAir.Cp_J_kg_K * (dT_fan + dT_hpc)
                                    / _mechanicalEfficiency / (1.0 + f);
        var turbine = _turbineMap.Operate(T_t4_eff, P_t4, W_required_per_total);
        double dT_turb = T_t4_eff - turbine.OutletStagnationT_K;
        double W_turb = IdealGasAir.Cp_J_kg_K * dT_turb * mdot_core * (1.0 + f);

        return W_turb * _mechanicalEfficiency - W_fan_hpc;
    }

    // Turbine work extraction used for residual normalisation.
    private double TurbineWorkAtPiC(AirbreathingEngineDesign design, FlightConditions cond, double piC)
    {
        var fuel = AirbreathingFuelTables.Lookup(cond.Fuel);
        var atm = StandardAtmosphere.At(cond.Altitude_m);
        double bpr = design.BypassRatio;

        double T_t0 = atm.StaticT_K * IdealGasAir.StagnationTemperatureRatio(cond.MachNumber);
        double P_t0 = atm.StaticP_Pa * IdealGasAir.StagnationPressureRatio(cond.MachNumber);
        double pi_d = InletRecovery.Pi_d(cond.MachNumber);
        double T_t2 = T_t0;
        double P_t2 = P_t0 * pi_d;

        double T_face_static = T_t2 / IdealGasAir.StagnationTemperatureRatio(CompressorFaceMach);
        double P_face_static = P_t2 / IdealGasAir.StagnationPressureRatio(CompressorFaceMach);
        double rho_face = P_face_static / (IdealGasAir.R_J_kg_K * T_face_static);
        double V_face = CompressorFaceMach * IdealGasAir.SpeedOfSound_m_s(T_face_static);
        double mdot_core = rho_face * V_face * design.InletThroatArea_m2 / (1.0 + bpr);

        double pi_fan = DefaultFanPressureRatio(piC, bpr);
        var fan = _compressorMap.Operate(T_t2, P_t2, pi_fan);
        double T_t13 = fan.OutletStagnationT_K;
        double pi_hpc = piC / pi_fan;
        var comp = _compressorMap.Operate(T_t13, fan.OutletStagnationP_Pa, pi_hpc);
        double T_t3 = comp.OutletStagnationT_K;
        double P_t3 = comp.OutletStagnationP_Pa;

        double f = design.EquivalenceRatio * fuel.StoichiometricFuelAirRatio;
        double T_t4 = (T_t3 + f * CombustionEfficiency * fuel.LowerHeatingValue_J_kg / IdealGasAir.Cp_J_kg_K)
                    / (1.0 + f);
        double tau = design.TurbineCoolingFraction;
        double T_t4_eff = T_t4 * (1.0 - tau) + T_t3 * tau;
        double P_t4 = P_t3 * CombustorPressureRecovery;
        double W_req = IdealGasAir.Cp_J_kg_K
                     * ((1.0 + bpr) * (T_t13 - T_t2) + (T_t3 - T_t13))
                     / _mechanicalEfficiency / (1.0 + f);
        var turb = _turbineMap.Operate(T_t4_eff, P_t4, W_req);
        return IdealGasAir.Cp_J_kg_K * (T_t4_eff - turb.OutletStagnationT_K) * mdot_core * (1.0 + f);
    }

    private static StationState NaNStation()
        => new(double.NaN, double.NaN, 0.0, double.NaN);
}
