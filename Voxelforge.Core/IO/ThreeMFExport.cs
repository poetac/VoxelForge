// ThreeMFExport.cs — PHASE 7 (2026-04-20):
// Minimal 3MF exporter that wraps the existing binary STL with LPBF-oriented
// metadata (material, orientation, design hash, prediction envelope).
//
// Why 3MF over STL:
//   STL is a dumb triangle list — no orientation hint, no material callout,
//   no provenance. Most metal-AM shops ask for 3MF because it carries the
//   build context needed for a print quote. The Core Production Extension
//   would let us embed support markers and slice settings too; for MVP we
//   just emit a valid base-profile 3MF with human-readable metadata.
//
// Pipeline:
//   1. Parse the binary STL (80-byte header + uint count + 50 B/triangle).
//   2. Deduplicate vertices (rough 1 µm hash) and rewrite as
//      &lt;vertices&gt; / &lt;triangles&gt; in 3MF XML.
//   3. Stuff the model file + [Content_Types].xml + _rels/.rels into a ZIP
//      with .3mf extension.
//
// Notes / caveats:
//   • Vertex dedup is O(N log N); triangle counts above ~5M may OOM on a
//     small laptop. Export at a coarser voxel (0.3 mm) if you hit this.
//   • We do NOT add units='millimeter' on the model because PicoGK's STL
//     already bakes in mm. 3MF spec allows the unit to be omitted (default
//     mm).
//   • Not yet wired to a UI button — accessible via IO API for future
//     demo polish. A follow-on adds a "Save as 3MF…" action.

using System.Globalization;
using System.IO.Compression;
using System.Text;
using Voxelforge.Optimization;

namespace Voxelforge.IO;

public static class ThreeMFExport
{
    /// <summary>
    /// Write a 3MF that wraps <paramref name="stlPath"/> (binary STL) and
    /// stamps LPBF metadata drawn from <paramref name="r"/>. The mesh is
    /// de-duplicated vertex-wise to keep file size reasonable.
    /// </summary>
    public static void SaveFromStl(string stlPath, string threeMfPath, RegenGenerationResult r)
    {
        var (vertices, triangles) = ParseBinaryStl(stlPath);
        var (idx, uniqueVerts) = Deduplicate(vertices, triangles);

        string modelXml = BuildModelXml(uniqueVerts, idx, r);

        if (File.Exists(threeMfPath)) File.Delete(threeMfPath);
        using var zip = ZipFile.Open(threeMfPath, ZipArchiveMode.Create);

        WriteEntry(zip, "[Content_Types].xml", ContentTypesXml);
        WriteEntry(zip, "_rels/.rels",         RelsXml);
        WriteEntry(zip, "3D/3dmodel.model",    modelXml);
    }

    // ── 3MF skeleton files (const) ─────────────────────────────────

    private const string ContentTypesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\"/>" +
        "</Types>";

    private const string RelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\" Target=\"/3D/3dmodel.model\"/>" +
        "</Relationships>";

    // ── Mesh parsing / writing ─────────────────────────────────────

    private static (List<(float X, float Y, float Z)> verts, List<int[]> tris)
        ParseBinaryStl(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        br.ReadBytes(80);                       // header
        uint n = br.ReadUInt32();
        if ((long)50 * n + 84 > fs.Length)
            throw new InvalidDataException("STL file is shorter than declared triangle count — probably ASCII, not binary.");

        var verts = new List<(float, float, float)>(capacity: (int)(n * 3));
        var tris  = new List<int[]>(capacity: (int)n);
        for (uint i = 0; i < n; i++)
        {
            br.ReadBytes(12);                   // normal, discarded
            int baseIdx = verts.Count;
            for (int k = 0; k < 3; k++)
            {
                float x = br.ReadSingle();
                float y = br.ReadSingle();
                float z = br.ReadSingle();
                verts.Add((x, y, z));
            }
            br.ReadBytes(2);                    // attribute
            tris.Add(new[] { baseIdx, baseIdx + 1, baseIdx + 2 });
        }
        return (verts, tris);
    }

