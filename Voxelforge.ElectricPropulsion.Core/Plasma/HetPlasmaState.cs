// HetPlasmaState.cs — concrete IPlasmaState for the Hall-Effect Thruster.
//
// Wave-2 first plasma variant per ADR-029. Carries the Busch-discharge-
// model outputs that the gates and report consume.

using Voxelforge.Plasma;

namespace Voxelforge.ElectricPropulsion.Plasma;

/// <summary>
/// HET plasma-state snapshot. Populated by <see cref="Solvers.BuschDischargeModel"/>
/// and stored on <see cref="ElectricPropulsionResult.PlasmaState"/> when
/// <see cref="ElectricPropulsionEngineDesign.Kind"/> is
/// <see cref="ElectricPropulsionEngineKind.HallEffect"/>.
/// </summary>
/// <param name="IonExitVelocity_ms">
/// Singly-ionised xenon exit velocity [m/s] from
/// v_i = √(2·e·V_d / m_xe) · √η_b.
/// </param>
/// <param name="BeamCurrent_A">
/// Useful beam current [A] = I_d · η_t (the thrust-producing fraction).
/// </param>
/// <param name="PlumeDivergenceHalfAngle_rad">
/// Plume half-angle θ [rad] from arctan(K_div / B). cos(θ) is the
/// thrust-correction factor.
/// </param>
/// <param name="MagneticField_T">
/// Peak radial magnetic-field strength in the discharge channel [T].
/// </param>
/// <param name="MassUtilization">
/// η_m — fraction of injected propellant that ionises.
/// </param>
/// <param name="BeamEfficiency">
/// η_b — fraction of beam current that exits at full V_d (vs back-flow
/// + charge-exchange losses).
/// </param>
/// <param name="DischargePower_W">
/// V_d × I_d [W] — gross discharge power, ignoring PPU losses.
/// </param>
public sealed record HetPlasmaState(
    double IonExitVelocity_ms,
    double BeamCurrent_A,
    double PlumeDivergenceHalfAngle_rad,
    double MagneticField_T,
    double MassUtilization,
    double BeamEfficiency,
    double DischargePower_W) : IPlasmaState;
