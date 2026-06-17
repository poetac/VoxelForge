// StlWelder.cs — Binary-STL reader/writer + tile-seam welder for the
// axial-tiling driver in ChamberAxialTileBuilder.BuildTiled. Pure
// C# — no PicoGK interop — so the whole welder is unit-testable in
// xUnit without tripping the Library-singleton gotcha. The driver
// produces one binary STL per tile, then hands the file list +
// per-tile core x-ranges here; this module emits the unified STL with
// interior-seam end-caps dropped so the weld is clean.
//
// Format reference (binary STL)
// ─────────────────────────────
// header (80 bytes, discarded by readers)
// triangle_count (uint32 LE)
// for each triangle:
//     normal        (3 × float32 LE)
//     vertex_1      (3 × float32 LE)
//     vertex_2      (3 × float32 LE)
//     vertex_3      (3 × float32 LE)
//     attribute     (uint16 LE, almost always 0)
//
// Each triangle = 50 bytes fixed. Header + count = 84 bytes. Total
// file size = 84 + 50 × N. No endianness or ASCII-STL fallback —
// PicoGK's Mesh.SaveToStlFile writes binary STL exclusively.
//
// Welder strategy
// ───────────────
// Each tile owns a unique "core" axial range [coreXMin, coreXMax]
// that excludes the overlap region shared with its neighbours.
// The welder keeps a tile's triangle iff the triangle's x-centroid
// falls within the tile's core range. This drops:
//   • Interior tile's left/right end-cap triangles (they're at the
//     overlap plane, ONE tile away from core) — removed because the
//     neighbouring tile supplies the continuous shell wall there.
//   • Overlap-region shell-wall triangles from BOTH tiles — kept
//     only on the tile whose core range actually covers the centroid.
// Exterior tiles (first / last) get to keep their outer-facing caps
// because there's no neighbour past the chamber's end.
//
// Caveats
// ───────
// Centroid-based filtering does NOT vertex-weld across seams — two
// tile meshes meeting at a seam produce abutting triangles with
// near-identical vertex coordinates on the seam plane, but the
// vertices are not merged into shared indices. This is fine for
// LPBF slicers that triangulate from soup (Bambu Studio, PrusaSlicer,
// Autodesk Netfabb all handle this); STL is already a vertex-soup
// format. If downstream tooling requires a truly vertex-shared
// mesh, Phase 2d adds a vertex-hash-merge pass on the welded output.

using System.IO;

namespace Voxelforge.Geometry;

/// <summary>
/// A single binary-STL triangle — normal vector + three vertices.
/// All values are single-precision (<see cref="float"/>) to match
/// the binary format exactly. Layout matches the on-disk struct so
/// welding is a byte-copy hot path when no filtering is needed.
/// </summary>
public readonly record struct StlTriangle(
    float Nx, float Ny, float Nz,
    float V1x, float V1y, float V1z,
    float V2x, float V2y, float V2z,
    float V3x, float V3y, float V3z)
{
    /// <summary>x-coordinate of the triangle's vertex centroid.</summary>
    public float CentroidX => (V1x + V2x + V3x) / 3f;

    /// <summary>Lowest x across the three vertices (for bbox tests).</summary>
    public float MinX => System.Math.Min(V1x, System.Math.Min(V2x, V3x));

    /// <summary>Highest x across the three vertices (for bbox tests).</summary>
    public float MaxX => System.Math.Max(V1x, System.Math.Max(V2x, V3x));
}

/// <summary>
/// Specifies the axial range a tile owns in the welded output.
/// Triangles whose centroid x falls within [CoreXMin, CoreXMax] are
/// kept; everything else is dropped. <paramref name="KeepLeftCap"/>
/// lets the FIRST tile hold onto triangles with x below CoreXMin
/// (its outward-facing -X end cap); <paramref name="KeepRightCap"/>
/// does the same for the last tile's +X end cap. Interior tiles
/// should pass both as <c>false</c>.
/// </summary>
public readonly record struct TileWeldRange(
    double CoreXMin_mm,
    double CoreXMax_mm,
    bool   KeepLeftCap,
    bool   KeepRightCap);

