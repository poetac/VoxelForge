// RamjetGeometryResult.cs — output of RamjetVoxelBuilder.Build.
//
// Mirrors the rocket-side ChamberGeometryResult shape but drops jacket /
// injector fields (no jacket, no injector in ramjet MVP) and adds
// air-breathing-specific scalars (throat area, contraction ratio,
// expansion ratio) the cycle solver + manufacturing layers consume
// downstream.

using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Voxel + scalar metadata for a built ramjet shell. The voxel handle
/// is opaque (<see cref="IVoxelHandle"/>); consumers in the air-breathing
/// pillar unwrap via the internal <c>AsPicoGK()</c> extension only when
/// they need to mesh / export.
/// </summary>
/// <param name="Voxels">
/// Opaque voxel handle for the printable shell (annular wall between gas
/// path and outer surface). Concrete type is
/// <c>Voxelforge.Airbreathing.PicoGKVoxelHandle</c>.
/// </param>
/// <param name="SolidVolume_mm3">
/// Volume of solid material in the shell [mm³]. Drives mass + cost
/// projections.
/// </param>
/// <param name="InnerSurfaceArea_mm2">
/// Gas-side wetted area [mm²] — analytical from the contour, not voxel-
/// surface-extracted. Used by the cycle solver's heat-loss path
/// (when wall-cooling work lands; today it's informational only).
/// </param>
/// <param name="WallThickness_mm">Uniform shell wall thickness [mm].</param>
/// <param name="TotalMass_g">
/// Estimated mass [g] using a fixed 7.9 g/cm³ density (300-series
/// stainless / Inconel typical). When the air-breathing material library
/// lands, switch to <c>LpbfMaterial</c>-derived density.
/// </param>
/// <param name="BoundingLength_mm">Voxel bounding-box length along the X axis [mm].</param>
/// <param name="BoundingDiameter_mm">Voxel bounding-box diameter (peak outer dimension) [mm].</param>
/// <param name="ThroatArea_mm2">Nozzle throat cross-sectional area [mm²].</param>
/// <param name="ContractionRatio">CombustorArea / ThroatArea (dimensionless).</param>
/// <param name="ExpansionRatio">ExitArea / ThroatArea (dimensionless).</param>
/// <param name="Description">One-line human summary for logs / build sheets.</param>
/// <param name="Printability">
/// LPBF printability analysis result — null when
/// <see cref="RamjetBuildOptions.RunLpbfAnalysis"/> is false or
/// <see cref="RamjetBuildOptions.LpbfMaterial"/> is null.
/// </param>
public sealed record RamjetGeometryResult(
    IVoxelHandle Voxels,
    double SolidVolume_mm3,
    double InnerSurfaceArea_mm2,
    double WallThickness_mm,
    double TotalMass_g,
    double BoundingLength_mm,
    double BoundingDiameter_mm,
    double ThroatArea_mm2,
    double ContractionRatio,
    double ExpansionRatio,
    string Description,
    LpbfPrintabilityResult? Printability = null);
