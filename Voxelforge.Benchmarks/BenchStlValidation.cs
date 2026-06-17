// BenchStlValidation.cs — Nightly STL topology validation (#657).
//
// Exports a binary STL for each canonical rocket preset at 0.5 mm voxel
// and validates the mesh topology using a pure-managed binary-STL reader:
//
//   - Degenerate triangles (zero-area, duplicate vertices)
//   - Non-manifold edges (each edge must appear in exactly 2 triangles)
//   - Zero-triangle mesh
//
// Satisfies the zero-new-native-dependency rule (ADR-024): no admesh,
// no MeshLab CLI — only managed System.IO byte reading.
//
// CLI:
//   --bench-stl-validation [--voxel <mm=0.5>] [--out <jsonl>]
//                           [--stl-dir <dir>]
//
// Exit codes:
//   0 — all presets pass
//   1 — one or more presets have topology failures
//   3 — argument error
//
// JSONL record per preset:
//   { "schema_version":1, ..., "bench_name":"bench-stl-validation",
//     "preset":"merlin", "voxel_mm":0.5,
//     "triangle_count":12345, "degenerate_count":0,
//     "non_manifold_edge_count":0, "unique_edge_count":8230,
//     "watertight":true, "status":"pass",
//     "stl_bytes":617450, "export_ms":1234 }

