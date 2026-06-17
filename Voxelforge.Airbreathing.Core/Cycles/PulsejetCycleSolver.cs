// PulsejetCycleSolver.cs — Wave 1 PR-4 valveless pulsejet station march.
//
// Closed-form 0-D cycle analysis per Foa 1960 *Elements of Flight Propulsion*
// §11. Distinct from the ramjet's constant-pressure Brayton cycle: pulsejet
// combustion is constant-volume Humphrey (Foa §11.4). The thermodynamic
// state is treated as cycle-mean (time-averaged over the buzz oscillation),
// with peak-cycle pressure exposed via HumphreyCyclePerformance for the
// PULSEJET_ACOUSTIC_OVERPRESSURE advisory gate.
//
// Station numbering (SAE AS755):
//   0  freestream (or atmosphere for static operation)
//   1  intake face (= station 0 in lumped 0-D)
//   2  diffuser exit (forward-firing diffuser, π_d ≈ 0.85 typical)
//   4  combustor cycle-mean exit (Humphrey constant-volume balance)
//   5  pre-tailpipe = combustor exit
//   8  tailpipe throat (sonic at peak chamber-P moments; subsonic mean)
//   9  tailpipe exit (subsonic, near-atmospheric)
// Stations 3, 6, 7 are NaN — pulsejet has no compressor or afterburner.
//
// Static-thrust handling: at M_∞ ≈ 0 the inertial momentum-flux into the
// intake vanishes, but real pulsejets self-drive the intake via the buzz
// oscillation. This solver applies a volumetric-efficiency model
// (η_vol = 0.40 calibrated against V-1 NACA RM E50A04 data) to estimate
// cycle-mean ṁ_a at static. Forward-flight (M_∞ > 0.1) uses the standard
// inertial capture-area model the ramjet uses.
//
// Simplifying assumptions:
//   1. Constant-property gas (γ = 1.4, cp = 1004.7 J/(kg·K)). Real
//      hot-cycle cp(T) variation is deferred — same trade-off
//      RamjetCycleSolver accepts. Wider validation tolerances absorb the
//      error.
//   2. Combustor pressure recovery π_b = 0.92 — slightly lower than
//      ramjet's 0.98 because the cyclic operation has more pressure loss
//      than a steady-flow combustor. NACA RM E50A04 fig 8 supports
//      π_b ≈ 0.85-0.95 for V-1-class engines.
//   3. Tailpipe pressure recovery π_n = 0.90 — long-tube with no CD
//      contour gives lower recovery than ramjet's 0.96.
//   4. Time-averaged station map. Cycle dynamics (buzz frequency, valveless
//      reverse flow) are absorbed into mean values. Peak-cycle Pc surfaces
//      separately via HumphreyCyclePerformance.PeakChamberPressureRatio
//      for the acoustic-overpressure gate.
//
// References:
//   Foa, J.V. 1960 Elements of Flight Propulsion, Wiley, §11.
//   NACA RM E50A04 (Cleveland-instrumented V-1 buzz-bomb static-thrust tests).
//   Glassman, I. 1996 Combustion §3 (lower flammability limits, used by gate).

using System;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Valveless pulsejet cycle solver. Closed-form 0-D Humphrey-cycle analysis
/// per Foa 1960 §11.
/// </summary>
public sealed class PulsejetCycleSolver : IAirbreathingCycleSolver
{
    /// <summary>
    /// Combustor stagnation pressure recovery factor π_b = P_t4 / P_t2.
    /// Foa §11.4 + NACA RM E50A04 fig 8 — V-1-class valveless pulsejet
    /// combustors cluster around 0.85-0.95 due to cyclic pressure loss
    /// during the venting-and-charge phase. 0.92 is the centred preliminary-
    /// design value.
    /// </summary>
    public const double CombustorPressureRecovery = 0.92;

    /// <summary>
    /// Tailpipe stagnation pressure recovery factor π_n = P_t9 / P_t4. Long-
    /// tube valveless geometry gives lower recovery than a CD nozzle.
    /// </summary>
    public const double TailpipePressureRecovery = 0.90;

    /// <summary>
    /// Forward-firing diffuser pressure recovery π_d for a valveless
    /// pulsejet at low Mach. Foa §11.3.
    /// </summary>
    public const double DiffuserPressureRecovery = 0.85;

