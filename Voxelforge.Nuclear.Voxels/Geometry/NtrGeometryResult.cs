// NtrGeometryResult.cs — output of NtrChamberVoxelBuilder.Build.

using Voxelforge;
//
// Sibling to MarineHullGeometryResult (marine pillar) and
// ResistojetGeometryResult (electric-propulsion pillar).
// Voxel handle is opaque (IVoxelHandle) so Nuclear.Core stays free of PicoGK.

namespace Voxelforge.Nuclear.Geometry;

/// <summary>
/// Voxel + scalar metadata for a built NTR nozzle + stub core assembly.
/// </summary>
/// <param name="Voxels">
/// Opaque voxel handle for the printable shell. Concrete type is
/// <c>Voxelforge.Nuclear.PicoGKVoxelHandle</c>.
/// </param>
/// <param name="SolidVolume_mm3">Volume of solid material [mm³].</param>
/// <param name="TotalMass_g">Estimated mass [g] (Inconel 718 density: 8.22 g/cm³).</param>
/// <param name="NozzleLength_mm">Nozzle axial length from throat to exit [mm].</param>
/// <param name="BoundingDiameter_mm">Bounding-box diameter [mm].</param>
/// <param name="ThroatRadius_mm">Nozzle throat radius [mm].</param>
/// <param name="ExitRadius_mm">Nozzle exit radius [mm].</param>
/// <param name="ExpansionRatio">A_exit / A_throat [-].</param>
/// <param name="Description">One-line human summary for logs / build sheets.</param>
public sealed record NtrGeometryResult(
    IVoxelHandle Voxels,
    double       SolidVolume_mm3,
    double       TotalMass_g,
    double       NozzleLength_mm,
    double       BoundingDiameter_mm,
    double       ThroatRadius_mm,
    double       ExitRadius_mm,
    double       ExpansionRatio,
    string       Description);
