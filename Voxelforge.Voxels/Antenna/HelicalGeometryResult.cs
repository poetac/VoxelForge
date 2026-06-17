// HelicalGeometryResult.cs — Sprint ANT.W5-voxel result record for the
// helical antenna voxel builder. All dimensional fields are in mm.

namespace Voxelforge.Antenna;

/// <summary>
/// Geometry summary for a helical antenna built by
/// <see cref="AntennaVoxelBuilder.BuildHelical"/>. Helix axis is +Z
/// (boresight = end-fire direction); ground plane at z = 0.
/// </summary>
/// <param name="HelixRadius_mm">R_h [mm] — coil radius = C/(2π) where
/// C is the physical circumference in mm.</param>
/// <param name="TurnSpacing_mm">S [mm] — axial pitch between adjacent
/// turns (= S/λ × λ_mm).</param>
/// <param name="WireDiameter_mm">d_wire [mm] — wire cross-section
/// diameter (floored to the print-material minimum feature).</param>
/// <param name="GroundPlaneDiameter_mm">D_gp [mm] — ground-plane disc
/// diameter (≈ 1.5 λ; typical for end-fire helical antennas).</param>
/// <param name="TotalAxialLength_mm">L_helix [mm] — axial extent of the
/// helical coil alone (= N × TurnSpacing_mm).</param>
/// <param name="OverallAxialLength_mm">L_total [mm] — total axial extent
/// of the assembly including the ground-plane thickness.</param>
/// <param name="WireTooThinForMaterial">True when the computed wire
/// diameter was below the print-material minimum feature
/// (<see cref="AntennaConstraintIds.WireTooThin"/> gate). The builder
/// floors the wire to the material minimum; this flag records the
/// violation for downstream constraint reporting.</param>
/// <param name="Voxels">PicoGK voxel handle for the complete helix +
/// ground plane assembly.</param>
internal sealed record HelicalGeometryResult(
    double HelixRadius_mm,
    double TurnSpacing_mm,
    double WireDiameter_mm,
    double GroundPlaneDiameter_mm,
    double TotalAxialLength_mm,
    double OverallAxialLength_mm,
    bool   WireTooThinForMaterial,
    IVoxelHandle Voxels) : IAntennaGeometryResult;
