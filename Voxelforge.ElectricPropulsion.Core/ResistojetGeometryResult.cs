// ResistojetGeometryResult.cs — output of ResistojetVoxelBuilder.Build.
//
// Sibling to RamjetGeometryResult on the airbreathing side and
// ChamberGeometryResult on the rocket side. Voxel handle is opaque
// (IVoxelHandle) so Core stays free of PicoGK.

using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Voxel + scalar metadata for a built resistojet shell.
/// </summary>
/// <param name="Voxels">
/// Opaque voxel handle for the printable shell. Concrete type is
/// <c>Voxelforge.ElectricPropulsion.PicoGKVoxelHandle</c>.
/// </param>
/// <param name="SolidVolume_mm3">Volume of solid material in the shell [mm³].</param>
/// <param name="WallThickness_mm">Uniform shell wall thickness [mm].</param>
/// <param name="TotalMass_g">
/// Estimated mass [g] using a 8.6 g/cm³ niobium-class default density
/// (typical refractory-metal resistojet body). When the EP material
/// library lands, switch to <c>LpbfMaterial</c>-derived density.
/// </param>
/// <param name="BoundingLength_mm">Voxel bounding-box length along the X axis [mm].</param>
/// <param name="BoundingDiameter_mm">Voxel bounding-box diameter [mm].</param>
/// <param name="ThroatArea_mm2">Nozzle throat cross-sectional area [mm²].</param>
/// <param name="ExitArea_mm2">Nozzle exit cross-sectional area [mm²].</param>
/// <param name="AreaRatio">A_exit / A_throat (dimensionless).</param>
/// <param name="Description">One-line human summary for logs / build sheets.</param>
/// <param name="Printability">
/// LPBF printability analysis result. Null when the build was run
/// without a material profile.
/// </param>
public sealed record ResistojetGeometryResult(
    IVoxelHandle Voxels,
    double       SolidVolume_mm3,
    double       WallThickness_mm,
    double       TotalMass_g,
    double       BoundingLength_mm,
    double       BoundingDiameter_mm,
    double       ThroatArea_mm2,
    double       ExitArea_mm2,
    double       AreaRatio,
    string       Description,
    LpbfPrintabilityResult? Printability = null);
