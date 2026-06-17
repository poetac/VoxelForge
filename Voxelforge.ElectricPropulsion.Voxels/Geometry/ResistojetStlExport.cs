// ResistojetStlExport.cs — write a built resistojet voxel shell to STL.
// Mirrors RamjetStlExport on the airbreathing side.

using System;
using PicoGK;

namespace Voxelforge.ElectricPropulsion.Geometry;

/// <summary>
/// STL-export helpers for the electric-propulsion pillar's voxel shells.
/// </summary>
public static class ResistojetStlExport
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
