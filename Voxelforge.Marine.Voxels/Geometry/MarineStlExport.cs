// MarineStlExport.cs — write a built marine hull voxel shell to STL.
//
// Thin wrapper around PicoGK's Mesh + SaveToStlFile pipeline.
// Mirrors RamjetStlExport.cs in Voxelforge.Airbreathing.Voxels.

using System;
using PicoGK;
using Voxelforge;

namespace Voxelforge.Marine.Geometry;

/// <summary>
/// Export helpers for the marine pillar's voxelised hull shells.
/// </summary>
public static class MarineStlExport
{
    /// <summary>
    /// Mesh the voxel handle and write an STL to <paramref name="outPath"/>.
    /// Returns the triangle count.
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
