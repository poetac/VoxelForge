// NtrStlExport.cs — write a built NTR voxel assembly to STL.
// Mirrors ResistojetStlExport on the electric-propulsion side.

using System;
using PicoGK;
using Voxelforge;
using Voxelforge.Nuclear;  // for VoxelHandleExtensions.AsPicoGK

namespace Voxelforge.Nuclear.Geometry;

/// <summary>
/// STL-export helpers for the nuclear-thermal pillar's voxel assembly.
/// </summary>
public static class NtrStlExport
{
    /// <summary>
    /// Mesh the voxel handle and write an STL to <paramref name="outPath"/>.
    /// Returns the triangle count of the generated mesh.
    /// </summary>
    public static int Save(IVoxelHandle voxels, string outPath)
    {
        if (voxels  is null) throw new ArgumentNullException(nameof(voxels));
        if (outPath is null) throw new ArgumentNullException(nameof(outPath));
        if (string.IsNullOrWhiteSpace(outPath))
            throw new ArgumentException("Output STL path must be non-empty.", nameof(outPath));

        var picoGkVoxels = voxels.AsPicoGK();
        var mesh = new Mesh(picoGkVoxels);
        mesh.SaveToStlFile(outPath);
        return (int)mesh.nTriangleCount();
    }
}
