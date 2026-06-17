// IonPlasmaState.cs — concrete IPlasmaState for the Gridded-Ion Thruster.
//
// Sprint EP.W2.GIT (fourth IPlasmaState consumer after HET + Arcjet + PPT).
// Rule of three already fired in ADR-029a; the interface lives in
// Voxelforge.Core/Plasma/ and this concrete record stays pillar-local.
//
// Gridded-ion physics summary:
//   • Xenon (or other inert-gas) propellant is ionised in a discharge chamber.
//   • A biased two-grid optics system (screen grid at ~+1100 V, accelerator
//     grid at ~-200 V) extracts the ion beam with Child-Langmuir-limited
//     current density.
//   • A neutraliser cathode injects an equal current of electrons downstream
//     to maintain spacecraft potential.
//
// Differences from the HET / Arcjet / PPT records:
//   • Genuinely meaningful BeamCurrent_A (not a misnomer): the Child-Langmuir
//     beam-current limit is the binding design constraint.
//   • Adds AcceleratingVoltage_V (net beam energy = V_b for charge-singly-
//     ionised ions) and Perveance_AOverV1p5 (geometry-anchored beam-current
//     coefficient) so the perveance-saturation gate has data to inspect.
//   • Adds NeutralizerCurrent_A so the neutraliser-mismatch gate can compare
//     directly against BeamCurrent_A.

using Voxelforge.Plasma;

namespace Voxelforge.ElectricPropulsion.Plasma;

/// <summary>
/// Gridded-ion thruster plasma-state snapshot. Populated by
/// <see cref="Solvers.ChildLangmuirBeamModel"/> and stored on
/// <see cref="ElectricPropulsionResult.PlasmaState"/> when
/// <see cref="ElectricPropulsionEngineDesign.Kind"/> is
/// <see cref="ElectricPropulsionEngineKind.GriddedIon"/>.
/// </summary>
/// <param name="IonExitVelocity_ms">
/// Ion exit velocity [m/s] = √(2 · q · V_b / m_ion) where V_b is the net
/// beam voltage and m_ion is the singly-charged xenon ion mass. Continuous
/// — gridded-ion is steady-state.
/// </param>
/// <param name="BeamCurrent_A">
/// Extracted ion-beam current [A]. Limited by Child-Langmuir perveance for
/// the screen-grid geometry: J_max = (4/9) · ε₀ · √(2 q / m_ion) · V_b^1.5 / d².
/// Gridded-ion is the variant where this field is the binding design metric
/// (HET / Arcjet / PPT carry it as a misnomer or zero).
/// </param>
/// <param name="PlumeDivergenceHalfAngle_rad">
/// Beam-plume half-angle θ [rad] from the two-grid extraction optics. NSTAR
/// cluster value ≈ 20° (0.35 rad) for a well-tuned beam (Goebel & Katz §5).
/// </param>
/// <param name="AcceleratingVoltage_V">
/// Net accelerating voltage V_net = V_screen − V_plasma [V]. The kinetic
/// energy a singly-charged ion gains crossing the grids. For NSTAR
/// V_screen ≈ 1100 V, V_plasma ≈ 0 V → V_net ≈ 1100 V.
/// </param>
/// <param name="Perveance_AOverV1p5">
/// Geometric perveance P = J / V^1.5 [A / V^1.5] characterising the
/// screen-grid extraction optics. Beam current scales as J = P · V_b^1.5
/// up to the Child-Langmuir saturation limit (= space-charge perveance for
/// the screen-grid geometry).
/// </param>
/// <param name="NeutralizerCurrent_A">
/// Neutraliser-cathode emission current [A]. Must match
/// <paramref name="BeamCurrent_A"/> within ±10 % to avoid spacecraft
/// charge build-up. Drives the <c>GIT_NEUTRALIZER_CURRENT_MISMATCH</c>
/// gate.
/// </param>
/// <param name="ChildLangmuirLimit_A">
/// Child-Langmuir saturation beam-current limit for the screen-grid
/// geometry at <paramref name="AcceleratingVoltage_V"/> [A]. Gate fires
/// when the design <paramref name="BeamCurrent_A"/> exceeds this value
/// (perveance saturation; beam extraction collapses).
/// </param>
public sealed record IonPlasmaState(
    double IonExitVelocity_ms,
    double BeamCurrent_A,
    double PlumeDivergenceHalfAngle_rad,
    double AcceleratingVoltage_V,
    double Perveance_AOverV1p5,
    double NeutralizerCurrent_A,
    double ChildLangmuirLimit_A) : IPlasmaState;
