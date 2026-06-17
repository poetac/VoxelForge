// MpdPlasmaState.cs — concrete IPlasmaState for the Magnetoplasmadynamic
// (MPD) thruster.
//
// Sprint EP.W2.MPD (fifth IPlasmaState consumer after HET + Arcjet + PPT +
// GIT). Rule of three already fired in ADR-029a; the interface lives in
// Voxelforge.Core/Plasma/ and this concrete record stays pillar-local.
//
// MPD physics summary (self-field axisymmetric, lithium or argon):
//   • A high-current arc (kA-scale) flows radially from a central cathode
//     through partially-ionised plasma to a coaxial anode.
//   • The radial current self-induces an azimuthal magnetic field B_θ.
//   • The J × B Lorentz force is axial (J_r × B_θ → F_z), accelerating
//     plasma out the back of the thruster.
//   • Thrust scales as T = b · J² (Maecker formula) where b is a geometry-
//     coupled constant ≈ (μ₀/4π) · (ln(r_a / r_c) + 3/4).
//   • Specific impulse follows from T = ṁ · v_exit and Isp = v_exit / g₀.
//
// Differences from the HET / Arcjet / PPT / GIT records:
//   • Genuinely meaningful BeamCurrent_A is the arc current (J_arc) — the
//     dominant design knob (T ∝ J²).
//   • Adds DischargeVoltage_V, MagneticPressure_Pa (peak B²/2μ₀ at the
//     cathode tip), and ThrustCoefficient_NperA2 (Maecker-formula b)
//     so the gates can inspect the discharge state directly.
//   • CathodeWallTemp_K surfaces the dominant failure mode (cathode tip
//     erosion at sustained high current).

using Voxelforge.Plasma;

namespace Voxelforge.ElectricPropulsion.Plasma;

/// <summary>
/// Magnetoplasmadynamic thruster plasma-state snapshot. Populated by
/// <see cref="Solvers.SelfFieldLorentzModel"/> and stored on
/// <see cref="ElectricPropulsionResult.PlasmaState"/> when
/// <see cref="ElectricPropulsionEngineDesign.Kind"/> is
/// <see cref="ElectricPropulsionEngineKind.MagnetoPlasmaDynamic"/>.
/// </summary>
/// <param name="IonExitVelocity_ms">
/// Effective exit velocity v [m/s] = T / ṁ. Continuous; MPD is steady-state.
/// </param>
/// <param name="BeamCurrent_A">
/// Arc current J_arc [A] flowing from cathode to anode. The dominant design
/// knob (T ∝ J²); kA-scale for most published MPD designs (LiLFA cluster
/// 1–10 kA).
/// </param>
/// <param name="PlumeDivergenceHalfAngle_rad">
/// Plume half-angle θ [rad]. MPD plumes are wider than gridded-ion
/// (~30–45°) because the J×B acceleration is distributed across the
/// plasma volume rather than collimated by grid optics.
/// </param>
/// <param name="DischargeVoltage_V">
/// Arc terminal voltage V_arc [V]. Cluster envelope ~50–200 V at the
/// kA-scale current operating point.
/// </param>
/// <param name="ThrustCoefficient_NperA2">
/// Maecker-formula thrust coefficient b [N/A²] from
/// b = (μ₀ / 4π) · (ln(r_a / r_c) + 3/4) where r_a is the anode inner
/// radius and r_c the cathode outer radius. Geometry-only; carried so
/// the gate has the b factor data without re-deriving from design fields.
/// </param>
/// <param name="MagneticPressure_Pa">
/// Peak self-induced magnetic pressure B_peak² / (2 μ₀) [Pa] at the cathode
/// tip — characterises the J×B acceleration scale. Reaches ~10⁴–10⁶ Pa for
/// kA-scale currents.
/// </param>
/// <param name="CathodeWallTemp_K">
/// Cathode-tip surface temperature [K] — drives the dominant failure mode
/// (cathode erosion above the material's evaporation threshold). Limits:
/// W ~3700 K, Th-W ~3200 K.
/// </param>
/// <param name="ThrustEfficiency_Maecker">
/// Maecker-only thrust efficiency η_T = (½ ṁ v²) / (V_arc · J_arc). Cluster
/// 0.10–0.30 at the self-field kA scale (Polk 1991 LiLFA campaign).
/// </param>
public sealed record MpdPlasmaState(
    double IonExitVelocity_ms,
    double BeamCurrent_A,
    double PlumeDivergenceHalfAngle_rad,
    double DischargeVoltage_V,
    double ThrustCoefficient_NperA2,
    double MagneticPressure_Pa,
    double CathodeWallTemp_K,
    double ThrustEfficiency_Maecker) : IPlasmaState
{
    /// <summary>
    /// Applied-field solenoid B_z [T] in force at solve time. 0 for
    /// self-field-only (Wave-2) operation; finite &gt; 0 for Wave-3
    /// applied-field augmentation. Carried so gates can introspect the
    /// operating point directly without re-deriving from the design record.
    /// Wave-3 (Sprint EP.W3.AF).
    /// </summary>
    public double AppliedFieldStrength_T { get; init; }

    /// <summary>
    /// Applied-field thrust contribution T_af [N] from the Sankaran-2004
    /// fit T_af = k_af · J · B_applied · r_a. 0 when B_applied = 0;
    /// positive otherwise. Total thrust on
    /// <see cref="ElectricPropulsionResult.Thrust_N"/> is T_self + T_af.
    /// Wave-3 (Sprint EP.W3.AF).
    /// </summary>
    public double AppliedFieldThrust_N { get; init; }

    /// <summary>
    /// Self-field Maecker thrust T_self = b · J² [N]. Bit-identical to
    /// <see cref="ElectricPropulsionResult.Thrust_N"/> when
    /// <see cref="AppliedFieldStrength_T"/> = 0. Wave-3 (Sprint EP.W3.AF).
    /// </summary>
    public double SelfFieldThrust_N { get; init; }
}
