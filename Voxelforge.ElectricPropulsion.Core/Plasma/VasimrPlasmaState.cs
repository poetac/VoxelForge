// VasimrPlasmaState.cs — concrete IPlasmaState for the Variable
// Specific Impulse Magnetoplasma Rocket (VASIMR) thruster.
//
// Sprint EP.W4 phase 2 (ninth IPlasmaState consumer). The interface
// lives in Voxelforge.Core/Plasma/ per ADR-029a.
//
// VASIMR physics summary (Chang Diaz / Ad Astra Rocket model):
//   • Helicon RF source ionises argon in the source chamber. Cluster
//     η_i ≈ 0.9–0.99 because helicon coupling at VX-200i power scales
//     is highly efficient (Chen 1991; Bering 2010).
//   • ICRH (Ion-Cyclotron Resonance Heating) stage heats perpendicular
//     ion temperature: at ω = qB/m the RF deposits energy preferentially
//     in the ion population. Energy per ion = P_icrh / (η_i · ṁ / m_Ar).
//   • Magnetic nozzle: ions flow through a B-field gradient (mirror
//     ratio M = B_source / B_throat). Adiabatic-invariant μ = m·v_⊥²/(2B)
//     converts T_perp → T_parallel as B drops. Nozzle efficiency
//     η_nozzle ≈ 1 - 1/M.
//   • Directed exit velocity: v_eff = √(2·η_nozzle·E_per_ion·e/m_Ar).
//   • Variable specific impulse: by trading P_helicon vs P_icrh at
//     fixed total power, the engine sweeps Isp from ~1500 s (high-thrust
//     mode, low T_perp, high ṁ_ion) to ~30000 s (low-thrust mode,
//     high T_perp, low ṁ_ion). VX-200i nominal: 5000 s @ 5 N / 200 kW.
//
// Differences from FEEP / HET / MPD / HDLT records:
//   • IonTemperature_eV is the defining VASIMR observable — proxy for
//     T_perp set by ICRH heating efficiency.
//   • MagneticMirrorRatio captures the nozzle-conversion physics
//     directly. Gates check it for inversion / minimum threshold.
//   • IonisationFraction (η_i) carried from the helicon stage so
//     downstream gates can introspect.

using Voxelforge.Plasma;

namespace Voxelforge.ElectricPropulsion.Plasma;

/// <summary>
/// VASIMR (Variable Specific Impulse Magnetoplasma Rocket) plasma-
/// state snapshot. Populated by <see cref="Solvers.HeliconIcrhMagneticNozzleModel"/>
/// and stored on <see cref="ElectricPropulsionResult.PlasmaState"/>
/// when <see cref="ElectricPropulsionEngineDesign.Kind"/> is
/// <see cref="ElectricPropulsionEngineKind.Vasimr"/>.
/// </summary>
/// <param name="IonExitVelocity_ms">
/// Effective directed exit velocity v [m/s] after magnetic-nozzle
/// expansion. Equivalent to Isp·g₀.
/// </param>
/// <param name="BeamCurrent_A">
/// Equivalent beam current I [A] = η_i · ṁ_total · e / m_Ar. Ion
/// saturation current at the magnetic-nozzle throat.
/// </param>
/// <param name="PlumeDivergenceHalfAngle_rad">
/// Plume half-angle θ [rad] from the magnetic-nozzle expansion ratio.
/// Cluster ~ 15–25° for typical mirror ratios.
/// </param>
/// <param name="IonTemperature_eV">
/// ICRH-deposited ion temperature T_⊥ [eV] before the magnetic-nozzle
/// thermal-to-directed conversion. The VASIMR variable-Isp lever.
/// </param>
/// <param name="MagneticMirrorRatio">
/// M = B_source / B_throat [-]. Set by the solenoid B_z + nozzle
/// geometry. Drives the η_nozzle = 1 − 1/M conversion.
/// </param>
/// <param name="IonisationFraction">
/// η_i [-] — fraction of inlet argon flow ionised by the helicon
/// source. Cluster 0.9-0.99 at VASIMR operating points.
/// </param>
/// <param name="NozzleConversionEfficiency">
/// η_nozzle [-] = 1 − 1/M. Fraction of T_⊥ converted to directed
/// kinetic via adiabatic mirror reflection (Chen 2010).
/// </param>
public sealed record VasimrPlasmaState(
    double IonExitVelocity_ms,
    double BeamCurrent_A,
    double PlumeDivergenceHalfAngle_rad,
    double IonTemperature_eV,
    double MagneticMirrorRatio,
    double IonisationFraction,
    double NozzleConversionEfficiency) : IPlasmaState;
