// PulsejetBuilderAdapter.cs — concrete IAirbreathingVoxelGenerator wrapping
// the static PulsejetVoxelBuilder.Build (Wave 1 PR-5, sub-step 1a.5).
//
// Mirrors RamjetBuilderAdapter on the same pillar. Implements only the
// pulsejet overload; the ramjet overload falls through to the default
// NotSupportedException stub on IAirbreathingVoxelGenerator.

using Voxelforge.Airbreathing.Geometry;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// <see cref="IAirbreathingVoxelGenerator"/> implementation for pulsejet
/// shells. Dispatches to <see cref="PulsejetVoxelBuilder.Build"/>.
/// </summary>
public sealed class PulsejetBuilderAdapter : IAirbreathingVoxelGenerator
{
    /// <inheritdoc />
    public PulsejetGeometryResult Build(PulsejetContour contour, PulsejetBuildOptions opts)
        => PulsejetVoxelBuilder.Build(contour, opts);
}