    /// <summary>
    /// Rough vertex deduplication via a 1 µm quantised hash map. Returns
    /// remapped triangle indices + the unique vertex list.
    /// </summary>
    private static (List<int[]> tris, List<(float X, float Y, float Z)> verts)
        Deduplicate(
            List<(float X, float Y, float Z)> inVerts,
            List<int[]> inTris)
    {
        var map = new Dictionary<long, int>(inVerts.Count);
        var outVerts = new List<(float, float, float)>(inVerts.Count / 3);

        int Canon((float X, float Y, float Z) v)
        {
            // 1 µm quantisation bucket per axis — fits 3 × 20-bit integers into
            // one 64-bit key (offset by 2^19 to keep positive).
            long x = (long)Math.Round(v.X * 1000.0) + (1L << 19);
            long y = (long)Math.Round(v.Y * 1000.0) + (1L << 19);
            long z = (long)Math.Round(v.Z * 1000.0) + (1L << 19);
            long key = (x & 0xFFFFF) | ((y & 0xFFFFF) << 20) | ((z & 0xFFFFF) << 40);
            if (map.TryGetValue(key, out int idx)) return idx;
            idx = outVerts.Count;
            outVerts.Add(v);
            map[key] = idx;
            return idx;
        }

        var outTris = new List<int[]>(inTris.Count);
        foreach (var t in inTris)
        {
            int a = Canon(inVerts[t[0]]);
            int b = Canon(inVerts[t[1]]);
            int c = Canon(inVerts[t[2]]);
            if (a != b && b != c && a != c) outTris.Add(new[] { a, b, c });
        }
        return (outTris, outVerts);
    }

    private static string BuildModelXml(
        List<(float X, float Y, float Z)> verts,
        List<int[]> tris,
        RegenGenerationResult r)
    {
        var sb = new StringBuilder(verts.Count * 40 + tris.Count * 30);
        var ci = CultureInfo.InvariantCulture;
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<model unit=\"millimeter\" xml:lang=\"en-US\" ");
        sb.Append("xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">");

        // ── Metadata: LPBF-oriented provenance ───────────────────
        var mat = HeatTransfer.WallMaterials.All[r.Conditions.WallMaterialIndex];
        void Meta(string name, string val) =>
            sb.Append($"<metadata name=\"{name}\">{System.Security.SecurityElement.Escape(val)}</metadata>");

        // PR-2 namespace rename (2026-04-30): 3MF Application metadata
        // kept as the literal "RegenChamberDesigner (Leap71 PicoGK)" so
        // existing 3MF artifacts round-trip without a schema bump.
        Meta("Application",  "RegenChamberDesigner (Leap71 PicoGK)");
        Meta("Title",        "Regeneratively-cooled thrust chamber");
        Meta("Material",     $"{mat.Name} ({mat.DataSource})");
        Meta("ProcessNote",  mat.LPBFProcessNote);
        Meta("Certification", mat.CertificationStatus);
        Meta("PrintedMass_g",   r.Geometry.TotalMass_g.ToString("F1", ci));
        Meta("PrintedCost_USD", r.Geometry.PrintedCost_USD.ToString("F2", ci));
        Meta("PeakWallT_K",     r.Thermal.PeakGasSideWallT_K.ToString("F0", ci));
        Meta("MinFeature_mm",   r.Manufacturing.MinFeatureSize_mm.ToString("F2", ci));
        if (!string.IsNullOrEmpty(r.DesignHash))
            Meta("DesignHash", r.DesignHash);
        if (!string.IsNullOrEmpty(r.Manufacturing.Overhang.RecommendedBuildOrientation))
            Meta("Orientation", r.Manufacturing.Overhang.RecommendedBuildOrientation);

        // OOB-15 (2026-04-29): forever-traceability provenance block.
        // Every fired part carries (commit, schema, gate status) so a
        // real-hardware fault can be back-correlated to the exact code
        // + design state that produced it. The SA-vector-hash semantic
        // is already covered by `DesignHash` above (computed via
        // DesignProvenance.Compute over the JSON-serialised
        // (cond, design) tuple — different algorithm from a pure
        // SHA(Pack), same provenance role).
        Meta("GitSha",           ExportMetadata.GitSha());
        Meta("SchemaVersion",    ExportMetadata.SchemaVersion);
        Meta("GatePassManifest", ExportMetadata.GatePassManifest(FeasibilityGate.Evaluate(r)));

        // ── Mesh resources ────────────────────────────────────────
        sb.Append("<resources><object id=\"1\" type=\"model\"><mesh>");
        sb.Append("<vertices>");
        foreach (var v in verts)
            sb.Append($"<vertex x=\"{v.X.ToString("G6", ci)}\" y=\"{v.Y.ToString("G6", ci)}\" z=\"{v.Z.ToString("G6", ci)}\"/>");
        sb.Append("</vertices>");
        sb.Append("<triangles>");
        foreach (var t in tris)
            sb.Append($"<triangle v1=\"{t[0]}\" v2=\"{t[1]}\" v3=\"{t[2]}\"/>");
        sb.Append("</triangles></mesh></object></resources>");
        sb.Append("<build><item objectid=\"1\"/></build>");
        sb.Append("</model>");
        return sb.ToString();
    }

    private static void WriteEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        w.Write(content);
    }
}