using System.Diagnostics;
using System.Globalization;
using System.Text;
using PicoGK;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static class BenchStlValidation
{
    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --bench-stl-validation "
      + "[--voxel <mm=0.5>] [--out <jsonl>] [--stl-dir <dir>]";

    // ── Pure-managed binary-STL reader ────────────────────────────────

    // Binary STL layout (little-endian):
    //   80 bytes: header
    //   4 bytes:  uint32 triangle count
    //   Per triangle (50 bytes):
    //     12 bytes: normal  (3 × float32)
    //     12 bytes: vertex0 (3 × float32)
    //     12 bytes: vertex1 (3 × float32)
    //     12 bytes: vertex2 (3 × float32)
    //     2 bytes:  attribute byte count

    private readonly record struct Vec3(float X, float Y, float Z)
    {
        public static Vec3 Read(byte[] buf, int offset) =>
            new(BitConverter.ToSingle(buf, offset),
                BitConverter.ToSingle(buf, offset + 4),
                BitConverter.ToSingle(buf, offset + 8));

        public float LengthSq() => X * X + Y * Y + Z * Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new(a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);

        public static Vec3 operator -(Vec3 a, Vec3 b) =>
            new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    // Edge is an unordered pair of vertices. Two triangles sharing the same
    // edge will produce the same Edge value regardless of winding order
    // because we normalize so that the lexically-smaller vertex is always first.
    private readonly struct Edge : IEquatable<Edge>
    {
        public readonly Vec3 A, B;

        public Edge(Vec3 v0, Vec3 v1)
        {
            // Normalize: smaller (by x,y,z lex) comes first so the same
            // physical edge always hashes identically regardless of the
            // triangle's winding direction.
            if (Compare(v0, v1) <= 0) { A = v0; B = v1; }
            else                       { A = v1; B = v0; }
        }

        private static int Compare(Vec3 a, Vec3 b)
        {
            int cx = BitConverter.SingleToInt32Bits(a.X).CompareTo(BitConverter.SingleToInt32Bits(b.X));
            if (cx != 0) return cx;
            int cy = BitConverter.SingleToInt32Bits(a.Y).CompareTo(BitConverter.SingleToInt32Bits(b.Y));
            if (cy != 0) return cy;
            return BitConverter.SingleToInt32Bits(a.Z).CompareTo(BitConverter.SingleToInt32Bits(b.Z));
        }

        public bool Equals(Edge other) =>
            BitConverter.SingleToInt32Bits(A.X) == BitConverter.SingleToInt32Bits(other.A.X) &&
            BitConverter.SingleToInt32Bits(A.Y) == BitConverter.SingleToInt32Bits(other.A.Y) &&
            BitConverter.SingleToInt32Bits(A.Z) == BitConverter.SingleToInt32Bits(other.A.Z) &&
            BitConverter.SingleToInt32Bits(B.X) == BitConverter.SingleToInt32Bits(other.B.X) &&
            BitConverter.SingleToInt32Bits(B.Y) == BitConverter.SingleToInt32Bits(other.B.Y) &&
            BitConverter.SingleToInt32Bits(B.Z) == BitConverter.SingleToInt32Bits(other.B.Z);

        public override bool Equals(object? obj) => obj is Edge e && Equals(e);

        public override int GetHashCode()
        {
            var h = new HashCode();
            h.Add(BitConverter.SingleToInt32Bits(A.X));
            h.Add(BitConverter.SingleToInt32Bits(A.Y));
            h.Add(BitConverter.SingleToInt32Bits(A.Z));
            h.Add(BitConverter.SingleToInt32Bits(B.X));
            h.Add(BitConverter.SingleToInt32Bits(B.Y));
            h.Add(BitConverter.SingleToInt32Bits(B.Z));
            return h.ToHashCode();
        }
    }

    private sealed record TopologyResult(
        int  TriangleCount,
        int  DegenerateCount,
        int  UniqueEdgeCount,
        int  NonManifoldEdgeCount,
        bool Watertight)
    {
        // Severity ladder. Holes / non-manifold edges break slicing and must
        // FAIL the guardrail (a watertight mesh here means nonManifold == 0 &&
        // triangleCount > 0, so !Watertight after the empty-mesh guard implies
        // NonManifoldEdgeCount > 0). Zero-area degenerate slivers are a known,
        // tolerable PicoGK marching-cubes artifact at coarse voxel sizes, so
        // they warn rather than fail.
        public string Status =>
            TriangleCount == 0  ? "fail" :   // empty / unreadable STL
            !Watertight         ? "fail" :   // holes or non-manifold edges (edge shared by != 2 triangles)
            DegenerateCount > 0 ? "warn" :   // zero-area slivers — tolerable at coarse voxel
            "pass";
    }

    private static TopologyResult ValidateBinaryStl(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 84)
            return new TopologyResult(0, 0, 0, 0, false);

        int triangleCount = (int)BitConverter.ToUInt32(bytes, 80);
        int expectedLen   = 84 + triangleCount * 50;
        if (bytes.Length < expectedLen)
            triangleCount = (bytes.Length - 84) / 50;

        int degenerate = 0;
        var edgeCounts = new Dictionary<Edge, int>(triangleCount * 3);

        for (int i = 0; i < triangleCount; i++)
        {
            int off = 84 + i * 50;
            // Skip normal (12 bytes)
            var v0 = Vec3.Read(bytes, off + 12);
            var v1 = Vec3.Read(bytes, off + 24);
            var v2 = Vec3.Read(bytes, off + 36);

            // Degenerate: zero cross-product area
            var cross = Vec3.Cross(v1 - v0, v2 - v0);
            if (cross.LengthSq() < 1e-18f)
            {
                degenerate++;
            }

            void CountEdge(Vec3 a, Vec3 b)
            {
                var e = new Edge(a, b);
                edgeCounts.TryGetValue(e, out int c);
                edgeCounts[e] = c + 1;
            }

            CountEdge(v0, v1);
            CountEdge(v1, v2);
            CountEdge(v2, v0);
        }

        int nonManifold = 0;
        foreach (var c in edgeCounts.Values)
            if (c != 2) nonManifold++;

        return new TopologyResult(
            TriangleCount:        triangleCount,
            DegenerateCount:      degenerate,
            UniqueEdgeCount:      edgeCounts.Count,
            NonManifoldEdgeCount: nonManifold,
            Watertight:           nonManifold == 0 && triangleCount > 0);
    }

    // ── CLI entry point ───────────────────────────────────────────────

    public static int Run(string[] args)
    {
        double voxel_mm  = 0.5;
        string? outPath  = null;
        string? stlDir   = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--voxel":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--voxel missing value"); return 3; }
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out voxel_mm)
                        || voxel_mm < 0.1 || voxel_mm > 2.0)
                    { Console.Error.WriteLine($"--voxel must be 0.1–2.0, got '{args[i]}'"); return 3; }
                    break;
                case "--out":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--out missing value"); return 3; }
                    outPath = args[++i];
                    break;
                case "--stl-dir":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--stl-dir missing value"); return 3; }
                    stlDir = args[++i];
                    break;
                case "-h":
                case "--help":
                    Console.WriteLine(UsageLine);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown arg '{args[i]}'");
                    Console.Error.WriteLine(UsageLine);
                    return 3;
            }
        }

        outPath ??= Path.Combine(AppContext.BaseDirectory, "baselines", "stl-validation",
            $"bench-stl-validation-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        stlDir ??= Path.Combine(Path.GetTempPath(), "stl-validation");
        Directory.CreateDirectory(stlDir);

        Console.WriteLine($"# bench-stl-validation voxel_mm={voxel_mm:F2}");
        Console.WriteLine($"# JSONL: {outPath}");
        Console.WriteLine($"# STL output dir: {stlDir}");

        int anyFail = 0;
        try
        {
            using var lib = new Library((float)voxel_mm);

            foreach (string presetName in CanonicalDesigns.AllNames)
            {
                Console.WriteLine();
                Console.WriteLine($"# ── {presetName} ──────────────────────────────────────");
                var result = ValidatePreset(presetName, voxel_mm, stlDir, outPath);
                if (result == "fail") anyFail = 1;
                Console.WriteLine($"# {presetName}: {result}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"BENCH_MEDIAN  bench=bench-stl-validation  presets={CanonicalDesigns.AllNames.Length}  any_fail={anyFail}");
        return anyFail;
    }

    private static string ValidatePreset(string presetName, double voxel_mm, string stlDir, string outPath)
    {
        var preset = CanonicalDesigns.Get(presetName);
        PropellantTables.UseEquilibrium = preset.Seed.UseEquilibriumRecommended;

        string stlPath = Path.Combine(stlDir, $"{presetName}.stl");

        var sw = Stopwatch.StartNew();
        long stlBytes = 0;
        TopologyResult topo;
        string status;

        try
        {
            var gen = RegenChamberOptimization.GenerateWith(
                preset.Seed.Conditions, preset.Seed.Design,
                voxelSize_mm:        voxel_mm,
                skipVoxelGeometry:   false,
                skipMfgAnalysis:     true);

            if (gen.Geometry.Voxels is null)
            {
                Console.WriteLine($"# {presetName}: no voxel geometry produced");
                EmitRecord(outPath, presetName, voxel_mm, 0, 0, 0, 0, false, "fail", 0, sw.ElapsedMilliseconds);
                return "fail";
            }

            var export = ChamberVoxelBuilder.ExportStlProfiled(gen.Geometry.Voxels.AsPicoGK(), stlPath);
            stlBytes = export.StlBytes;
            sw.Stop();

            topo   = ValidateBinaryStl(stlPath);
            status = topo.Status;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"# {presetName}: export threw {ex.GetType().Name}: {ex.Message}");
            EmitRecord(outPath, presetName, voxel_mm, 0, 0, 0, 0, false, "fail", 0, sw.ElapsedMilliseconds);
            return "fail";
        }

        Console.WriteLine($"# {presetName}: triangles={topo.TriangleCount} degenerate={topo.DegenerateCount} "
                        + $"non_manifold_edges={topo.NonManifoldEdgeCount} watertight={topo.Watertight} "
                        + $"status={status}");

        EmitRecord(outPath, presetName, voxel_mm,
            topo.TriangleCount, topo.DegenerateCount,
            topo.UniqueEdgeCount, topo.NonManifoldEdgeCount,
            topo.Watertight, status, stlBytes, sw.ElapsedMilliseconds);

        return status;
    }

    private static void EmitRecord(
        string outPath,
        string preset,
        double voxel_mm,
        int    triangleCount,
        int    degenerateCount,
        int    uniqueEdgeCount,
        int    nonManifoldEdgeCount,
        bool   watertight,
        string status,
        long   stlBytes,
        long   elapsedMs)
    {
        var sb = new StringBuilder(512);
        sb.Append('{');
        JsonlSchema.AppendProvenance(sb, "bench-stl-validation");
        sb.Append("\"preset\":\"").Append(preset).Append("\",");
        sb.Append("\"voxel_mm\":").Append(voxel_mm.ToString("R", CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"triangle_count\":").Append(triangleCount).Append(',');
        sb.Append("\"degenerate_count\":").Append(degenerateCount).Append(',');
        sb.Append("\"unique_edge_count\":").Append(uniqueEdgeCount).Append(',');
        sb.Append("\"non_manifold_edge_count\":").Append(nonManifoldEdgeCount).Append(',');
        sb.Append("\"watertight\":").Append(watertight ? "true" : "false").Append(',');
        sb.Append("\"status\":\"").Append(status).Append("\",");
        sb.Append("\"stl_bytes\":").Append(stlBytes).Append(',');
        sb.Append("\"export_ms\":").Append(elapsedMs);
        JsonlSchema.AppendRecord(outPath, sb);
    }
}
