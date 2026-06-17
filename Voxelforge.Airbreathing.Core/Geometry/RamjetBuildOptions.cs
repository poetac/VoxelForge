// RamjetBuildOptions.cs — knobs for the ramjet voxel builder.
//
// Scope-trimmed for the Step 1 sub-step 1c MVP: single uniform wall
// thickness, no cooling channels, no manifolds, no flanges. Future
// follow-ons will extend the record additively (LPBF cooling channels,
// flange/mount geometry, instrumentation bosses) without breaking
// existing call sites.

using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Build-time knobs for <see cref="RamjetVoxelBuilder.Build"/>. All
/// dimensional fields are millimetres at the voxel boundary; the
/// <see cref="RamjetContour"/> from Core is in metres and is converted
/// once at the build entry.
/// </summary>
/// <param name="WallThickness_mm">
/// Uniform pressure-shell wall thickness [mm]. Default 2.0 mm — sufficient
/// for the 1-3 atm chamber pressures typical of subsonic-combustion ramjets
/// at the airframe-integrated scale this MVP targets. Must be &gt; 0.
/// </param>
/// <param name="VoxelSize_mm">
/// Voxel grid resolution [mm]. 0 = auto-resolve to
/// <c>min(WallThickness_mm / 4, 0.4)</c> so the wall is sampled by ≥ 4
/// voxels across (printability + structural-margin tolerance).
/// </param>
/// <param name="SmoothenRadius_mm">
/// LPBF-safe smoothing pass radius [mm]. Hard-clamped at build time to
/// <c>min(SmoothenRadius_mm, 0.25 * WallThickness_mm)</c> per CLAUDE.md
/// PicoGK pitfall #1 ("Smoothen(d) destroys features &lt; 2d; cap at 25 %
/// of minimum feature thickness"). Default 0.15 mm.
/// </param>
/// <param name="LpbfMaterial">
/// LPBF alloy profile for the printability analysis. Null skips the
/// analysis (useful for a pure-geometry build / fast STL preview).
/// </param>
/// <param name="LpbfAzimuthalSamples">
/// Azimuthal density of contour-driven surface sampling for LPBF
/// printability. Higher = more sample points per station = better
/// localisation of overhang violations. Default 64 — matches the
/// rocket-side <c>SampleAxisymmetricSurface</c> default range.
/// </param>
/// <param name="RunLpbfAnalysis">
/// Master toggle for the LPBF printability pass. False produces a result
/// with <c>Printability = null</c> regardless of <see cref="LpbfMaterial"/>.
/// </param>
public sealed record RamjetBuildOptions(
    double WallThickness_mm    = 2.0,
    double VoxelSize_mm        = 0.0,
    double SmoothenRadius_mm   = 0.15,
    LpbfMaterialProfile? LpbfMaterial = null,
    int    LpbfAzimuthalSamples = 64,
    bool   RunLpbfAnalysis      = true);
