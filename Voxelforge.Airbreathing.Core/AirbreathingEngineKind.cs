// AirbreathingEngineKind.cs — discriminator for the air-breathing pillar.
//
// Parallel to ChannelTopology / EngineCycle on the rocket side. The
// ramjet/turbojet/turbofan/scramjet/RBCC sub-sequence in the
// air-breathing build-out lands one variant at a time;
// the enum is filled in incrementally and each new value is paired with
// an IAirbreathingCycleSolver implementation.

namespace Voxelforge.Airbreathing;

/// <summary>
/// Kind of air-breathing engine. Discriminator for
/// <see cref="AirbreathingEngineDesign"/> + the
/// <c>IAirbreathingCycleSolver</c> registry.
/// </summary>
/// <remarks>
/// Air-breathing propulsion sub-sequence:
/// ramjet → turbojet → turbofan → scramjet → RBCC. Sprint
/// A4 ships <see cref="Ramjet"/>; A7 adds <see cref="Turbojet"/>; later
/// sprints extend.
/// </remarks>
public enum AirbreathingEngineKind
{
    /// <summary>
    /// Sentinel — not a real engine. Records use it as the default value
    /// so an uninitialised design throws cleanly at the cycle-solver
    /// dispatch site rather than silently falling through.
    /// </summary>
    None = 0,

    /// <summary>
    /// Ramjet — no moving parts. Inlet recovery (subsonic + supersonic
    /// shock train) → subsonic combustor → CD nozzle. Sprint A4.
    /// </summary>
    Ramjet = 1,

    /// <summary>
    /// Turbojet — inlet → compressor → combustor → turbine → CD nozzle
    /// with shaft balance (compressor work = turbine work). Sprint A7.
    /// </summary>
    Turbojet = 2,

    /// <summary>
    /// Turbofan — turbojet core + bypass duct + (optional) mixer.
    /// Bypass-ratio (BPR) becomes an SA dim. Sub-step 1c, post-A7.
    /// </summary>
    Turbofan = 3,

    /// <summary>
    /// Scramjet — supersonic combustion, no flame stabiliser, isolator
    /// pseudo-shock train. Sub-step 1d, post-turbofan.
    /// </summary>
    Scramjet = 4,

    /// <summary>
    /// Rocket-Based Combined Cycle — touches both pillars (rocket +
    /// air-breathing). Sub-step 1e, capstone of Step 1.
    /// </summary>
    Rbcc = 5,

    /// <summary>
    /// Open Brayton-cycle gas turbine — stationary power generation.
    /// No useful jet exhaust;
    /// shaft power output is the primary product. Optional recuperator
    /// raises cycle efficiency by pre-heating combustor inlet air with
    /// turbine exhaust. Sprint A8 (paired with turbofan).
    /// </summary>
    GasTurbine = 6,

    /// <summary>
    /// Rankine-cycle steam turbine — stationary power generation.
    /// Boiler converts feedwater to superheated steam; turbine expands to condenser
    /// pressure; pump returns condensate. Primary output is shaft power.
    /// Design knobs: <see cref="AirbreathingEngineDesign.SteamBoilerPressure_bar"/>,
    /// <see cref="AirbreathingEngineDesign.SteamCondensePressure_bar"/>,
    /// <see cref="AirbreathingEngineDesign.SteamSuperheatDeltaT_K"/>.
    /// </summary>
    SteamTurbine = 7,

    /// <summary>
    /// Valveless pulsejet — intermittent constant-volume deflagration
    /// combustion driven by Helmholtz resonance of the combustor + tail.
    /// No moving parts (Argus / Lockwood Hiller forward-firing diffuser
    /// geometry). Wave 1.
    /// Cycle solver: <c>PulsejetCycleSolver</c> (Foa 1960 §11). Design
    /// knobs: <see cref="AirbreathingEngineDesign.PulsejetTubeLength_m"/>,
    /// <see cref="AirbreathingEngineDesign.PulsejetIntakeArea_m2"/>,
    /// <see cref="AirbreathingEngineDesign.PulsejetTailpipeArea_m2"/>.
    /// </summary>
    Pulsejet = 8,

