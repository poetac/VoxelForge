// HdltPlasmaState.cs — concrete IPlasmaState for the Helicon Double-
// Layer Thruster.
//
// Sprint EP.W6 phase 2 (eighth IPlasmaState consumer after HET +
// Arcjet + PPT + GIT + MPD + Resistojet + FEEP). The interface lives
// in Voxelforge.Core/Plasma/ per ADR-029a; this concrete record stays
// pillar-local.
//
// HDLT physics summary (Charles-Boswell single-fluid model):
//   • A helicon RF source ionises argon to plasma density n_e in the
//     source chamber at high B-field uniformity.
//   • Where the magnetic-flux tube expands (B drops), the plasma
//     density abruptly drops and a current-free electrostatic double
//     layer (CFDL) self-organises across the expansion.
//   • DL strength: e·ΔV ≈ k_DL · T_e · ln(B_source / B_throat)
//     (Charles-Boswell 2003 scaling).
//   • Ions falling through the DL gain v_ion = √(2 e ΔV / m_Ar). The
//     beam exits without grids or a neutralizer cathode — electrons
//     that form the high-potential side eventually exit too, keeping
//     the spacecraft current-balanced.
//   • Real HDLT ionises only a small fraction η_i of the inlet flow;
//     the un-ionised neutral fraction exits at thermal velocity and
//     contributes negligibly to thrust.
//
// Differences from FEEP / HET / MPD records:
//   • DoubleLayerStrength_V is THE characteristic HDLT observable;
//     gates check it for formation threshold.
//   • IonisationFraction is the calibration parameter visible to
//     reporting and downstream consumers.

using Voxelforge.Plasma;

namespace Voxelforge.ElectricPropulsion.Plasma;

/// <summary>
/// Helicon Double-Layer Thruster plasma-state snapshot. Populated by
/// <see cref="Solvers.HeliconDoubleLayerModel"/> and stored on
/// <see cref="ElectricPropulsionResult.PlasmaState"/> when
/// <see cref="ElectricPropulsionEngineDesign.Kind"/> is
/// <see cref="ElectricPropulsionEngineKind.Hdlt"/>.
/// </summary>
/// <param name="IonExitVelocity_ms">
/// Effective ion exit velocity v [m/s] from energy conservation
/// through the double-layer: v = √(2 e ΔV / m_Ar).
/// </param>
/// <param name="BeamCurrent_A">
/// Equivalent beam current I [A] = η_i · ṁ_total · e / m_Ar — the
/// ionised-fraction flux at the DL throat in charge units.
/// </param>
/// <param name="PlumeDivergenceHalfAngle_rad">
/// Plume half-angle θ [rad]. HDLT plumes are wide (~25-30°) because
/// the magnetic nozzle downstream of the DL is weaker than gridded
/// or magnetic-mirror-collimated designs (Plihon 2007).
/// </param>
/// <param name="DoubleLayerStrength_V">
/// CFDL potential drop ΔV [V] = k_DL · T_e · ln(B_source / B_throat).
/// The defining HDLT observable; gates check for formation threshold.
/// </param>
/// <param name="ElectronTemperature_eV">
/// Bulk electron temperature T_e [eV] in the helicon source.
/// Cluster envelope 3-10 eV (Chen 1991, Plihon 2007).
/// </param>
/// <param name="IonisationFraction">
/// Fraction η_i [-] of the inlet argon flow that ionises and
/// participates in DL acceleration. Cluster mid-band 0.02-0.10;
/// the model uses calibration anchored to Charles-Boswell ANU data.
/// </param>
public sealed record HdltPlasmaState(
    double IonExitVelocity_ms,
    double BeamCurrent_A,
    double PlumeDivergenceHalfAngle_rad,
    double DoubleLayerStrength_V,
    double ElectronTemperature_eV,
    double IonisationFraction) : IPlasmaState;