    /// <summary>
    /// Volumetric efficiency η_vol for static operation (M_∞ ≈ 0).
    /// Calibrated against V-1 (Argus As 109-014) sea-level-static thrust
    /// ~3 kN and the energy-balance V_9 ≈ 1650 m/s at φ=0.95 / JP-8 /
    /// T_t4≈3000 K. Target ṁ_total = F/V_9 ≈ 1.82 kg/s; with intake area
    /// 0.030 m² and ambient ρ·c = 416.5 kg/(m²·s) the required η_vol =
    /// 1.82 / (0.030 · 416.5) ≈ 0.146. Set to 0.14. Engineering rule of
    /// thumb for valveless buzz operation per Foa §11.3 — typical
    /// volumetric efficiency 10-25% because cycle dynamics include
    /// residual hot-gas backflow phase.
    /// </summary>
    public const double StaticVolumetricEfficiency = 0.14;

    /// <summary>
    /// Mach threshold below which the static-thrust volumetric-efficiency
    /// model is used. Above this, the standard inertial capture-area model
    /// (ρ·V·A) applies.
    /// </summary>
    public const double StaticMachThreshold = 0.10;

    /// <summary>
    /// Buzz-cycle thermal-to-kinetic conversion efficiency for the
    /// energy-balance exhaust-velocity model. Below 1.0 because cyclic
    /// operation has more losses than steady flow: incomplete burn,
    /// reverse-flow recompression, and heat loss to walls during the
    /// charge phase. Calibrated against V-1 NACA RM E50A04 ~3 kN
    /// sea-level static thrust given ṁ_a ~0.83 kg/s and T_t4 ~2400 K.
    /// </summary>
    public const double BuzzEnergyEfficiency = 0.50;

    /// <summary>
    /// Volumetric efficiency η_vol for a valveless (Lockwood-Hiller U-tube)
    /// pulsejet at static conditions (M_∞ ≈ 0). Lower than
    /// <see cref="StaticVolumetricEfficiency"/> because both ends of the
    /// tube are open: reflected expansion waves in the tailpipe aspirate
    /// fresh charge, but partial backflow through the open intake during
    /// the blowdown phase reduces the net cycle-mean ingestion. Foa 1960
    /// §11.4 gives a typical range of 8–12 %; 0.10 is the centred value.
    /// </summary>
    public const double ValvelessVolumetricEfficiency = 0.10;

    /// <inheritdoc />
    public AirbreathingEngineKind Kind => AirbreathingEngineKind.Pulsejet;

    /// <inheritdoc />
    public CycleSolveResult Solve(AirbreathingEngineDesign design, FlightConditions cond)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (cond is null) throw new ArgumentNullException(nameof(cond));
        if (design.Kind != AirbreathingEngineKind.Pulsejet)
            throw new ArgumentException(
                $"PulsejetCycleSolver invoked with design.Kind = {design.Kind}; expected Pulsejet.",
                nameof(design));

        var fuel = AirbreathingFuelTables.Lookup(cond.Fuel);
        var atm = StandardAtmosphere.At(cond.Altitude_m);

        // Intake area: prefer pulsejet-specific field; fall back to InletThroatArea
        // for v5 designs that haven't been re-saved at v6.
        double intakeArea = design.PulsejetIntakeArea_m2 > 0.0
            ? design.PulsejetIntakeArea_m2
            : design.InletThroatArea_m2;

        // Tailpipe exit area: same fallback rule.
        double tailpipeArea = design.PulsejetTailpipeArea_m2 > 0.0
            ? design.PulsejetTailpipeArea_m2
            : design.NozzleExitArea_m2;

        // Station 0 / 1 — freestream + intake face. Static state from
        // atmosphere; stagnation state from M_∞.
        double c_atm = IdealGasAir.SpeedOfSound_m_s(atm.StaticT_K);
        double V_inf = cond.MachNumber * c_atm;
        double T_t0 = atm.StaticT_K * IdealGasAir.StagnationTemperatureRatio(cond.MachNumber);
        double P_t0 = atm.StaticP_Pa * IdealGasAir.StagnationPressureRatio(cond.MachNumber);

