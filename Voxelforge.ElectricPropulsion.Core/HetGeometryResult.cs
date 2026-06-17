// HetGeometryResult.cs — output of HetEnvelopeBuilder.Build.
//
// Sibling to ResistojetGeometryResult on the same pillar. Voxel handle
// is opaque (IVoxelHandle) so Core stays free of PicoGK.

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Voxel + scalar metadata for a built Hall-Effect Thruster shell.
/// </summary>
/// <param name="Voxels">
/// Opaque voxel handle for the printable HET body (annular channel +
/// integrated magnetic-shroud ring + cathode post). Concrete type is
/// <c>Voxelforge.ElectricPropulsion.PicoGKVoxelHandle</c>.
/// </param>
/// <param name="SolidVolume_mm3">Volume of solid material in the shell [mm³].</param>
/// <param name="WallThickness_mm">Annular outer wall thickness [mm].</param>
/// <param name="TotalMass_g">
/// Estimated mass [g] using a 8.6 g/cm³ niobium-class default density (consistent with
/// resistojet builder).
/// </param>
/// <param name="BoundingLength_mm">Voxel bounding-box length along the X axis [mm].</param>
/// <param name="BoundingDiameter_mm">Voxel bounding-box outer diameter [mm].</param>
/// <param name="ChannelInnerRadius_mm">Inner radius of the discharge channel [mm].</param>
/// <param name="ChannelOuterRadius_mm">Outer radius of the discharge channel (= AnodeRadius_mm) [mm].</param>
/// <param name="ChannelWidth_mm">Outer minus inner channel radius [mm].</param>
/// <param name="CathodePostLength_mm">Cathode post axial length [mm].</param>
/// <param name="Description">One-line human summary for logs / build sheets.</param>
public sealed record HetGeometryResult(
    IVoxelHandle Voxels,
    double       SolidVolume_mm3,
    double       WallThickness_mm,
    double       TotalMass_g,
    double       BoundingLength_mm,
    double       BoundingDiameter_mm,
    double       ChannelInnerRadius_mm,
    double       ChannelOuterRadius_mm,
    double       ChannelWidth_mm,
    double       CathodePostLength_mm,
    string       Description);
