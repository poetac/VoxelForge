// TankageGeometryResult.cs — Sprint A.70 (C.2) result record returned by
// the cylindrical pressure-vessel voxel builder. Mirrors the pattern of
// FlywheelGeometryResult / ChamberGeometryResult: a small immutable summary
// of the built geometry plus the voxel handle for downstream STL / 3MF
// export.

namespace Voxelforge.Tankage;

/// <summary>
/// Geometry summary for a cylindrical pressure vessel built by
/// <see cref="TankageVoxelBuilder"/>. All dimensional fields are in
/// millimetres to match the PicoGK voxel-grid convention; the
/// <see cref="Voxels"/> handle wraps the underlying PicoGK voxel body so
/// callers can mesh / export without dragging PicoGK types into
/// <c>Voxelforge.Core</c>.
/// </summary>
/// <param name="OuterRadius_mm">R_outer [mm] — shell outer radius = InternalRadius + WallThickness.</param>
/// <param name="InternalRadius_mm">R [mm] — internal cavity radius (matches design.InternalRadius_m × 1000).</param>
/// <param name="WallThickness_mm">t [mm] — uniform shell-wall thickness (matches design.WallThickness_m × 1000).</param>
/// <param name="ShellLength_mm">L [mm] — cylindrical-section length (matches design.ShellLength_m × 1000).</param>
/// <param name="OverallLength_mm">Total axial extent [mm]: L (cylinder only) or L + 2·R_outer (with hemispherical end caps).</param>
/// <param name="HasEndCaps">True if the vessel has hemispherical end caps (matches design.HasHemisphericalEndCaps).</param>
/// <param name="Voxels">PicoGK voxel handle wrapping the built pressure-vessel shell body.</param>
internal sealed record TankageGeometryResult(
    double OuterRadius_mm,
    double InternalRadius_mm,
    double WallThickness_mm,
    double ShellLength_mm,
    double OverallLength_mm,
    bool   HasEndCaps,
    IVoxelHandle Voxels);
