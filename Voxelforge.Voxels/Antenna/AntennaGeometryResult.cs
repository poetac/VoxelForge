// AntennaGeometryResult.cs — Sprint A.83 (C.2) result record returned by
// the parabolic-dish + feed voxel builder. Mirrors the pattern of
// FlywheelGeometryResult / TankageGeometryResult: a small immutable
// summary of the built geometry plus the voxel handle for downstream
// STL / 3MF export.

namespace Voxelforge.Antenna;

/// <summary>
/// Geometry summary for a parabolic-dish antenna built by
/// <see cref="AntennaVoxelBuilder"/>. All dimensional fields are in
/// millimetres to match the PicoGK voxel-grid convention; the
/// <see cref="Voxels"/> handle wraps the underlying PicoGK voxel body so
/// callers can mesh / export without dragging PicoGK types into
/// <c>Voxelforge.Core</c>.
/// </summary>
/// <param name="DishDiameter_mm">D [mm] — full aperture diameter
/// (matches design.TransmitDishDiameter_m × 1000 for a Tx-side build).
/// </param>
/// <param name="FocalLength_mm">F [mm] — focal length of the paraboloid
/// (vertex-to-focus distance along the boresight axis). Derived from the
/// f/D ratio anchor (<see cref="AntennaVoxelBuilder.DefaultFocalToDiameterRatio"/>).
/// </param>
/// <param name="DishDepth_mm">Depth [mm] of the dish from vertex to rim
/// plane — for a paraboloid z = r²/(4F), depth = (D/2)²/(4F).</param>
/// <param name="ReflectorWallThickness_mm">t [mm] — reflector shell-wall
/// thickness. The reflector is an open-front shell that approximates a
/// real spun-aluminium dish (typically 1-3 mm wall for ground-station
/// dishes).</param>
/// <param name="FeedRadius_mm">r_feed [mm] — radius of the cylindrical
/// feed envelope at the focal point.</param>
/// <param name="FeedLength_mm">L_feed [mm] — axial length of the
/// cylindrical feed envelope along the boresight axis.</param>
/// <param name="OverallAxialLength_mm">Total axial extent [mm] of the
/// assembled antenna (vertex of dish to feed tip on the +Z side).</param>
/// <param name="Voxels">PicoGK voxel handle wrapping the built dish +
/// feed body.</param>
internal sealed record AntennaGeometryResult(
    double DishDiameter_mm,
    double FocalLength_mm,
    double DishDepth_mm,
    double ReflectorWallThickness_mm,
    double FeedRadius_mm,
    double FeedLength_mm,
    double OverallAxialLength_mm,
    IVoxelHandle Voxels) : IAntennaGeometryResult;
