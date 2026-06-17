// PulsejetGeometryResult.cs — output of PulsejetVoxelBuilder.Build
// (Wave 1 PR-5, sub-step 1a.5).
//
// Mirrors RamjetGeometryResult shape but drops contraction/expansion-
// ratio fields (no CD nozzle on a valveless pulsejet) and adds the
// pulsejet-specific intake / tailpipe / tube-length scalars the build-
// sheet + LPBF + acoustic-validation layers consume downstream.

using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Voxel + scalar metadata for a built valveless pulsejet shell.
/// </summary>
/// <param name="Voxels">
/// Opaque voxel handle for the printable shell (annular wall between gas
/// path and outer surface).
/// </param>
/// <param name="SolidVolume_mm3">Volume of solid material in the shell [mm³].</param>
/// <param name="InnerSurfaceArea_mm2">Gas-side wetted area [mm²] — analytical from the contour.</param>
/// <param name="WallThickness_mm">Uniform shell wall thickness [mm].</param>
/// <param name="TotalMass_g">Estimated mass [g] using a fixed 7.9 g/cm³ density.</param>
/// <param name="BoundingLength_mm">Voxel bounding-box length along the X axis [mm].</param>
/// <param name="BoundingDiameter_mm">Voxel bounding-box diameter (peak outer dimension) [mm].</param>
/// <param name="IntakeArea_mm2">Forward-firing diffuser intake area [mm²].</param>
/// <param name="TailpipeArea_mm2">Tailpipe exit area [mm²].</param>
/// <param name="TubeLength_mm">Total resonant tube length [mm] — drives Helmholtz frequency.</param>
/// <param name="Description">One-line human summary for logs / build sheets.</param>
/// <param name="Printability">
/// LPBF printability analysis result — null when
/// <c>RunLpbfAnalysis</c> is false or <c>LpbfMaterial</c> is null.
/// </param>
public sealed record PulsejetGeometryResult(
    IVoxelHandle Voxels,
    double SolidVolume_mm3,
    double InnerSurfaceArea_mm2,
    double WallThickness_mm,
    double TotalMass_g,
    double BoundingLength_mm,
    double BoundingDiameter_mm,
    double IntakeArea_mm2,
    double TailpipeArea_mm2,
    double TubeLength_mm,
    string Description,
    LpbfPrintabilityResult? Printability = null);
