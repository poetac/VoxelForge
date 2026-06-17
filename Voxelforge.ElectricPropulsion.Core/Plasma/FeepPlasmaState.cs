// FeepPlasmaState.cs — concrete IPlasmaState for the Field-Emission
// Electric Propulsion (FEEP) thruster.
//
// Sprint EP.W5 phase 2 (seventh IPlasmaState consumer after HET +
// Arcjet + PPT + GIT + MPD + Resistojet). Rule of three already fired
// in ADR-029a; the interface lives in Voxelforge.Core/Plasma/ and this
// concrete record stays pillar-local.
//
// FEEP physics summary (Mair-Lozano single-component model):
//   • A liquid-metal propellant (Indium or Cesium) wets a sharp emitter
//     tip held at +5..12 kV relative to an extractor electrode.
//   • The high tip field (E_tip ≈ α · V_acc / r_tip ≈ 1e9 V/m) overcomes
//     the work function and pulls ionised metal atoms / clusters / small
//     droplets from the liquid surface (Fowler-Nordheim emission cliff).
//   • The accelerated beam exits the extractor at v_exit set by energy
//     conservation against an EFFECTIVE ion mass m_eff = γ · m_ion_pure
//     where γ is the propellant-specific cluster factor (Indium γ ≈ 47
//     calibrated to IFM Nano; Cesium γ ≈ 5).
//   • Thrust T = ṁ · v_exit with ṁ = I_beam · m_eff / e (charge
//     conservation against the effective mass).
//   • The single-component model gives KINEMATIC Isp from the thrust-
//     bearing beam (typically ~1800 s for Indium at 9 kV). Marketing
//     "effective Isp" values (4000–6000 s for Indium-FEEP) reflect a
//     two-population beam (light ions + heavy clusters) — beyond Wave-3
//     scope; deferred to a future refinement.
//
// Differences from HET / Arcjet / PPT / GIT / MPD records:
//   • EmitterTipField_VperM surfaces the dominant emission-physics
//     observable (the FN cliff sits near 1e9 V/m).
//   • EffectiveIonMass_kg makes the calibration parameter visible to
//     gates so they can reason about beam composition without re-deriving.
//   • PropellantMaterial carries the FeepPropellant enum so gates and
//     reporting know whether the cluster anchor is Indium or Cesium.

using Voxelforge.Plasma;

namespace Voxelforge.ElectricPropulsion.Plasma;

/// <summary>
/// Field-Emission Electric Propulsion plasma-state snapshot. Populated
/// by <see cref="Solvers.MairLozanoEmitterModel"/> and stored on
/// <see cref="ElectricPropulsionResult.PlasmaState"/> when
/// <see cref="ElectricPropulsionEngineDesign.Kind"/> is
/// <see cref="ElectricPropulsionEngineKind.Feep"/>.
/// </summary>
/// <param name="IonExitVelocity_ms">
/// Effective exit velocity v [m/s] = T / ṁ. Continuous beam from the
/// liquid-metal emitter tip.
/// </param>
/// <param name="BeamCurrent_A">
/// Total emitter beam current I_beam [A]. Sub-mA per single emitter
/// tip; mA-class for emitter arrays. The dominant SA design knob.
/// </param>
/// <param name="PlumeDivergenceHalfAngle_rad">
/// Plume half-angle θ [rad]. FEEP plumes are narrow (~15°) because
/// the extractor electrode acts as a focusing element. Reference:
/// Mair 1996 single-tip Indium beam measurements.
/// </param>
/// <param name="AcceleratingVoltage_V">
/// Tip-to-extractor accelerating voltage V_acc [V]. Cluster envelope
/// 5–12 kV; the kinematic-energy source for the ions.
/// </param>
/// <param name="EmitterTipField_VperM">
/// Local electric field at the emitter tip E_tip [V/m] from
/// α · V_acc / r_tip with α ≈ 0.5 (geometry factor for sharp tungsten
/// cones). Above the Fowler-Nordheim threshold (~1e9 V/m) the emission
/// turns on; below it the current collapses.
/// </param>
/// <param name="EffectiveIonMass_kg">
/// Calibrated effective ion mass m_eff [kg] of the beam — the cluster-
/// factor product γ · m_ion_pure. Indium ≈ 47 · m_In, Cesium ≈ 5 · m_Cs.
/// Carried so gates can introspect the beam-composition assumption
/// directly without re-deriving from the propellant material.
/// </param>
/// <param name="PropellantMaterial">
/// Liquid-metal propellant. Determines m_ion_pure + work function in
/// the model branch; carried here so gates and reporting see the choice.
/// </param>
public sealed record FeepPlasmaState(
    double IonExitVelocity_ms,
    double BeamCurrent_A,
    double PlumeDivergenceHalfAngle_rad,
    double AcceleratingVoltage_V,
    double EmitterTipField_VperM,
    double EffectiveIonMass_kg,
    FeepPropellant PropellantMaterial) : IPlasmaState;
