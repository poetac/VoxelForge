// Su2SurfaceParserTests.cs — Unit tests for Su2SurfaceParser.

using System;
using System.IO;
using Voxelforge.Cfd.Parser;
using Voxelforge.Chamber;
using Xunit;

namespace Voxelforge.Cfd.Tests.Parser;

public sealed class Su2SurfaceParserTests : IDisposable
{
    // Compact nozzle: small enough to run fast, large enough that x=10..50 mm
    // all fall inside the diverging section and map to distinct station indices.
    private static readonly ChamberContour Contour = ChamberContourGenerator.Generate(
        throatRadius_mm:       20,
        contractionRatio:      4,
        expansionRatio:        8,
        characteristicLength_m: 1.0,
        stationCount:          100);

    // Five wall rows (y>0) + one axis row (y=0 → must be filtered).
    // x in metres; parser converts to mm for StationAt().
    private const string WallCsv =
        "\"x-coordinate\",\"y-coordinate\",\"Temperature\"\n" +
        "0.010,0.040,3200.0\n" +
        "0.020,0.038,3250.0\n" +
        "0.030,0.020,3400.0\n" +
        "0.040,0.025,3350.0\n" +
        "0.050,0.030,3300.0\n" +
        "0.010,0.000,2900.0\n";   // axis node — should be filtered

    private readonly string _tempDir;
    private readonly Su2WallProfile _profile;

    public Su2SurfaceParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "surface_flow_0.csv"), WallCsv);
        _profile = Su2SurfaceParser.Parse(_tempDir, Contour, converged: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Parse_PeakTempIsMaxNonAxisNode()
        => Assert.Equal(3400.0, _profile.PeakAdiabaticWallTemp_K);

    [Fact]
    public void Parse_AxisNodeFiltered()
        => Assert.True(_profile.PeakAdiabaticWallTemp_K > 2900.0,
            "Axis node (T=2900 K) should have been filtered; peak must exceed it.");

    [Fact]
    public void Parse_StationMapIsPopulated()
        => Assert.True(_profile.AdiabaticWallTempByStation.Count >= 4,
            $"Expected ≥4 distinct stations, got {_profile.AdiabaticWallTempByStation.Count}.");

    [Fact]
    public void Parse_NodeCountExcludesAxisNodes()
        => Assert.Equal(5, _profile.NodeCount);

    [Fact]
    public void Parse_FallsBackToSurfaceFlowCsv()
    {
        using var dir = new TempDir();
        const string fallback =
            "x-coordinate,y-coordinate,Temperature\n" +
            "0.010,0.040,3100.0\n" +
            "0.020,0.038,3050.0\n";
        File.WriteAllText(Path.Combine(dir.Path, "surface_flow.csv"), fallback);
        var profile = Su2SurfaceParser.Parse(dir.Path, Contour, converged: true);
        Assert.Equal(3100.0, profile.PeakAdiabaticWallTemp_K);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
