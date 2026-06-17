// PulsejetBuildOptions.cs — knobs for the pulsejet voxel builder
// (Wave 1 PR-5, sub-step 1a.5).
//
// Mirrors RamjetBuildOptions structure. Pulsejet-specific MVP scope:
// single uniform wall thickness, no cooling channels, no flanges, no
// instrumentation bosses. Valveless geometry has no moving parts so
// the build is mechanically simpler than the ramjet's CD nozzle.

using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Build-time knobs for <c>PulsejetVoxelBuilder.Build</c>. All
/// dimensional fields are millimetres at the voxel boundary; the
/// <see cref="PulsejetContour"/> from Core is in metres and is converted
/// once at the build entry.
/// </summary>
/// <param name="WallThickness_mm">
/// Uniform pressure-shell wall thickness [mm]. Default 1.5 mm —
/// pulsejet steady-state chamber pressures are near-atmospheric (peak
/// excursions ~1.3× ambient per Foa §11.4), so a thinner wall than the
/// ramjet's 2.0 mm is structurally adequate. Must be &gt; 0.
/// </param>
/// <param name="VoxelSize_mm">
/// Voxel grid resolution [mm]. 0 = auto-resolve to
/// <c>min(WallThickness_mm / 4, 0.4)</c>.
/// </param>
/// <param name="SmoothenRadius_mm">
/// LPBF-safe smoothing pass radius [mm]. Hard-clamped at build time to
/// <c>min(SmoothenRadius_mm, 0.25 · WallThickness_mm)</c> per ADR-007 +
/// CLAUDE.md PicoGK pitfall #1.
/// </param>
/// <param name="LpbfMaterial">
/// LPBF alloy profile for the printability analysis. Null skips the
/// analysis.
/// </param>
/// <param name="LpbfAzimuthalSamples">
/// Azimuthal density of contour-driven surface sampling for LPBF
/// printability. Default 64 — matches the ramjet builder.
/// </param>
/// <param name="RunLpbfAnalysis">
/// Master toggle for the LPBF printability pass. False produces a result
/// with <c>Printability = null</c> regardless of <see cref="LpbfMaterial"/>.
/// </param>
public sealed record PulsejetBuildOptions(
    double WallThickness_mm    = 1.5,
    double VoxelSize_mm        = 0.0,
    double SmoothenRadius_mm   = 0.15,
    LpbfMaterialProfile? LpbfMaterial = null,
    int    LpbfAzimuthalSamples = 64,
    bool   RunLpbfAnalysis      = true);
