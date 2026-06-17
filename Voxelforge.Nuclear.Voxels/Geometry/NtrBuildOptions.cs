// NtrBuildOptions.cs — voxel-build options for the NTR nozzle + core assembly.

namespace Voxelforge.Nuclear.Geometry;

/// <summary>
/// Build options for the NTR nozzle + stub reactor core voxel assembly.
/// </summary>
/// <param name="VoxelSize_mm">PicoGK Library voxel size [mm].</param>
/// <param name="SmoothenRadius_mm">
/// Cleanup smoothen radius [mm]. Should be capped at 25 % of the thinnest
/// wall (CLAUDE.md PicoGK pitfall #1). Caller is responsible for the cap.
/// </param>
/// <param name="EnableStubCore">
/// When true, a cylindrical stub reactor core is BoolAdded behind the nozzle
/// injector face (Wave-1 placeholder — no fuel-pin geometry).
/// </param>
public sealed record NtrBuildOptions(
    double VoxelSize_mm      = 0.5,
    double SmoothenRadius_mm = 0.10,
    bool   EnableStubCore    = true);
