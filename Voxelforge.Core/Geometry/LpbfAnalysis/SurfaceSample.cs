// SurfaceSample.cs — Sprint 27 (2026-04-23): abstract voxel-surface sample.
//
// Design rationale (ADR-005 motivated). The overhang analysis wants to ask
// "is there a down-facing patch on this part that exceeds the material's
// overhang limit?" On the production side the samples come from the voxel
// field's surface mesh (marching-cubes triangles, one sample per triangle
// centroid). On the test side they come from synthesised arrays — because
// instantiating `PicoGK.Library` inside xUnit crashes the test host. The
// `OverhangAnalysis.Analyze` API therefore takes an abstract
// `IEnumerable<SurfaceSample>` rather than a `PicoGK.Voxels`; both
// production and test callers feed the same API through different
// producers.

using System.Numerics;

namespace Voxelforge.Geometry.LpbfAnalysis;

/// <summary>
/// One point on the part surface with an outward-pointing normal and the
/// area of the patch it represents. Area weights the violation-area
/// accumulator in <see cref="OverhangAnalysis"/> so large flat overhangs
/// count more heavily than small grazing facets.
/// </summary>
public readonly record struct SurfaceSample(
    Vector3 Point,
    Vector3 Normal,
    double  Area_mm2);
