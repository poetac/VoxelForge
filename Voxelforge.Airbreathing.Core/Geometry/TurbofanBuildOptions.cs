// TurbofanBuildOptions.cs — knobs for the turbofan voxel builder.
//
// Sibling to RamjetBuildOptions. Carries two distinct wall-thickness
// fields: WallThickness_mm for the hot-stream core shell, and
// BypassDuctWallThickness_mm for the cold-stream bypass duct (since
// bypass sees lower pressures and can run thinner).

using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Build-time knobs for <c>TurbofanVoxelBuilder.Build</c>.
/// </summary>
/// <param name="WallThickness_mm">
/// Core-flow pressure-shell wall thickness [mm]. Default 2.0 mm. Hot-stream
/// shell sees combustor pressure; size accordingly.
/// </param>
/// <param name="BypassDuctWallThickness_mm">
/// Outer bypass-duct pressure-shell wall thickness [mm]. Default 2.0 mm.
/// Cold-stream duct sees lower pressures than the core shell; can run
/// thinner once the structural-margin solver fires.
/// </param>
/// <param name="VoxelSize_mm">
/// Voxel grid resolution [mm]. 0 = auto-resolve to
/// <c>min(min(WallThickness_mm, BypassDuctWallThickness_mm) / 4, 0.4)</c>.
/// </param>
/// <param name="SmoothenRadius_mm">
/// LPBF-safe smoothing pass radius [mm]. Hard-clamped at build time to
/// 25 % of the thinnest wall per CLAUDE.md PicoGK pitfall #1.
/// </param>
/// <param name="LpbfMaterial">LPBF alloy profile; null skips the analysis.</param>
/// <param name="LpbfAzimuthalSamples">Azimuthal density of contour-driven surface sampling.</param>
/// <param name="RunLpbfAnalysis">Master toggle for the LPBF printability pass.</param>
public sealed record TurbofanBuildOptions(
    double WallThickness_mm           = 2.0,
    double BypassDuctWallThickness_mm = 2.0,
    double VoxelSize_mm               = 0.0,
    double SmoothenRadius_mm          = 0.15,
    LpbfMaterialProfile? LpbfMaterial = null,
    int    LpbfAzimuthalSamples       = 64,
    bool   RunLpbfAnalysis            = true);