        // Variant-dependent volumetric efficiency for the static path.
        // Valveless (Lockwood-Hiller) has lower η_vol than Standard
        // (reed-valve / V-1-style) because backflow through the open intake
        // during the blowdown phase reduces net cycle-mean ingestion.
        double etaVol = design.PulsejetVariant == PulsejetVariant.Valveless
            ? ValvelessVolumetricEfficiency
            : StaticVolumetricEfficiency;

        // Cycle-mean ṁ_a — static (buzz-driven) vs forward-flight branch.
        double mdot_a;
        if (cond.MachNumber >= StaticMachThreshold)
        {
            // Forward-flight: standard inertial capture (same as ramjet).
            // Variant does not affect forward-flight capture (momentum-flux
            // driven; valve geometry changes the cycle timing but not the
            // inertial capture).
            mdot_a = atm.Density_kg_m3 * V_inf * intakeArea;
        }
        else
        {
            // Static: buzz-driven self-aspiration. Foa §11.3 — cycle-mean
            // intake velocity scales with c · η_vol over the buzz period.
            mdot_a = atm.Density_kg_m3 * intakeArea * c_atm * etaVol;
        }

        var s0 = new StationState(
            StagnationT_K:    T_t0,
            StagnationP_Pa:   P_t0,
            MassFlow_kg_s:    mdot_a,
            MachNumber:       cond.MachNumber);

        // Station 2 — diffuser exit. Adiabatic (T_t2 = T_t0); π_d applied
        // to stagnation pressure. Diffuser-exit Mach low (~0.15) for
        // near-stagnation combustor entry.
        double T_t2 = T_t0;
        double P_t2 = P_t0 * DiffuserPressureRecovery;
        var s2 = new StationState(
            StagnationT_K:    T_t2,
            StagnationP_Pa:   P_t2,
            MassFlow_kg_s:    mdot_a,
            MachNumber:       0.15);

        // Station 4 — combustor cycle-mean exit. Humphrey constant-volume
        // combustion energy balance: T_t4 = T_t2 + η·f·LHV/cp. The
        // pressure-rise from constant-V combustion is captured separately
        // via HumphreyCyclePerformance.PeakChamberPressureRatio for the
        // acoustic-overpressure gate; the steady-mean P_t4 uses the
        // ramjet-style π_b = 0.92.
        double f = design.EquivalenceRatio * fuel.StoichiometricFuelAirRatio;
        double T_t4 = HumphreyCyclePerformance.CombustorExitT_K(
            T_t2, f, fuel.LowerHeatingValue_J_kg);
        double P_t4 = P_t2 * CombustorPressureRecovery;
        double mdot_total = mdot_a * (1.0 + f);
        var s4 = new StationState(
            StagnationT_K:    T_t4,
            StagnationP_Pa:   P_t4,
            MassFlow_kg_s:    mdot_total,
            MachNumber:       0.15);

        // Station 9 — tailpipe exit. Subsonic, near-atmospheric (no CD nozzle).
        // T_t9 = T_t4 (adiabatic tailpipe). P_t9 = P_t4 · π_n.
        double T_t9 = T_t4;
        double P_t9 = P_t4 * TailpipePressureRecovery;

        // Energy-balance exhaust velocity (Foa §11.3 + Hill & Peterson §5.5
        // applied to cyclic combustion). The pulsejet's cycle-averaged
        // exhaust velocity is driven by the thermal energy released, not
        // the steady-state stagnation-pressure recovery a ramjet uses.
        // For pulsejets the ram-pressure source is internal (Humphrey
        // peak-cycle pressure) rather than external (forward-flight ram),
        // so a stagnation-pressure expansion model breaks at static
        // conditions where π_d · π_b · π_n < 1 produces P_t9 < P_∞.
        // Use the textbook ideal-Brayton expansion velocity formula
        //   V_9 = √(2 · η_buzz · cp · T_t4 · (1 − T_∞/T_t4))
        // scaled by an empirical buzz-cycle efficiency that absorbs
        // cyclic losses (incomplete burn + reverse-flow recompression).
        double V_9, F_net;
        double T_9 = atm.StaticT_K;  // reported diagnostic; exhaust mixes to ambient
        if (T_t4 > atm.StaticT_K && atm.StaticT_K > 0.0)
        {
            V_9 = Math.Sqrt(
                2.0 * BuzzEnergyEfficiency * IdealGasAir.Cp_J_kg_K * T_t4
                * (1.0 - atm.StaticT_K / T_t4));
            // Net thrust: F = ṁ_total·V_9 − ṁ_a·V_∞
            // For static (V_∞ ≈ 0), F = ṁ_total · V_9.
            F_net = mdot_total * V_9 - mdot_a * V_inf;
        }
        else
        {
            // Combustor produced no net heat — degenerate / non-firing state.
            V_9 = 0.0;
            F_net = 0.0;
        }

