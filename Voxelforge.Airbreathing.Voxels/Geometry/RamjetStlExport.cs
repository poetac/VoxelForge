// RamjetStlExport.cs — write a built ramjet voxel shell to STL.
//
// Thin wrapper around PicoGK's Mesh + SaveToStlFile pipeline. Returns
// the triangle count so callers (StlExporter, sub-process tests) can
// report it in `BENCH triangle_count=N` stdout lines that mirror the
// rocket-side ChamberVoxelBuilder.ExportStlProfiled contract.

using System;
using PicoGK;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Export helpers for the air-breathing pillar's voxel shells.
/// </summary>
public static class RamjetStlExport
{
    /// <summary>
    /// Mesh the voxel handle and write an ASCII / binary STL to
    /// <paramref name="outPath"/>. Returns the triangle count of the
    /// generated mesh.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="voxels"/> or <paramref name="outPath"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="outPath"/> is empty / whitespace.</exception>
    /// <exception cref="System.IO.IOException">The STL file could not be written.</exception>
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