/// <summary>
/// Binary-STL weld / concat helper. All operations are synchronous
/// and allocate per-triangle (no managed-heap pressure — one 50-byte
/// buffer is reused for the hot path).
/// </summary>
public static class StlWelder
{
    private const int BinaryStlHeaderBytes   = 80;
    private const int BinaryStlTriangleBytes = 50;

    /// <summary>
    /// Read all triangles from a binary-STL file into memory. Throws
    /// <see cref="InvalidDataException"/> if the file size is
    /// inconsistent with the declared triangle count (detects
    /// ASCII-STL inputs or truncated writes).
    /// </summary>
    public static StlTriangle[] Read(string path)
    {
        using var fs = File.OpenRead(path);
        return Read(fs);
    }

    /// <summary>
    /// Stream-oriented overload. Consumes exactly 84 + 50 × N bytes
    /// starting at the stream's current position.
    /// </summary>
    public static StlTriangle[] Read(Stream stream)
    {
        var br = new BinaryReader(stream);
        var header = br.ReadBytes(BinaryStlHeaderBytes);
        if (header.Length != BinaryStlHeaderBytes)
            throw new InvalidDataException(
                $"Truncated STL header: got {header.Length} / {BinaryStlHeaderBytes} bytes.");
        uint count = br.ReadUInt32();
        long expectedBytes = 4L + (long)count * BinaryStlTriangleBytes;

        // Best-effort size sanity when the stream is a FileStream —
        // catches ASCII-STL masquerading as binary (the declared count
        // would be unreasonable given the actual file size).
        if (stream is FileStream fs)
        {
            long remaining = fs.Length - fs.Position + 4; // +4 = we already consumed count
            if (remaining < expectedBytes)
                throw new InvalidDataException(
                    $"STL file size {fs.Length} inconsistent with declared triangle count {count} "
                  + $"(would need {BinaryStlHeaderBytes + expectedBytes} bytes).");
        }

        var tris = new StlTriangle[count];
        for (int i = 0; i < count; i++)
        {
            float nx = br.ReadSingle(), ny = br.ReadSingle(), nz = br.ReadSingle();
            float v1x = br.ReadSingle(), v1y = br.ReadSingle(), v1z = br.ReadSingle();
            float v2x = br.ReadSingle(), v2y = br.ReadSingle(), v2z = br.ReadSingle();
            float v3x = br.ReadSingle(), v3y = br.ReadSingle(), v3z = br.ReadSingle();
            _ = br.ReadUInt16();   // attribute — discarded
            tris[i] = new StlTriangle(
                nx, ny, nz, v1x, v1y, v1z, v2x, v2y, v2z, v3x, v3y, v3z);
        }
        return tris;
    }

    /// <summary>
    /// Write a binary-STL with the given triangles. An 80-byte ASCII
    /// tag is written into the header slot for diagnostic readability
    /// (e.g. "RegenChamberDesigner tiled STL"); the spec allows
    /// arbitrary bytes there, and most viewers display it.
    /// </summary>
    public static void Write(string path, IReadOnlyList<StlTriangle> triangles, string headerTag = "")
    {
        using var fs = File.Create(path);
        Write(fs, triangles, headerTag);
    }

