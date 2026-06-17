// FlywheelGeometryResult.cs — Sprint A.67 (C.2) result record returned by
// the flywheel rotor voxel builder. Mirrors the pattern of
// ChamberGeometryResult: a small immutable summary of the built geometry
// plus the voxel handle for downstream STL / 3MF export.

namespace Voxelforge.Flywheel;

/// <summary>
/// Geometry summary for a flywheel rotor built by
/// <see cref="FlywheelVoxelBuilder"/>. All dimensional fields are in
/// millimetres to match the PicoGK voxel-grid convention; the
/// <see cref="Voxels"/> handle wraps the underlying PicoGK voxel body so
/// callers can mesh / export without dragging PicoGK types into
/// <c>Voxelforge.Core</c>.
/// </summary>
/// <param name="OuterRadius_mm">R_o [mm] — rotor outer radius (matches design.OuterRadius_m × 1000).</param>
/// <param name="InnerRadius_mm">R_i [mm] — rim inner radius. Equals 0 for SolidDisk; (1 − rimFraction) × R_o for ThinRim.</param>
/// <param name="AxialThickness_mm">t [mm] — axial extent of the rotor disc, sized so ρ · V = m_design.</param>
/// <param name="ShaftBoreRadius_mm">R_shaft [mm] — central hub-bore radius (0.05 × R_o, conservative).</param>
/// <param name="RimWallThickness_mm">R_o − R_i [mm] — radial rim wall thickness (= R_o for SolidDisk).</param>
/// <param name="Voxels">PicoGK voxel handle wrapping the built rotor body.</param>
internal sealed record FlywheelGeometryResult(
    double OuterRadius_mm,
    double InnerRadius_mm,
    double AxialThickness_mm,
    double ShaftBoreRadius_mm,
    double RimWallThickness_mm,
    IVoxelHandle Voxels);
