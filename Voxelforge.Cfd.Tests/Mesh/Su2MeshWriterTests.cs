// Su2MeshWriterTests.cs — Unit tests for Su2MeshWriter (Coarse mesh, 50×20 cells).

using System;
using System.IO;
using System.Linq;
using Voxelforge.Cfd.Mesh;
using Voxelforge.Chamber;
using Xunit;

namespace Voxelforge.Cfd.Tests.Mesh;

public sealed class Su2MeshWriterTests : IDisposable
{
    private readonly string _tempFile;
    private readonly string _content;
    private readonly string[] _lines;

    public Su2MeshWriterTests()
    {
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm:      20,
            contractionRatio:     4,
            expansionRatio:       8,
            characteristicLength_m: 1.0,
            stationCount:         100);

        _tempFile = Path.GetTempFileName();
        Su2MeshWriter.Write(_tempFile, contour, Su2MeshDensity.Coarse);
        _content = File.ReadAllText(_tempFile);
        _lines   = File.ReadAllLines(_tempFile);
    }

    public void Dispose() => File.Delete(_tempFile);

    [Fact]
    public void Write_Ndime2()
        => Assert.Contains("NDIME= 2", _content);

    [Fact]
    public void Write_Coarse_HasCorrectNodeCount()
        // (Nx+1)*(Nr+1) = 51*21 = 1071
        => Assert.Contains("NPOIN= 1071", _content);

    [Fact]
    public void Write_Coarse_HasCorrectQuadCount()
        // Nx*Nr = 50*20 = 1000
        => Assert.Contains("NELEM= 1000", _content);

    [Fact]
    public void Write_HasFourMarkers()
    {
        Assert.Contains("NMARK= 4", _content);
        Assert.Contains("MARKER_TAG= inlet",  _content);
        Assert.Contains("MARKER_TAG= outlet", _content);
        Assert.Contains("MARKER_TAG= wall",   _content);
        Assert.Contains("MARKER_TAG= axis",   _content);
    }

    [Fact]
    public void Write_InletHasNrSegments()
    {
        for (int i = 0; i < _lines.Length - 1; i++)
        {
            if (_lines[i].Trim() == "MARKER_TAG= inlet")
            {
                Assert.Equal("MARKER_ELEMS= 20", _lines[i + 1].Trim());
                return;
            }
        }
        Assert.Fail("MARKER_TAG= inlet not found in mesh file.");
    }

    [Fact]
    public void Write_AllQuadsAreType9()
    {
        int count = _lines.Count(l => l.StartsWith("9\t", StringComparison.Ordinal));
        Assert.Equal(1000, count);
    }
}