    /// <summary>Stream-oriented overload.</summary>
    public static void Write(Stream stream, IReadOnlyList<StlTriangle> triangles, string headerTag = "")
    {
        var bw = new BinaryWriter(stream);
        // Header: 80 bytes, padded with zeros after the ASCII tag.
        byte[] header = new byte[BinaryStlHeaderBytes];
        int tagLen = System.Math.Min(headerTag.Length, BinaryStlHeaderBytes);
        System.Text.Encoding.ASCII.GetBytes(headerTag, 0, tagLen, header, 0);
        bw.Write(header);
        bw.Write((uint)triangles.Count);
        foreach (var t in triangles)
        {
            bw.Write(t.Nx);  bw.Write(t.Ny);  bw.Write(t.Nz);
            bw.Write(t.V1x); bw.Write(t.V1y); bw.Write(t.V1z);
            bw.Write(t.V2x); bw.Write(t.V2y); bw.Write(t.V2z);
            bw.Write(t.V3x); bw.Write(t.V3y); bw.Write(t.V3z);
            bw.Write((ushort)0);
        }
    }

    /// <summary>
    /// True when the triangle's x-centroid lies inside the weld
    /// range's [CoreXMin, CoreXMax]. When the triangle's ALL-three
    /// vertices lie below CoreXMin and <paramref name="range"/>.KeepLeftCap
    /// is true, the triangle is also kept (it's the tile's outward
    /// -X end cap, which only the first tile should carry). Symmetric
    /// for KeepRightCap. Returns false otherwise.
    /// </summary>
    public static bool KeepTriangle(in StlTriangle t, in TileWeldRange range)
    {
        float cx = t.CentroidX;
        if (cx >= (float)range.CoreXMin_mm && cx <= (float)range.CoreXMax_mm)
            return true;
        if (range.KeepLeftCap && t.MaxX <= (float)range.CoreXMin_mm)
            return true;
        if (range.KeepRightCap && t.MinX >= (float)range.CoreXMax_mm)
            return true;
        return false;
    }

    /// <summary>
    /// Weld N per-tile STL files into a single output STL. The
    /// <paramref name="tileStlPaths"/> and <paramref name="tileRanges"/>
    /// lists must be the same length and in the same tile order. Each
    /// tile's triangles are filtered through <see cref="KeepTriangle"/>
    /// with that tile's range; survivors are concatenated and written
    /// to <paramref name="outputPath"/>.
    /// </summary>
    public static StlWeldResult Weld(
        IReadOnlyList<string>        tileStlPaths,
        IReadOnlyList<TileWeldRange> tileRanges,
        string                       outputPath,
        string                       headerTag = "")
    {
        if (tileStlPaths.Count != tileRanges.Count)
            throw new System.ArgumentException(
                $"tileStlPaths.Count ({tileStlPaths.Count}) must equal tileRanges.Count ({tileRanges.Count}).");

        long totalIn = 0, totalKept = 0;
        var kept = new List<StlTriangle>();
        for (int i = 0; i < tileStlPaths.Count; i++)
        {
            var tris = Read(tileStlPaths[i]);
            totalIn += tris.Length;
            foreach (var t in tris)
            {
                if (KeepTriangle(t, tileRanges[i]))
                    kept.Add(t);
            }
            totalKept = kept.Count;
        }
        Write(outputPath, kept, headerTag);
        return new StlWeldResult(
            InputTriangleCount:  totalIn,
            OutputTriangleCount: totalKept,
            DroppedTriangleCount: totalIn - totalKept,
            OutputBytes:          new FileInfo(outputPath).Length);
    }

    /// <summary>
    /// Filter a single in-memory triangle array against one weld range.
    /// Exposed for unit tests and callers that already have the
    /// triangles in memory (e.g. a future in-process welder that
    /// skips the per-tile STL round-trip to disk).
    /// </summary>
    public static StlTriangle[] Filter(
        IReadOnlyList<StlTriangle> triangles,
        in TileWeldRange           range)
    {
        var kept = new List<StlTriangle>(triangles.Count);
        foreach (var t in triangles)
            if (KeepTriangle(t, range)) kept.Add(t);
        return kept.ToArray();
    }
}

public sealed record StlWeldResult(
    long InputTriangleCount,
    long OutputTriangleCount,
    long DroppedTriangleCount,
    long OutputBytes);
