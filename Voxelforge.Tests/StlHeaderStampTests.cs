// OOB-15 follow-up (#257): round-trip tests for StampStlHeader / ReadStlHeaderStamp.
// All tests operate on synthetic binary STL files (no PicoGK dependency).

using System;
using System.IO;
using System.Text;
using Voxelforge.IO;

namespace Voxelforge.Tests;

public class StlHeaderStampTests
{
    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a minimal valid binary STL: 80-byte zero header + uint32 count + 4 triangles.
    /// </summary>
    private static string WriteSyntheticStl()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vxf-test-{Guid.NewGuid():N}.stl");
        using var bw = new BinaryWriter(File.Create(path));
        bw.Write(new byte[80]);  // blank header
        bw.Write((uint)4);       // four triangles
        for (int i = 0; i < 4; i++)
        {
            // normal + 3 vertices + attribute byte count (50 bytes per tri)
            for (int f = 0; f < 12; f++) bw.Write(0f);
            bw.Write((ushort)0);
        }
        return path;
    }

    // ── StampStlHeader ──────────────────────────────────────────────────────

    [Fact]
    public void StampStlHeader_WritesVxfMagicPrefix()
    {
        string path = WriteSyntheticStl();
        try
        {
            ExportMetadata.StampStlHeader(path);

            string? stamp = ExportMetadata.ReadStlHeaderStamp(path);
            Assert.NotNull(stamp);
            Assert.StartsWith("vxf:", stamp, StringComparison.Ordinal);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void StampStlHeader_HeaderIsExactly80Bytes()
    {
        string path = WriteSyntheticStl();
        try
        {
            ExportMetadata.StampStlHeader(path);

            // File length must be unchanged (we only overwrite, never extend).
            long expected = 80 + 4 + 4 * 50;
            Assert.Equal(expected, new FileInfo(path).Length);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void StampStlHeader_ContainsGitShaField()
    {
        string path = WriteSyntheticStl();
        try
        {
            ExportMetadata.StampStlHeader(path);

            string? stamp = ExportMetadata.ReadStlHeaderStamp(path);
            Assert.NotNull(stamp);
            // SHA segment: 40 hex chars or the sentinel "unknown"
            string shaSegment = stamp!.Substring(4); // strip "vxf:"
            shaSegment = shaSegment.Split('|')[0];
            Assert.Matches("^([0-9a-f]{40}|unknown)$", shaSegment);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void StampStlHeader_ContainsSchemaVersionField()
    {
        string path = WriteSyntheticStl();
        try
        {
            ExportMetadata.StampStlHeader(path);

            string? stamp = ExportMetadata.ReadStlHeaderStamp(path);
            Assert.NotNull(stamp);
            // Schema segment looks like "v19" — "v" + decimal digits.
            string[] parts = stamp!.Split('|');
            Assert.True(parts.Length >= 2, $"Expected at least 2 pipe-delimited segments, got: {stamp}");
            // SchemaVersion is already "v19"-style (includes the "v" prefix).
            Assert.Matches(@"^v\d+$", parts[1]);
            Assert.Equal(ExportMetadata.SchemaVersion, parts[1]);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void StampStlHeader_WithDesignHash_EmbeddesHashInThirdSegment()
    {
        string path = WriteSyntheticStl();
        const string fakeHash = "abcd1234abcd1234";
        try
        {
            ExportMetadata.StampStlHeader(path, designHash: fakeHash);

            string? stamp = ExportMetadata.ReadStlHeaderStamp(path);
            Assert.NotNull(stamp);
            string[] parts = stamp!.Split('|');
            Assert.True(parts.Length >= 3, $"Expected 3 pipe-delimited segments with designHash, got: {stamp}");
            Assert.Equal(fakeHash, parts[2]);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void StampStlHeader_WithoutDesignHash_OnlyTwoSegments()
    {
        string path = WriteSyntheticStl();
        try
        {
            ExportMetadata.StampStlHeader(path, designHash: null);

            string? stamp = ExportMetadata.ReadStlHeaderStamp(path);
            Assert.NotNull(stamp);
            string[] parts = stamp!.Split('|');
            Assert.Equal(2, parts.Length);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void StampStlHeader_DoesNotThrowOnMissingFile()
    {
        // Must not throw — non-fatal contract.
        var ex = Record.Exception(() =>
            ExportMetadata.StampStlHeader(Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.stl")));
        Assert.Null(ex);
    }

    [Fact]
    public void StampStlHeader_DoesNotModifyFileShorterThan84Bytes()
    {
        // File with only 83 bytes (< minimum) must not be touched.
        string path = Path.Combine(Path.GetTempPath(), $"vxf-short-{Guid.NewGuid():N}.stl");
        try
        {
            File.WriteAllBytes(path, new byte[83]);
            ExportMetadata.StampStlHeader(path);

            // ReadStlHeaderStamp should return null since the stamp was not written.
            // (The file has no "vxf:" prefix in its first bytes.)
            string? stamp = ExportMetadata.ReadStlHeaderStamp(path);
            Assert.Null(stamp);
        }
        finally { TryDelete(path); }
    }

    // ── ReadStlHeaderStamp ──────────────────────────────────────────────────

    [Fact]
    public void ReadStlHeaderStamp_ReturnsNullForUnstampedStl()
    {
        string path = WriteSyntheticStl();
        try
        {
            // Synthetic STL has a zero header — no "vxf:" magic.
            string? stamp = ExportMetadata.ReadStlHeaderStamp(path);
            Assert.Null(stamp);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void ReadStlHeaderStamp_ReturnsNullForMissingFile()
    {
        string stamp = ExportMetadata.ReadStlHeaderStamp(
            Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.stl"))!;
        Assert.Null(stamp);
    }

    [Fact]
    public void StampThenRead_RoundTrip_IsConsistent()
    {
        string path = WriteSyntheticStl();
        const string hash = "deadbeefdeadbeef";
        try
        {
            ExportMetadata.StampStlHeader(path, designHash: hash);
            string? stamp = ExportMetadata.ReadStlHeaderStamp(path);

            Assert.NotNull(stamp);
            Assert.StartsWith("vxf:", stamp, StringComparison.Ordinal);
            Assert.Contains($"|{ExportMetadata.SchemaVersion}|{hash}", stamp,
                StringComparison.Ordinal);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void StampStlHeader_StampFitsIn80Bytes_NulPaddingPreservesTriangleCount()
    {
        // Write a known triangle count (7 triangles), stamp the header, then
        // re-read the triangle count field (bytes 80-83) to verify it is intact.
        string path = Path.Combine(Path.GetTempPath(), $"vxf-tc-{Guid.NewGuid():N}.stl");
        try
        {
            using (var bw = new BinaryWriter(File.Create(path)))
            {
                bw.Write(new byte[80]);
                bw.Write((uint)7);
                for (int i = 0; i < 7; i++)
                {
                    for (int f = 0; f < 12; f++) bw.Write(0f);
                    bw.Write((ushort)0);
                }
            }

            ExportMetadata.StampStlHeader(path, designHash: "1122334411223344");

            using var br = new BinaryReader(File.OpenRead(path));
            byte[] header = br.ReadBytes(80);
            uint triCount = br.ReadUInt32();

            Assert.Equal(7u, triCount);
            Assert.StartsWith("vxf:", Encoding.ASCII.GetString(header).TrimEnd('\0'),
                StringComparison.Ordinal);
        }
        finally { TryDelete(path); }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
}
