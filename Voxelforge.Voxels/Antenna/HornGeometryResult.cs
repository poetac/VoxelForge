// HornGeometryResult.cs — Sprint ANT.W5-voxel result record for the
// horn antenna voxel builder. All dimensional fields are in mm.

namespace Voxelforge.Antenna;

/// <summary>
/// Geometry summary for a conical horn antenna built by
/// <see cref="AntennaVoxelBuilder.BuildHorn"/>. Boresight is +Z;
/// waveguide section at z &lt; 0; horn flares to +Z.
/// </summary>
/// <param name="ThroatDiameter_mm">D_throat [mm] — inner diameter at
/// the waveguide-to-horn transition (z = 0).</param>
/// <param name="ApertureDiameter_mm">D_aperture [mm] — aperture inner
/// diameter at the open end (z = HornLength_mm).</param>
/// <param name="HornLength_mm">L_horn [mm] — axial length of the
/// conical flare section (z ∈ [0, L_horn]).</param>
/// <param name="WaveguideLength_mm">L_wg [mm] — axial length of the
/// circular cylindrical waveguide section (z ∈ [−L_wg, 0]).</param>
/// <param name="WallThickness_mm">t_wall [mm] — wall thickness of both
/// the horn shell and the waveguide cylinder.</param>
/// <param name="FlareAngle_deg">θ_flare [°] — half-angle of the conical
/// flare = atan((R_aperture − R_throat) / L_horn).</param>
/// <param name="OverallAxialLength_mm">L_total [mm] — total axial extent
/// (= L_horn + L_wg).</param>
/// <param name="Voxels">PicoGK voxel handle for the complete horn +
/// waveguide assembly.</param>
internal sealed record HornGeometryResult(
    double ThroatDiameter_mm,
    double ApertureDiameter_mm,
    double HornLength_mm,
    double WaveguideLength_mm,
    double WallThickness_mm,
    double FlareAngle_deg,
    double OverallAxialLength_mm,
    IVoxelHandle Voxels) : IAntennaGeometryResult;