        // Reported exit Mach (informational): derived from V_9 and local
        // sound speed at T_∞ since the exhaust mixes to ambient.
        double a_9 = IdealGasAir.SpeedOfSound_m_s(atm.StaticT_K);
        double M_9 = a_9 > 0.0 ? V_9 / a_9 : double.NaN;

        var s9 = new StationState(
            StagnationT_K:    T_t9,
            StagnationP_Pa:   P_t9,
            MassFlow_kg_s:    mdot_total,
            MachNumber:       M_9);

        // Performance.
        double mdot_f = mdot_a * f;
        double Isp = (mdot_f > 0.0 && F_net > 0.0)
            ? F_net / (mdot_f * StandardAtmosphere.G0_m_s2)
            : 0.0;

        // Build the 10-element station array. Pulsejet has no compressor
        // (3) or afterburner (6, 7); pre-throat (5) = pre-tailpipe = post-
        // combustor; throat (8) is NaN-meaningful for valveless geometry
        // but we report tailpipe-mean state there for symmetry with ramjet.
        var stations = new StationState[10];
        stations[0] = s0;
        stations[1] = s0;                           // intake face = freestream (lumped 0-D)
        stations[2] = s2;
        stations[3] = NaNStation();                 // no compressor
        stations[4] = s4;
        stations[5] = s4;                           // pre-tailpipe = post-combustor
        stations[6] = NaNStation();                 // no afterburner
        stations[7] = NaNStation();                 // no afterburner
        stations[8] = new StationState(             // tailpipe throat — same as exit (no CD)
            StagnationT_K:    T_t9,
            StagnationP_Pa:   P_t9,
            MassFlow_kg_s:    mdot_total,
            MachNumber:       double.IsNaN(M_9) ? double.NaN : M_9);
        stations[9] = s9;

        var stationMap = new StationMap(
            Stations:           stations,
            ThrustNet_N:        F_net,
            SpecificImpulse_s:  Isp,
            FuelMassFlow_kg_s:  mdot_f);

        // Estimated buzz frequency (issue #451): combined Helmholtz + half-wave-pipe
        // estimator with variant dispatch. Standard reed-valve uses closed-open mode
        // (V-1: ~45 Hz vs published 47 Hz, 4.3 % gap); Valveless Lockwood-Hiller uses
        // open-open mode. Falls through to NaN for degenerate (non-firing) cycles
        // where T_t4 ≤ T_∞ — Cold-flow buzz is undefined per Foa §11.3.
        double combustorVolume_m3 = design.CombustorArea_m2 * design.CombustorLength_m;
        double estimatedBuzz_Hz   = T_t4 > atm.StaticT_K
            ? HalfWavePipeAcousticCalculator.CombinedFrequency_Hz(
                tubeLength_m:         design.PulsejetTubeLength_m,
                intakeArea_m2:        intakeArea,
                combustorVolume_m3:   combustorVolume_m3,
                speedOfSoundCold_m_s: c_atm,
                speedOfSoundHot_m_s:  IdealGasAir.SpeedOfSound_m_s(T_t4),
                variant:              design.PulsejetVariant)
            : double.NaN;

        // Pulsejet has no rotating turbomachinery — both diagnostics null.
        return new CycleSolveResult(
            Stations:                stationMap,
            CompressorDiagnostics:   null,
            TurbineDiagnostics:      null)
        {
            EstimatedBuzzFrequency_Hz = estimatedBuzz_Hz,
        };
    }

    private static StationState NaNStation()
        => new(double.NaN, double.NaN, 0.0, double.NaN);
}
