// OOB-15 round-trip tests for the 3MF metadata block. Writes a 3MF
// from a canonical Merlin GenerationResult against a synthetic 4-tri
// STL, opens the .3mf as ZIP, parses the embedded 3D/3dmodel.model
// XML, and asserts every OOB-15 field is present and well-formed.

using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Geometry;
using Voxelforge.IO;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class ThreeMfExportMetadataRoundTripTests
{
    [Fact]
    public void SaveFromStl_EmbedsOob15ProvenanceMetadata()
    {
        // Arrange — synthetic 4-triangle binary STL + canonical Merlin
        // GenerationResult (skipVoxelGeometry/skipMfgAnalysis to keep the
        // test under the xUnit + PicoGK incompatibility per ADR-005).
        string tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-oob15-roundtrip");
        Directory.CreateDirectory(tempDir);
        string stlPath = Path.Combine(tempDir, $"input_{System.Guid.NewGuid():N}.stl");
        string threeMfPath = Path.Combine(tempDir, $"output_{System.Guid.NewGuid():N}.3mf");

        try
        {
            WriteSyntheticTetrahedronStl(stlPath);

            var spec = new EngineSpec(
                PropellantPair: PropellantPair.LOX_CH4,
                Thrust_N: 15_000.0,
                ChamberPressure_Pa: 4e6,
                ExpansionRatio: 16.0,
                EngineCycleOverride: EngineCycle.GasGenerator);
            var seed = AutoSeeder.Seed(spec);
            var cond = new OperatingConditions
            {
                PropellantPair = spec.PropellantPair,
                Thrust_N = spec.Thrust_N,
                ChamberPressure_Pa = spec.ChamberPressure_Pa,
            };
            var r = RegenChamberOptimization.GenerateWith(
                cond, seed.Design,
                skipVoxelGeometry: true, skipMfgAnalysis: true);

            // Act
            ThreeMFExport.SaveFromStl(stlPath, threeMfPath, r);

            // Assert — open .3mf as ZIP, parse 3dmodel.model XML, scan
            // for the OOB-15 metadata fields.
            using var zip = ZipFile.OpenRead(threeMfPath);
            var modelEntry = zip.GetEntry("3D/3dmodel.model");
            Assert.NotNull(modelEntry);
            using var reader = new StreamReader(modelEntry!.Open());
            string xml = reader.ReadToEnd();

            string gitSha = ExtractMetadata(xml, "GitSha");
            Assert.Matches("^([0-9a-f]{40}|unknown)$", gitSha);

            string schemaVersion = ExtractMetadata(xml, "SchemaVersion");
            Assert.Equal(DesignPersistence.CurrentSchemaVersion, schemaVersion);

            string gateManifest = ExtractMetadata(xml, "GatePassManifest");
            Assert.True(gateManifest == "PASS" || gateManifest.StartsWith("FAIL: ", System.StringComparison.Ordinal),
                $"GatePassManifest must be 'PASS' or 'FAIL: ...' — got '{gateManifest}'");

            // Sanity — DesignHash is unchanged by OOB-15 (it was already there).
            string designHash = ExtractMetadata(xml, "DesignHash");
            Assert.Matches("^[0-9a-f]{16}$", designHash);
        }
        finally
        {
            try { File.Delete(stlPath); } catch { }
            try { File.Delete(threeMfPath); } catch { }
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void GatePassManifest_FeasibleGenerationResult_RendersPass()
    {
        var feasible = new FeasibilityGateResult(IsFeasible: true,
            Violations: System.Array.Empty<FeasibilityViolation>());
        Assert.Equal("PASS", ExportMetadata.GatePassManifest(feasible));
    }

    [Fact]
    public void GatePassManifest_InfeasibleGenerationResult_RendersFailWithIds()
    {
        var infeasible = new FeasibilityGateResult(IsFeasible: false,
            Violations: new[]
            {
                new FeasibilityViolation("WALL_TEMP", "wall too hot", 1500.0, 1100.0),
                new FeasibilityViolation("YIELD_EXCEEDED", "below SF=1", 0.5, 1.0),
            });
        Assert.Equal("FAIL: WALL_TEMP,YIELD_EXCEEDED",
            ExportMetadata.GatePassManifest(infeasible));
    }

    [Fact]
    public void SchemaVersion_MatchesDesignPersistenceConstant()
    {
        Assert.Equal(DesignPersistence.CurrentSchemaVersion, ExportMetadata.SchemaVersion);
    }

    [Fact]
    public void GitSha_ReturnsKnownShaOrSentinel()
    {
        // In a git checkout we expect a 40-hex SHA; otherwise the
        // sentinel "unknown". Both are valid per the OOB-15 contract.
        string sha = ExportMetadata.GitSha();
        Assert.Matches("^([0-9a-f]{40}|unknown)$", sha);
    }

    // Synthetic 4-triangle binary STL — small enough to fit in tests'
    // tempdir budget. Format: 80-byte header + uint32 triangle count +
    // 50 bytes/triangle (12 normal + 36 vertices + 2 attribute).
    private static void WriteSyntheticTetrahedronStl(string path)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(new byte[80]);
        bw.Write((uint)4);
        var A = (X: 0f,  Y: 0f,  Z: 0f);
        var B = (X: 10f, Y: 0f,  Z: 0f);
        var C = (X: 5f,  Y: 10f, Z: 0f);
        var D = (X: 5f,  Y: 5f,  Z: 10f);
        var triangles = new[]
        {
            (A, B, C),
            (A, B, D),
            (B, C, D),
            (C, A, D),
        };
        foreach (var (v0, v1, v2) in triangles)
        {
            bw.Write(0f); bw.Write(0f); bw.Write(0f);
            bw.Write(v0.X); bw.Write(v0.Y); bw.Write(v0.Z);
            bw.Write(v1.X); bw.Write(v1.Y); bw.Write(v1.Z);
            bw.Write(v2.X); bw.Write(v2.Y); bw.Write(v2.Z);
            bw.Write((ushort)0);
        }
    }

    private static string ExtractMetadata(string xml, string name)
    {
        var match = Regex.Match(xml,
            $"<metadata name=\"{Regex.Escape(name)}\">([^<]*)</metadata>");
        Assert.True(match.Success,
            $"3MF model XML missing '<metadata name=\"{name}\">…</metadata>' element. XML excerpt:\n{xml.Substring(0, System.Math.Min(2000, xml.Length))}");
        return match.Groups[1].Value;
    }
}
