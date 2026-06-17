// VoxelFieldSnapshot.cs — Sprint 27 (2026-04-23): PicoGK-free voxel field
// abstraction for the trapped-powder flood-fill.
//
// Design rationale (ADR-005 motivated). `TrappedPowderAnalysis` wants to
// flood-fill from the bounding-box exterior through the part's void space
// and report any pocket it can't reach. In production the voxel data
// lives in a `PicoGK.Voxels` object; in tests we can't instantiate one
// (xUnit + PicoGK crashes the test host on dispose). The flood-fill API
// therefore takes a dense `bool[,,]` occupancy grid — easy to synthesise
// in tests, easy to populate from `PicoGK.Voxels.AsBoolArray(...)` (or
// its moral equivalent) on the production side.
//
// Storage is `true` for solid voxels, `false` for void — inverse of a
// filled-part SDF but matches the "part present" mental model most
// consumers reach for.

using System.Numerics;

namespace Voxelforge.Geometry.LpbfAnalysis;

/// <summary>
/// Dense axis-aligned voxel occupancy grid. <see cref="Occupancy"/>[i, j, k]
/// is <c>true</c> when the voxel at lattice index (i, j, k) is solid
/// (part material), <c>false</c> when it's void (air / powder).
/// World-space mapping:
/// <code>
///   worldPoint = Origin + (i + 0.5, j + 0.5, k + 0.5) * VoxelSize_mm
/// </code>
/// </summary>
public sealed record VoxelFieldSnapshot(
    bool[,,] Occupancy,
    double   VoxelSize_mm,
    Vector3  Origin)
{
    public int SizeI => Occupancy.GetLength(0);
    public int SizeJ => Occupancy.GetLength(1);
    public int SizeK => Occupancy.GetLength(2);

    public bool InBounds(int i, int j, int k) =>
        i >= 0 && i < SizeI &&
        j >= 0 && j < SizeJ &&
        k >= 0 && k < SizeK;
}

/// <summary>
/// One external opening (drain port, manifold inlet, injector face bore,
/// etc.) through which powder can evacuate the part. The flood-fill seeds
/// from every opening simultaneously; any void voxel NOT reached is a
/// trapped-powder region.
/// <para>The opening is specified as a world-space point plus a radius;
/// every void voxel within that radius is marked as a seed.</para>
/// </summary>
public readonly record struct OpeningPort(
    Vector3 Center,
    double  Radius_mm,
    string  Label);
