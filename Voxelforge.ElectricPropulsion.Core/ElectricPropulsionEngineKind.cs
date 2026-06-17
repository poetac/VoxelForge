// ElectricPropulsionEngineKind.cs — discriminator for the
// electric-propulsion pillar.
//
// Parallel to AirbreathingEngineKind on the airbreathing side and
// ChannelTopology / EngineCycle on the rocket side. Wave-1 ships
// `Resistojet` only; HET / MPD / GriddedIon / Arcjet are reserved
// here as enum values but their cycle-solver dispatch throws
// NotSupportedException until the Wave-2 plasma-state audit ships
// (ADR-026 §6).

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Kind of electric-propulsion engine. Discriminator for
/// <see cref="ElectricPropulsionEngineDesign"/> + the future cycle-solver
/// registry (post-Wave-1).
/// </summary>
/// <remarks>
/// Wave-1 ships <see cref="Resistojet"/>. Reserved values
/// (<see cref="Arcjet"/>, <see cref="HallEffect"/>, <see cref="GriddedIon"/>,
/// <see cref="MagnetoPlasmaDynamic"/>) are enum slots only — instantiating a
/// design with those kinds throws at the dispatch site until Wave-2's
/// plasma-state audit defines the contract.
/// </remarks>
public enum ElectricPropulsionEngineKind
{
    /// <summary>
    /// Sentinel — not a real engine. Records use it as the default value
    /// so an uninitialised design throws cleanly at dispatch rather than
    /// silently falling through.
    /// </summary>
    None = 0,

    /// <summary>
    /// Resistojet — electrothermal heating of catalyst-decomposed propellant
    /// through a resistively-heated chamber, accelerated through a conical
    /// CD nozzle. Wave-1 (this PR).
    /// </summary>
    Resistojet = 1,

    /// <summary>
    /// Arcjet — distributed-arc electrothermal heating. Higher power density
    /// than resistojet; needs plasma physics. Wave-2, gated by the Team-P
    /// plasma-state audit (ADR-026 §6).
    /// </summary>
    Arcjet = 2,

    /// <summary>
    /// Hall-effect thruster (HET) — crossed-field plasma electrostatic
    /// acceleration. Wave-2; reserved.
    /// </summary>
    HallEffect = 3,

    /// <summary>
    /// Gridded-ion thruster — Child-Langmuir-limited ion extraction across
    /// biased grids. Wave-2; reserved.
    /// </summary>
    GriddedIon = 4,

    /// <summary>
    /// Magnetoplasmadynamic (MPD) thruster — Lorentz-force acceleration of
    /// partially ionized plasma. Wave-2; reserved.
    /// </summary>
    MagnetoPlasmaDynamic = 5,

    /// <summary>
    /// Pulsed Plasma Thruster (PPT) — capacitor-discharge ablation of solid
    /// PTFE between parallel-rail electrodes. Time-averaged thrust = I_bit · f_pulse.
    /// Wave-2 (Sprint EP.W2.PPT) — third <c>IPlasmaState</c> consumer; rule of
    /// three met (ADR-029a).
    /// </summary>
    PulsedPlasmaThruster = 6,

    /// <summary>
    /// Variable Specific Impulse Magnetoplasma Rocket (VASIMR) — RF-heated
    /// magnetized plasma with helicon-source ionization stage + ion-cyclotron-
    /// resonance heating stage + magnetic-nozzle expansion. Wave-3 (deferred,
    /// see Sprint EP.W4); reserved enum slot only. Genuinely different physics
    /// from Wave-2 variants (variable Isp at fixed power, hot-ion magnetic
    /// expansion). Reference: Chang Diaz, Squire, et al. (Ad Astra Rocket VX-200).
    /// </summary>
    /// <remarks>
    /// Wave-4 (ADR-032 follow-on candidate). Enum slot reserved to keep schema
    /// EP v7 forward-compatible — designs that record <c>Kind = Vasimr</c>
    /// today can be round-tripped through migration even though the physics
    /// path throws on dispatch.
    /// </remarks>
    Vasimr = 7,

    /// <summary>
    /// Field-Emission Electric Propulsion (FEEP) — liquid-metal (typically
    /// indium or cesium) field-emission ion thruster. Single-stage
    /// electrostatic acceleration from a sharp emitter tip biased to
    /// 5-10 kV. Sub-mN thrust, very-high Isp (4 000–8 000 s). Wave-3
    /// (deferred, see Sprint EP.W5 phase 1 scaffold + phase 2 physics);
    /// reserved enum slot. Reference: Mair G., Lozano P. (Indium-FEEP
    /// development at MIT / TUI / Austrian Research Centers).
    /// </summary>
    /// <remarks>
    /// Wave-3 (Sprint EP.W5 phase 1 scaffold). Enum slot reserved per
    /// ADR-034 D1 to keep schema forward-compatible — designs that
    /// record <c>Kind = Feep</c> today can be round-tripped through
    /// migration even though the physics path throws on dispatch
    /// pending Sprint EP.W5 phase 2.
    /// </remarks>
    Feep = 8,

    /// <summary>
    /// Helicon Double-Layer Thruster (HDLT) — RF-driven helicon plasma
    /// source with a self-forming current-free electrostatic double-layer
    /// that accelerates ions out the back. No biased grids, no
    /// neutralizer cathode (current-free). Discovered by Charles &amp;
    /// Boswell at ANU (2003). Wave-3 (deferred, Sprint EP.W6 phase 1
    /// scaffold + phase 2 physics); reserved enum slot per ADR-034 D1.
    /// </summary>
    /// <remarks>
    /// Wave-3 (Sprint EP.W6 phase 1 scaffold). The physics dispatch
    /// throws on <c>Kind = Hdlt</c> until the phase-2 helicon + double-
    /// layer solver ships. References: Charles C., Boswell R.W. (2003)
    /// "Current-free double-layer formation in a high-density helicon
    /// discharge." Appl. Phys. Lett. 82(9); Plihon N., Chabert P.,
    /// Corr C.S. (2007) "Experimental investigation of double layers."
    /// </remarks>
    Hdlt = 9,
}
