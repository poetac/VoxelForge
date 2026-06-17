// ArcjetPlasmaState.cs — concrete IPlasmaState for the Arcjet variant.
//
// Wave-2 second IPlasmaState consumer per ADR-029 (HET first; this PR adds
// Arcjet — the rule-of-three watch becomes "1 more variant promotes the
// abstraction to Voxelforge.Core/Plasma/").
//
// Arcjet plasma state differs from HET in two important ways:
//   • There is no magnetic field — the arc is electrothermal-with-plasma,
//     not crossed-field acceleration. The Hall parameter ω_e·τ_en is < 1.
//   • The "useful beam current" maps to the discharge current itself, not
//     a fraction of it (no electron back-flow path through a separate
//     downstream cathode); BeamCurrent_A = I_arc, with no η_t multiplier.
//
// Plume divergence is dominated by the conical nozzle expansion + arc
// constriction profile rather than B-field geometry, so the half-angle is
// a static property of the design (computed by Maecker-Kovitya from the
// ratio of arc-column radius to nozzle exit radius).

using Voxelforge.Plasma;

namespace Voxelforge.ElectricPropulsion.Plasma;

/// <summary>
/// Arcjet plasma-state snapshot. Populated by
/// <see cref="Solvers.MaeckerKovityaArcModel"/> and stored on
/// <see cref="ElectricPropulsionResult.PlasmaState"/> when
/// <see cref="ElectricPropulsionEngineDesign.Kind"/> is
/// <see cref="ElectricPropulsionEngineKind.Arcjet"/>.
/// </summary>
/// <param name="IonExitVelocity_ms">
/// Effective exit-gas velocity [m/s] = √(2·η_thermal·V_arc·I_arc / ṁ).
/// "Ion" terminology in <see cref="IPlasmaState"/> is a misnomer here —
/// arcjet exhaust is a partially-ionised plasma; the bulk gas velocity
/// is what produces thrust. Reused field for cross-variant compatibility.
/// </param>
/// <param name="BeamCurrent_A">
/// Discharge current [A]. Unlike HET, all of I_arc flows through the
/// useful path (no separate neutraliser cathode), so BeamCurrent_A == I_arc.
/// </param>
/// <param name="PlumeDivergenceHalfAngle_rad">
/// Plume half-angle θ [rad] from nozzle expansion + arc-column geometry.
/// Typical 15-25° for low-power arcjets; cos(θ) is the thrust-correction factor.
/// </param>
/// <param name="ArcVoltage_V">Arc terminal voltage V_arc [V] (input).</param>
/// <param name="ArcCurrent_A">Arc current I_arc [A] (input).</param>
/// <param name="ThermalEfficiency">
/// η_thermal — fraction of P_arc = V_arc · I_arc deposited as bulk-gas
/// enthalpy. Goebel &amp; Katz §4 reports 0.30-0.50 across the cluster.
/// </param>
/// <param name="ArcPower_W">V_arc × I_arc [W] — gross arc power, ignoring PPU losses.</param>
/// <param name="AnodeWallTemp_K">
/// Steady-state anode (downstream electrode) wall temperature [K] from the
/// radiative balance P_anode = ε·σ·A·(T_w⁴ − T_∞⁴).
/// </param>
public sealed record ArcjetPlasmaState(
    double IonExitVelocity_ms,
    double BeamCurrent_A,
    double PlumeDivergenceHalfAngle_rad,
    double ArcVoltage_V,
    double ArcCurrent_A,
    double ThermalEfficiency,
    double ArcPower_W,
    double AnodeWallTemp_K) : IPlasmaState;
