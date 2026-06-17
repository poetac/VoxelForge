// IAirbreathingVoxelGenerator.cs — abstraction over air-breathing voxel-build
// methods. Mirrors IVoxelGenerator on the rocket side (ADR-021 / Phase 0):
// Core orchestrators reference this interface; Voxelforge.Airbreathing.Voxels
// provides the PicoGK-bound concrete implementation.

using System;
using Voxelforge.Airbreathing.Geometry;

namespace Voxelforge.Airbreathing;

/// <summary>
/// Abstraction over air-breathing voxel-build methods. Keeps the headless
/// <c>Voxelforge.Airbreathing.Core</c> library free of PicoGK references —
/// the concrete builders live in <c>Voxelforge.Airbreathing.Voxels</c>.
/// </summary>
/// <remarks>
/// Per-variant overload pattern: each engine kind (ramjet, pulsejet, future
/// turbojet/turbofan/scramjet) gets its own typed <c>Build</c> method.
/// Adapters implement the variants they support and inherit a default
/// <see cref="NotSupportedException"/> stub for the rest. Wave 1 (sub-step
/// 1a.5) ships pulsejet alongside the existing ramjet.
/// </remarks>
public interface IAirbreathingVoxelGenerator
{
    /// <summary>
    /// Build the ramjet shell voxel from a contour + build-options bundle.
    /// Must run inside a <c>PicoGK.Library</c> scope on the task thread
    /// (CLAUDE.md PicoGK pitfall #4).
    /// </summary>
    RamjetGeometryResult Build(RamjetContour contour, RamjetBuildOptions opts)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support ramjet builds.");

    /// <summary>
    /// Build the valveless pulsejet shell voxel from a contour + build-options
    /// bundle. Must run inside a <c>PicoGK.Library</c> scope on the task
    /// thread. Wave 1 sub-step 1a.5; default-throws for adapters that don't
    /// implement pulsejet support.
    /// </summary>
    PulsejetGeometryResult Build(PulsejetContour contour, PulsejetBuildOptions opts)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support pulsejet builds.");
}
