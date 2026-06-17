// PptPlasmaState.cs — concrete IPlasmaState for the Pulsed Plasma Thruster.
//
// Wave-2 third IPlasmaState consumer (after HET + Arcjet) — the rule-of-three
// watch fires per ADR-029 D1 + ADR-029a. The interface itself moved to
// Voxelforge.Core/Plasma/; this concrete record stays pillar-local because
// PPT-specific state (impulse bit, mass-per-pulse, capacitor energy) has no
// cross-pillar consumers.
//
// PPT differs from HET / Arcjet in two important ways:
//   • No continuous beam current — discharge is impulsive. BeamCurrent_A is
//     carried as 0.0 to honour the IPlasmaState contract while flagging
//     that the variant has no continuous-current path. The interface's
//     "BeamCurrent_A" naming is a misnomer here (kept for cross-variant
//     compatibility per ADR-029 D1).
//   • No anode wall temperature — short-pulse discharges (~µs) don't reach
//     thermal steady state. The "ChamberTemp_K" reported on the result is
//     NaN for PPT.
//
// Plume divergence is dominated by the parallel-rail electrode gap and
// ablation-vapour expansion (Spanjers et al., AIAA-2002-3974); the model
// holds it as a cluster anchor rather than a geometric scaling.

using Voxelforge.Plasma;

namespace Voxelforge.ElectricPropulsion.Plasma;

/// <summary>
/// PPT plasma-state snapshot. Populated by
/// <see cref="Solvers.AblationDischargeModel"/> and stored on
/// <see cref="ElectricPropulsionResult.PlasmaState"/> when
/// <see cref="ElectricPropulsionEngineDesign.Kind"/> is
/// <see cref="ElectricPropulsionEngineKind.PulsedPlasmaThruster"/>.
/// </summary>
/// <param name="IonExitVelocity_ms">
/// Effective exit-vapour velocity [m/s] = I_bit / Δm. "Ion" terminology
/// in <see cref="IPlasmaState"/> is a misnomer here — PPT exhaust is a
/// partially-ionised PTFE-vapour plasma; the bulk vapour velocity is what
/// produces thrust. Reused field for cross-variant compatibility.
/// </param>
/// <param name="BeamCurrent_A">
/// Carried as 0.0. PPT impulse comes from pulsed Lorentz acceleration of
/// ablated PTFE; there is no continuous current path. Exposed only to
/// honour the <see cref="IPlasmaState"/> interface contract.
/// </param>
/// <param name="PlumeDivergenceHalfAngle_rad">
/// Plume half-angle θ [rad] from the parallel-rail discharge geometry.
/// Typical 15–30° for low-energy PPTs; cos(θ) is the thrust-correction factor.
/// </param>
/// <param name="ImpulseBit_Ns">Impulse bit per pulse I_bit [N·s] — Solbes-Vondra fit.</param>
/// <param name="MassPerPulse_kg">PTFE ablated per pulse Δm [kg] — Solbes-Vondra fit.</param>
/// <param name="PulseFrequency_Hz">Pulse repetition frequency f [Hz] (input).</param>
/// <param name="CapacitorEnergy_J">Capacitor energy per pulse E_cap [J] (input).</param>
/// <param name="AveragePower_W">E_cap · f_pulse [W] — gross average bus power, ignoring PPU losses.</param>
public sealed record PptPlasmaState(
    double IonExitVelocity_ms,
    double BeamCurrent_A,
    double PlumeDivergenceHalfAngle_rad,
    double ImpulseBit_Ns,
    double MassPerPulse_kg,
    double PulseFrequency_Hz,
    double CapacitorEnergy_J,
    double AveragePower_W) : IPlasmaState;
