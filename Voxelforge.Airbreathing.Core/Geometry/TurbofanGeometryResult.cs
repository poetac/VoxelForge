// TurbofanGeometryResult.cs — output of TurbofanVoxelBuilder.Build.
//
// Sibling to RamjetGeometryResult. Carries scalars for both flow paths
// (core + bypass) so the cycle solver / mass / LPBF analysis layers can
// reason about each stream independently.

using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Voxel + scalar metadata for a built turbofan shell. Concrete voxel
/// type is <c>Voxelforge.Airbreathing.PicoGKVoxelHandle</c>.
/// </summary>
public sealed record TurbofanGeometryResult(
    IVoxelHandle Voxels,
    double SolidVolume_mm3,
    double InnerSurfaceArea_mm2,
    double WallThickness_mm,
    double BypassDuctWallThickness_mm,
    double TotalMass_g,
    double BoundingLength_mm,
    double BoundingDiameter_mm,
    double CoreThroatArea_mm2,
    double BypassExitArea_mm2,
    double BypassRatio,
    double ContractionRatio,
    double ExpansionRatio,
    string Description,
    LpbfPrintabilityResult? Printability = null);
