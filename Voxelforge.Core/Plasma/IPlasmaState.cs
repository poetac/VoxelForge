// IPlasmaState.cs — plasma-state abstraction common to every plasma-variant
// electric-propulsion engine.
//
// Promoted from `Voxelforge.ElectricPropulsion.Core/Plasma/` to
// `Voxelforge.Core/Plasma/` per ADR-029a (rule-of-three met by HET +
// Arcjet + PPT). The interface contract is unchanged; only its namespace
// and assembly home moved. Cross-pillar consumers (e.g. nuclear-electric
// in a hypothetical Wave-3) can now reference IPlasmaState without an
// EP-pillar reference.
//
// Three properties are common to every plasma-variant engine:
//   • IonExitVelocity_ms          — characteristic accelerated-species velocity
//   • BeamCurrent_A               — useful (thrust-producing) ion current
//   • PlumeDivergenceHalfAngle_rad — geometric loss factor on the thrust vector
//
// Concrete implementations (HetPlasmaState, ArcjetPlasmaState, PptPlasmaState)
// extend this with variant-specific state (magnetic-field strength, sheath
// voltages, current-density tensors, impulse bits, etc.). Resistojet engines
// do not implement this interface — `ElectricPropulsionResult.PlasmaState`
// is null for resistojet runs.

namespace Voxelforge.Plasma;

/// <summary>
/// Plasma-state abstraction common to every Wave-2+ plasma-variant
/// electric-propulsion engine. See ADR-029 D1 + ADR-029a (promotion).
/// </summary>
public interface IPlasmaState
{
    /// <summary>
    /// Characteristic exit velocity of the accelerated species [m/s].
    /// For HET this is the singly-ionised xenon ion velocity at the
    /// channel exit; for arcjet the bulk plasma gas velocity; for PPT
    /// the time-averaged exit velocity of ablated PTFE; for ion engines
    /// the post-grid ion velocity.
    /// </summary>
    double IonExitVelocity_ms { get; }

    /// <summary>
    /// Useful beam current [A] — the fraction of discharge current that
    /// produces thrust, after subtracting electron-back-flow and
    /// charge-exchange losses. Variants with no continuous current path
    /// (e.g. PPT) carry this as zero.
    /// </summary>
    double BeamCurrent_A { get; }

    /// <summary>
    /// Plume divergence half-angle [rad]. The cosine of this angle scales
    /// the ideal axial thrust: T = T_ideal · cos(θ).
    /// </summary>
    double PlumeDivergenceHalfAngle_rad { get; }
}
