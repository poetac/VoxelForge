// RbccOperatingMode.cs — operating-mode discriminator for the RBCC
// cycle solver (sub-step 1e).
//
// RBCC covers three bands of the flight envelope in a single engine
// family. The mode determines which sub-solver the RbccCycleSolver
// delegates to and which feasibility gates are active.

namespace Voxelforge.Airbreathing;

/// <summary>
/// Operating mode for a Rocket-Based Combined Cycle (RBCC) engine
/// design. Set on <see cref="AirbreathingEngineDesign.RbccMode"/>;
/// drives cycle-solver dispatch in <see cref="Cycles.RbccCycleSolver"/>.
/// </summary>
public enum RbccOperatingMode
{
    /// <summary>
    /// Ducted-rocket (ejector) mode. The rocket primary stream
    /// entrains atmospheric secondary air through an ejector duct,
    /// augmenting thrust at low flight Mach numbers (M ≤ 2.5).
    /// Phase 1 uses a simplified constant-entrainment-ratio model;
    /// full variable-geometry ejector model is a Stream B follow-on.
    /// </summary>
    DuctedRocket = 0,

    /// <summary>
    /// Pure ramjet mode. The rocket is inactive; inlet ram compression
    /// + subsonic combustion + nozzle expansion. Delegates to
    /// <see cref="Cycles.RamjetCycleSolver"/>. Appropriate for
    /// M ≈ 2–6.
    /// </summary>
    Ramjet = 1,

    /// <summary>
    /// Scramjet (supersonic combustion) mode. The rocket is inactive;
    /// oblique-shock inlet + supersonic combustion + expansion ramp.
    /// Delegates to <see cref="Cycles.ScramjetCycleSolver"/>. Appropriate
    /// for M ≥ 4.
    /// </summary>
    Scramjet = 2,
}