    /// <summary>
    /// Turboprop — turbojet gas generator + free power turbine that
    /// extracts the majority of available enthalpy to drive a propeller
    /// via a reduction gearbox. Net thrust = propeller thrust (dominant)
    /// + small residual jet thrust from the exhaust nozzle.
    /// Cycle solver: <c>TurbopropCycleSolver</c> (Mattingly §8 / Walsh &amp;
    /// Fletcher §9). Design knob:
    /// <see cref="AirbreathingEngineDesign.PropellerPowerExtraction_frac"/>
    /// [0.85–0.95] sets what fraction of the isentropic enthalpy
    /// downstream of the gas-generator turbine exit the power turbine
    /// captures. Reference: Allison T56-A-15.
    /// </summary>
    Turboprop = 9,

    /// <summary>
    /// Turboshaft — free-turbine gas generator that extracts
    /// essentially 100 % of the available exhaust enthalpy to a shaft
    /// output. Net propulsive thrust ≈ 0 by design; the exhaust exits
    /// via a non-thrust-producing duct. Primary output is shaft power [W].
    /// Cycle solver: <c>TurboshaftCycleSolver</c> — structurally identical
    /// to <c>TurbopropCycleSolver</c> with fpe forced to 1.0 and
    /// propulsive nozzle thrust suppressed.
    /// Reference: GE T700-GE-701C (Black Hawk).
    /// </summary>
    Turboshaft = 10,

    /// <summary>
    /// Liquid Air Cycle Engine (LACE) — combined air-breathing / rocket
    /// hybrid. LH₂ propellant is used as a heat sink to cool and liquefy
    /// captured ambient air via a high-effectiveness counterflow precooler;
    /// liquid air + LH₂ then burn in a rocket-style chamber + nozzle. The
    /// hot LH₂ leaving the precooler feeds the chamber. Design point sits
    /// in the supersonic-aircraft / lower-stage launcher envelope
    /// (~Mach 4–6). Reference: RB-545 (Rolls-Royce / HOTOL 1980s precursor)
    /// at ~Mach 5 / 200 kN thrust; conceptual ancestor of Reaction Engines'
    /// SABRE precooler.
    /// Cycle solver: <c>LaceCycleSolver</c>. Design knobs:
    /// <see cref="AirbreathingEngineDesign.PrecoolerEffectiveness"/>,
    /// <see cref="AirbreathingEngineDesign.LH2MassFlow_kgs"/>,
    /// <see cref="AirbreathingEngineDesign.LaceChamberPressure_bar"/>,
    /// <see cref="AirbreathingEngineDesign.LaceAirToFuelRatio"/>.
    /// </summary>
    LiquidAirCycle = 11,

    /// <summary>
    /// Rotating Detonation Engine (RDE) — pressure-gain combustion via
    /// azimuthally-propagating detonation waves in an annular combustor.
    /// Combustion is Chapman-Jouguet detonation rather than constant-
    /// pressure deflagration; PGR (pressure-gain ratio) ≈ 1.10–1.30 yields
    /// 5–15 % Isp improvement over conventional Brayton at the same fuel-
    /// air ratio. References: AFRL test articles (Anand &amp; Gutmark 2019);
    /// Mitsubishi-Heavy-Industries IHI flight-tested RDE 2021;
    /// Pratt &amp; Whitney rotating-detonation rocket engine concepts.
    /// Cycle solver: <c>RotatingDetonationCycleSolver</c>. Design knobs:
    /// <see cref="AirbreathingEngineDesign.RdePressureGainRatio"/>,
    /// <see cref="AirbreathingEngineDesign.RdeWaveCount"/>,
    /// <see cref="AirbreathingEngineDesign.RdeAnnularOuterDiameter_m"/>,
    /// <see cref="AirbreathingEngineDesign.RdeAnnularInnerDiameter_m"/>,
    /// <see cref="AirbreathingEngineDesign.RdeAnnularLength_m"/>.
    /// </summary>
    RotatingDetonation = 12,
}
