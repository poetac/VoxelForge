// PressureVesselResult.cs — Sprint TANK.W1 solver output.

namespace Voxelforge.Tankage;

/// <summary>
/// Solve-time outputs for a cylindrical pressure vessel snapshot
/// (Sprint TANK.W1).
/// </summary>
/// <param name="HoopStress_Pa">σ_hoop = P·R/t [Pa] (thin-wall) —
/// circumferential stress on the cylindrical shell.</param>
/// <param name="AxialStress_Pa">σ_axial = P·R/(2t) [Pa] — half the
/// hoop stress for an internally-pressurised thin-walled cylinder.</param>
/// <param name="VonMisesStress_Pa">σ_vm = √(σ_hoop² − σ_hoop·σ_axial
/// + σ_axial²) [Pa] — the equivalent uniaxial yield criterion.</param>
/// <param name="BurstPressure_Pa">P_burst = σ_yield · t / R [Pa] —
/// the pressure at which σ_hoop = σ_yield.</param>
/// <param name="SafetyFactor">SF = P_burst / P_operating [-]. Aerospace
/// rule of thumb 1.5-2.5; civil pressure vessels 4+; H₂ tanks 2.25 per
/// FMVSS 304.</param>
/// <param name="ShellMass_kg">Total shell mass [kg] = ρ · V_shell. Includes
/// hemispherical end caps when configured.</param>
/// <param name="InternalVolume_m3">π·R²·L cylindrical + (4/3)π·R³ end-cap
/// volume (if configured) [m³].</param>
/// <param name="GravimetricEfficiency">P·V / (m_shell · g₀) [m] —
/// pressure-volume-energy per unit shell weight, a figure of merit for
/// rocket / H₂ tankage. Higher = better mass-payload trade.</param>
internal sealed record PressureVesselResult(
    double HoopStress_Pa,
    double AxialStress_Pa,
    double VonMisesStress_Pa,
    double BurstPressure_Pa,
    double SafetyFactor,
    double ShellMass_kg,
    double InternalVolume_m3,
    double GravimetricEfficiency);
