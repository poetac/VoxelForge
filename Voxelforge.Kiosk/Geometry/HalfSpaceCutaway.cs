// HalfSpaceCutaway.cs — half-section axial cut for kiosk demo prints.
//
// Subtracts a half-space (y < yCutPlane by default) from a chamber Voxels,
// exposing the regen channels and injector face on a single planar slice.
// Runs entirely outside ChamberVoxelBuilder so production paths are
// untouched.
//
// Sign convention: Voxels are "solid" where fSignedDistance < 0. The
// half-space implicit returns p.Y - yCutPlane → solid for y < yCutPlane.
// BoolSubtractTemp voxelises the implicit into a temporary Voxels (sized
// to the chamber bounds plus pad) and removes it from the chamber, leaving
// only y > yCutPlane material.
//
// CLAUDE.md pitfall #2: this cutaway must NOT run on TPMS-filled regions
// — the lattice would fragment. The kiosk pipeline restricts canonicals to
// Axial-channel topologies (merlin / aerospike / pintle); TPMS support
// would require interleaving the cut with the TPMS subtract inside
// ChamberVoxelBuilder.Build, which is out of scope for v1.

using System.Numerics;
using PicoGK;
using Voxelforge.Geometry;

namespace Voxelforge.Kiosk.Geometry;

internal sealed class AxialHalfSpaceImplicit : IImplicit
{
    private readonly float _yCutPlane;

    public AxialHalfSpaceImplicit(float yCutPlane) { _yCutPlane = yCutPlane; }

    public float fSignedDistance(in Vector3 p) => p.Y - _yCutPlane;
}

public static class HalfSectionCutaway
{
    /// <summary>
    /// Removes the y &lt; <paramref name="yCutPlane"/> half of
    /// <paramref name="chamberVox"/>, in-place. The remaining material
    /// is everything with y ≥ yCutPlane plus the planar cut surface
    /// from PicoGK's marching cubes.
    /// </summary>
    /// <remarks>
    /// Bounds are sized from the chamber's reported bounding length +
    /// diameter (from <c>ChamberGeometryResult</c>) with conservative pad
    /// to cover any upstream mounting-flange or downstream nozzle
    /// extensions. A small (+2 mm) pad above yCutPlane prevents
    /// marching-cubes from leaving a sliver at the cut boundary.
    /// </remarks>
    public static void ApplyAxialHalfSection(
        Voxels chamberVox,
        double boundingLength_mm,
        double boundingDiameter_mm,
        float yCutPlane = 0f,
        double axialPad_mm = 50.0,
        double radialPad_mm = 5.0)
    {
        float radius_mm = (float)(boundingDiameter_mm * 0.5 + radialPad_mm);
        var bounds = new BBox3(
            new Vector3(-(float)axialPad_mm,
                        -radius_mm,
                        -radius_mm),
            new Vector3((float)(boundingLength_mm + axialPad_mm),
                        yCutPlane + 2f,
                        radius_mm));

        chamberVox.BoolSubtractTemp(new AxialHalfSpaceImplicit(yCutPlane), bounds);
    }
}
